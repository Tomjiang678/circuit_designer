using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic;

public sealed class FilterSchematicLayoutService
{
    private const double SectionWidth = 1380;
    private const double BaselineY = 360;
    private readonly SchematicOrthogonalRouter router = new();

    public SchematicDocument Layout(FilterCircuitBuildResult buildResult)
    {
        ArgumentNullException.ThrowIfNull(buildResult);
        var document = new SchematicDocument(buildResult.CircuitGraph);

        foreach (var group in buildResult.PlacementHints.GroupBy(item => item.SectionIndex).OrderBy(item => item.Key))
        {
            var originX = 145 + (group.Key - 1) * SectionWidth;
            foreach (var hint in group)
            {
                var component = buildResult.CircuitGraph.GetComponent(hint.ComponentReference);
                var (position, orientation) = PlaceHint(originX, hint);
                document.AddPlacedComponent(new PlacedComponent(component, position, orientation));
            }
            if (group.Any(hint => hint.SemanticRole.StartsWith("Bs", StringComparison.Ordinal)))
            {
                document.AddTextAnnotation(new SchematicTextAnnotation("LOW-PASS BRANCH", new SchematicPoint(originX - 100, BaselineY - 240)));
                document.AddTextAnnotation(new SchematicTextAnnotation("HIGH-PASS BRANCH", new SchematicPoint(originX - 100, BaselineY + 230)));
                document.AddTextAnnotation(new SchematicTextAnnotation("WEIGHTED SUM", new SchematicPoint(originX + 760, BaselineY - 230)));
            }
        }

        router.Route(document);
        return document;
    }

