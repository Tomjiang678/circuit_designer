using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

/// <summary>
/// A two-terminal port impedance. Series R-L / R-C models are frequency-dependent
/// through the reactance specified at the reference design frequency.
/// </summary>
public sealed record PortImpedancePoint(double FrequencyHz, double ResistanceOhm, double ReactanceOhm);

public sealed record PortImpedanceModel(
    string Name,
    double ResistanceOhm,
    double ReactanceOhm,
    PortImpedanceType Type,
    double ReferenceFrequencyHz = 0,
    IReadOnlyList<PortImpedancePoint>? VnaData = null)
{
    public bool IsComplex => Type == PortImpedanceType.Vna || Math.Abs(ReactanceOhm) > 1e-12;
    public bool IsFrequencyDependent => Type is PortImpedanceType.SeriesRL or PortImpedanceType.SeriesRC or PortImpedanceType.Vna;

    public PortImpedanceModel WithReferenceFrequency(double frequencyHz) => this with { ReferenceFrequencyHz = frequencyHz };

    public double EquivalentInductanceHenry => Type == PortImpedanceType.SeriesRL
        ? ReactanceOhm / (2 * Math.PI * ReferenceFrequencyHz)
        : 0;

    public double EquivalentCapacitanceFarad => Type == PortImpedanceType.SeriesRC
        ? -1 / (2 * Math.PI * ReferenceFrequencyHz * ReactanceOhm)
        : 0;

    public Complex ImpedanceAt(double frequencyHz)
    {
        if (!(frequencyHz > 0) || !double.IsFinite(frequencyHz)) throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        if (Type == PortImpedanceType.Vna) return InterpolateVna(frequencyHz);
        var omega = 2 * Math.PI * frequencyHz;
        return Type switch
        {
            PortImpedanceType.PureResistance => new Complex(ResistanceOhm, 0),
            PortImpedanceType.SeriesRL => new Complex(ResistanceOhm, omega * EquivalentInductanceHenry),
            PortImpedanceType.SeriesRC => new Complex(ResistanceOhm, -1 / (omega * EquivalentCapacitanceFarad)),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public Complex ReferenceImpedance => Type switch
    {
        PortImpedanceType.PureResistance => new Complex(ResistanceOhm, 0),
        PortImpedanceType.SeriesRL => new Complex(ResistanceOhm, 2 * Math.PI * ReferenceFrequencyHz * EquivalentInductanceHenry),
        PortImpedanceType.SeriesRC => new Complex(ResistanceOhm, -1 / (2 * Math.PI * ReferenceFrequencyHz * EquivalentCapacitanceFarad)),
            PortImpedanceType.Vna => InterpolateVna(ReferenceFrequencyHz),
            _ => throw new ArgumentOutOfRangeException()
    };

    public string ModelDescription => Type switch
    {
        PortImpedanceType.PureResistance => "pure resistance, frequency independent",
        PortImpedanceType.SeriesRL => $"series R-L, L={Engineering(EquivalentInductanceHenry)}H derived from X at the design frequency",
        PortImpedanceType.SeriesRC => $"series R-C, C={EquivalentCapacitanceFarad:G6}F derived from X at the design frequency",
        PortImpedanceType.Vna => VnaData is { Count: > 0 } table ? $"measured VNA table, {table.Count} points, {table[0].FrequencyHz:G6}..{table[^1].FrequencyHz:G6} Hz" : "measured VNA table (no points)",
        _ => string.Empty
    };

    private Complex InterpolateVna(double frequencyHz)
    {
        if (VnaData is not { Count: >= 2 } points)
            throw new InvalidOperationException($"{Name} has no usable VNA table.");
        if (!(frequencyHz > 0) || !double.IsFinite(frequencyHz))
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        var first = points[0].FrequencyHz;
        var last = points[^1].FrequencyHz;
        if (frequencyHz < first || frequencyHz > last)
            throw new InvalidOperationException($"{Name} VNA data covers {first:G6}..{last:G6} Hz; {frequencyHz:G6} Hz is outside that range.");
        for (var index = 1; index < points.Count; index++)
        {
            if (frequencyHz > points[index].FrequencyHz) continue;
            var previous = points[index - 1];
            var fraction = frequencyHz <= previous.FrequencyHz ? 0 :
                (frequencyHz - previous.FrequencyHz) / (points[index].FrequencyHz - previous.FrequencyHz);
            return new Complex(
                previous.ResistanceOhm + (points[index].ResistanceOhm - previous.ResistanceOhm) * fraction,
                previous.ReactanceOhm + (points[index].ReactanceOhm - previous.ReactanceOhm) * fraction);
        }
        return new Complex(points[^1].ResistanceOhm, points[^1].ReactanceOhm);
    }
    private static string Engineering(double value) => value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record SourceSignal(double Value, SignalLevelUnit Unit)
{
    public double AvailablePowerWatt(double sourceResistanceOhm) => Unit switch
    {
        SignalLevelUnit.RmsVoltage => Value * Value / (4 * sourceResistanceOhm),
        SignalLevelUnit.AvailablePowerDbm => Math.Pow(10, (Value - 30) / 10),
        SignalLevelUnit.AvailablePowerWatt => Value,
        _ => throw new ArgumentOutOfRangeException()
    };

    public double TheveninRmsVoltage(double sourceResistanceOhm) => Unit switch
    {
        SignalLevelUnit.RmsVoltage => Value,
        _ => Math.Sqrt(4 * sourceResistanceOhm * AvailablePowerWatt(sourceResistanceOhm))
    };

    public string Description => Unit switch
    {
        SignalLevelUnit.RmsVoltage => $"{Value:G6} V RMS Thevenin source",
        SignalLevelUnit.AvailablePowerDbm => $"{Value:G6} dBm available source power",
        SignalLevelUnit.AvailablePowerWatt => $"{Value:G6} W available source power",
        _ => string.Empty
    };
}

public sealed record AttenuationSpecification(
    double TargetAttenuationDb = 6,
    double AllowedAttenuationErrorDb = .5,
    double MinimumInputReturnLossDb = 20,
    double MinimumOutputReturnLossDb = 20,
    double? MaximumInputPowerWatt = null)
{
    public bool IsBypass => TargetAttenuationDb <= AllowedAttenuationErrorDb && TargetAttenuationDb <= 1e-9;
}

public sealed record MatchingSpecification(
    double TargetInputReturnLossDb = 20,
    double? TargetOutputReturnLossDb = 20,
    double MaximumInsertionLossDb = 1,
    double TargetBandwidthHz = 0);

public sealed record MatchingBand(
    double StartFrequencyHz,
    double StopFrequencyHz,
    double TargetInputReturnLossDb = 20,
    double? TargetOutputReturnLossDb = 20,
    double MaximumInsertionLossDb = 1,
    double Weight = 1)
{
    public bool IsPoint => Math.Abs(StopFrequencyHz - StartFrequencyHz) <= Math.Max(1, StartFrequencyHz) * 1e-12;
    public double CenterFrequencyHz => IsPoint ? StartFrequencyHz : Math.Sqrt(StartFrequencyHz * StopFrequencyHz);
}

public sealed record MatchingPlan
{
    public MatchingDesignKind Kind { get; init; } = MatchingDesignKind.SingleFrequency;
    public IReadOnlyList<MatchingBand> Bands { get; init; } = [];
    public int MinimumPointsPerBand { get; init; } = 31;

    public IReadOnlyList<MatchingBand> EffectiveBands(AttenuationMatchingDesignRequest request) => Bands.Count > 0
        ? Bands
        : [new MatchingBand(request.DesignFrequencyHz, request.DesignFrequencyHz,
            request.Matching.TargetInputReturnLossDb, request.Matching.TargetOutputReturnLossDb,
            request.Matching.MaximumInsertionLossDb)];
}

public sealed record ComponentConstraints(
    ComponentValueMode ValueMode = ComponentValueMode.E96,
    double MinimumResistanceOhm = 1,
    double MaximumResistanceOhm = 1_000_000,
    double MaximumResistorDissipationWatt = .25,
    double MinimumCapacitanceFarad = 1e-12,
    double MaximumCapacitanceFarad = 1e-5,
    double CapacitorEsrOhm = 0,
    double MinimumInductanceHenry = 1e-9,
    double MaximumInductanceHenry = 1e-2,
    double InductorQ = 0,
    double InductorSeriesResistanceOhm = 0,
    int MaximumSearchCombinations = 20_000,
    int MaximumCandidateCount = 12,
    double? CapacitorMaximumVoltageVolt = null,
    double? CapacitorMaximumRippleCurrentAmpere = null,
    double? CapacitorSelfResonantFrequencyHz = null,
    double? InductorMaximumRmsCurrentAmpere = null,
    double? InductorSaturationCurrentAmpere = null,
    double? InductorSelfResonantFrequencyHz = null);


public sealed record TopologySelection(
    TopologySelectionMode Mode = TopologySelectionMode.Automatic,
    TopologyFamily? Family = null,
    MatchingResponse Response = MatchingResponse.LowPass,
    double? DesignQ = null,
    bool AllowMixedReactance = false)
{
    public bool IsAutomatic => Mode == TopologySelectionMode.Automatic;
}

public sealed record AttenuationMatchingDesignRequest
{
    public DesignMode Mode { get; init; } = DesignMode.Attenuation;
    public PortImpedanceModel Source { get; init; } = new("Source", 50, 0, PortImpedanceType.PureResistance);
    public PortImpedanceModel Load { get; init; } = new("Load", 75, 0, PortImpedanceType.PureResistance);
    public double DesignFrequencyHz { get; init; } = 10e6;
    public double StartFrequencyHz { get; init; } = 1e6;
    public double StopFrequencyHz { get; init; } = 100e6;
    public int SweepPoints { get; init; } = 121;
    public bool LogarithmicSweep { get; init; } = true;
    public SourceSignal Signal { get; init; } = new(1, SignalLevelUnit.RmsVoltage);
    public AttenuationSpecification Attenuation { get; init; } = new();
    public MatchingSpecification Matching { get; init; } = new();
    public MatchingPlan MatchingPlan { get; init; } = new();
    public ComponentConstraints Components { get; init; } = new();
    public TopologySelection Topology { get; init; } = new();
    public OptimizationObjective Objective { get; init; } = OptimizationObjective.Balanced;
    public int? MaximumComponentCount { get; init; }
}

public sealed record AttenuationMatchingComponent(
    string Reference,
    ComponentType Type,
    double IdealValue,
    double SelectedValue,
    string Unit,
    double ErrorPercent,
    double DissipationWatt,
    string Rating)
{
    public string TypeName => Type switch
    {
        ComponentType.Resistor => "Resistor",
        ComponentType.Capacitor => "Capacitor",
        ComponentType.Inductor => "Inductor",
        _ => Type.ToString()
    };
}

public sealed record AttenuationMatchingDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Description,
    string? Parameter = null,
    double? ActualValue = null,
    string? ExpectedRange = null,
    string? SuggestedAction = null);

public sealed record AcSweepPoint(
    double FrequencyHz,
    double TransducerGain,
    double TransducerGainDb,
    double AttenuationDb,
    double InputReturnLossDb,
    double OutputReturnLossDb,
    Complex InputImpedance,
    Complex OutputImpedance,
    Complex InputReflectionCoefficient,
    Complex OutputReflectionCoefficient,
    double? VswrIn,
    double? VswrOut);

public sealed record AcSweepResult(
    IReadOnlyList<AcSweepPoint> Points,
    double? LowerBandEdgeHz,
    double? UpperBandEdgeHz,
    double? BandwidthHz,
    double? LoadedQ,
    bool BandwidthLimitedBySweep,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<MatchingBandMetrics> BandMetrics { get; init; } = [];
}

public sealed record MatchingBandMetrics(
    int BandIndex,
    double StartFrequencyHz,
    double StopFrequencyHz,
    double WorstInputReturnLossDb,
    double WorstOutputReturnLossDb,
    double MaximumInsertionLossDb,
    double WorstFrequencyHz,
    bool Passed);

public sealed record NetworkMetrics
{
    public required double FrequencyHz { get; init; }
    public Complex SourceImpedance { get; init; }
    public Complex LoadImpedance { get; init; }
    public Complex InputImpedance { get; init; }
    public Complex OutputImpedance { get; init; }
    public Complex InputReflectionCoefficient { get; init; }
    public Complex OutputReflectionCoefficient { get; init; }
    public double TransducerGain { get; init; }
    public double TransducerGainDb => 10 * Math.Log10(Math.Max(TransducerGain, 1e-30));
    public double AttenuationDb => -TransducerGainDb;
    public double InsertionLossDb => -TransducerGainDb;
    public double? RelativeInsertionLossDb { get; init; }
    public double? MatchedPortAttenuationDb { get; init; }
    public double? S11Db { get; init; }
    public double? S21Db { get; init; }
    public double? S22Db { get; init; }
    public double? S12Db { get; init; }
    public double InputReturnLossDb { get; init; }
    public double OutputReturnLossDb { get; init; }
    public double? VswrIn { get; init; }
    public double? VswrOut { get; init; }
    public double? BandwidthHz { get; init; }
    public double? LowerBandEdgeHz { get; init; }
    public double? UpperBandEdgeHz { get; init; }
    public double? LoadedQ { get; init; }
    public double MaximumResistorDissipationWatt { get; init; }
    public double TotalResistorDissipationWatt { get; init; }
    public int ComponentCount { get; init; }
    public bool IsPassive { get; init; } = true;
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class TopologyCandidate
{
    public required string Id { get; init; }
    public required string TopologyName { get; init; }
    public required TopologyFamily Family { get; init; }
    public NetworkTopologySpec? TopologySpec { get; init; }
    public MatchingResponse Response { get; init; } = MatchingResponse.LowPass;
    public required CircuitGraph Graph { get; init; }
    public required IReadOnlyList<AttenuationMatchingComponent> Components { get; init; }
    public required NetworkMetrics Metrics { get; init; }
    public required AcSweepResult Sweep { get; init; }
    public DesignStatus Status { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public List<AttenuationMatchingDiagnostic> Diagnostics { get; init; } = [];
    public double Score { get; set; }
    public int Rank { get; set; }
    public bool IsStandardValueDesign { get; init; }
    public string? TopologyDescription { get; init; }
    public string? LimitNote { get; init; }
}

public sealed record AttenuationMatchingDesignResult
{
    public required AttenuationMatchingDesignRequest Request { get; init; }
    public DesignStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<TopologyCandidate> Candidates { get; init; } = [];
    public int SelectedIndex { get; init; }
    public IReadOnlyList<AttenuationMatchingDiagnostic> Diagnostics { get; init; } = [];
    public DateTimeOffset DesignedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public TopologyCandidate? SelectedCandidate => SelectedIndex >= 0 && SelectedIndex < Candidates.Count ? Candidates[SelectedIndex] : null;
    public bool Success => Status != DesignStatus.Failed && SelectedCandidate is not null;
}

public sealed record DesignCapability(
    DesignStatus Status,
    string Code,
    string Message,
    string SuggestedAction = "");
