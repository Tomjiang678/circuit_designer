using System.Globalization;
using System.Net;
using System.Text;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;

public sealed class HtmlFilterReportExporter : IFilterReportExporter
{
    public string FileExtension => ".html";

    public string Export(FilterDesignReportModel report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var html = new StringBuilder(48_000);
        html.Append("""
<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Filter Design Report</title><style>
body{font:13px "Segoe UI",Arial,sans-serif;color:#111;background:#fff;margin:24px auto;max-width:1120px;line-height:1.35}h1{font-size:22px;border-bottom:2px solid #315b83;padding-bottom:6px}h2{font-size:16px;color:#244f78;border-bottom:1px solid #9aafc3;margin-top:24px;padding-bottom:3px}h3{font-size:14px;color:#333}.meta{color:#555}table{border-collapse:collapse;width:100%;margin:7px 0 14px}th,td{border:1px solid #9ca6af;padding:4px 6px;text-align:left;vertical-align:top}th{background:#e8edf2}.num{font-family:Consolas,"Courier New",monospace;text-align:right}.pass{color:#176b2c;font-weight:600}.fail{color:#9e1d1d;font-weight:600}.warning{border-left:4px solid #c28a00;background:#fff8df;padding:5px 8px;margin:4px 0}.note{color:#555}pre{font:12px Consolas,"Courier New",monospace;background:#f5f5f5;border:1px solid #aaa;padding:8px;white-space:pre-wrap}.page-break{break-before:page}@media print{body{margin:8mm;max-width:none}h2{break-after:avoid}table{break-inside:auto}tr{break-inside:avoid}.no-print{display:none}}
</style></head><body>
""");
        html.Append("<h1>Circuit Module Designer - Filter Design Report</h1><p class=\"meta\">Generated ")
            .Append(E(report.GeneratedAtUtc.ToString("u", CultureInfo.InvariantCulture))).Append(" | Software ")
            .Append(E(report.SoftwareVersion)).Append("</p>");
        Fields(html, "1. Design Summary", report.DesignSummary);
        Fields(html, "2. Input Specifications", report.InputSpecifications);
        Fields(html, "3. Approximation Details", report.ApproximationDetails);
        PoleTable(html, report.PrototypePolesAndZeros);
        SectionTable(html, report.Sections);
        ComponentTable(html, report.Components);
        Fields(html, "6. Gain Distribution", report.GainDistribution);
        VerificationTable(html, report.Verification);
        Warnings(html, report.Warnings);
        Circuit(html, report.CircuitSummary);
        Trace(html, report.CalculationTrace);
        Reproducibility(html, report);
        html.Append("</body></html>");
        return html.ToString();
    }

    private static void Fields(StringBuilder b, string heading, IEnumerable<ReportField> fields)
    {
        b.Append("<h2>").Append(E(heading)).Append("</h2><table><thead><tr><th>Item</th><th>Value</th><th>Unit</th></tr></thead><tbody>");
        foreach (var field in fields) b.Append("<tr><td>").Append(E(field.Name)).Append("</td><td class=\"num\">").Append(E(field.Value)).Append("</td><td>").Append(E(field.Unit)).Append("</td></tr>");
        b.Append("</tbody></table>");
    }

    private static void PoleTable(StringBuilder b, IReadOnlyList<ReportPole> values)
    {
        b.Append("<h3>Prototype Poles and Zeros</h3><table><thead><tr><th>Kind</th><th>#</th><th>Real</th><th>Imaginary</th></tr></thead><tbody>");
        foreach (var value in values) b.Append("<tr><td>").Append(value.IsZero ? "Zero" : "Pole").Append("</td><td class=\"num\">").Append(value.Index).Append("</td><td class=\"num\">").Append(N(value.Real)).Append("</td><td class=\"num\">").Append(N(value.Imaginary)).Append("</td></tr>");
        b.Append("</tbody></table>");
    }

    private static void SectionTable(StringBuilder b, IReadOnlyList<ReportSection> sections)
    {
        b.Append("<h2>4. Section Decomposition</h2><table><thead><tr><th>#</th><th>Order / Response</th><th>Target f0 (Hz)</th><th>Q</th><th>Target Gain</th><th>Intrinsic Gain</th><th>Actual Gain</th><th>Polarity</th><th>Numerator</th><th>Denominator</th></tr></thead><tbody>");
        foreach (var s in sections) b.Append("<tr><td class=\"num\">").Append(s.Index).Append("</td><td>").Append(E(s.Order)).Append(" / ").Append(E(s.Response)).Append("</td><td class=\"num\">").Append(N(s.TargetF0Hz)).Append("</td><td class=\"num\">").Append(N(s.TargetQ)).Append("</td><td class=\"num\">").Append(N(s.TargetGain)).Append("</td><td class=\"num\">").Append(N(s.IntrinsicGain)).Append("</td><td class=\"num\">").Append(N(s.ActualGain)).Append("</td><td>").Append(E(s.Polarity)).Append("</td><td class=\"num\">").Append(E(Coefficients(s.Numerator))).Append("</td><td class=\"num\">").Append(E(Coefficients(s.Denominator))).Append("</td></tr>");
        b.Append("</tbody></table>");
    }

