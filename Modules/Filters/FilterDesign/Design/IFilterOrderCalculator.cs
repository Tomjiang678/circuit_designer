using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;

public interface IFilterOrderCalculator
{
    FilterApproximation Approximation { get; }
    int CalculateMinimumOrder(double passbandRippleDb, double stopbandAttenuationDb, double stopbandRatio);
    double CalculateCutoffAngularFrequency(double passbandAngularFrequency, double passbandRippleDb, int order);
}
