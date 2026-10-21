using System.Text;
using System.Text.Json;
using CircuitModuleDesigner.Core.Reporting;
using CircuitModuleDesigner.Modules.Comparators.Models;

namespace CircuitModuleDesigner.Modules.Comparators.Reporting;

public sealed class ComparatorDesignReportService
{
    public string ExportHtml(ComparatorDesignResult result)
    {
        EnsureSuccessful(result);
        return new ComparatorDesignReportBuilder().BuildHtml(result);
    }

    public string ExportJson(ComparatorDesignResult result)
    {
        EnsureSuccessful(result);
        var graph = result.CircuitGraph!;
        var snapshot = new
        {
            Schema = "CircuitModuleDesigner.ComparatorDesign.v1",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Design = new
            {
                result.Request.Kind,
                result.Request.Topology,
                result.Request.OutputMode,
                result.Status,
                result.Message
            },
            Specification = result.Request,
            ActualThresholds = result.ActualThresholds,
            BoundaryThresholds = result.WindowThresholds,
            result.Oscillator,
            result.BiasNodeVoltage,
            Components = result.Resistors,
            result.Capacitors,
            Verification = result.Verification,
            Calculations = result.CalculationTrace,
            result.Warnings,
            Circuit = new
            {
                Components = graph.Components.Select(component => new
                {
                    component.Reference,
                    Type = component.Type.ToString(),
                    Parameters = component.Parameters.Values
                }),
                Nets = graph.Nets.Select(net => new
                {
                    net.Name,
                    Pins = net.Pins.Select(pin => pin.Id)
                })
            }
        };
        return JsonSerializer.Serialize(snapshot, ReportJson.Default);
    }

    public void SaveHtml(ComparatorDesignResult result, string fileName) => Save(fileName, ExportHtml(result));
    public void SaveJson(ComparatorDesignResult result, string fileName) => Save(fileName, ExportJson(result));

    private static void Save(string fileName, string content)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("An export file path is required.", nameof(fileName));
        File.WriteAllText(fileName, content, new UTF8Encoding(false));
    }

    private static void EnsureSuccessful(ComparatorDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Success || result.CircuitGraph is null)
            throw new InvalidOperationException("Only a successful verified comparator design can be exported.");
    }
}