    private static void ComponentTable(StringBuilder b, IReadOnlyList<ReportComponent> components)
    {
        b.Append("<h2>5. Component Design</h2><table><thead><tr><th>Section</th><th>Topology</th><th>Role</th><th>Type</th><th>Theoretical</th><th>Selected / Actual</th><th>Series</th><th>Tolerance</th><th>Relative Error</th></tr></thead><tbody>");
        foreach (var c in components) b.Append("<tr><td class=\"num\">").Append(c.SectionIndex == 0 ? "Network" : c.SectionIndex).Append("</td><td>").Append(E(c.Topology)).Append("</td><td>").Append(E(c.Role)).Append("</td><td>").Append(E(c.ComponentType)).Append("</td><td class=\"num\">").Append(N(c.TheoreticalValue)).Append(' ').Append(E(c.Unit)).Append("</td><td class=\"num\">").Append(N(c.SelectedValue)).Append(' ').Append(E(c.Unit)).Append("</td><td>").Append(E(c.Series)).Append("</td><td class=\"num\">").Append(N(c.TolerancePercent)).Append(" %</td><td class=\"num\">").Append(N(c.RelativeError * 100)).Append(" %</td></tr>");
        b.Append("</tbody></table>");
    }

    private static void VerificationTable(StringBuilder b, IReadOnlyList<ReportVerificationRow> rows)
    {
        b.Append("<h2>7. Verification Results</h2><p class=\"note\">Actual values are taken from the selected-component whole-circuit analysis and section reverse verification.</p><table><thead><tr><th>Metric</th><th>Target</th><th>Actual</th><th>Error</th><th>Result</th></tr></thead><tbody>");
        foreach (var row in rows) b.Append("<tr><td>").Append(E(row.Metric)).Append("</td><td class=\"num\">").Append(E(row.Target)).Append("</td><td class=\"num\">").Append(E(row.Actual)).Append("</td><td class=\"num\">").Append(E(row.Error)).Append("</td><td class=\"").Append(row.Passed ? "pass\">PASS" : "fail\">FAIL").Append("</td></tr>");
        b.Append("</tbody></table>");
    }

    private static void Warnings(StringBuilder b, IReadOnlyList<string> warnings)
    {
        b.Append("<h2>8. Warnings and Design Notes</h2>");
        if (warnings.Count == 0) b.Append("<p>No design warnings were recorded.</p>");
        else foreach (var warning in warnings) b.Append("<div class=\"warning\">").Append(E(warning)).Append("</div>");
    }

    private static void Circuit(StringBuilder b, ReportCircuitSummary c) => b.Append("<h2>9. Circuit Summary</h2><table><tbody>")
        .Append(Row("Resistors", c.Resistors.ToString(CultureInfo.InvariantCulture))).Append(Row("Capacitors", c.Capacitors.ToString(CultureInfo.InvariantCulture)))
        .Append(Row("Inductors", c.Inductors.ToString(CultureInfo.InvariantCulture))).Append(Row("OpAmps", c.OpAmps.ToString(CultureInfo.InvariantCulture)))
        .Append(Row("Nets", c.Nets.ToString(CultureInfo.InvariantCulture))).Append(Row("Sections", c.Sections.ToString(CultureInfo.InvariantCulture)))
        .Append(Row("Topology chain", c.TopologyChain)).Append("</tbody></table>");

    private static void Trace(StringBuilder b, IReadOnlyList<DesignStepRecord> records)
    {
        b.Append("<h2>Calculation Trace / Engineering Explanation</h2>");
        foreach (var record in records)
        {
            b.Append("<h3>").Append(record.Sequence).Append(". ").Append(E(record.Title)).Append(" <span class=\"note\">[").Append(E(record.Category)).Append("]</span></h3><p>").Append(E(record.Explanation)).Append("</p>");
            if (record.Values.Count == 0) continue;
            b.Append("<table><tbody>");
            foreach (var value in record.Values) b.Append(Row(value.Key, value.Value));
            b.Append("</tbody></table>");
        }
    }

    private static void Reproducibility(StringBuilder b, FilterDesignReportModel report)
    {
        var json = new JsonFilterDesignSnapshotExporter().Export(report.Snapshot);
        b.Append("<h2>10. Design Reproducibility</h2><p>Snapshot schema: ").Append(E(report.Snapshot.SchemaVersion)).Append(". This JSON contains normalized SI-unit inputs and selected component values.</p><pre>").Append(E(json)).Append("</pre>");
    }

    private static string Row(string name, string value) => $"<tr><th>{E(name)}</th><td>{E(value)}</td></tr>";
    private static string Coefficients(IEnumerable<double> values) => "[" + string.Join(", ", values.Select(N)) + "]";
    private static string N(double value) => value.ToString("G10", CultureInfo.InvariantCulture);
    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