    private static (SchematicPoint Position, ComponentOrientation Orientation) PlaceHint(double originX, FilterPlacementHint hint) => hint.SemanticRole switch
    {
        "Series" => (new SchematicPoint(originX + 90, BaselineY), ComponentOrientation.Right),
        "Series1" => (new SchematicPoint(originX + 70, BaselineY), ComponentOrientation.Right),
        "Series2" => (new SchematicPoint(originX + 225, BaselineY), ComponentOrientation.Right),
        "Shunt" => (new SchematicPoint(originX + 330, BaselineY + 105), ComponentOrientation.Down),
        "Feedback" => (new SchematicPoint(originX + 245, BaselineY - 115), ComponentOrientation.Right),
        "Buffer" => (new SchematicPoint(originX + 445, BaselineY + 16), ComponentOrientation.Right),
        "OpAmp" => (new SchematicPoint(originX + 465, BaselineY + 16), ComponentOrientation.Right),
        "SectionGainFeedback" => (new SchematicPoint(originX + 420, BaselineY - 115), ComponentOrientation.Right),
        "SectionGainShunt" => (new SchematicPoint(originX + 390, BaselineY + 125), ComponentOrientation.Down),
        "MfbInput" => (new SchematicPoint(originX + 55, BaselineY), ComponentOrientation.Right),
        "MfbToInput" => (new SchematicPoint(originX + 210, BaselineY), ComponentOrientation.Right),
        "MfbShunt" => (new SchematicPoint(originX + 175, BaselineY + 115), ComponentOrientation.Down),
        "MfbFeedbackTop" => (new SchematicPoint(originX + 275, BaselineY - 120), ComponentOrientation.Right),
        "MfbFeedbackBottom" => (new SchematicPoint(originX + 350, BaselineY + 120), ComponentOrientation.Right),
        "MfbOpAmp" => (new SchematicPoint(originX + 470, BaselineY + 16), ComponentOrientation.Right),
        "SkBpInput" => (new SchematicPoint(originX + 35, BaselineY), ComponentOrientation.Right),
        "SkBpSeries" => (new SchematicPoint(originX + 155, BaselineY), ComponentOrientation.Right),
        "SkBpFeedback" => (new SchematicPoint(originX + 230, BaselineY - 120), ComponentOrientation.Right),
        "SkBpShuntLeft" => (new SchematicPoint(originX + 245, BaselineY + 105), ComponentOrientation.Down),
        "SkBpShuntRight" => (new SchematicPoint(originX + 300, BaselineY + 105), ComponentOrientation.Down),
        "SkBpGainTop" => (new SchematicPoint(originX + 365, BaselineY + 115), ComponentOrientation.Down),
        "SkBpGainBottom" => (new SchematicPoint(originX + 365, BaselineY + 255), ComponentOrientation.Down),
        "SkBpOpAmp" => (new SchematicPoint(originX + 460, BaselineY + 16), ComponentOrientation.Right),
        "SkBpAttenuator" => (new SchematicPoint(originX + 640, BaselineY), ComponentOrientation.Right),
        "SkBpAttenuatorShunt" => (new SchematicPoint(originX + 680, BaselineY + 120), ComponentOrientation.Down),
        "SkBpBuffer" => (new SchematicPoint(originX + 820, BaselineY + 16), ComponentOrientation.Right),
        "BsLpSeries1" => (new SchematicPoint(originX + 45, BaselineY - 170), ComponentOrientation.Right),
        "BsLpSeries2" => (new SchematicPoint(originX + 205, BaselineY - 170), ComponentOrientation.Right),
        "BsLpShunt" => (new SchematicPoint(originX + 330, BaselineY - 60), ComponentOrientation.Down),
        "BsLpFeedback" => (new SchematicPoint(originX + 275, BaselineY - 280), ComponentOrientation.Right),
        "BsLpFeedbackBottom" => (new SchematicPoint(originX + 430, BaselineY - 280), ComponentOrientation.Right),
        "BsLpOpAmp" => (new SchematicPoint(originX + 485, BaselineY - 154), ComponentOrientation.Right),
        "BsHpSeries1" => (new SchematicPoint(originX + 45, BaselineY + 170), ComponentOrientation.Right),
        "BsHpSeries2" => (new SchematicPoint(originX + 205, BaselineY + 170), ComponentOrientation.Right),
        "BsHpShunt" => (new SchematicPoint(originX + 330, BaselineY + 280), ComponentOrientation.Down),
        "BsHpFeedback" => (new SchematicPoint(originX + 275, BaselineY + 60), ComponentOrientation.Right),
        "BsHpFeedbackBottom" => (new SchematicPoint(originX + 430, BaselineY + 280), ComponentOrientation.Right),
        "BsHpOpAmp" => (new SchematicPoint(originX + 485, BaselineY + 186), ComponentOrientation.Right),
        "BsLpSummerInput" => (new SchematicPoint(originX + 700, BaselineY - 170), ComponentOrientation.Right),
        "BsHpSummerInput" => (new SchematicPoint(originX + 700, BaselineY + 170), ComponentOrientation.Right),
        "BsSummerFeedback" => (new SchematicPoint(originX + 895, BaselineY - 5), ComponentOrientation.Right),
        "BsSummerOpAmp" => (new SchematicPoint(originX + 1040, BaselineY + 26), ComponentOrientation.Right),
        "BainterR1" => (new SchematicPoint(originX + 45, BaselineY), ComponentOrientation.Right),
        "BainterR2" => (new SchematicPoint(originX + 210, BaselineY + 125), ComponentOrientation.Right),
        "BainterU1" => (new SchematicPoint(originX + 310, BaselineY + 20), ComponentOrientation.Right),
        "BainterR3" => (new SchematicPoint(originX + 430, BaselineY), ComponentOrientation.Right),
        "BainterC1" => (new SchematicPoint(originX + 590, BaselineY - 120), ComponentOrientation.Right),
        "BainterU2" => (new SchematicPoint(originX + 650, BaselineY + 20), ComponentOrientation.Right),
        "BainterR4" => (new SchematicPoint(originX + 730, BaselineY - 215), ComponentOrientation.Right),
        "BainterR5" => (new SchematicPoint(originX + 800, BaselineY), ComponentOrientation.Right),
        "BainterC2" => (new SchematicPoint(originX + 520, BaselineY + 235), ComponentOrientation.Right),
        "BainterR6" => (new SchematicPoint(originX + 900, BaselineY + 50), ComponentOrientation.Down),
        "BainterU3" => (new SchematicPoint(originX + 1035, BaselineY + 20), ComponentOrientation.Right),
        "BainterGainFeedback" => (new SchematicPoint(originX + 1015, BaselineY - 115), ComponentOrientation.Right),
        "BainterGainShunt" => (new SchematicPoint(originX + 980, BaselineY + 140), ComponentOrientation.Down),
        "PrototypeNormalizationSeries" => (new SchematicPoint(originX + 1190, BaselineY), ComponentOrientation.Right),
        "PrototypeNormalizationShunt" => (new SchematicPoint(originX + 1240, BaselineY + 125), ComponentOrientation.Down),
        "SeriesInductor" => (new SchematicPoint(originX + 75 + hint.Column * 145, BaselineY), ComponentOrientation.Right),
        "ShuntCapacitor" => (new SchematicPoint(originX + 75 + hint.Column * 145, BaselineY + 105), ComponentOrientation.Down),
        "SeriesElement" => (new SchematicPoint(originX + 75 + hint.Column * 135, BaselineY), ComponentOrientation.Right),
        "ParallelSeriesTop" => (new SchematicPoint(originX + 95 + hint.Column * 135, BaselineY - 55), ComponentOrientation.Right),
        "ParallelSeriesBottom" => (new SchematicPoint(originX + 95 + hint.Column * 135, BaselineY + 55), ComponentOrientation.Right),
        "ParallelShuntLeft" => (new SchematicPoint(originX + 70 + hint.Column * 135, BaselineY + 110), ComponentOrientation.Down),
        "ParallelShuntRight" => (new SchematicPoint(originX + 125 + hint.Column * 135, BaselineY + 110), ComponentOrientation.Down),
        "ShuntSeriesUpper" => (new SchematicPoint(originX + 95 + hint.Column * 135, BaselineY + 75), ComponentOrientation.Down),
        "ShuntSeriesLower" => (new SchematicPoint(originX + 95 + hint.Column * 135, BaselineY + 180), ComponentOrientation.Down),
        _ => (new SchematicPoint(originX + 75 + hint.Column * 140, BaselineY + hint.Row * 105), ComponentOrientation.Right)
    };
}
