namespace CircuitModuleDesigner.Core.Circuit;

public sealed class CircuitBuilder
{
    private readonly Dictionary<string, CircuitComponent> components = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ComponentPin>> nets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> netNameByPinId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> netAliases = new(StringComparer.OrdinalIgnoreCase);

    public CircuitComponent Add(CircuitComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!components.TryAdd(component.Reference, component))
            throw new InvalidOperationException($"A component with reference '{component.Reference}' already exists.");
        return component;
    }

    public CircuitNetHandle CreateNet(string netName)
    {
        ValidateNetName(netName);
        if (!nets.TryAdd(netName, []))
            throw new InvalidOperationException($"A net named '{netName}' already exists.");
        return new CircuitNetHandle(this, netName);
    }

    public CircuitNetHandle Connect(string netName, params ComponentPin[] pins)
    {
        ValidateNetName(netName);
        ArgumentNullException.ThrowIfNull(pins);
        if (!nets.TryGetValue(netName, out var connectedPins))
        {
            connectedPins = [];
            nets.Add(netName, connectedPins);
        }

        foreach (var pin in pins)
        {
            EnsureOwnedPin(pin);
            if (netNameByPinId.TryGetValue(pin.Id, out var existingNet))
            {
                if (!string.Equals(existingNet, netName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Pin '{pin.Id}' is already connected to net '{existingNet}'.");
                continue;
            }
            connectedPins.Add(pin);
            netNameByPinId.Add(pin.Id, netName);
        }
        return new CircuitNetHandle(this, netName);
    }

    public CircuitNetHandle Connect(string netName, params PinReference[] pins) =>
        Connect(netName, pins.Select(pin => ResolvePin(pin.ComponentReference, pin.PinName)).ToArray());

    public ComponentPin ResolvePin(string componentReference, string pinName)
    {
        if (!components.TryGetValue(componentReference, out var component))
            throw new KeyNotFoundException($"Component '{componentReference}' was not added to this circuit builder.");
        return component.GetPin(pinName);
    }

    public CircuitBuilder AliasNet(string alias, string target)
    {
        ValidateNetName(alias);
        ValidateNetName(target);
        if (nets.ContainsKey(alias)) throw new InvalidOperationException($"Net '{alias}' already exists as a real net.");
        if (!nets.ContainsKey(target)) throw new InvalidOperationException($"Alias target net '{target}' does not exist.");
        netAliases[alias] = target;
        return this;
    }

    public CircuitGraph Build()
    {
        var immutableNets = nets.ToDictionary(
            pair => pair.Key,
            pair => new CircuitNet(pair.Key, pair.Value.ToArray()),
            StringComparer.OrdinalIgnoreCase);
        var graph = new CircuitGraph(components, immutableNets, netAliases);
        CircuitGraphValidator.EnsureValid(graph);
        return graph;
    }

    private void EnsureOwnedPin(ComponentPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (!components.TryGetValue(pin.ComponentReference, out var component))
            throw new InvalidOperationException($"Pin '{pin.Id}' belongs to a component that was not added to this circuit builder.");
        var expectedPin = component.GetPin(pin.Name);
        if (!ReferenceEquals(expectedPin, pin))
            throw new InvalidOperationException($"Pin '{pin.Id}' is not the pin instance owned by component '{component.Reference}'.");
    }

    private static void ValidateNetName(string netName)
    {
        if (string.IsNullOrWhiteSpace(netName))
            throw new ArgumentException("Net name must not be empty.", nameof(netName));
    }

    public readonly record struct PinReference(string ComponentReference, string PinName);

    public sealed class CircuitNetHandle
    {
        private readonly CircuitBuilder builder;
        public string Name { get; }

        internal CircuitNetHandle(CircuitBuilder builder, string name)
        {
            this.builder = builder;
            Name = name;
        }

        public CircuitNetHandle Connect(params ComponentPin[] pins) => builder.Connect(Name, pins);
        public CircuitNetHandle Connect(params PinReference[] pins) => builder.Connect(Name, pins);
    }
}
