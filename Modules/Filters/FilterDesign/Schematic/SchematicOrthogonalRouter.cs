using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Core.Schematic.Symbols;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic;

internal sealed class SchematicOrthogonalRouter
{
    private const double Clearance = 18;
    private const double PortLength = 75;
    private const double PinEscapeLength = 28;

    public void Route(SchematicDocument document)
    {
        var placed = document.PlacedComponents.ToDictionary(item => item.Component.Reference, StringComparer.OrdinalIgnoreCase);
        var componentBounds = document.PlacedComponents.Select(item => SchematicGeometry.GetBounds(item, Clearance)).ToArray();
        foreach (var net in document.Circuit.Nets)
        {
            var anchors = net.Pins
                .Select(pin => new PinAnchor(
                    pin,
                    SchematicGeometry.GetWorldPin(placed[pin.ComponentReference], pin.Name),
                    SchematicGeometry.GetWorldPinDirection(placed[pin.ComponentReference], pin.Name)))
                .ToArray();
            if (anchors.Length == 0)
                continue;

            anchors = RouteLocalOpAmpFeedback(document, net, anchors, placed);
            anchors = NormalizeFacingEscapes(anchors);
            if (TryRouteNonInvertingGainNet(document, net, anchors, placed))
                continue;
            foreach (var anchor in anchors.Where(anchor => anchor.Pin.Name is not ("V+" or "V-")))
                document.AddWire(new SchematicWire(net.Name, [anchor.Point, anchor.RoutingPoint]));

            switch (net.Name.ToUpperInvariant())
            {
                case "GND":
                    RouteGround(document, net, anchors);
                    break;
                case "VCC":
                    // Supply pins remain connected in CircuitGraph but are intentionally
                    // omitted from the functional filter schematic.
                    break;
                case "VEE":
                    // Supply pins remain connected in CircuitGraph but are intentionally
                    // omitted from the functional filter schematic.
                    break;
                case "VIN":
                    RoutePort(document, net, anchors, true, componentBounds);
                    break;
                case "VOUT":
                    RoutePort(document, net, anchors, false, componentBounds);
                    break;
                default:
                    RouteSignalNet(document, net, anchors, componentBounds);
                    break;
            }
        }
        new SchematicWireUniquenessService().Normalize(document);
        AddDerivedJunctions(document);
    }

