namespace CircuitModuleDesigner.Core.Circuit;

public enum ComparatorOutputBehavior { PushPull, OpenCollector }
public enum LogicGateFunction { And, Or, NotAAndB, AOrNotB }

/// <summary>Static comparator abstraction used by synthesis, verification, schematic and export.</summary>
public sealed record GenericComparatorModel(
    double OutputLowVoltage,
    double OutputHighVoltage,
    ComparatorOutputBehavior OutputBehavior = ComparatorOutputBehavior.PushPull)
{
    public GenericComparatorModel Validate()
    {
        if (!double.IsFinite(OutputLowVoltage) || !double.IsFinite(OutputHighVoltage) || OutputLowVoltage >= OutputHighVoltage)
            throw new ArgumentOutOfRangeException(nameof(OutputHighVoltage), "Comparator output low/high levels must be finite and ordered.");
        return this;
    }
}
