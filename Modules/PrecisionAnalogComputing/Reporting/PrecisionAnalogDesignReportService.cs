using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Reporting;

public sealed class PrecisionAnalogDesignReportService
{
    public string ExportHtml(AnalogComputeDesignResult result)
    {
        EnsureSuccessful(result);
        var html = new StringBuilder(32_000);
        html.Append("""
<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Precision Analog Computing Design Report</title><style>
body{font:13px "Segoe UI",Arial,sans-serif;color:#111;background:#fff;margin:24px auto;max-width:1120px;line-height:1.35}h1{font-size:22px;border-bottom:2px solid #315b83;padding-bottom:6px}h2{font-size:16px;color:#244f78;border-bottom:1px solid #9aafc3;margin-top:24px;padding-bottom:3px}h3{font-size:14px;color:#333}.meta{color:#555}table{border-collapse:collapse;width:100%;margin:7px 0 14px}th,td{border:1px solid #9ca6af;padding:4px 6px;text-align:left;vertical-align:top}th{background:#e8edf2}.num{font-family:Consolas,"Courier New",monospace;text-align:right}.pass{color:#176b2c;font-weight:600}.fail{color:#9e1d1d;font-weight:600}.warning{border-left:4px solid #c28a00;background:#fff8df;padding:5px 8px;margin:4px 0}.note{color:#555}pre{font:12px Consolas,"Courier New",monospace;background:#f5f5f5;border:1px solid #aaa;padding:8px;white-space:pre-wrap}@media print{body{margin:8mm;max-width:none}h2{break-after:avoid}table{break-inside:auto}tr{break-inside:avoid}.no-print{display:none}}
</style></head><body>
""");
        html.Append("<h1>Circuit Module Designer - Precision Analog Computing Design Report</h1><p class=\"meta\">Generated ")
            .Append(E(DateTimeOffset.UtcNow.ToString("u"))).Append(" | Verified selected-value CircuitGraph</p>");
        html.Append("<h2>1. Design Summary</h2><table><tbody>")
            .Append(Row("Target", result.TargetRelation)).Append(Row("Topology", result.TopologyName))
            .Append(Row("Supply", $"VCC={N(result.RequiredVccVoltage)} V, GND=0 V, VEE={N(result.RequiredVeeVoltage)} V"))
            .Append(Row("Design status", "Success")).Append(Row("CapabilityLevel", result.Capability.Level.ToString()))
            .Append(Row("CapabilityReasonCode", result.Capability.ReasonCode.ToString()))
            .Append(Row("Allowed function error", N(result.Request.MaximumFunctionErrorPercent) + " %"))
            .Append(Row("Resistor series", result.Request.Components.ResistorSeries.ToString()))
            .Append("</tbody></table>");
        html.Append("<h2>2. Component Values</h2><table><thead><tr><th>Reference</th><th>Role</th><th>Theoretical</th><th>Selected</th></tr></thead><tbody>");
        foreach (var component in result.Components) html.Append("<tr><td>").Append(E(component.Reference)).Append("</td><td>").Append(E(component.Role))
            .Append("</td><td class=\"num\">").Append(N(component.TheoreticalValue)).Append(' ').Append(E(component.Unit)).Append("</td><td class=\"num\">").Append(N(component.SelectedValue)).Append(' ').Append(E(component.Unit)).Append("</td></tr>");
        html.Append("</tbody></table><h2>3. Actual Verification</h2><table><thead><tr><th>Metric</th><th>Target</th><th>Actual</th><th>Error</th><th>Tolerance</th><th>Result</th></tr></thead><tbody>");
        foreach (var metric in result.VerificationMetrics) html.Append("<tr><td>").Append(E(metric.Name)).Append("</td><td class=\"num\">").Append(N(metric.Target)).Append("</td><td class=\"num\">").Append(N(metric.Actual))
            .Append("</td><td class=\"num\">").Append(N(metric.Error)).Append("</td><td class=\"num\">").Append(N(metric.Tolerance)).Append("</td><td class=\"").Append(metric.Pass ? "pass\">PASS" : "fail\">FAIL").Append("</td></tr>");
        html.Append("</tbody></table>");
        foreach(var warning in result.Warnings)html.Append("<p class=\"warning\">").Append(E(warning)).Append("</p>");
        html.Append("<h2>4. JSON Snapshot</h2><details><summary>Show JSON</summary><pre>").Append(E(ExportJson(result))).Append("</pre></details></body></html>");
        return html.ToString();
    }

    public string ExportJson(AnalogComputeDesignResult result)
    {
        EnsureSuccessful(result);
        var snapshot = new
        {
            Schema = "CircuitModuleDesigner.PrecisionAnalogDesign.v1",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Design = new { result.Request.Operation, result.TargetRelation, result.TopologyName },
            Supply = new { VccVoltage = result.RequiredVccVoltage, GroundVoltage = 0d, VeeVoltage = result.RequiredVeeVoltage, ReferencePolicy = "Derived from VCC/GND/VEE; no independent VREF input." },
            Capability = new { result.Capability.Level, result.Capability.IsSupported, result.Capability.ReasonCode, result.Capability.Message, result.Capability.SuggestedAlternative },
            OperationSpecification = result.Request,
            Inputs = result.Request.Inputs,
            ComponentConstraints = result.Request.Components,
            Components = result.Components,
            Verification = result.VerificationMetrics,
            Calculations = result.Calculations,
            Warnings = result.Warnings,
            Circuit = new
            {
                Components = result.CircuitGraph!.Components.Select(component => new { component.Reference, Type = component.Type.ToString(), Parameters = component.Parameters.Values }),
                Nets = result.CircuitGraph.Nets.Select(net => new { net.Name, Pins = net.Pins.Select(pin => pin.Id) })
            }
        };
        return JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        });
    }

    public void SaveHtml(AnalogComputeDesignResult result, string fileName) => Save(fileName, ExportHtml(result));
    public void SaveJson(AnalogComputeDesignResult result, string fileName) => Save(fileName, ExportJson(result));

    private static void Save(string fileName, string content)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("An export file path is required.", nameof(fileName));
        File.WriteAllText(fileName, content, new UTF8Encoding(false));
    }

    private static void EnsureSuccessful(AnalogComputeDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Success || result.CircuitGraph is null) throw new InvalidOperationException("Only a successful verified design can be exported.");
    }

    private static string Row(string name, string value) => $"<tr><th>{E(name)}</th><td>{E(value)}</td></tr>";
    private static string N(double value) => value.ToString("G10", System.Globalization.CultureInfo.InvariantCulture);
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
