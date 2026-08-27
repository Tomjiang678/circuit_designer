using CircuitModuleDesigner.Core.Schematic;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic.Collision;

public sealed record SchematicOccupiedBounds(
    SchematicRect SymbolBounds,
    SchematicRect ReferenceTextBounds,
    SchematicRect ValueTextBounds,
    IReadOnlyList<SchematicRect> PinKeepoutBounds,
    SchematicRect OccupiedBounds);

public sealed class ComponentCollisionDetector
{
    public IReadOnlyList<(PlacedComponent First, PlacedComponent Second)> Find(SchematicDocument document, double margin = 8)
    {
        var placed = document.PlacedComponents.ToArray();
        var result = new List<(PlacedComponent, PlacedComponent)>();
        for (var first = 0; first < placed.Length; first++)
        for (var second = first + 1; second < placed.Length; second++)
            if (Intersects(GetOccupiedBounds(placed[first], margin).OccupiedBounds,
                           GetOccupiedBounds(placed[second], margin).OccupiedBounds))
                result.Add((placed[first], placed[second]));
        return result;
    }

    public SchematicOccupiedBounds GetOccupiedBounds(PlacedComponent placed, double margin = 8)
    {
        var symbol = SchematicGeometry.GetBounds(placed, margin);
        var referenceWidth = Math.Max(28, placed.Component.Reference.Length * 8);
        var reference = new SchematicRect(placed.Position.X - referenceWidth / 2, symbol.Y - 22, referenceWidth, 16);
        var value = new SchematicRect(placed.Position.X - 48, symbol.Y + symbol.Height + 6, 96, 16);
        var pins = placed.Component.Pins.Select(pin =>
        {
            var point = SchematicGeometry.GetWorldPin(placed, pin.Name);
            return new SchematicRect(point.X - 7, point.Y - 7, 14, 14);
        }).ToArray();
        var occupied = new[] { symbol, reference, value }.Concat(pins).Aggregate(Union);
        return new(symbol, reference, value, pins, occupied);
    }

    private static SchematicRect Union(SchematicRect a, SchematicRect b)
    {
        var left = Math.Min(a.X, b.X); var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X + a.Width, b.X + b.Width); var bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new(left, top, right - left, bottom - top);
    }

    private static bool Intersects(SchematicRect a, SchematicRect b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
}
