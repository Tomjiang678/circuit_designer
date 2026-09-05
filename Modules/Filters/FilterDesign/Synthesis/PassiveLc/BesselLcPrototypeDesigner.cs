using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

public sealed record InsertionLossPolynomial(
    IReadOnlyList<double> HurwitzDenominator,
    double NumeratorGain,
    IReadOnlyList<double> ReflectionProduct);

public sealed class InsertionLossPolynomialBuilder
{
    public InsertionLossPolynomial Build(FilterPrototype transferPrototype)
    {
        var denominator = LcPolynomial.FromRoots(transferPrototype.Poles).ToArray();
        var gain = denominator[0];
        var reflected = denominator.Select((value, index) => index % 2 == 0 ? value : -value).ToArray();
        var product = LcPolynomial.Multiply(denominator, reflected);
        product[0] -= gain * gain;
        return new(denominator, gain, LcPolynomial.Trim(product));
    }
}

public sealed class HurwitzSpectralFactorizer
{
    public IReadOnlyList<double> Factor(IReadOnlyList<double> reflectionProduct, int order)
    {
        var polynomial = LcPolynomial.Trim(reflectionProduct.ToArray());
        if (polynomial.Length < 3 || Math.Abs(polynomial[0]) > 1e-7)
            throw new InvalidOperationException("The reflection polynomial does not contain the required DC transmission zero.");
        // P(s) has a double zero at the origin; the reflection factor F(s)
        // contains one of those zeros. Factoring the deflated polynomial avoids
        // the repeated-root instability of a general root finder.
        var deflated = polynomial.Skip(2).ToArray();
        var roots = LcPolynomial.Roots(deflated);
        var stable = roots.Where(root => root.Real < -1e-7).ToList();
        if (stable.Count != order - 1)
            throw new InvalidOperationException($"Stable reflection spectral factor requires {order - 1} LHP roots; found {stable.Count}.");
        var factor = LcPolynomial.FromRoots([Complex.Zero, .. stable]);
        var requiredLeading = Math.Sqrt(Math.Abs(polynomial[^1]));
        factor = LcPolynomial.Scale(factor, requiredLeading / Math.Abs(factor[^1]));
        var reconstructed = LcPolynomial.Multiply(factor,
            factor.Select((value, index) => index % 2 == 0 ? value : -value).ToArray());
        var error = LcPolynomial.MaximumRelativeCoefficientError(reconstructed, polynomial);
        if (error > 2e-6)
            throw new InvalidOperationException($"Reflection spectral-factor reconstruction error {error:G6} exceeds tolerance.");
        return factor;
    }
}

public sealed class PositiveRealLadderExtractor
{
    public NormalizedLcLadderPrototype Extract(FilterPrototype transferPrototype,
        IReadOnlyList<double> denominator, IReadOnlyList<double> reflectionFactor)
    {
        foreach (var sign in new[] { 1d, -1d })
        {
            var factor = LcPolynomial.Scale(reflectionFactor, sign);
            foreach (var admittanceFirst in new[] { false, true })
            {
                var numerator = admittanceFirst
                    ? LcPolynomial.Subtract(denominator, factor)
                    : LcPolynomial.Add(denominator, factor);
                var divisor = admittanceFirst
                    ? LcPolynomial.Add(denominator, factor)
                    : LcPolynomial.Subtract(denominator, factor);
                if (TryExtract(numerator, divisor, transferPrototype.Order, out var elements, out var records, out var termination))
                {
                    if (admittanceFirst) continue; // v1 ladder assembler starts with a series element.
                    return new NormalizedLcLadderPrototype(transferPrototype.Approximation, transferPrototype.Order,
                        transferPrototype.Normalization?.Name ?? "Bessel insertion-loss normalization",
                        1, termination, LcPrototypeConnection.Series, LcTerminationMode.DoublyTerminated,
                        elements, "Insertion-loss polynomial, Hurwitz spectral factor, positive-real driving point, Cauer-I continued fraction",
                        "Equal source/load terminated transducer-power response reconstructed from S21=K/E(s).",
                        new(true, false, double.PositiveInfinity, "Cauer extraction completed; response reconstruction pending."), records);
                }
            }
        }
        throw new InvalidOperationException("LcLadderExtractionProducedNegativeElement: no Cauer orientation produced an all-positive finite LC ladder.");
    }

