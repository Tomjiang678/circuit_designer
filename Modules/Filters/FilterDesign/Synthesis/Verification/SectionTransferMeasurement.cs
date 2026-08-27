using System.Numerics;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Verification;

public sealed record SectionTransferMeasurement
{
    public required IReadOnlyList<double> NumeratorCoefficients { get; init; }
    public required IReadOnlyList<double> DenominatorCoefficients { get; init; }
    public required IReadOnlyList<Complex> Poles { get; init; }
    public required IReadOnlyList<Complex> Zeros { get; init; }
    public required double PoleAngularFrequency { get; init; }
    public double? ZeroAngularFrequency { get; init; }
    public required double Q { get; init; }
    public required double GainMagnitude { get; init; }
    public required int Polarity { get; init; }
    public required double DcGainMagnitude { get; init; }
    public required double CenterGainMagnitude { get; init; }
    public required double HighFrequencyGainMagnitude { get; init; }
    public double? TargetZeroGainMagnitude { get; init; }
    public double? NotchAttenuationDb { get; init; }
    public required double FrequencyError { get; init; }
    public required double QError { get; init; }
    public required double GainError { get; init; }
    public double? ZeroFrequencyError { get; init; }
    public required double MaximumSampleRelativeError { get; init; }
    public required int SampleCount { get; init; }
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}
