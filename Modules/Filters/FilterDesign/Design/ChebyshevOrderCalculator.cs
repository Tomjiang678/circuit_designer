using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;

public sealed class ChebyshevOrderCalculator : IFilterOrderCalculator
{
    public FilterApproximation Approximation => FilterApproximation.ChebyshevTypeI;

    public int CalculateMinimumOrder(double rp, double attenuation, double ratio)
    {
        if (rp <= 0 || attenuation <= rp || ratio <= 1)
            throw new ArgumentException("Chebyshev-I order calculation requires Rp > 0, As > Rp and stopband ratio > 1.");
        var epsilonSquared = Math.Pow(10, rp / 10) - 1;
        var stopTerm = Math.Pow(10, attenuation / 10) - 1;
        return Math.Max(1, (int)Math.Ceiling(Acosh(Math.Sqrt(stopTerm / epsilonSquared)) / Acosh(ratio)));
    }

    public double CalculateCutoffAngularFrequency(double wp, double rp, int order) => wp;

    private static double Acosh(double value) => Math.Log(value + Math.Sqrt(value * value - 1));
}
