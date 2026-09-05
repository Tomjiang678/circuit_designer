using System.Globalization;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Services;

public sealed record ParsedFilterTestRow(string[] Fields, char Delimiter, FilterDesignRequest Request, bool IsComprehensive = false);

/// <summary>Maps one row from either manual-test sheet to a request and back to a completed row.</summary>
public sealed class FilterManualTestRowService
{
    public const int MaximumBatchRows = 300;
    public const int ColumnCount = 51;
    public const int ComprehensiveInputColumnCount = 35;
    public const int ComprehensiveColumnCount = 57;

    public ParsedFilterTestRow Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Paste one test-point row copied from Excel or the CSV file.", nameof(text));
        var lines = text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.FirstOrDefault(value => !value.StartsWith("测试点", StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("The pasted text contains a header but no test-point row.", nameof(text));
        var delimiter = line.Contains('\t') ? '\t' : ',';
        var fields = Split(line, delimiter);
        if (fields.Length == ComprehensiveInputColumnCount)
        {
            var expanded = new string[ComprehensiveColumnCount];
            Array.Copy(fields, expanded, fields.Length);
            Array.Fill(expanded, string.Empty, fields.Length, expanded.Length - fields.Length);
            return ParseComprehensive(expanded, delimiter);
        }
        if (fields.Length == ComprehensiveColumnCount)
            return ParseComprehensive(fields, delimiter);
        if (fields.Length != ColumnCount)
            throw new ArgumentException($"The row must contain {ComprehensiveInputColumnCount} input columns (combined TXT), {ComprehensiveColumnCount} complete columns, or {ColumnCount} legacy Passive columns; this row contains {fields.Length}.", nameof(text));

        var state = new FilterDesignState { Mode = FilterMode.Passive };
        var response = EnumValue<FilterResponseType>(fields[4], "Response");
        var implementation = EnumValue<FilterImplementationType>(fields[2], "Implementation");
        state.SelectResponseType(response);
        state.SelectImplementation(implementation);
        state.Approximation = EnumValue<FilterApproximation>(fields[5], "Approximation");
        state.OrderMode = fields[6].Trim() switch
        {
            var value when value.Equals("Automatic", StringComparison.OrdinalIgnoreCase) => FilterOrderMode.CalculateAutomatically,
            var value when value.Equals("Specified", StringComparison.OrdinalIgnoreCase) => FilterOrderMode.SpecifyOrder,
            _ => EnumValue<FilterOrderMode>(fields[6], "OrderMode")
        };
        state.SpecifiedOrder = Integer(fields[7], state.SpecifiedOrder);
        state.PassbandRippleDb = Number(fields[8], state.PassbandRippleDb);
        state.StopbandAttenuationDb = Number(fields[9], state.StopbandAttenuationDb);
        state.PassbandEdgeHz = Number(fields[10], state.PassbandEdgeHz);
        state.StopbandEdgeHz = Number(fields[11], state.StopbandEdgeHz);
        state.StopbandLowerHz = Number(fields[12], state.StopbandLowerHz);
        state.PassbandLowerHz = Number(fields[13], state.PassbandLowerHz);
        state.PassbandUpperHz = Number(fields[14], state.PassbandUpperHz);
        state.StopbandUpperHz = Number(fields[15], state.StopbandUpperHz);
        state.SourceResistanceOhm = Number(fields[16], state.SourceResistanceOhm);
        state.LoadResistanceOhm = Number(fields[17], state.SourceResistanceOhm > 0 ? state.SourceResistanceOhm : state.LoadResistanceOhm);
        state.LcTerminationMode = state.SourceResistanceOhm > 0 ? LcTerminationMode.DoublyTerminated : LcTerminationMode.IdealVoltageSource;
        if (!Missing(fields[18])) ApplyLcTermination(state, fields[18]);
        if (!Missing(fields[19])) state.LcLadderOrientation = EnumValue<LcLadderOrientation>(fields[19], "LC orientation");
        if (!Missing(fields[20])) state.PreferredResistanceOhm = Number(fields[20], state.PreferredResistanceOhm);
        if (!Missing(fields[21])) state.PreferredResistorMinOhm = Number(fields[21], state.PreferredResistorMinOhm);
        if (!Missing(fields[22])) state.PreferredResistorMaxOhm = Number(fields[22], state.PreferredResistorMaxOhm);
        if (!Missing(fields[23])) SetCapacitorMinimum(state, Number(fields[23], state.PreferredCapacitorMinF), implementation);
        if (!Missing(fields[24])) SetCapacitorMaximum(state, Number(fields[24], state.PreferredCapacitorMaxF), implementation);
        if (!Missing(fields[25])) state.InductorMinimumH = Number(fields[25], state.InductorMinimumH);
        if (!Missing(fields[26])) state.InductorMaximumH = Number(fields[26], state.InductorMaximumH);
        state.OptimizeStandardValues = Integer(fields[27], 0) != 0;
        if (!Missing(fields[28])) state.ResistorSeries = EnumValue<StandardComponentSeries>(fields[28], "R series");
        if (!Missing(fields[29])) state.CapacitorSeries = EnumValue<StandardComponentSeries>(fields[29], "C series");
        if (!Missing(fields[30])) state.InductorSeries = EnumValue<StandardComponentSeries>(fields[30], "L series");
        if (!Missing(fields[3])) state.PassiveRcTopology = EnumValue<PassiveRcTopology>(fields[3], "RC topology");
        return new(fields, delimiter, FilterDesignRequest.FromState(state));
    }

    public IReadOnlyList<ParsedFilterTestRow> ParseMany(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Paste one or more comma-separated test rows.", nameof(text));
        var rows = text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("测试点", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rows.Length == 0)
            throw new ArgumentException("The pasted text contains no test-point rows.", nameof(text));
        if (rows.Length > MaximumBatchRows)
            throw new ArgumentException($"At most {MaximumBatchRows} test rows can be run at once; {rows.Length} rows were pasted.", nameof(text));
        var parsed = new List<ParsedFilterTestRow>(rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            try { parsed.Add(Parse(rows[index])); }
            catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
            {
                throw new ArgumentException($"Input row {index + 1} is invalid: {exception.Message}", nameof(text), exception);
            }
        }
        return parsed;
    }

    public string Complete(ParsedFilterTestRow parsed, FilterDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(result);
        if (parsed.IsComprehensive) return CompleteComprehensive(parsed, result);
        var fields = parsed.Fields.ToArray();
        fields[32] = result.IsImplemented ? "1" : "0";
        fields[33] = result.IsFullySupported ? "1" : "0";
        fields[34] = result.Capability?.Level.ToString() ?? "无";
        fields[35] = result.Capability?.ReasonCode.ToString() ?? "无";
        fields[36] = F(result.DesignRequest?.LoadResistanceOhm ?? parsed.Request.LoadResistanceOhm);
        foreach (var edge in result.SpecificationCompliance?.Edges ?? [])
        {
            var column = edge.Label switch { "fp" => 37, "fs" => 38, "fs1" or "notch" => 39, "fp1" => 40, "fp2" => 41, "fs2" => 42, _ => -1 };
            if (column >= 0) fields[column] = F(edge.ActualMagnitudeDb);
        }
        if (result.PassiveAnalysis is { } passive && TryNumber(fields[31], out var frequency))
        {
            fields[43] = At(passive.AbsoluteVoltageTransfer, frequency);
            fields[44] = At(passive.FilterNodeVoltageTransfer, frequency);
            fields[45] = passive.TransducerGain == null ? "无" : At(passive.TransducerGain, frequency);
            fields[46] = At(passive.InsertionLossRelativeToThrough, frequency);
            fields[47] = "1";
        }
        else
        {
            for (var index = 43; index <= 47; index++) fields[index] = "无";
        }
        fields[48] = CanBuildReport(result) ? "1" : "0";
        fields[49] = HasCorrectPassiveBoundary(result, parsed.Request) ? "1" : "0";
        return Join(fields, parsed.Delimiter);
    }

    private static ParsedFilterTestRow ParseComprehensive(string[] fields, char delimiter)
    {
        var state = new FilterDesignState
        {
            Mode = EnumValue<FilterMode>(fields[2], "Mode"),
            ResponseType = EnumValue<FilterResponseType>(fields[3], "Response"),
            Approximation = EnumValue<FilterApproximation>(fields[4], "Approximation"),
            Implementation = EnumValue<FilterImplementationType>(fields[5], "Implementation"),
            SignalArchitecture = EnumValue<FilterSignalArchitecture>(fields[6], "Architecture"),
            OrderMode = ParseOrderMode(fields[7]),
            SpecifiedOrder = Integer(fields[8], 2),
            PassbandRippleDb = Number(fields[9], 1),
            StopbandAttenuationDb = Number(fields[10], 40),
            PassbandGain = Number(fields[11], 1),
            PassbandEdgeHz = Number(fields[12], 1_000),
            StopbandEdgeHz = Number(fields[13], 3_000),
            StopbandLowerHz = Number(fields[14], 500),
            PassbandLowerHz = Number(fields[15], 900),
            PassbandUpperHz = Number(fields[16], 1_100),
            StopbandUpperHz = Number(fields[17], 1_500),
            SourceResistanceOhm = Number(fields[18], 0),
            LoadResistanceOhm = Number(fields[19], 1_000_000),
            OptimizeStandardValues = Integer(fields[30], 0) != 0
        };
        state.LcTerminationMode = state.SourceResistanceOhm > 0 ? LcTerminationMode.DoublyTerminated : LcTerminationMode.IdealVoltageSource;
        if (!Missing(fields[20])) state.PassiveRcTopology = EnumValue<PassiveRcTopology>(fields[20], "RC topology");
        if (!Missing(fields[21])) ApplyLcTermination(state, fields[21]);
        if (!Missing(fields[22])) state.LcLadderOrientation = EnumValue<LcLadderOrientation>(fields[22], "LC orientation");
        if (!Missing(fields[23])) state.PreferredResistanceOhm = Number(fields[23], state.PreferredResistanceOhm);
        if (!Missing(fields[24])) state.PreferredResistorMinOhm = Number(fields[24], state.PreferredResistorMinOhm);
        if (!Missing(fields[25])) state.PreferredResistorMaxOhm = Number(fields[25], state.PreferredResistorMaxOhm);
        if (!Missing(fields[26])) SetCapacitorMinimum(state, Number(fields[26], state.PreferredCapacitorMinF), state.Implementation);
        if (!Missing(fields[27])) SetCapacitorMaximum(state, Number(fields[27], state.PreferredCapacitorMaxF), state.Implementation);
        if (!Missing(fields[28])) state.InductorMinimumH = Number(fields[28], state.InductorMinimumH);
        if (!Missing(fields[29])) state.InductorMaximumH = Number(fields[29], state.InductorMaximumH);
        if (!Missing(fields[31])) state.ResistorSeries = EnumValue<StandardComponentSeries>(fields[31], "R series");
        if (!Missing(fields[32])) state.CapacitorSeries = EnumValue<StandardComponentSeries>(fields[32], "C series");
        if (!Missing(fields[33])) state.InductorSeries = EnumValue<StandardComponentSeries>(fields[33], "L series");
        return new(fields, delimiter, FilterDesignRequest.FromState(state), true);
    }

    private string CompleteComprehensive(ParsedFilterTestRow parsed, FilterDesignResult result)
    {
        var fields = parsed.Fields.ToArray();
        fields[35] = result.IsImplemented ? "1" : "0";
        fields[36] = result.IsFullySupported ? "1" : "0";
        fields[37] = result.Status.ToString();
        fields[38] = result.Capability?.Level.ToString() ?? "无";
        fields[39] = result.Capability?.ReasonCode.ToString() ?? "无";
        fields[40] = result.RealizedOrder > 0 ? result.RealizedOrder.ToString(CultureInfo.InvariantCulture) : "无";
        fields[41] = result.DesignRequest?.Mode == FilterMode.Passive
            ? F(result.DesignRequest.LoadResistanceOhm)
            : "无";
        FillEdges(fields, result, 42, 43, 44, 45, 46, 47);
        if (result.PassiveAnalysis is { } passive && TryNumber(fields[34], out var frequency))
        {
            fields[48] = At(passive.AbsoluteVoltageTransfer, frequency);
            fields[49] = At(passive.FilterNodeVoltageTransfer, frequency);
            fields[50] = passive.TransducerGain == null ? "无" : At(passive.TransducerGain, frequency);
            fields[51] = At(passive.InsertionLossRelativeToThrough, frequency);
        }
        else for (var index = 48; index <= 51; index++) fields[index] = "无";
        fields[52] = result.AnalysisResult?.Points.Count > 1 ? "1" : "0";
        fields[53] = CanBuildReport(result) ? "1" : "0";
        fields[54] = HasCorrectCir(result, parsed.Request) ? "1" : "0";
        fields[55] = result.Warnings.Count.ToString(CultureInfo.InvariantCulture);
        fields[56] = DiagnosticDetail(result);
        return Join(fields, parsed.Delimiter);
    }

    private static void FillEdges(string[] fields, FilterDesignResult result, int fp, int fs, int fs1, int fp1, int fp2, int fs2)
    {
        fields[fp] = fields[fs] = fields[fs1] = fields[fp1] = fields[fp2] = fields[fs2] = "无";
        foreach (var edge in result.SpecificationCompliance?.Edges ?? [])
        {
            var column = edge.Label switch { "fp" => fp, "fs" => fs, "fs1" or "notch" => fs1, "fp1" => fp1, "fp2" => fp2, "fs2" => fs2, _ => -1 };
            if (column >= 0) fields[column] = F(edge.ActualMagnitudeDb);
        }
    }

    private static bool CanBuildReport(FilterDesignResult result)
    {
        if (!result.IsImplemented || result.Verification?.IsValid != true) return false;
        try { _ = new FilterDesignReportBuilder().Build(result); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private static bool HasCorrectPassiveBoundary(FilterDesignResult result, FilterDesignRequest input)
    {
        if (string.IsNullOrWhiteSpace(result.SpiceNetlist)) return false;
        var hasLoad = result.SpiceNetlist.Contains("R_LOAD VOUT 0", StringComparison.Ordinal);
        var hasSource = input.SourceResistanceOhm > 0
            ? result.SpiceNetlist.Contains("R_SOURCE VSRC VIN", StringComparison.Ordinal)
            : result.SpiceNetlist.Contains("V_INPUT VIN 0", StringComparison.Ordinal);
        return hasLoad && hasSource;
    }

    private static bool HasCorrectCir(FilterDesignResult result, FilterDesignRequest request)
    {
        if (string.IsNullOrWhiteSpace(result.SpiceNetlist)) return false;
        if (request.Mode == FilterMode.Active)
            return result.SpiceNetlist.Contains("V_INPUT VIN 0", StringComparison.Ordinal) && result.SpiceNetlist.Contains(".END", StringComparison.Ordinal);
        return HasCorrectPassiveBoundary(result, request);
    }

    private static string At(Core.Analysis.AcAnalysisResult analysis, double frequency)
    {
        var point = analysis.Points.MinBy(value => Math.Abs(Math.Log(value.FrequencyHz / frequency)));
        return point == null ? "无" : F(point.MagnitudeDb);
    }

    private static void SetCapacitorMinimum(FilterDesignState state, double value, FilterImplementationType implementation)
    { if (implementation == FilterImplementationType.Lc) state.PassiveLcCapacitorMinimumF = value; else state.PreferredCapacitorMinF = value; }
    private static void SetCapacitorMaximum(FilterDesignState state, double value, FilterImplementationType implementation)
    { if (implementation == FilterImplementationType.Lc) state.PassiveLcCapacitorMaximumF = value; else state.PreferredCapacitorMaxF = value; }

    private static T EnumValue<T>(string text, string field) where T : struct, Enum =>
        Enum.TryParse<T>(text.Trim(), true, out var value) ? value : throw new ArgumentException($"Invalid {field}: {text}");
    private static void ApplyLcTermination(FilterDesignState state, string text)
    {
        var normalized = text.Trim().Replace(" ", string.Empty).Replace("-", string.Empty);
        if (Enum.TryParse<LcTerminationPolicy>(normalized, true, out var policy))
        {
            state.LcTerminationPolicy = policy;
            return;
        }
        if (Enum.TryParse<LcTerminationMode>(normalized, true, out var mode))
        {
            state.LcTerminationMode = mode;
            return;
        }
        throw new ArgumentException($"Invalid LC termination value: {text}. Use IdealVoltageSource, DoublyTerminated, MatchedUserTermination, PrototypeNativeTermination, CustomIndependentTermination, or 无.");
    }
    private static int Integer(string text, int fallback) => Missing(text) ? fallback : int.Parse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static double Number(string text, double fallback) => Missing(text) ? fallback : double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
    private static bool TryNumber(string text, out double value) => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;
    private static bool Missing(string text) => string.IsNullOrWhiteSpace(text) || text.Trim() is "无" or "自动推导";
    private static FilterOrderMode ParseOrderMode(string text) => text.Trim() switch
    {
        var value when value.Equals("Automatic", StringComparison.OrdinalIgnoreCase) => FilterOrderMode.CalculateAutomatically,
        var value when value.Equals("Specified", StringComparison.OrdinalIgnoreCase) => FilterOrderMode.SpecifyOrder,
        _ => EnumValue<FilterOrderMode>(text, "OrderMode")
    };
    private static string F(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    private static string DiagnosticDetail(FilterDesignResult result)
    {
        var stage = result.StageDiagnostics;
        var prefix = stage == null ? string.Empty :
            $"TheoryCircuit={TriState(stage.TheoreticalCircuitValid)}; TheoryMeets={TriState(stage.TheoreticalMeetsSpecifications)}; " +
            $"SelectedCircuit={(stage.SelectedCircuitValid ? 1 : 0)}; SelectedMeets={(stage.SelectedMeetsSpecifications ? 1 : 0)}; FailureStage={stage.FailureStage}; ";
        var capability = result.Capability;
        var detail = capability == null ? result.Message : capability.Message +
            (string.IsNullOrWhiteSpace(capability.SuggestedAlternative) ? string.Empty : " Alternative: " + capability.SuggestedAlternative);
        return OneLine(prefix + detail);
    }

    private static string TriState(bool? value) => value switch { true => "1", false => "0", null => "N/A" };

    private static string[] Split(string line, char delimiter)
    {
        var values = new List<string>(); var current = new System.Text.StringBuilder(); var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"') { current.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (character == delimiter && !quoted) { values.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(character);
        }
        values.Add(current.ToString().Trim());
        return values.ToArray();
    }

    private static string Join(IEnumerable<string> fields, char delimiter) => string.Join(delimiter,
        fields.Select(value => value.Contains(delimiter) || value.Contains('"')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value));
}
