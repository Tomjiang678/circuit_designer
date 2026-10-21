using System.Globalization;
using System.Net;
using System.Text;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Schematic;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Services;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Reporting;

public sealed class AttenuationMatchingHtmlReportBuilder
{
    private readonly AttenuationMatchingSpiceExportService spice = new();

    public string Build(AttenuationMatchingDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var selected = result.SelectedCandidate;
        var html = new StringBuilder(48_000);
        html.Append("""
<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Signal Attenuation &amp; Impedance Matching Report</title><style>
body{font:13px "Segoe UI",Arial,sans-serif;color:#111827;background:#fff;margin:24px auto;max-width:1180px;line-height:1.4}
h1{font-size:23px;border-bottom:2px solid #315b83;padding-bottom:7px}h2{font-size:16px;color:#244f78;border-bottom:1px solid #9aafc3;margin-top:25px;padding-bottom:3px}
h3{font-size:14px;color:#374151;margin-top:18px}.meta{color:#4b5563}table{border-collapse:collapse;width:100%;margin:8px 0 16px}
th,td{border:1px solid #9ca6af;padding:5px 7px;text-align:left;vertical-align:top}th{background:#e8edf2}.num{font-family:Consolas,"Courier New",monospace;text-align:right}
.pass{color:#176b2c;font-weight:600}.fail{color:#9e1d1d;font-weight:600}.warning{color:#8a5a00;font-weight:600}.note{color:#4b5563}
.warnbox{border-left:4px solid #c28a00;background:#fff8df;padding:6px 9px;margin:5px 0}.errbox{border-left:4px solid #9e1d1d;background:#fff0f0;padding:6px 9px;margin:5px 0}
pre{font:11.5px Consolas,"Courier New",monospace;background:#f5f5f5;border:1px solid #aaa;padding:9px;white-space:pre-wrap}
svg{max-width:100%;height:auto;border:1px solid #d1d5db;background:#fff}@media print{body{margin:8mm;max-width:none}h2{break-after:avoid}tr{break-inside:avoid}}
</style></head><body>
""");
        html.Append("<h1>Circuit Module Designer — Signal Attenuation &amp; Impedance Matching</h1>")
            .Append("<p class=\"meta\">Generated ").Append(E(DateTimeOffset.UtcNow.ToString("u"))).Append(" | Mode: ").Append(E(result.Request.Mode.ToString())).Append(" | Status: <b>").Append(E(result.Status.ToString())).Append("</b></p>");

        html.Append("<h2>1. Design Overview</h2><table><tbody>")
            .Append(Row("Design mode", result.Request.Mode.ToString()))
            .Append(Row("Status", result.Status.ToString()))
            .Append(Row("Message", result.Message))
            .Append(Row("Design frequency", N(result.Request.DesignFrequencyHz) + " Hz"))
            .Append(Row("Sweep", N(result.Request.StartFrequencyHz) + " – " + N(result.Request.StopFrequencyHz) + " Hz, " + result.Request.SweepPoints + " points (" + (result.Request.LogarithmicSweep ? "log" : "linear") + ")"))
            .Append(Row("Selected topology", selected?.TopologyName ?? "None"))
            .Append(Row("Source signal", result.Request.Signal.Description))
            .Append("</tbody></table>");

        html.Append("<h2>2. Source &amp; Load Conditions</h2><table><thead><tr><th>Port</th><th>Type</th><th>R (Ω)</th><th>X @ f0 (Ω)</th><th>Frequency model</th></tr></thead><tbody>")
            .Append(PortRow(result.Request.Source)).Append(PortRow(result.Request.Load)).Append("</tbody></table>");
        html.Append("<p class=\"note\">Source and load are external test conditions. The design CircuitGraph contains only the passive matching/attenuation network.</p>");

        html.Append("<h2>3. Design Requirements</h2><table><tbody>")
            .Append(Row("Target attenuation", N(result.Request.Attenuation.TargetAttenuationDb) + " dB"))
            .Append(Row("Allowed attenuation error", "±" + N(result.Request.Attenuation.AllowedAttenuationErrorDb) + " dB"))
            .Append(Row("Minimum input return loss", N(result.Request.Attenuation.MinimumInputReturnLossDb) + " dB"))
            .Append(Row("Minimum output return loss", N(result.Request.Attenuation.MinimumOutputReturnLossDb) + " dB"))
            .Append(Row("Matching input return loss target", N(result.Request.Matching.TargetInputReturnLossDb) + " dB"))
            .Append(Row("Matching output return loss target", result.Request.Matching.TargetOutputReturnLossDb is { } output ? N(output) + " dB" : "N/A"))
            .Append(Row("Maximum insertion loss", N(result.Request.Matching.MaximumInsertionLossDb) + " dB"))
            .Append(Row("Target bandwidth", result.Request.Matching.TargetBandwidthHz > 0 ? N(result.Request.Matching.TargetBandwidthHz) + " Hz" : "Not required"))
            .Append(Row("Component value mode", result.Request.Components.ValueMode.ToString()))
            .Append("</tbody></table>");

        html.Append("<h2>4. Selected Topology</h2>");
        if (selected is null)
            html.Append("<p class=\"errbox\">No candidate reached a verified final CircuitGraph.</p>");
        else
        {
            html.Append("<table><tbody>")
                .Append(Row("Topology", selected.TopologyName))
                .Append(Row("Family", selected.Family.ToString()))
                .Append(Row("Response", selected.Response.ToString()))
                .Append(Row("Description", selected.TopologyDescription ?? ""))
                .Append(Row("Components", selected.Components.Count.ToString(CultureInfo.InvariantCulture)))
                .Append(Row("Design note", selected.LimitNote ?? ""))
                .Append("</tbody></table>");
            html.Append("<h2>5. Circuit Schematic</h2>")
                .Append(new AttenuationMatchingSchematicSvgBuilder().Build(selected))
                .Append("<p class=\"note\">The diagram is generated from the final CircuitGraph topology. Source/load labels identify external test ports.</p>");

            html.Append("<h2>6. Component Values</h2><table><thead><tr><th>Reference</th><th>Type</th><th>Ideal</th><th>Selected</th><th>Unit</th><th>Error</th><th>Dissipation / rating</th></tr></thead><tbody>");
            foreach (var component in selected.Components)
                html.Append("<tr><td>").Append(E(component.Reference)).Append("</td><td>").Append(E(component.TypeName)).Append("</td><td class=\"num\">")
                    .Append(N(component.IdealValue)).Append("</td><td class=\"num\">").Append(N(component.SelectedValue)).Append("</td><td>").Append(E(component.Unit))
                    .Append("</td><td class=\"num\">").Append(N(component.ErrorPercent)).Append(" %</td><td>").Append(E(component.Rating)).Append("</td></tr>");
            html.Append("</tbody></table>");

            html.Append("<h2>7. Ideal vs Selected Values</h2><p class=\"note\">The selected values are the actual values in the final CircuitGraph and are the values used for verification, SPICE, and reporting. Continuous mode uses the ideal values directly.</p>");

            html.Append("<h2>8. Calculated Electrical Performance</h2><table><thead><tr><th>Metric</th><th>Value</th><th>Definition / condition</th></tr></thead><tbody>")
                .Append(MetricRow("Source impedance", Complex(selected.Metrics.SourceImpedance), "At design frequency"))
                .Append(MetricRow("Load impedance", Complex(selected.Metrics.LoadImpedance), "At design frequency"))
                .Append(MetricRow("Input impedance", Complex(selected.Metrics.InputImpedance), "Network input with specified load"))
                .Append(MetricRow("Output impedance", Complex(selected.Metrics.OutputImpedance), "Network output with specified source"))
                .Append(MetricRow("Attenuation", N(selected.Metrics.AttenuationDb) + " dB", "−10·log10(GT), specified source/load"))
                .Append(MetricRow("Insertion loss", N(selected.Metrics.InsertionLossDb) + " dB", "−10·log10(GT)"))
                .Append(MetricRow("Relative insertion loss", selected.Metrics.RelativeInsertionLossDb is { } relative ? N(relative) + " dB" : "N/A", "Relative to direct-through reference"))
                .Append(MetricRow("Matched-port attenuation / S21", selected.Metrics.MatchedPortAttenuationDb is { } matched ? N(matched) + " dB" : "N/A", "−20·log10(|S21|) for real reference impedances"))
                .Append(MetricRow("Input return loss", N(selected.Metrics.InputReturnLossDb) + " dB", "Conjugate power-wave reference"))
                .Append(MetricRow("Output return loss", N(selected.Metrics.OutputReturnLossDb) + " dB", "Conjugate power-wave reference"))
                .Append(MetricRow("VSWR input", selected.Metrics.VswrIn is { } vswrIn ? N(vswrIn) : "N/A", "Only valid for real reference impedance"))
                .Append(MetricRow("VSWR output", selected.Metrics.VswrOut is { } vswrOut ? N(vswrOut) : "N/A", "Only valid for real reference impedance"))
                .Append(MetricRow("Transducer gain", N(selected.Metrics.TransducerGain), "Pload / Pavs"))
                .Append(MetricRow("Matching bandwidth", selected.Metrics.BandwidthHz is { } bandwidth ? N(bandwidth) + " Hz" : "N/A", "Finite sweep, target return-loss and insertion-loss gates"))
                .Append(MetricRow("Loaded Q from sweep", selected.Metrics.LoadedQ is { } q ? N(q) : "N/A", "f0 / measured matched bandwidth"))
                .Append(MetricRow("Maximum resistor dissipation", N(selected.Metrics.MaximumResistorDissipationWatt) + " W", "At the specified source signal"))
                .Append(MetricRow("Total resistor dissipation", N(selected.Metrics.TotalResistorDissipationWatt) + " W", "Sum over resistors"))
                .Append("</tbody></table>");
            html.Append(ResponseSvg(selected));

            html.Append("<h2>9. Independent Verification</h2>");
            var errors = selected.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
            var warnings = selected.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Warning).ToArray();
            html.Append("<p>Hard constraints passed: <b>").Append(errors.Length == 0 ? "Yes" : "No").Append("</b>; errors: ").Append(errors.Length).Append("; warnings: ").Append(warnings.Length).Append(".</p>");
            if (selected.Diagnostics.Count > 0)
            {
                html.Append("<table><thead><tr><th>Severity</th><th>Code</th><th>Description</th><th>Parameter</th><th>Actual</th><th>Expected</th><th>Suggested action</th></tr></thead><tbody>");
                foreach (var diagnostic in selected.Diagnostics)
                    html.Append("<tr><td class=\"").Append(diagnostic.Severity == DiagnosticSeverity.Error ? "fail" : diagnostic.Severity == DiagnosticSeverity.Warning ? "warning" : "note").Append("\">").Append(E(diagnostic.Severity.ToString()))
                        .Append("</td><td>").Append(E(diagnostic.Code)).Append("</td><td>").Append(E(diagnostic.Description)).Append("</td><td>").Append(E(diagnostic.Parameter ?? ""))
                        .Append("</td><td class=\"num\">").Append(diagnostic.ActualValue is { } actual ? N(actual) : "—").Append("</td><td>").Append(E(diagnostic.ExpectedRange ?? "")).Append("</td><td>").Append(E(diagnostic.SuggestedAction ?? "")).Append("</td></tr>");
                html.Append("</tbody></table>");
            }

            html.Append("<h2>10. Frequency Response</h2>").Append(ResponseTable(selected.Sweep));

            html.Append("<h2>11. Design Warnings</h2>");
            var allWarnings = selected.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Warning).Select(item => item.Description)
                .Concat(selected.Sweep.Warnings).Concat(selected.Metrics.Warnings).Distinct().ToArray();
            if (allWarnings.Length == 0) html.Append("<p>No warnings recorded.</p>");
            else foreach (var warning in allWarnings) html.Append("<p class=\"warnbox\">").Append(E(warning)).Append("</p>");

