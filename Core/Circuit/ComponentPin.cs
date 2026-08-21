namespace CircuitModuleDesigner.Core.Circuit;

public sealed class ComponentPin
{
    public string Id { get; }
    public string ComponentReference { get; }
    public string Name { get; }

    internal ComponentPin(string componentReference, string name)
    {
        if (string.IsNullOrWhiteSpace(componentReference))
            throw new ArgumentException("Component reference must not be empty.", nameof(componentReference));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Pin name must not be empty.", nameof(name));
        ComponentReference = componentReference;
        Name = name;
        Id = $"{componentReference}.{name}";
    }

    public override string ToString() => Id;
}
