namespace CircuitModuleDesigner.Core.Circuit.Spice;

public enum SpiceNetlistDialect { GenericSpice, Ltspice, Multisim }
public enum SpiceSourceKind { Voltage, Current }
public enum SpiceOpAmpExportMode { FiniteGainVcvs, ExternalSubcircuit, Unsupported }
public enum SpiceSweepScale { Decade, Octave, Linear }

public sealed record SpiceNetAlias(string Name, string PositiveNode, string NegativeNode);
public sealed record SpiceIndependentSource(
    string Name,
    SpiceSourceKind Kind,
    string PositiveNode,
    string NegativeNode,
    double DcValue = 0,
    double? AcMagnitude = null,
    double AcPhaseDegrees = 0);

public abstract record SpiceAnalysisDirective;
public sealed record SpiceAcAnalysis(SpiceSweepScale Scale, int PointCount, double StartFrequencyHz, double StopFrequencyHz) : SpiceAnalysisDirective;
public sealed record SpiceDcAnalysis(string SourceName, double StartValue, double StopValue, double Increment) : SpiceAnalysisDirective;
public sealed record SpiceTransientAnalysis(double TimeStepSeconds, double StopTimeSeconds, double StartSavingSeconds = 0, double? MaximumTimeStepSeconds = null) : SpiceAnalysisDirective;
public sealed record SpiceOperatingPointAnalysis : SpiceAnalysisDirective;

public sealed record SpiceDiodeModelDefinition(
    string Name,
    double SaturationCurrentAmpere = 1e-14,
    double EmissionCoefficient = 1,
    double? SeriesResistanceOhm = null);

/// <summary>
/// Two-terminal behavioral ideal-diode approximation used when the verified model
/// is an ideal conducting/blocking state rather than a Shockley junction.
/// </summary>
public sealed record SpiceIdealDiodeDefinition(
    double OnResistanceOhm = 1e-3,
    double OffResistanceOhm = 1e12);

public sealed record SpiceLibraryReference(string Path, string? EntryName = null);

/// <summary>
/// Describes an external op-amp subcircuit. Pin order is deliberately explicit because
/// SPICE X-instance pin order must exactly match the referenced .SUBCKT declaration.
/// </summary>
public sealed record SpiceOpAmpSubcircuit(
    string Name,
    IReadOnlyList<string> PinOrder,
    string? IncludePath = null,
    IReadOnlyList<string>? EmbeddedBodyLines = null);

public sealed class CircuitSpiceExportOptions
{
    public CircuitSpiceExportOptions(string title) => Title = title;

    // Compatibility constructor used by the existing Filter export path.
    public CircuitSpiceExportOptions(
        string title,
        double startFrequencyHz,
        double stopFrequencyHz,
        int PointsPerDecade = 40,
        double OpAmpOpenLoopGain = 1e6,
        double? SourceResistanceOhm = null,
        double? LoadResistanceOhm = null)
    {
        Title = title;
        this.OpAmpOpenLoopGain = OpAmpOpenLoopGain;
        this.SourceResistanceOhm = SourceResistanceOhm;
        this.LoadResistanceOhm = LoadResistanceOhm;
        HighImpedanceOutputLoadOhm = LoadResistanceOhm is null ? 1e9 : null;
        Analyses = [new SpiceAcAnalysis(SpiceSweepScale.Decade, PointsPerDecade, startFrequencyHz, stopFrequencyHz)];
        OutputExpressions = ["VM(VOUT)", "VP(VOUT)"];
        Sources = [new SpiceIndependentSource("V_INPUT", SpiceSourceKind.Voltage, SourceResistanceOhm is > 0 ? "VSRC" : "VIN", "0", AcMagnitude: 1)];
    }

    public string Title { get; init; }
    public SpiceNetlistDialect Dialect { get; init; } = SpiceNetlistDialect.GenericSpice;
    public IReadOnlyList<string> AdditionalComments { get; init; } = [];
    public IReadOnlyList<SpiceIndependentSource> Sources { get; init; } = [];
    public IReadOnlyList<SpiceNetAlias> NetAliases { get; init; } = [];
    public IReadOnlyList<SpiceAnalysisDirective> Analyses { get; init; } = [];
    public IReadOnlyList<string> OutputExpressions { get; init; } = [];
    public IReadOnlyDictionary<string, double> Parameters { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<string> LibraryIncludes { get; init; } = [];
    public IReadOnlyList<SpiceLibraryReference> Libraries { get; init; } = [];
    public double OpAmpOpenLoopGain { get; init; } = 1e6;
    public SpiceOpAmpExportMode OpAmpMode { get; init; } = SpiceOpAmpExportMode.FiniteGainVcvs;
    public SpiceOpAmpSubcircuit? OpAmpSubcircuit { get; init; }
    public SpiceDiodeModelDefinition? DiodeModel { get; init; }
    public SpiceIdealDiodeDefinition? IdealDiodeModel { get; init; }
    public double? TemperatureCelsius { get; init; }
    public double? SourceResistanceOhm { get; init; }
    public double? LoadResistanceOhm { get; init; }
    public double? HighImpedanceOutputLoadOhm { get; init; }
}
