using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls.SpecificationPreview;

public sealed class FilterSpecPlotControl : UserControl
{
    private FilterSpecPlotModel model = new()
    {
        IsValid = false,
        Message = "Enter valid filter specifications to preview."
    };

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FilterSpecPlotModel Model
    {
        get => model;
        set
        {
            model = value ?? throw new ArgumentNullException(nameof(value));
            Invalidate();
        }
    }

    public FilterSpecPlotControl()
    {
        DoubleBuffered = true;
        BackColor = SystemColors.Window;
        MinimumSize = new Size(240, 180);
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(SystemColors.Window);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var scale = DeviceDpi / 96f;
        var leftMargin = 66f * scale;
        var rightMargin = 18f * scale;
        var topMargin = 28f * scale;
        var bottomMargin = 62f * scale;
        if (ClientSize.Width < leftMargin + rightMargin + 140f * scale ||
            ClientSize.Height < topMargin + bottomMargin + 90f * scale)
        {
            DrawCenteredMessage(e.Graphics, "Increase the preview area to display the specification plot.");
            return;
        }

        var plot = RectangleF.FromLTRB(
            leftMargin,
            topMargin,
            ClientSize.Width - rightMargin,
            ClientSize.Height - bottomMargin);

        if (!model.IsValid)
        {
            DrawCenteredMessage(e.Graphics, model.Message);
            return;
        }

        var mapper = new PlotCoordinateMapper(
            plot,
            model.MinimumFrequencyHz,
            model.MaximumFrequencyHz,
            model.MinimumDb,
            model.MaximumDb);

        using var gridPen = new Pen(Color.FromArgb(224, 224, 224));
        using var axisPen = new Pen(Color.FromArgb(45, 45, 45), Math.Max(1f, scale));
        using var responsePen = new Pen(Color.FromArgb(0, 70, 150), Math.Max(2f, 2f * scale));
        using var specificationPen = new Pen(Color.Firebrick, Math.Max(1f, scale)) { DashStyle = DashStyle.Dash };
        using var specificationBrush = new SolidBrush(Color.Firebrick);
        using var tickBrush = new SolidBrush(Color.FromArgb(80, 80, 80));
        using var labelBackground = new SolidBrush(Color.FromArgb(238, 255, 255, 255));

        DrawGridAndAxes(e.Graphics, plot, gridPen, axisPen, tickBrush, scale);

        var responsePoints = model.Response
            .Select(point => mapper.Map(point.FrequencyHz, point.Db))
            .Where(point => float.IsFinite(point.X) && float.IsFinite(point.Y))
            .ToArray();
        if (responsePoints.Length > 1)
            e.Graphics.DrawLines(responsePen, responsePoints);

        var labelBounds = RectangleF.Inflate(plot, -3f * scale, -3f * scale);
        var labelLayout = new PlotLabelLayoutEngine(labelBounds, 3f * scale);

        foreach (var marker in model.Markers)
        {
            var anchor = mapper.Map(marker.FrequencyHz, marker.Db);
            e.Graphics.DrawLine(specificationPen, anchor.X, plot.Top, anchor.X, plot.Bottom);
            e.Graphics.DrawLine(specificationPen, plot.Left, anchor.Y, anchor.X, anchor.Y);
            DrawCollisionSafeLabel(
                e.Graphics,
                marker.Label,
                anchor,
                labelLayout,
                specificationBrush,
                labelBackground,
                scale);
        }

        foreach (var band in model.Bands)
        {
            var start = mapper.Map(band.StartHz, model.MinimumDb + 5);
            var end = mapper.Map(band.EndHz, model.MinimumDb + 5);
            PlotDrawingHelpers.DrawArrow(e.Graphics, specificationPen, start, end);
            PlotDrawingHelpers.DrawArrow(e.Graphics, specificationPen, end, start);
            DrawCollisionSafeLabel(
                e.Graphics,
                band.Label,
                new PointF((start.X + end.X) / 2, start.Y),
                labelLayout,
                specificationBrush,
                labelBackground,
                scale);
        }

        e.Graphics.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        e.Graphics.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        e.Graphics.DrawLine(axisPen, plot.Left, plot.Top, plot.Right, plot.Top);
        e.Graphics.DrawLine(axisPen, plot.Right, plot.Top, plot.Right, plot.Bottom);
        DrawFrequencyTicks(e.Graphics, plot, axisPen, tickBrush, scale);
        var axisTitle = "Frequency (log scale)";
        var axisTitleSize = e.Graphics.MeasureString(axisTitle, Font);
        e.Graphics.DrawString(
            axisTitle,
            Font,
            Brushes.Black,
            plot.Left + (plot.Width - axisTitleSize.Width) / 2,
            Math.Min(ClientSize.Height - axisTitleSize.Height - 2f * scale, plot.Bottom + 30f * scale));
        e.Graphics.DrawString("Magnitude (dB)", Font, Brushes.Black, plot.Left, 4f * scale);
    }

