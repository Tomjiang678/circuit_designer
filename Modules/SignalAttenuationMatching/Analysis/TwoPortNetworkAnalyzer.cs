using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Analysis;

public sealed record NetworkSolution(
    NetworkMetrics Metrics,
    Complex InputVoltage,
    Complex OutputVoltage,
    Complex InputCurrent,
    Complex LoadCurrent,
    IReadOnlyDictionary<string, double> ResistorDissipationWatt);

/// <summary>
/// Independent nodal (MNA) analysis of the final passive CircuitGraph. The design
/// synthesizer never supplies cached performance values to this service.
/// </summary>
public sealed class TwoPortNetworkAnalyzer
{
    private const double GminSiemens = 1e-15;
    private const double ReturnLossCapDb = 300;

    public NetworkSolution Analyze(CircuitGraph graph, AttenuationMatchingDesignRequest request, double frequencyHz, bool includeSWeepWarnings = true)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(request);
        if (!(frequencyHz > 0) || !double.IsFinite(frequencyHz)) throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        var source = request.Source.ImpedanceAt(frequencyHz);
        var load = request.Load.ImpedanceAt(frequencyHz);
        if (source.Real <= 0 || load.Real <= 0 || !double.IsFinite(source.Real) || !double.IsFinite(load.Real))
            throw new InvalidOperationException("Port resistance must be finite and positive at every analysis frequency.");

        var nodes = graph.Nets
            .Where(net => !net.Name.Equals(NetworkTopologyBuilder.GroundNet, StringComparison.OrdinalIgnoreCase))
            .Select(net => net.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var index = nodes.Select((name, position) => (name, position))
            .ToDictionary(item => item.name, item => item.position, StringComparer.OrdinalIgnoreCase);
        if (!index.TryGetValue(NetworkTopologyBuilder.InputNet, out var inputNode) ||
            !index.TryGetValue(NetworkTopologyBuilder.OutputNet, out var outputNode))
            throw new InvalidOperationException("The passive network must expose VIN and VOUT nets.");

        var matrix = BuildAdmittance(graph, nodes, index, frequencyHz);
        matrix[inputNode, inputNode] += 1 / source;
        matrix[outputNode, outputNode] += 1 / load;
        var sourceVoltage = request.Signal.TheveninRmsVoltage(source.Real);
        var right = new Complex[nodes.Count];
        right[inputNode] += sourceVoltage / source;
        var solution = Solve(matrix, right);
        var inputVoltage = solution[inputNode];
        var outputVoltage = solution[outputNode];
        var inputCurrent = (sourceVoltage - inputVoltage) / source;
        var loadCurrent = outputVoltage / load;
        var inputImpedance = Divide(inputVoltage, inputCurrent);
        var outputImpedance = OutputImpedance(graph, nodes, index, source, frequencyHz);
        var availablePower = request.Signal.AvailablePowerWatt(source.Real);
        var loadPower = (outputVoltage * Complex.Conjugate(loadCurrent)).Real;
        var through = 4 * source.Real * load.Real / Math.Pow(Complex.Abs(source + load), 2);
        var transducerGain = availablePower > 0 ? Math.Max(0, loadPower / availablePower) : 0;
        if (transducerGain > 1 + 1e-8)
            transducerGain = Math.Min(transducerGain, 1 + 1e-8);
        var gammaIn = Reflection(inputImpedance, source);
        var gammaOut = Reflection(outputImpedance, load);
        var inputReturnLoss = ReturnLoss(gammaIn);
        var outputReturnLoss = ReturnLoss(gammaOut);
        var warnings = new List<string>();
        if (request.Source.IsComplex) warnings.Add("The source uses a custom R+jX single-frequency model; its impedance is held constant across the sweep.");
        if (request.Load.IsComplex) warnings.Add("The load uses a custom R+jX single-frequency model; its impedance is held constant across the sweep.");

        double? s11 = null, s21 = null, s22 = null, matchedAttenuation = null;
        if (Math.Abs(source.Imaginary) < 1e-12 && Math.Abs(load.Imaginary) < 1e-12 && includeSWeepWarnings)
        {
            var referenceSource = new Complex(source.Real, 0);
            var referenceLoad = new Complex(load.Real, 0);
            var reference = AnalyzeReference(graph, request, frequencyHz, referenceSource, referenceLoad);
            s11 = reference.S11Db;
            s21 = reference.S21Db;
            s22 = reference.S22Db;
            matchedAttenuation = reference.S21Db is { } s21Db ? -s21Db : null;
        }
        else if (includeSWeepWarnings && (source.Imaginary != 0 || load.Imaginary != 0))
            warnings.Add("S-parameters are not defined by a real reference impedance for complex source/load ports; return loss uses a conjugate power-wave reference instead.");

        var vswrIn = Math.Abs(source.Imaginary) < 1e-12 ? Vswr(gammaIn) : null;
        var vswrOut = Math.Abs(load.Imaginary) < 1e-12 ? Vswr(gammaOut) : null;
        var dissipation = ResistorDissipation(graph, nodes, index, solution);
        var totalDissipation = dissipation.Values.Sum();
        var maximumDissipation = dissipation.Count == 0 ? 0 : dissipation.Values.Max();
        var passive = transducerGain <= 1 + 1e-7 && totalDissipation >= -1e-12;
        var metrics = new NetworkMetrics
        {
            FrequencyHz = frequencyHz,
            SourceImpedance = source,
            LoadImpedance = load,
            InputImpedance = inputImpedance,
            OutputImpedance = outputImpedance,
            InputReflectionCoefficient = gammaIn,
            OutputReflectionCoefficient = gammaOut,
            TransducerGain = transducerGain,
            RelativeInsertionLossDb = through > 0 && transducerGain > 0 ? -10 * Math.Log10(transducerGain / through) : null,
            MatchedPortAttenuationDb = matchedAttenuation,
            S11Db = s11,
            S21Db = s21,
            S22Db = s22,
            S12Db = null,
            InputReturnLossDb = inputReturnLoss,
            OutputReturnLossDb = outputReturnLoss,
            VswrIn = vswrIn,
            VswrOut = vswrOut,
            MaximumResistorDissipationWatt = maximumDissipation,
            TotalResistorDissipationWatt = totalDissipation,
            ComponentCount = graph.Components.Count,
            IsPassive = passive,
            Warnings = warnings
        };
        return new NetworkSolution(metrics, inputVoltage, outputVoltage, inputCurrent, loadCurrent, dissipation);
    }

