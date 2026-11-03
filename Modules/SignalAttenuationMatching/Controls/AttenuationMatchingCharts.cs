using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Controls;

internal static class ChartTheme
{
    public static readonly Color Ink = Color.FromArgb(31, 41, 55);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);
    public static readonly Color Grid = Color.FromArgb(226, 232, 240);
    public static readonly Color Axis = Color.FromArgb(148, 163, 184);
    public static readonly Color Accent = Color.FromArgb(70, 70, 70);
    public static readonly Color Loss = Color.FromArgb(25, 25, 25);
    public static readonly Color Design = Color.FromArgb(30, 30, 30);
    public static readonly Color Panel = Color.FromArgb(248, 250, 252);
    public static readonly Color Node = Color.FromArgb(30, 41, 59);

    public static Font TitleFont => new("Segoe UI Semibold", 10.5f);
    public static Font BodyFont => new("Segoe UI", 8.5f);
    public static Font SmallFont => new("Segoe UI", 7.5f);

    public static string Frequency(double value)
    {
        if (!double.IsFinite(value)) return "N/A";
        if (Math.Abs(value) >= 1e9) return (value / 1e9).ToString("0.###", CultureInfo.InvariantCulture) + " GHz";
        if (Math.Abs(value) >= 1e6) return (value / 1e6).ToString("0.###", CultureInfo.InvariantCulture) + " MHz";
        if (Math.Abs(value) >= 1e3) return (value / 1e3).ToString("0.###", CultureInfo.InvariantCulture) + " kHz";
        return value.ToString("0.###", CultureInfo.InvariantCulture) + " Hz";
    }

    public static string Db(double value) => double.IsFinite(value) ? value.ToString("0.#", CultureInfo.InvariantCulture) + " dB" : "N/A";

    public static double NiceMaximum(double value)
    {
        if (!(value > 0) || !double.IsFinite(value)) return 1;
        var exponent = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var fraction = value / exponent;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return nice * exponent;
    }
}

internal static class CursorReadout
{
    public static void Draw(Graphics graphics, Point cursor, string text, Size clientSize)
    {
        using var font = new Font("Segoe UI", 8f);
        var size = graphics.MeasureString(text, font);
        var x = Math.Min(cursor.X + 14f, clientSize.Width - size.Width - 6f);
        var y = Math.Min(cursor.Y + 14f, clientSize.Height - size.Height - 6f);
        x = Math.Max(4f, x); y = Math.Max(4f, y);
        using var background = new SolidBrush(Color.FromArgb(220, 30, 30, 30));
        using var foreground = new SolidBrush(Color.White);
        graphics.FillRectangle(background, x - 3, y - 2, size.Width + 6, size.Height + 4);
        graphics.DrawString(text, font, foreground, x, y);
    }
}
public sealed class AttenuationMatchingResponsePlotControl : UserControl
{
    private AcSweepResult? sweep;
    private double designFrequencyHz;
    private AttenuationMatchingDesignRequest? request;

    public AttenuationMatchingResponsePlotControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        ResizeRedraw = true;
        MinimumSize = new Size(360, 220);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AcSweepResult? Sweep
    {
        get => sweep;
        set { sweep = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double DesignFrequencyHz
    {
        get => designFrequencyHz;
        set { designFrequencyHz = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AttenuationMatchingDesignRequest? Request
    {
        get => request;
        set { request = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point? CursorPosition => cursor;

    private Point? cursor;
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); cursor = e.Location; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); cursor = null; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var title = ChartTheme.TitleFont;
        using var body = ChartTheme.BodyFont;
        using var small = ChartTheme.SmallFont;

        var points = sweep?.Points ?? [];
        if (points.Count < 2)
        {
            DrawEmpty(g, "Frequency response", string.Empty, title, body);
            return;
        }

        const float left = 72;
        const float right = 74;
        const float top = 58;
        const float bottom = 52;
        var width = Math.Max(40, ClientSize.Width - left - right);
        var height = Math.Max(40, ClientSize.Height - top - bottom);
        var minLog = Math.Log10(points.Min(point => point.FrequencyHz));
        var maxLog = Math.Log10(points.Max(point => point.FrequencyHz));
        if (!double.IsFinite(minLog) || !double.IsFinite(maxLog) || maxLog - minLog < 1e-12)
        {
            DrawEmpty(g, "Frequency response", string.Empty, title, body);
            return;
        }

        var attenuation = points.Select(point => point.AttenuationDb).Where(double.IsFinite).ToArray();
        var returnLoss = points.SelectMany(point => new[] { point.InputReturnLossDb, point.OutputReturnLossDb }).Where(double.IsFinite).ToArray();
        var attenuationMin = Math.Min(0, Math.Floor(attenuation.DefaultIfEmpty(0).Min() / 5) * 5);
        var attenuationMax = Math.Max(attenuationMin + 10, ChartTheme.NiceMaximum(attenuation.DefaultIfEmpty(10).Max() * 1.08));
        var returnLossMax = Math.Max(20, ChartTheme.NiceMaximum(returnLoss.DefaultIfEmpty(20).Max() * 1.05));

        float X(double frequency) => left + (float)((Math.Log10(frequency) - minLog) / (maxLog - minLog) * width);
        float YLoss(double value) => top + (float)((attenuationMax - Math.Clamp(value, attenuationMin, attenuationMax)) / (attenuationMax - attenuationMin) * height);
        float YRl(double value) => top + (float)((returnLossMax - Math.Clamp(value, 0, returnLossMax)) / returnLossMax * height);

        var modeTitle = request?.Mode == CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums.DesignMode.ImpedanceMatching ? request.MatchingPlan.Kind switch
        {
            MatchingDesignKind.MultiFrequency => "Multi-frequency matching response",
            MatchingDesignKind.Broadband => "Broadband matching response",
            _ => "Single-frequency matching response"
        } : "Frequency response";
        g.DrawString(modeTitle, title, new SolidBrush(ChartTheme.Ink), left, 14);

        using var grid = new Pen(ChartTheme.Grid, 1);
        using var axis = new Pen(ChartTheme.Axis, 1.2f);
        for (var index = 0; index <= 4; index++)
        {
            var y = top + height * index / 4;
            g.DrawLine(index is 0 or 4 ? axis : grid, left, y, left + width, y);
            var db = attenuationMax - (attenuationMax - attenuationMin) * index / 4;
            g.DrawString(db.ToString("0.#", CultureInfo.InvariantCulture), small, new SolidBrush(ChartTheme.Loss), 8, y - 7);
            var rl = returnLossMax * (4 - index) / 4;
            g.DrawString(rl.ToString("0.#", CultureInfo.InvariantCulture), small, new SolidBrush(ChartTheme.Accent), left + width + 8, y - 7);
        }
        for (var index = 0; index <= 5; index++)
        {
            var x = left + width * index / 5;
            g.DrawLine(index is 0 or 5 ? axis : grid, x, top, x, top + height);
            var frequency = Math.Pow(10, minLog + (maxLog - minLog) * index / 5);
            var label = ChartTheme.Frequency(frequency);
            var size = g.MeasureString(label, small);
            g.DrawString(label, small, new SolidBrush(ChartTheme.Muted), x - size.Width / 2, top + height + 7);
        }

        var plot = new RectangleF(left, top, width, height);
        var oldClip = g.Clip;
        g.SetClip(plot);
        if (request?.Mode == CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums.DesignMode.ImpedanceMatching)
        {
            using var targetFill = new SolidBrush(Color.FromArgb(28, 34, 139, 94));
            using var targetPen = new Pen(Color.FromArgb(52, 120, 74), 1.2f) { DashStyle = DashStyle.Dot };
            foreach (var band in request.MatchingPlan.EffectiveBands(request))
            {
                if (band.StopFrequencyHz < points[0].FrequencyHz || band.StartFrequencyHz > points[^1].FrequencyHz) continue;
                var x1 = X(Math.Clamp(band.StartFrequencyHz, points[0].FrequencyHz, points[^1].FrequencyHz));
                var x2 = X(Math.Clamp(band.StopFrequencyHz, points[0].FrequencyHz, points[^1].FrequencyHz));
                if (band.IsPoint)
                    g.DrawLine(targetPen, x1, top, x1, top + height);
                else
                    g.FillRectangle(targetFill, Math.Min(x1, x2), top, Math.Max(2, Math.Abs(x2 - x1)), height);
                g.DrawLine(targetPen, x1, YRl(band.TargetInputReturnLossDb), Math.Max(x1 + 1, x2), YRl(band.TargetInputReturnLossDb));
                g.DrawLine(targetPen, x1, YLoss(band.MaximumInsertionLossDb), Math.Max(x1 + 1, x2), YLoss(band.MaximumInsertionLossDb));
            }
        }
        using var lossPen = new Pen(ChartTheme.Loss, 2.3f) { LineJoin = LineJoin.Round };
        using var rlPen = new Pen(ChartTheme.Accent, 2.1f) { LineJoin = LineJoin.Round, DashStyle = DashStyle.Dash };
        using var rlOutPen = new Pen(Color.FromArgb(100, 116, 139), 1.8f) { LineJoin = LineJoin.Round, DashStyle = DashStyle.Dot };
        g.DrawLines(lossPen, points.Select(point => new PointF(X(point.FrequencyHz), YLoss(point.AttenuationDb))).ToArray());
        g.DrawLines(rlPen, points.Select(point => new PointF(X(point.FrequencyHz), YRl(point.InputReturnLossDb))).ToArray());
        g.DrawLines(rlOutPen, points.Select(point => new PointF(X(point.FrequencyHz), YRl(point.OutputReturnLossDb))).ToArray());
        if (designFrequencyHz > 0 && designFrequencyHz >= points[0].FrequencyHz && designFrequencyHz <= points[^1].FrequencyHz)
        {
            using var design = new Pen(ChartTheme.Design, 1.3f) { DashStyle = DashStyle.Dash };
            var x = X(designFrequencyHz);
            g.DrawLine(design, x, top, x, top + height);
            var nearest = points.MinBy(point => Math.Abs(Math.Log(point.FrequencyHz) - Math.Log(designFrequencyHz)))!;
            using var marker = new SolidBrush(ChartTheme.Design);
            g.FillEllipse(marker, x - 3.5f, YLoss(nearest.AttenuationDb) - 3.5f, 7, 7);
            g.FillEllipse(marker, x - 3.5f, YRl(nearest.InputReturnLossDb) - 3.5f, 7, 7);
        }
        if (sweep?.BandMetrics is { Count: > 0 })
        {
            foreach (var band in sweep.BandMetrics)
            {
                var nearest = points.MinBy(point => Math.Abs(Math.Log(point.FrequencyHz) - Math.Log(band.WorstFrequencyHz)))!;
                using var marker = new SolidBrush(band.Passed ? Color.FromArgb(35, 130, 75) : Color.FromArgb(190, 45, 45));
                var x = X(nearest.FrequencyHz); var y = YRl(nearest.InputReturnLossDb);
                g.FillEllipse(marker, x - 4, y - 4, 8, 8);
            }
        }
        g.Clip = oldClip;

        if (designFrequencyHz > 0)
            g.DrawString("f0", small, new SolidBrush(ChartTheme.Design), Math.Clamp(X(designFrequencyHz), left + 3, left + width - 18), top + 4);
        g.DrawString("Attenuation (dB)", small, new SolidBrush(ChartTheme.Loss), 8, top - 16);
        g.DrawString("Input return loss (dB)", small, new SolidBrush(ChartTheme.Accent), left + width - 108, top - 16);

        var legendX = left + 10;
        var legendY = top + 8;
        using var legendBack = new SolidBrush(Color.FromArgb(238, 255, 255, 255));
        g.FillRectangle(legendBack, legendX - 3, legendY - 2, 180, 55);
        g.DrawLine(lossPen, legendX, legendY + 6, legendX + 22, legendY + 6);
        g.DrawString("Attenuation", body, new SolidBrush(ChartTheme.Ink), legendX + 28, legendY - 2);
        g.DrawLine(rlPen, legendX, legendY + 22, legendX + 22, legendY + 22);
        g.DrawString("Input return loss", body, new SolidBrush(ChartTheme.Ink), legendX + 28, legendY + 14);
        g.DrawLine(rlOutPen, legendX, legendY + 38, legendX + 22, legendY + 38);
        g.DrawString("Output return loss", body, new SolidBrush(ChartTheme.Ink), legendX + 28, legendY + 30);
        if (cursor is { } pointer && pointer.X >= left && pointer.X <= left + width && pointer.Y >= top && pointer.Y <= top + height)
        {
            var frequency = Math.Pow(10, minLog + (pointer.X - left) / width * (maxLog - minLog));
            var nearest = points.MinBy(point => Math.Abs(Math.Log(point.FrequencyHz) - Math.Log(frequency)))!;
            CursorReadout.Draw(g, pointer, $"x {ChartTheme.Frequency(nearest.FrequencyHz)}   y {nearest.AttenuationDb:0.##} dB   RL {nearest.InputReturnLossDb:0.##} dB", ClientSize);
        }
    }

    private void DrawEmpty(Graphics g, string titleText, string message, Font title, Font body)
    {
        g.DrawString(titleText, title, new SolidBrush(ChartTheme.Ink), 18, 16);
        using var box = new SolidBrush(ChartTheme.Panel);
        g.FillRectangle(box, 18, 50, Math.Max(20, ClientSize.Width - 36), Math.Max(20, ClientSize.Height - 68));

    }
}

public sealed class AttenuationMatchingSmithChartControl : UserControl
{
    private AcSweepResult? sweep;
    private double referenceImpedanceOhm = 50;
    private double designFrequencyHz;
    private AttenuationMatchingDesignRequest? request;

    public AttenuationMatchingSmithChartControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        ResizeRedraw = true;
        MinimumSize = new Size(300, 260);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AcSweepResult? Sweep
    {
        get => sweep;
        set { sweep = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ReferenceImpedanceOhm
    {
        get => referenceImpedanceOhm;
        set { referenceImpedanceOhm = value > 0 ? value : 50; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double DesignFrequencyHz
    {
        get => designFrequencyHz;
        set { designFrequencyHz = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AttenuationMatchingDesignRequest? Request
    {
        get => request;
        set { request = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point? CursorPosition => cursor;

    private Point? cursor;
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); cursor = e.Location; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); cursor = null; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var title = ChartTheme.TitleFont;
        using var body = ChartTheme.BodyFont;
        using var small = ChartTheme.SmallFont;
        var points = sweep?.Points;
        var maximumGamma = points is { Count: > 0 }
            ? points.Max(point => Math.Sqrt(point.InputReflectionCoefficient.Real * point.InputReflectionCoefficient.Real +
                                            point.InputReflectionCoefficient.Imaginary * point.InputReflectionCoefficient.Imaginary))
            : double.NaN;
        var matched = double.IsFinite(maximumGamma) && maximumGamma < .02;
        g.DrawString("Smith chart", title, new SolidBrush(ChartTheme.Ink), 16, 12);

        const float top = 58f;
        const float bottom = 30f;
        var infoWidth = 0;
        var usableWidth = ClientSize.Width - 48;
        var radius = Math.Min(usableWidth / 2f, (ClientSize.Height - top - bottom - 9) / 2f);
        if (radius < 40)
        {
            return;
        }
        var groupWidth = radius * 2 + (infoWidth > 0 ? infoWidth + 24 : 0);
        var groupLeft = Math.Max(16, (ClientSize.Width - groupWidth) / 2f);
        var cx = groupLeft + radius;
        var cy = top + radius + 4;

        using var outer = new GraphicsPath();
        outer.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
        using var outerFill = new SolidBrush(Color.FromArgb(250, 252, 255));
        g.FillPath(outerFill, outer);
        using var outerPen = new Pen(ChartTheme.Axis, 1.6f);
        g.DrawPath(outerPen, outer);

        var oldClip = g.Clip;
        g.SetClip(outer, CombineMode.Intersect);
        using var grid = new Pen(ChartTheme.Grid, 1);
        using var reactanceGrid = new Pen(Color.FromArgb(219, 227, 238), 1);
        using var vswrGrid = new Pen(Color.FromArgb(155, 177, 202), 1) { DashStyle = DashStyle.Dot };
        foreach (var gamma in new[] { .2, .4, .6, .8 })
        {
            var circleRadius = (float)(radius * gamma);
            g.DrawEllipse(vswrGrid, cx - circleRadius, cy - circleRadius, circleRadius * 2, circleRadius * 2);
        }

        if (request?.Mode == CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums.DesignMode.ImpedanceMatching &&
            request.MatchingPlan.Kind != MatchingDesignKind.SingleFrequency)
        {
            var requiredReturnLossDb = request.MatchingPlan.EffectiveBands(request)
                .Max(band => band.TargetInputReturnLossDb);
            var allowableGamma = Math.Pow(10, -requiredReturnLossDb / 20);
            if (allowableGamma is > 0 and < 1 && double.IsFinite(allowableGamma))
            {
                var requirementRadius = (float)(radius * allowableGamma);
                using var requirementPen = new Pen(Color.FromArgb(58, 118, 82), 1.7f)
                {
                    DashStyle = DashStyle.Dash
                };
                g.DrawEllipse(requirementPen, cx - requirementRadius, cy - requirementRadius,
                    requirementRadius * 2, requirementRadius * 2);
            }
        }

        foreach (var resistance in new[] { .2, .5, 1d, 2d, 5d })
        {
            var r = (float)(radius / (1 + resistance));
            var x = (float)(cx + radius * resistance / (1 + resistance));
            g.DrawEllipse(grid, x - r, cy - r, r * 2, r * 2);
        }
        foreach (var reactance in new[] { .2, .5, 1d, 2d, 5d })
        {
            foreach (var sign in new[] { 1d, -1d })
            {
                var x = (float)(cx + radius);
                var r = (float)(radius / reactance);
                var y = (float)(cy - radius / (sign * reactance));
                g.DrawEllipse(reactanceGrid, x - r, y - r, r * 2, r * 2);
            }
        }
        g.DrawLine(new Pen(ChartTheme.Axis, 1.1f), cx - radius, cy, cx + radius, cy);
        g.Clip = oldClip;

        using var resistanceLabel = new SolidBrush(ChartTheme.Muted);
        g.DrawString("0", small, resistanceLabel, cx - radius - 9, cy + 7);
        g.DrawString("0.2", small, resistanceLabel, (float)(cx - radius * .6667) - 7, cy + 7);
        g.DrawString("0.5", small, resistanceLabel, (float)(cx - radius * .3333) - 7, cy + 7);
        g.DrawString("1", small, resistanceLabel, cx - 2, cy + 7);
        g.DrawString("2", small, resistanceLabel, (float)(cx + radius * .3333) - 2, cy + 7);
        g.DrawString("5", small, resistanceLabel, (float)(cx + radius * .6667) - 2, cy + 7);
        g.DrawString("∞", small, resistanceLabel, cx + radius - 3, cy + 7);

        var reactanceLabels = new (double X, string Text, float Angle)[]
        {
            (.2, "+j0.2", 28), (.5, "+j0.5", 52), (1, "+j1", 74), (2, "+j2", 104), (5, "+j5", 128)
        };
        foreach (var (_, text, angle) in reactanceLabels)
        {
            var theta = angle * Math.PI / 180;
            var lx = (float)(cx + radius * Math.Cos(theta));
            var ly = (float)(cy - radius * Math.Sin(theta));
            g.DrawString(text, small, new SolidBrush(ChartTheme.Muted), lx - 11, ly - 7);
        }
        var negativeLabels = new (string Text, float Angle)[] { ("−j0.5", 308), ("−j1", 286), ("−j2", 256), ("−j5", 232) };
        foreach (var (text, angle) in negativeLabels)
        {
            var theta = angle * Math.PI / 180;
            var lx = (float)(cx + radius * Math.Cos(theta));
            var ly = (float)(cy - radius * Math.Sin(theta));
            g.DrawString(text, small, new SolidBrush(ChartTheme.Muted), lx - 12, ly - 7);
        }

        if (points is { Count: > 1 })
        {
            var trace = points.Select(point => new PointF(
                cx + (float)point.InputReflectionCoefficient.Real * radius,
                cy - (float)point.InputReflectionCoefficient.Imaginary * radius)).ToArray();
            using var tracePen = new Pen(ChartTheme.Accent, 2.1f) { LineJoin = LineJoin.Round };
            g.DrawLines(tracePen, trace);
            using var startBrush = new SolidBrush(Color.FromArgb(148, 163, 184));
            g.FillEllipse(startBrush, trace[0].X - 3, trace[0].Y - 3, 6, 6);
            var nearest = designFrequencyHz > 0
                ? points.MinBy(point => Math.Abs(Math.Log(point.FrequencyHz) - Math.Log(designFrequencyHz)))!
                : points[points.Count / 2];
            var markerX = cx + (float)nearest.InputReflectionCoefficient.Real * radius;
            var markerY = cy - (float)nearest.InputReflectionCoefficient.Imaginary * radius;
            using var marker = new SolidBrush(ChartTheme.Loss);
            g.FillEllipse(marker, markerX - 4.5f, markerY - 4.5f, 9, 9);
            using var markerRing = new Pen(Color.White, 1.5f);
            g.DrawEllipse(markerRing, markerX - 4.5f, markerY - 4.5f, 9, 9);
        }
        else
        {
        }

        if (cursor is { } pointer && points is { Count: > 0 })
        {
            var real = (pointer.X - cx) / radius;
            var imaginary = (cy - pointer.Y) / radius;
            if (real * real + imaginary * imaginary <= 1.01)
            {
                var nearest = points.MinBy(point =>
                {
                    var dx = point.InputReflectionCoefficient.Real - real;
                    var dy = point.InputReflectionCoefficient.Imaginary - imaginary;
                    return dx * dx + dy * dy;
                })!;
                var impedance = nearest.InputImpedance;
                CursorReadout.Draw(g, pointer,
                    $"f {ChartTheme.Frequency(nearest.FrequencyHz)}   Zin {impedance.Real:0.###} {(impedance.Imaginary >= 0 ? "+" : "−")} j{Math.Abs(impedance.Imaginary):0.###} Ω",
                    ClientSize);
            }
        }
    }
}


public sealed class AttenuationMatchingTopologyPreviewControl : UserControl
{
    private TopologyFamily? family;
    private MatchingResponse response;

    public AttenuationMatchingTopologyPreviewControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        ResizeRedraw = true;
        MinimumSize = new Size(300, 170);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TopologyFamily? Family
    {
        get => family;
        set { family = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MatchingResponse Response
    {
        get => response;
        set { response = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var title = ChartTheme.TitleFont;
        using var body = ChartTheme.BodyFont;
        using var small = ChartTheme.SmallFont;

        if (family is null)
        {
            return;
        }

        var (name, _) = Describe(family.Value, response);
        g.DrawString(name, title, new SolidBrush(ChartTheme.Ink), 14, 10);

        var left = 44f;
        var right = ClientSize.Width - 44f;
        var signalY = Math.Max(86f, ClientSize.Height * .52f);
        var groundY = Math.Min(ClientSize.Height - 28f, signalY + 58f);
        using var wire = new Pen(ChartTheme.Node, 1.8f) { LineJoin = LineJoin.Round };
        using var symbol = new Pen(ChartTheme.Node, 1.7f) { LineJoin = LineJoin.Round };
        using var thin = new Pen(ChartTheme.Muted, 1.1f);

        g.DrawLine(wire, left - 22, signalY, right + 22, signalY);
        DrawPort(g, left - 26, signalY, "IN", true, small);
        DrawPort(g, right + 26, signalY, "OUT", false, small);

        switch (family.Value)
        {
            case TopologyFamily.TPads:
            {
                var x1 = left + (right - left) * .24f;
                var x2 = left + (right - left) * .76f;
                DrawSeries(g, symbol, body, "R1", "R", x1, signalY);
                DrawSeries(g, symbol, body, "R2", "R", x2, signalY);
                DrawShunt(g, symbol, thin, body, "R3", "R", x1 + (x2 - x1) / 2, signalY, groundY);
                g.DrawLine(wire, x1 + 43, signalY, x2 - 43, signalY);
                DrawNode(g, x1 + 43, signalY);
                DrawNode(g, (x1 + x2) / 2, signalY);
                g.DrawLine(wire, (x1 + x2) / 2, signalY, (x1 + x2) / 2, signalY + 13);
                break;
            }
            case TopologyFamily.PiPads:
            {
                var x1 = left + (right - left) * .24f;
                var x2 = left + (right - left) * .76f;
                DrawShunt(g, symbol, thin, body, "R1", "R", x1, signalY, groundY);
                DrawSeries(g, symbol, body, "R2", "R", (x1 + x2) / 2, signalY);
                DrawShunt(g, symbol, thin, body, "R3", "R", x2, signalY, groundY);
                g.DrawLine(wire, x1, signalY, x1, signalY + 12);
                g.DrawLine(wire, x1 + 43, signalY, x2 - 43, signalY);
                DrawNode(g, x1, signalY);
                DrawNode(g, x2, signalY);
                break;
            }
            case TopologyFamily.LPads:
            {
                var seriesX = left + (right - left) * .42f;
                var nodeX = left + (right - left) * .66f;
                DrawSeries(g, symbol, body, "R1", "R", seriesX, signalY);
                DrawShunt(g, symbol, thin, body, "R2", "R", nodeX, signalY, groundY);
                g.DrawLine(wire, seriesX + 43, signalY, nodeX, signalY);
                DrawNode(g, nodeX, signalY);
                break;
            }
            case TopologyFamily.CascadedPads:
            {
                var stageWidth = (right - left) / 3f;
                for (var index = 0; index < 3; index++)
                {
                    var x = left + stageWidth * index + stageWidth * .28f;
                    var node = left + stageWidth * index + stageWidth * .62f;
                    DrawSeries(g, symbol, body, $"R{index * 2 + 1}", "R", x, signalY);
                    DrawShunt(g, symbol, thin, body, $"R{index * 2 + 2}", "R", node, signalY, groundY);
                    g.DrawLine(wire, x + 43, signalY, node, signalY);
                    DrawNode(g, node, signalY);
                    if (index < 2) g.DrawLine(wire, node, signalY, left + stageWidth * (index + 1) + stageWidth * .28f - 43, signalY);
                }
                g.DrawString("stage 1", small, Brushes.Gray, left + stageWidth * .10f, groundY + 6);
                g.DrawString("stage 2", small, Brushes.Gray, left + stageWidth * 1.10f, groundY + 6);
                g.DrawString("stage 3", small, Brushes.Gray, left + stageWidth * 2.10f, groundY + 6);
                break;
            }
            case TopologyFamily.LMatching:
            {
                var (series, shunt) = Reactive(response);
                var seriesX = left + (right - left) * .38f;
                var nodeX = left + (right - left) * .66f;
                DrawSeries(g, symbol, body, series + "1", series, seriesX, signalY);
                DrawShunt(g, symbol, thin, body, shunt + "2", shunt, nodeX, signalY, groundY);
                g.DrawLine(wire, seriesX + 43, signalY, nodeX, signalY);
                DrawNode(g, nodeX, signalY);
                break;
            }
            case TopologyFamily.PiMatching:
            {
                var x1 = left + (right - left) * .23f;
                var x2 = left + (right - left) * .77f;
                if (response == MatchingResponse.Mixed)
                {
                    DrawShunt(g, symbol, thin, body, "C1", "C", x1, signalY, groundY);
                    DrawSeries(g, symbol, body, "L2", "L", x1 + (x2 - x1) * .32f, signalY);
                    DrawSeries(g, symbol, body, "C3", "C", x1 + (x2 - x1) * .68f, signalY);
                    DrawShunt(g, symbol, thin, body, "L4", "L", x2, signalY, groundY);
                    g.DrawLine(wire, x1, signalY, x2, signalY);
                    DrawNode(g, x1, signalY); DrawNode(g, x2, signalY);
                }
                else
                {
                    var (series, shunt) = Reactive(response);
                    DrawShunt(g, symbol, thin, body, shunt + "1", shunt, x1, signalY, groundY);
                    DrawSeries(g, symbol, body, series + "2", series, (x1 + x2) / 2, signalY);
                    DrawShunt(g, symbol, thin, body, shunt + "3", shunt, x2, signalY, groundY);
                    g.DrawLine(wire, x1, signalY, x2, signalY);
                    DrawNode(g, x1, signalY); DrawNode(g, x2, signalY);
                }
                break;
            }
            case TopologyFamily.TMatching:
            {
                var x1 = left + (right - left) * .25f;
                var x2 = left + (right - left) * .75f;
                var center = (x1 + x2) / 2;
                if (response == MatchingResponse.Mixed)
                {
                    DrawSeries(g, symbol, body, "L1", "L", x1, signalY);
                    DrawShunt(g, symbol, thin, body, "C2", "C", center - 22, signalY, groundY);
                    DrawShunt(g, symbol, thin, body, "L3", "L", center + 22, signalY, groundY);
                    DrawSeries(g, symbol, body, "C4", "C", x2, signalY);
                }
                else
                {
                    var (series, shunt) = Reactive(response);
                    DrawSeries(g, symbol, body, series + "1", series, x1, signalY);
                    DrawShunt(g, symbol, thin, body, shunt + "2", shunt, center, signalY, groundY);
                    DrawSeries(g, symbol, body, series + "3", series, x2, signalY);
                }
                g.DrawLine(wire, x1 + 43, signalY, x2 - 43, signalY);
                DrawNode(g, center, signalY);
                break;
            }
            default:
            {
                var mid = (left + right) / 2;
                using var divider = new Pen(ChartTheme.Muted, 1.1f) { DashStyle = DashStyle.Dash };
                g.DrawLine(divider, mid, signalY - 28, mid, groundY);
                DrawSeries(g, symbol, body, "R1", "R", left + (right - left) * .25f, signalY);
                DrawShunt(g, symbol, thin, body, "R2", "R", left + (right - left) * .40f, signalY, groundY);
                DrawSeries(g, symbol, body, "L3", "L", left + (right - left) * .62f, signalY);
                DrawShunt(g, symbol, thin, body, "C4", "C", left + (right - left) * .78f, signalY, groundY);
                g.DrawString("PAD", small, Brushes.Gray, mid - 70, groundY + 6);
                g.DrawString("LC MATCH", small, Brushes.Gray, mid + 8, groundY + 6);
                break;
            }
        }

    }

    private static (string Name, string Subtitle) Describe(TopologyFamily family, MatchingResponse response) => family switch
    {
        TopologyFamily.TPads => ("T-Pad", "Series resistor · shunt resistor · series resistor"),
        TopologyFamily.PiPads => ("Pi-Pad", "Shunt resistor · series resistor · shunt resistor"),
        TopologyFamily.LPads => ("L-Pad", "Series resistor with output shunt; fixed minimum loss"),
        TopologyFamily.CascadedPads => ("Cascaded Pads", "Three matched T stages for higher loss / lower stress"),
        TopologyFamily.LMatching => ("L Matching", $"{ResponseName(response)} L-section; one series and one shunt element"),
        TopologyFamily.PiMatching => ("Pi Matching", $"{ResponseName(response)} Pi network; virtual-resistance synthesis"),
        TopologyFamily.TMatching => ("T Matching", $"{ResponseName(response)} T network; virtual-resistance synthesis"),
        _ => ("Pad + LC Matching", "Pad and LC sections analyzed as one cascade")
    };

    private static string ResponseName(MatchingResponse response) => response switch
    {
        MatchingResponse.LowPass => "Low-pass",
        MatchingResponse.HighPass => "High-pass",
        _ => "Mixed-reactance"
    };

    private static (string Series, string Shunt) Reactive(MatchingResponse response) => response switch
    {
        MatchingResponse.HighPass => ("C", "L"),
        _ => ("L", "C")
    };

    private static void DrawSeries(Graphics g, Pen pen, Font font, string reference, string value, float cx, float y)
    {
        var left = cx - 43;
        var right = cx + 43;
        g.DrawLine(pen, left, y, cx - 24, y);
        switch (value)
        {
            case "R":
                g.DrawLines(pen, new PointF[] { new(cx - 24, y), new(cx - 19, y - 8), new(cx - 10, y + 8), new(cx - 1, y - 8), new(cx + 8, y + 8), new(cx + 17, y - 8), new(cx + 24, y) });
                break;
            case "C":
                g.DrawLine(pen, cx - 24, y, cx - 4, y); g.DrawLine(pen, cx - 4, y - 14, cx - 4, y + 14); g.DrawLine(pen, cx + 4, y - 14, cx + 4, y + 14); g.DrawLine(pen, cx + 4, y, cx + 24, y);
                break;
            default:
                for (var index = 0; index < 3; index++) g.DrawArc(pen, cx - 24 + index * 16, y - 7, 16, 14, 180, 180);
                break;
        }
        g.DrawLine(pen, cx + 24, y, right, y);
        DrawLabel(g, font, reference, value, cx, y - 30);
    }

    private static void DrawShunt(Graphics g, Pen pen, Pen thin, Font font, string reference, string value, float x, float nodeY, float groundY)
    {
        var centerY = (nodeY + groundY) / 2;
        g.DrawLine(pen, x, nodeY, x, centerY - 24);
        switch (value)
        {
            case "R":
                g.DrawLines(pen, new PointF[] { new(x, centerY - 24), new(x - 8, centerY - 19), new(x + 8, centerY - 10), new(x - 8, centerY - 1), new(x + 8, centerY + 8), new(x - 8, centerY + 17), new(x, centerY + 24) });
                break;
            case "C":
                g.DrawLine(pen, x, centerY - 24, x, centerY - 4); g.DrawLine(pen, x - 14, centerY - 4, x + 14, centerY - 4); g.DrawLine(pen, x - 14, centerY + 4, x + 14, centerY + 4); g.DrawLine(pen, x, centerY + 4, x, centerY + 24);
                break;
            default:
                for (var index = 0; index < 3; index++) g.DrawArc(pen, x - 7, centerY - 24 + index * 16, 14, 16, 0, 180);
                break;
        }
        g.DrawLine(pen, x, centerY + 24, x, groundY - 8);
        DrawGround(g, thin, x, groundY - 8);
        DrawLabel(g, font, reference, value, x + 28, centerY - 12);
    }

    private static void DrawLabel(Graphics g, Font font, string reference, string value, float x, float y)
    {
        var text = reference;
        var size = g.MeasureString(text, font);
        using var back = new SolidBrush(Color.FromArgb(238, 255, 255, 255));
        g.FillRectangle(back, x - size.Width / 2 - 3, y - 1, size.Width + 6, size.Height + 2);
        g.DrawString(text, font, new SolidBrush(ChartTheme.Ink), x - size.Width / 2, y);
    }

    private static void DrawNode(Graphics g, float x, float y)
    {
        using var brush = new SolidBrush(ChartTheme.Node);
        g.FillEllipse(brush, x - 2.4f, y - 2.4f, 4.8f, 4.8f);
    }

    private static void DrawGround(Graphics g, Pen pen, float x, float y)
    {
        g.DrawLine(pen, x, y, x, y + 7);
        g.DrawLine(pen, x - 11, y + 7, x + 11, y + 7);
        g.DrawLine(pen, x - 7, y + 11, x + 7, y + 11);
        g.DrawLine(pen, x - 3, y + 15, x + 3, y + 15);
    }

    private static void DrawPort(Graphics g, float x, float y, string text, bool left, Font font)
    {
        using var pen = new Pen(ChartTheme.Muted, 1.1f);
        var size = g.MeasureString(text, font);
        if (left)
        {
            g.DrawLine(pen, x, y, x + 14, y);
            g.DrawString(text, font, new SolidBrush(ChartTheme.Muted), x, y - 18);
        }
        else
        {
            g.DrawLine(pen, x - 14, y, x, y);
            g.DrawString(text, font, new SolidBrush(ChartTheme.Muted), x - size.Width, y - 18);
        }
    }
}
