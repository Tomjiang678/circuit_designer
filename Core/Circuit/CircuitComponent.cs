using System.Collections.ObjectModel;

namespace CircuitModuleDesigner.Core.Circuit;

public sealed class CircuitComponent
{
    private readonly IReadOnlyDictionary<string, ComponentPin> pins;

    public string Reference { get; }
    public ComponentType Type { get; }
    public ComponentParameters Parameters { get; }
    public IReadOnlyCollection<ComponentPin> Pins => pins.Values.ToArray();

    internal CircuitComponent(
        string reference,
        ComponentType type,
        ComponentParameters parameters,
        IEnumerable<string> pinNames)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("Component reference must not be empty.", nameof(reference));
        Reference = reference.Trim();
        Type = type;
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

        var pinMap = new Dictionary<string, ComponentPin>(StringComparer.OrdinalIgnoreCase);
        foreach (var pinName in pinNames)
        {
            var pin = new ComponentPin(Reference, pinName);
            if (!pinMap.TryAdd(pin.Name, pin))
                throw new ArgumentException($"Duplicate pin '{pin.Name}' on component '{Reference}'.", nameof(pinNames));
        }
        if (pinMap.Count == 0)
            throw new ArgumentException("A component must contain at least one pin.", nameof(pinNames));
        pins = new ReadOnlyDictionary<string, ComponentPin>(pinMap);
    }

    public ComponentPin GetPin(string name) =>
        pins.TryGetValue(name, out var pin)
            ? pin
            : throw new KeyNotFoundException($"Component '{Reference}' does not contain pin '{name}'.");

    public bool TryGetPin(string name, out ComponentPin? pin) => pins.TryGetValue(name, out pin);

    public override string ToString() => Reference;
}
