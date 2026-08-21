using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public sealed class ButterworthPrototypeDesigner : IFilterPrototypeDesigner
{
    public FilterApproximation Approximation => FilterApproximation.Butterworth;

    public FilterPrototype Design(int order, double passbandRippleDb)
    {
        if (order < 1)
            throw new ArgumentOutOfRangeException(nameof(order));
        var poles = Enumerable.Range(0, order)
            .Select(index => Complex.FromPolarCoordinates(1, Math.PI * (2 * index + order + 1) / (2 * order)))
            .ToArray();
        var g = new double[order + 2];
        g[0] = 1;
        for (var index = 1; index <= order; index++)
            g[index] = 2 * Math.Sin((2 * index - 1) * Math.PI / (2 * order));
        g[^1] = 1;
        return new FilterPrototype(Approximation, order, poles, g);
    }
}