    public AcSweepResult Sweep(CircuitGraph graph, AttenuationMatchingDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var points = SweepFrequencies(request);
        var results = new List<(AcSweepPoint Point, NetworkSolution Solution)>();
        foreach (var frequency in points)
        {
            var solution = Analyze(graph, request, frequency);
            var metrics = solution.Metrics;
            results.Add((new AcSweepPoint(
                frequency, metrics.TransducerGain, metrics.TransducerGainDb, metrics.AttenuationDb,
                metrics.InputReturnLossDb, metrics.OutputReturnLossDb,
                metrics.InputImpedance, metrics.OutputImpedance,
                metrics.InputReflectionCoefficient, metrics.OutputReflectionCoefficient,
                metrics.VswrIn, metrics.VswrOut), solution));
        }

        var designIndex = NearestIndex(results.Select(result => result.Point.FrequencyHz).ToArray(), request.DesignFrequencyHz);
        // The design point itself must satisfy the same gate before a band can be reported.
        if (designIndex < 0 || !BandwidthPass(results[designIndex].Point, request))
            return new AcSweepResult(results.Select(result => result.Point).ToArray(), null, null, null, null, false,
                ["The design-frequency point does not meet the matching/insertion-loss gate; no valid matching bandwidth is reported."]);

        var lower = designIndex;
        while (lower > 0 && BandwidthPass(results[lower - 1].Point, request)) lower--;
        var upper = designIndex;
        while (upper < results.Count - 1 && BandwidthPass(results[upper + 1].Point, request)) upper++;
        var lowerEdge = results[lower].Point.FrequencyHz;
        var upperEdge = results[upper].Point.FrequencyHz;
        var bandwidth = upperEdge - lowerEdge;
        var limited = lower == 0 || upper == results.Count - 1;
        double? loadedQ = bandwidth > 0 ? request.DesignFrequencyHz / bandwidth : null;
        var warnings = new List<string>();
        if (limited) warnings.Add("The reported matching bandwidth touches a sweep boundary; it is not a device bandwidth limit.");
        return new AcSweepResult(results.Select(result => result.Point).ToArray(), lowerEdge, upperEdge, bandwidth, loadedQ, limited, warnings);
    }

    private static bool BandwidthPass(AcSweepPoint point, AttenuationMatchingDesignRequest request)
    {
        if (point.InputReturnLossDb + 1e-9 < request.Matching.TargetInputReturnLossDb) return false;
        if (request.Matching.TargetOutputReturnLossDb is { } output && point.OutputReturnLossDb + 1e-9 < output) return false;
        if ((request.Mode is DesignMode.ImpedanceMatching) &&
            point.AttenuationDb > request.Matching.MaximumInsertionLossDb + 1e-9) return false;
        return true;
    }

