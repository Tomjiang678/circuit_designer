using System.Globalization;
using System.Numerics;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Services;

/// <summary>
/// Minimal Touchstone reader for one- and two-port VNA files. The module needs
/// only the selected reflection coefficient at the design frequency, so the
/// importer deliberately does not attempt fixture de-embedding or extrapolation.
/// </summary>
public sealed record TouchstonePoint(double FrequencyHz, Complex S11, Complex S21, Complex S12, Complex S22);

public sealed record TouchstoneData(
    IReadOnlyList<TouchstonePoint> Points,
    double ReferenceImpedanceOhm,
    int PortCount,
    string DataFormat,
    string FrequencyUnit);

public sealed record ImportedPortImpedance(
    double FrequencyHz,
    Complex Impedance,
    int PortIndex,
    int PortCount,
    string FileName,
    string DataFormat,
    double ReferenceImpedanceOhm);

public static class TouchstoneImporter
{
    public static TouchstoneData Parse(string content, string fileName = "measurement.s1p")
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidDataException("The Touchstone file is empty.");

        var extension = Path.GetExtension(fileName);
        var extensionPorts = extension.Equals(".s2p", StringComparison.OrdinalIgnoreCase) ? 2
            : extension.Equals(".s1p", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        var frequencyFactor = 1e9;
        var format = "MA";
        var reference = 50.0;
        var optionSeen = false;
        var points = new List<TouchstonePoint>();
        var lineNumber = 0;

        foreach (var raw in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('!')) continue;
            if (line.StartsWith('#'))
            {
                if (!optionSeen)
                {
                    ParseOption(line, ref frequencyFactor, ref format, ref reference);
                    optionSeen = true;
                }
                continue;
            }

            var fields = line.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            var expected = extensionPorts == 2 ? 9 : extensionPorts == 1 ? 3 : 9;
            if (extensionPorts == 0)
                expected = fields.Length >= 9 ? 9 : fields.Length >= 3 ? 3 : 0;
            if (expected == 0 || fields.Length < expected)
                throw new InvalidDataException($"Touchstone line {lineNumber} has {fields.Length} columns; expected {expected}.");

            var frequency = Number(fields[0], lineNumber, "frequency") * frequencyFactor;
            var s11 = Pair(fields, 1, format, lineNumber);
            var s21 = expected >= 9 ? Pair(fields, 3, format, lineNumber) : Complex.Zero;
            var s12 = expected >= 9 ? Pair(fields, 5, format, lineNumber) : Complex.Zero;
            var s22 = expected >= 9 ? Pair(fields, 7, format, lineNumber) : s11;
            if (!(frequency > 0) || !double.IsFinite(frequency))
                throw new InvalidDataException($"Touchstone line {lineNumber} has an invalid frequency.");
            points.Add(new TouchstonePoint(frequency, s11, s21, s12, s22));
        }

