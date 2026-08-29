using CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Gain;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Services;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.SallenKey;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Bainter;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;

public sealed class FilterDesignEngine : IFilterDesignEngine
{
    private readonly FilterSpecificationNormalizer normalizer;
    private readonly IReadOnlyDictionary<FilterApproximation, IFilterPrototypeDesigner> prototypeDesigners;
    private readonly AnalogFrequencyTransformationService transformation = new();
    private readonly FilterSectionBuilder sectionBuilder = new();
    private readonly GainDistributionEngine gainDistributionEngine = new();
    private readonly SallenKeyLowPassSectionSolver sallenKeyLowPassSolver = new();
    private readonly SallenKeyHighPassSectionSolver sallenKeyHighPassSolver = new();
    private readonly SallenKeyBandPassSectionSolver sallenKeyBandPassSolver = new();
    private readonly MfbLowPassSectionCalculator mfbLowPassCalculator = new();
    private readonly MfbHighPassSectionCalculator mfbHighPassCalculator = new();
    private readonly MfbBandPassSectionCalculator mfbBandPassCalculator = new();
    private readonly PassiveRcSectionCalculator passiveRcCalculator = new();
    private readonly BainterNotchSectionSolver bainterNotchSolver = new();
    private readonly BainterNotchCascadeOptimizer bainterNotchCascadeOptimizer = new();
    private readonly SallenKeyBandPassCascadeOptimizer sallenKeyBandPassCascadeOptimizer = new();
    private readonly FilterCircuitAssembler circuitAssembler = new();
    private readonly FilterSchematicLayoutService layoutService = new();
    private readonly FilterAnalysisService analysisService = new();
    private readonly FilterSpecificationComplianceEvaluator complianceEvaluator = new();
    private readonly FilterDesignVerifier designVerifier = new();
    private readonly FilterSpiceExportService spiceExportService = new();

    public FilterDesignEngine()
    {
        var tabulated = new[]
        {
            FilterApproximation.GaussianTo6Db,
            FilterApproximation.GaussianTo12Db,
            FilterApproximation.LinearPhase005Degree,
            FilterApproximation.LinearPhase05Degree
        };
        normalizer = new FilterSpecificationNormalizer(
        [
            new ButterworthOrderCalculator(),
            new ChebyshevOrderCalculator(),
            new BesselOrderCalculator(),
            .. tabulated.Select(value => new TabulatedPrototypeOrderCalculator(value))
        ]);
        IFilterPrototypeDesigner[] designers =
        [
            new ButterworthPrototypeDesigner(),
            new ChebyshevPrototypeDesigner(),
            new BesselPrototypeDesigner(),
            .. tabulated.Select(value => new TabulatedPrototypeDesigner(value))
        ];
        prototypeDesigners = designers.ToDictionary(item => item.Approximation);
    }

