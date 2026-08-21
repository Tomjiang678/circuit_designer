using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public static class StandardValueSeries
{
    private static readonly double[] E12 = [1.0, 1.2, 1.5, 1.8, 2.2, 2.7, 3.3, 3.9, 4.7, 5.6, 6.8, 8.2];
    private static readonly double[] E24 = [1.0, 1.1, 1.2, 1.3, 1.5, 1.6, 1.8, 2.0, 2.2, 2.4, 2.7, 3.0, 3.3, 3.6, 3.9, 4.3, 4.7, 5.1, 5.6, 6.2, 6.8, 7.5, 8.2, 9.1];

    public static IReadOnlyList<double> GetNearby(double target, StandardComponentSeries series, double minimum, double maximum, int eachSide = 2)
    {
        if (target <= 0 || minimum <= 0 || maximum < minimum)
            throw new ArgumentOutOfRangeException(nameof(target));
        var values = Generate(series, minimum, maximum).OrderBy(value => value).ToArray();
        if (values.Length == 0)
            throw new InvalidOperationException("No standard values exist inside the selected range.");
        var nearest = Array.FindIndex(values, value => value >= target);
        if (nearest < 0) nearest = values.Length - 1;
        var start = Math.Max(0, nearest - eachSide);
        var end = Math.Min(values.Length - 1, nearest + eachSide);
        return values[start..(end + 1)];
    }

    public static bool IsSeriesValue(double value, StandardComponentSeries series, double relativeTolerance = 1e-9)
    {
        var decade = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / decade;
        return BaseValues(series).Any(candidate => Math.Abs(candidate - normalized) / normalized <= relativeTolerance);
    }

    private static IEnumerable<double> Generate(StandardComponentSeries series, double minimum, double maximum)
    {
        var bases = BaseValues(series);
        var minDecade = (int)Math.Floor(Math.Log10(minimum)) - 1;
        var maxDecade = (int)Math.Ceiling(Math.Log10(maximum)) + 1;
        for (var decade = minDecade; decade <= maxDecade; decade++)
        {
            var scale = Math.Pow(10, decade);
            foreach (var basis in bases)
            {
                var value = basis * scale;
                if (value >= minimum && value <= maximum)
                    yield return value;
            }
        }
    }

    private static IReadOnlyList<double> BaseValues(StandardComponentSeries series) => series switch
    {
        StandardComponentSeries.E12 => E12,
        StandardComponentSeries.E24 => E24,
        StandardComponentSeries.E48 => GenerateFormulaSeries(48),
        StandardComponentSeries.E96 => GenerateFormulaSeries(96),
        _ => throw new ArgumentOutOfRangeException(nameof(series))
    };

    private static double[] GenerateFormulaSeries(int count) => Enumerable.Range(0, count)
        .Select(index => Math.Round(Math.Pow(10, index / (double)count), 3, MidpointRounding.AwayFromZero))
        .Distinct()
        .ToArray();
}
