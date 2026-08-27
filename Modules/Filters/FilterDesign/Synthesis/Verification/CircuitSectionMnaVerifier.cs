using System.Numerics;
using CircuitModuleDesigner.Core.Analysis;
using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Verification;

/// <summary>
/// Extracts a section transfer function from the assembled CircuitGraph.  It uses
/// the circuit MNA response only; no topology calculator or calculator Verify()
/// method participates in the extraction.
/// </summary>
public sealed class CircuitSectionMnaVerifier : ICircuitSectionTransferVerifier
{
    private readonly ICircuitAcAnalysisService analyzer;

    public CircuitSectionMnaVerifier(ICircuitAcAnalysisService? analyzer = null) =>
        this.analyzer = analyzer ?? new CircuitAcAnalysisService();

    public SectionTransferMeasurement Verify(CircuitGraph sectionCircuit, SectionTransferTarget target,
        SectionTransferVerificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sectionCircuit);
        ArgumentNullException.ThrowIfNull(target);
        options ??= new SectionTransferVerificationOptions();
        if (target.PoleAngularFrequency <= 0 || !double.IsFinite(target.PoleAngularFrequency))
            throw new ArgumentOutOfRangeException(nameof(target));

        var centerHz = target.PoleAngularFrequency / (2 * Math.PI);
        var frequencies = LogarithmicFrequencies(centerHz / 100, centerHz * 100, options.PointsPerDecade)
            .Append(centerHz).Distinct().Order().ToArray();
        var response = analyzer.Analyze(sectionCircuit,
            new AcAnalysisOptions(frequencies[0], frequencies[^1], options.PointsPerDecade,
                options.OpAmpOpenLoopGain, frequencies));
        var samples = response.Points.Select(point => new Sample(point.FrequencyHz,
            Complex.FromPolarCoordinates(point.MagnitudeLinear, point.PhaseDegrees * Math.PI / 180))).ToArray();
        if (samples.Length < 20) throw new InvalidOperationException("Section verification requires at least 20 AC samples.");

        var fitted = FitTransfer(samples, target.PoleAngularFrequency, target.DenominatorCoefficients.Count - 1);
        var poleOmega = fitted.PoleAngularFrequency;
        var q = fitted.Q;
        var center = fitted.Evaluate(poleOmega);
        var dc = fitted.Evaluate(0);
        var high = fitted.HighFrequencyGain;
        var gainReference = target.ResponseType switch
        {
            Enums.FilterResponseType.LowPass => dc,
            Enums.FilterResponseType.HighPass => high,
            Enums.FilterResponseType.BandPass => center,
            Enums.FilterResponseType.BandStop => high,
            _ => dc
        };
        var gain = gainReference.Magnitude;
        var polarity = Math.Abs(gainReference.Real) >= Math.Abs(gainReference.Imaginary)
            ? Math.Sign(gainReference.Real) : Math.Sign(gainReference.Imaginary);
        if (polarity == 0) polarity = 1;
        var frequencyError = Relative(poleOmega, target.PoleAngularFrequency);
        var qError = target.Q > 0 ? Relative(q, target.Q) : 0;
        var gainError = Relative(gain, target.TargetGainMagnitude);
        double? zeroError = target.ZeroAngularFrequency is > 0 && fitted.ZeroAngularFrequency is > 0
            ? Relative(fitted.ZeroAngularFrequency.Value, target.ZeroAngularFrequency.Value)
            : null;
        var maximumSampleError = samples.Max(sample =>
        {
            var expected = target.Evaluate(sample.FrequencyHz);
            // Near a transmission zero, relative error against |Htarget| tends to
            // infinity and measures numerical floor rather than circuit fidelity.
            // Normalize by the section passband gain floor while retaining true
            // relative error where the target magnitude is larger.
            return (sample.Value - expected).Magnitude /
                   Math.Max(expected.Magnitude, Math.Max(target.TargetGainMagnitude, 1e-12));
        });
        double? targetZeroGain = target.ZeroAngularFrequency is > 0
            ? fitted.Evaluate(target.ZeroAngularFrequency.Value).Magnitude
            : null;
        var errors = new List<string>();
        if (Math.Abs(frequencyError) > options.MetricRelativeTolerance) errors.Add($"pole frequency error {frequencyError:P6}");
        if (target.Q > 0 && Math.Abs(qError) > options.MetricRelativeTolerance) errors.Add($"Q error {qError:P6}");
        if (Math.Abs(gainError) > options.MetricRelativeTolerance) errors.Add($"gain error {gainError:P6}");
        if (zeroError.HasValue && Math.Abs(zeroError.Value) > options.MetricRelativeTolerance) errors.Add($"zero frequency error {zeroError.Value:P6}");
        if (polarity != target.TargetPolarity) errors.Add($"polarity is {polarity}, expected {target.TargetPolarity}");
        if (maximumSampleError > options.SampleRelativeTolerance) errors.Add($"maximum AC sample error {maximumSampleError:P6}");

