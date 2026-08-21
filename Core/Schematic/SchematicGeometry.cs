using CircuitModuleDesigner.Core.Schematic.Symbols;

namespace CircuitModuleDesigner.Core.Schematic;

public static class SchematicGeometry
{
    public static SchematicPoint TransformLocal(SchematicPoint point, ComponentOrientation orientation) => orientation switch
    {
        ComponentOrientation.Right => point,
        ComponentOrientation.Down => new SchematicPoint(-point.Y, point.X),
        ComponentOrientation.Left => new SchematicPoint(-point.X, -point.Y),
        ComponentOrientation.Up => new SchematicPoint(point.Y, -point.X),
        _ => point
    };

    public static SchematicPoint GetWorldPin(PlacedComponent placed, string pinName)
    {
        var local = SymbolDefinitionCatalog.Get(placed.Component.Type).GetPinAnchor(pinName).Position;
        var rotated = TransformLocal(local, placed.Orientation);
        return new SchematicPoint(placed.Position.X + rotated.X, placed.Position.Y + rotated.Y);
    }

    public static SymbolPinDirection GetWorldPinDirection(PlacedComponent placed, string pinName)
    {
        var direction = SymbolDefinitionCatalog.Get(placed.Component.Type).GetPinAnchor(pinName).Direction;
        var turns = placed.Orientation switch
        {
            ComponentOrientation.Down => 1,
            ComponentOrientation.Left => 2,
            ComponentOrientation.Up => 3,
            _ => 0
        };
        for (var index = 0; index < turns; index++)
            direction = direction switch
            {
                SymbolPinDirection.Left => SymbolPinDirection.Up,
                SymbolPinDirection.Up => SymbolPinDirection.Right,
                SymbolPinDirection.Right => SymbolPinDirection.Down,
                SymbolPinDirection.Down => SymbolPinDirection.Left,
                _ => direction
            };
        return direction;
    }

    public static SchematicRect GetBounds(PlacedComponent placed, double margin = 0)
    {
        var size = SymbolDefinitionCatalog.Get(placed.Component.Type).Size;
        var rotated = placed.Orientation is ComponentOrientation.Down or ComponentOrientation.Up;
        var width = rotated ? size.Height : size.Width;
        var height = rotated ? size.Width : size.Height;
        return new SchematicRect(
            placed.Position.X - width / 2 - margin,
            placed.Position.Y - height / 2 - margin,
            width + margin * 2,
            height + margin * 2);
    }
}