    private static IReadOnlyList<double> SweepFrequencies(AttenuationMatchingDesignRequest request)
    {
        var count = Math.Clamp(request.SweepPoints, 21, 4001);
        var start = request.StartFrequencyHz;
        var stop = request.StopFrequencyHz;
        if (!(start > 0) || !(stop > start)) throw new ArgumentOutOfRangeException(nameof(request), "Sweep frequencies are invalid.");
        var points = new List<double>(count + 1);
        for (var index = 0; index < count; index++)
        {
            var fraction = count == 1 ? 0 : index / (double)(count - 1);
            points.Add(request.LogarithmicSweep
                ? start * Math.Pow(stop / start, fraction)
                : start + (stop - start) * fraction);
        }
        points.Add(request.DesignFrequencyHz);
        // A user sweep can be coarse (for example 121 points over two decades).
        // Add a deterministic local grid around the match point so a narrow but
        // real matching band is not collapsed to a single sample.
        for (var index = 0; index <= 200; index++)
            points.Add(request.DesignFrequencyHz * (.75 + .5 * (index / 200d)));
        return points.Where(frequency => frequency >= start && frequency <= stop && frequency > 0 && double.IsFinite(frequency))
            .DistinctBy(frequency => Math.Round(Math.Log10(frequency), 12)).OrderBy(frequency => frequency).ToArray();
    }

    private static SParameterResult AnalyzeReference(CircuitGraph graph, AttenuationMatchingDesignRequest request, double frequencyHz, Complex source, Complex load)
    {
        var nodes = graph.Nets.Where(net => !net.Name.Equals(NetworkTopologyBuilder.GroundNet, StringComparison.OrdinalIgnoreCase))
            .Select(net => net.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var index = nodes.Select((name, position) => (name, position)).ToDictionary(item => item.name, item => item.position, StringComparer.OrdinalIgnoreCase);
        var inputNode = index[NetworkTopologyBuilder.InputNet];
        var outputNode = index[NetworkTopologyBuilder.OutputNet];
        var matrix = BuildAdmittance(graph, nodes, index, frequencyHz);
        matrix[inputNode, inputNode] += 1 / source;
        matrix[outputNode, outputNode] += 1 / load;
        var right = new Complex[nodes.Count];
        right[inputNode] += 1 / source;
        var solution = Solve(matrix, right);
        var vin = solution[inputNode];
        var vout = solution[outputNode];
        var iin = (1 - vin) / source;
        var zin = Divide(vin, iin);
        var zout = OutputImpedance(graph, nodes, index, source, frequencyHz);
        var s11 = (zin - source.Real) / (zin + source.Real);
        var s22 = (zout - load.Real) / (zout + load.Real);
        var s21 = 2 * vout * Math.Sqrt(source.Real / load.Real);
        return new SParameterResult(
            Complex.Abs(s11) <= 1e-15 ? ReturnLossCapDb : -20 * Math.Log10(Math.Max(Complex.Abs(s11), 1e-15)),
            s21 == Complex.Zero ? -300 : 20 * Math.Log10(Math.Max(Complex.Abs(s21), 1e-15)),
            Complex.Abs(s22) <= 1e-15 ? ReturnLossCapDb : -20 * Math.Log10(Math.Max(Complex.Abs(s22), 1e-15)));
    }

    private static Complex[,] BuildAdmittance(CircuitGraph graph, IReadOnlyList<string> nodes, IReadOnlyDictionary<string, int> index, double frequencyHz)
    {
        var matrix = new Complex[nodes.Count, nodes.Count];
        for (var node = 0; node < nodes.Count; node++) matrix[node, node] += GminSiemens;
        var omega = 2 * Math.PI * frequencyHz;
        foreach (var component in graph.Components)
        {
            if (component.Type is not (ComponentType.Resistor or ComponentType.Capacitor or ComponentType.Inductor))
                throw new NotSupportedException($"The passive matching module cannot analyze {component.Type}; only R/L/C are allowed.");
            var firstPin = component.GetPin("1");
            var secondPin = component.GetPin("2");
            var firstNet = graph.GetNetForPin(firstPin)?.Name;
            var secondNet = graph.GetNetForPin(secondPin)?.Name;
            var first = NodeIndex(firstNet, index);
            var second = NodeIndex(secondNet, index);
            var admittance = component.Type switch
            {
                ComponentType.Resistor => new Complex(1 / component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm), 0),
                ComponentType.Capacitor => new Complex(0, omega * component.Parameters.GetRequiredValue(ComponentParameterNames.CapacitanceFarad)),
                ComponentType.Inductor => 1 / new Complex(0, omega * component.Parameters.GetRequiredValue(ComponentParameterNames.InductanceHenry)),
                _ => throw new ArgumentOutOfRangeException()
            };
            Stamp(matrix, first, second, admittance);
        }
        return matrix;
    }