            html.Append("<h2>12. SPICE Netlist</h2><h3>Design network</h3><pre>").Append(E(spice.ExportDesign(selected, result.Request)))
                .Append("</pre><h3>Testbench</h3><pre>").Append(E(spice.ExportTestbench(selected, result.Request))).Append("</pre>");

            html.Append("<h2>13. Assumptions &amp; Limitations</h2><ul class=\"note\">")
                .Append("<li>Ideal lumped R/L/C models only; no PCB parasitic, transmission-line or vendor model extraction.</li>")
                .Append("<li>Custom R+jX ports are held at their design-frequency value over the sweep; Series R-L/R-C reactances are frequency-dependent.</li>")
                .Append("<li>Bandwidth is limited by the finite sweep and the stated thresholds; sweep-boundary edges are not device limits.</li>")
                .Append("<li>Attenuation, insertion loss, transducer gain, S21 and return loss are distinct quantities and are reported separately.</li>")
                .Append("<li>Resistor tolerance, capacitor ESR and inductor Q are recorded as constraints but the nominal model is ideal unless explicitly represented by additional elements.</li>")
                .Append("</ul>");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    private static string ResponseSvg(TopologyCandidate candidate)
    {
        var points = candidate.Sweep.Points;
        if (points.Count < 2) return "<p class=\"note\">No frequency sweep points available.</p>";
        const double width = 920, height = 330, left = 70, right = 72, top = 48, bottom = 52;
        var minLog = Math.Log10(points.Min(point => point.FrequencyHz));
        var maxLog = Math.Log10(points.Max(point => point.FrequencyHz));
        var attenValues = points.Select(point => point.AttenuationDb).Where(double.IsFinite).ToArray();
        var rlValues = points.Select(point => point.InputReturnLossDb).Where(double.IsFinite).ToArray();
        var attMin = Math.Min(0, Math.Floor(attenValues.DefaultIfEmpty(0).Min() / 5) * 5);
        var attMax = Math.Max(attMin + 10, NiceMax(attenValues.DefaultIfEmpty(10).Max() * 1.08));
        var rlMax = Math.Max(20, NiceMax(rlValues.DefaultIfEmpty(20).Max() * 1.05));
        string X(double frequency) => (left + (Math.Log10(frequency) - minLog) / Math.Max(1e-12, maxLog - minLog) * (width - left - right)).ToString("0.##", CultureInfo.InvariantCulture);
        string YAtt(double value) => (top + (attMax - Math.Clamp(value, attMin, attMax)) / Math.Max(1e-12, attMax - attMin) * (height - top - bottom)).ToString("0.##", CultureInfo.InvariantCulture);
        string YRl(double value) => (top + (rlMax - Math.Clamp(value, 0, rlMax)) / rlMax * (height - top - bottom)).ToString("0.##", CultureInfo.InvariantCulture);
        var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 920 330\" width=\"100%\" height=\"auto\" role=\"img\" aria-label=\"Attenuation and return loss versus frequency\">");
        svg.Append("<rect width=\"920\" height=\"330\" fill=\"#fff\"/>");
        svg.Append("<text x=\"").Append(left).Append("\" y=\"18\" font-family=\"Segoe UI,Arial\" font-size=\"14\" font-weight=\"600\" fill=\"#1f2937\">Frequency response</text>");
        svg.Append("<text x=\"").Append(left).Append("\" y=\"35\" font-family=\"Segoe UI,Arial\" font-size=\"11\" fill=\"#64748b\">Attenuation (left axis) and input return loss (right axis), specified source/load</text>");
        for (var index = 0; index <= 4; index++)
        {
            var y = top + (height - top - bottom) * index / 4;
            svg.Append("<line x1=\"").Append(left).Append("\" y1=\"").Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append("\" x2=\"").Append(width - right).Append("\" y2=\"").Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append("\" stroke=\"#e2e8f0\" stroke-width=\"1\"/>");
            var att = attMax - (attMax - attMin) * index / 4;
            svg.Append("<text x=\"").Append(left - 9).Append("\" y=\"").Append((y + 4).ToString("0.##", CultureInfo.InvariantCulture)).Append("\" text-anchor=\"end\" font-family=\"Segoe UI,Arial\" font-size=\"11\" fill=\"#b91c1c\">").Append(att.ToString("0.#", CultureInfo.InvariantCulture)).Append("</text>");
            var rl = rlMax * (4 - index) / 4;
            svg.Append("<text x=\"").Append(width - right + 9).Append("\" y=\"").Append((y + 4).ToString("0.##", CultureInfo.InvariantCulture)).Append("\" font-family=\"Segoe UI,Arial\" font-size=\"11\" fill=\"#1d4ed8\">").Append(rl.ToString("0.#", CultureInfo.InvariantCulture)).Append("</text>");
        }
        for (var index = 0; index <= 5; index++)
        {
            var x = left + (width - left - right) * index / 5;
            svg.Append("<line x1=\"").Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append("\" y1=\"").Append(top).Append("\" x2=\"").Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append("\" y2=\"").Append(height - bottom).Append("\" stroke=\"#e2e8f0\" stroke-width=\"1\"/>");
            var frequency = Math.Pow(10, minLog + (maxLog - minLog) * index / 5);
            svg.Append("<text x=\"").Append(x.ToString("0.##", CultureInfo.InvariantCulture)).Append("\" y=\"").Append(height - 32).Append("\" text-anchor=\"middle\" font-family=\"Segoe UI,Arial\" font-size=\"11\" fill=\"#64748b\">").Append(E(Engineering(frequency))).Append("</text>");
        }
        svg.Append("<line x1=\"").Append(left).Append("\" y1=\"").Append(height - bottom).Append("\" x2=\"").Append(width - right).Append("\" y2=\"").Append(height - bottom).Append("\" stroke=\"#94a3b8\"/>");
        svg.Append("<line x1=\"").Append(left).Append("\" y1=\"").Append(top).Append("\" x2=\"").Append(left).Append("\" y2=\"").Append(height - bottom).Append("\" stroke=\"#94a3b8\"/>");
        svg.Append("<text x=\"").Append(left + (width - left - right) / 2 - 54).Append("\" y=\"").Append(height - 10).Append("\" font-family=\"Segoe UI,Arial\" font-size=\"11\" fill=\"#64748b\">Frequency (Hz, logarithmic)</text>");
        svg.Append("<polyline fill=\"none\" stroke=\"#b91c1c\" stroke-width=\"2.3\" stroke-linejoin=\"round\" points=\"")
            .Append(string.Join(" ", points.Select(point => X(point.FrequencyHz) + "," + YAtt(point.AttenuationDb)))).Append("\"/>");
        svg.Append("<polyline fill=\"none\" stroke=\"#1d4ed8\" stroke-width=\"2.1\" stroke-linejoin=\"round\" points=\"")
            .Append(string.Join(" ", points.Select(point => X(point.FrequencyHz) + "," + YRl(point.InputReturnLossDb)))).Append("\"/>");
        var designX = X(candidate.Metrics.FrequencyHz);
        svg.Append("<line x1=\"").Append(designX).Append("\" y1=\"").Append(top).Append("\" x2=\"").Append(designX).Append("\" y2=\"").Append(height - bottom).Append("\" stroke=\"#059669\" stroke-dasharray=\"4 3\"/>");
        svg.Append("<text x=\"").Append(designX).Append("\" y=\"").Append(top - 5).Append("\" text-anchor=\"middle\" font-family=\"Segoe UI,Arial\" font-size=\"11\" fill=\"#047857\">f0</text>");
        svg.Append("<rect x=\"").Append(left + 10).Append("\" y=\"").Append(top + 9).Append("\" width=\"168\" height=\"42\" fill=\"#ffffff\" fill-opacity=\"0.92\" stroke=\"#e2e8f0\"/>");
        svg.Append("<line x1=\"").Append(left + 20).Append("\" y1=\"").Append(top + 22).Append("\" x2=\"").Append(left + 42).Append("\" y2=\"").Append(top + 22).Append("\" stroke=\"#b91c1c\" stroke-width=\"2.3\"/><text x=\"").Append(left + 48).Append("\" y=\"").Append(top + 26).Append("\" font-family=\"Segoe UI,Arial\" font-size=\"11\" fill=\"#1f2937\">Attenuation</text>");
        svg.Append("<line x1=\"").Append(left + 20).Append("\" y1=\"").Append(top + 40).Append("\" x2=\"").Append(left + 42).Append("\" y2=\"").Append(top + 40).Append("\" stroke=\"#1d4ed8\" stroke-width=\"2.1\"/><text x=\"").Append(left + 48).Append("\" y=\"").Append(top + 44).Append("\" font-family=\"Segoe UI,Arial\" font-size=\"11\" fill=\"#1f2937\">Input return loss</text>");
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static double NiceMax(double value)
    {
        if (!(value > 0) || !double.IsFinite(value)) return 1;
        var exponent = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var fraction = value / exponent;
        return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * exponent;
    }
    private static string ResponseTable(AcSweepResult sweep)
    {
        if (sweep.Points.Count == 0) return "<p class=\"note\">No sweep points.</p>";
        var html = new StringBuilder("<table><thead><tr><th>Frequency (Hz)</th><th>Attenuation (dB)</th><th>Input RL (dB)</th><th>Output RL (dB)</th><th>VSWR in</th></tr></thead><tbody>");
        foreach (var point in sweep.Points.Where((_, index) => index % Math.Max(1, sweep.Points.Count / 40) == 0 || index == sweep.Points.Count - 1))
            html.Append("<tr><td class=\"num\">").Append(N(point.FrequencyHz)).Append("</td><td class=\"num\">").Append(N(point.AttenuationDb)).Append("</td><td class=\"num\">")
                .Append(N(point.InputReturnLossDb)).Append("</td><td class=\"num\">").Append(N(point.OutputReturnLossDb)).Append("</td><td class=\"num\">")
                .Append(point.VswrIn is { } vswr ? N(vswr) : "N/A").Append("</td></tr>");
        html.Append("</tbody></table>");
        return html.ToString();
    }

    private static string PortRow(PortImpedanceModel port) =>
        $"<tr><td>{E(port.Name)}</td><td>{E(port.Type.ToString())}</td><td class=\"num\">{N(port.ResistanceOhm)}</td><td class=\"num\">{N(port.ReactanceOhm)}</td><td>{E(port.ModelDescription)}</td></tr>";

    private static string MetricRow(string name, string value, string definition) =>
        $"<tr><td>{E(name)}</td><td class=\"num\">{E(value)}</td><td>{E(definition)}</td></tr>";

    private static string Row(string name, string value) => $"<tr><th>{E(name)}</th><td>{E(value)}</td></tr>";
    private static string Complex(System.Numerics.Complex value) => $"{N(value.Real)} {(value.Imaginary >= 0 ? "+" : "−")} j{N(Math.Abs(value.Imaginary))} Ω";
    private static string Engineering(double value) => value.ToString("G6", CultureInfo.InvariantCulture);
    private static string N(double value) => double.IsFinite(value) ? value.ToString("G8", CultureInfo.InvariantCulture) : "N/A";
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
