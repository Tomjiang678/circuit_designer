using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Core.Schematic;

public sealed class PlacedComponent
{
    public CircuitComponent Component { get; }
    public SchematicPoint Position { get; set; }
    public ComponentOrientation Orientation { get; set; }

    public PlacedComponent(
        CircuitComponent component,
        SchematicPoint position,
        ComponentOrientation orientation = ComponentOrientation.Right)
    {
        Component = component ?? throw new ArgumentNullException(nameof(component));
        Position = position;
        Orientation = orientation;
    }
}