    private static Complex OutputImpedance(CircuitGraph graph, IReadOnlyList<string> nodes, IReadOnlyDictionary<string, int> index, Complex source, double frequencyHz)
    {
        var matrix = BuildAdmittance(graph, nodes, index, frequencyHz);
        var inputNode = index[NetworkTopologyBuilder.InputNet];
        var outputNode = index[NetworkTopologyBuilder.OutputNet];
        matrix[inputNode, inputNode] += 1 / source;
        var right = new Complex[nodes.Count];
        right[outputNode] += 1;
        return Solve(matrix, right)[outputNode];
    }

    private static IReadOnlyDictionary<string, double> ResistorDissipation(CircuitGraph graph, IReadOnlyList<string> nodes, IReadOnlyDictionary<string, int> index, Complex[] solution)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in graph.Components.Where(component => component.Type == ComponentType.Resistor))
        {
            var first = NodeIndex(graph.GetNetForPin(component.GetPin("1"))?.Name, index);
            var second = NodeIndex(graph.GetNetForPin(component.GetPin("2"))?.Name, index);
            var firstVoltage = first >= 0 ? solution[first] : Complex.Zero;
            var secondVoltage = second >= 0 ? solution[second] : Complex.Zero;
            var voltage = firstVoltage - secondVoltage;
            var resistance = component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm);
            result[component.Reference] = voltage.Magnitude * voltage.Magnitude / resistance;
        }
        return result;
    }

    private static int NodeIndex(string? net, IReadOnlyDictionary<string, int> index) =>
        net is null || net.Equals(NetworkTopologyBuilder.GroundNet, StringComparison.OrdinalIgnoreCase) ? -1 : index.TryGetValue(net, out var value) ? value : -1;

    private static void Stamp(Complex[,] matrix, int first, int second, Complex admittance)
    {
        if (first >= 0) matrix[first, first] += admittance;
        if (second >= 0) matrix[second, second] += admittance;
        if (first >= 0 && second >= 0) { matrix[first, second] -= admittance; matrix[second, first] -= admittance; }
    }

    private static Complex[] Solve(Complex[,] matrix, Complex[] right)
    {
        var size = right.Length;
        for (var column = 0; column < size; column++)
        {
            var pivot = column;
            var magnitude = matrix[pivot, column].Magnitude;
            for (var row = column + 1; row < size; row++)
                if (matrix[row, column].Magnitude > magnitude) { pivot = row; magnitude = matrix[row, column].Magnitude; }
            if (magnitude < 1e-24) throw new InvalidOperationException("The passive network is singular at this frequency; check for floating nodes or an invalid topology.");
            if (pivot != column)
            {
                for (var item = column; item < size; item++) (matrix[column, item], matrix[pivot, item]) = (matrix[pivot, item], matrix[column, item]);
                (right[column], right[pivot]) = (right[pivot], right[column]);
            }
            for (var row = column + 1; row < size; row++)
            {
                var factor = matrix[row, column] / matrix[column, column];
                if (factor == Complex.Zero) continue;
                matrix[row, column] = Complex.Zero;
                for (var item = column + 1; item < size; item++) matrix[row, item] -= factor * matrix[column, item];
                right[row] -= factor * right[column];
            }
        }
        var result = new Complex[size];
        for (var row = size - 1; row >= 0; row--)
        {
            var value = right[row];
            for (var column = row + 1; column < size; column++) value -= matrix[row, column] * result[column];
            result[row] = value / matrix[row, row];
        }
        return result;
    }

    private static Complex Divide(Complex numerator, Complex denominator) =>
        denominator.Magnitude < 1e-24 ? new Complex(double.MaxValue, 0) : numerator / denominator;

    private static Complex Reflection(Complex port, Complex reference) => (port - Complex.Conjugate(reference)) / (port + reference);

    private static double ReturnLoss(Complex gamma)
    {
        var magnitude = gamma.Magnitude;
        return magnitude <= 1e-15 ? ReturnLossCapDb : -20 * Math.Log10(Math.Max(magnitude, 1e-15));
    }

    private static double? Vswr(Complex gamma)
    {
        var magnitude = Math.Min(gamma.Magnitude, 1 - 1e-15);
        return magnitude >= 1 - 1e-12 ? null : (1 + magnitude) / (1 - magnitude);
    }

    private static int NearestIndex(IReadOnlyList<double> frequencies, double target)
    {
        if (frequencies.Count == 0) return -1;
        var best = 0;
        var distance = Math.Abs(frequencies[0] - target);
        for (var index = 1; index < frequencies.Count; index++)
        {
            var candidate = Math.Abs(frequencies[index] - target);
            if (candidate < distance) { best = index; distance = candidate; }
        }
        return best;
    }

    private sealed record SParameterResult(double S11Db, double S21Db, double S22Db);
}
