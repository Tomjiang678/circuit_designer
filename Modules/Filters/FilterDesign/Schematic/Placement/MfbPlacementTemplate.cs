using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic.Placement;

public sealed class MfbPlacementTemplate : IFilterPlacementTemplate
{
    public bool TryPlace(double x, double y, FilterPlacementHint hint, out FilterPlacement placement)
    {
        placement = hint.SemanticRole switch
        {
            "MfbInput" => new(x + 55, y, ComponentOrientation.Right),
            "MfbToInput" => new(x + 210, y, ComponentOrientation.Right),
            "MfbShunt" => new(x + 175, y + 115, ComponentOrientation.Down),
            "MfbFeedbackTop" => new(x + 275, y - 120, ComponentOrientation.Right),
            "MfbFeedbackBottom" => new(x + 350, y + 120, ComponentOrientation.Right),
            "MfbOpAmp" => new(x + 470, y + 16, ComponentOrientation.Right),
            _ => default
        };
        return placement != default;
    }
}