    private void DrawGridAndAxes(
        Graphics graphics,
        RectangleF plot,
        Pen gridPen,
        Pen axisPen,
        Brush tickBrush,
        float scale)
    {
        const int horizontalDivisions = 6;
        const int verticalDivisions = 6;
        for (var index = 0; index <= horizontalDivisions; index++)
        {
            var y = plot.Top + plot.Height * index / horizontalDivisions;
            graphics.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var db = model.MaximumDb - (model.MaximumDb - model.MinimumDb) * index / horizontalDivisions;
            var label = $"{db:0} dB";
            var size = graphics.MeasureString(label, Font);
            graphics.DrawString(label, Font, tickBrush, plot.Left - size.Width - 7f * scale, y - size.Height / 2);
        }
        for (var index = 0; index <= verticalDivisions; index++)
        {
            var x = plot.Left + plot.Width * index / verticalDivisions;
            graphics.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
        }

        graphics.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        graphics.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        graphics.DrawLine(axisPen, plot.Left, plot.Top, plot.Right, plot.Top);
        graphics.DrawLine(axisPen, plot.Right, plot.Top, plot.Right, plot.Bottom);
    }

    private void DrawFrequencyTicks(Graphics graphics, RectangleF plot, Pen axisPen, Brush tickBrush, float scale)
    {
        const int tickCount = 5;
        for (var index = 0; index < tickCount; index++)
        {
            var ratio = index / (tickCount - 1.0);
            var x = plot.Left + plot.Width * (float)ratio;
            graphics.DrawLine(axisPen, x, plot.Bottom, x, plot.Bottom + 5f * scale);
            var frequency = model.MinimumFrequencyHz * Math.Pow(model.MaximumFrequencyHz / model.MinimumFrequencyHz, ratio);
            var label = FormatFrequency(frequency);
            var size = graphics.MeasureString(label, Font);
            var labelX = Math.Clamp(x - size.Width / 2, 1f, ClientSize.Width - size.Width - 1f);
            graphics.DrawString(label, Font, tickBrush, labelX, plot.Bottom + 7f * scale);
        }
    }

    private void DrawCollisionSafeLabel(
        Graphics graphics,
        string text,
        PointF anchor,
        PlotLabelLayoutEngine layout,
        Brush textBrush,
        Brush backgroundBrush,
        float scale)
    {
        var measured = graphics.MeasureString(text, Font);
        var size = new SizeF(measured.Width + 4f * scale, measured.Height + 2f * scale);
        var rectangle = layout.Place(size, anchor);
        graphics.FillRectangle(backgroundBrush, rectangle);
        graphics.DrawString(text, Font, textBrush, rectangle.Left + 2f * scale, rectangle.Top + scale);
    }

    private void DrawCenteredMessage(Graphics graphics, string message)
    {
        var bounds = RectangleF.Inflate(ClientRectangle, -12, -12);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(message, Font, Brushes.DimGray, bounds, format);
    }

    private static string FormatFrequency(double frequency) => frequency switch
    {
        >= 1e6 => $"{frequency / 1e6:0.##} MHz",
        >= 1e3 => $"{frequency / 1e3:0.##} kHz",
        _ => $"{frequency:0.##} Hz"
    };
}
