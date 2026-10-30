using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Schematic;

/// <summary>
/// A compact cascade layout: series elements on the signal axis, shunt elements
/// on a lower ground rail. The layout is generated from the final topology spec,
/// not from a decorative picture.
/// </summary>
public sealed class AttenuationMatchingLayoutService
{
    public SchematicDocument Layout(TopologyCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var graph = candidate.Graph;
        var spec = candidate.TopologySpec;
        var document = new SchematicDocument(graph);
        if (graph.AreConnected(NetworkTopologyBuilder.InputNet, NetworkTopologyBuilder.OutputNet))
        {
            document.AddWire(new SchematicWire(NetworkTopologyBuilder.InputNet,
                [new SchematicPoint(82, 122), new SchematicPoint(768, 122)]));
            document.AddNetLabel(new SchematicNetLabel(NetworkTopologyBuilder.InputNet, "SOURCE / VIN", new SchematicPoint(82, 84)));
            document.AddNetLabel(new SchematicNetLabel(NetworkTopologyBuilder.OutputNet, "LOAD / VOUT", new SchematicPoint(684, 84)));
            return document;
        }
        if (spec is null) return document;

        const double signalY = 122;
        const double groundY = 228;
        const double sourceX = 82;
        const double firstElementX = 226;
        var elements = spec.Elements;
        var contentWidth = Math.Max(548, elements.Count * 108);
        var lastElementX = firstElementX + contentWidth;
        var outputX = lastElementX + 88;
        var seriesCount = Math.Max(1, elements.Count(element => element.Placement == ElementPlacement.Series));
        var seriesIndex = 0;
        var currentNet = NetworkTopologyBuilder.InputNet;
        var currentPoint = new SchematicPoint(sourceX, signalY);
        var groundPoints = new List<SchematicPoint>();
        var spacing = elements.Count == 0 ? 0 : (lastElementX - firstElementX) / elements.Count;

        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var component = graph.GetComponent(element.Reference);
            var x = firstElementX + spacing * (index + .5);
            if (element.Placement == ElementPlacement.Series)
            {
                document.AddWire(new SchematicWire(currentNet, [currentPoint, new SchematicPoint(x - 43, signalY)]));
                var placed = new PlacedComponent(component, new SchematicPoint(x, signalY), ComponentOrientation.Right);
                document.AddPlacedComponent(placed);
                seriesIndex++;
                var nextNet = seriesIndex == seriesCount ? NetworkTopologyBuilder.OutputNet : $"N{seriesIndex}";
                currentPoint = new SchematicPoint(x + 43, signalY);
                currentNet = nextNet;
            }
            else
            {
                document.AddWire(new SchematicWire(currentNet, [currentPoint, new SchematicPoint(x, signalY)]));
                document.AddJunction(new SchematicJunction(currentNet, new SchematicPoint(x, signalY)));
                var placed = new PlacedComponent(component, new SchematicPoint(x, signalY + 58), ComponentOrientation.Down);
                document.AddPlacedComponent(placed);
                document.AddWire(new SchematicWire(currentNet, [new SchematicPoint(x, signalY), new SchematicPoint(x, signalY + 24)]));
                document.AddWire(new SchematicWire(NetworkTopologyBuilder.GroundNet, [new SchematicPoint(x, signalY + 98), new SchematicPoint(x, groundY)]));
                groundPoints.Add(new SchematicPoint(x, groundY));
                currentPoint = new SchematicPoint(x, signalY);
            }
        }

        document.AddWire(new SchematicWire(currentNet, [currentPoint, new SchematicPoint(outputX, signalY)]));
        document.AddNetLabel(new SchematicNetLabel(NetworkTopologyBuilder.InputNet, "SOURCE / VIN", new SchematicPoint(sourceX, 84)));
        document.AddNetLabel(new SchematicNetLabel(NetworkTopologyBuilder.OutputNet, "LOAD / VOUT", new SchematicPoint(outputX - 84, 84)));

        if (groundPoints.Count > 0)
        {
            var railStart = new SchematicPoint(Math.Min(groundPoints.Min(point => point.X) - 42, 170), groundY);
            var railEnd = new SchematicPoint(Math.Max(groundPoints.Max(point => point.X) + 42, outputX - 52), groundY);
            document.AddWire(new SchematicWire(NetworkTopologyBuilder.GroundNet, [railStart, railEnd]));
            foreach (var point in groundPoints)
                document.AddJunction(new SchematicJunction(NetworkTopologyBuilder.GroundNet, point));
            var groundLabelX = (railStart.X + railEnd.X) / 2;
            document.AddNetLabel(new SchematicNetLabel(NetworkTopologyBuilder.GroundNet, "GND", new SchematicPoint(groundLabelX, groundY - 8)));
        }

        return document;
    }
    public static IReadOnlyList<string> ValidateLayout(SchematicDocument document)
    {
        var errors = new List<string>();
        foreach (var placed in document.PlacedComponents)
        {
            if (!document.Circuit.Components.Any(component => ReferenceEquals(component, placed.Component)))
                errors.Add($"Placed component {placed.Component.Reference} is not in the circuit graph.");
        }
        foreach (var wire in document.Wires)
        {
            if (wire.Points.Count < 2) errors.Add($"Wire {wire.NetName} has fewer than two points.");
            if (!document.Circuit.Nets.Any(net => net.Name.Equals(wire.NetName, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Wire net {wire.NetName} is not in the circuit graph.");
        }
        return errors;
    }
}
