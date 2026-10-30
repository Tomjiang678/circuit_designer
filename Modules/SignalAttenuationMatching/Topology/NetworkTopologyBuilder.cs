using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

public enum ElementPlacement
{
    Series,
    Shunt
}

public sealed record NetworkElementSpec(
    string Reference,
    ComponentType Type,
    ElementPlacement Placement,
    double Value);

/// <summary>
/// A cascade description: series elements chain the signal path, shunt elements
/// connect the current node to ground. This single representation covers T, Pi,
/// L and LC networks without a second circuit model.
/// </summary>
public sealed record NetworkTopologySpec(
    string Name,
    TopologyFamily Family,
    MatchingResponse Response,
    IReadOnlyList<NetworkElementSpec> Elements,
    string Description,
    string LimitNote = "")
{
    public bool HasSeriesPath => Elements.Any(element => element.Placement == ElementPlacement.Series);
    public bool HasShuntElement => Elements.Any(element => element.Placement == ElementPlacement.Shunt);
    public bool HasDcPath => HasSeriesPath && Elements.Where(element => element.Placement == ElementPlacement.Series)
        .All(element => element.Type != ComponentType.Capacitor);
}

public static class NetworkTopologyBuilder
{
    public const string InputNet = "VIN";
    public const string OutputNet = "VOUT";
    public const string GroundNet = "GND";

    public static CircuitGraph Build(NetworkTopologySpec spec, ComponentConstraints? constraints = null, double designFrequencyHz = 0)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Elements.Count == 0)
        {
            var directBuilder = new CircuitBuilder();
            directBuilder.CreateNet(InputNet);
            directBuilder.CreateNet(GroundNet);
            directBuilder.AliasNet(OutputNet, InputNet);
            return directBuilder.Build();
        }
        if (!spec.HasSeriesPath) throw new InvalidOperationException($"Topology '{spec.Name}' has no series signal path.");
        var builder = new CircuitBuilder();
        var components = new Dictionary<string, CircuitComponent>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in spec.Elements)
        {
            if (!double.IsFinite(element.Value) || element.Value <= 0)
                throw new InvalidOperationException($"Component '{element.Reference}' must be finite and positive.");
            if (!components.TryAdd(element.Reference, Create(element.Reference, element.Type, element.Value)))
                throw new InvalidOperationException($"Duplicate component reference '{element.Reference}'.");
            builder.Add(components[element.Reference]);
        }

        var series = spec.Elements.Where(element => element.Placement == ElementPlacement.Series).ToArray();
        var current = InputNet;
        var seriesIndex = 0;
        foreach (var element in spec.Elements)
        {
            var component = components[element.Reference];
            var parasitic = ParasiticResistance(element, constraints, designFrequencyHz);
            if (element.Placement == ElementPlacement.Series)
            {
                seriesIndex++;
                var next = seriesIndex == series.Length ? OutputNet : $"N{seriesIndex}";
                if (parasitic > 0)
                {
                    var resistor = ComponentFactory.Resistor("R_PAR_" + element.Reference, parasitic);
                    builder.Add(resistor);
                    builder.Connect(current, resistor.GetPin("1"));
                    builder.Connect("NP_" + element.Reference, resistor.GetPin("2"), component.GetPin("1"));
                    builder.Connect(next, component.GetPin("2"));
                }
                else
                {
                    builder.Connect(current, component.GetPin("1"));
                    builder.Connect(next, component.GetPin("2"));
                }
                current = next;
            }
            else if (parasitic > 0)
            {
                var resistor = ComponentFactory.Resistor("R_PAR_" + element.Reference, parasitic);
                builder.Add(resistor);
                builder.Connect(current, resistor.GetPin("1"));
                builder.Connect("NP_" + element.Reference, resistor.GetPin("2"), component.GetPin("1"));
                builder.Connect(GroundNet, component.GetPin("2"));
            }
            else
            {
                builder.Connect(current, component.GetPin("1"));
                builder.Connect(GroundNet, component.GetPin("2"));
            }
        }

        if (current != OutputNet) throw new InvalidOperationException($"Topology '{spec.Name}' did not reach the output port.");
        // A shunt-only trailing element is handled by the loop because the last
        // series element already assigned the VOUT net.
        return builder.Build();
    }

    public static IReadOnlyList<NetworkElementSpec> Elements(NetworkTopologySpec spec) => spec.Elements;

    private static double ParasiticResistance(NetworkElementSpec element, ComponentConstraints? constraints, double designFrequencyHz)
    {
        if (constraints is null) return 0;
        return element.Type switch
        {
            ComponentType.Capacitor => constraints.CapacitorEsrOhm,
            ComponentType.Inductor => constraints.InductorSeriesResistanceOhm +
                (constraints.InductorQ > 0 && designFrequencyHz > 0 ? 2 * Math.PI * designFrequencyHz * element.Value / constraints.InductorQ : 0),
            _ => 0
        };
    }

    private static CircuitComponent Create(string reference, ComponentType type, double value) => type switch
    {
        ComponentType.Resistor => ComponentFactory.Resistor(reference, value),
        ComponentType.Capacitor => ComponentFactory.Capacitor(reference, value),
        ComponentType.Inductor => ComponentFactory.Inductor(reference, value),
        _ => throw new NotSupportedException($"Only passive R/L/C components are allowed in a matching/attenuation network, not {type}.")
    };

    public static string UnitFor(ComponentType type) => type switch
    {
        ComponentType.Resistor => "ohm",
        ComponentType.Capacitor => "F",
        ComponentType.Inductor => "H",
        _ => string.Empty
    };

    public static string PrefixFor(ComponentType type) => type switch
    {
        ComponentType.Resistor => "R",
        ComponentType.Capacitor => "C",
        ComponentType.Inductor => "L",
        _ => "X"
    };
}
