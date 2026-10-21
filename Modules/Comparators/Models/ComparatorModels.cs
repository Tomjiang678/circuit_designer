using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Comparators.Models;

public enum ComparatorDesignerKind { SchmittTrigger, WindowComparator, RelaxationOscillator }
public enum SchmittTopology { Inverting, NonInverting }
public enum WindowOutputMode { InsideWindowHigh, OutsideWindowHigh }
public enum ComparatorDesignStatus { Pass, Warning, Failed }

public sealed record ComparatorDesignRequest
{
    public ComparatorDesignerKind Kind { get; init; } = ComparatorDesignerKind.SchmittTrigger;
    public SchmittTopology Topology { get; init; } = SchmittTopology.Inverting;
    public WindowOutputMode OutputMode { get; init; } = WindowOutputMode.InsideWindowHigh;
    public double LowerThresholdVoltage { get; init; } = 1.5;
    public double UpperThresholdVoltage { get; init; } = 3.5;
    public double LowerBoundaryHysteresisVoltage { get; init; }
    public double UpperBoundaryHysteresisVoltage { get; init; }
    public double SupplyHighVoltage { get; init; } = 5;
    public double SupplyLowVoltage { get; init; }
    public double OutputHighVoltage { get; init; } = 4.9;
    public double OutputLowVoltage { get; init; } = .1;
    public ComparatorOutputBehavior OutputBehavior { get; init; } = ComparatorOutputBehavior.PushPull;
    public bool OptimizeStandardValues { get; init; } = true;
    public StandardComponentSeries ResistorSeries { get; init; } = StandardComponentSeries.E96;
    public double MinimumResistanceOhm { get; init; } = 1_000;
    public double MaximumResistanceOhm { get; init; } = 1_000_000;
    public double TargetFrequencyHz { get; init; } = 1_000;
    public double DutyCyclePercent { get; init; } = 50;
}

public sealed record ComparatorSelectedResistor(string Reference, double ContinuousOhm, double SelectedOhm);
public sealed record ComparatorSelectedCapacitor(string Reference, double ContinuousFarad, double SelectedFarad);
public sealed record ComparatorThresholds(double Lower, double Upper)
{
    public double Width => Upper - Lower;
    public double Center => (Lower + Upper) / 2;
}

public sealed record WindowBoundaryThresholds(double LowerFalling, double LowerRising, double UpperFalling, double UpperRising)
{
    public double RisingWindowWidth => UpperRising - LowerRising;
    public double FallingWindowWidth => UpperFalling - LowerFalling;
}

public sealed record ComparatorVerificationMetric(string Name, double Target, double Actual, double ErrorVoltage, bool Pass,string Unit="V");

public sealed record RelaxationOscillatorPerformance(
    double TargetFrequencyHz,
    double ActualFrequencyHz,
    double TargetDutyCyclePercent,
    double ActualDutyCyclePercent,
    double HighTimeSeconds,
    double LowTimeSeconds);

public sealed class ComparatorDesignResult
{
    public required ComparatorDesignRequest Request { get; init; }
    public ComparatorDesignStatus Status { get; init; }
    public bool Success => Status != ComparatorDesignStatus.Failed;
    public string Message { get; init; } = string.Empty;
    public CircuitGraph? CircuitGraph { get; init; }
    public ComparatorThresholds? ActualThresholds { get; init; }
    public WindowBoundaryThresholds? WindowThresholds { get; init; }
    public double BiasNodeVoltage { get; init; }
    public IReadOnlyList<ComparatorSelectedResistor> Resistors { get; init; } = [];
    public IReadOnlyList<ComparatorSelectedCapacitor> Capacitors { get; init; } = [];
    public RelaxationOscillatorPerformance? Oscillator { get; init; }
    public IReadOnlyList<ComparatorVerificationMetric> Verification { get; init; } = [];
    public IReadOnlyList<string> CalculationTrace { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
