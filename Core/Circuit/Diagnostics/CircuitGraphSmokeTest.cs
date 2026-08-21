using CircuitModuleDesigner.Core.Schematic.Symbols;

namespace CircuitModuleDesigner.Core.Circuit.Diagnostics;

public static class CircuitGraphSmokeTest
{
    public static void Run()
    {
        var builder = new CircuitBuilder();
        var resistor = builder.Add(ComponentFactory.Resistor("R1", 10_000));
        var capacitor = builder.Add(ComponentFactory.Capacitor("C1", 10e-9));
        var inductor = builder.Add(ComponentFactory.Inductor("L1", 100e-6));
        var opAmp = builder.Add(ComponentFactory.OpAmp("U1"));

        builder.Connect("VIN", resistor.GetPin("1"));
        builder.Connect("N001", resistor.GetPin("2"), capacitor.GetPin("1"), opAmp.GetPin("IN+"));
        builder.Connect("GND", capacitor.GetPin("2"), opAmp.GetPin("IN-"), opAmp.GetPin("V-"));
        builder.Connect("VOUT", opAmp.GetPin("OUT"), inductor.GetPin("1"));
        builder.Connect("LOAD", inductor.GetPin("2"));
        builder.Connect("VCC", opAmp.GetPin("V+"));
        var graph = builder.Build();

        Require(graph.Components.Count == 4, "Expected four components.");
        Require(graph.Nets.Count == 6, "Expected six named nets.");
        Require(graph.GetNetForPin("R1", "2")?.Name == "N001", "R1.2 must belong to N001.");
        Require(graph.GetNet("N001").Pins.Count == 3, "N001 must contain three pins.");
        Require(Math.Abs(resistor.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm) - 10_000) < 1e-9, "Resistance must be stored in Ohm.");
        Require(SymbolDefinitionCatalog.Get(ComponentType.OpAmp).GetPinAnchor("V+").PinName == "V+", "OpAmp symbol must expose its supply pins.");

        ExpectFailure(() =>
        {
            var duplicateBuilder = new CircuitBuilder();
            duplicateBuilder.Add(ComponentFactory.Resistor("R1", 1_000));
            duplicateBuilder.Add(ComponentFactory.Capacitor("R1", 1e-9));
        }, "Duplicate references must be rejected.");

        ExpectFailure(() =>
        {
            var conflictBuilder = new CircuitBuilder();
            var r = conflictBuilder.Add(ComponentFactory.Resistor("R1", 1_000));
            conflictBuilder.Connect("A", r.GetPin("1"));
            conflictBuilder.Connect("B", r.GetPin("1"));
        }, "A pin must not belong to two nets.");

        ExpectFailure(() => graph.GetPin("U1", "UNKNOWN"), "Unknown pins must be rejected.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void ExpectFailure(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }
}
