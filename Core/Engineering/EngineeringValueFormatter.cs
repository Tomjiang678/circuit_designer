using System.Globalization;

namespace CircuitModuleDesigner.Core.Engineering;

public static class EngineeringValueFormatter
{
    private static readonly (double Scale, string Prefix)[] Prefixes =
        [(1e9, "G"), (1e6, "M"), (1e3, "k"), (1, ""), (1e-3, "m"), (1e-6, "µ"), (1e-9, "n"), (1e-12, "p")];

    public static string Format(double value, string unit, int significantDecimals = 3)
    {
        if (!double.IsFinite(value)) return value.ToString(CultureInfo.InvariantCulture) + " " + unit;
        var magnitude = Math.Abs(value);
        var choice = Prefixes.First(item => magnitude >= item.Scale || item.Scale == 1e-12);
        return $"{(value / choice.Scale).ToString("0." + new string('#', significantDecimals), CultureInfo.InvariantCulture)} {choice.Prefix}{unit}";
    }
}
