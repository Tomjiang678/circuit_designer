using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

public static class FilterCircuitGraphValidator
{
    public static void EnsureFilterRequirements(CircuitGraph graph, FilterMode mode)
    {
        foreach (var requiredNet in new[] { "VIN", "VOUT", "GND" })
            _ = graph.GetNet(requiredNet);
        if (mode == FilterMode.Active)
        {
            _ = graph.GetNet("VCC");
            _ = graph.GetNet("VEE");
            foreach (var opAmp in graph.Components.Where(component => component.Type == ComponentType.OpAmp))
            {
                var nonInvertingNet = graph.GetNetForPin(opAmp.GetPin("IN+"));
                var invertingNet = graph.GetNetForPin(opAmp.GetPin("IN-"));
                if (nonInvertingNet == null || invertingNet == null)
                    throw new InvalidOperationException($"Op-amp '{opAmp.Reference}' has an unconnected signal input.");
                if (ReferenceEquals(nonInvertingNet, invertingNet))
                    throw new InvalidOperationException($"Op-amp '{opAmp.Reference}' has IN+ and IN- shorted on net '{nonInvertingNet.Name}'.");
            }
        }
        foreach (var component in graph.Components)
        foreach (var pin in component.Pins)
        {
            if (graph.GetNetForPin(pin) == null)
                throw new InvalidOperationException($"Required pin '{pin.Id}' is left unconnected.");
        }
        EnsureSignalPath(graph);
    }

    private static void EnsureSignalPath(CircuitGraph graph)
    {
        var excluded = new HashSet<string>(["GND", "VCC", "VEE"], StringComparer.OrdinalIgnoreCase);
        var adjacency = graph.Nets.Where(net => !excluded.Contains(net.Name))
            .ToDictionary(net => net.Name, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var component in graph.Components)
        {
            var signalNets = component.Pins
                .Where(pin => pin.Name is not ("V+" or "V-"))
                .Select(graph.GetNetForPin).Where(net => net != null && !excluded.Contains(net.Name))
                .Select(net => net!.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var first in signalNets)
            foreach (var second in signalNets)
                if (!first.Equals(second, StringComparison.OrdinalIgnoreCase)) adjacency[first].Add(second);
        }
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "VIN" };
        var pending = new Queue<string>(); pending.Enqueue("VIN");
        while (pending.Count > 0)
            foreach (var next in adjacency[pending.Dequeue()])
                if (visited.Add(next)) pending.Enqueue(next);
        if (!visited.Contains("VOUT"))
            throw new InvalidOperationException("No component-level signal path connects VIN to VOUT.");
    }
}
