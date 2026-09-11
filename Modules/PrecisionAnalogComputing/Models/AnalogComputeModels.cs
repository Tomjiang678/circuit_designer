using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

public enum AnalogComputeOperationType
{
    GeneralLinearCombination,
    VoltageRangeMapper,
    PrecisionHalfWaveRectifier,
    PrecisionFullWaveRectifier,
    LogarithmicAmplifier,
    ExponentialAmplifier,
    PracticalIntegrator,
    PracticalDifferentiator
}

public enum AnalogCapabilityLevel { Supported, Unsupported }

public enum AnalogDesignFailureStage { None, SpecificationValidation, Synthesis, IndependentVerification }

public enum AnalogCapabilityReasonCode
{
    None,
    InvalidInput,
    SupplyRailsInvalid,
    ComponentRangeImpossible,
    StandardValueAccuracyInsufficient,
    CircuitVerificationFailed,
    GbBandwidthInsufficient,
    SlewRateInsufficient,
    JunctionCurrentOutOfRange,
    TopologyCapabilityUnsupported,
    InternalError
}

public enum AnalogStandardSeries { E12, E24, E48, E96 }

public enum DiodeBehaviorModel { IdealSwitch, ShockleyJunction }
public enum HalfWavePolarity { Positive, Negative }
public enum RectifierOutputPolarity { Positive, Negative }

public sealed record JunctionModelParameters
{
    public DiodeBehaviorModel Behavior { get; init; } = DiodeBehaviorModel.ShockleyJunction;
    public double SaturationCurrentAmpere { get; init; } = 1e-12;
    public double EmissionCoefficient { get; init; } = 1;
    public double TemperatureCelsius { get; init; } = 25;
    public double ThermalVoltage => 8.617333262e-5 * (TemperatureCelsius + 273.15);

    public double Current(double junctionVoltage) =>
        SaturationCurrentAmpere * (Math.Exp(Math.Clamp(junctionVoltage / (EmissionCoefficient * ThermalVoltage), -700, 700)) - 1);
}

public sealed record AnalogInputTerm(
    string Name,
    double Coefficient,
    double MinimumVoltage = -1,
    double MaximumVoltage = 1,
    double NominalVoltage = 0);

public sealed record ComponentSelectionConstraints
{
    public AnalogStandardSeries ResistorSeries { get; init; } = AnalogStandardSeries.E96;
    public AnalogStandardSeries CapacitorSeries { get; init; } = AnalogStandardSeries.E96;
    public double ResistorMinimumOhm { get; init; } = 1_000;
    public double ResistorMaximumOhm { get; init; } = 1_000_000;
    public double CapacitorMinimumFarad { get; init; } = 10e-12;
    public double CapacitorMaximumFarad { get; init; } = 10e-6;
    public bool OptimizeStandardValues { get; init; } = true;
    public bool BiasCompensation { get; init; } = true;
}

public sealed record OpAmpPracticalConstraints
{
    public bool UseIdealOpAmp { get; init; } = true;
    public double GainBandwidthHz { get; init; } = double.PositiveInfinity;
    public double SlewRateVoltPerSecond { get; init; } = double.PositiveInfinity;
    public double OutputSwingHeadroomVoltage { get; init; }
}

public sealed record AnalogComputeDesignRequest
{
    public AnalogComputeOperationType Operation { get; init; } = AnalogComputeOperationType.GeneralLinearCombination;
    public IReadOnlyList<AnalogInputTerm> Inputs { get; init; } = [new("U1", 1)];
    public double OutputOffsetVoltage { get; init; }
    public double SupplyVccVoltage { get; init; } = 15;
    public double SupplyVeeVoltage { get; init; } = -15;
    public double MaximumFunctionErrorPercent { get; init; } = 1;
    public ComponentSelectionConstraints Components { get; init; } = new();
    public OpAmpPracticalConstraints OpAmp { get; init; } = new();

    // Range Mapper endpoints. They are used only when Operation is VoltageRangeMapper.
    public double InputLowVoltage { get; init; } = 0.5;
    public double InputHighVoltage { get; init; } = 4.5;
    public double OutputLowVoltage { get; init; }
    public double OutputHighVoltage { get; init; } = 3.3;

