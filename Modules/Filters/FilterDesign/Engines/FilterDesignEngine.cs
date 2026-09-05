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
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

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
                Capability = capability.IsSupported ? PreparationFailure(availability.Message) : capability
            };
        }

        try
        {
            request = prepared.Request;
            var circuitBuild = circuitAssembler.Build(
                request,
                prepared.Specification,
                prepared.Prototype,
                prepared.SynthesizedSections,
                prepared.GainPlan);
            FilterComponentRangeValidator.EnsureWithinConstraints(circuitBuild.CircuitGraph, request.ComponentConstraints);
            var schematic = layoutService.Layout(circuitBuild);
            var passiveAnalysis = request.Mode == FilterMode.Passive
                ? analysisService.AnalyzePassive(circuitBuild.CircuitGraph, request)
                : null;
            var analysis = passiveAnalysis?.Primary ?? analysisService.Analyze(circuitBuild.CircuitGraph, request);
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
            var meetsSpecifications = prepared.Specification.MeetsOrderRequirement && compliance.MeetsAllEdges;
            var theoretical = meetsSpecifications
                ? TheoreticalCircuitEvaluation.NotEvaluated("Continuous-value verification was not required because the selected-value CircuitGraph passed every final gate.")
                : EvaluateTheoreticalCircuit(prepared);
            var warnings = (prepared.Specification.OrderWarning == null
                    ? Array.Empty<string>()
                    : new[] { prepared.Specification.OrderWarning })
                .Concat(compliance.Warnings)
                .Concat(verification.Warnings)
                .Concat(BuildPassiveEngineeringWarnings(request, circuitBuild.CircuitGraph))
                .ToArray();
            var capability = meetsSpecifications
                ? new FilterCapabilityEvaluation(FilterCapabilityLevel.FinalCircuitVerification, true,
                    FilterSupportReasonCode.Supported, "All synthesis and final CircuitGraph verification gates passed.", string.Empty)
                : ClassifySpecificationFailure(request, prepared.Specification, compliance, theoretical);
            var result = new FilterDesignResult
            {
                Status = FilterDesignStatus.Success,
                Message = meetsSpecifications ? "Filter circuit generated successfully." : capability.Message,
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
                PassiveAnalysis = passiveAnalysis,
                PassiveComponentAssumptions = request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Lc
                    ? new PassiveComponentPracticalAssumptions(true)
                    : null,
                SpecificationCompliance = compliance,
                GainDistribution = gainSummary,
                Verification = verification,
                StageDiagnostics = new(theoretical.CircuitValid, theoretical.MeetsSpecifications, true,
                    compliance.MeetsAllEdges, capability.Level.ToString(), theoretical.Detail),
                CalculationTrace = BuildCalculationTrace(request, prepared, circuitBuild, compliance, gainSummary, passiveAnalysis, theoretical, capability)
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
            var failure = BuildSynthesisFailureCapability(request, exception.Message);
            return new FilterDesignResult
            {
                Status = FilterDesignStatus.SynthesisFailure,
                Message = exception.Message,
                Capability = failure
            };
        }
    }

    private static FilterCapabilityEvaluation BuildSynthesisFailureCapability(FilterDesignRequest request, string message)
    {
        if (request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Lc)
        {
            if (message.Contains("ComponentRangeInfeasible", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("outside the selected", StringComparison.OrdinalIgnoreCase))
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.LcComponentRangeViolation, message);
            if (message.Contains("standard-value", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("center-frequency error", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Final design verification failed", StringComparison.OrdinalIgnoreCase))
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.LcSelectedValuesFailSpecification, message);
        }
        return new FilterCapabilityEvaluation(FilterCapabilityLevel.ComponentRangeFeasibility, false,
            FilterSupportReasonCode.ComponentRangeInfeasible, message,
            "Adjust component ranges or standard-value series and redesign.");
    }

    private FilterCapabilityEvaluation ClassifySpecificationFailure(FilterDesignRequest request,
        NormalizedFilterSpecification specification, FilterSpecificationCompliance compliance,
        TheoreticalCircuitEvaluation theoretical)
    {
        if (request.Implementation != FilterImplementationType.Rc && !specification.MeetsOrderRequirement)
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.SpecifiedOrderInsufficient,
                $"Specified prototype order {specification.ImplementedOrder} is below required order {specification.RequiredOrder}; the theoretical circuit cannot meet the entered edges.");
        if (request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc)
        {
            if (request.ResponseType == FilterResponseType.BandPass)
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.RcBandPassCornersNotAchievable,
                    "The loaded Two-Corner RC network is valid, but its actual peak-relative corners/stopband do not meet every entered edge.");
            if (request.ResponseType == FilterResponseType.BandStop)
            {
                var notchFails = compliance.Edges.Any(edge => edge.Label == "notch" && !edge.MeetsLimit);
                var reason = notchFails && request.ComponentConstraints.OptimizeStandardValues && theoretical.MeetsSpecifications == true
                    ? FilterSupportReasonCode.TwinTNotchDepthInsufficientWithSelectedSeries
                    : notchFails && request.SourceResistanceOhm > 0
                        ? FilterSupportReasonCode.TwinTSourceLoadInteractionTooStrong
                        : FilterSupportReasonCode.TwinTRequestedResponseNotAchievable;
                return FilterSupportDecision.Unsupported(reason,
                    "The loaded Twin-T circuit is valid, but its notch depth and/or passband insertion do not meet the requested piecewise response.");
            }
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.RcLoadedPoleNotAchievable,
                "The loaded first-order RC circuit is valid, but its peak-relative edge response does not meet every entered limit.");
        }
        if (theoretical.CircuitValid == false)
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.ComponentRangeInfeasible,
                "A continuous-value circuit could not be constructed inside the selected component ranges: " + theoretical.Detail);
        if (theoretical.MeetsSpecifications == false)
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.TheoreticalDesignDoesNotMeetSpecification,
                "The continuous-value whole CircuitGraph does not meet every entered edge; this is not an E-series rounding failure.");
        if (theoretical.MeetsSpecifications is null)
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.SelectedSectionVerificationFailed,
                "The selected-value circuit did not meet the requested specification; continuous-value verification was not applicable or could not be completed: " + theoretical.Detail);
        return FilterSupportDecision.Unsupported(request.Implementation == FilterImplementationType.Lc
                ? FilterSupportReasonCode.LcSelectedValuesFailSpecification
                : FilterSupportReasonCode.SelectedValuesFailSpecification,
            "The continuous-value circuit meets the specification, but the selected E-series CircuitGraph does not.");
    }

    private TheoreticalCircuitEvaluation EvaluateTheoreticalCircuit(PreparedDesign prepared)
    {
        try
        {
            var request = prepared.Request with { ComponentConstraints = CloneConstraints(prepared.Request.ComponentConstraints, false) };
            IReadOnlyList<SynthesizedSection> sections;
            if (request.Implementation == FilterImplementationType.Lc)
                sections = prepared.SynthesizedSections;
            else if (request.Implementation == FilterImplementationType.BainterNotch)
                sections = bainterNotchCascadeOptimizer.Optimize(prepared.GainPlan.Sections, request, prepared.Prototype, prepared.GainPlan).ToArray();
            else
                sections = prepared.GainPlan.Sections.Select(section => ApplyTopologyPolarity(CalculateComponents(section, request), request)).ToArray();
            var circuit = circuitAssembler.Build(request, prepared.Specification, prepared.Prototype, sections, prepared.GainPlan);
            FilterComponentRangeValidator.EnsureWithinConstraints(circuit.CircuitGraph, request.ComponentConstraints);
            var analysis = request.Mode == FilterMode.Passive
                ? analysisService.AnalyzePassive(circuit.CircuitGraph, request).Primary
                : analysisService.Analyze(circuit.CircuitGraph, request);
            var compliance = complianceEvaluator.Evaluate(request, analysis);
            return new(true, compliance.MeetsAllEdges, compliance.MeetsAllEdges
                ? "Continuous theoretical CircuitGraph meets all edges."
                : string.Join(" ", compliance.Warnings));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            var isRangeFailure = exception.Message.Contains("ComponentRangeInfeasible", StringComparison.OrdinalIgnoreCase) ||
                                 exception.Message.Contains("outside the selected", StringComparison.OrdinalIgnoreCase) ||
                                 exception.Message.Contains("component range", StringComparison.OrdinalIgnoreCase);
            return isRangeFailure
                ? new(false, null, exception.Message)
                : TheoreticalCircuitEvaluation.NotEvaluated(exception.Message);
        }
    }

    private static ComponentSelectionConstraints CloneConstraints(ComponentSelectionConstraints source, bool optimize) => new()
    {
        ResistorSeries = source.ResistorSeries, CapacitorSeries = source.CapacitorSeries, InductorSeries = source.InductorSeries,
        ResistorTolerancePercent = source.ResistorTolerancePercent, CapacitorTolerancePercent = source.CapacitorTolerancePercent,
        InductorTolerancePercent = source.InductorTolerancePercent, ResistorMinimumOhm = source.ResistorMinimumOhm,
        ResistorMaximumOhm = source.ResistorMaximumOhm, CapacitorMinimumF = source.CapacitorMinimumF,
        CapacitorMaximumF = source.CapacitorMaximumF, InductorMinimumH = source.InductorMinimumH,
        InductorMaximumH = source.InductorMaximumH, OptimizeStandardValues = optimize, OptimizationTarget = source.OptimizationTarget
    };

    private sealed record TheoreticalCircuitEvaluation(bool? CircuitValid, bool? MeetsSpecifications, string Detail)
    {
        public static TheoreticalCircuitEvaluation NotEvaluated(string detail) => new(null, null, detail);
    }

    private static IEnumerable<string> BuildPassiveEngineeringWarnings(FilterDesignRequest request, Core.Circuit.CircuitGraph graph)
    {
        if (request.Mode != FilterMode.Passive || request.Implementation != FilterImplementationType.Lc)
            return [];
        var warnings = new List<string>();
        if (request.ResponseType is FilterResponseType.BandPass or FilterResponseType.BandStop)
        {
            var center = Math.Sqrt(request.PassbandLowerHz * request.PassbandUpperHz);
            var fractionalBandwidth = (request.PassbandUpperHz - request.PassbandLowerHz) / center;
            if (fractionalBandwidth < .05)
                warnings.Add($"IdealComponentAssumptionMayBeUnrealistic: fractional bandwidth is {fractionalBandwidth:P2}; real inductor Q/DCR and capacitor ESR require engineering verification.");
        }
        var spreads = graph.Components
            .Where(value => value.Type is Core.Circuit.ComponentType.Inductor or Core.Circuit.ComponentType.Capacitor)
            .GroupBy(value => value.Type)
            .Select(group =>
            {
                var values = group.Select(value => value.Type == Core.Circuit.ComponentType.Inductor
                    ? value.Parameters.GetRequiredValue(Core.Circuit.ComponentParameterNames.InductanceHenry)
                    : value.Parameters.GetRequiredValue(Core.Circuit.ComponentParameterNames.CapacitanceFarad)).ToArray();
                return values.Max() / values.Min();
            }).DefaultIfEmpty(1).Max();
        if (spreads > 10_000)
            warnings.Add($"NarrowBandComponentSpreadTooLarge: same-type LC component spread is {spreads:G5}:1; consider another impedance or ladder orientation.");
        return warnings;
    }

    private static FilterCapabilityEvaluation PreparationFailure(string message)
    {
        var reason = new[]
        {
            FilterSupportReasonCode.BainterNotchDepthInsufficientWithSelectedSeries,
            FilterSupportReasonCode.LcPrototypeNotPositiveReal,
            FilterSupportReasonCode.LcTerminationModeMismatch,
            FilterSupportReasonCode.LcTerminationRatioUnsupported,
            FilterSupportReasonCode.LcPrototypeRequiresNativeTermination,
            FilterSupportReasonCode.RcLoadedPoleNotAchievable,
            FilterSupportReasonCode.RcPassbandLossTooHigh,
            FilterSupportReasonCode.RcBandPassCornersNotAchievable,
            FilterSupportReasonCode.TwinTNotchDepthInsufficient
        }.FirstOrDefault(value => message.Contains(value.ToString(), StringComparison.Ordinal));
        if (reason != default) return FilterSupportDecision.Unsupported(reason, message);
        if (message.Contains("Sallen-Key band-pass section requires gain greater", StringComparison.Ordinal))
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.SallenKeyBpGainCouplingViolation, message);
        if (message.Contains("MFB band-pass section requires Q", StringComparison.Ordinal))
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.SectionQOutOfRange, message);
        if (message.Contains("MFB band-pass section gain", StringComparison.OrdinalIgnoreCase) &&
            message.Contains("positive-component realization range", StringComparison.OrdinalIgnoreCase))
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.MfbBpGainOutOfRange, message);
        if (message.Contains("Bainter selected values exceed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Bainter selected-value verification", StringComparison.OrdinalIgnoreCase))
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.BainterSelectedValueVerificationFailed, message);
        if (message.Contains("selected values exceed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("selected-value verification", StringComparison.OrdinalIgnoreCase))
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.SelectedSectionVerificationFailed, message);
        if (message.Contains("cannot fit", StringComparison.OrdinalIgnoreCase) || message.Contains("component range", StringComparison.OrdinalIgnoreCase))
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.ComponentRangeInfeasible, message);
        if (message.Contains("Bessel Passive LC v1", StringComparison.Ordinal))
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.OrderLimited, message);
        return FilterSupportDecision.Unsupported(FilterSupportReasonCode.SelectedSectionVerificationFailed, message);
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
            if (request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc)
                specification = specification with
                {
                    RequiredOrder = 1,
                    ImplementedOrder = 1,
                    MeetsOrderRequirement = true,
                    OrderWarning = "Passive RC is topology-based; approximation-family order is not applied."
                };
            var prototype = request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc
                ? new ButterworthPrototypeDesigner().Design(1, request.PassbandRippleDb)
                : prototypeDesigners[request.Approximation].Design(specification.ImplementedOrder, request.PassbandRippleDb);
            LcTerminationResolution? terminationResolution = null;
            if (request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Lc && prototype.LcLadderPrototype is { } rawLadder)
            {
                var resolved = new LcTerminationPolicyService().Resolve(request, rawLadder);
                request = resolved.Request;
                terminationResolution = resolved.Resolution;
                var oriented = new LcLadderOrientationService().Select(rawLadder, request.LcLadderOrientation, request, specification);
                prototype = prototype with { LcLadderPrototype = oriented, LadderGValues = oriented.AsLegacyGValues() };
            }
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

            prepared = new PreparedDesign(request, specification, prototype, transformed, gainPlan, synthesizedSections, terminationResolution);
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
        if (request.Mode == FilterMode.Passive)
        {
            if (request.SourceResistanceOhm < 0 || !double.IsFinite(request.SourceResistanceOhm))
                throw new ArgumentException("Passive source resistance Rs must be finite and greater than or equal to zero.");
            if (request.LoadResistanceOhm <= 0 || !double.IsFinite(request.LoadResistanceOhm))
                throw new ArgumentException("Passive load resistance RL must be finite and greater than zero.");
        }
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

        var ladder = prototype.LcLadderPrototype;
        var impedanceScale = request.SourceResistanceOhm / ladder.SourceTerminationG0;
        var requiredLoad = impedanceScale * ladder.LoadTerminationGN1;
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
        GainDistributionSummary gain,
        PassiveFilterAnalysisResult? passiveAnalysis,
        TheoreticalCircuitEvaluation theoretical,
        FilterCapabilityEvaluation capability)
    {
        var records = new List<DesignStepRecord>();
        void Add(string category, string title, string explanation, params (string Key, object Value)[] values) =>
            records.Add(new DesignStepRecord(records.Count + 1, category, title, explanation,
                values.ToDictionary(item => item.Key, item => Convert.ToString(item.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)));
        if (request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc)
            Add("RC Topology", "Select physical passive RC network",
                "Passive RC synthesis is topology-based; approximation-family order and arbitrary complex-pole prototypes are not claimed.",
                ("Topology", request.PassiveRcTopology), ("Response", request.ResponseType));
        else
        {
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
        }
        if (passiveAnalysis is { } passive)
        {
            Add("Source / Load / Reference", "Define passive electrical boundary",
                "VSRC is the ideal Thevenin open-circuit source voltage; Rs connects VSRC to the filter input VIN; VOUT is loaded by RL to GND.",
                ("Boundary", "VSRC -> Rs -> VIN -> Filter Network -> VOUT -> RL -> GND"),
                ("RsOhm", passive.Boundary.SourceResistanceOhm),
                ("RLOhm", passive.Boundary.LoadResistanceOhm),
                ("DirectThroughVoutOverVsrc", passive.Boundary.DirectThroughVoltageRatio),
                ("PrimaryResponseReference", passive.PrimaryReference),
                ("TransducerGainAvailable", passive.TransducerGain is not null));
        }
        if (prepared.Prototype.LcLadderPrototype is { } ladder)
            Add("LC Synthesis", "Synthesize and validate normalized loaded ladder",
                ladder.SynthesisMethod,
                ("TerminationMode", ladder.TerminationMode),
                ("SourceTerminationG0", ladder.SourceTerminationG0),
                ("LoadTerminationGN1", ladder.LoadTerminationGN1),
                ("Elements", ladder.Elements.Count),
                ("CauerExtractionSteps", ladder.ExtractionRecords?.Count ?? 0),
                ("ReconstructionMaximumError", ladder.ValidationMetrics.MaximumMagnitudeError));
        if (prepared.TerminationResolution is { } termination)
            Add("LC Termination", "Resolve normalized prototype terminations",
                termination.Summary,
                ("Policy", termination.Policy), ("PrototypeRLOverRs", termination.PrototypeTerminationRatio),
                ("RequiredRsOhm", termination.RequiredSourceResistanceOhm), ("RequiredRLOhm", termination.RequiredLoadResistanceOhm),
                ("ImpedanceScaleOhm", termination.ImpedanceScaleOhm), ("Orientation", request.LcLadderOrientation));
        Add("Verification", "Separate theoretical and selected-value gates",
            "Continuous-value and selected-value whole CircuitGraphs are checked separately so order, range and E-series failures are not conflated.",
            ("TheoreticalCircuitValid", TriState(theoretical.CircuitValid)),
            ("TheoreticalMeetsSpecifications", TriState(theoretical.MeetsSpecifications)),
            ("SelectedCircuitValid", true),
            ("SelectedMeetsSpecifications", compliance.MeetsAllEdges),
            ("FailureStage", capability.Level),
            ("ReasonCode", capability.ReasonCode));
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
        if (request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc &&
            prepared.SynthesizedSections.FirstOrDefault() is { } rcSection)
        {
            if (request.ResponseType == FilterResponseType.BandPass)
            {
                var bandwidth = rcSection.ActualQ > 0 ? rcSection.ActualPoleFrequencyHz / rcSection.ActualQ : 0;
                var discriminant = Math.Sqrt(bandwidth * bandwidth + 4 * rcSection.ActualPoleFrequencyHz * rcSection.ActualPoleFrequencyHz);
                Add("RC Verification", "Loaded Two-Corner response",
                    "Actual corners are the two -3 dB crossings relative to the loaded band-pass peak; the complete four-component CircuitGraph, Rs and RL are solved together.",
                    ("ActualLowerCornerHz", (discriminant - bandwidth) / 2),
                    ("ActualUpperCornerHz", (discriminant + bandwidth) / 2),
                    ("ActualPeakGain", rcSection.ActualGain),
                    ("WholeLoadedMnaVerified", true));
            }
            else if (request.ResponseType == FilterResponseType.BandStop)
            {
                var values = rcSection.Components.ToDictionary(value => value.Role, value => value.SelectedValue);
                var rMismatch = Math.Abs(values["R1"] / values["R2"] - 1);
                var cMismatch = Math.Abs(values["C1"] / values["C2"] - 1);
                var notch = compliance.Edges.FirstOrDefault(edge => edge.Label == "notch");
                Add("RC Verification", "Loaded Twin-T notch and ratio matching",
                    "The two equal-R/equal-C arms plus real parallel pairs implement R/2 and 2C; the final notch is measured by whole loaded CircuitGraph MNA.",
                    ("ActualNotchFrequencyHz", rcSection.ActualPoleFrequencyHz),
                    ("ActualNotchMagnitudeDb", notch?.ActualMagnitudeDb ?? double.NaN),
                    ("RPairMismatch", rMismatch), ("CPairMismatch", cMismatch),
                    ("CompoundComponentsPresent", rcSection.Components.Count == 8),
                    ("WholeLoadedMnaVerified", true));
            }
        }
        if (request.Mode == FilterMode.Active)
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

    private static string TriState(bool? value) => value switch { true => "True", false => "False", null => "NotEvaluated" };

    private sealed record PreparedDesign(
        FilterDesignRequest Request,
        NormalizedFilterSpecification Specification,
        FilterPrototype Prototype,
        TransformedFilterPoles Transformed,
        GainDistributionPlan GainPlan,
        IReadOnlyList<SynthesizedSection> SynthesizedSections,
        LcTerminationResolution? TerminationResolution);
}
