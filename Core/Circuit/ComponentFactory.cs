namespace CircuitModuleDesigner.Core.Circuit;

public static class ComponentFactory
{
    private static readonly string[] TwoTerminalPins = ["1", "2"];
    private static readonly string[] OpAmpPins = ["IN+", "IN-", "OUT", "V+", "V-"];
    private static readonly string[] DiodePins = ["A", "K"];
    private static readonly string[] ComparatorPins = ["IN+", "IN-", "OUT", "V+", "V-"];
    private static readonly string[] LogicGatePins = ["A", "B", "OUT", "V+", "V-"];
    private static readonly string[] MosfetPins = ["G", "D", "S"];
    private static readonly string[] AnalogSwitchPins = ["A", "B", "CTRL"];

    public static CircuitComponent Resistor(string reference, double resistanceOhm) =>
        CreateSingleValueComponent(reference, ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, resistanceOhm, TwoTerminalPins);

    public static CircuitComponent Capacitor(string reference, double capacitanceFarad) =>
        CreateSingleValueComponent(reference, ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, capacitanceFarad, TwoTerminalPins);

    public static CircuitComponent Inductor(string reference, double inductanceHenry) =>
        CreateSingleValueComponent(reference, ComponentType.Inductor, ComponentParameterNames.InductanceHenry, inductanceHenry, TwoTerminalPins);

    public static CircuitComponent OpAmp(string reference) =>
        new(reference, ComponentType.OpAmp, ComponentParameters.Empty, OpAmpPins);

    public static CircuitComponent SaturatingOpAmp(string reference, double outputLowVoltage, double outputHighVoltage) =>
        outputLowVoltage < outputHighVoltage && double.IsFinite(outputLowVoltage) && double.IsFinite(outputHighVoltage)
            ? new(reference, ComponentType.OpAmp,
                new ComponentParameters([
                    new(ComponentParameterNames.OutputHighVoltage, outputHighVoltage),
                    new(ComponentParameterNames.OutputLowVoltage, outputLowVoltage)]), OpAmpPins)
            : throw new ArgumentOutOfRangeException(nameof(outputHighVoltage), "Op-amp output low/high levels must be finite and ordered.");

    public static CircuitComponent Diode(string reference) =>
        new(reference, ComponentType.Diode, ComponentParameters.Empty, DiodePins);

    public static CircuitComponent Comparator(string reference, GenericComparatorModel model) =>
        new(reference, ComponentType.Comparator,
            new ComponentParameters([
                new(ComponentParameterNames.OutputHighVoltage, model.Validate().OutputHighVoltage),
                new(ComponentParameterNames.OutputLowVoltage, model.OutputLowVoltage),
                new(ComponentParameterNames.OutputBehavior, (double)model.OutputBehavior)]), ComparatorPins);

    public static CircuitComponent LogicGate(string reference, LogicGateFunction function, double outputLowVoltage, double outputHighVoltage) =>
        outputLowVoltage < outputHighVoltage && double.IsFinite(outputLowVoltage) && double.IsFinite(outputHighVoltage)
        ? new(reference, ComponentType.LogicGate,
            new ComponentParameters([
                new(ComponentParameterNames.LogicFunction, (double)function),
                new(ComponentParameterNames.OutputHighVoltage, outputHighVoltage),
                new(ComponentParameterNames.OutputLowVoltage, outputLowVoltage)]), LogicGatePins)
        : throw new ArgumentOutOfRangeException(nameof(outputHighVoltage), "Logic output levels must be finite and ordered.");

    public static CircuitComponent Mosfet(string reference) =>
        new(reference, ComponentType.Mosfet, ComponentParameters.Empty, MosfetPins);

    public static CircuitComponent AnalogSwitch(string reference) =>
        new(reference, ComponentType.AnalogSwitch, ComponentParameters.Empty, AnalogSwitchPins);

    public static CircuitComponent AnalogSwitch(string reference, double onResistanceOhm) =>
        double.IsFinite(onResistanceOhm) && onResistanceOhm > 0
            ? new(reference, ComponentType.AnalogSwitch,
                new ComponentParameters([new(ComponentParameterNames.ResistanceOhm, onResistanceOhm)]), AnalogSwitchPins)
            : throw new ArgumentOutOfRangeException(nameof(onResistanceOhm), "Switch on-resistance must be finite and positive.");

    private static CircuitComponent CreateSingleValueComponent(
        string reference,
        ComponentType type,
        string parameterName,
        double value,
        IEnumerable<string> pins) =>
        double.IsFinite(value) && value > 0 ? new(
            reference,
            type,
            new ComponentParameters([new KeyValuePair<string, double>(parameterName, value)]),
            pins) : throw new ArgumentOutOfRangeException(nameof(value), "Passive component values must be finite and greater than zero.");
}
