using System.Globalization;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Services;

public sealed record ParsedPrecisionAnalogTestRow(
    string[] InputFields,
    char Delimiter,
    AnalogComputeDesignRequest Request);

/// <summary>
/// Converts tab/comma separated manual-test rows to production design requests and
/// appends machine-readable result fields. The format intentionally avoids quoted
/// CSV fields so rows can be copied directly between Excel and the dialog.
/// </summary>
public sealed class PrecisionAnalogManualTestRowService
{
    public const int MaximumBatchRows = 500;
    public const int InputColumnCount = 40;
    public const int ResultColumnCount = 17;
    public const int ColumnCount = InputColumnCount + ResultColumnCount;

    public const string Header =
        "PointId\tName\tOperation\tInputs\tOutputOffsetV\tVinLowV\tVinHighV\tVoutLowV\tVoutHighV\tGainK\tHalfWavePolarity\tOutputPolarity\tVinMinV\tVinMaxV\tMaxFrequencyHz\tReferenceVoltageV\tLogScaleV\tKiPerSecond\tKdSecond\tLowerCornerHz\tUpperCornerHz\tVccV\tVeeV\tMaxErrorPercent\tRSeries\tRMinOhm\tRMaxOhm\tCSeries\tCMinF\tCMaxF\tOptimize\tBiasCompensation\tIdealOpAmp\tGBWHz\tSlewVPerSecond\tJunctionModel\tSaturationCurrentA\tEmissionCoefficient\tTemperatureC\tMaxJunctionCurrentA\tSuccess\tCapabilityLevel\tReasonCode\tFailureStage\tTopology\tTargetRelation\tComponentCount\tMetricCount\tFailedMetricCount\tWorstMetricRatio\tAnalysisType\tAnalysisPointCount\tWarningCount\tCapabilityMessage\tFailedMetrics\tComponentSummary\tWarnings";

