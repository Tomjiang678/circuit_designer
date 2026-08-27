namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public enum PrototypeNormalizationKind
{
    Minus3DbMagnitude,
    PassbandRipple,
    UnitCutoff
}

public sealed record PrototypeNormalizationInfo(
    PrototypeNormalizationKind Kind,
    string Name,
    string Definition,
    string OrderDeterminationMethod,
    string ReferenceSource);
