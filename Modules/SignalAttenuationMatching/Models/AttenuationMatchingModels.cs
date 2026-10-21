using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

/// <summary>
/// A two-terminal port impedance. Series R-L / R-C models are frequency-dependent
/// through the reactance specified at the reference design frequency. Custom R+jX
/// is a single-frequency model and is explicitly reported as such.
/// </summary>
public sealed record PortImpedanceModel(
    string Name,
    double ResistanceOhm,
    double ReactanceOhm,
    PortImpedanceType Type,
    double ReferenceFrequencyHz = 0)
{
    public bool IsComplex => Math.Abs(ReactanceOhm) > 1e-12;

    public PortImpedanceModel WithReferenceFrequency(double frequencyHz) => this with { ReferenceFrequencyHz = frequencyHz };

    public Complex ImpedanceAt(double frequencyHz)
    {
        if (!(frequencyHz > 0) || !double.IsFinite(frequencyHz)) throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        return Type switch
        {
            PortImpedanceType.PureResistance => new Complex(ResistanceOhm, 0),
            PortImpedanceType.Custom => new Complex(ResistanceOhm, ReactanceOhm),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public Complex ReferenceImpedance => Type switch
    {
        PortImpedanceType.PureResistance => new Complex(ResistanceOhm, 0),
        PortImpedanceType.Custom => new Complex(ResistanceOhm, ReactanceOhm),
        _ => throw new ArgumentOutOfRangeException()
    };

    public string ModelDescription => Type switch
    {
        PortImpedanceType.PureResistance => "pure resistance, frequency independent",
        PortImpedanceType.Custom => "custom R+jX held constant over the sweep (single-frequency model)",
        _ => string.Empty
    };

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
    int MaximumCandidateCount = 12);


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
    IReadOnlyList<string> Warnings);

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
