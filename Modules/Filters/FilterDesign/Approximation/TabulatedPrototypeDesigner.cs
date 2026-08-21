using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public sealed class TabulatedPrototypeDesigner(FilterApproximation approximation) : IFilterPrototypeDesigner
{
    public FilterApproximation Approximation { get; } = approximation;
    public FilterPrototype Design(int order, double passbandRippleDb) =>
        new(Approximation, order, TabulatedPrototypeCatalog.GetPoles(Approximation, order), Array.Empty<double>());
}
