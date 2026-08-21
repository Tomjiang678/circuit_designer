using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public sealed class BesselPrototypeDesigner : IFilterPrototypeDesigner
{
    public FilterApproximation Approximation => FilterApproximation.Bessel;
    public FilterPrototype Design(int order, double passbandRippleDb) =>
        new(Approximation, order, BesselPrototypeMath.CreateMinus3DbPoles(order), Array.Empty<double>());
}
