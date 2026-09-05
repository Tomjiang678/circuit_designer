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
    ,FinalCircuitSpecificationNotMet
    ,RcLoadedPoleNotAchievable
    ,RcPassbandLossTooHigh
    ,RcBandPassCornersNotAchievable
    ,TwinTNotchDepthInsufficient
    ,LcTerminationRatioUnsupported
    ,LcPrototypeRequiresNativeTermination
    ,LcComponentRangeViolation
    ,NarrowBandComponentSpreadTooLarge
    ,LcSelectedValuesFailSpecification
    ,SpecifiedOrderInsufficient
    ,TheoreticalDesignDoesNotMeetSpecification
    ,SelectedValuesFailSpecification
    ,RcBandPassComponentRangeViolation
    ,TwinTNotchDepthInsufficientWithSelectedSeries
    ,TwinTSourceLoadInteractionTooStrong
    ,TwinTRequestedResponseNotAchievable
    ,SallenKeyBpGainCouplingViolation
    ,SectionQOutOfRange
    ,MfbBpGainOutOfRange
    ,BainterSelectedValueVerificationFailed
    ,SelectedSectionVerificationFailed
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
            FilterSupportReasonCode.LcTerminationRatioUnsupported or FilterSupportReasonCode.LcPrototypeRequiresNativeTermination => FilterCapabilityLevel.TerminationCapability,
            FilterSupportReasonCode.BainterNotchDepthInsufficientWithSelectedSeries => FilterCapabilityLevel.StandardValueFeasibility,
            FilterSupportReasonCode.ComponentRangeInfeasible => FilterCapabilityLevel.ComponentRangeFeasibility,
            FilterSupportReasonCode.LcComponentRangeViolation or FilterSupportReasonCode.NarrowBandComponentSpreadTooLarge => FilterCapabilityLevel.ComponentRangeFeasibility,
            FilterSupportReasonCode.LcSelectedValuesFailSpecification => FilterCapabilityLevel.StandardValueFeasibility,
            FilterSupportReasonCode.SelectedValuesFailSpecification or FilterSupportReasonCode.TwinTNotchDepthInsufficientWithSelectedSeries => FilterCapabilityLevel.StandardValueFeasibility,
            FilterSupportReasonCode.SpecifiedOrderInsufficient or FilterSupportReasonCode.TheoreticalDesignDoesNotMeetSpecification => FilterCapabilityLevel.FrequencyTransformationCapability,
            FilterSupportReasonCode.RcBandPassComponentRangeViolation => FilterCapabilityLevel.ComponentRangeFeasibility,
            FilterSupportReasonCode.TwinTSourceLoadInteractionTooStrong or FilterSupportReasonCode.TwinTRequestedResponseNotAchievable or
                FilterSupportReasonCode.SallenKeyBpGainCouplingViolation or FilterSupportReasonCode.SectionQOutOfRange or
                FilterSupportReasonCode.MfbBpGainOutOfRange or FilterSupportReasonCode.BainterSelectedValueVerificationFailed or
                FilterSupportReasonCode.SelectedSectionVerificationFailed => FilterCapabilityLevel.SectionFeasibility,
            FilterSupportReasonCode.RcLoadedPoleNotAchievable or FilterSupportReasonCode.RcPassbandLossTooHigh or FilterSupportReasonCode.RcBandPassCornersNotAchievable or FilterSupportReasonCode.TwinTNotchDepthInsufficient => FilterCapabilityLevel.SectionFeasibility,
            FilterSupportReasonCode.LcPrototypeNotPositiveReal or FilterSupportReasonCode.LcLadderExtractionProducedNegativeElement or FilterSupportReasonCode.LcFinalReconstructionFailed => FilterCapabilityLevel.LcPositiveRealFeasibility,
            FilterSupportReasonCode.PendingIndependentVerification => FilterCapabilityLevel.FinalCircuitVerification,
            FilterSupportReasonCode.FinalCircuitSpecificationNotMet => FilterCapabilityLevel.FinalCircuitVerification,
            _ => FilterCapabilityLevel.SectionFeasibility
        };
        var alternative = reasonCode switch
        {
            FilterSupportReasonCode.LcTerminationModeMismatch => "Use Doubly Terminated mode with explicit Rs/RL, or use an active filter for an ideal-voltage-source interface.",
            FilterSupportReasonCode.LcPrototypeRequiresNativeTermination => "Select Prototype-Native termination so RL is derived from g0/gN+1, or choose a ladder/order compatible with matched terminations.",
            FilterSupportReasonCode.LcTerminationRatioUnsupported => "Use Prototype-Native termination, or enter the exact source/load ratio required by the normalized ladder.",
            FilterSupportReasonCode.LcComponentRangeViolation => "Widen the L/C ranges, change system impedance, or try the alternate ladder orientation.",
            FilterSupportReasonCode.NarrowBandComponentSpreadTooLarge => "Widen the band, change impedance, or use an active filter with practical resonator Q.",
            FilterSupportReasonCode.LcSelectedValuesFailSpecification => "Use E96, theoretical values, wider component ranges, or another ladder orientation.",
            FilterSupportReasonCode.SpecifiedOrderInsufficient => "Increase the specified order or use automatic order.",
            FilterSupportReasonCode.TheoreticalDesignDoesNotMeetSpecification => "Relax the edge specification, increase order, or choose a topology whose theoretical response can meet it.",
            FilterSupportReasonCode.SelectedValuesFailSpecification => "Use a denser E-series, disable standard-value optimization, or relax the requested tolerance.",
            FilterSupportReasonCode.RcBandPassComponentRangeViolation => "Widen the R/C ranges or change the preferred impedance scale.",
            FilterSupportReasonCode.TwinTNotchDepthInsufficientWithSelectedSeries => "Use E96, tighter matched components, or reduce the required notch depth.",
            FilterSupportReasonCode.TwinTSourceLoadInteractionTooStrong => "Reduce Rs, increase RL, or buffer the Twin-T network.",
            FilterSupportReasonCode.TwinTRequestedResponseNotAchievable => "Move the passband edges farther from the notch, relax Rp/As, or use Active Bainter/Passive LC.",
            FilterSupportReasonCode.SallenKeyBpGainCouplingViolation => "Change overall gain/order/bandwidth or use MFB Band-Pass for this section set.",
            FilterSupportReasonCode.SectionQOutOfRange => "Change order/bandwidth or select a topology that supports the required section Q.",
            FilterSupportReasonCode.MfbBpGainOutOfRange => "Reduce the requested section gain, change order/bandwidth, or use another verified Band-Pass topology.",
            FilterSupportReasonCode.BainterSelectedValueVerificationFailed => "Use a denser E-series/tighter matching, relax the notch requirement, or change the order/band edges.",
            FilterSupportReasonCode.SelectedSectionVerificationFailed => "Adjust the section target, order, topology, or component constraints and redesign.",
            FilterSupportReasonCode.RcLoadedPoleNotAchievable => "Passive RC is limited to a real first-order roll-off. Reduce As/transition steepness, increase fs/fp separation, or select Passive LC or Active Filter.",
            FilterSupportReasonCode.RcPassbandLossTooHigh => "Increase RL, reduce Rs, or select Passive LC/Active Filter when low insertion loss is required.",
            FilterSupportReasonCode.RcBandPassCornersNotAchievable => "Widen the passband/corner separation, relax attenuation, or select Passive LC/Active Filter for a steeper band-pass.",
            FilterSupportReasonCode.TwinTNotchDepthInsufficient => "Use E96/tighter matching, reduce source loading, increase RL, or select Passive LC/Active Bainter for a deeper controlled stopband.",
            FilterSupportReasonCode.LcPrototypeNotPositiveReal or FilterSupportReasonCode.LcLadderExtractionProducedNegativeElement => "Choose a supported family/order, or use the verified active-filter realization.",
            FilterSupportReasonCode.ResponseUnavailable => "Choose the response-specific v1 topology shown by the implementation selector.",
            FilterSupportReasonCode.BainterNotchDepthInsufficientWithSelectedSeries => "Use E96, tighter component tolerance, theoretical values, or a lower notch-depth requirement.",
            FilterSupportReasonCode.FinalCircuitSpecificationNotMet => "Use automatic order, specify at least the calculated required order, or choose tighter standard-value constraints.",
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
            return request.ResponseType switch
            {
                FilterResponseType.LowPass => FilterSupportDecision.Supported("Topology-based loaded first-order RC Low-Pass synthesis is available."),
                FilterResponseType.HighPass => FilterSupportDecision.Supported("Topology-based loaded first-order RC High-Pass synthesis is available."),
                FilterResponseType.BandPass => FilterSupportDecision.Supported("Topology-based loaded two-corner RC Band-Pass synthesis is available."),
                FilterResponseType.BandStop => FilterSupportDecision.Supported("Topology-based loaded passive Twin-T notch synthesis is available."),
                _ => FilterSupportDecision.Unsupported(FilterSupportReasonCode.ResponseUnavailable, "No passive RC topology is available for this response.")
            };
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
