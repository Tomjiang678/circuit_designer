namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls.SpecificationPreview;

internal sealed class PlotLabelLayoutEngine
{
    private readonly RectangleF bounds;
    private readonly float gap;
    private readonly List<RectangleF> occupied = [];

    public PlotLabelLayoutEngine(RectangleF bounds, float gap)
    {
        this.bounds = bounds;
        this.gap = gap;
    }

    public RectangleF Place(SizeF size, PointF anchor)
    {
        var candidates = new[]
        {
            new PointF(anchor.X + gap, anchor.Y - size.Height - gap),
            new PointF(anchor.X + gap, anchor.Y + gap),
            new PointF(anchor.X - size.Width - gap, anchor.Y - size.Height - gap),
            new PointF(anchor.X - size.Width - gap, anchor.Y + gap),
            new PointF(anchor.X - size.Width / 2, anchor.Y - size.Height - gap),
            new PointF(anchor.X - size.Width / 2, anchor.Y + gap)
        };

        RectangleF? best = null;
        var bestOverlap = float.MaxValue;
        foreach (var candidate in candidates)
        {
            var rectangle = Clamp(new RectangleF(candidate, size));
            var overlap = occupied.Sum(existing => IntersectionArea(Inflate(rectangle, gap), existing));
            if (overlap <= 0)
            {
                occupied.Add(Inflate(rectangle, gap));
                return rectangle;
            }
            if (overlap < bestOverlap)
            {
                best = rectangle;
                bestOverlap = overlap;
            }
        }

        RectangleF? nearestFree = null;
        var nearestDistance = float.MaxValue;
        var horizontalStep = Math.Max(size.Width + gap * 2, 1);
        var verticalStep = Math.Max(size.Height + gap * 2, 1);
        for (var y = bounds.Top; y <= bounds.Bottom - size.Height; y += verticalStep)
        {
            for (var x = bounds.Left; x <= bounds.Right - size.Width; x += horizontalStep)
            {
                var rectangle = new RectangleF(x, y, size.Width, size.Height);
                var padded = Inflate(rectangle, gap);
                if (occupied.Any(existing => existing.IntersectsWith(padded)))
                    continue;
                var distance = MathF.Abs(rectangle.Left - anchor.X) + MathF.Abs(rectangle.Top - anchor.Y);
                if (distance < nearestDistance)
                {
                    nearestFree = rectangle;
                    nearestDistance = distance;
                }
            }
        }
        if (nearestFree.HasValue)
        {
            occupied.Add(Inflate(nearestFree.Value, gap));
            return nearestFree.Value;
        }

        var selected = best ?? Clamp(new RectangleF(anchor, size));
        occupied.Add(Inflate(selected, gap));
        return selected;
    }

    private RectangleF Clamp(RectangleF rectangle)
    {
        var width = Math.Min(rectangle.Width, bounds.Width);
        var height = Math.Min(rectangle.Height, bounds.Height);
        var x = Math.Clamp(rectangle.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - width));
        var y = Math.Clamp(rectangle.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));
        return new RectangleF(x, y, width, height);
    }

    private static RectangleF Inflate(RectangleF rectangle, float amount)
    {
        rectangle.Inflate(amount, amount);
        return rectangle;
    }

    private static float IntersectionArea(RectangleF first, RectangleF second)
    {
        var intersection = RectangleF.Intersect(first, second);
        return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
    }
}
