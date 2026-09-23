using System.ComponentModel;
using System.Drawing.Drawing2D;
using CircuitModuleDesigner.Modules.Comparators.Analysis;

namespace CircuitModuleDesigner.Modules.Comparators.Controls;

public sealed class ComparatorTransferPlotControl : Control
{
    private const double EqualityTolerance = 1e-9;
    private static readonly Color RisingColor = Color.FromArgb(25, 91, 171);
    private static readonly Color FallingColor = Color.FromArgb(202, 92, 26);
    private static readonly Color GuideColor = Color.FromArgb(142, 146, 151);
    private ComparatorTransferAnalysis? analysis;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ComparatorTransferAnalysis? Analysis { get => analysis; set { analysis = value; Invalidate(); } }

    public ComparatorTransferPlotControl() { DoubleBuffered = true; BackColor = SystemColors.Window; ResizeRedraw = true; Dock = DockStyle.Fill; }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.Clear(BackColor);
        if (analysis is null || analysis.Points.Count == 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var area = new RectangleF(92, 34, Math.Max(100, Width - 128), Math.Max(100, Height - 108));
        var minX = analysis.Points.Min(p => p.InputVoltage); var maxX = analysis.Points.Max(p => p.InputVoltage);
        var outputLow = analysis.Points.Min(p => Math.Min(p.RisingOutputVoltage, p.FallingOutputVoltage));
        var outputHigh = analysis.Points.Max(p => Math.Max(p.RisingOutputVoltage, p.FallingOutputVoltage));
        var padY = Math.Max(.1, (outputHigh - outputLow) * .18); var minY = outputLow - padY; var maxY = outputHigh + padY;
        PointF Map(double x, double y) => new((float)(area.Left + (x - minX) / (maxX - minX) * area.Width), (float)(area.Bottom - (y - minY) / (maxY - minY) * area.Height));
        var isWindow = analysis.Regions.Count > 1;
        if (isWindow && analysis.Regions.Count >= 2)
        {
            var window = analysis.Regions[1]; var left = Map(window.Start, minY).X; var right = Map(window.End, minY).X;
            using var brush = new SolidBrush(Color.FromArgb(18, Color.SteelBlue));
            e.Graphics.FillRectangle(brush, Math.Min(left, right), area.Top, Math.Abs(right - left), area.Height);
        }

        using var levelGuide = new Pen(GuideColor, 1) { DashStyle = DashStyle.Dot };
        DrawLevel(e.Graphics, levelGuide, area, Map, minX, outputHigh, "VOH");
        DrawLevel(e.Graphics, levelGuide, area, Map, minX, outputLow, "VOL");

        var markers = ThresholdLabels(analysis, isWindow);
        using var thresholdGuide = new Pen(GuideColor, 1) { DashStyle = DashStyle.Dash };
        for (var index = 0; index < markers.Count; index++)
        {
            var marker = markers[index]; var x = Map(marker.Voltage, minY).X;
            e.Graphics.DrawLine(thresholdGuide, x, area.Top, x, area.Bottom);
            var text = $"{marker.Label}  {marker.Voltage:0.###} V"; var size = e.Graphics.MeasureString(text, Font);
            var row = markers.Count > 2 ? index % 2 : 0;
            e.Graphics.DrawString(text, Font, Brushes.Black, x - size.Width / 2, area.Bottom + 6 + row * (size.Height + 1));
        }

        using var axis = new Pen(Color.FromArgb(35, 39, 43), 1.4F) { EndCap = LineCap.ArrowAnchor };
        e.Graphics.DrawLine(axis, area.Left, area.Bottom, area.Right + 8, area.Bottom);
        e.Graphics.DrawLine(axis, area.Left, area.Bottom, area.Left, area.Top - 8);
        e.Graphics.DrawString("Vout", Font, Brushes.Black, area.Left - 42, area.Top - 26);
        e.Graphics.DrawString("Vin", Font, Brushes.Black, area.Right - 12, area.Bottom + 30);

        var risingSwitches = isWindow ? new[] { analysis.Markers[1], analysis.Markers[3] } : [analysis.Markers[1]];
        var fallingSwitches = isWindow ? new[] { analysis.Markers[0], analysis.Markers[2] } : [analysis.Markers[0]];
        using var risingPen = new Pen(RisingColor, 2.2F) { LineJoin = LineJoin.Miter };
        using var fallingPen = new Pen(FallingColor, 2.2F) { DashStyle = DashStyle.Dash, LineJoin = LineJoin.Miter };
        e.Graphics.DrawLines(risingPen, StepCurve(analysis.Points, risingSwitches, p => p.RisingOutputVoltage, Map, minX, maxX));
        e.Graphics.DrawLines(fallingPen, StepCurve(analysis.Points, fallingSwitches, p => p.FallingOutputVoltage, Map, minX, maxX));
        DrawDirection(e.Graphics, analysis.Points, p => p.RisingOutputVoltage, Map, minX, maxX, true, RisingColor);
        DrawDirection(e.Graphics, analysis.Points, p => p.FallingOutputVoltage, Map, minX, maxX, false, FallingColor);
    }

    private void DrawLevel(Graphics graphics, Pen pen, RectangleF area, Func<double, double, PointF> map, double minX, double voltage, string label)
    {
        var y = map(minX, voltage).Y; graphics.DrawLine(pen, area.Left, y, area.Right, y);
        var text = $"{label}  {voltage:0.###} V"; var size = graphics.MeasureString(text, Font);
        graphics.DrawString(text, Font, Brushes.Black, area.Left - size.Width - 7, y - size.Height / 2);
    }

    private static IReadOnlyList<(double Voltage, string Label)> ThresholdLabels(ComparatorTransferAnalysis current, bool isWindow)
    {
        if (!isWindow) return [(current.Markers[0], "VTL"), (current.Markers[1], "VTH")];
        var hysteresis = Math.Abs(current.Markers[0] - current.Markers[1]) > EqualityTolerance ||
                         Math.Abs(current.Markers[2] - current.Markers[3]) > EqualityTolerance;
        return hysteresis
            ? [(current.Markers[0], "VL↓"), (current.Markers[1], "VL↑"), (current.Markers[2], "VH↓"), (current.Markers[3], "VH↑")]
            : [(current.Markers[0], "VL"), (current.Markers[2], "VH")];
    }

    private static PointF[] StepCurve(IReadOnlyList<ComparatorTransferPoint> points, IReadOnlyList<double> switches,
        Func<ComparatorTransferPoint, double> output, Func<double, double, PointF> map, double minX, double maxX)
    {
        var level = output(points[0]); var path = new List<PointF> { map(minX, level) };
        foreach (var threshold in switches.OrderBy(value => value))
        {
            path.Add(map(threshold, level)); var next = points.FirstOrDefault(point => point.InputVoltage > threshold);
            if (next is null) continue; level = output(next); path.Add(map(threshold, level));
        }
        path.Add(map(maxX, level)); return [.. path];
    }

    private static void DrawDirection(Graphics graphics, IReadOnlyList<ComparatorTransferPoint> points,
        Func<ComparatorTransferPoint, double> output, Func<double, double, PointF> map,
        double minX, double maxX, bool right, Color color)
    {
        var x = minX + (maxX - minX) * (right ? .22 : .78); var point = points.MinBy(p => Math.Abs(p.InputVoltage - x))!;
        var center = map(x, output(point)); using var pen = new Pen(color, 2.2F) { EndCap = LineCap.ArrowAnchor };
        graphics.DrawLine(pen, right ? center.X - 28 : center.X + 28, center.Y, center.X, center.Y);
    }
}
