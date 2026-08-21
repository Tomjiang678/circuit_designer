namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls.SpecificationPreview;

internal sealed class PlotCoordinateMapper
{
    private readonly RectangleF plot; private readonly double minF, maxF, minDb, maxDb;
    public PlotCoordinateMapper(RectangleF plot, double minF, double maxF, double minDb, double maxDb) { this.plot = plot; this.minF = minF; this.maxF = maxF; this.minDb = minDb; this.maxDb = maxDb; }
    public PointF Map(double f, double db) => new((float)(plot.Left + (Math.Log10(Math.Max(f, minF)) - Math.Log10(minF)) / (Math.Log10(maxF) - Math.Log10(minF)) * plot.Width), (float)(plot.Bottom - (db - minDb) / (maxDb - minDb) * plot.Height));
}