    private static bool TryExtract(double[] numerator, double[] denominator, int order,
        out IReadOnlyList<LcPrototypeElement> elements, out IReadOnlyList<LcCauerExtractionRecord> records,
        out double termination)
    {
        const double tolerance = 2e-8;
        var extracted = new List<LcPrototypeElement>();
        var trace = new List<LcCauerExtractionRecord>();
        var num = LcPolynomial.Trim(numerator);
        var den = LcPolynomial.Trim(denominator);
        for (var step = 1; step <= order; step++)
        {
            if (num.Length != den.Length + 1) { elements = []; records = []; termination = 0; return false; }
            var value = num[^1] / den[^1];
            if (!double.IsFinite(value) || value <= tolerance) { elements = []; records = []; termination = 0; return false; }
            var remainder = LcPolynomial.Subtract(num, LcPolynomial.ShiftAndScale(den, value));
            remainder = LcPolynomial.Trim(remainder, tolerance);
            if (remainder.Any(coefficient => !double.IsFinite(coefficient))) { elements = []; records = []; termination = 0; return false; }
            var connection = step % 2 == 1 ? LcPrototypeConnection.Series : LcPrototypeConnection.Shunt;
            var kind = step % 2 == 1 ? LcPrototypeElementKind.NormalizedInductor : LcPrototypeElementKind.NormalizedCapacitor;
            extracted.Add(new(step, connection, kind, value));
            trace.Add(new(step, connection, kind, value, remainder, den));
            num = den;
            den = remainder;
        }
        if (den.Length != 1 || num.Length != 1 || Math.Abs(den[0]) <= tolerance)
        { elements = []; records = []; termination = 0; return false; }
        termination = num[0] / den[0];
        if (!double.IsFinite(termination) || termination <= tolerance)
        { elements = []; records = []; termination = 0; return false; }
        elements = extracted; records = trace; return true;
    }
}

public sealed class LcLadderPrototypeVerifier : ILcLadderPrototypeVerifier
{
    public LcLadderValidationMetrics Verify(NormalizedLcLadderPrototype prototype, FilterPrototype transferPrototype)
    {
        var positive = prototype.Elements.Count == prototype.Order && prototype.Elements.All(value =>
            value.GValue > 0 && double.IsFinite(value.GValue));
        if (!positive) return new(false, false, double.PositiveInfinity, "The ladder contains a non-positive or non-finite normalized element.");
        var maximum = 0d;
        foreach (var omega in Enumerable.Range(-40, 81).Select(index => Math.Pow(10, index / 20d)))
        {
            var actual = EvaluateLoadedTransfer(prototype, omega);
            var expected = PrototypeResponseEvaluator.Evaluate(transferPrototype, omega);
            maximum = Math.Max(maximum, (actual - expected).Magnitude / Math.Max(expected.Magnitude, 1e-10));
        }
        var matches = maximum <= 2e-5;
        return new(true, matches, maximum,
            matches ? $"Loaded ladder reconstructs the target transfer; max complex relative error {maximum:G6}."
                    : $"Loaded ladder reconstruction error {maximum:G6} exceeds tolerance.");
    }

    public static Complex EvaluateLoadedTransfer(NormalizedLcLadderPrototype prototype, double omega)
    {
        Complex a = 1, b = 0, c = 0, d = 1;
        foreach (var element in prototype.Elements.OrderBy(value => value.Index))
        {
            var immittance = new Complex(0, omega * element.GValue);
            if (element.Connection == LcPrototypeConnection.Series)
            {
                var nextB = a * immittance + b;
                var nextD = c * immittance + d;
                b = nextB; d = nextD;
            }
            else
            {
                var nextA = a + b * immittance;
                var nextC = c + d * immittance;
                a = nextA; c = nextC;
            }
        }
        var denominator = a + b / prototype.LoadTerminationGN1 +
                          prototype.SourceTerminationG0 * (c + d / prototype.LoadTerminationGN1);
        var output = 1 / denominator;
        var directThrough = prototype.LoadTerminationGN1 /
                            (prototype.SourceTerminationG0 + prototype.LoadTerminationGN1);
        return output / directThrough;
    }
}

public sealed class BesselLcPrototypeDesigner : ILcLadderPrototypeDesigner
{
    public FilterApproximation Approximation => FilterApproximation.Bessel;

    public NormalizedLcLadderPrototype Design(FilterPrototype transferPrototype, LcTerminationMode terminationMode)
    {
        if (terminationMode != LcTerminationMode.DoublyTerminated)
            throw new NotSupportedException("LcTerminationModeMismatch: Bessel LC v1 supports DoublyTerminated insertion-loss synthesis only.");
        if (transferPrototype.Order is < 1 or > 6)
            throw new NotSupportedException("Bessel LC v1 is numerically qualified for orders 1 through 6.");
        return InsertionLossLcSynthesis.Synthesize(transferPrototype);
    }
}

