using System.Globalization;
using System.Text;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Schematic;

public sealed class AttenuationMatchingSchematicSvgBuilder
{
    public string Build(TopologyCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var document = new AttenuationMatchingLayoutService().Layout(candidate);
        var points = document.PlacedComponents.Select(component => component.Position).ToArray();
        var wirePoints = document.Wires.SelectMany(wire => wire.Points).ToArray();
        var minX = Math.Min(points.Select(point => point.X).DefaultIfEmpty(0).Min(), wirePoints.Select(point => point.X).DefaultIfEmpty(0).Min()) - 48;
        var maxX = Math.Max(points.Select(point => point.X).DefaultIfEmpty(0).Max(), wirePoints.Select(point => point.X).DefaultIfEmpty(0).Max()) + 62;
        var minY = Math.Min(points.Select(point => point.Y).DefaultIfEmpty(0).Min(), wirePoints.Select(point => point.Y).DefaultIfEmpty(0).Min()) - 52;
        var maxY = Math.Max(points.Select(point => point.Y).DefaultIfEmpty(0).Max(), wirePoints.Select(point => point.Y).DefaultIfEmpty(0).Max()) + 62;
        var width = Math.Max(720, maxX - minX);
        var height = Math.Max(330, maxY - minY);
        var svg = new StringBuilder();
        svg.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"")
            .Append(Number(minX)).Append(' ').Append(Number(minY)).Append(' ').Append(Number(width)).Append(' ').Append(Number(height))
            .Append("\" width=\"100%\" height=\"auto\" role=\"img\" aria-label=\"Passive matching network schematic\">");
        svg.Append("<rect x=\"").Append(Number(minX)).Append("\" y=\"").Append(Number(minY)).Append("\" width=\"").Append(Number(width)).Append("\" height=\"").Append(Number(height)).Append("\" fill=\"#ffffff\"/>");
        svg.Append("<g stroke-linecap=\"round\" stroke-linejoin=\"round\">");
        foreach (var wire in document.Wires)
        {
            svg.Append("<polyline fill=\"none\" stroke=\"#243447\" stroke-width=\"2\" points=\"")
                .Append(string.Join(" ", wire.Points.Select(point => Number(point.X) + "," + Number(point.Y)))).Append("\"/>");
        }
        foreach (var junction in document.Junctions)
            svg.Append("<circle cx=\"").Append(Number(junction.Position.X)).Append("\" cy=\"").Append(Number(junction.Position.Y)).Append("\" r=\"3\" fill=\"#243447\"/>");
        svg.Append("</g>");

        foreach (var component in document.PlacedComponents)
        {
            var rotation = component.Orientation switch { ComponentOrientation.Down => 90, ComponentOrientation.Left => 180, ComponentOrientation.Up => 270, _ => 0 };
            svg.Append("<g transform=\"translate(").Append(Number(component.Position.X)).Append(' ').Append(Number(component.Position.Y)).Append(") rotate(").Append(rotation).Append(")\">");
            switch (component.Component.Type)
            {
                case ComponentType.Resistor:
                    svg.Append("<polyline fill=\"none\" stroke=\"#1f2937\" stroke-width=\"2\" stroke-linejoin=\"round\" points=\"-43,0 -28,0 -24,-10 -16,10 -8,-10 0,10 8,-10 16,10 24,0 43,0\"/>");
                    break;
                case ComponentType.Capacitor:
                    svg.Append("<line x1=\"-43\" y1=\"0\" x2=\"-8\" y2=\"0\" stroke=\"#1f2937\" stroke-width=\"2\"/><line x1=\"-8\" y1=\"-16\" x2=\"-8\" y2=\"16\" stroke=\"#1f2937\" stroke-width=\"3\"/><line x1=\"8\" y1=\"-16\" x2=\"8\" y2=\"16\" stroke=\"#1f2937\" stroke-width=\"3\"/><line x1=\"8\" y1=\"0\" x2=\"43\" y2=\"0\" stroke=\"#1f2937\" stroke-width=\"2\"/>");
                    break;
                case ComponentType.Inductor:
                    svg.Append("<line x1=\"-43\" y1=\"0\" x2=\"-28\" y2=\"0\" stroke=\"#1f2937\" stroke-width=\"2\"/><path d=\"M-28 0 A8 8 0 0 1 -12 0 A8 8 0 0 1 4 0 A8 8 0 0 1 20 0 A8 8 0 0 1 28 0\" fill=\"none\" stroke=\"#1f2937\" stroke-width=\"2\"/><line x1=\"28\" y1=\"0\" x2=\"43\" y2=\"0\" stroke=\"#1f2937\" stroke-width=\"2\"/>");
                    break;
            }
            svg.Append("</g>");
            var vertical = component.Orientation is ComponentOrientation.Up or ComponentOrientation.Down;
            var labelX = component.Position.X + (vertical ? 34 : 0);
            var labelY = component.Position.Y + (vertical ? -5 : -34);
            var anchor = vertical ? "start" : "middle";
            svg.Append("<text x=\"").Append(Number(labelX)).Append("\" y=\"").Append(Number(labelY)).Append("\" text-anchor=\"").Append(anchor)
                .Append("\" font-family=\"Segoe UI,Arial\" font-size=\"12\" font-weight=\"600\" fill=\"#111827\">").Append(Xml(component.Component.Reference)).Append("</text>");
            svg.Append("<text x=\"").Append(Number(labelX)).Append("\" y=\"").Append(Number(labelY + 14)).Append("\" text-anchor=\"").Append(anchor)
                .Append("\" font-family=\"Segoe UI,Arial\" font-size=\"11\" fill=\"#374151\">").Append(Xml(Value(component.Component))).Append("</text>");
        }

