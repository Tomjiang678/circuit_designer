using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public static class FilterApproximationCapabilities
{
    public static bool HasVerifiedActivePrototype(FilterApproximation approximation) => approximation switch
    {
        FilterApproximation.Butterworth or FilterApproximation.ChebyshevTypeI or FilterApproximation.Bessel or
        FilterApproximation.GaussianTo6Db or FilterApproximation.GaussianTo12Db or
        FilterApproximation.LinearPhase005Degree or FilterApproximation.LinearPhase05Degree => true,
        _ => false
    };

    public static bool HasDoublyTerminatedLadder(FilterApproximation approximation) =>
        approximation is FilterApproximation.Butterworth or FilterApproximation.ChebyshevTypeI or FilterApproximation.Bessel or
        FilterApproximation.GaussianTo6Db or FilterApproximation.GaussianTo12Db or
        FilterApproximation.LinearPhase005Degree or FilterApproximation.LinearPhase05Degree;
}
