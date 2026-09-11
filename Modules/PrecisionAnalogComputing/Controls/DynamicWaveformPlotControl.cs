using System.ComponentModel;
using System.Drawing.Drawing2D;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class DynamicWaveformPlotControl : Control
{
    private DynamicTimeDomainAnalysisResult? analysis;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DynamicTimeDomainAnalysisResult? Analysis
    {
        get => analysis;
        set { analysis = value; Invalidate(); }
    }

    public DynamicWaveformPlotControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = SystemColors.Window;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var data = analysis;
        if (data is null || data.Points.Count < 2)
        {
            e.Graphics.DrawString("No time-domain data.", Font, Brushes.DimGray, 20, 20);
            return;
        }

        var left = 72f;
        var width = Math.Max(120, ClientSize.Width - 100);
        var half = Math.Max(100, (ClientSize.Height - 85) / 2f);
        var inputBounds = new RectangleF(left, 25, width, half - 18);
        var outputBounds = new RectangleF(left, half + 45, width, half - 18);
        var inputLimit = Math.Max(1e-9, data.Points.Max(point => Math.Abs(point.InputVoltage)) * 1.15);
        var outputLimit = Math.Max(1e-9, data.Points.Max(point =>
            Math.Max(Math.Abs(point.TargetOutputVoltage), Math.Abs(point.ActualOutputVoltage))) * 1.15);

        Axes(e.Graphics, inputBounds, "Vin (V)", -inputLimit, inputLimit);
        Axes(e.Graphics, outputBounds, "Vout (V)", -outputLimit, outputLimit);
        Curve(e.Graphics, inputBounds, data.Points.Select(point => (point.Periods, point.InputVoltage)),
            -inputLimit, inputLimit, Color.DimGray, DashStyle.Solid);
        Curve(e.Graphics, outputBounds, data.Points.Select(point => (point.Periods, point.TargetOutputVoltage)),
            -outputLimit, outputLimit, Color.DarkOrange, DashStyle.Dash);
        Curve(e.Graphics, outputBounds, data.Points.Select(point => (point.Periods, point.ActualOutputVoltage)),
            -outputLimit, outputLimit, Color.Navy, DashStyle.Solid);
        using var theoryPen = new Pen(Color.DarkOrange, 2) { DashStyle = DashStyle.Dash };
        using var actualPen = new Pen(Color.Navy, 2);
        e.Graphics.DrawLine(theoryPen, left, 12, left + 24, 12);
        e.Graphics.DrawString("Theoretical", Font, Brushes.DarkOrange, left + 30, 5);
        var actualX = left + 44 + e.Graphics.MeasureString("Theoretical", Font).Width;
        e.Graphics.DrawLine(actualPen, actualX, 12, actualX + 24, 12);
        e.Graphics.DrawString("Actual", Font, Brushes.Navy, actualX + 30, 5);
    }

    private void Axes(Graphics graphics, RectangleF bounds, string label, double minimum, double maximum)
    {
        using var grid = new Pen(Color.Gainsboro);
        for (var index = 0; index <= 4; index++)
        {
            var y = bounds.Top + bounds.Height * index / 4;
            graphics.DrawLine(grid, bounds.Left, y, bounds.Right, y);
            graphics.DrawString((maximum - (maximum - minimum) * index / 4).ToString("G4"),
                Font, Brushes.DimGray, 5, y - 7);
        }
        for (var index = 0; index <= 3; index++)
        {
            var x = bounds.Left + bounds.Width * index / 3;
            graphics.DrawLine(grid, x, bounds.Top, x, bounds.Bottom);
            graphics.DrawString(index == 0 ? "0" : index == 1 ? "T" : $"{index}T",
                Font, Brushes.DimGray, x - 6, bounds.Bottom + 3);
        }
        graphics.DrawRectangle(Pens.DimGray, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        graphics.DrawString(label, Font, Brushes.Black, bounds.Left + 5, bounds.Top + 3);
    }

    private static void Curve(Graphics graphics, RectangleF bounds, IEnumerable<(double X, double Y)> values,
        double minimum, double maximum, Color color, DashStyle dash)
    {
        var points = values.Select(value => new PointF(
            bounds.Left + (float)(value.X / 3) * bounds.Width,
            bounds.Bottom - (float)((value.Y - minimum) / (maximum - minimum)) * bounds.Height)).ToArray();
        using var pen = new Pen(color, 2) { DashStyle = dash, LineJoin = LineJoin.Round };
        graphics.DrawLines(pen, points);
    }
}
