namespace CircuitModuleDesigner.Core.Circuit.Spice;

/// <summary>A simulator-neutral text-netlist representation produced solely from CircuitGraph plus export metadata.</summary>
public sealed record SpiceNetlistDocument(
    string Title,
    IReadOnlyList<string> Comments,
    IReadOnlyList<string> ElementLines,
    IReadOnlyList<string> ModelAndLibraryLines,
    IReadOnlyList<string> AnalysisLines,
    IReadOnlyList<string> OutputLines);
