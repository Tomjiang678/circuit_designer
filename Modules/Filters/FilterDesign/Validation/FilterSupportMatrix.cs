using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;

public enum FilterSupportReasonCode
{
    Supported,
    ArchitectureUnavailable,
    PrototypeUnavailable,
    ApproximationUnavailable,
    TopologyUnavailable,
    ResponseUnavailable,
    OrderLimited,
    PendingIndependentVerification
    ,LcTerminationModeMismatch
    ,LcPrototypeNotPositiveReal
    ,LcLadderExtractionProducedNegativeElement
    ,LcFinalReconstructionFailed
    ,BainterNotchDepthInsufficientWithSelectedSeries
    ,ComponentRangeInfeasible
}

public enum FilterCapabilityLevel
{
    PrototypeCapability,
    FrequencyTransformationCapability,
    TopologyCapability,
    SectionFeasibility,
    ComponentRangeFeasibility,
    StandardValueFeasibility,
    TerminationCapability,
    LcPositiveRealFeasibility,
    FinalCircuitVerification
}

public sealed record FilterCapabilityEvaluation(
    FilterCapabilityLevel Level,
    bool IsSupported,
    FilterSupportReasonCode ReasonCode,
    string Message,
    string SuggestedAlternative);

public static class FilterSupportDecision
{
    public static FilterCapabilityEvaluation Supported(string message) =>
        new(FilterCapabilityLevel.FinalCircuitVerification, true, FilterSupportReasonCode.Supported, message, string.Empty);

    public static FilterCapabilityEvaluation Unsupported(FilterSupportReasonCode reasonCode, string message)
    {
        var level = reasonCode switch
        {
            FilterSupportReasonCode.ArchitectureUnavailable or FilterSupportReasonCode.TopologyUnavailable or FilterSupportReasonCode.ResponseUnavailable => FilterCapabilityLevel.TopologyCapability,
            FilterSupportReasonCode.PrototypeUnavailable or FilterSupportReasonCode.ApproximationUnavailable or FilterSupportReasonCode.OrderLimited => FilterCapabilityLevel.PrototypeCapability,
            FilterSupportReasonCode.LcTerminationModeMismatch => FilterCapabilityLevel.TerminationCapability,
            FilterSupportReasonCode.BainterNotchDepthInsufficientWithSelectedSeries => FilterCapabilityLevel.StandardValueFeasibility,
            FilterSupportReasonCode.ComponentRangeInfeasible => FilterCapabilityLevel.ComponentRangeFeasibility,
            FilterSupportReasonCode.LcPrototypeNotPositiveReal or FilterSupportReasonCode.LcLadderExtractionProducedNegativeElement or FilterSupportReasonCode.LcFinalReconstructionFailed => FilterCapabilityLevel.LcPositiveRealFeasibility,
            FilterSupportReasonCode.PendingIndependentVerification => FilterCapabilityLevel.FinalCircuitVerification,
            _ => FilterCapabilityLevel.SectionFeasibility
        };
        var alternative = reasonCode switch
        {
            FilterSupportReasonCode.LcTerminationModeMismatch => "Use Doubly Terminated mode with explicit Rs/RL, or use an active filter for an ideal-voltage-source interface.",
            FilterSupportReasonCode.LcPrototypeNotPositiveReal or FilterSupportReasonCode.LcLadderExtractionProducedNegativeElement => "Choose a supported family/order, or use the verified active-filter realization.",
            FilterSupportReasonCode.ResponseUnavailable => "Choose the response-specific v1 topology shown by the implementation selector.",
            FilterSupportReasonCode.BainterNotchDepthInsufficientWithSelectedSeries => "Use E96, tighter component tolerance, theoretical values, or a lower notch-depth requirement.",
            _ => "Adjust the selected architecture, approximation, order, component constraints, or standard-value series as indicated."
        };
        return new(level, false, reasonCode, message, alternative);
    }
}

public static class FilterSupportMatrix
{
    public static IReadOnlyList<FilterImplementationType> GetSupportedImplementations(FilterDesignRequest request)
    {
        var candidates=request.Mode==FilterMode.Active
            ? new[]{FilterImplementationType.SallenKey,FilterImplementationType.MultipleFeedback,FilterImplementationType.BainterNotch}
            : new[]{FilterImplementationType.Rc,FilterImplementationType.Lc};
        return candidates.Where(implementation=>Evaluate(request with { Implementation=implementation }).IsSupported).ToArray();
    }

