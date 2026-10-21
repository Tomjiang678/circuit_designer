using System.Net;
using System.Text;
using System.Globalization;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Engineering;
using CircuitModuleDesigner.Modules.Comparators.Models;

namespace CircuitModuleDesigner.Modules.Comparators.Reporting;

public sealed class ComparatorDesignReportBuilder
{
    public string BuildText(ComparatorDesignResult result)
    {
        var b = new StringBuilder(); var r = result.Request;
        b.AppendLine("COMPARATORS & THRESHOLD CONTROL DESIGN REPORT").AppendLine();
        b.AppendLine($"Designer: {DesignerName(r.Kind)}");
        b.AppendLine($"Topology / output mode: {ModeName(r)}");
        b.AppendLine($"Standard Value Optimization: {(r.OptimizeStandardValues ? "Enabled" : "Disabled")}");
        b.AppendLine($"Selected E-Series: {(r.OptimizeStandardValues ? r.ResistorSeries.ToString() : "Not applied")}");
        b.AppendLine($"Target Thresholds: {r.LowerThresholdVoltage:G8} V .. {r.UpperThresholdVoltage:G8} V");
        if (result.ActualThresholds is { } t)
        {
            b.AppendLine($"Actual Thresholds: {t.Lower:G8} V .. {t.Upper:G8} V");
            b.AppendLine($"Threshold Error: lower {t.Lower-r.LowerThresholdVoltage:+0.######;-0.######;0} V, upper {t.Upper-r.UpperThresholdVoltage:+0.######;-0.######;0} V");
            b.AppendLine(r.Kind == ComparatorDesignerKind.WindowComparator ? $"Window Width: {t.Width:G8} V" : $"Hysteresis: {t.Width:G8} V; center={t.Center:G8} V");
        }
        if(result.Oscillator is { } oscillator)
            b.AppendLine($"Oscillation: target/actual frequency={oscillator.TargetFrequencyHz:G8}/{oscillator.ActualFrequencyHz:G8} Hz; target/actual duty={oscillator.TargetDutyCyclePercent:G6}/{oscillator.ActualDutyCyclePercent:G6} %");
        if (result.WindowThresholds is { } w) b.AppendLine($"Boundary switching: lower fall/rise={w.LowerFalling:G8}/{w.LowerRising:G8} V; upper fall/rise={w.UpperFalling:G8}/{w.UpperRising:G8} V");
        b.AppendLine().AppendLine("Selected Components"); foreach (var item in result.Resistors) b.AppendLine($"  {item.Reference}: continuous {EngineeringValueFormatter.Format(item.ContinuousOhm, "Ω")}, selected {EngineeringValueFormatter.Format(item.SelectedOhm, "Ω")}");
        foreach(var item in result.Capacitors)b.AppendLine($"  {item.Reference}: continuous {EngineeringValueFormatter.Format(item.ContinuousFarad,"F")}, selected {EngineeringValueFormatter.Format(item.SelectedFarad,"F")}");
        b.AppendLine().AppendLine("Independent CircuitGraph Verification"); foreach (var metric in result.Verification) b.AppendLine($"  {metric.Name}: target={metric.Target:G8} {metric.Unit}, graph={metric.Actual:G8} {metric.Unit}, error={metric.ErrorVoltage:G6} {metric.Unit}");
        b.AppendLine().AppendLine("Calculation Trace"); foreach (var line in result.CalculationTrace) b.AppendLine("  " + line);
        if (result.Warnings.Count > 0) { b.AppendLine().AppendLine("Warnings"); foreach (var warning in result.Warnings) b.AppendLine("  " + warning); }
        return b.ToString();
    }

