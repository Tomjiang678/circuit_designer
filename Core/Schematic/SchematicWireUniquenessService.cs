namespace CircuitModuleDesigner.Core.Schematic;

/// <summary>
/// Converts routed polylines into a unique, cycle-free set of orthogonal segments.
/// Electrical nets remain unchanged; only redundant schematic geometry is removed.
/// </summary>
public sealed class SchematicWireUniquenessService
{
    private const double Epsilon = .01;

    public void Normalize(SchematicDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = new List<SchematicWire>();
        foreach (var netGroup in document.Wires.GroupBy(wire => wire.NetName, StringComparer.OrdinalIgnoreCase))
        {
            var accepted = new List<Segment>();
            foreach (var wire in netGroup)
            {
                var start = wire.Points[0]; var end = wire.Points[^1];
                if (accepted.Count > 0 && AreConnected(start, end, accepted))
                    continue;

                for (var index = 1; index < wire.Points.Count; index++)
                {
                    var segment = new Segment(wire.Points[index - 1], wire.Points[index]);
                    if (segment.IsZeroLength) continue;
                    foreach (var unique in SubtractCovered(segment, accepted))
                    {
                        accepted.Add(unique);
                        normalized.Add(new SchematicWire(netGroup.Key, [unique.Start, unique.End]));
                    }
                }
            }
        }
        document.ReplaceWires(normalized);
    }

    private static IReadOnlyList<Segment> SubtractCovered(Segment source, IReadOnlyList<Segment> accepted)
    {
        var intervals = new List<(double Start, double End)> { source.Interval };
        foreach (var existing in accepted.Where(source.IsCollinearWith))
        {
            var cover = existing.Interval;
            var next = new List<(double Start, double End)>();
            foreach (var interval in intervals)
            {
                if (cover.End <= interval.Start + Epsilon || cover.Start >= interval.End - Epsilon)
                {
                    next.Add(interval); continue;
                }
                if (cover.Start > interval.Start + Epsilon) next.Add((interval.Start, Math.Min(cover.Start, interval.End)));
                if (cover.End < interval.End - Epsilon) next.Add((Math.Max(cover.End, interval.Start), interval.End));
            }
            intervals = next;
            if (intervals.Count == 0) break;
        }
        return intervals.Select(source.FromInterval).Where(segment => !segment.IsZeroLength).ToArray();
    }

    private static bool AreConnected(SchematicPoint start, SchematicPoint end, IReadOnlyList<Segment> segments)
    {
        var starting = Enumerable.Range(0, segments.Count).Where(index => segments[index].Contains(start)).ToArray();
        var ending = Enumerable.Range(0, segments.Count).Where(index => segments[index].Contains(end)).ToHashSet();
        if (starting.Length == 0 || ending.Count == 0) return false;
        var pending = new Queue<int>(starting); var visited = starting.ToHashSet();
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (ending.Contains(current)) return true;
            for (var candidate = 0; candidate < segments.Count; candidate++)
                if (!visited.Contains(candidate) && segments[current].Touches(segments[candidate]))
                { visited.Add(candidate); pending.Enqueue(candidate); }
        }
        return false;
    }

    private readonly record struct Segment(SchematicPoint Start, SchematicPoint End)
    {
        public bool Horizontal => Math.Abs(Start.Y - End.Y) < Epsilon;
        public bool IsZeroLength => Math.Abs(Start.X - End.X) < Epsilon && Math.Abs(Start.Y - End.Y) < Epsilon;
        public (double Start, double End) Interval => Horizontal
            ? (Math.Min(Start.X, End.X), Math.Max(Start.X, End.X))
            : (Math.Min(Start.Y, End.Y), Math.Max(Start.Y, End.Y));

        public bool IsCollinearWith(Segment other) => Horizontal == other.Horizontal &&
            (Horizontal ? Math.Abs(Start.Y - other.Start.Y) < Epsilon : Math.Abs(Start.X - other.Start.X) < Epsilon);

        public Segment FromInterval((double Start, double End) interval)
        {
            var forward = Horizontal ? End.X >= Start.X : End.Y >= Start.Y;
            var first = forward ? interval.Start : interval.End; var second = forward ? interval.End : interval.Start;
            return Horizontal
                ? new Segment(new SchematicPoint(first, Start.Y), new SchematicPoint(second, Start.Y))
                : new Segment(new SchematicPoint(Start.X, first), new SchematicPoint(Start.X, second));
        }

        public bool Contains(SchematicPoint point) => Horizontal
            ? Math.Abs(point.Y - Start.Y) < Epsilon && Between(point.X, Start.X, End.X)
            : Math.Abs(point.X - Start.X) < Epsilon && Between(point.Y, Start.Y, End.Y);

        public bool Touches(Segment other)
        {
            if (IsCollinearWith(other)) return IntervalsOverlap(Interval, other.Interval);
            var horizontal = Horizontal ? this : other; var vertical = Horizontal ? other : this;
            return Between(vertical.Start.X, horizontal.Start.X, horizontal.End.X) &&
                   Between(horizontal.Start.Y, vertical.Start.Y, vertical.End.Y);
        }

        private static bool Between(double value, double first, double second) =>
            value >= Math.Min(first, second) - Epsilon && value <= Math.Max(first, second) + Epsilon;
        private static bool IntervalsOverlap((double Start, double End) first, (double Start, double End) second) =>
            Math.Max(first.Start, second.Start) <= Math.Min(first.End, second.End) + Epsilon;
    }
}
