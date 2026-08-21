using System.Numerics;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

internal static class BesselPrototypeMath
{
    public const int MaximumOrder = 10;
    private static readonly IReadOnlyDictionary<int, Lazy<IReadOnlyList<Complex>>> PoleCache =
        Enumerable.Range(1, MaximumOrder).ToDictionary(order => order, order => new Lazy<IReadOnlyList<Complex>>(() => CalculateMinus3DbPoles(order), true));

    public static IReadOnlyList<Complex> CreateMinus3DbPoles(int order)
    {
        if (order is < 1 or > MaximumOrder) throw new ArgumentOutOfRangeException(nameof(order), $"Bessel order must be between 1 and {MaximumOrder}.");
        return PoleCache[order].Value;
    }

    private static IReadOnlyList<Complex> CalculateMinus3DbPoles(int order)
    {
        var coefficients = CreateReverseBesselCoefficients(order);
        var roots = FindRoots(coefficients, order);
        var cutoff = FindFrequency(coefficients, Math.Sqrt(0.5));
        return roots.Select(root => root / cutoff).OrderBy(root => root.Imaginary).ToArray();
    }

    public static double FindFrequencyForAttenuation(IReadOnlyList<Complex> poles, double attenuationDb)
    {
        if (attenuationDb <= 0) throw new ArgumentOutOfRangeException(nameof(attenuationDb));
        var target = Math.Pow(10, -attenuationDb / 20);
        double low = 0, high = 1;
        while (Magnitude(poles, high) > target) high *= 2;
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var middle = (low + high) / 2;
            if (Magnitude(poles, middle) > target) low = middle; else high = middle;
        }
        return (low + high) / 2;
    }

    public static double AttenuationDb(IReadOnlyList<Complex> poles, double normalizedFrequency) =>
        -20 * Math.Log10(Magnitude(poles, normalizedFrequency));

    private static double Magnitude(IReadOnlyList<Complex> poles, double frequency)
    {
        var numerator = poles.Aggregate(1d, (value, pole) => value * pole.Magnitude);
        var denominator = poles.Aggregate(1d, (value, pole) => value * (new Complex(0, frequency) - pole).Magnitude);
        return numerator / denominator;
    }

    private static double[] CreateReverseBesselCoefficients(int order)
    {
        var coefficients = new double[order + 1];
        for (var power = 0; power <= order; power++)
            coefficients[power] = Factorial(2 * order - power) / (Math.Pow(2, order - power) * Factorial(power) * Factorial(order - power));
        return coefficients;
    }

    private static IReadOnlyList<Complex> FindRoots(IReadOnlyList<double> coefficients, int order)
    {
        var original = coefficients.Select(value => new Complex(value, 0)).ToArray();
        var deflated = original.ToArray();
        var roots = new Complex[order];
        for (var degree = order; degree >= 1; degree--)
        {
            var root = FindLaguerreRoot(deflated, degree);
            if (Math.Abs(root.Imaginary) < 2e-12 * Math.Max(1, Math.Abs(root.Real))) root = new Complex(root.Real, 0);
            roots[degree - 1] = root;
            var next = new Complex[degree];
            next[degree - 1] = deflated[degree];
            for (var index = degree - 2; index >= 0; index--) next[index] = deflated[index + 1] + root * next[index + 1];
            deflated = next;
        }
        for (var index = 0; index < roots.Length; index++)
        {
            try { roots[index] = Laguerre(original, order, roots[index]); }
            catch (InvalidOperationException) { /* Deflated root is already converged; polishing is optional. */ }
        }
        if (roots.Any(root => root.Real >= 0 || !double.IsFinite(root.Real) || !double.IsFinite(root.Imaginary)))
            throw new InvalidOperationException("Bessel root solver produced invalid poles.");
        return roots;
    }

    private static Complex FindLaguerreRoot(IReadOnlyList<Complex> coefficients, int degree)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var initial = attempt == 0 ? Complex.Zero : Complex.FromPolarCoordinates(degree * (0.25 + attempt * 0.08), 2 * Math.PI * attempt / 11);
            try { return Laguerre(coefficients, degree, initial); }
            catch (InvalidOperationException) when (attempt < 11) { }
        }
        throw new InvalidOperationException($"Complex Laguerre solver did not converge for degree {degree}.");
    }

    private static Complex Laguerre(IReadOnlyList<Complex> coefficients, int degree, Complex initial)
    {
        var value = initial;
        for (var iteration = 1; iteration <= 500; iteration++)
        {
            var polynomial = coefficients[degree]; var first = Complex.Zero; var second = Complex.Zero; var error = polynomial.Magnitude;
            for (var index = degree - 1; index >= 0; index--)
            {
                second = value * second + first;
                first = value * first + polynomial;
                polynomial = value * polynomial + coefficients[index];
                error = polynomial.Magnitude + value.Magnitude * error;
            }
            if (polynomial.Magnitude <= 2e-14 * error) return value;
            var g = first / polynomial;
            var h = g * g - 2 * second / polynomial;
            var squareRoot = Complex.Sqrt((degree - 1) * (degree * h - g * g));
            var plus = g + squareRoot; var minus = g - squareRoot;
            var denominator = plus.Magnitude > minus.Magnitude ? plus : minus;
            var delta = denominator.Magnitude > 1e-30
                ? degree / denominator
                : Complex.FromPolarCoordinates(1 + value.Magnitude, iteration);
            var next = value - delta;
            if ((next - value).Magnitude <= 2e-14 * Math.Max(1, next.Magnitude)) return next;
            value = next;
        }
        throw new InvalidOperationException($"Complex Laguerre solver did not converge for degree {degree}.");
    }

    private static Complex Evaluate(IReadOnlyList<double> coefficients, Complex value)
    {
        var result = Complex.Zero;
        for (var index = coefficients.Count - 1; index >= 0; index--) result = result * value + coefficients[index];
        return result;
    }

    private static double FindFrequency(IReadOnlyList<double> coefficients, double targetMagnitude)
    {
        var dc = coefficients[0]; double low = 0, high = 1;
        double MagnitudeAt(double frequency) => dc / Evaluate(coefficients, new Complex(0, frequency)).Magnitude;
        while (MagnitudeAt(high) > targetMagnitude) high *= 2;
        for (var iteration = 0; iteration < 100; iteration++) { var middle = (low + high) / 2; if (MagnitudeAt(middle) > targetMagnitude) low = middle; else high = middle; }
        return (low + high) / 2;
    }

    private static double Factorial(int value) { var result = 1d; for (var factor = 2; factor <= value; factor++) result *= factor; return result; }
}
