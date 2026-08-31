using System.Globalization;
using System.Text;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Services;

public sealed class PrecisionAnalogSpiceExportService
{
    public string Export(AnalogComputeDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Success) throw new InvalidOperationException("Only a successful verified design can be exported.");
        var graph = result.CircuitGraph ?? throw new InvalidOperationException("The design result has no CircuitGraph.");
        var text = new StringBuilder();
        text.AppendLine($"Circuit Module Designer - Precision Analog {result.Request.Operation}");
        text.AppendLine("* Multisim-compatible Berkeley SPICE netlist");
        text.AppendLine("* Abstract op-amps use finite-gain VCVS models; VCC/VEE are explicit sources.");
        text.AppendLine($"V_VCC VCC 0 DC {N(result.Request.SupplyVccVoltage)}");
        text.AppendLine($"V_VEE VEE 0 DC {N(result.Request.SupplyVeeVoltage)}");
        foreach (var input in result.Request.Inputs)
            text.AppendLine($"V_{Safe(input.Name)} {Safe(input.Name)} 0 DC 0");
        text.AppendLine();
        foreach (var component in graph.Components.OrderBy(component => component.Type).ThenBy(component => component.Reference))
        {
            if (component.Type == ComponentType.Resistor)
            {
                text.AppendLine($"{component.Reference} {Net(graph, component, "1")} {Net(graph, component, "2")} {N(component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm))}");
                continue;
            }
            if (component.Type == ComponentType.OpAmp)
            {
                text.AppendLine($"E_{component.Reference} {Net(graph, component, "OUT")} 0 {Net(graph, component, "IN+")} {Net(graph, component, "IN-")} 1e9");
                continue;
            }
            throw new NotSupportedException($"Precision Analog CIR export does not yet map {component.Type}.");
        }
        text.AppendLine("R_OUTPUT_LOAD VOUT 0 1e9");
        var swept = result.Request.Inputs[0].Name;
        text.AppendLine(); text.AppendLine($".DC V_{Safe(swept)} -1 1 0.02");
        text.AppendLine(".PRINT DC V(VOUT)"); text.AppendLine(".END");
        return text.ToString();
    }

    public void Save(AnalogComputeDesignResult result, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A CIR file path is required.", nameof(fileName));
        File.WriteAllText(fileName, Export(result), new UTF8Encoding(false));
    }

    private static string Net(CircuitGraph graph, CircuitComponent component, string pin) =>
        graph.GetNetForPin(component.Reference, pin)?.Name is { } name ? (name.Equals("GND", StringComparison.OrdinalIgnoreCase) ? "0" : Safe(name)) : throw new InvalidOperationException($"{component.Reference}.{pin} is not connected.");
    private static string Safe(string value) => new(value.Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_').ToArray());
    private static string N(double value) => value.ToString("0.###############e+0", CultureInfo.InvariantCulture);
}
