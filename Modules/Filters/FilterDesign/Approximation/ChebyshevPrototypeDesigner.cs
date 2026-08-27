using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public sealed class ChebyshevPrototypeDesigner : IFilterPrototypeDesigner
{
    public FilterApproximation Approximation => FilterApproximation.ChebyshevTypeI;

    public FilterPrototype Design(int order, double passbandRippleDb)
    {
        if (order < 1)
            throw new ArgumentOutOfRangeException(nameof(order));
        if (passbandRippleDb <= 0)
            throw new ArgumentOutOfRangeException(nameof(passbandRippleDb));
        var epsilon = Math.Sqrt(Math.Pow(10, passbandRippleDb / 10) - 1);
        var mu = Asinh(1 / epsilon) / order;
        var poles = new Complex[order];
        for (var index = 1; index <= order; index++)
        {
            var theta = Math.PI * (2 * index - 1) / (2 * order);
            poles[index - 1] = new Complex(
                -Math.Sinh(mu) * Math.Sin(theta),
                Math.Cosh(mu) * Math.Cos(theta));
        }
        var ladder = CalculateLadderValues(order, epsilon);
        return new FilterPrototype(Approximation, order, poles, ladder,
            Normalization: new PrototypeNormalizationInfo(
                PrototypeNormalizationKind.PassbandRipple,
                "Chebyshev-I passband-ripple prototype",
                "The normalized passband edge is Ω=1 and ripple is set by epsilon=sqrt(10^(Rp/10)-1).",
                "Closed-form Chebyshev-I order equation",
                "Classical Chebyshev Type-I all-pole prototype"),
            Description: "Equiripple passband magnitude with monotonic stopband attenuation.",
            Parameters: new Dictionary<string, double> { ["Epsilon"] = epsilon },
            LcLadderPrototype: ClassicLcPrototypeFactory.Create(Approximation, order, ladder,
                $"{passbandRippleDb:G6} dB equal-ripple insertion-loss ladder", "Closed-form Chebyshev-I g-values"));
    }

    private static IReadOnlyList<double> CalculateLadderValues(int order, double epsilon)
    {
        var alpha = Asinh(1 / epsilon) / order;
        var gamma = Math.Sinh(alpha);
        var a = new double[order + 1];
        var b = new double[order + 1];
        var g = new double[order + 2];
        g[0] = 1;
        for (var index = 1; index <= order; index++)
            a[index] = Math.Sin((2 * index - 1) * Math.PI / (2 * order));
        for (var index = 1; index < order; index++)
            b[index] = gamma * gamma + Math.Pow(Math.Sin(index * Math.PI / order), 2);
        g[1] = 2 * a[1] / gamma;
        for (var index = 2; index <= order; index++)
            g[index] = 4 * a[index - 1] * a[index] / (b[index - 1] * g[index - 1]);
        var beta = 2 * Asinh(1 / epsilon);
        g[^1] = order % 2 == 1 ? 1 : Math.Pow(Coth(beta / 4), 2);
        return g;
    }

    private static double Asinh(double value) => Math.Log(value + Math.Sqrt(value * value + 1));
    private static double Coth(double value) => Math.Cosh(value) / Math.Sinh(value);
}
