using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

public sealed class ReferenceDesignatorAllocator
{
    private readonly Dictionary<ComponentType, int> counters = [];

    public string Next(ComponentType type)
    {
        counters.TryGetValue(type, out var current);
        counters[type] = ++current;
        var prefix = type switch
        {
            ComponentType.Resistor => "R",
            ComponentType.Capacitor => "C",
            ComponentType.Inductor => "L",
            ComponentType.OpAmp => "U",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        return prefix + current;
    }
}
