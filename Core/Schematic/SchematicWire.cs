namespace CircuitModuleDesigner.Core.Schematic;

public sealed class SchematicWire
{
    public string NetName { get; }
    public IReadOnlyList<SchematicPoint> Points { get; }

    public SchematicWire(string netName, IEnumerable<SchematicPoint> points)
    {
        if (string.IsNullOrWhiteSpace(netName))
            throw new ArgumentException("Wire net name must not be empty.", nameof(netName));
        ArgumentNullException.ThrowIfNull(points);
        var copy = points.ToArray();
        if (copy.Length < 2)
            throw new ArgumentException("A wire must contain at least two points.", nameof(points));
        NetName = netName;
        Points = copy;
    }
}
