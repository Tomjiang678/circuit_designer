using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic.Placement;

public sealed class SallenKeyPlacementTemplate : IFilterPlacementTemplate
{
    public bool TryPlace(double x, double y, FilterPlacementHint hint, out FilterPlacement placement)
    {
        placement = hint.SemanticRole switch
        {
            "Series" => new(x + 90, y, ComponentOrientation.Right),
            "Series1" => new(x + 70, y, ComponentOrientation.Right),
            "Series2" => new(x + 225, y, ComponentOrientation.Right),
            "Shunt" => new(x + 330, y + 105, ComponentOrientation.Down),
            "Feedback" => new(x + 245, y - 115, ComponentOrientation.Right),
            "Buffer" => new(x + 445, y + 16, ComponentOrientation.Right),
            "OpAmp" => new(x + 465, y + 16, ComponentOrientation.Right),
            // Keep the feedback resistor's left pin to the right of the gain-net
            // trunk. Otherwise its route doubles back through the symbol lead and
            // makes the pin look open even though CircuitGraph is connected.
            "SectionGainFeedback" => new(x + 455, y - 115, ComponentOrientation.Right),
            "SectionGainShunt" => new(x + 390, y + 125, ComponentOrientation.Down),
            "InterstageAttenuatorSeries" => new(x + 1190, y, ComponentOrientation.Right),
            "InterstageAttenuatorShunt" => new(x + 1240, y + 125, ComponentOrientation.Down),
            "OutputScaleBuffer" => new(x + 1370, y + 16, ComponentOrientation.Right),
            _ => default
        };
        return placement != default;
    }
}
