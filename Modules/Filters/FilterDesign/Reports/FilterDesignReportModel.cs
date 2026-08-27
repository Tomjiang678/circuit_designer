using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;

public sealed record ReportField(string Name, string Value, string Unit = "");
public sealed record ReportPole(int Index, double Real, double Imaginary, bool IsZero);
public sealed record ReportSection(
    int Index, string Order, string Response, double TargetF0Hz, double TargetQ,
    double TargetGain, double IntrinsicGain, double ActualGain, string Polarity,
    IReadOnlyList<double> Numerator, IReadOnlyList<double> Denominator,
    double FrequencyError, double QError, double GainError);
public sealed record ReportComponent(
    int SectionIndex, string Topology, string Role, string ComponentType,
    double TheoreticalValue, double SelectedValue, string Unit,
    string Series, double TolerancePercent, double RelativeError);
public sealed record ReportVerificationRow(
    string Metric, string Target, string Actual, string Error, bool Passed);
public sealed record ReportCircuitSummary(
    int Resistors, int Capacitors, int Inductors, int OpAmps, int Nets, int Sections,
    string TopologyChain);

public sealed record FilterDesignSnapshot(
    string SchemaVersion,
    DateTimeOffset DesignedAtUtc,
    IReadOnlyDictionary<string, object> Request,
    IReadOnlyList<ReportComponent> Components,
    IReadOnlyList<ReportPole> PrototypePoles,
    IReadOnlyDictionary<string, object> ResultSummary);

public sealed record FilterDesignReportModel(
    DateTimeOffset GeneratedAtUtc,
    string SoftwareVersion,
    IReadOnlyList<ReportField> DesignSummary,
    IReadOnlyList<ReportField> InputSpecifications,
    IReadOnlyList<ReportField> ApproximationDetails,
    IReadOnlyList<ReportPole> PrototypePolesAndZeros,
    IReadOnlyList<ReportSection> Sections,
    IReadOnlyList<ReportComponent> Components,
    IReadOnlyList<ReportField> GainDistribution,
    IReadOnlyList<ReportVerificationRow> Verification,
    IReadOnlyList<string> Warnings,
    ReportCircuitSummary CircuitSummary,
    IReadOnlyList<DesignStepRecord> CalculationTrace,
    FilterDesignSnapshot Snapshot);