    public static FilterCapabilityEvaluation Evaluate(FilterDesignRequest request)
    {
        if (request.SignalArchitecture == FilterSignalArchitecture.FullyDifferential)
            return FilterSupportDecision.Unsupported(FilterSupportReasonCode.ArchitectureUnavailable, "Fully Differential synthesis is not implemented yet.");
        if (request.Mode == FilterMode.Active)
        {
            if (!FilterApproximationCapabilities.HasVerifiedActivePrototype(request.Approximation))
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.PrototypeUnavailable, $"No verified active prototype designer is registered for {request.Approximation}.");
            if (request.ResponseType == FilterResponseType.BandStop)
                return request.Implementation == FilterImplementationType.BainterNotch
                    ? FilterSupportDecision.Supported("Verified Bainter Notch Band-Stop section synthesis is available subject to section and selected-value notch-depth feasibility.")
                    : FilterSupportDecision.Unsupported(FilterSupportReasonCode.ResponseUnavailable, "Active Band-Stop v1 is realized only by Bainter Notch; Sallen-Key BS and MFB BS remain unsupported.");
            if (request.Implementation == FilterImplementationType.BainterNotch)
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.ResponseUnavailable, "Bainter Notch is a response-specific Active Band-Stop topology.");
            if (request.Implementation == FilterImplementationType.MultipleFeedback)
            {
                return request.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass or FilterResponseType.BandPass
                    ? FilterSupportDecision.Supported("Verified MFB Low-Pass, High-Pass and Band-Pass section synthesis is available.")
                    : FilterSupportDecision.Unsupported(FilterSupportReasonCode.ResponseUnavailable, "This MFB response-specific solver is not implemented.");
            }
            if (request.Implementation != FilterImplementationType.SallenKey)
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.TopologyUnavailable, "The selected active implementation is not supported.");
            return request.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass or FilterResponseType.BandPass
                ? FilterSupportDecision.Supported("Verified Sallen-Key Low-Pass, High-Pass and conditionally feasible Band-Pass section synthesis is available.")
                : FilterSupportDecision.Unsupported(FilterSupportReasonCode.ResponseUnavailable, "Sallen-Key Band-Stop is not a supported realization; use Bainter Notch when that path becomes available.");
        }
        if (request.Implementation == FilterImplementationType.Rc)
        {
            if (request.Approximation is not (FilterApproximation.Butterworth or FilterApproximation.ChebyshevTypeI))
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.ApproximationUnavailable, $"{request.Approximation} is retained for future prototype work but is not in the verified passive RC support set.");
            if (request.ResponseType is not (FilterResponseType.LowPass or FilterResponseType.HighPass))
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.ResponseUnavailable, "Passive RC is limited to first-order Low-Pass or High-Pass.");
            return FilterSupportDecision.Supported("First-order passive RC synthesis is supported when the required/selected order is 1.");
        }
        if (request.Implementation == FilterImplementationType.Lc)
        {
            if (request.LcTerminationMode == LcTerminationMode.IdealVoltageSource)
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.LcTerminationModeMismatch,
                    "The current verified lossless LC prototypes are doubly terminated insertion-loss networks. Rs=0 Ideal Voltage Source mode requires a separately synthesized singly terminated voltage-transfer ladder and is not represented by classic g-values.");
            if (!FilterApproximationCapabilities.HasDoublyTerminatedLadder(request.Approximation))
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.ApproximationUnavailable, $"{request.Approximation} is an all-pole active-filter table and does not define the doubly terminated LC ladder g-values used by this synthesizer.");
            if (request.Approximation == FilterApproximation.Bessel && request.OrderMode == FilterOrderMode.SpecifyOrder &&
                request.SpecifiedOrder is < 1 or > 6)
                return FilterSupportDecision.Unsupported(FilterSupportReasonCode.OrderLimited,
                    "Bessel Passive LC v1 is numerically qualified for prototype orders 1 through 6.");
            if (request.Approximation is FilterApproximation.GaussianTo6Db or FilterApproximation.GaussianTo12Db or
                FilterApproximation.LinearPhase005Degree or FilterApproximation.LinearPhase05Degree &&
                request.OrderMode == FilterOrderMode.SpecifyOrder)
            {
                var audit = LcPrototypeCapabilityRegistry.Evaluate(request.Approximation, request.SpecifiedOrder);
                if (!audit.IsSupported)
                {
                    var reason = audit.ReasonCode switch
                    {
                        "LcLadderExtractionProducedNegativeElement" => FilterSupportReasonCode.LcLadderExtractionProducedNegativeElement,
                        "LcFinalReconstructionFailed" => FilterSupportReasonCode.LcFinalReconstructionFailed,
                        _ => FilterSupportReasonCode.LcPrototypeNotPositiveReal
                    };
                    return FilterSupportDecision.Unsupported(reason, audit.Message);
                }
            }
            return FilterSupportDecision.Supported("Verified doubly terminated LC ladder synthesis is available; Bessel is qualified for prototype orders 1 through 6.");
        }
        return FilterSupportDecision.Unsupported(FilterSupportReasonCode.TopologyUnavailable, "The selected implementation is not supported.");
    }
}