    // Operation-specific engineering parameters.
    public double GainK { get; init; } = 1;
    public HalfWavePolarity HalfWavePolarity { get; init; } = HalfWavePolarity.Positive;
    public RectifierOutputPolarity OutputPolarity { get; init; } = RectifierOutputPolarity.Positive;
    public double MinimumInputVoltage { get; init; } = -5;
    public double MaximumInputVoltage { get; init; } = 5;
    public double MaximumExpectedFrequencyHz { get; init; } = 1_000;
    public double ReferenceVoltage { get; init; } = 1;
    public double LogScaleVoltage { get; init; } = .1;
    public double IntegrationConstantPerSecond { get; init; } = 1_000;
    public double DifferentiationConstantSecond { get; init; } = .001;
    public double DifferentiationUsefulUpperFrequencyHz { get; init; }
    public double LowerCornerHz { get; init; } = 10;
    public double UpperCornerHz { get; init; } = 10_000;
    public double MaximumJunctionCurrentAmpere { get; init; } = .01;
    public JunctionModelParameters Junction { get; init; } = new();
}

public sealed record AnalogComputeCapabilityEvaluation(
    AnalogCapabilityLevel Level,
    bool IsSupported,
    AnalogCapabilityReasonCode ReasonCode,
    string Message,
    string? SuggestedAlternative = null)
{
    public static AnalogComputeCapabilityEvaluation Supported(string message = "Design capability verified.") =>
        new(AnalogCapabilityLevel.Supported, true, AnalogCapabilityReasonCode.None, message);

    public static AnalogComputeCapabilityEvaluation Failure(AnalogCapabilityReasonCode reason, string message, string? alternative = null) =>
        new(AnalogCapabilityLevel.Unsupported, false, reason, message, alternative);
}

public sealed record SelectedAnalogComponent(
    string Reference,
    string Role,
    string Quantity,
    double TheoreticalValue,
    double SelectedValue,
    string Unit,
    AnalogStandardSeries? Series = null);

public sealed record VerificationMetric(
    string Name,
    double Target,
    double Actual,
    double Error,
    double Tolerance,
    bool Pass,
    string Unit = "");

public sealed record CalculationRecord(string Title, string Expression, string Result, string? Note = null);

public sealed record AnalogAnalysisPoint(
    double Input,
    double Target,
    double Actual,
    bool IsSupplyLimited = false,
    double? Theoretical = null,
    double? TargetPhaseDegrees = null,
    double? ActualPhaseDegrees = null);

public sealed record AnalogAnalysisResult(
    string AnalysisType,
    IReadOnlyList<AnalogAnalysisPoint> Points,
    IReadOnlyList<VerificationMetric> Metrics,
    DynamicTimeDomainAnalysisResult? TimeDomain = null);

public sealed record DynamicTimeDomainPoint(
    double Periods,
    double InputVoltage,
    double TargetOutputVoltage,
    double ActualOutputVoltage);

public sealed record DynamicTimeDomainAnalysisResult(
    string InputWaveform,
    string OutputWaveform,
    double DemoFrequencyHz,
    double InputAmplitudeVoltage,
    double TargetOutputAmplitudeVoltage,
    double ActualOutputAmplitudeVoltage,
    double AmplitudeErrorPercent,
    IReadOnlyList<DynamicTimeDomainPoint> Points);

public sealed record AnalogComputeDesignResult
{
    public required bool Success { get; init; }
    public required AnalogComputeCapabilityEvaluation Capability { get; init; }
    public required AnalogComputeDesignRequest Request { get; init; }
    public AnalogDesignFailureStage FailureStage { get; init; }
    public string TopologyName { get; init; } = "";
    public string TargetRelation { get; init; } = "";
    public CircuitGraph? CircuitGraph { get; init; }
    public IReadOnlyList<SelectedAnalogComponent> Components { get; init; } = [];
    public IReadOnlyList<VerificationMetric> VerificationMetrics { get; init; } = [];
    public IReadOnlyList<CalculationRecord> Calculations { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public AnalogAnalysisResult? Analysis { get; init; }
    public double RequiredVccVoltage { get; init; }
    public double RequiredVeeVoltage { get; init; }
}

public static class PrecisionAnalogV1Status
{
    public const bool IsFrozen = true;
    public const string SupportedOperations = "Linear Combination; Range Mapper; Half-Wave; Full-Wave; Log; Antilog; Integrator; Differentiator.";
}
