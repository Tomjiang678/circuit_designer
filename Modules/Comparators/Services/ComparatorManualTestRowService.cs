using System.Globalization;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Comparators.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Comparators.Services;

public sealed record ParsedComparatorTestRow(
    string[] InputFields,
    char Delimiter,
    ComparatorDesignRequest Request);

/// <summary>
/// Converts tab/comma-separated comparator test rows into production requests and
/// appends machine-readable results. Rows intentionally avoid quoted CSV fields so
/// they can be copied directly between TXT/Excel and the in-app batch dialog.
/// </summary>
public sealed class ComparatorManualTestRowService
{
    public const int MaximumBatchRows = 150;
    public const int InputColumnCount = 18;
    public const int ResultColumnCount = 20;
    public const int ColumnCount = InputColumnCount + ResultColumnCount;

    public const string Header =
        "PointId\tName\tKind\tTopology\tOutputMode\tLowerThresholdV\tUpperThresholdV\tLowerHysteresisV\tUpperHysteresisV\tVccV\tVeeV\tOutputHighV\tOutputLowV\tOutputBehavior\tOptimize\tRSeries\tRMinOhm\tRMaxOhm\tSuccess\tStatus\tMessage\tCircuitComponentCount\tResistorCount\tMetricCount\tFailedMetricCount\tMaxAbsErrorV\tActualLowerV\tActualUpperV\tActualWidthV\tBiasNodeV\tLowerFallingV\tLowerRisingV\tUpperFallingV\tUpperRisingV\tVerificationSummary\tResistorSummary\tCalculationSummary\tWarnings";

    public IReadOnlyList<ParsedComparatorTestRow> ParseMany(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith("PointId", StringComparison.OrdinalIgnoreCase) &&
                           !line.StartsWith("测试点", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (lines.Length == 0) throw new FormatException("No comparator test rows were found.");
        if (lines.Length > MaximumBatchRows)
            throw new FormatException($"A maximum of {MaximumBatchRows} rows can be run at once; received {lines.Length}.");

        var result = new List<ParsedComparatorTestRow>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            try { result.Add(Parse(lines[index])); }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                throw new FormatException($"Row {index + 1}: {exception.Message}", exception);
            }
        }
        return result;
    }

    public ParsedComparatorTestRow Parse(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);
        var delimiter = line.Contains('\t') ? '\t' : ',';
        var fields = line.Trim().Split(delimiter, StringSplitOptions.None).Select(value => value.Trim()).ToArray();
        if (fields.Length is not (InputColumnCount or ColumnCount))
            throw new FormatException($"Expected {InputColumnCount} input fields or {ColumnCount} completed fields, but received {fields.Length}.");

        var inputs = fields.Take(InputColumnCount).ToArray();
        var request = new ComparatorDesignRequest
        {
            Kind = EnumValue<ComparatorDesignerKind>(inputs[2], "Kind"),
            Topology = EnumValue<SchmittTopology>(inputs[3], "Topology"),
            OutputMode = EnumValue<WindowOutputMode>(inputs[4], "OutputMode"),
            LowerThresholdVoltage = Number(inputs[5], "LowerThresholdV"),
            UpperThresholdVoltage = Number(inputs[6], "UpperThresholdV"),
            LowerBoundaryHysteresisVoltage = Number(inputs[7], "LowerHysteresisV"),
            UpperBoundaryHysteresisVoltage = Number(inputs[8], "UpperHysteresisV"),
            SupplyHighVoltage = Number(inputs[9], "VccV"),
            SupplyLowVoltage = Number(inputs[10], "VeeV"),
            OutputHighVoltage = Number(inputs[11], "OutputHighV"),
            OutputLowVoltage = Number(inputs[12], "OutputLowV"),
            OutputBehavior = EnumValue<ComparatorOutputBehavior>(inputs[13], "OutputBehavior"),
            OptimizeStandardValues = Boolean(inputs[14], "Optimize"),
            ResistorSeries = EnumValue<StandardComponentSeries>(inputs[15], "RSeries"),
            MinimumResistanceOhm = Number(inputs[16], "RMinOhm"),
            MaximumResistanceOhm = Number(inputs[17], "RMaxOhm")
        };
        return new ParsedComparatorTestRow(inputs, delimiter, request);
    }

    public string Complete(ParsedComparatorTestRow parsed, ComparatorDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(result);
        var failed = result.Verification.Where(metric => !metric.Pass).ToArray();
        var thresholds = result.ActualThresholds;
        var window = result.WindowThresholds;
        var fields = new[]
        {
            result.Success ? "1" : "0",
            result.Status.ToString(),
            Clean(result.Message),
            (result.CircuitGraph?.Components.Count ?? 0).ToString(CultureInfo.InvariantCulture),
            result.Resistors.Count.ToString(CultureInfo.InvariantCulture),
            result.Verification.Count.ToString(CultureInfo.InvariantCulture),
            failed.Length.ToString(CultureInfo.InvariantCulture),
            result.Verification.Count == 0 ? "NA" : result.Verification.Max(metric => Math.Abs(metric.ErrorVoltage)).ToString("G12", CultureInfo.InvariantCulture),
            Value(thresholds?.Lower), Value(thresholds?.Upper), Value(thresholds?.Width),
            Value(result.CircuitGraph is not null && result.Request.Kind == ComparatorDesignerKind.SchmittTrigger ? result.BiasNodeVoltage : null),
            Value(window?.LowerFalling), Value(window?.LowerRising), Value(window?.UpperFalling), Value(window?.UpperRising),
            Join(result.Verification.Select(metric => $"{metric.Name}:{metric.Actual:G9}/{metric.Target:G9};pass={metric.Pass}")),
            Join(result.Resistors.Select(item => $"{item.Reference}:{item.SelectedOhm:G9}ohm;continuous={item.ContinuousOhm:G9}")),
            Join(result.CalculationTrace),
            Join(result.Warnings)
        };
        return string.Join(parsed.Delimiter, parsed.InputFields.Concat(fields));
    }

    private static string Value(double? value) => value is null ? "NA" : value.Value.ToString("G12", CultureInfo.InvariantCulture);

    private static double Number(string value, string name)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)) return result;
        throw new FormatException($"{name} must be an invariant-culture number; received '{value}'.");
    }

    private static bool Boolean(string value, string name) => value.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "是" => true,
        "0" or "false" or "no" or "否" => false,
        _ => throw new FormatException($"{name} must be 1/0 or true/false; received '{value}'.")
    };

    private static T EnumValue<T>(string value, string name) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var result) ? result :
        throw new FormatException($"{name} has unknown value '{value}'.");

    private static string Join(IEnumerable<string> values)
    {
        var joined = string.Join(" | ", values.Select(Clean).Where(value => value.Length > 0));
        return joined.Length == 0 ? "NA" : joined;
    }

    private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Replace(',', '，').Trim();
}
