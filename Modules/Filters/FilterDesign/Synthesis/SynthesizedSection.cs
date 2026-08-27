using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public sealed record SynthesizedComponentValue(
    string Role,
    ComponentType ComponentType,
    string ParameterName,
    double TheoreticalValue,
    double SelectedValue,
    StandardComponentSeries? Series,
    double TolerancePercent)
{
    public double RelativeError => TheoreticalValue == 0 ? 0 : (SelectedValue - TheoreticalValue) / TheoreticalValue;
}

public sealed record SynthesizedSection(
    AnalogFilterSection Target,
    IReadOnlyList<SynthesizedComponentValue> Components,
    double ActualPoleFrequencyHz,
    double ActualQ,
    double ActualGain,
    double FrequencyError,
    double QError,
    double GainError)
{
    public TopologyGainRealization? TopologyGain { get; init; }
    public double IntrinsicTopologyGain => TopologyGain?.IntrinsicTopologyGain ?? ActualGain;
    public double InterstageScale { get; init; } = 1;
    public int PolaritySign { get; init; } = 1;
    public double ActualSignedGain => PolaritySign * ActualGain * InterstageScale;
}

public sealed record TopologyGainRealization(
    double IntrinsicTopologyGain,
    double MinimumAvailableGain,
    double MaximumAvailableGain,
    double RequiredOutputOrInterstageScale,
    double ActualSectionGain);
