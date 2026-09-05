using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Core.Analysis;

/// <summary>Linear AC modified-nodal analysis for the project's abstract R/C/L/op-amp CircuitGraph.</summary>
public sealed class CircuitAcAnalysisService : ICircuitAcAnalysisService
{
    private const double GminSiemens = 1e-12;

    public AcAnalysisResult Analyze(CircuitGraph graph, AcAnalysisOptions options, double? sourceResistanceOhm = null, double? loadResistanceOhm = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (options.StartFrequencyHz <= 0 || options.StopFrequencyHz <= options.StartFrequencyHz || options.PointsPerDecade < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (options.OpAmpOpenLoopGain <= 1 || !double.IsFinite(options.OpAmpOpenLoopGain))
            throw new ArgumentOutOfRangeException(nameof(options.OpAmpOpenLoopGain));
        if (options.MagnitudeReferenceLinear <= 0 || !double.IsFinite(options.MagnitudeReferenceLinear))
            throw new ArgumentOutOfRangeException(nameof(options.MagnitudeReferenceLinear));

        var excluded = new HashSet<string>(["GND", "VCC", "VEE"], StringComparer.OrdinalIgnoreCase);
        var nodeNames = graph.Nets.Where(net => !excluded.Contains(net.Name)).Select(net => net.Name).ToList();
        var useSourceResistance = sourceResistanceOhm is > 0;
        if (useSourceResistance) nodeNames.Add("VSRC");
        var nodes = nodeNames.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        if (!nodes.ContainsKey("VIN") || !nodes.ContainsKey("VOUT"))
            throw new InvalidOperationException("AC analysis requires VIN and VOUT nets.");

        var opAmps = graph.Components.Where(component => component.Type == ComponentType.OpAmp).ToArray();
        var sourceIndex = nodes.Count + opAmps.Length;
        var matrixSize = sourceIndex + 1;
        var decades = Math.Log10(options.StopFrequencyHz / options.StartFrequencyHz);
        var pointCount = Math.Clamp((int)Math.Ceiling(decades * options.PointsPerDecade) + 1, 2, 801);
        var frequencies = Enumerable.Range(0, pointCount)
            .Select(pointIndex => options.StartFrequencyHz * Math.Pow(options.StopFrequencyHz / options.StartFrequencyHz, pointIndex / (double)(pointCount - 1)))
            .Concat(options.AdditionalFrequenciesHz ?? Array.Empty<double>())
            .Where(frequency => frequency >= options.StartFrequencyHz && frequency <= options.StopFrequencyHz && frequency > 0 && double.IsFinite(frequency))
            .DistinctBy(frequency => Math.Round(Math.Log10(frequency), 12)).OrderBy(frequency => frequency).ToArray();
        var points = new List<FrequencyResponsePoint>(frequencies.Length);
        double? previousPhase = null;

        foreach (var frequency in frequencies)
        {
            var omega = 2 * Math.PI * frequency;
            var matrix = new Complex[matrixSize, matrixSize];
            var right = new Complex[matrixSize];
            for (var node = 0; node < nodes.Count; node++) matrix[node, node] += GminSiemens;

            foreach (var component in graph.Components)
            {
                if (component.Type == ComponentType.OpAmp) continue;
                var first = Node(graph.GetNetForPin(component.GetPin("1"))?.Name, nodes);
                var second = Node(graph.GetNetForPin(component.GetPin("2"))?.Name, nodes);
                var admittance = component.Type switch
                {
                    ComponentType.Resistor => 1 / component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm),
                    ComponentType.Capacitor => new Complex(0, omega * component.Parameters.GetRequiredValue(ComponentParameterNames.CapacitanceFarad)),
                    ComponentType.Inductor => 1 / new Complex(0, omega * component.Parameters.GetRequiredValue(ComponentParameterNames.InductanceHenry)),
                    _ => throw new NotSupportedException($"No AC stamp exists for {component.Type}.")
                };
                StampAdmittance(matrix, first, second, admittance);
            }

            for (var index = 0; index < opAmps.Length; index++)
            {
                var opAmp = opAmps[index];
                var branch = nodes.Count + index;
                var output = Node(graph.GetNetForPin(opAmp.GetPin("OUT"))?.Name, nodes);
                var positive = Node(graph.GetNetForPin(opAmp.GetPin("IN+"))?.Name, nodes);
                var negative = Node(graph.GetNetForPin(opAmp.GetPin("IN-"))?.Name, nodes);
                StampControlledVoltageSource(matrix, branch, output, positive, negative, options.OpAmpOpenLoopGain);
            }

            var sourceNode = useSourceResistance ? nodes["VSRC"] : nodes["VIN"];
            StampVoltageSource(matrix, right, sourceIndex, sourceNode, Complex.One);
            if (useSourceResistance)
                StampAdmittance(matrix, nodes["VSRC"], nodes["VIN"], 1 / sourceResistanceOhm!.Value);
            if (loadResistanceOhm is > 0)
                StampAdmittance(matrix, nodes["VOUT"], -1, 1 / loadResistanceOhm.Value);

            var solution = Solve(matrix, right);
            var outputVoltage = solution[nodes["VOUT"]];
            var magnitude = outputVoltage.Magnitude / options.MagnitudeReferenceLinear;
            var magnitudeDb = 20 * Math.Log10(Math.Max(magnitude, 1e-15));
            var phase = Math.Atan2(outputVoltage.Imaginary, outputVoltage.Real) * 180 / Math.PI;
            if (previousPhase.HasValue)
            {
                while (phase - previousPhase.Value > 180) phase -= 360;
                while (phase - previousPhase.Value < -180) phase += 360;
            }
            previousPhase = phase;
            points.Add(new FrequencyResponsePoint(frequency, magnitude, magnitudeDb, phase,
                solution[nodes["VIN"]], outputVoltage));
        }

        return new AcAnalysisResult
        {
            Points = points,
            MagnitudeReferenceLinear = options.MagnitudeReferenceLinear,
            MagnitudeReferenceDescription = options.MagnitudeReferenceDescription,
            Warnings = opAmps.Length == 0 ? Array.Empty<string>() : ["Abstract op-amps are analyzed as 1,000,000 V/V ideal voltage-controlled sources; bandwidth, slew rate and output limits are not modeled."]
        };
    }

