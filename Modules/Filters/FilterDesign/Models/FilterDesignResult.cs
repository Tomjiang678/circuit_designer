using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Core.Analysis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

public enum FilterDesignStatus
{
    Success,
    UnsupportedDesignCombination,
    ValidationFailure,
    SynthesisFailure
}

public sealed class FilterDesignResult
{
    public FilterDesignStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsImplemented => Status == FilterDesignStatus.Success;
    public bool IsFullySupported => Status == FilterDesignStatus.Success;
    public int RequiredOrder { get; init; }
    public int PrototypeOrder { get; init; }
    public int RealizedOrder { get; init; }
    public bool MeetsRequestedOrder { get; init; }
    public NormalizedFilterSpecification? NormalizedSpecification { get; init; }
    public IReadOnlyList<Complex> PrototypePoles { get; init; } = Array.Empty<Complex>();
    public IReadOnlyList<double> PrototypeElementValues { get; init; } = Array.Empty<double>();
    public IReadOnlyList<SynthesizedSection> Sections { get; init; } = Array.Empty<SynthesizedSection>();
    public IReadOnlyList<SynthesizedComponentValue> AdditionalNetworkComponents { get; init; } = Array.Empty<SynthesizedComponentValue>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public CircuitGraph? CircuitGraph { get; init; }
    public SchematicDocument? SchematicDocument { get; init; }
    public FilterDesignRequest? DesignRequest { get; init; }
    public AcAnalysisResult? AnalysisResult { get; init; }
    public string SpiceNetlist { get; set; } = string.Empty;
}
