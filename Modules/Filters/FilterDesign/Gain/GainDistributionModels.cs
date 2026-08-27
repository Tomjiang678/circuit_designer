using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Gain;

public enum FilterStagePolarity
{
    NonInverting = 1,
    Inverting = -1
}

public sealed record SectionGainAssignment(
    int SectionIndex,
    double BaseTransferGain,
    double TargetTopologyGain,
    double InterstageScale,
    FilterStagePolarity Polarity);

public sealed record GainDistributionPlan(
    double RequestedOverallGain,
    IReadOnlyList<AnalogFilterSection> Sections,
    IReadOnlyList<SectionGainAssignment> Assignments,
    double OutputScale,
    FilterStagePolarity ExpectedOverallPolarity);

public sealed record GainDistributionSummary(
    double RequestedOverallGain,
    double RawCascadeGainMagnitude,
    double InterstageScaleProduct,
    double ActualPassbandPeakGain,
    FilterStagePolarity ExpectedOverallPolarity,
    double ReferencePhaseDegrees,
    IReadOnlyList<string> Warnings);