    private static int Node(string? name, IReadOnlyDictionary<string, int> nodes) =>
        name == null || name.Equals("GND", StringComparison.OrdinalIgnoreCase) || name.Equals("VCC", StringComparison.OrdinalIgnoreCase) || name.Equals("VEE", StringComparison.OrdinalIgnoreCase)
            ? -1 : nodes.TryGetValue(name, out var index) ? index : -1;

    private static void StampAdmittance(Complex[,] matrix, int first, int second, Complex admittance)
    {
        if (first >= 0) matrix[first, first] += admittance;
        if (second >= 0) matrix[second, second] += admittance;
        if (first >= 0 && second >= 0) { matrix[first, second] -= admittance; matrix[second, first] -= admittance; }
    }

    private static void StampVoltageSource(Complex[,] matrix, Complex[] right, int branch, int positive, Complex voltage)
    {
        if (positive < 0) throw new InvalidOperationException("The AC source positive node is missing.");
        matrix[positive, branch] += 1; matrix[branch, positive] += 1; right[branch] += voltage;
    }

    private static void StampControlledVoltageSource(Complex[,] matrix, int branch, int output, int positive, int negative, double gain)
    {
        if (output < 0) throw new InvalidOperationException("An op-amp output is not connected to an analyzable net.");
        matrix[output, branch] += 1; matrix[branch, output] += 1;
        if (positive >= 0) matrix[branch, positive] -= gain;
        if (negative >= 0) matrix[branch, negative] += gain;
    }

    private static Complex[] Solve(Complex[,] matrix, Complex[] right)
    {
        var size = right.Length;
        for (var column = 0; column < size; column++)
        {
            var pivot = column;
            var pivotMagnitude = matrix[pivot, column].Magnitude;
            for (var row = column + 1; row < size; row++)
                if (matrix[row, column].Magnitude > pivotMagnitude) { pivot = row; pivotMagnitude = matrix[row, column].Magnitude; }
            if (pivotMagnitude < 1e-20) throw new InvalidOperationException("Circuit AC matrix is singular; check for floating nets or conflicting ideal sources.");
            if (pivot != column)
            {
                for (var index = column; index < size; index++) (matrix[column, index], matrix[pivot, index]) = (matrix[pivot, index], matrix[column, index]);
                (right[column], right[pivot]) = (right[pivot], right[column]);
            }
            for (var row = column + 1; row < size; row++)
            {
                var factor = matrix[row, column] / matrix[column, column];
                if (factor == Complex.Zero) continue;
                matrix[row, column] = Complex.Zero;
                for (var index = column + 1; index < size; index++) matrix[row, index] -= factor * matrix[column, index];
                right[row] -= factor * right[column];
            }
        }
        var solution = new Complex[size];
        for (var row = size - 1; row >= 0; row--)
        {
            var value = right[row];
            for (var column = row + 1; column < size; column++) value -= matrix[row, column] * solution[column];
            solution[row] = value / matrix[row, row];
        }
        return solution;
    }
}
