using System.ComponentModel;
using System.Drawing.Drawing2D;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class AffineTransferPlotControl : Control
{
    private AnalogAnalysisResult? analysis;
    private string sweepInput = "U1";
    private double lowerLimit = -15;
    private double upperLimit = 15;
    private double? preferredMinimumY;
    private double? preferredMaximumY;
    private RectangleF plotBounds;
    private double plotMinimumX;
    private double plotMaximumX;
    private double plotMinimumY;
    private double plotMaximumY;
    private int? hoverIndex;
    private int? pinnedIndex;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AnalogAnalysisResult? Analysis { get => analysis; set { analysis = value; hoverIndex = null; pinnedIndex = null; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SweepInput { get => sweepInput; set { sweepInput = value; hoverIndex = null; pinnedIndex = null; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double LowerLimit { get => lowerLimit; set { lowerLimit = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double UpperLimit { get => upperLimit; set { upperLimit = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double? PreferredMinimumY { get => preferredMinimumY; set { preferredMinimumY = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double? PreferredMaximumY { get => preferredMaximumY; set { preferredMaximumY = value; Invalidate(); } }

    public AffineTransferPlotControl()
    {
        DoubleBuffered = true; ResizeRedraw = true; BackColor = SystemColors.Window; Cursor = Cursors.Cross;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (analysis is null || plotBounds.Width <= 0 || !plotBounds.Contains(e.Location))
        {
            if (hoverIndex is not null) { hoverIndex = null; Invalidate(); }
            return;
        }
        var input = plotMinimumX + (e.X - plotBounds.Left) / plotBounds.Width * (plotMaximumX - plotMinimumX);
        var points = analysis.Points; var nearest = 0; var distance = double.PositiveInfinity;
        for (var index = 0; index < points.Count; index++)
        {
            var candidate = Math.Abs(points[index].Input - input);
            if (candidate >= distance) continue; distance = candidate; nearest = index;
        }
        if (hoverIndex != nearest) { hoverIndex = nearest; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverIndex is not null) { hoverIndex = null; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || hoverIndex is null) return;
        pinnedIndex = pinnedIndex == hoverIndex ? null : hoverIndex;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.Clear(SystemColors.Window); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var current = analysis;
        if (current is null || current.Points.Count < 2) { e.Graphics.DrawString("No sweep data.", Font, Brushes.DimGray, 18, 18); return; }
        var points = current.Points;
        var bounds = new RectangleF(78, 46, Math.Max(100, ClientSize.Width - 110), Math.Max(100, ClientSize.Height - 105));
        var minX = points.Min(point => point.Input); var maxX = points.Max(point => point.Input);
        var dataMinY = points.Min(point => Math.Min(point.Target, point.Actual));
        var dataMaxY = points.Max(point => Math.Max(point.Target, point.Actual));
        double minY;
        double maxY;
        if (preferredMinimumY is double preferredLow && preferredMaximumY is double preferredHigh)
        {
            minY = Math.Min(Math.Min(preferredLow, preferredHigh), dataMinY);
            maxY = Math.Max(Math.Max(preferredLow, preferredHigh), dataMaxY);
            if (maxY - minY < 1e-12)
            {
                var padding = Math.Max(.05, Math.Abs(minY) * .05);
                minY -= padding;
                maxY += padding;
            }
        }
        else
        {
            minY = Math.Min(lowerLimit, dataMinY);
            maxY = Math.Max(upperLimit, dataMaxY);
            var padding = Math.Max(.05, (maxY - minY) * .08);
            minY -= padding;
            maxY += padding;
        }
        plotBounds = bounds; plotMinimumX = minX; plotMaximumX = maxX; plotMinimumY = minY; plotMaximumY = maxY;
        using var grid = new Pen(Color.Gainsboro); using var axis = new Pen(Color.DimGray, 1.2f);
        for (var index = 0; index <= 10; index++)
        {
            var x = bounds.Left + bounds.Width * index / 10f; var y = bounds.Top + bounds.Height * index / 10f;
            e.Graphics.DrawLine(grid, x, bounds.Top, x, bounds.Bottom); e.Graphics.DrawLine(grid, bounds.Left, y, bounds.Right, y);
            var xValue = minX + (maxX - minX) * index / 10d; var yValue = maxY - (maxY - minY) * index / 10d;
            e.Graphics.DrawString(xValue.ToString("G4"), Font, Brushes.DimGray, x - 14, bounds.Bottom + 5);
            e.Graphics.DrawString(yValue.ToString("G4"), Font, Brushes.DimGray, 8, y - 8);
        }
        e.Graphics.DrawRectangle(axis, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        DrawRail(e.Graphics, lowerLimit, "VEE limit", bounds, minY, maxY);
        DrawRail(e.Graphics, upperLimit, "VCC limit", bounds, minY, maxY);
        DrawCurve(e.Graphics, points.Select(point => (point.Input, point.Target)), bounds, minX, maxX, minY, maxY, Color.Gray, DashStyle.Dash);
        if(points.Any(point=>point.Theoretical.HasValue))
            DrawCurve(e.Graphics, points.Select(point => (point.Input, point.Theoretical ?? point.Target)), bounds, minX, maxX, minY, maxY, Color.DarkOrange, DashStyle.Dot);
        DrawCurve(e.Graphics, points.Select(point => (point.Input, point.Actual)), bounds, minX, maxX, minY, maxY, Color.Navy, DashStyle.Solid);
        using (var limitedBrush = new SolidBrush(Color.Firebrick))
            foreach (var point in points.Where(point => point.IsSupplyLimited))
            {
                var x = bounds.Left + (float)((point.Input - minX) / (maxX - minX) * bounds.Width);
                var y = bounds.Bottom - (float)((point.Actual - minY) / (maxY - minY) * bounds.Height);
                e.Graphics.FillEllipse(limitedBrush, x - 3, y - 3, 6, 6);
            }
        using var titleFont = new Font(Font, FontStyle.Bold);
        e.Graphics.DrawString($"Uo versus {sweepInput}", titleFont, Brushes.Black, bounds.Left, 15);
        e.Graphics.DrawString($"{sweepInput} (V)", Font, Brushes.Black, bounds.Left + bounds.Width / 2 - 20, bounds.Bottom + 32);
        var state = e.Graphics.Save(); e.Graphics.TranslateTransform(18, bounds.Top + bounds.Height / 2 + 22); e.Graphics.RotateTransform(-90); e.Graphics.DrawString("Uo (V)", Font, Brushes.Black, 0, 0); e.Graphics.Restore(state);
        using var targetPen = new Pen(Color.Gray, 2) { DashStyle = DashStyle.Dash }; using var actualPen = new Pen(Color.Navy, 2);
        e.Graphics.DrawLine(targetPen, bounds.Right - 280, 22, bounds.Right - 250, 22); e.Graphics.DrawString("Target", Font, Brushes.Gray, bounds.Right - 245, 14);
        if(points.Any(point=>point.Theoretical.HasValue)){using var theoryPen=new Pen(Color.DarkOrange,2){DashStyle=DashStyle.Dot};e.Graphics.DrawLine(theoryPen,bounds.Right-185,22,bounds.Right-155,22);e.Graphics.DrawString("Theoretical",Font,Brushes.DarkOrange,bounds.Right-150,14);}
        e.Graphics.DrawLine(actualPen, bounds.Right - 90, 22, bounds.Right - 60, 22); e.Graphics.DrawString("Actual", Font, Brushes.Navy, bounds.Right - 55, 14);
        DrawCursor(e.Graphics, points);
    }

    private void DrawCursor(Graphics graphics, IReadOnlyList<AnalogAnalysisPoint> points)
    {
        var index = pinnedIndex ?? hoverIndex;
        if (index is null || index < 0 || index >= points.Count) return;
        var point = points[index.Value];
        var x = plotBounds.Left + (float)((point.Input - plotMinimumX) / (plotMaximumX - plotMinimumX) * plotBounds.Width);
        var targetY = plotBounds.Bottom - (float)((point.Target - plotMinimumY) / (plotMaximumY - plotMinimumY) * plotBounds.Height);
        var actualY = plotBounds.Bottom - (float)((point.Actual - plotMinimumY) / (plotMaximumY - plotMinimumY) * plotBounds.Height);
        using var cursorPen = new Pen(Color.DarkOrange, 1.2f) { DashStyle = DashStyle.Dash };
        graphics.DrawLine(cursorPen, x, plotBounds.Top, x, plotBounds.Bottom);
        using var targetBrush = new SolidBrush(Color.Gray); using var actualBrush = new SolidBrush(point.IsSupplyLimited ? Color.Firebrick : Color.Navy);
        graphics.FillEllipse(targetBrush, x - 4, targetY - 4, 8, 8); graphics.FillEllipse(actualBrush, x - 4, actualY - 4, 8, 8);
        var theoryText=point.Theoretical.HasValue?$"\nTheoretical Uo = {point.Theoretical.Value:G8} V":"";
        var text = $"{sweepInput} = {point.Input:G8} V\nTarget Uo = {point.Target:G8} V{theoryText}\nActual Uo = {point.Actual:G8} V\nError = {point.Actual - point.Target:G6} V{(pinnedIndex is null ? "" : "  [pinned]")}";
        var size = graphics.MeasureString(text, Font); var boxX = x + 12;
        if (boxX + size.Width + 14 > ClientSize.Width) boxX = x - size.Width - 20;
        var boxY = Math.Max(plotBounds.Top + 4, Math.Min(actualY - size.Height - 12, plotBounds.Bottom - size.Height - 12));
        var box = new RectangleF(boxX, boxY, size.Width + 12, size.Height + 9);
        using var background = new SolidBrush(Color.FromArgb(246, 255, 250, 225));
        graphics.FillRectangle(background, box); graphics.DrawRectangle(Pens.DarkGoldenrod, box.X, box.Y, box.Width, box.Height);
        graphics.DrawString(text, Font, Brushes.Black, box.X + 6, box.Y + 4);
    }

    private void DrawRail(Graphics graphics, double value, string label, RectangleF bounds, double minY, double maxY)
    {
        if (value < minY || value > maxY) return;
        var y = bounds.Bottom - (float)((value - minY) / (maxY - minY) * bounds.Height);
        using var pen = new Pen(Color.Firebrick, 1) { DashStyle = DashStyle.Dash };
        graphics.DrawLine(pen, bounds.Left, y, bounds.Right, y);
        graphics.DrawString(label, Font, Brushes.Firebrick, bounds.Left + 5, y + 2);
    }

    private static void DrawCurve(Graphics graphics, IEnumerable<(double X, double Y)> values, RectangleF bounds, double minX, double maxX, double minY, double maxY, Color color, DashStyle dash)
    {
        var points = values.Select(value => new PointF(bounds.Left + (float)((value.X - minX) / (maxX - minX) * bounds.Width), bounds.Bottom - (float)((value.Y - minY) / (maxY - minY) * bounds.Height))).ToArray();
        using var pen = new Pen(color, 2) { DashStyle = dash, LineJoin = LineJoin.Round }; graphics.DrawLines(pen, points);
    }
}