    public FilterDesignAvailability GetAvailability(FilterDesignRequest request)
    {
        var support = FilterSupportMatrix.Evaluate(request);
        if (!support.IsSupported) return FilterDesignAvailability.Unavailable(support.Message);
        try
        {
            ValidateRequest(request);
            return FilterDesignAvailability.Available("Supported. Final feasibility is checked when Design Filter runs.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return FilterDesignAvailability.Unavailable(exception.Message);
        }
    }

    public FilterDesignResult Design(FilterDesignRequest request)
    {
        if (!TryPrepare(request, out var prepared, out var availability))
        {
            var capability = FilterSupportMatrix.Evaluate(request);
            return new FilterDesignResult
            {
                Status = FilterDesignStatus.UnsupportedDesignCombination,
                Message = availability.Message,
                Capability = capability.IsSupported
                    ? availability.Message.Contains("BainterNotchDepthInsufficientWithSelectedSeries", StringComparison.Ordinal)
                        ? FilterSupportDecision.Unsupported(FilterSupportReasonCode.BainterNotchDepthInsufficientWithSelectedSeries, availability.Message)
                        : availability.Message.Contains("LcPrototypeNotPositiveReal", StringComparison.Ordinal)
                            ? FilterSupportDecision.Unsupported(FilterSupportReasonCode.LcPrototypeNotPositiveReal, availability.Message)
                            : availability.Message.Contains("Bessel Passive LC v1", StringComparison.Ordinal)
                                ? FilterSupportDecision.Unsupported(FilterSupportReasonCode.OrderLimited, availability.Message)
                        : new FilterCapabilityEvaluation(FilterCapabilityLevel.SectionFeasibility, false,
                            FilterSupportReasonCode.PendingIndependentVerification, availability.Message,
                            "Adjust order, component ranges, tolerances, or standard-value series and redesign.")
                    : capability
            };
        }

        try
        {
            var circuitBuild = circuitAssembler.Build(
                request,
                prepared.Specification,
                prepared.Prototype,
                prepared.SynthesizedSections,
                prepared.GainPlan);
            FilterComponentRangeValidator.EnsureWithinConstraints(circuitBuild.CircuitGraph, request.ComponentConstraints);
            var schematic = layoutService.Layout(circuitBuild);
            var analysis = analysisService.Analyze(circuitBuild.CircuitGraph, request);
            var compliance = complianceEvaluator.Evaluate(request, analysis);
            var gainSummary = gainDistributionEngine.Summarize(
                prepared.GainPlan,
                prepared.SynthesizedSections,
                GetPassbandPeakGain(request, analysis),
                GetReferencePhase(request, analysis));
            var verification = designVerifier.Verify(request, prepared.SynthesizedSections, circuitBuild,
                schematic, compliance, gainSummary);
            if (!verification.IsValid)
                throw new InvalidOperationException("Final design verification failed: " + string.Join(" ", verification.Errors));
            var warnings = (prepared.Specification.OrderWarning == null
                    ? Array.Empty<string>()
                    : new[] { prepared.Specification.OrderWarning })
                .Concat(compliance.Warnings)
                .Concat(verification.Warnings)
                .ToArray();
            var meetsSpecifications = prepared.Specification.MeetsOrderRequirement && compliance.MeetsAllEdges;
            var capability = meetsSpecifications
                ? new FilterCapabilityEvaluation(FilterCapabilityLevel.FinalCircuitVerification, true,
                    FilterSupportReasonCode.Supported, "All synthesis and final CircuitGraph verification gates passed.", string.Empty)
                : new FilterCapabilityEvaluation(FilterCapabilityLevel.FinalCircuitVerification, false,
                    FilterSupportReasonCode.FinalCircuitSpecificationNotMet,
                    $"The circuit was generated and verified, but implemented order {prepared.Specification.ImplementedOrder} " +
                    $"does not satisfy all entered edge specifications; calculated minimum order is {prepared.Specification.RequiredOrder}.",
                    $"Use automatic order or specify order {prepared.Specification.RequiredOrder} or higher, then re-run selected-value verification.");
            var result = new FilterDesignResult
            {
                Status = FilterDesignStatus.Success,
                Message = meetsSpecifications
                    ? "Filter circuit generated successfully."
                    : "Filter circuit generated, but it does not meet all entered edge specifications.",
                Capability = capability,
                RequiredOrder = prepared.Specification.RequiredOrder,
                PrototypeOrder = prepared.Specification.ImplementedOrder,
                RealizedOrder = prepared.Transformed.RealizedOrder,
                MeetsRequestedOrder = prepared.Specification.MeetsOrderRequirement,
                MeetsSpecifications = meetsSpecifications,
                NormalizedSpecification = prepared.Specification,
                PrototypePoles = prepared.Prototype.Poles,
                PrototypeZeros = prepared.Prototype.Zeros,
                PrototypeGain = prepared.Prototype.NumeratorGain,
                PrototypeNormalization = prepared.Prototype.Normalization,
                ApproximationDescription = prepared.Prototype.Description,
                ApproximationParameters = prepared.Prototype.Parameters ?? new Dictionary<string, double>(),
                PrototypeElementValues = prepared.Prototype.LadderGValues,
                LcLadderPrototype = prepared.Prototype.LcLadderPrototype,
                Sections = prepared.SynthesizedSections,
                AdditionalNetworkComponents = circuitBuild.NetworkComponentValues ?? Array.Empty<SynthesizedComponentValue>(),
                Warnings = warnings,
                CircuitGraph = circuitBuild.CircuitGraph,
                SchematicDocument = schematic,
                DesignRequest = request,
                AnalysisResult = analysis,
                SpecificationCompliance = compliance,
                GainDistribution = gainSummary,
                Verification = verification,
                CalculationTrace = BuildCalculationTrace(request, prepared, circuitBuild, compliance, gainSummary)
            };
            result.SpiceNetlist = spiceExportService.Export(result);
            return result;
        }
        catch (NotSupportedException exception)
        {
            return new FilterDesignResult
            {
                Status = FilterDesignStatus.UnsupportedDesignCombination,
                Message = exception.Message,
                Capability = new FilterCapabilityEvaluation(FilterCapabilityLevel.SectionFeasibility, false,
                    FilterSupportReasonCode.ResponseUnavailable, exception.Message, "Choose a capability-supported topology/order combination.")
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new FilterDesignResult
            {
                Status = FilterDesignStatus.SynthesisFailure,
                Message = exception.Message,
                Capability = new FilterCapabilityEvaluation(FilterCapabilityLevel.ComponentRangeFeasibility, false,
                    FilterSupportReasonCode.PendingIndependentVerification, exception.Message,
                    "Adjust component ranges or standard-value series and redesign.")
            };
        }
    }

    private bool TryPrepare(
        FilterDesignRequest request,
        out PreparedDesign prepared,
        out FilterDesignAvailability availability)
    {
        try
        {
            var support = FilterSupportMatrix.Evaluate(request);
            if (!support.IsSupported)
            {
                prepared = null!;
                availability = FilterDesignAvailability.Unavailable(support.Message);
                return false;
            }

            ValidateRequest(request);
            var specification = normalizer.Normalize(request);
            var prototype = prototypeDesigners[request.Approximation]
                .Design(specification.ImplementedOrder, request.PassbandRippleDb);
            ValidateImplementation(request, specification, prototype);
            var transformed = Transform(request, specification, prototype);
            var gainPlan = gainDistributionEngine.Plan(sectionBuilder.Build(transformed), request);
            var targetSections = gainPlan.Sections;
            var synthesizedSections = request.Implementation == FilterImplementationType.Lc
                ? targetSections.Select(section => new SynthesizedSection(
                    section,
                    Array.Empty<SynthesizedComponentValue>(),
                    section.PoleFrequencyHz,
                    section.Q,
                    section.SectionGain,
                    0,
                    0,
                    0)).ToArray()
                : request.Implementation == FilterImplementationType.BainterNotch
                    ? bainterNotchCascadeOptimizer.Optimize(targetSections, request, prototype, gainPlan).ToArray()
                    : targetSections.Select(section => ApplyTopologyPolarity(CalculateComponents(section, request), request)).ToArray();
            synthesizedSections = sallenKeyBandPassCascadeOptimizer.Optimize(request, prototype, gainPlan, synthesizedSections).ToArray();

            prepared = new PreparedDesign(specification, prototype, transformed, gainPlan, synthesizedSections);
            availability = FilterDesignAvailability.Available(support.Message);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            prepared = null!;
            availability = FilterDesignAvailability.Unavailable(exception.Message);
            return false;
        }
    }

    private TransformedFilterPoles Transform(
        FilterDesignRequest request,
        NormalizedFilterSpecification specification,
        FilterPrototype prototype) => request.ResponseType switch
    {
        FilterResponseType.LowPass or FilterResponseType.HighPass =>
            transformation.TransformLowPassOrHighPass(
                prototype,
                request.ResponseType,
                specification.CutoffAngularFrequency),
        FilterResponseType.BandPass => transformation.TransformBandPass(
            prototype,
            specification.CenterAngularFrequency,
            specification.BandwidthAngularFrequency),
        FilterResponseType.BandStop => transformation.TransformBandStop(
            prototype,
            specification.CenterAngularFrequency,
            specification.BandwidthAngularFrequency),
        _ => throw new NotSupportedException($"No analog transformation exists for {request.ResponseType}.")
    };

    private static void ValidateRequest(FilterDesignRequest request)
    {
        if (request.PassbandRippleDb <= 0 || request.StopbandAttenuationDb <= request.PassbandRippleDb)
            throw new ArgumentException("Enter Rp > 0 and As > Rp.");
        if (request.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass)
        {
            if (request.PassbandEdgeHz <= 0 || request.StopbandEdgeHz <= 0)
                throw new ArgumentException("Passband and stopband frequencies must be positive.");
            if (request.ResponseType == FilterResponseType.LowPass && request.StopbandEdgeHz <= request.PassbandEdgeHz)
                throw new ArgumentException("Low-Pass requires fs > fp.");
            if (request.ResponseType == FilterResponseType.HighPass && request.PassbandEdgeHz <= request.StopbandEdgeHz)
                throw new ArgumentException("High-Pass requires fp > fs.");
        }
        if (request.Mode == FilterMode.Active &&
            (request.PassbandGain <= 0 || !double.IsFinite(request.PassbandGain)))
            throw new ArgumentException("Passband gain A0 must be positive and finite.");
    }

    private static void ValidateImplementation(
        FilterDesignRequest request,
        NormalizedFilterSpecification specification,
        FilterPrototype prototype)
    {
        if (request.Mode == FilterMode.Passive &&
            request.Implementation == FilterImplementationType.Rc &&
            specification.ImplementedOrder != 1)
            throw new NotSupportedException(
                $"Passive RC only supports order 1; the current implemented order is {specification.ImplementedOrder}.");
        if (request.Mode != FilterMode.Passive || request.Implementation != FilterImplementationType.Lc)
            return;

        if (request.LcTerminationMode != LcTerminationMode.DoublyTerminated || request.SourceResistanceOhm <= 0)
            throw new NotSupportedException("LcTerminationModeMismatch: verified LC ladder synthesis requires DoublyTerminated mode with Rs > 0.");
        if (prototype.LcLadderPrototype == null)
        {
            if (request.Approximation == FilterApproximation.Bessel && prototype.Order > 6)
                throw new NotSupportedException("Bessel Passive LC v1 is numerically qualified for prototype orders 1 through 6.");
            throw new NotSupportedException($"LcPrototypeNotPositiveReal: no verified positive-real LC ladder is registered for {request.Approximation} order {prototype.Order}.");
        }

        var impedanceScale = request.SourceResistanceOhm;
        var requiredLoad = impedanceScale * prototype.LadderGValues[^1];
        if (Math.Abs(request.LoadResistanceOhm - requiredLoad) / requiredLoad > 1e-4)
            throw new NotSupportedException(
                $"This normalized {request.Approximation} order-{specification.ImplementedOrder} ladder requires " +
                $"RL = {requiredLoad:0.###} ohm for the {impedanceScale:0.###} ohm LC impedance scale.");
    }

    private SynthesizedSection CalculateComponents(AnalogFilterSection section, FilterDesignRequest request) =>
        request.Implementation switch
        {
            FilterImplementationType.MultipleFeedback when section.Order == SectionOrder.Second && section.ResponseType == FilterResponseType.LowPass =>
                mfbLowPassCalculator.Calculate(section, request),
            FilterImplementationType.MultipleFeedback when section.Order == SectionOrder.Second && section.ResponseType == FilterResponseType.HighPass =>
                mfbHighPassCalculator.Calculate(section, request),
            FilterImplementationType.MultipleFeedback when section.Order == SectionOrder.Second && section.ResponseType == FilterResponseType.BandPass =>
                mfbBandPassCalculator.Calculate(section, request),
            FilterImplementationType.MultipleFeedback when section.ResponseType == FilterResponseType.LowPass => sallenKeyLowPassSolver.Solve(section, request),
            FilterImplementationType.MultipleFeedback when section.ResponseType == FilterResponseType.HighPass => sallenKeyHighPassSolver.Solve(section, request),
            FilterImplementationType.SallenKey when section.ResponseType == FilterResponseType.LowPass => sallenKeyLowPassSolver.Solve(section, request),
            FilterImplementationType.SallenKey when section.ResponseType == FilterResponseType.HighPass => sallenKeyHighPassSolver.Solve(section, request),
            FilterImplementationType.SallenKey when section.ResponseType == FilterResponseType.BandPass => sallenKeyBandPassSolver.Solve(section, request),
            FilterImplementationType.BainterNotch when section.ResponseType == FilterResponseType.BandStop => bainterNotchSolver.Solve(section, request),
            FilterImplementationType.Rc => passiveRcCalculator.Calculate(section, request),
            _ => throw new NotSupportedException($"No verified section solver exists for {request.Implementation}/{section.ResponseType}.")
        };

    private static SynthesizedSection ApplyTopologyPolarity(SynthesizedSection section, FilterDesignRequest request) =>
        section with
        {
            PolaritySign = request.Implementation == FilterImplementationType.MultipleFeedback &&
                           section.Target.Order == SectionOrder.Second ? -1 : 1
        };

    private static double GetPassbandPeakGain(FilterDesignRequest request, Core.Analysis.AcAnalysisResult analysis)
    {
        var points = analysis.Points.Where(point => request.ResponseType switch
        {
            FilterResponseType.LowPass => point.FrequencyHz <= request.PassbandEdgeHz,
            FilterResponseType.HighPass => point.FrequencyHz >= request.PassbandEdgeHz,
            FilterResponseType.BandPass => point.FrequencyHz >= request.PassbandLowerHz && point.FrequencyHz <= request.PassbandUpperHz,
            FilterResponseType.BandStop => point.FrequencyHz <= request.PassbandLowerHz || point.FrequencyHz >= request.PassbandUpperHz,
            _ => false
        }).ToArray();
        return points.Length == 0 ? 0 : points.Max(point => point.MagnitudeLinear);
    }

    private static double GetReferencePhase(FilterDesignRequest request, Core.Analysis.AcAnalysisResult analysis)
    {
        var frequency = request.ResponseType switch
        {
            FilterResponseType.LowPass => analysis.Points.Where(point => point.FrequencyHz <= request.PassbandEdgeHz).Min(point => point.FrequencyHz),
            FilterResponseType.HighPass => analysis.Points.Where(point => point.FrequencyHz >= request.PassbandEdgeHz).Max(point => point.FrequencyHz),
            _ => Math.Sqrt(request.PassbandLowerHz * request.PassbandUpperHz)
        };
        return analysis.Points.MinBy(point => Math.Abs(Math.Log(point.FrequencyHz / frequency)))!.PhaseDegrees;
    }

    private static IReadOnlyList<DesignStepRecord> BuildCalculationTrace(
        FilterDesignRequest request,
        PreparedDesign prepared,
        FilterCircuitBuildResult circuit,
        FilterSpecificationCompliance compliance,
        GainDistributionSummary gain)
    {
        var records = new List<DesignStepRecord>();
        void Add(string category, string title, string explanation, params (string Key, object Value)[] values) =>
            records.Add(new DesignStepRecord(records.Count + 1, category, title, explanation,
                values.ToDictionary(item => item.Key, item => Convert.ToString(item.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)));
        Add("Order", "Normalize specifications",
            $"{request.Approximation} order determination evaluated Rp, As and normalized stopband ratio.",
            ("NormalizedStopbandRatio", prepared.Specification.StopbandRatio),
            ("RequiredPrototypeOrder", prepared.Specification.RequiredOrder),
            ("ImplementedPrototypeOrder", prepared.Specification.ImplementedOrder),
            ("OrderMode", request.OrderMode));
        Add("Prototype", "Generate normalized analog prototype",
            prepared.Prototype.Normalization?.Definition ?? "Normalized all-pole prototype generated.",
            ("Poles", prepared.Prototype.Poles.Count), ("FiniteZeros", prepared.Prototype.Zeros.Count),
            ("NumeratorGain", prepared.Prototype.NumeratorGain));
        if (prepared.Prototype.LcLadderPrototype is { } ladder)
            Add("LC Synthesis", "Synthesize and validate normalized loaded ladder",
                ladder.SynthesisMethod,
                ("TerminationMode", ladder.TerminationMode),
                ("SourceTerminationG0", ladder.SourceTerminationG0),
                ("LoadTerminationGN1", ladder.LoadTerminationGN1),
                ("Elements", ladder.Elements.Count),
                ("CauerExtractionSteps", ladder.ExtractionRecords?.Count ?? 0),
                ("ReconstructionMaximumError", ladder.ValidationMetrics.MaximumMagnitudeError));
        Add("Transform", $"Low-pass prototype to {request.ResponseType}",
            "The common analog frequency-transformation service generated the target poles; band transformations double realized order.",
            ("PrototypeOrder", prepared.Transformed.PrototypeOrder), ("RealizedOrder", prepared.Transformed.RealizedOrder));
        foreach (var section in prepared.SynthesizedSections)
            Add("Section", $"Section {section.Target.Index} synthesis",
                $"{request.Implementation} solved one {section.Target.Order} {section.Target.ResponseType} section and reverse-verified the selected values.",
                ("TargetF0Hz", section.Target.PoleFrequencyHz), ("TargetQ", section.Target.Q),
                ("TargetGain", section.Target.SectionGain), ("ActualF0Hz", section.ActualPoleFrequencyHz),
                ("ActualQ", section.ActualQ), ("ActualGain", section.ActualGain),
                ("StandardValueOptimization", request.ComponentConstraints.OptimizeStandardValues));
        Add("Gain", "Distribute and verify overall gain",
            "Overall A0 was separated from intrinsic topology gains and checked against the final CircuitGraph response.",
            ("RequestedA0", gain.RequestedOverallGain), ("RawCascadeGain", gain.RawCascadeGainMagnitude),
            ("InterstageScale", gain.InterstageScaleProduct), ("ActualPassbandPeak", gain.ActualPassbandPeakGain),
            ("ExpectedPolarity", gain.ExpectedOverallPolarity));
        Add("Circuit", "Assemble and validate CircuitGraph",
            "Response-specific topology assembly created components and nets, then graph and schematic verification were applied.",
            ("Components", circuit.CircuitGraph.Components.Count), ("Nets", circuit.CircuitGraph.Nets.Count));
        Add("Verification", "Verify final standard-value circuit",
            "AC analysis was performed from the selected-value CircuitGraph; edge measurements below are actual circuit results.",
            ("AllEdgesPass", compliance.MeetsAllEdges), ("MeasuredEdges", compliance.Edges.Count));
        return records;
    }

    private sealed record PreparedDesign(
        NormalizedFilterSpecification Specification,
        FilterPrototype Prototype,
        TransformedFilterPoles Transformed,
        GainDistributionPlan GainPlan,
        IReadOnlyList<SynthesizedSection> SynthesizedSections);
}