        if (points.Count == 0)
            throw new InvalidDataException("No Touchstone data rows were found.");
        if (!(reference > 0) || !double.IsFinite(reference))
            throw new InvalidDataException("The Touchstone reference impedance must be finite and positive.");
        var portCount = extensionPorts != 0 ? extensionPorts : points.Any(point => point.S21 != Complex.Zero || point.S12 != Complex.Zero) ? 2 : 1;
        if (portCount == 2 && extensionPorts == 1)
            throw new InvalidDataException("A .s1p file cannot be used as a two-port measurement.");
        for (var index = 1; index < points.Count; index++)
        {
            if (points[index].FrequencyHz <= points[index - 1].FrequencyHz)
                throw new InvalidDataException("Touchstone frequencies must be strictly increasing.");
        }
        return new TouchstoneData(points, reference, portCount, format, FrequencyUnitName(frequencyFactor));
    }

    public static ImportedPortImpedance Import(string path, bool useOutputPort, double designFrequencyHz)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Touchstone file not found.", path);
        var data = Parse(File.ReadAllText(path), path);
        var portIndex = data.PortCount == 2 && useOutputPort ? 1 : 0;
        var reflection = Interpolate(data, portIndex, designFrequencyHz);
        var denominator = Complex.One - reflection;
        if (Complex.Abs(denominator) < 1e-14)
            throw new InvalidDataException("The measured reflection coefficient is exactly 1; the impedance is unbounded.");
        var impedance = data.ReferenceImpedanceOhm * (Complex.One + reflection) / denominator;
        if (!double.IsFinite(impedance.Real) || !double.IsFinite(impedance.Imaginary) || impedance.Real <= 0)
            throw new InvalidDataException("The Touchstone data does not produce a finite positive-resistance impedance.");
        return new ImportedPortImpedance(designFrequencyHz, impedance, portIndex, data.PortCount,
            Path.GetFileName(path), data.DataFormat, data.ReferenceImpedanceOhm);
    }

    public static Complex Interpolate(TouchstoneData data, int portIndex, double frequencyHz)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!(frequencyHz > 0) || !double.IsFinite(frequencyHz))
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        if (portIndex is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(portIndex));
        if (portIndex == 1 && data.PortCount != 2)
            throw new InvalidDataException("The file does not contain a second port.");

        var first = data.Points[0].FrequencyHz;
        var last = data.Points[^1].FrequencyHz;
        if (frequencyHz < first || frequencyHz > last)
            throw new InvalidDataException($"The design frequency {frequencyHz:G6} Hz is outside the measured range {first:G6}..{last:G6} Hz.");

        for (var index = 0; index < data.Points.Count; index++)
        {
            var point = data.Points[index];
            if (Math.Abs(point.FrequencyHz - frequencyHz) <= Math.Max(1, frequencyHz) * 1e-12)
                return portIndex == 0 ? point.S11 : point.S22;
            if (index == 0 || point.FrequencyHz < frequencyHz) continue;
            var previous = data.Points[index - 1];
            var fraction = (frequencyHz - previous.FrequencyHz) / (point.FrequencyHz - previous.FrequencyHz);
            var a = portIndex == 0 ? previous.S11 : previous.S22;
            var b = portIndex == 0 ? point.S11 : point.S22;
            return a + (b - a) * fraction;
        }
        return portIndex == 0 ? data.Points[^1].S11 : data.Points[^1].S22;
    }

    private static void ParseOption(string line, ref double frequencyFactor, ref string format, ref double reference)
    {
        var tokens = line[1..].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            switch (token.ToUpperInvariant())
            {
                case "HZ": frequencyFactor = 1; break;
                case "KHZ": frequencyFactor = 1e3; break;
                case "MHZ": frequencyFactor = 1e6; break;
                case "GHZ": frequencyFactor = 1e9; break;
                case "RI": format = "RI"; break;
                case "MA": format = "MA"; break;
                case "DB": format = "DB"; break;
            }
        }
        var referenceIndex = Array.FindIndex(tokens, token => token.Equals("R", StringComparison.OrdinalIgnoreCase));
        if (referenceIndex >= 0 && referenceIndex + 1 < tokens.Length &&
            double.TryParse(tokens[referenceIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedReference))
            reference = parsedReference;
    }

    private static Complex Pair(string[] fields, int index, string format, int lineNumber)
    {
        var first = Number(fields[index], lineNumber, "reflection");
        var second = Number(fields[index + 1], lineNumber, "reflection");
        return format switch
        {
            "MA" => Complex.FromPolarCoordinates(first, second * Math.PI / 180),
            "DB" => Complex.FromPolarCoordinates(Math.Pow(10, first / 20), second * Math.PI / 180),
            _ => new Complex(first, second)
        };
    }

    private static double Number(string text, int lineNumber, string what)
    {
        if (!double.TryParse(text.Replace('D', 'E').Replace('d', 'e'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new InvalidDataException($"Touchstone line {lineNumber} has an invalid {what} value '{text}'.");
        return value;
    }

    private static string FrequencyUnitName(double factor) => factor switch
    {
        1 => "Hz", 1e3 => "kHz", 1e6 => "MHz", 1e9 => "GHz", _ => factor.ToString("G3", CultureInfo.InvariantCulture) + " Hz"
    };
}
