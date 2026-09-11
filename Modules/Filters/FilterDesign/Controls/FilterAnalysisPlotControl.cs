using System.ComponentModel;
using System.Drawing.Drawing2D;
using CircuitModuleDesigner.Core.Analysis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterAnalysisPlotControl : UserControl
{
    private AcAnalysisResult? analysis;
    private FilterDesignRequest? request;
    private RectangleF magnitudeBounds;
    private RectangleF phaseBounds;
    private double minimumLogFrequency;
    private double maximumLogFrequency;
    private double magnitudeMinimum;
    private double magnitudeMaximum;
    private double phaseMinimum;
    private double phaseMaximum;
    private int? hoverIndex;
    private int? pinnedIndex;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AcAnalysisResult? Analysis { get => analysis; set { analysis = value; hoverIndex = null; pinnedIndex = null; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FilterDesignRequest? Request { get => request; set { request = value; Invalidate(); } }

    public FilterAnalysisPlotControl()
    {
        DoubleBuffered = true; ResizeRedraw = true; BackColor = SystemColors.Window; Cursor = Cursors.Cross;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (analysis == null || magnitudeBounds.Width <= 0 || e.X < magnitudeBounds.Left || e.X > magnitudeBounds.Right || e.Y < magnitudeBounds.Top || e.Y > phaseBounds.Bottom)
        {
            if (hoverIndex != null) { hoverIndex = null; Invalidate(); }
            return;
        }
        var log = minimumLogFrequency + (e.X - magnitudeBounds.Left) / magnitudeBounds.Width * (maximumLogFrequency - minimumLogFrequency);
        var index = FindNearestPoint(log);
        if (index != hoverIndex) { hoverIndex = index; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hoverIndex != null) { hoverIndex = null; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        pinnedIndex = pinnedIndex == hoverIndex ? null : hoverIndex;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.Clear(SystemColors.Window);
        if (analysis == null || analysis.Points.Count < 2)
        {
            e.Graphics.DrawString("No actual response analysis available.", Font, Brushes.DimGray, 18, 18); return;
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var outer = RectangleF.Inflate(ClientRectangle, -12, -10);
        if (outer.Width < 280 || outer.Height < 220) return;
        const float left = 88, right = 28, top = 24, bottom = 44, gap = 40;
        var plotWidth = outer.Width - left - right;
        var plotHeight = (outer.Height - top - bottom - gap) / 2;
        magnitudeBounds = new RectangleF(outer.Left + left, outer.Top + top, plotWidth, plotHeight);
        phaseBounds = new RectangleF(magnitudeBounds.Left, magnitudeBounds.Bottom + gap, plotWidth, plotHeight);
        minimumLogFrequency = Math.Log10(analysis.StartFrequencyHz); maximumLogFrequency = Math.Log10(analysis.StopFrequencyHz);
        magnitudeMaximum = Math.Max(10, Math.Ceiling(analysis.Points.Max(point => point.MagnitudeDb) / 10) * 10 + 5);
        magnitudeMinimum = DetermineMagnitudeMinimum();
        if (magnitudeMaximum - magnitudeMinimum < 50) magnitudeMinimum = magnitudeMaximum - 50;
        phaseMinimum = Math.Floor(analysis.Points.Min(point => point.PhaseDegrees) / 90) * 90;
        phaseMaximum = Math.Ceiling(analysis.Points.Max(point => point.PhaseDegrees) / 90) * 90;
        if (phaseMaximum - phaseMinimum < 180) { phaseMinimum -= 90; phaseMaximum += 90; }

        var magnitudeAxisTitle = analysis.MagnitudeReferenceLinear == 1 ? "Magnitude (dB)" : "Insertion Gain (dB)";
        DrawGrid(e.Graphics, magnitudeBounds, minimumLogFrequency, maximumLogFrequency, magnitudeMinimum, magnitudeMaximum, magnitudeAxisTitle, false);
        DrawGrid(e.Graphics, phaseBounds, minimumLogFrequency, maximumLogFrequency, phaseMinimum, phaseMaximum, "Phase (deg)", true);
        DrawSpecificationEdges(e.Graphics, magnitudeBounds, phaseBounds, minimumLogFrequency, maximumLogFrequency);
        DrawCurve(e.Graphics, magnitudeBounds, minimumLogFrequency, maximumLogFrequency, magnitudeMinimum, magnitudeMaximum, analysis.Points.Select(point => (point.FrequencyHz, point.MagnitudeDb)), Color.Navy);
        DrawCurve(e.Graphics, phaseBounds, minimumLogFrequency, maximumLogFrequency, phaseMinimum, phaseMaximum, analysis.Points.Select(point => (point.FrequencyHz, point.PhaseDegrees)), Color.DarkGreen);
        DrawCursor(e.Graphics);
        var frequencyLabel = "Frequency (Hz)";
        e.Graphics.DrawString(frequencyLabel, Font, Brushes.Black,
            phaseBounds.Left + (phaseBounds.Width - e.Graphics.MeasureString(frequencyLabel, Font).Width) / 2, phaseBounds.Bottom + 20);
    }

    private double DetermineMagnitudeMinimum()
    {
        if (request != null && request.StopbandAttenuationDb > 0)
        {
            var floorDepth = Math.Clamp(Math.Ceiling((request.StopbandAttenuationDb + 20) / 20) * 20, 60, 160);
            return -floorDepth;
        }
        return Math.Max(-160, Math.Min(-40, Math.Floor(analysis!.Points.Min(point => point.MagnitudeDb) / 20) * 20));
    }

    private int FindNearestPoint(double targetLog)
    {
        var points = analysis!.Points;
        var low = 0; var high = points.Count - 1;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Math.Log10(points[middle].FrequencyHz) < targetLog) low = middle + 1; else high = middle;
        }
        if (low == 0) return 0;
        return Math.Abs(Math.Log10(points[low].FrequencyHz) - targetLog) < Math.Abs(Math.Log10(points[low - 1].FrequencyHz) - targetLog) ? low : low - 1;
    }

    private void DrawCursor(Graphics graphics)
    {
        var index = pinnedIndex ?? hoverIndex;
        if (analysis == null || index is null || index < 0 || index >= analysis.Points.Count) return;
        var point = analysis.Points[index.Value];
        var x = MapX(Math.Log10(point.FrequencyHz), minimumLogFrequency, maximumLogFrequency, magnitudeBounds);
        var magnitudeY = Math.Clamp(MapY(point.MagnitudeDb, magnitudeMinimum, magnitudeMaximum, magnitudeBounds), magnitudeBounds.Top, magnitudeBounds.Bottom);
        var phaseY = Math.Clamp(MapY(point.PhaseDegrees, phaseMinimum, phaseMaximum, phaseBounds), phaseBounds.Top, phaseBounds.Bottom);
        using var cursorPen = new Pen(Color.DarkOrange, 1.2F) { DashStyle = DashStyle.Dash };
        graphics.DrawLine(cursorPen, x, magnitudeBounds.Top, x, phaseBounds.Bottom);
        using var markerBrush = new SolidBrush(Color.DarkOrange);
        graphics.FillEllipse(markerBrush, x - 4, magnitudeY - 4, 8, 8);
        graphics.FillEllipse(markerBrush, x - 4, phaseY - 4, 8, 8);
        var pinText = pinnedIndex is null ? "" : "  [pinned]";
        var text = $"f = {FormatFrequency(point.FrequencyHz)}\nMagnitude = {point.MagnitudeDb:0.###} dB\nPhase = {point.PhaseDegrees:0.###} deg{pinText}";
        var size = graphics.MeasureString(text, Font);
        var boxX = x + 10;
        if (boxX + size.Width + 12 > ClientSize.Width) boxX = x - size.Width - 18;
        var boxY = Math.Max(magnitudeBounds.Top + 4, magnitudeY - size.Height - 12);
        var box = new RectangleF(boxX, boxY, size.Width + 10, size.Height + 8);
        using var background = new SolidBrush(Color.FromArgb(245, 255, 250, 225));
        graphics.FillRectangle(background, box); graphics.DrawRectangle(Pens.DarkGoldenrod, box.X, box.Y, box.Width, box.Height);
        graphics.DrawString(text, Font, Brushes.Black, box.X + 5, box.Y + 4);
    }

    private void DrawGrid(Graphics graphics, RectangleF rectangle, double minLog, double maxLog, double minimum, double maximum, string axisTitle, bool drawFrequencyLabels)
    {
        using var gridPen = new Pen(Color.Gainsboro, 1); using var axisPen = new Pen(Color.DimGray, 1.2F);
        graphics.FillRectangle(Brushes.White, rectangle);
        var firstDecade = (int)Math.Floor(minLog); var lastDecade = (int)Math.Ceiling(maxLog);
        for (var decade = firstDecade; decade <= lastDecade; decade++)
        for (var multiplier = 1; multiplier < 10; multiplier++)
        {
            var log = decade + Math.Log10(multiplier); if (log < minLog || log > maxLog) continue;
            var x = MapX(log, minLog, maxLog, rectangle); graphics.DrawLine(gridPen, x, rectangle.Top, x, rectangle.Bottom);
            if (multiplier == 1 && drawFrequencyLabels)
            {
                var label = EngineeringFrequency(Math.Pow(10, decade));
                var size = graphics.MeasureString(label, Font);
                var labelX = x - size.Width / 2;
                if (labelX >= rectangle.Left - 4 && labelX + size.Width <= rectangle.Right + 4)
                    graphics.DrawString(label, Font, Brushes.DimGray, labelX, rectangle.Bottom + 3);
            }
        }
        const int horizontalDivisions = 5;
        for (var division = 0; division <= horizontalDivisions; division++)
        {
            var value = maximum - division * (maximum - minimum) / horizontalDivisions;
            var y = rectangle.Top + division * rectangle.Height / horizontalDivisions;
            graphics.DrawLine(gridPen, rectangle.Left, y, rectangle.Right, y);
            var label = value.ToString("0.#"); var size = graphics.MeasureString(label, Font); graphics.DrawString(label, Font, Brushes.DimGray, rectangle.Left - size.Width - 6, y - size.Height / 2);
        }
        graphics.DrawRectangle(axisPen, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        var state = graphics.Save();
        var axisSize = graphics.MeasureString(axisTitle, Font);
        graphics.TranslateTransform(rectangle.Left - 72, rectangle.Top + (rectangle.Height + axisSize.Width) / 2);
        graphics.RotateTransform(-90);
        graphics.DrawString(axisTitle, Font, Brushes.Black, 0, 0);
        graphics.Restore(state);
    }

    private void DrawSpecificationEdges(Graphics graphics, RectangleF magnitude, RectangleF phase, double minLog, double maxLog)
    {
        if (request == null) return;
        var edges = request.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass
            ? new[] { (request.PassbandEdgeHz, "fp"), (request.StopbandEdgeHz, "fs") }
            : request.ResponseType == FilterResponseType.BandPass
                ? new[] { (request.StopbandLowerHz, "fs1"), (request.PassbandLowerHz, "fp1"), (request.PassbandUpperHz, "fp2"), (request.StopbandUpperHz, "fs2") }
                : new[] { (request.PassbandLowerHz, "fp1"), (request.StopbandLowerHz, "fs1"), (request.StopbandUpperHz, "fs2"), (request.PassbandUpperHz, "fp2") };
        using var pen = new Pen(Color.Firebrick, 1) { DashStyle = DashStyle.Dash };
        var labelRow = 0;
        foreach (var edge in edges.Where(edge => edge.Item1 > 0))
        {
            var log = Math.Log10(edge.Item1); if (log < minLog || log > maxLog) continue;
            var x = MapX(log, minLog, maxLog, magnitude); graphics.DrawLine(pen, x, magnitude.Top, x, phase.Bottom);
            graphics.DrawString(edge.Item2, Font, Brushes.Firebrick, x + 2, magnitude.Top + 18 + 14 * (labelRow++ % 2));
        }
    }

    private static void DrawCurve(Graphics graphics, RectangleF rectangle, double minLog, double maxLog, double minimum, double maximum, IEnumerable<(double Frequency, double Value)> values, Color color)
    {
        var points = values.Select(value => new PointF(MapX(Math.Log10(value.Frequency), minLog, maxLog, rectangle),
            Math.Clamp(MapY(value.Value, minimum, maximum, rectangle), rectangle.Top, rectangle.Bottom))).ToArray();
        if (points.Length <= 1) return;
        var state = graphics.Save();
        graphics.SetClip(rectangle);
        using var pen = new Pen(color, 1.8F) { LineJoin = LineJoin.Round };
        graphics.DrawLines(pen, points);
        graphics.Restore(state);
    }
    private static float MapX(double log, double minimum, double maximum, RectangleF rectangle) => rectangle.Left + (float)((log - minimum) / (maximum - minimum) * rectangle.Width);
    private static float MapY(double value, double minimum, double maximum, RectangleF rectangle) => rectangle.Bottom - (float)((value - minimum) / (maximum - minimum) * rectangle.Height);
    private static string FormatFrequency(double value) => value >= 1e6 ? $"{value / 1e6:0.######} MHz" : value >= 1e3 ? $"{value / 1e3:0.######} kHz" : $"{value:0.######} Hz";
    private static string EngineeringFrequency(double value) => value >= 1e6 ? $"{value / 1e6:0.#}M" : value >= 1e3 ? $"{value / 1e3:0.#}k" : value >= 1 ? $"{value:0.#}" : $"{value:G2}";
}
