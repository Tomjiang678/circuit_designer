using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Core.Schematic;

public sealed class SchematicDocument
{
    private readonly List<PlacedComponent> placedComponents = [];
    private readonly List<SchematicWire> wires = [];
    private readonly List<SchematicJunction> junctions = [];
    private readonly List<SchematicNetLabel> netLabels = [];
    private readonly List<SchematicTextAnnotation> textAnnotations = [];

    public CircuitGraph Circuit { get; }
    public IReadOnlyList<PlacedComponent> PlacedComponents => placedComponents;
    public IReadOnlyList<SchematicWire> Wires => wires;
    public IReadOnlyList<SchematicJunction> Junctions => junctions;
    public IReadOnlyList<SchematicNetLabel> NetLabels => netLabels;
    public IReadOnlyList<SchematicTextAnnotation> TextAnnotations => textAnnotations;

    public SchematicDocument(CircuitGraph circuit)
    {
        Circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
    }

    public void AddPlacedComponent(PlacedComponent placedComponent)
    {
        ArgumentNullException.ThrowIfNull(placedComponent);
        var graphComponent = Circuit.GetComponent(placedComponent.Component.Reference);
        if (!ReferenceEquals(graphComponent, placedComponent.Component))
            throw new InvalidOperationException($"Placed component '{placedComponent.Component.Reference}' is not owned by this document's circuit graph.");
        if (placedComponents.Any(item => string.Equals(item.Component.Reference, placedComponent.Component.Reference, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Component '{placedComponent.Component.Reference}' has already been placed.");
        placedComponents.Add(placedComponent);
    }

    public void AddWire(SchematicWire wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        _ = Circuit.GetNet(wire.NetName);
        wires.Add(wire);
    }

    public void ReplaceWires(IEnumerable<SchematicWire> replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var copy = replacement.ToArray();
        foreach (var wire in copy)
        {
            ArgumentNullException.ThrowIfNull(wire);
            _ = Circuit.GetNet(wire.NetName);
        }
        wires.Clear();
        wires.AddRange(copy);
        junctions.Clear();
    }

    public void AddJunction(SchematicJunction junction)
    {
        ArgumentNullException.ThrowIfNull(junction);
        _ = Circuit.GetNet(junction.NetName);
        if (!junctions.Any(item => item.NetName.Equals(junction.NetName, StringComparison.OrdinalIgnoreCase) && item.Position == junction.Position))
            junctions.Add(junction);
    }

    public void AddNetLabel(SchematicNetLabel label)
    {
        ArgumentNullException.ThrowIfNull(label);
        _ = Circuit.GetNet(label.NetName);
        netLabels.Add(label);
    }

    public void AddTextAnnotation(SchematicTextAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (string.IsNullOrWhiteSpace(annotation.Text)) throw new ArgumentException("Annotation text must not be empty.", nameof(annotation));
        textAnnotations.Add(annotation);
    }
}
