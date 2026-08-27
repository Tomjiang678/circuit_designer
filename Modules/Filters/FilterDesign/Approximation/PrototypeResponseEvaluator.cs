using System.Numerics;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public static class PrototypeResponseEvaluator
{
    public static Complex Evaluate(FilterPrototype prototype, double omega)
    {
        if (omega < 0 || !double.IsFinite(omega)) throw new ArgumentOutOfRangeException(nameof(omega));
        var s = new Complex(0, omega);
        var numerator = new Complex(prototype.NumeratorGain, 0);
        foreach (var zero in prototype.Zeros) numerator *= s - zero;
        var denominator = Complex.One;
        foreach (var pole in prototype.Poles) denominator *= s - pole;
        return numerator / denominator;
    }

    public static double AttenuationDb(FilterPrototype prototype, double omega) =>
        -20 * Math.Log10(Math.Max(Evaluate(prototype, omega).Magnitude, 1e-300));

    public static double FindFrequencyForAttenuation(FilterPrototype prototype, double attenuationDb)
    {
        if (attenuationDb <= 0) throw new ArgumentOutOfRangeException(nameof(attenuationDb));
        double low = 0, high = 1;
        while (AttenuationDb(prototype, high) < attenuationDb && high < 1e12) high *= 2;
        if (high >= 1e12 && AttenuationDb(prototype, high) < attenuationDb)
            throw new InvalidOperationException("Prototype attenuation target could not be bracketed.");
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var middle = (low + high) / 2;
            if (AttenuationDb(prototype, middle) < attenuationDb) low = middle; else high = middle;
        }
        return (low + high) / 2;
    }

    public static double GroupDelay(FilterPrototype prototype, double omega)
    {
        var step = Math.Max(1e-6, Math.Max(1, omega) * 1e-5);
        var lower = Math.Max(0, omega - step);
        var upper = omega + step;
        var first = Evaluate(prototype, lower).Phase;
        var second = Evaluate(prototype, upper).Phase;
        var difference = second - first;
        while (difference > Math.PI) difference -= 2 * Math.PI;
        while (difference < -Math.PI) difference += 2 * Math.PI;
        return -difference / (upper - lower);
    }
}
