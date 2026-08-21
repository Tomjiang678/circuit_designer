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
