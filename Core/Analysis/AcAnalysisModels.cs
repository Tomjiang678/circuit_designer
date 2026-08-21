namespace CircuitModuleDesigner.Core.Analysis;

public sealed record AcAnalysisOptions(double StartFrequencyHz, double StopFrequencyHz, int PointsPerDecade = 40, double OpAmpOpenLoopGain = 1e6,
    IReadOnlyList<double>? AdditionalFrequenciesHz = null, double MagnitudeReferenceLinear = 1,
    string MagnitudeReferenceDescription = "1 V/V source reference");

public sealed record FrequencyResponsePoint(double FrequencyHz, double MagnitudeLinear, double MagnitudeDb, double PhaseDegrees);

public sealed class AcAnalysisResult
{
    public required IReadOnlyList<FrequencyResponsePoint> Points { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public double MagnitudeReferenceLinear { get; init; } = 1;
    public string MagnitudeReferenceDescription { get; init; } = "1 V/V source reference";
    public double StartFrequencyHz => Points.Count == 0 ? 0 : Points[0].FrequencyHz;
    public double StopFrequencyHz => Points.Count == 0 ? 0 : Points[^1].FrequencyHz;
}