    private static PinAnchor[] RouteLocalOpAmpFeedback(
        SchematicDocument document,
        CircuitNet net,
        IReadOnlyList<PinAnchor> anchors,
        IReadOnlyDictionary<string, PlacedComponent> placed)
    {
        var removedPinIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in anchors.GroupBy(item => item.Pin.ComponentReference))
        {
            var output = group.FirstOrDefault(item => item.Pin.Name.Equals("OUT", StringComparison.OrdinalIgnoreCase));
            var inverting = group.FirstOrDefault(item => item.Pin.Name.Equals("IN-", StringComparison.OrdinalIgnoreCase));
            if (output == null || inverting == null)
                continue;

            var bounds = SchematicGeometry.GetBounds(placed[group.Key], 12);
            var leftTrack = bounds.X - 18;
            var rightTrack = bounds.X + bounds.Width + 18;
            var bottomTrack = bounds.Y + bounds.Height + 24;
            document.AddWire(new SchematicWire(net.Name,
            [
                inverting.Point,
                new SchematicPoint(leftTrack, inverting.Point.Y),
                new SchematicPoint(leftTrack, bottomTrack),
                new SchematicPoint(rightTrack, bottomTrack),
                new SchematicPoint(rightTrack, output.Point.Y),
                output.Point
            ]));
            removedPinIds.Add(inverting.Pin.Id);
        }
        return anchors.Where(item => !removedPinIds.Contains(item.Pin.Id)).ToArray();
    }

    private static void RouteGround(SchematicDocument document, CircuitNet net, IReadOnlyList<PinAnchor> anchors)
    {
        foreach (var anchor in anchors)
        {
            var endpoint = anchor.Direction switch
            {
                SymbolPinDirection.Left => new SchematicPoint(anchor.RoutingPoint.X - 34, anchor.RoutingPoint.Y),
                SymbolPinDirection.Right => new SchematicPoint(anchor.RoutingPoint.X + 34, anchor.RoutingPoint.Y),
                SymbolPinDirection.Up => new SchematicPoint(anchor.RoutingPoint.X, anchor.RoutingPoint.Y - 34),
                _ => new SchematicPoint(anchor.RoutingPoint.X, anchor.RoutingPoint.Y + 34)
            };
            document.AddWire(new SchematicWire(net.Name, Compact([anchor.RoutingPoint, endpoint])));
            document.AddNetLabel(new SchematicNetLabel(net.Name, "GND", endpoint));
        }
    }

    private void RoutePort(SchematicDocument document, CircuitNet net, IReadOnlyList<PinAnchor> anchors, bool input, IReadOnlyList<SchematicRect> bounds)
    {
        var portAnchor = input ? anchors.MinBy(item => item.RoutingPoint.X)! : anchors.MaxBy(item => item.RoutingPoint.X)!;
        var endpoint = new SchematicPoint(portAnchor.RoutingPoint.X + (input ? -PortLength : PortLength), portAnchor.RoutingPoint.Y);
        document.AddWire(new SchematicWire(net.Name, [endpoint, portAnchor.RoutingPoint]));
        document.AddNetLabel(new SchematicNetLabel(net.Name, net.Name, new SchematicPoint(endpoint.X + (input ? -8 : 8), endpoint.Y - 12)));
        var connected = new List<PinAnchor> { portAnchor };
        foreach (var anchor in anchors.Where(item => item != portAnchor).OrderBy(item => Distance(item.RoutingPoint, portAnchor.RoutingPoint)))
        {
            // Two pin escape segments may already overlap or cross. They are then
            // visibly and electrically connected; adding another route creates a
            // redundant rectangular loop around nearby components.
            if (!connected.Any(existing => EscapeSegmentsTouch(anchor, existing)))
                AddBestRoute(document, net.Name, anchor.RoutingPoint, FindNearest(anchor.RoutingPoint, connected), bounds);
            connected.Add(anchor);
        }
    }

    private void RouteSignalNet(SchematicDocument document, CircuitNet net, IReadOnlyList<PinAnchor> anchors, IReadOnlyList<SchematicRect> bounds)
    {
        if (anchors.Count == 1)
        {
            var endpoint = new SchematicPoint(anchors[0].RoutingPoint.X + 40, anchors[0].RoutingPoint.Y);
            document.AddWire(new SchematicWire(net.Name, [anchors[0].RoutingPoint, endpoint]));
            document.AddNetLabel(new SchematicNetLabel(net.Name, net.Name, new SchematicPoint(endpoint.X + 4, endpoint.Y - 10)));
            return;
        }

        var remaining = anchors.OrderBy(item => item.RoutingPoint.X).ThenBy(item => item.RoutingPoint.Y).ToList();
        var connected = new List<PinAnchor> { remaining[0] };
        remaining.RemoveAt(0);
        while (remaining.Count > 0)
        {
            var candidate = remaining
                .Select(anchor => (Anchor: anchor, Target: FindNearest(anchor.RoutingPoint, connected)))
                .OrderBy(item => Distance(item.Anchor.RoutingPoint, item.Target))
                .First();
            if (!connected.Any(existing => EscapeSegmentsTouch(candidate.Anchor, existing)))
                AddBestRoute(document, net.Name, candidate.Anchor.RoutingPoint, candidate.Target, bounds);
            connected.Add(candidate.Anchor);
            remaining.Remove(candidate.Anchor);
        }

    }

    private static bool TryRouteNonInvertingGainNet(
        SchematicDocument document,
        CircuitNet net,
        IReadOnlyList<PinAnchor> anchors,
        IReadOnlyDictionary<string, PlacedComponent> placed)
    {
        var inverting = anchors.SingleOrDefault(anchor => anchor.Pin.Name.Equals("IN-", StringComparison.OrdinalIgnoreCase));
        if (inverting == null) return false;
        var resistorAnchors = anchors.Where(anchor => anchor != inverting &&
            placed[anchor.Pin.ComponentReference].Component.Type == ComponentType.Resistor).ToArray();
        if (resistorAnchors.Length != 2 || anchors.Count != 3) return false;

        // RG is drawn vertically and its upper pin is the natural star point for
        // RF, RG and IN-. Keeping one trunk here avoids the two tall parallel
        // routes produced by the generic minimum-distance router.
        var shunt = resistorAnchors.FirstOrDefault(anchor => anchor.Direction is SymbolPinDirection.Up or SymbolPinDirection.Down);
        if (shunt == null) return false;
        var feedback = resistorAnchors.Single(anchor => anchor != shunt);
        var junction = new SchematicPoint(shunt.Point.X, inverting.Point.Y);
        AddLocalWire(document, net.Name, inverting.Point, junction);
        AddLocalWire(document, net.Name, shunt.Point, junction);
        AddLocalWire(document, net.Name, feedback.Point,
            new SchematicPoint(junction.X, feedback.Point.Y), junction);
        return true;
    }

    private static void AddLocalWire(SchematicDocument document, string netName, params SchematicPoint[] points)
    {
        var compact = Compact(points);
        if (compact.Count >= 2) document.AddWire(new SchematicWire(netName, compact));
    }

    private static void AddDerivedJunctions(SchematicDocument document)
    {
        foreach (var group in document.Wires.GroupBy(wire => wire.NetName, StringComparer.OrdinalIgnoreCase))
        {
            var segments = group.SelectMany(wire => wire.Points.Zip(wire.Points.Skip(1), (a, b) => (A: a, B: b))).ToArray();
            var candidates = new HashSet<SchematicPoint>(group.SelectMany(wire => wire.Points));
            for (var first = 0; first < segments.Length; first++)
            for (var second = first + 1; second < segments.Length; second++)
                if (TryIntersection(segments[first], segments[second], out var intersection)) candidates.Add(intersection);

            foreach (var point in candidates)
            {
                var directions = new HashSet<int>();
                foreach (var segment in segments.Where(segment => Contains(segment, point)))
                {
                    if (segment.A.X < point.X || segment.B.X < point.X) directions.Add(0);
                    if (segment.A.X > point.X || segment.B.X > point.X) directions.Add(1);
                    if (segment.A.Y < point.Y || segment.B.Y < point.Y) directions.Add(2);
                    if (segment.A.Y > point.Y || segment.B.Y > point.Y) directions.Add(3);
                }
                if (directions.Count >= 3) document.AddJunction(new SchematicJunction(group.Key, point));
            }
        }
    }

    private static bool TryIntersection((SchematicPoint A, SchematicPoint B) first, (SchematicPoint A, SchematicPoint B) second, out SchematicPoint point)
    {
        var firstHorizontal = NearlyEqual(first.A.Y, first.B.Y); var secondHorizontal = NearlyEqual(second.A.Y, second.B.Y);
        if (firstHorizontal == secondHorizontal) { point = default; return false; }
        var horizontal = firstHorizontal ? first : second; var vertical = firstHorizontal ? second : first;
        point = new SchematicPoint(vertical.A.X, horizontal.A.Y);
        return Between(point.X, horizontal.A.X, horizontal.B.X) && Between(point.Y, vertical.A.Y, vertical.B.Y);
    }

    private static bool Contains((SchematicPoint A, SchematicPoint B) segment, SchematicPoint point) =>
        Between(point.X, segment.A.X, segment.B.X) && Between(point.Y, segment.A.Y, segment.B.Y) &&
        (NearlyEqual(segment.A.X, segment.B.X) ? NearlyEqual(point.X, segment.A.X) : NearlyEqual(point.Y, segment.A.Y));
    private static bool Between(double value, double first, double second) => value >= Math.Min(first, second) - .01 && value <= Math.Max(first, second) + .01;

    private void AddBestRoute(SchematicDocument document, string netName, SchematicPoint start, SchematicPoint end, IReadOnlyList<SchematicRect> bounds)
    {
        var direct = Compact([start, end]);
        var alignedX = NearlyEqual(start.X, end.X);
        var alignedY = NearlyEqual(start.Y, end.Y);
        if ((alignedX || alignedY) && CollisionScore(direct, bounds) == 0)
        {
            document.AddWire(new SchematicWire(netName, direct));
            return;
        }

        var horizontalFirst = Compact([start, new SchematicPoint(end.X, start.Y), end]);
        var verticalFirst = Compact([start, new SchematicPoint(start.X, end.Y), end]);
        var horizontalScore = CollisionScore(horizontalFirst, bounds);
        var verticalScore = CollisionScore(verticalFirst, bounds);
        IReadOnlyList<SchematicPoint> selected = horizontalScore <= verticalScore ? horizontalFirst : verticalFirst;

        if (Math.Min(horizontalScore, verticalScore) > 0)
        {
            var top = bounds.Min(item => item.Y) - 30;
            var bottom = bounds.Max(item => item.Y + item.Height) + 30;
            var topRoute = Compact([start, new SchematicPoint(start.X, top), new SchematicPoint(end.X, top), end]);
            var bottomRoute = Compact([start, new SchematicPoint(start.X, bottom), new SchematicPoint(end.X, bottom), end]);
            selected = CollisionScore(topRoute, bounds) <= CollisionScore(bottomRoute, bounds) ? topRoute : bottomRoute;
        }
        document.AddWire(new SchematicWire(netName, selected));
    }

    private static int CollisionScore(IReadOnlyList<SchematicPoint> route, IReadOnlyList<SchematicRect> bounds)
    {
        var score = 0;
        for (var i = 1; i < route.Count; i++)
        {
            foreach (var box in bounds)
            {
                if (SegmentIntersectsBox(route[i - 1], route[i], box))
                    score++;
            }
        }
        return score;
    }

    private static bool SegmentIntersectsBox(SchematicPoint a, SchematicPoint b, SchematicRect box)
    {
        var minX = Math.Min(a.X, b.X); var maxX = Math.Max(a.X, b.X);
        var minY = Math.Min(a.Y, b.Y); var maxY = Math.Max(a.Y, b.Y);
        return maxX >= box.X && minX <= box.X + box.Width && maxY >= box.Y && minY <= box.Y + box.Height;
    }

    private static SchematicPoint FindNearest(SchematicPoint point, IReadOnlyList<PinAnchor> candidates) =>
        candidates.MinBy(item => Distance(point, item.RoutingPoint))!.RoutingPoint;

    private static double Distance(SchematicPoint a, SchematicPoint b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private static PinAnchor[] NormalizeFacingEscapes(PinAnchor[] anchors)
    {
        var result = anchors.ToArray();
        var paired = new HashSet<int>();
        for (var first = 0; first < result.Length; first++)
        {
            if (paired.Contains(first) || result[first].Pin.Name is "V+" or "V-") continue;
            var partner = Enumerable.Range(first + 1, result.Length - first - 1)
                .Where(index => !paired.Contains(index) && result[index].Pin.Name is not ("V+" or "V-") && Faces(result[first], result[index]))
                .OrderBy(index => Distance(result[first].Point, result[index].Point))
                .FirstOrDefault(-1);
            if (partner < 0) continue;
            var a = result[first]; var b = result[partner];
            var reach = Distance(a.Point, a.RoutingPoint) + Distance(b.Point, b.RoutingPoint);
            if (Distance(a.Point, b.Point) > reach + .01) continue;
            var meeting = new SchematicPoint((a.Point.X + b.Point.X) / 2, (a.Point.Y + b.Point.Y) / 2);
            result[first] = a with { RoutingPointOverride = meeting };
            result[partner] = b with { RoutingPointOverride = meeting };
            paired.Add(first); paired.Add(partner);
        }
        return result;
    }

    private static bool Faces(PinAnchor first, PinAnchor second)
    {
        if (NearlyEqual(first.Point.Y, second.Point.Y))
            return first.Direction == SymbolPinDirection.Right && second.Direction == SymbolPinDirection.Left && first.Point.X <= second.Point.X ||
                   second.Direction == SymbolPinDirection.Right && first.Direction == SymbolPinDirection.Left && second.Point.X <= first.Point.X;
        if (NearlyEqual(first.Point.X, second.Point.X))
            return first.Direction == SymbolPinDirection.Down && second.Direction == SymbolPinDirection.Up && first.Point.Y <= second.Point.Y ||
                   second.Direction == SymbolPinDirection.Down && first.Direction == SymbolPinDirection.Up && second.Point.Y <= first.Point.Y;
        return false;
    }
    private static bool EscapeSegmentsTouch(PinAnchor first, PinAnchor second)
    {
        var a = first.Point; var b = first.RoutingPoint; var c = second.Point; var d = second.RoutingPoint;
        var firstHorizontal = NearlyEqual(a.Y, b.Y); var secondHorizontal = NearlyEqual(c.Y, d.Y);
        if (firstHorizontal && secondHorizontal)
            return NearlyEqual(a.Y, c.Y) && RangesOverlap(a.X, b.X, c.X, d.X);
        if (!firstHorizontal && !secondHorizontal)
            return NearlyEqual(a.X, c.X) && RangesOverlap(a.Y, b.Y, c.Y, d.Y);
        var horizontalA = firstHorizontal ? a : c; var horizontalB = firstHorizontal ? b : d;
        var verticalA = firstHorizontal ? c : a; var verticalB = firstHorizontal ? d : b;
        return Between(verticalA.X, horizontalA.X, horizontalB.X) && Between(horizontalA.Y, verticalA.Y, verticalB.Y);
    }
    private static bool RangesOverlap(double firstA, double firstB, double secondA, double secondB) =>
        Math.Max(Math.Min(firstA, firstB), Math.Min(secondA, secondB)) <= Math.Min(Math.Max(firstA, firstB), Math.Max(secondA, secondB)) + .01;
    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) < 0.1;

    private static IReadOnlyList<SchematicPoint> Compact(IEnumerable<SchematicPoint> points)
    {
        var result = new List<SchematicPoint>();
        foreach (var point in points)
            if (result.Count == 0 || result[^1] != point)
                result.Add(point);
        // Remove A-B-A retraces as well as collinear intermediate points. A route
        // with no lateral displacement must never leave a dangling visual spur.
        for (var index = result.Count - 2; index > 0; index--)
        {
            if (result[index - 1] == result[index + 1])
            {
                result.RemoveAt(index + 1);
                result.RemoveAt(index);
                continue;
            }
            var collinearX = NearlyEqual(result[index - 1].X, result[index].X) && NearlyEqual(result[index].X, result[index + 1].X);
            var collinearY = NearlyEqual(result[index - 1].Y, result[index].Y) && NearlyEqual(result[index].Y, result[index + 1].Y);
            if (collinearX || collinearY) result.RemoveAt(index);
        }
        return result;
    }

    private sealed record PinAnchor(ComponentPin Pin, SchematicPoint Point, SymbolPinDirection Direction)
    {
        public SchematicPoint? RoutingPointOverride { get; init; }
        public SchematicPoint RoutingPoint => RoutingPointOverride ?? (Direction switch
        {
            SymbolPinDirection.Left => new SchematicPoint(Point.X - LeftEscapeLength, Point.Y),
            SymbolPinDirection.Right => new SchematicPoint(Point.X + PinEscapeLength, Point.Y),
            SymbolPinDirection.Up => new SchematicPoint(Point.X, Point.Y - PinEscapeLength),
            SymbolPinDirection.Down => new SchematicPoint(Point.X, Point.Y + PinEscapeLength),
            _ => Point
        });

        // Op-amp inputs must leave on different vertical tracks. Otherwise a vertical
        // route from IN+ can form a false T-junction with the IN- escape segment.
        private double LeftEscapeLength => Pin.Name.Equals("IN+", StringComparison.OrdinalIgnoreCase)
            ? PinEscapeLength + 16
            : PinEscapeLength;
    }
}
