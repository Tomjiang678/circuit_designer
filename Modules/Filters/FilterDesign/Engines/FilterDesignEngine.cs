using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Services;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;

public sealed class FilterDesignEngine : IFilterDesignEngine
{
    private readonly FilterSpecificationNormalizer normalizer;
    private readonly IReadOnlyDictionary<FilterApproximation, IFilterPrototypeDesigner> prototypeDesigners;
    private readonly AnalogFrequencyTransformationService transformation = new();
    private readonly FilterSectionBuilder sectionBuilder = new();
    private readonly FilterSectionGainAllocator gainAllocator = new();
    private readonly SallenKeySectionCalculator sallenKeyCalculator = new();
    private readonly SallenKeyBandPassSectionCalculator sallenKeyBandPassCalculator = new();
    private readonly MfbLowPassSectionCalculator mfbLowPassCalculator = new();
    private readonly MfbHighPassSectionCalculator mfbHighPassCalculator = new();
    private readonly MfbBandPassSectionCalculator mfbBandPassCalculator = new();
    private readonly BainterNotchSectionCalculator bainterNotchCalculator = new();
    private readonly FilterCircuitAssembler circuitAssembler = new();
    private readonly FilterSchematicLayoutService layoutService = new();
    private readonly FilterAnalysisService analysisService = new();
    private readonly FilterSpiceExportService spiceExportService = new();

    public FilterDesignEngine()
    {
        var tabulated = new[] { FilterApproximation.GaussianTo6Db, FilterApproximation.GaussianTo12Db, FilterApproximation.LinearPhase005Degree, FilterApproximation.LinearPhase05Degree };
        normalizer = new FilterSpecificationNormalizer([new ButterworthOrderCalculator(), new ChebyshevOrderCalculator(), new BesselOrderCalculator(), .. tabulated.Select(value => new TabulatedPrototypeOrderCalculator(value))]);
        IFilterPrototypeDesigner[] designers = [new ButterworthPrototypeDesigner(), new ChebyshevPrototypeDesigner(), new BesselPrototypeDesigner(), .. tabulated.Select(value => new TabulatedPrototypeDesigner(value))];
        prototypeDesigners = designers.ToDictionary(item => item.Approximation);
    }

    public FilterDesignAvailability GetAvailability(FilterDesignRequest request)
    {
        try
        {
            var support = FilterSupportMatrix.Evaluate(request);
            if (!support.IsSupported) return FilterDesignAvailability.Unavailable(support.Message);
            if (request.PassbandRippleDb <= 0 || request.StopbandAttenuationDb <= request.PassbandRippleDb)
                return FilterDesignAvailability.Unavailable("Enter Rp > 0 and As > Rp.");
            if (request.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass)
            {
                if (request.PassbandEdgeHz <= 0 || request.StopbandEdgeHz <= 0)
                    return FilterDesignAvailability.Unavailable("Passband and stopband frequencies must be positive.");
                if (request.ResponseType == FilterResponseType.LowPass && request.StopbandEdgeHz <= request.PassbandEdgeHz)
                    return FilterDesignAvailability.Unavailable("Low-Pass requires fs > fp.");
                if (request.ResponseType == FilterResponseType.HighPass && request.PassbandEdgeHz <= request.StopbandEdgeHz)
                    return FilterDesignAvailability.Unavailable("High-Pass requires fp > fs.");
            }
            if (request.Mode == FilterMode.Active && (request.PassbandGain <= 0 || !double.IsFinite(request.PassbandGain)))
                return FilterDesignAvailability.Unavailable("Passband gain A0 must be positive and finite.");
            var specification = normalizer.Normalize(request);
            var availabilityPrototype = prototypeDesigners[request.Approximation].Design(specification.ImplementedOrder, request.PassbandRippleDb);
            if (request.Mode == FilterMode.Active && request.ResponseType == FilterResponseType.BandPass)
            {
                var bandSections = gainAllocator.Allocate(sectionBuilder.Build(transformation.TransformBandPass(availabilityPrototype, specification.CenterAngularFrequency, specification.BandwidthAngularFrequency)), request);
                if (request.Implementation == FilterImplementationType.MultipleFeedback && bandSections.Any(section => section.Q <= Math.Sqrt(.5) || section.SectionGain >= 2 * section.Q * section.Q))
                    return FilterDesignAvailability.Unavailable("This MFB band-pass realization requires Q > 0.707 and section gain < 2Q² for every transformed section; narrow the passband or choose another implementation.");
                if (request.Implementation == FilterImplementationType.SallenKey && bandSections.Any(section =>
                    section.Q <= 1d / 3.5 || section.SectionGain > ((6.5 - 1 / section.Q) / 3) * section.Q))
                    return FilterDesignAvailability.Unavailable("This Sallen-Key band-pass realization requires Q > 0.2857 and a target section gain no greater than its Q-coupled natural gain.");
            }
            if (request.Mode == FilterMode.Active)
            {
                var transformed = request.ResponseType switch
                {
                    FilterResponseType.LowPass or FilterResponseType.HighPass => transformation.TransformLowPassOrHighPass(availabilityPrototype, request.ResponseType, specification.CutoffAngularFrequency),
                    FilterResponseType.BandPass => transformation.TransformBandPass(availabilityPrototype, specification.CenterAngularFrequency, specification.BandwidthAngularFrequency),
                    FilterResponseType.BandStop => transformation.TransformBandStop(availabilityPrototype, specification.CenterAngularFrequency, specification.BandwidthAngularFrequency),
                    _ => throw new NotSupportedException()
                };
                foreach (var section in gainAllocator.Allocate(sectionBuilder.Build(transformed), request)) _ = CalculateComponents(section, request);
            }
            if (request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc && specification.ImplementedOrder != 1)
                return FilterDesignAvailability.Unavailable($"Passive RC only supports order 1; the current implemented order is {specification.ImplementedOrder}.");
            if (request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Lc)
            {
                var requiredLoad = request.SourceResistanceOhm * availabilityPrototype.LadderGValues[^1];
                if (Math.Abs(request.LoadResistanceOhm - requiredLoad) / requiredLoad > 1e-4)
                    return FilterDesignAvailability.Unavailable($"This normalized {request.Approximation} order-{specification.ImplementedOrder} ladder requires RL = {requiredLoad:0.###} Ω for Rs = {request.SourceResistanceOhm:0.###} Ω.");
            }
            return FilterDesignAvailability.Available(support.Message);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return FilterDesignAvailability.Unavailable(exception.Message);
        }
    }

