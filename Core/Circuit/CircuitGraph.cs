using System.Collections.ObjectModel;

namespace CircuitModuleDesigner.Core.Circuit;

public sealed class CircuitGraph
{
    private readonly IReadOnlyDictionary<string, CircuitComponent> components;
    private readonly IReadOnlyDictionary<string, CircuitNet> nets;
    private readonly IReadOnlyDictionary<string, CircuitNet> netByPinId;
    private readonly IReadOnlyDictionary<string, string> netAliases;

    public IReadOnlyCollection<CircuitComponent> Components => components.Values.ToArray();
    public IReadOnlyCollection<CircuitNet> Nets => nets.Values.ToArray();

    internal CircuitGraph(
        IDictionary<string, CircuitComponent> components,
        IDictionary<string, CircuitNet> nets,
        IReadOnlyDictionary<string, string>? netAliases = null)
    {
        this.components = new ReadOnlyDictionary<string, CircuitComponent>(
            new Dictionary<string, CircuitComponent>(components, StringComparer.OrdinalIgnoreCase));
        this.nets = new ReadOnlyDictionary<string, CircuitNet>(
            new Dictionary<string, CircuitNet>(nets, StringComparer.OrdinalIgnoreCase));
        this.netAliases = new ReadOnlyDictionary<string, string>(
            netAliases is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(netAliases, StringComparer.OrdinalIgnoreCase));

        var pinMap = new Dictionary<string, CircuitNet>(StringComparer.OrdinalIgnoreCase);
        foreach (var net in this.nets.Values)
        {
            foreach (var pin in net.Pins)
            {
                if (!pinMap.TryAdd(pin.Id, net))
                    throw new InvalidOperationException($"Pin '{pin.Id}' belongs to more than one net.");
            }
        }
        netByPinId = new ReadOnlyDictionary<string, CircuitNet>(pinMap);
    }

    public CircuitComponent GetComponent(string reference) =>
        components.TryGetValue(reference, out var component)
            ? component
            : throw new KeyNotFoundException($"Component '{reference}' was not found.");

    public CircuitNet GetNet(string name)
    {
        if (nets.TryGetValue(name, out var direct)) return direct;
        if (netAliases.TryGetValue(name, out var canonical) && nets.TryGetValue(canonical, out var aliased)) return aliased;
        throw new KeyNotFoundException($"Net '{name}' was not found.");
    }

    public bool AreConnected(string first, string second) =>
        string.Equals(GetNet(first).Name, GetNet(second).Name, StringComparison.OrdinalIgnoreCase);

    public ComponentPin GetPin(string componentReference, string pinName) =>
        GetComponent(componentReference).GetPin(pinName);

    public CircuitNet? GetNetForPin(ComponentPin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);
        return netByPinId.TryGetValue(pin.Id, out var net) ? net : null;
    }

    public CircuitNet? GetNetForPin(string componentReference, string pinName) =>
        GetNetForPin(GetPin(componentReference, pinName));
}
