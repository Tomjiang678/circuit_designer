using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Core.Analysis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Gain;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

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
    public FilterCapabilityEvaluation? Capability { get; init; }
    public bool IsImplemented => Status == FilterDesignStatus.Success;
    public bool IsFullySupported => Status == FilterDesignStatus.Success &&
                                    Capability?.IsSupported == true && MeetsSpecifications;
    public int RequiredOrder { get; init; }
    public int PrototypeOrder { get; init; }
    public int RealizedOrder { get; init; }
    public bool MeetsRequestedOrder { get; init; }
    public bool MeetsSpecifications { get; init; }
    public NormalizedFilterSpecification? NormalizedSpecification { get; init; }
    public IReadOnlyList<Complex> PrototypePoles { get; init; } = Array.Empty<Complex>();
    public IReadOnlyList<Complex> PrototypeZeros { get; init; } = Array.Empty<Complex>();
    public double PrototypeGain { get; init; } = 1;
    public PrototypeNormalizationInfo? PrototypeNormalization { get; init; }
    public string ApproximationDescription { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, double> ApproximationParameters { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<double> PrototypeElementValues { get; init; } = Array.Empty<double>();
    public NormalizedLcLadderPrototype? LcLadderPrototype { get; init; }
    public IReadOnlyList<SynthesizedSection> Sections { get; init; } = Array.Empty<SynthesizedSection>();
    public IReadOnlyList<SynthesizedComponentValue> AdditionalNetworkComponents { get; init; } = Array.Empty<SynthesizedComponentValue>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public CircuitGraph? CircuitGraph { get; init; }
    public SchematicDocument? SchematicDocument { get; init; }
    public FilterDesignRequest? DesignRequest { get; init; }
    public AcAnalysisResult? AnalysisResult { get; init; }
    public FilterSpecificationCompliance? SpecificationCompliance { get; init; }
    public GainDistributionSummary? GainDistribution { get; init; }
    public FilterDesignVerificationReport? Verification { get; init; }
    public IReadOnlyList<DesignStepRecord> CalculationTrace { get; init; } = Array.Empty<DesignStepRecord>();
    public DateTimeOffset DesignedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string SpiceNetlist { get; set; } = string.Empty;
}