    public FilterDesignResult Design(FilterDesignRequest request)
    {
        var availability = GetAvailability(request);
        if (!availability.CanDesign)
            return new FilterDesignResult { Status = FilterDesignStatus.UnsupportedDesignCombination, Message = availability.Message };
        try
        {
            var specification = normalizer.Normalize(request);
            var prototype = prototypeDesigners[request.Approximation].Design(specification.ImplementedOrder, request.PassbandRippleDb);
            var transformed = request.ResponseType switch
            {
                FilterResponseType.LowPass or FilterResponseType.HighPass => transformation.TransformLowPassOrHighPass(prototype, request.ResponseType, specification.CutoffAngularFrequency),
                FilterResponseType.BandPass => transformation.TransformBandPass(prototype, specification.CenterAngularFrequency, specification.BandwidthAngularFrequency),
                FilterResponseType.BandStop => transformation.TransformBandStop(prototype, specification.CenterAngularFrequency, specification.BandwidthAngularFrequency),
                _ => throw new NotSupportedException($"No analog transformation exists for {request.ResponseType}.")
            };
            var targetSections = gainAllocator.Allocate(sectionBuilder.Build(transformed), request);
            var synthesizedSections = request.Implementation == FilterImplementationType.Lc
                ? targetSections.Select(section => new SynthesizedSection(section, Array.Empty<SynthesizedComponentValue>(), section.PoleFrequencyHz, section.Q, section.SectionGain, 0, 0, 0)).ToArray()
                : targetSections.Select(section => CalculateComponents(section, request)).ToArray();
            var circuitBuild = circuitAssembler.Build(request, specification, prototype, synthesizedSections);
            FilterComponentRangeValidator.EnsureWithinConstraints(circuitBuild.CircuitGraph, request.ComponentConstraints);
            var schematic = layoutService.Layout(circuitBuild);
            var analysis = analysisService.Analyze(circuitBuild.CircuitGraph, request);
            var warnings = specification.OrderWarning == null ? Array.Empty<string>() : new[] { specification.OrderWarning };
            var result = new FilterDesignResult
            {
                Status = FilterDesignStatus.Success,
                Message = "Filter circuit generated successfully.",
                RequiredOrder = specification.RequiredOrder,
                PrototypeOrder = specification.ImplementedOrder,
                RealizedOrder = transformed.RealizedOrder,
                MeetsRequestedOrder = specification.MeetsOrderRequirement,
                NormalizedSpecification = specification,
                PrototypePoles = prototype.Poles,
                PrototypeElementValues = prototype.LadderGValues,
                Sections = synthesizedSections,
                AdditionalNetworkComponents = circuitBuild.NetworkComponentValues ?? Array.Empty<SynthesizedComponentValue>(),
                Warnings = warnings,
                CircuitGraph = circuitBuild.CircuitGraph,
                SchematicDocument = schematic,
                DesignRequest = request,
                AnalysisResult = analysis
            };
            result.SpiceNetlist = spiceExportService.Export(result);
            return result;
        }
        catch (NotSupportedException exception)
        {
            return new FilterDesignResult { Status = FilterDesignStatus.UnsupportedDesignCombination, Message = exception.Message };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new FilterDesignResult { Status = FilterDesignStatus.SynthesisFailure, Message = exception.Message };
        }
    }

    private SynthesizedSection CalculateComponents(AnalogFilterSection section, FilterDesignRequest request) =>
        request.Implementation switch
        {
            FilterImplementationType.BainterNotch when section.ResponseType == FilterResponseType.BandStop => bainterNotchCalculator.Calculate(section, request),
            FilterImplementationType.MultipleFeedback when section.Order == SectionOrder.Second && section.ResponseType == FilterResponseType.LowPass => mfbLowPassCalculator.Calculate(section, request),
            FilterImplementationType.MultipleFeedback when section.Order == SectionOrder.Second && section.ResponseType == FilterResponseType.HighPass => mfbHighPassCalculator.Calculate(section, request),
            FilterImplementationType.MultipleFeedback when section.Order == SectionOrder.Second && section.ResponseType == FilterResponseType.BandPass => mfbBandPassCalculator.Calculate(section, request),
            FilterImplementationType.MultipleFeedback => sallenKeyCalculator.Calculate(section, request),
            FilterImplementationType.SallenKey when section.ResponseType == FilterResponseType.BandPass => sallenKeyBandPassCalculator.Calculate(section, request),
            _ => sallenKeyCalculator.Calculate(section, request)
        };
}
