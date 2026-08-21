namespace CircuitModuleDesigner.Core.Circuit;

public static class CircuitGraphValidator
{
    public static IReadOnlyList<string> Validate(CircuitGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var errors = new List<string>();
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownPins = new Dictionary<string, ComponentPin>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in graph.Components)
        {
            if (!references.Add(component.Reference))
                errors.Add($"Duplicate component reference '{component.Reference}'.");
            foreach (var pin in component.Pins)
            {
                if (!knownPins.TryAdd(pin.Id, pin))
                    errors.Add($"Duplicate pin ID '{pin.Id}'.");
            }
        }

        var netNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assignedPins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var net in graph.Nets)
        {
            if (!netNames.Add(net.Name))
                errors.Add($"Duplicate net name '{net.Name}'.");
            foreach (var pin in net.Pins)
            {
                if (!knownPins.ContainsKey(pin.Id))
                    errors.Add($"Net '{net.Name}' references unknown pin '{pin.Id}'.");
                if (assignedPins.TryGetValue(pin.Id, out var otherNet) && !string.Equals(otherNet, net.Name, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Pin '{pin.Id}' belongs to both '{otherNet}' and '{net.Name}'.");
                else
                    assignedPins[pin.Id] = net.Name;
            }
        }
        return errors;
    }

    public static void EnsureValid(CircuitGraph graph)
    {
        var errors = Validate(graph);
        if (errors.Count > 0)
            throw new InvalidOperationException("Circuit graph validation failed: " + string.Join(" ", errors));
    }
}