public sealed record LcPrototypeFeasibilityResult(
    FilterApproximation Approximation,
    int Order,
    LcTerminationMode TerminationMode,
    bool IsSupported,
    string ReasonCode,
    string Message,
    NormalizedLcLadderPrototype? Prototype);

public static class LcPrototypeFeasibilityAuditor
{
    public static LcPrototypeFeasibilityResult Evaluate(FilterPrototype transferPrototype,
        LcTerminationMode terminationMode = LcTerminationMode.DoublyTerminated)
    {
        if (terminationMode != LcTerminationMode.DoublyTerminated)
            return new(transferPrototype.Approximation, transferPrototype.Order, terminationMode, false,
                "LcTerminationModeMismatch", "Insertion-loss synthesis requires explicit source and load terminations.", null);
        try
        {
            var ladder = InsertionLossLcSynthesis.Synthesize(transferPrototype);
            return new(transferPrototype.Approximation, transferPrototype.Order, terminationMode, true,
                "Supported", ladder.ValidationMetrics.Summary, ladder);
        }
        catch (InvalidOperationException exception)
        {
            var reason = exception.Message.Contains("NegativeElement", StringComparison.Ordinal)
                ? "LcLadderExtractionProducedNegativeElement"
                : exception.Message.Contains("spectral", StringComparison.OrdinalIgnoreCase) || exception.Message.Contains("positive", StringComparison.OrdinalIgnoreCase)
                    ? "LcPrototypeNotPositiveReal"
                    : "LcFinalReconstructionFailed";
            return new(transferPrototype.Approximation, transferPrototype.Order, terminationMode, false,
                reason, exception.Message, null);
        }
    }
}

public static class LcPrototypeCapabilityRegistry
{
    private static readonly Dictionary<(FilterApproximation Approximation, int Order), LcPrototypeFeasibilityResult> Cache = new();
    private static readonly object Sync = new();

    public static LcPrototypeFeasibilityResult Evaluate(FilterApproximation approximation, int order)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue((approximation, order), out var cached)) return cached;
            if (approximation is FilterApproximation.GaussianTo6Db or FilterApproximation.GaussianTo12Db or
                FilterApproximation.LinearPhase005Degree or FilterApproximation.LinearPhase05Degree &&
                !TabulatedPrototypeCatalog.GetOrders(approximation).Contains(order))
            {
                var unavailable = new LcPrototypeFeasibilityResult(approximation, order, LcTerminationMode.DoublyTerminated,
                    false, "OrderUnavailable", $"{approximation} has no published transfer prototype for order {order}.", null);
                Cache[(approximation, order)] = unavailable;
                return unavailable;
            }
            FilterPrototype transfer = approximation switch
            {
                FilterApproximation.Bessel => new FilterPrototype(approximation, order,
                    BesselPrototypeMath.CreateMinus3DbPoles(order), []),
                FilterApproximation.GaussianTo6Db or FilterApproximation.GaussianTo12Db or
                FilterApproximation.LinearPhase005Degree or FilterApproximation.LinearPhase05Degree =>
                    new FilterPrototype(approximation, order, TabulatedPrototypeCatalog.GetPoles(approximation, order), []),
                _ => throw new NotSupportedException($"{approximation} uses a closed-form classic LC prototype rather than feasibility extraction.")
            };
            var result = LcPrototypeFeasibilityAuditor.Evaluate(transfer);
            Cache[(approximation, order)] = result;
            return result;
        }
    }
}

internal static class InsertionLossLcSynthesis
{
    public static NormalizedLcLadderPrototype Synthesize(FilterPrototype transferPrototype)
    {
        var insertionLoss = new InsertionLossPolynomialBuilder().Build(transferPrototype);
        var reflection = new HurwitzSpectralFactorizer().Factor(insertionLoss.ReflectionProduct, transferPrototype.Order);
        var extracted = new PositiveRealLadderExtractor().Extract(transferPrototype, insertionLoss.HurwitzDenominator, reflection);
        var metrics = new LcLadderPrototypeVerifier().Verify(extracted, transferPrototype);
        if (!metrics.ReconstructedResponseMatches)
            throw new InvalidOperationException("LC Cauer ladder failed reconstructed loaded-response verification: " + metrics.Summary);
        return extracted with { ValidationMetrics = metrics };
    }
}

