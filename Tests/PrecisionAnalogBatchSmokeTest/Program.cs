using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Engines;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Services;

var defaultPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "PrecisionAnalogComputing_500_TestPoints.txt"));
var path = args.Length > 0 ? Path.GetFullPath(args[0]) : defaultPath;
if (!File.Exists(path)) throw new FileNotFoundException("The batch input file was not found.", path);

var service = new PrecisionAnalogManualTestRowService();
var rows = service.ParseMany(File.ReadAllText(path));

var engine = new PrecisionAnalogDesignEngine();
var auditIds = args.Length > 2
    ? args[2].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)
    : [];
var success = 0;
var failures = new Dictionary<string, int>(StringComparer.Ordinal);
var completedRows = new List<string>(rows.Count);
foreach (var row in rows)
{
    var result = engine.Design(row.Request);
    if (!row.Request.Components.OptimizeStandardValues)
    {
        var altered = result.Components.Where(component => component.Quantity is "Resistance" or "Capacitance")
            .FirstOrDefault(component => component.Series is not null ||
                Math.Abs(component.SelectedValue - component.TheoreticalValue) > 1e-12 * Math.Max(1, Math.Abs(component.TheoreticalValue)));
        if (altered is not null)
            throw new InvalidOperationException($"Optimize=false changed {altered.Reference} from its continuous value or assigned an E-series.");
    }
    else
    {
        var missingSeries = result.Components.Where(component => component.Quantity is "Resistance" or "Capacitance")
            .FirstOrDefault(component => component.Series is null);
        if (missingSeries is not null)
            throw new InvalidOperationException($"Optimize=true did not retain E-series provenance for {missingSeries.Reference}.");
    }
    if (auditIds.Contains(row.InputFields[0]))
    {
        Console.WriteLine($"AUDIT Point {row.InputFields[0]} {row.InputFields[1]}");
        Console.WriteLine("  Components: " + string.Join("; ", result.Components.Select(component => $"{component.Reference}={component.SelectedValue:G12}{component.Unit}")));
        foreach (var metric in result.VerificationMetrics)
            Console.WriteLine($"  Metric: {metric.Name}; target={metric.Target:G12}; actual={metric.Actual:G12}; tolerance={metric.Tolerance:G12}; pass={metric.Pass}");
        foreach (var calculation in result.Calculations.Where(item => item.Title.Contains("optimization audit", StringComparison.OrdinalIgnoreCase)))
            Console.WriteLine($"  Search: {calculation.Expression}; {calculation.Result}; {calculation.Note}");
    }
    if (result.Success) success++;
    else
    {
        var key = $"{result.FailureStage}/{result.Capability.ReasonCode}";
        failures[key] = failures.GetValueOrDefault(key) + 1;
    }
    var completed = service.Complete(row, result);
    completedRows.Add(completed);
    if (completed.Split('\t', StringSplitOptions.None).Length != PrecisionAnalogManualTestRowService.ColumnCount)
        throw new InvalidOperationException("A completed result row did not contain 57 fields.");
}

if (args.Length > 1)
{
    var outputPath = Path.GetFullPath(args[1]);
    var content = PrecisionAnalogManualTestRowService.Header + Environment.NewLine +
                  string.Join(Environment.NewLine, completedRows) + Environment.NewLine;
    File.WriteAllText(outputPath, content, new System.Text.UTF8Encoding(true));
    Console.WriteLine($"Wrote completed results: {outputPath}");
}

Console.WriteLine($"PASS: parsed and completed {rows.Count} rows; {success} succeeded, {rows.Count - success} failed/unsupported as test coverage.");
foreach (var failure in failures.OrderByDescending(item => item.Value))
    Console.WriteLine($"  {failure.Key}: {failure.Value}");
