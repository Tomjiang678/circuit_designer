using System.Drawing.Drawing2D;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls.SpecificationPreview;

internal static class PlotDrawingHelpers
{
    public static void DrawDashedLine(Graphics g, Pen pen, PointF a, PointF b) { using var dash = new Pen(pen.Color, pen.Width) { DashStyle = DashStyle.Dash }; g.DrawLine(dash, a, b); }
    public static void DrawArrow(Graphics g, Pen pen, PointF a, PointF b)
    {
        g.DrawLine(pen, a, b); var angle = Math.Atan2(b.Y - a.Y, b.X - a.X); const double size = 6;
        var p1 = new PointF((float)(b.X - size * Math.Cos(angle - Math.PI / 6)), (float)(b.Y - size * Math.Sin(angle - Math.PI / 6)));
        var p2 = new PointF((float)(b.X - size * Math.Cos(angle + Math.PI / 6)), (float)(b.Y - size * Math.Sin(angle + Math.PI / 6)));
        g.DrawLine(pen, b, p1); g.DrawLine(pen, b, p2);
    }
}
