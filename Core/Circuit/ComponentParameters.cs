using System.Collections.ObjectModel;

namespace CircuitModuleDesigner.Core.Circuit;

public sealed class ComponentParameters
{
    private readonly IReadOnlyDictionary<string, double> values;

    public static ComponentParameters Empty { get; } = new(new Dictionary<string, double>());

    public IReadOnlyDictionary<string, double> Values => values;

    public ComponentParameters(IEnumerable<KeyValuePair<string, double>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new ArgumentException("Parameter names must not be empty.", nameof(values));
            // The parameter bag also carries signed voltages and zero-based enum values.
            // Individual component factories/serializers enforce positivity where it is electrical.
            if (!double.IsFinite(pair.Value))
                throw new ArgumentOutOfRangeException(nameof(values), pair.Value, $"Parameter '{pair.Key}' must be finite.");
            if (!copy.TryAdd(pair.Key, pair.Value))
                throw new ArgumentException($"Duplicate parameter '{pair.Key}'.", nameof(values));
        }
        this.values = new ReadOnlyDictionary<string, double>(copy);
    }

    public bool TryGetValue(string name, out double value) => values.TryGetValue(name, out value);

    public double GetRequiredValue(string name) =>
        values.TryGetValue(name, out var value)
            ? value
            : throw new KeyNotFoundException($"Component parameter '{name}' was not found.");
}
