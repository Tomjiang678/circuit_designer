using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic.Placement;

public sealed class PassiveLcPlacementTemplate : IFilterPlacementTemplate
{
    public bool TryPlace(double x, double y, FilterPlacementHint hint, out FilterPlacement placement)
    {
        placement = hint.SemanticRole switch
        {
            "SeriesInductor" => new(x + 75 + hint.Column * 145, y, ComponentOrientation.Right),
            "ShuntCapacitor" => new(x + 75 + hint.Column * 145, y + 105, ComponentOrientation.Down),
            "SeriesElement" => new(x + 75 + hint.Column * 135, y, ComponentOrientation.Right),
            "ParallelSeriesTop" => new(x + 95 + hint.Column * 135, y - 55, ComponentOrientation.Right),
            "ParallelSeriesBottom" => new(x + 95 + hint.Column * 135, y + 55, ComponentOrientation.Right),
            "ParallelShuntLeft" => new(x + 70 + hint.Column * 135, y + 110, ComponentOrientation.Down),
            "ParallelShuntRight" => new(x + 125 + hint.Column * 135, y + 110, ComponentOrientation.Down),
            "ShuntSeriesUpper" => new(x + 95 + hint.Column * 135, y + 75, ComponentOrientation.Down),
            "ShuntSeriesLower" => new(x + 95 + hint.Column * 135, y + 180, ComponentOrientation.Down),
            _ => default
        };
        return placement != default;
    }
}
