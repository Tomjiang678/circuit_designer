using CircuitModuleDesigner.Modules.Comparators.Engines;
using CircuitModuleDesigner.Modules.Comparators.Models;
using CircuitModuleDesigner.Modules.Comparators.Services;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

var defaultPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "Comparator_150_TestPoints.txt"));
var path = args.Length > 0 ? Path.GetFullPath(args[0]) : defaultPath;
if (!File.Exists(path)) throw new FileNotFoundException("The comparator batch input file was not found.", path);

var service = new ComparatorManualTestRowService();
var rows = service.ParseMany(File.ReadAllText(path));
var engine = new ComparatorDesignEngine();
var completed = new List<string>(rows.Count);
var statuses = new Dictionary<ComparatorDesignStatus, int>();
foreach (var row in rows)
{
    var result = engine.Design(row.Request);
    statuses[result.Status] = statuses.GetValueOrDefault(result.Status) + 1;
    if (result.Resistors.Count > 0)
    {
        if (row.Request.OptimizeStandardValues && result.Resistors.Any(item =>
                !StandardValueSeries.IsSeriesValue(item.SelectedOhm, row.Request.ResistorSeries, 5e-3)))
            throw new InvalidOperationException($"Point {row.InputFields[0]} selected a resistor outside {row.Request.ResistorSeries}.");
        if (!row.Request.OptimizeStandardValues && result.Resistors.Any(item =>
                Math.Abs(item.SelectedOhm - item.ContinuousOhm) > 1e-10 * Math.Max(1, Math.Abs(item.ContinuousOhm))))
            throw new InvalidOperationException($"Point {row.InputFields[0]} rounded a resistor while Optimize=false.");
    }
    var line = service.Complete(row, result);
    if (line.Split('\t', StringSplitOptions.None).Length != ComparatorManualTestRowService.ColumnCount)
        throw new InvalidOperationException($"Point {row.InputFields[0]} did not produce {ComparatorManualTestRowService.ColumnCount} fields.");
    completed.Add(line);
}

if (args.Length > 1)
{
    var outputPath = Path.GetFullPath(args[1]);
    File.WriteAllText(outputPath, ComparatorManualTestRowService.Header + Environment.NewLine +
        string.Join(Environment.NewLine, completed) + Environment.NewLine, new System.Text.UTF8Encoding(true));
    Console.WriteLine($"Wrote completed results: {outputPath}");
}

Console.WriteLine($"PASS: parsed and completed {rows.Count} comparator rows.");
foreach (var status in statuses.OrderBy(item => item.Key)) Console.WriteLine($"  {status.Key}: {status.Value}");
