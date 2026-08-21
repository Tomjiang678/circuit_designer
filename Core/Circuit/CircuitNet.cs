namespace CircuitModuleDesigner.Core.Circuit;

public sealed class CircuitNet
{
    public string Name { get; }
    public IReadOnlyList<ComponentPin> Pins { get; }

    internal CircuitNet(string name, IReadOnlyList<ComponentPin> pins)
    {
        Name = name;
        Pins = pins;
    }

    public override string ToString() => Name;
}