    public IReadOnlyList<ParsedPrecisionAnalogTestRow> ParseMany(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith("测试点", StringComparison.OrdinalIgnoreCase) &&
                           !line.StartsWith("PointId", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (lines.Length == 0) throw new FormatException("No test rows were found.");
        if (lines.Length > MaximumBatchRows)
            throw new FormatException($"A maximum of {MaximumBatchRows} rows can be run at once; received {lines.Length}.");

        var result = new List<ParsedPrecisionAnalogTestRow>(lines.Length);
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

    public ParsedPrecisionAnalogTestRow Parse(string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(line);
        var delimiter = line.Contains('\t') ? '\t' : ',';
        var fields = line.Trim().Split(delimiter, StringSplitOptions.None)
            .Select(value => value.Trim()).ToArray();
        if (fields.Length is not (InputColumnCount or ColumnCount))
            throw new FormatException($"Expected {InputColumnCount} input fields or {ColumnCount} completed fields, but received {fields.Length}.");

        var inputs = fields.Take(InputColumnCount).ToArray();
        var request = new AnalogComputeDesignRequest
        {
            Operation = EnumValue<AnalogComputeOperationType>(inputs[2], "Operation"),
            Inputs = ParseInputs(inputs[3]),
            OutputOffsetVoltage = Number(inputs[4], "OutputOffsetV"),
            InputLowVoltage = Number(inputs[5], "VinLowV"),
            InputHighVoltage = Number(inputs[6], "VinHighV"),
            OutputLowVoltage = Number(inputs[7], "VoutLowV"),
            OutputHighVoltage = Number(inputs[8], "VoutHighV"),
            GainK = Number(inputs[9], "GainK"),
            HalfWavePolarity = EnumValue<HalfWavePolarity>(inputs[10], "HalfWavePolarity"),
            OutputPolarity = EnumValue<RectifierOutputPolarity>(inputs[11], "OutputPolarity"),
            MinimumInputVoltage = Number(inputs[12], "VinMinV"),
            MaximumInputVoltage = Number(inputs[13], "VinMaxV"),
            MaximumExpectedFrequencyHz = Number(inputs[14], "MaxFrequencyHz"),
            ReferenceVoltage = Number(inputs[15], "ReferenceVoltageV"),
            LogScaleVoltage = Number(inputs[16], "LogScaleV"),
            IntegrationConstantPerSecond = Number(inputs[17], "KiPerSecond"),
            DifferentiationConstantSecond = Number(inputs[18], "KdSecond"),
            DifferentiationUsefulUpperFrequencyHz = Number(inputs[20], "UpperCornerHz"),
            LowerCornerHz = Number(inputs[19], "LowerCornerHz"),
            UpperCornerHz = Number(inputs[20], "UpperCornerHz"),
            SupplyVccVoltage = Number(inputs[21], "VccV"),
            SupplyVeeVoltage = Number(inputs[22], "VeeV"),
            MaximumFunctionErrorPercent = Number(inputs[23], "MaxErrorPercent"),
            Components = new ComponentSelectionConstraints
            {
                ResistorSeries = EnumValue<AnalogStandardSeries>(inputs[24], "RSeries"),
                ResistorMinimumOhm = Number(inputs[25], "RMinOhm"),
                ResistorMaximumOhm = Number(inputs[26], "RMaxOhm"),
                CapacitorSeries = EnumValue<AnalogStandardSeries>(inputs[27], "CSeries"),
                CapacitorMinimumFarad = Number(inputs[28], "CMinF"),
                CapacitorMaximumFarad = Number(inputs[29], "CMaxF"),
                OptimizeStandardValues = Boolean(inputs[30], "Optimize"),
                BiasCompensation = Boolean(inputs[31], "BiasCompensation")
            },
            OpAmp = new OpAmpPracticalConstraints
            {
                UseIdealOpAmp = Boolean(inputs[32], "IdealOpAmp"),
                GainBandwidthHz = Number(inputs[33], "GBWHz"),
                SlewRateVoltPerSecond = Number(inputs[34], "SlewVPerSecond")
            },
            Junction = new JunctionModelParameters
            {
                Behavior = EnumValue<DiodeBehaviorModel>(inputs[35], "JunctionModel"),
                SaturationCurrentAmpere = Number(inputs[36], "SaturationCurrentA"),
                EmissionCoefficient = Number(inputs[37], "EmissionCoefficient"),
                TemperatureCelsius = Number(inputs[38], "TemperatureC")
            },
            MaximumJunctionCurrentAmpere = Number(inputs[39], "MaxJunctionCurrentA")
        };
        return new ParsedPrecisionAnalogTestRow(inputs, delimiter, request);
    }

    public string Complete(ParsedPrecisionAnalogTestRow parsed, AnalogComputeDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(result);
        var metrics = result.VerificationMetrics;
        var failed = metrics.Where(metric => !metric.Pass).ToArray();
        var ratios = metrics.Where(metric => double.IsFinite(metric.Tolerance) && metric.Tolerance > 0)
            .Select(metric => Math.Abs(metric.Error) / metric.Tolerance).Where(double.IsFinite).ToArray();
        var resultFields = new[]
        {
            result.Success ? "1" : "0",
            result.Capability.Level.ToString(),
            result.Capability.ReasonCode.ToString(),
            result.FailureStage.ToString(),
            Clean(result.TopologyName),
            Clean(result.TargetRelation),
            result.Components.Count.ToString(CultureInfo.InvariantCulture),
            metrics.Count.ToString(CultureInfo.InvariantCulture),
            failed.Length.ToString(CultureInfo.InvariantCulture),
            ratios.Length == 0 ? "无" : ratios.Max().ToString("G9", CultureInfo.InvariantCulture),
            Clean(result.Analysis?.AnalysisType ?? "无"),
            (result.Analysis?.Points.Count ?? 0).ToString(CultureInfo.InvariantCulture),
            result.Warnings.Count.ToString(CultureInfo.InvariantCulture),
            Clean(result.Capability.Message),
            Join(failed.Select(metric => $"{metric.Name}:{metric.Actual:G6}/{metric.Target:G6}")),
            Join(result.Components.Select(component => $"{component.Reference}:{component.SelectedValue:G6}{component.Unit}")),
            Join(result.Warnings)
        };
        return string.Join(parsed.Delimiter, parsed.InputFields.Concat(resultFields));
    }

    private static IReadOnlyList<AnalogInputTerm> ParseInputs(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "无") return [];
        var result = new List<AnalogInputTerm>();
        foreach (var term in value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = term.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 5) throw new FormatException($"Input term '{term}' must be Name:Coefficient:Min:Max:Nominal.");
            result.Add(new AnalogInputTerm(parts[0], Number(parts[1], "input coefficient"), Number(parts[2], "input minimum"), Number(parts[3], "input maximum"), Number(parts[4], "input nominal")));
        }
        return result;
    }

    private static double Number(string value, string name)
    {
        if (string.Equals(value, "Infinity", StringComparison.OrdinalIgnoreCase) || value == "∞") return double.PositiveInfinity;
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
        return joined.Length == 0 ? "无" : joined;
    }

    private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Replace(',', '，').Trim();
}
