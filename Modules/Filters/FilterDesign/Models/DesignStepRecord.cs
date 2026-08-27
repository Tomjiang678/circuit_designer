namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

public sealed record DesignStepRecord(
    int Sequence,
    string Category,
    string Title,
    string Explanation,
    IReadOnlyDictionary<string, string> Values);
