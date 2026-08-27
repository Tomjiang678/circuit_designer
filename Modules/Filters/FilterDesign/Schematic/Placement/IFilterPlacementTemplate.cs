using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic.Placement;

public readonly record struct FilterPlacement(double X, double Y, ComponentOrientation Orientation);

public interface IFilterPlacementTemplate
{
    bool TryPlace(double originX, double baselineY, FilterPlacementHint hint, out FilterPlacement placement);
}