        foreach (var label in document.NetLabels)
        {
            if (label.Text.Equals("GND", StringComparison.OrdinalIgnoreCase))
            {
                var x = label.Position.X;
                var y = label.Position.Y + 8;
                svg.Append("<g stroke=\"#243447\" stroke-width=\"1.5\" fill=\"none\"><line x1=\"").Append(Number(x)).Append("\" y1=\"").Append(Number(y)).Append("\" x2=\"").Append(Number(x)).Append("\" y2=\"").Append(Number(y + 8)).Append("\"/>")
                    .Append("<line x1=\"").Append(Number(x - 10)).Append("\" y1=\"").Append(Number(y + 8)).Append("\" x2=\"").Append(Number(x + 10)).Append("\" y2=\"").Append(Number(y + 8)).Append("\"/>")
                    .Append("<line x1=\"").Append(Number(x - 6)).Append("\" y1=\"").Append(Number(y + 12)).Append("\" x2=\"").Append(Number(x + 6)).Append("\" y2=\"").Append(Number(y + 12)).Append("\"/>")
                    .Append("<line x1=\"").Append(Number(x - 2)).Append("\" y1=\"").Append(Number(y + 16)).Append("\" x2=\"").Append(Number(x + 2)).Append("\" y2=\"").Append(Number(y + 16)).Append("\"/></g>");
                svg.Append("<text x=\"").Append(Number(x + 16)).Append("\" y=\"").Append(Number(y + 14)).Append("\" font-family=\"Segoe UI,Arial\" font-size=\"11\" fill=\"#244f78\">GND</text>");
            }
            else
            {
                svg.Append("<text x=\"").Append(Number(label.Position.X)).Append("\" y=\"").Append(Number(label.Position.Y)).Append("\" font-family=\"Segoe UI,Arial\" font-size=\"12\" fill=\"#244f78\">").Append(Xml(label.Text)).Append("</text>");
            }
        }
        foreach (var annotation in document.TextAnnotations)
            svg.Append("<text x=\"").Append(Number(annotation.Position.X)).Append("\" y=\"").Append(Number(annotation.Position.Y)).Append("\" font-family=\"Segoe UI,Arial\" font-size=\"12\" fill=\"#4b5563\">").Append(Xml(annotation.Text)).Append("</text>");
        svg.Append("<text x=\"").Append(Number(minX + width - 16)).Append("\" y=\"").Append(Number(minY + height - 14)).Append("\" text-anchor=\"end\" font-family=\"Segoe UI,Arial\" font-size=\"10\" fill=\"#6b7280\">Generated from final CircuitGraph</text>");
        svg.Append("</svg>");
        return svg.ToString();
    }
    private static string Value(CircuitComponent component) => component.Type switch
    {
        ComponentType.Resistor => Engineering(component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm), "\u03A9"),
        ComponentType.Capacitor => Engineering(component.Parameters.GetRequiredValue(ComponentParameterNames.CapacitanceFarad), "F"),
        ComponentType.Inductor => Engineering(component.Parameters.GetRequiredValue(ComponentParameterNames.InductanceHenry), "H"),
        _ => string.Empty
    };

    private static string Engineering(double value, string unit)
    {
        var prefixes = new[] { (1e6, "M"), (1e3, "k"), (1d, ""), (1e-3, "m"), (1e-6, "µ"), (1e-9, "n"), (1e-12, "p") };
        var prefix = prefixes.First(item => value >= item.Item1 || item.Item1 == 1e-12);
        return $"{value / prefix.Item1:0.###} {prefix.Item2}{unit}";
    }
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
