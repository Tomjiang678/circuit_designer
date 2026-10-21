using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Core.Schematic.Symbols;

public static class SymbolDefinitionCatalog
{
    private static readonly IReadOnlyDictionary<ComponentType, SymbolDefinition> Definitions =
        new Dictionary<ComponentType, SymbolDefinition>
        {
            [ComponentType.Resistor] = CreateTwoTerminal(ComponentType.Resistor, SymbolShape.ResistorZigZag),
            [ComponentType.Capacitor] = CreateTwoTerminal(ComponentType.Capacitor, SymbolShape.CapacitorPlates),
            [ComponentType.Inductor] = CreateTwoTerminal(ComponentType.Inductor, SymbolShape.InductorCoil),
            [ComponentType.Diode] = new SymbolDefinition(
                ComponentType.Diode,
                SymbolShape.DiodeJunction,
                new SchematicSize(80, 30),
                [
                    new SymbolPinAnchor("A", new SchematicPoint(-40, 0), SymbolPinDirection.Left),
                    new SymbolPinAnchor("K", new SchematicPoint(40, 0), SymbolPinDirection.Right)
                ]),
            [ComponentType.OpAmp] = new SymbolDefinition(
                ComponentType.OpAmp,
                SymbolShape.OpAmpTriangle,
                new SchematicSize(110, 96),
                [
                    new SymbolPinAnchor("IN+", new SchematicPoint(-55, -22), SymbolPinDirection.Left),
                    new SymbolPinAnchor("IN-", new SchematicPoint(-55, 22), SymbolPinDirection.Left),
                    new SymbolPinAnchor("OUT", new SchematicPoint(55, 0), SymbolPinDirection.Right),
                    new SymbolPinAnchor("V+", new SchematicPoint(0, -48), SymbolPinDirection.Up),
                    new SymbolPinAnchor("V-", new SchematicPoint(0, 48), SymbolPinDirection.Down)
                ]),
            [ComponentType.Comparator] = new SymbolDefinition(
                ComponentType.Comparator, SymbolShape.ComparatorTriangle, new SchematicSize(110, 96),
                [
                    new("IN+", new(-55, -22), SymbolPinDirection.Left), new("IN-", new(-55, 22), SymbolPinDirection.Left),
                    new("OUT", new(55, 0), SymbolPinDirection.Right), new("V+", new(0, -48), SymbolPinDirection.Up),
                    new("V-", new(0, 48), SymbolPinDirection.Down)
                ]),
            [ComponentType.LogicGate] = new SymbolDefinition(
                ComponentType.LogicGate, SymbolShape.LogicGate, new SchematicSize(90, 70),
                [
                    new("A", new(-45, -17), SymbolPinDirection.Left), new("B", new(-45, 17), SymbolPinDirection.Left),
                    new("OUT", new(45, 0), SymbolPinDirection.Right), new("V+", new(0, -35), SymbolPinDirection.Up),
                    new("V-", new(0, 35), SymbolPinDirection.Down)
                ]),
            [ComponentType.Mosfet] = new SymbolDefinition(
                ComponentType.Mosfet, SymbolShape.Mosfet, new SchematicSize(90, 90),
                [
                    new("G",new(-45,0),SymbolPinDirection.Left),
                    new("D",new(18,-45),SymbolPinDirection.Up),
                    new("S",new(18,45),SymbolPinDirection.Down)
                ]),
            [ComponentType.AnalogSwitch] = new SymbolDefinition(
                ComponentType.AnalogSwitch, SymbolShape.AnalogSwitch, new SchematicSize(90, 45),
                [
                    new("A",new(-45,0),SymbolPinDirection.Left),
                    new("B",new(45,0),SymbolPinDirection.Right),
                    new("CTRL",new(0,22.5),SymbolPinDirection.Down)
                ])
        };

    public static SymbolDefinition Get(ComponentType componentType) => Definitions[componentType];

    private static SymbolDefinition CreateTwoTerminal(ComponentType type, SymbolShape shape) =>
        new(
            type,
            shape,
            new SchematicSize(80, 30),
            [
                new SymbolPinAnchor("1", new SchematicPoint(-40, 0), SymbolPinDirection.Left),
                new SymbolPinAnchor("2", new SchematicPoint(40, 0), SymbolPinDirection.Right)
            ]);
}
