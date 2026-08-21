using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;

public sealed class BesselOrderCalculator : IFilterOrderCalculator
{
    public FilterApproximation Approximation => FilterApproximation.Bessel;

    public int CalculateMinimumOrder(double rp, double attenuation, double ratio)
    {
        if (rp <= 0 || attenuation <= rp || ratio <= 1) throw new ArgumentException("Bessel order calculation requires Rp > 0, As > Rp and stopband ratio > 1.");
        var lastAttenuation = 0d;
        for (var order = 1; order <= BesselPrototypeMath.MaximumOrder; order++)
        {
            var poles = BesselPrototypeMath.CreateMinus3DbPoles(order);
            var passbandPoint = BesselPrototypeMath.FindFrequencyForAttenuation(poles, rp);
            lastAttenuation = BesselPrototypeMath.AttenuationDb(poles, passbandPoint * ratio);
            if (lastAttenuation >= attenuation) return order;
        }
        throw new NotSupportedException($"The requested Bessel specification requires an order above {BesselPrototypeMath.MaximumOrder} (order-{BesselPrototypeMath.MaximumOrder} gives {lastAttenuation:0.###} dB at fs).");
    }

    public double CalculateCutoffAngularFrequency(double wp, double rp, int order)
    {
        var poles = BesselPrototypeMath.CreateMinus3DbPoles(order);
        return wp / BesselPrototypeMath.FindFrequencyForAttenuation(poles, rp);
    }
}
