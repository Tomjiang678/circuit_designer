using System.Globalization;
using System.Text;

namespace CircuitModuleDesigner.Core.Circuit.Spice;

internal static class SpiceText
{
    public static string Number(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "SPICE values must be finite.");
        return value == 0 ? "0" : value.ToString("0.###############e+0", CultureInfo.InvariantCulture);
    }

    public static string Identifier(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var text = new StringBuilder();
        foreach (var character in value.Trim())
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '_') text.Append(character);
            else text.Append('_').Append(((int)character).ToString("X2", CultureInfo.InvariantCulture)).Append('_');
        }
        return text.Length == 0 ? fallback : text.ToString();
    }

    public static string Node(string name) => name.Equals("GND", StringComparison.OrdinalIgnoreCase) || name == "0" ? "0" : Identifier(name, "N_UNNAMED");
    public static string Title(string title) => string.IsNullOrWhiteSpace(title) ? "Circuit Module Designer" : title.Replace('\r', ' ').Replace('\n', ' ');
    public static string QuotePath(string path) => $"\"{path.Replace("\"", string.Empty).Replace('\\', '/')}\"";
}
