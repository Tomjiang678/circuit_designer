using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

public sealed record FilterPlacementHint(
    string ComponentReference,
    int SectionIndex,
    string SemanticRole,
    int Column,
    int Row);

public sealed record FilterCircuitBuildResult(
    CircuitGraph CircuitGraph,
    IReadOnlyList<FilterPlacementHint> PlacementHints,
    IReadOnlyList<SynthesizedComponentValue>? NetworkComponentValues = null);
