using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;

public sealed class TabulatedPrototypeOrderCalculator(FilterApproximation approximation) : IFilterOrderCalculator
{
    public FilterApproximation Approximation { get; } = approximation;

    public int CalculateMinimumOrder(double passbandRippleDb, double stopbandAttenuationDb, double stopbandRatio)
    {
        if (passbandRippleDb <= 0 || stopbandAttenuationDb <= passbandRippleDb || stopbandRatio <= 1) throw new ArgumentOutOfRangeException(nameof(passbandRippleDb));
        foreach (var order in TabulatedPrototypeCatalog.GetOrders(Approximation))
        {
            var poles = TabulatedPrototypeCatalog.GetPoles(Approximation, order); var passPoint = FindFrequencyForAttenuation(poles, passbandRippleDb);
            if (AttenuationDb(poles, passPoint * stopbandRatio) >= stopbandAttenuationDb) return order;
        }
        throw new NotSupportedException($"{Approximation} requires an order above the published order-10 table for these specifications.");
    }

    public double CalculateCutoffAngularFrequency(double passbandAngularFrequency, double passbandRippleDb, int order) =>
        passbandAngularFrequency / FindFrequencyForAttenuation(TabulatedPrototypeCatalog.GetPoles(Approximation, order), passbandRippleDb);

    public static double AttenuationDb(IReadOnlyList<Complex> poles, double angularFrequency)
    {
        Complex numerator = Complex.One, denominator = Complex.One; var s = new Complex(0, angularFrequency);
        foreach (var pole in poles) { numerator *= -pole; denominator *= s - pole; }
        return -20 * Math.Log10((numerator / denominator).Magnitude);
    }

    private static double FindFrequencyForAttenuation(IReadOnlyList<Complex> poles, double targetDb)
    {
        double low = 0, high = 1;
        while (AttenuationDb(poles, high) < targetDb && high < 1e6) high *= 2;
        for (var i = 0; i < 100; i++) { var mid = (low + high) / 2; if (AttenuationDb(poles, mid) < targetDb) low = mid; else high = mid; }
        return (low + high) / 2;
    }
}
