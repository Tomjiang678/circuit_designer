using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public interface IFilterPrototypeDesigner
{
    FilterApproximation Approximation { get; }
    FilterPrototype Design(int order, double passbandRippleDb);
}