internal static class LcPolynomial
{
    public static double[] FromRoots(IEnumerable<Complex> roots)
    {
        var coefficients = new[] { Complex.One };
        foreach (var root in roots)
        {
            var next = new Complex[coefficients.Length + 1];
            for (var index = 0; index < coefficients.Length; index++)
            {
                next[index] -= root * coefficients[index];
                next[index + 1] += coefficients[index];
            }
            coefficients = next;
        }
        var scale = coefficients.Max(value => Math.Abs(value.Real));
        if (coefficients.Any(value => Math.Abs(value.Imaginary) > 2e-7 * Math.Max(scale, 1)))
            throw new InvalidOperationException("Polynomial reconstruction did not return real coefficients.");
        return Trim(coefficients.Select(value => value.Real).ToArray());
    }

    public static double[] Multiply(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        var result = new double[first.Count + second.Count - 1];
        for (var i = 0; i < first.Count; i++) for (var j = 0; j < second.Count; j++) result[i + j] += first[i] * second[j];
        return Trim(result);
    }
    public static double[] Add(IReadOnlyList<double> a, IReadOnlyList<double> b) => Combine(a, b, 1);
    public static double[] Subtract(IReadOnlyList<double> a, IReadOnlyList<double> b) => Combine(a, b, -1);
    private static double[] Combine(IReadOnlyList<double> a, IReadOnlyList<double> b, double sign)
    {
        var result = new double[Math.Max(a.Count, b.Count)];
        for (var i = 0; i < a.Count; i++) result[i] += a[i];
        for (var i = 0; i < b.Count; i++) result[i] += sign * b[i];
        return Trim(result);
    }
    public static double[] Scale(IReadOnlyList<double> value, double scale) => value.Select(item => item * scale).ToArray();
    public static double[] ShiftAndScale(IReadOnlyList<double> value, double scale) => [0, .. value.Select(item => item * scale)];
    public static double[] Trim(double[] value, double tolerance = 1e-10)
    {
        var scale = value.Select(Math.Abs).DefaultIfEmpty(0).Max();
        var limit = tolerance * Math.Max(scale, 1);
        var count = value.Length;
        while (count > 1 && Math.Abs(value[count - 1]) <= limit) count--;
        var result = value[..count];
        for (var i = 0; i < result.Length; i++) if (Math.Abs(result[i]) <= limit) result[i] = 0;
        return result;
    }
    public static double MaximumRelativeCoefficientError(IReadOnlyList<double> actual, IReadOnlyList<double> expected)
    {
        var count = Math.Max(actual.Count, expected.Count); var maximum = 0d;
        for (var i = 0; i < count; i++)
        {
            var a = i < actual.Count ? actual[i] : 0; var e = i < expected.Count ? expected[i] : 0;
            maximum = Math.Max(maximum, Math.Abs(a - e) / Math.Max(Math.Abs(e), 1));
        }
        return maximum;
    }

    public static IReadOnlyList<Complex> Roots(IReadOnlyList<double> coefficients)
    {
        var polynomial = Trim(coefficients.ToArray());
        var degree = polynomial.Length - 1;
        if (degree <= 0) return [];
        var leading = polynomial[^1];
        var normalized = polynomial.Select(value => value / leading).ToArray();
        var radius = 1 + normalized[..^1].Select(Math.Abs).Max();
        var roots = Enumerable.Range(0, degree).Select(index =>
            Complex.FromPolarCoordinates(radius, 2 * Math.PI * (index + .25) / degree)).ToArray();
        for (var iteration = 0; iteration < 4000; iteration++)
        {
            var maximum = 0d;
            var next = new Complex[degree];
            for (var i = 0; i < degree; i++)
            {
                var denominator = Complex.One;
                for (var j = 0; j < degree; j++) if (i != j) denominator *= roots[i] - roots[j];
                if (denominator.Magnitude < 1e-24) denominator += new Complex(1e-12, 1e-12);
                var correction = Evaluate(normalized, roots[i]) / denominator;
                next[i] = roots[i] - correction;
                maximum = Math.Max(maximum, correction.Magnitude);
            }
            roots = next;
            if (maximum < 1e-12) return roots;
        }
        throw new InvalidOperationException("Polynomial root solver did not converge during LC spectral factorization.");
    }

    private static Complex Evaluate(IReadOnlyList<double> coefficients, Complex value)
    {
        Complex result = 0;
        for (var i = coefficients.Count - 1; i >= 0; i--) result = result * value + coefficients[i];
        return result;
    }
}