    public string BuildHtml(ComparatorDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var request = result.Request;
        var html = new StringBuilder(24_000);
        html.Append("""
<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Comparator Design Report</title><style>
body{font:13px "Segoe UI",Arial,sans-serif;color:#111;background:#fff;margin:24px auto;max-width:1120px;line-height:1.35}h1{font-size:22px;border-bottom:2px solid #315b83;padding-bottom:6px}h2{font-size:16px;color:#244f78;border-bottom:1px solid #9aafc3;margin-top:24px;padding-bottom:3px}.meta{color:#555}table{border-collapse:collapse;width:100%;margin:7px 0 14px}th,td{border:1px solid #9ca6af;padding:4px 6px;text-align:left;vertical-align:top}th{background:#e8edf2}.num{font-family:Consolas,"Courier New",monospace;text-align:right}.warning{border-left:4px solid #c28a00;background:#fff8df;padding:5px 8px;margin:4px 0}.error{border-left:4px solid #9e1d1d;background:#fff0f0;padding:5px 8px;margin:4px 0}@media print{body{margin:8mm;max-width:none}h2{break-after:avoid}table{break-inside:auto}tr{break-inside:avoid}}
</style></head><body>
""");
        html.Append("<h1>Circuit Module Designer - Comparator Design Report</h1><p class=\"meta\">Generated ")
            .Append(E(DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture)))
            .Append(" | Selected-value CircuitGraph verification</p>");

        html.Append("<h2>1. Design Definition</h2><table><thead><tr><th>Item</th><th>Value</th><th>Unit</th></tr></thead><tbody>")
            .Append(Field("Designer", DesignerName(request.Kind)))
            .Append(Field("Topology / output mode", ModeName(request)))
            .Append(Field("Standard-value optimization", request.OptimizeStandardValues ? "Enabled" : "Disabled"))
            .Append(Field("Resistor series", request.OptimizeStandardValues ? request.ResistorSeries.ToString() : "Not applied"))
            .Append(Field("Supply high VCC", N(request.SupplyHighVoltage), "V"))
            .Append(Field(request.SupplyLowVoltage == 0 ? "Ground" : "Supply low VEE", N(request.SupplyLowVoltage), "V"))
            .Append(Field("Output high VOH", N(request.OutputHighVoltage), "V"))
            .Append(Field("Output low VOL", N(request.OutputLowVoltage), "V"))
            .Append("</tbody></table>");

        if (!result.Success)
        {
            html.Append("<h2>2. Design Error</h2><div class=\"error\">").Append(E(result.Message)).Append("</div></body></html>");
            return html.ToString();
        }

        AppendThresholds(html, result);
        AppendComponents(html, result);
        AppendVerification(html, result);
        AppendCircuitSummary(html, result);
        AppendCalculations(html, result);
        AppendWarnings(html, result.Warnings);
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void AppendThresholds(StringBuilder html, ComparatorDesignResult result)
    {
        var request = result.Request;
        var actual = result.ActualThresholds!;
        html.Append("<h2>2. Threshold Design</h2><table><thead><tr><th>Quantity</th><th>Target</th><th>Actual</th><th>Error</th><th>Unit</th></tr></thead><tbody>")
            .Append(ThresholdRow("Lower threshold", request.LowerThresholdVoltage, actual.Lower))
            .Append(ThresholdRow("Upper threshold", request.UpperThresholdVoltage, actual.Upper));
        if (request.Kind != ComparatorDesignerKind.WindowComparator)
        {
            html.Append(ThresholdRow("Hysteresis", request.UpperThresholdVoltage - request.LowerThresholdVoltage, actual.Width))
                .Append(ThresholdRow("Center threshold", (request.LowerThresholdVoltage + request.UpperThresholdVoltage) / 2, actual.Center));
        }
        else
        {
            html.Append(ThresholdRow("Window width", request.UpperThresholdVoltage - request.LowerThresholdVoltage, actual.Width));
        }
        html.Append("</tbody></table>");
        if(result.Oscillator is { } oscillator)
        {
            html.Append("<h3>Oscillation</h3><table><thead><tr><th>Quantity</th><th>Target</th><th>Actual</th><th>Unit</th></tr></thead><tbody>")
                .Append($"<tr><td>Frequency</td><td class=\"num\">{N(oscillator.TargetFrequencyHz)}</td><td class=\"num\">{N(oscillator.ActualFrequencyHz)}</td><td>Hz</td></tr>")
                .Append($"<tr><td>Duty cycle</td><td class=\"num\">{N(oscillator.TargetDutyCyclePercent)}</td><td class=\"num\">{N(oscillator.ActualDutyCyclePercent)}</td><td>%</td></tr>")
                .Append($"<tr><td>High time</td><td></td><td class=\"num\">{N(oscillator.HighTimeSeconds)}</td><td>s</td></tr>")
                .Append($"<tr><td>Low time</td><td></td><td class=\"num\">{N(oscillator.LowTimeSeconds)}</td><td>s</td></tr></tbody></table>");
        }

        if (result.WindowThresholds is not { } window) return;
        html.Append("<h3>Boundary Switching Thresholds</h3><table><thead><tr><th>Boundary</th><th>Falling sweep</th><th>Rising sweep</th><th>Unit</th></tr></thead><tbody>")
            .Append(BoundaryRow("Lower", window.LowerFalling, window.LowerRising))
            .Append(BoundaryRow("Upper", window.UpperFalling, window.UpperRising))
            .Append("</tbody></table>");
    }

