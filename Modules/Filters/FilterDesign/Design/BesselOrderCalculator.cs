using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;

public sealed class BesselOrderCalculator : IFilterOrderCalculator
{
    private readonly BesselPrototypeDesigner designer = new();
    private readonly NumericalPrototypeOrderSearch search = new();
    public FilterApproximation Approximation => FilterApproximation.Bessel;

    public int CalculateMinimumOrder(double rp, double attenuation, double ratio)
    {
        if (rp <= 0 || attenuation <= rp || ratio <= 1) throw new ArgumentException("Bessel order calculation requires Rp > 0, As > Rp and stopband ratio > 1.");
        var order = search.FindMinimumOrder(rp, attenuation, ratio,
            Enumerable.Range(1, BesselPrototypeMath.MaximumOrder), value => designer.Design(value, rp), out var lastAttenuation);
        if (order > 0) return order;
        throw new NotSupportedException($"The requested Bessel specification requires an order above {BesselPrototypeMath.MaximumOrder} (order-{BesselPrototypeMath.MaximumOrder} gives {lastAttenuation:0.###} dB at fs).");
    }

    public double CalculateCutoffAngularFrequency(double wp, double rp, int order)
    {
        return search.CalculateCutoffAngularFrequency(wp, rp, designer.Design(order, rp));
    }
}
