using System.Collections.ObjectModel;

namespace CircuitModuleDesigner.Core.Circuit;

public sealed class CircuitGraph
{
    private readonly IReadOnlyDictionary<string, CircuitComponent> components;
    private readonly IReadOnlyDictionary<string, CircuitNet> nets;
    private readonly IReadOnlyDictionary<string, CircuitNet> netByPinId;

    public IReadOnlyCollection<CircuitComponent> Components => components.Values.ToArray();
    public IReadOnlyCollection<CircuitNet> Nets => nets.Values.ToArray();

    internal CircuitGraph(
        IDictionary<string, CircuitComponent> components,
        IDictionary<string, CircuitNet> nets)
    {
        this.components = new ReadOnlyDictionary<string, CircuitComponent>(
            new Dictionary<string, CircuitComponent>(components, StringComparer.OrdinalIgnoreCase));
        this.nets = new ReadOnlyDictionary<string, CircuitNet>(
            new Dictionary<string, CircuitNet>(nets, StringComparer.OrdinalIgnoreCase));

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

    public CircuitNet GetNet(string name) =>
        nets.TryGetValue(name, out var net)
            ? net
            : throw new KeyNotFoundException($"Net '{name}' was not found.");

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
