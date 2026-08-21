using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls.SpecificationPreview;

public sealed class FilterSpecPlotModel
{
    public FilterResponseType ResponseType { get; init; }
    public FilterApproximation Approximation { get; init; }
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
    public double MinimumFrequencyHz { get; init; }
    public double MaximumFrequencyHz { get; init; }
    public double MinimumDb { get; init; } = -60;
    public double MaximumDb { get; init; } = 10;
    public IReadOnlyList<PlotPoint> Response { get; init; } = Array.Empty<PlotPoint>();
    public IReadOnlyList<PlotMarker> Markers { get; init; } = Array.Empty<PlotMarker>();
    public IReadOnlyList<PlotBand> Bands { get; init; } = Array.Empty<PlotBand>();
}

public readonly record struct PlotPoint(double FrequencyHz, double Db);
public sealed record PlotMarker(double FrequencyHz, double Db, string Label, bool Dashed = true);
public sealed record PlotBand(double StartHz, double EndHz, string Label, bool Dashed = true);
