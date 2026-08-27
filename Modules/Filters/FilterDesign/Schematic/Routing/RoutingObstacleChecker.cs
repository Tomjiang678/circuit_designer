using CircuitModuleDesigner.Core.Schematic;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic.Routing;

internal sealed class RoutingObstacleChecker
{
    public int CollisionScore(IReadOnlyList<SchematicPoint> route, IReadOnlyList<SchematicRect> bounds)
    {
        var score = 0;
        for (var index = 1; index < route.Count; index++)
        foreach (var box in bounds)
            if (SegmentIntersectsBox(route[index - 1], route[index], box)) score++;
        return score;
    }

    private static bool SegmentIntersectsBox(SchematicPoint a, SchematicPoint b, SchematicRect box)
    {
        var minX = Math.Min(a.X, b.X); var maxX = Math.Max(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y); var maxY = Math.Max(a.Y, b.Y);
        return maxX >= box.X && minX <= box.X + box.Width && maxY >= box.Y && minY <= box.Y + box.Height;
    }
}
