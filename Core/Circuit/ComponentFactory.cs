namespace CircuitModuleDesigner.Core.Circuit;

public static class ComponentFactory
{
    private static readonly string[] TwoTerminalPins = ["1", "2"];
    private static readonly string[] OpAmpPins = ["IN+", "IN-", "OUT", "V+", "V-"];
    private static readonly string[] DiodePins = ["A", "K"];

    public static CircuitComponent Resistor(string reference, double resistanceOhm) =>
        CreateSingleValueComponent(reference, ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, resistanceOhm, TwoTerminalPins);

    public static CircuitComponent Capacitor(string reference, double capacitanceFarad) =>
        CreateSingleValueComponent(reference, ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, capacitanceFarad, TwoTerminalPins);

    public static CircuitComponent Inductor(string reference, double inductanceHenry) =>
        CreateSingleValueComponent(reference, ComponentType.Inductor, ComponentParameterNames.InductanceHenry, inductanceHenry, TwoTerminalPins);

    public static CircuitComponent OpAmp(string reference) =>
        new(reference, ComponentType.OpAmp, ComponentParameters.Empty, OpAmpPins);

    public static CircuitComponent Diode(string reference) =>
        new(reference, ComponentType.Diode, ComponentParameters.Empty, DiodePins);

    private static CircuitComponent CreateSingleValueComponent(
        string reference,
        ComponentType type,
        string parameterName,
        double value,
        IEnumerable<string> pins) =>
        new(
            reference,
            type,
            new ComponentParameters([new KeyValuePair<string, double>(parameterName, value)]),
            pins);
}
