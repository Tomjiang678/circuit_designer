using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;

public sealed record NormalizedFilterSpecification(
    FilterResponseType ResponseType,
    FilterApproximation Approximation,
    double PassbandAngularFrequency,
    double StopbandAngularFrequency,
    double StopbandRatio,
    double PassbandRippleDb,
    double StopbandAttenuationDb,
    int RequiredOrder,
    int ImplementedOrder,
    double CutoffAngularFrequency,
    bool MeetsOrderRequirement,
    string? OrderWarning,
    double CenterAngularFrequency = 0,
    double BandwidthAngularFrequency = 0,
    double SpecificationBandwidthAngularFrequency = 0);