    private static void AppendComponents(StringBuilder html, ComparatorDesignResult result)
    {
        html.Append("<h2>3. Component Design</h2><table><thead><tr><th>Reference</th><th>Continuous</th><th>Selected / Actual</th><th>Series</th><th>Relative change</th></tr></thead><tbody>");
        foreach (var resistor in result.Resistors)
        {
            var relative = resistor.ContinuousOhm == 0 ? 0 : (resistor.SelectedOhm - resistor.ContinuousOhm) / resistor.ContinuousOhm * 100;
            html.Append("<tr><td>").Append(E(resistor.Reference)).Append("</td><td class=\"num\">")
                .Append(E(EngineeringValueFormatter.Format(resistor.ContinuousOhm, "Ω"))).Append("</td><td class=\"num\">")
                .Append(E(EngineeringValueFormatter.Format(resistor.SelectedOhm, "Ω"))).Append("</td><td>")
                .Append(E(result.Request.OptimizeStandardValues ? result.Request.ResistorSeries.ToString() : "Continuous")).Append("</td><td class=\"num\">")
                .Append(N(relative)).Append(" %</td></tr>");
        }
        foreach(var capacitor in result.Capacitors)
        {
            var relative=capacitor.ContinuousFarad==0?0:(capacitor.SelectedFarad-capacitor.ContinuousFarad)/capacitor.ContinuousFarad*100;
            html.Append("<tr><td>").Append(E(capacitor.Reference)).Append("</td><td class=\"num\">")
                .Append(E(EngineeringValueFormatter.Format(capacitor.ContinuousFarad,"F"))).Append("</td><td class=\"num\">")
                .Append(E(EngineeringValueFormatter.Format(capacitor.SelectedFarad,"F"))).Append("</td><td>E24</td><td class=\"num\">")
                .Append(N(relative)).Append(" %</td></tr>");
        }
        html.Append("</tbody></table>");
    }

    private static void AppendVerification(StringBuilder html, ComparatorDesignResult result)
    {
        html.Append("<h2>4. Independent CircuitGraph Verification</h2><table><thead><tr><th>Switching condition</th><th>Target</th><th>Graph-derived</th><th>Error</th><th>Unit</th></tr></thead><tbody>");
        foreach (var metric in result.Verification)
            html.Append("<tr><td>").Append(E(metric.Name)).Append("</td><td class=\"num\">").Append(N(metric.Target))
                .Append("</td><td class=\"num\">").Append(N(metric.Actual)).Append("</td><td class=\"num\">")
                .Append(N(metric.ErrorVoltage)).Append("</td><td>").Append(E(metric.Unit)).Append("</td></tr>");
        html.Append("</tbody></table>");
    }

