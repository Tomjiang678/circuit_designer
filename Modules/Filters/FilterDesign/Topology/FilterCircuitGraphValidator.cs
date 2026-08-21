using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

public static class FilterCircuitGraphValidator
{
    public static void EnsureValid(CircuitGraph graph, FilterMode mode)
    {
        CircuitGraphValidator.EnsureValid(graph);
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
    }
}
