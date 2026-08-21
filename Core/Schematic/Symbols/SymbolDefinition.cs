using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Core.Schematic.Symbols;

public sealed record SymbolPinAnchor(
    string PinName,
    SchematicPoint Position,
    SymbolPinDirection Direction);

public sealed class SymbolDefinition
{
    private readonly IReadOnlyDictionary<string, SymbolPinAnchor> pinAnchors;

    public ComponentType ComponentType { get; }
    public SymbolShape Shape { get; }
    public SchematicSize Size { get; }
    public SchematicRect LocalBounds => SchematicRect.Centered(Size);
    public IReadOnlyCollection<SymbolPinAnchor> PinAnchors => pinAnchors.Values.ToArray();

    public SymbolDefinition(
        ComponentType componentType,
        SymbolShape shape,
        SchematicSize size,
        IEnumerable<SymbolPinAnchor> pinAnchors)
    {
        if (size.Width <= 0 || size.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "Symbol dimensions must be greater than zero.");
        ComponentType = componentType;
        Shape = shape;
        Size = size;
        this.pinAnchors = pinAnchors.ToDictionary(anchor => anchor.PinName, StringComparer.OrdinalIgnoreCase);
    }

    public SymbolPinAnchor GetPinAnchor(string pinName) =>
        pinAnchors.TryGetValue(pinName, out var anchor)
            ? anchor
            : throw new KeyNotFoundException($"Symbol for '{ComponentType}' does not define pin '{pinName}'.");
}