    private static void AppendCircuitSummary(StringBuilder html, ComparatorDesignResult result)
    {
        var graph = result.CircuitGraph!;
        html.Append("<h2>5. Circuit Summary</h2><table><tbody>")
            .Append(Row("Comparators", graph.Components.Count(component => component.Type == ComponentType.Comparator).ToString(CultureInfo.InvariantCulture)))
            .Append(Row("Output op-amp stages", graph.Components.Count(component => component.Type == ComponentType.OpAmp).ToString(CultureInfo.InvariantCulture)))
            .Append(Row("Resistors", graph.Components.Count(component => component.Type == ComponentType.Resistor).ToString(CultureInfo.InvariantCulture)))
            .Append(Row("Capacitors", graph.Components.Count(component => component.Type == ComponentType.Capacitor).ToString(CultureInfo.InvariantCulture)))
            .Append(Row("Nets", graph.Nets.Count.ToString(CultureInfo.InvariantCulture)))
            .Append(Row("Output behavior", result.Request.OutputBehavior.ToString()))
            .Append("</tbody></table>");
    }

    private static void AppendCalculations(StringBuilder html, ComparatorDesignResult result)
    {
        html.Append("<h2>6. Calculation Trace</h2><table><thead><tr><th>#</th><th>Derivation / selection step</th></tr></thead><tbody>");
        for (var index = 0; index < result.CalculationTrace.Count; index++)
            html.Append("<tr><td class=\"num\">").Append(index + 1).Append("</td><td>").Append(E(result.CalculationTrace[index])).Append("</td></tr>");
        html.Append("</tbody></table>");
    }

    private static void AppendWarnings(StringBuilder html, IReadOnlyList<string> warnings)
    {
        html.Append("<h2>7. Warnings and Engineering Notes</h2>");
        if (warnings.Count == 0)
        {
            html.Append("<p>No design warnings were recorded.</p>");
            return;
        }

        foreach (var warning in warnings) html.Append("<div class=\"warning\">").Append(E(warning)).Append("</div>");
    }

    private static string Field(string name, string value, string unit = "") =>
        $"<tr><td>{E(name)}</td><td class=\"num\">{E(value)}</td><td>{E(unit)}</td></tr>";

    private static string ThresholdRow(string name, double target, double actual) =>
        $"<tr><td>{E(name)}</td><td class=\"num\">{N(target)}</td><td class=\"num\">{N(actual)}</td><td class=\"num\">{N(actual - target)}</td><td>V</td></tr>";

    private static string BoundaryRow(string name, double falling, double rising) =>
        $"<tr><td>{E(name)}</td><td class=\"num\">{N(falling)}</td><td class=\"num\">{N(rising)}</td><td>V</td></tr>";

    private static string Row(string name, string value) => $"<tr><th>{E(name)}</th><td>{E(value)}</td></tr>";
    private static string N(double value) => value.ToString("G10", CultureInfo.InvariantCulture);
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string DesignerName(ComparatorDesignerKind kind)=>kind switch
    {
        ComparatorDesignerKind.SchmittTrigger=>"Schmitt Trigger",
        ComparatorDesignerKind.WindowComparator=>"Window Comparator",
        ComparatorDesignerKind.RelaxationOscillator=>"Relaxation Oscillator",
        _=>kind.ToString()
    };
    private static string ModeName(ComparatorDesignRequest request)=>request.Kind switch
    {
        ComparatorDesignerKind.SchmittTrigger=>request.Topology.ToString(),
        ComparatorDesignerKind.WindowComparator=>request.OutputMode.ToString(),
        ComparatorDesignerKind.RelaxationOscillator=>"Inverting Schmitt RC",
        _=>string.Empty
    };
}
