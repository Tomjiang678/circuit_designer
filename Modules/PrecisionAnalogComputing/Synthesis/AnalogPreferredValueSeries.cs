using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

internal static class AnalogPreferredValueSeries
{
    private static readonly double[] E12 = [1, 1.2, 1.5, 1.8, 2.2, 2.7, 3.3, 3.9, 4.7, 5.6, 6.8, 8.2];
    private static readonly double[] E24 = [1, 1.1, 1.2, 1.3, 1.5, 1.6, 1.8, 2, 2.2, 2.4, 2.7, 3, 3.3, 3.6, 3.9, 4.3, 4.7, 5.1, 5.6, 6.2, 6.8, 7.5, 8.2, 9.1];
    private static readonly double[] E48 = Formula(48);
    private static readonly double[] E96 = Formula(96);

    public static IReadOnlyList<double> Nearby(double target, AnalogStandardSeries series, double minimum, double maximum, int eachSide = 3)
    {
        var values = Generate(series, minimum, maximum).ToArray();
        if (values.Length == 0) return [];
        var upper = Array.FindIndex(values, value => value >= target);
        if (upper < 0) upper = values.Length - 1;
        var start = Math.Max(0, upper - eachSide);
        var end = Math.Min(values.Length - 1, upper + eachSide);
        return values[start..(end + 1)];
    }

    public static double Nearest(double target, AnalogStandardSeries series, double minimum, double maximum) =>
        Nearby(target, series, minimum, maximum, 2).MinBy(value => Math.Abs(Math.Log(value / target)));

    public static double Nearest(double target, IReadOnlyList<double> sortedValues)
    {
        if (sortedValues.Count == 0) throw new InvalidOperationException("No standard values exist inside the selected range.");
        var low = 0; var high = sortedValues.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (sortedValues[middle] < target) low = middle + 1; else high = middle;
        }
        if (low == 0) return sortedValues[0];
        if (low == sortedValues.Count) return sortedValues[^1];
        var below = sortedValues[low - 1]; var above = sortedValues[low];
        return Math.Abs(Math.Log(below / target)) <= Math.Abs(Math.Log(above / target)) ? below : above;
    }

    public static IReadOnlyList<double> All(AnalogStandardSeries series, double minimum, double maximum) =>
        Generate(series, minimum, maximum).ToArray();

    private static IEnumerable<double> Generate(AnalogStandardSeries series, double minimum, double maximum)
    {
        if (!(minimum > 0) || maximum < minimum) yield break;
        var bases = series switch
        {
            AnalogStandardSeries.E12 => E12,
            AnalogStandardSeries.E24 => E24,
            AnalogStandardSeries.E48 => E48,
            AnalogStandardSeries.E96 => E96,
            _ => throw new ArgumentOutOfRangeException(nameof(series))
        };
        var first = (int)Math.Floor(Math.Log10(minimum)) - 1;
        var last = (int)Math.Ceiling(Math.Log10(maximum)) + 1;
        for (var decade = first; decade <= last; decade++)
        {
            var scale = Math.Pow(10, decade);
            foreach (var basis in bases)
            {
                var value = basis * scale;
                if (value >= minimum && value <= maximum) yield return value;
            }
        }
    }

    private static double[] Formula(int count) => Enumerable.Range(0, count)
        .Select(index => Significant(Math.Pow(10, index / (double)count), 3)).Distinct().ToArray();

    private static double Significant(double value, int digits)
    {
        var scale = Math.Pow(10, digits - 1 - Math.Floor(Math.Log10(value)));
        return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
    }
}