        return new SectionTransferMeasurement
        {
            NumeratorCoefficients = fitted.Numerator,
            DenominatorCoefficients = fitted.Denominator,
            Poles = fitted.Poles,
            Zeros = fitted.Zeros,
            PoleAngularFrequency = poleOmega,
            ZeroAngularFrequency = fitted.ZeroAngularFrequency,
            Q = q,
            GainMagnitude = gain,
            Polarity = polarity,
            DcGainMagnitude = dc.Magnitude,
            CenterGainMagnitude = center.Magnitude,
            HighFrequencyGainMagnitude = high.Magnitude,
            TargetZeroGainMagnitude = targetZeroGain,
            NotchAttenuationDb = targetZeroGain.HasValue
                ? -20 * Math.Log10(Math.Max(targetZeroGain.Value / Math.Max(gain, 1e-15), 1e-15))
                : null,
            FrequencyError = frequencyError,
            QError = qError,
            GainError = gainError,
            ZeroFrequencyError = zeroError,
            MaximumSampleRelativeError = maximumSampleError,
            SampleCount = samples.Length,
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private static FittedTransfer FitTransfer(IReadOnlyList<Sample> samples, double referenceOmega, int order)
    {
        if (order == 1) return FitFirstOrder(samples, referenceOmega);
        if (order != 2) throw new NotSupportedException($"Section transfer extraction supports first- and second-order sections, not order {order}.");
        // H(x)=(n0+n1*x+n2*x^2)/(1+d1*x+d2*x^2), x=s/referenceOmega.
        var normal = new double[5, 5];
        var right = new double[5];
        foreach (var sample in samples)
        {
            var x = new Complex(0, 2 * Math.PI * sample.FrequencyHz / referenceOmega);
            var x2 = x * x;
            var coefficients = new[] { Complex.One, x, x2, -sample.Value * x, -sample.Value * x2 };
            Accumulate(coefficients.Select(value => value.Real).ToArray(), sample.Value.Real, normal, right);
            Accumulate(coefficients.Select(value => value.Imaginary).ToArray(), sample.Value.Imaginary, normal, right);
        }
        var solution = Solve(normal, right);
        return new FittedTransfer(2, solution[0], solution[1], solution[2], solution[3], solution[4], referenceOmega);
    }

    private static FittedTransfer FitFirstOrder(IReadOnlyList<Sample> samples, double referenceOmega)
    {
        // H(x)=(n0+n1*x)/(1+d1*x), x=s/referenceOmega.
        var normal = new double[3, 3];
        var right = new double[3];
        foreach (var sample in samples)
        {
            var x = new Complex(0, 2 * Math.PI * sample.FrequencyHz / referenceOmega);
            var coefficients = new[] { Complex.One, x, -sample.Value * x };
            Accumulate(coefficients.Select(value => value.Real).ToArray(), sample.Value.Real, normal, right);
            Accumulate(coefficients.Select(value => value.Imaginary).ToArray(), sample.Value.Imaginary, normal, right);
        }
        var solution = Solve(normal, right);
        return new FittedTransfer(1, solution[0], solution[1], 0, solution[2], 0, referenceOmega);
    }

    private static void Accumulate(double[] row, double value, double[,] normal, double[] right)
    {
        for (var first = 0; first < row.Length; first++)
        {
            right[first] += row[first] * value;
            for (var second = 0; second < row.Length; second++) normal[first, second] += row[first] * row[second];
        }
    }

    private static double[] Solve(double[,] matrix, double[] right)
    {
        var count = right.Length;
        for (var column = 0; column < count; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < count; row++)
                if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column])) pivot = row;
            if (Math.Abs(matrix[pivot, column]) < 1e-24) throw new InvalidOperationException("Section transfer fit is singular.");
            if (pivot != column)
            {
                for (var index = column; index < count; index++)
                    (matrix[column, index], matrix[pivot, index]) = (matrix[pivot, index], matrix[column, index]);
                (right[column], right[pivot]) = (right[pivot], right[column]);
            }
            for (var row = column + 1; row < count; row++)
            {
                var factor = matrix[row, column] / matrix[column, column];
                for (var index = column; index < count; index++) matrix[row, index] -= factor * matrix[column, index];
                right[row] -= factor * right[column];
            }
        }
        var result = new double[count];
        for (var row = count - 1; row >= 0; row--)
        {
            var value = right[row];
            for (var column = row + 1; column < count; column++) value -= matrix[row, column] * result[column];
            result[row] = value / matrix[row, row];
        }
        return result;
    }

    private static IReadOnlyList<double> LogarithmicFrequencies(double start, double stop, int pointsPerDecade)
    {
        var count = Math.Max(20, (int)Math.Ceiling(Math.Log10(stop / start) * pointsPerDecade) + 1);
        return Enumerable.Range(0, count).Select(index => start * Math.Pow(stop / start, index / (double)(count - 1))).ToArray();
    }

    private static double Relative(double actual, double target) => target == 0 ? actual : (actual - target) / target;
    private sealed record Sample(double FrequencyHz, Complex Value);

    private sealed class FittedTransfer
    {
        private readonly int order;
        private readonly double n0, n1, n2, d1, d2, referenceOmega;
        public FittedTransfer(int order, double n0, double n1, double n2, double d1, double d2, double referenceOmega) =>
            (this.order, this.n0, this.n1, this.n2, this.d1, this.d2, this.referenceOmega) = (order, n0, n1, n2, d1, d2, referenceOmega);
        public IReadOnlyList<double> Numerator => order == 1 ? [n1 / referenceOmega, n0] : [n2 / (referenceOmega * referenceOmega), n1 / referenceOmega, n0];
        public IReadOnlyList<double> Denominator => order == 1 ? [d1 / referenceOmega, 1] : [d2 / (referenceOmega * referenceOmega), d1 / referenceOmega, 1];
        public double PoleAngularFrequency => order == 1 ? referenceOmega / d1 : d2 > 0 ? referenceOmega / Math.Sqrt(d2) : double.NaN;
        public double Q => order == 1 ? 0 : d1 > 0 && d2 > 0 ? Math.Sqrt(d2) / d1 : double.NaN;
        public double? ZeroAngularFrequency => order == 2 && n0 * n2 > 0 ? referenceOmega * Math.Sqrt(n0 / n2) : null;
        public Complex HighFrequencyGain => order == 1 ? new Complex(n1 / d1, 0) : d2 == 0 ? new Complex(double.NaN, 0) : new Complex(n2 / d2, 0);
        public IReadOnlyList<Complex> Poles => (order == 1 ? Roots(0, d1, 1) : Roots(d2, d1, 1)).Select(value => value * referenceOmega).ToArray();
        public IReadOnlyList<Complex> Zeros => (order == 1 ? Roots(0, n1, n0) : Roots(n2, n1, n0)).Select(value => value * referenceOmega).ToArray();
        public Complex Evaluate(double omega)
        {
            var x = new Complex(0, omega / referenceOmega);
            return (n0 + n1 * x + n2 * x * x) / (1 + d1 * x + d2 * x * x);
        }
        private static IReadOnlyList<Complex> Roots(double a, double b, double c)
        {
            if (Math.Abs(a) < 1e-14) return Math.Abs(b) < 1e-14 ? Array.Empty<Complex>() : [new Complex(-c / b, 0)];
            var root = Complex.Sqrt(new Complex(b * b - 4 * a * c, 0));
            return [(-b + root) / (2 * a), (-b - root) / (2 * a)];
        }
    }
}
