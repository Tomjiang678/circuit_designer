using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

internal sealed record LimitedDcSolution(double OutputVoltage, IReadOnlyDictionary<string, double> NodeVoltages, IReadOnlyList<string> SaturatedOpAmps)
{
    public bool IsSupplyLimited => SaturatedOpAmps.Count > 0;
}

/// <summary>Independent resistor/ideal-op-amp nodal verifier with optional rail-limited op-amp state solving.</summary>
internal sealed class LinearCircuitDcVerifier
{
    private const double OpenLoopGain = 1e9;

    public double Solve(CircuitGraph graph, IReadOnlyDictionary<string, double> fixedVoltages, string outputNet = "VOUT") =>
        SolveNetwork(graph, fixedVoltages, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), 0, 0).Nodes[outputNet];

    public LimitedDcSolution SolveWithSupplyLimits(CircuitGraph graph, IReadOnlyDictionary<string, double> fixedVoltages, double lowerOutputVoltage, double upperOutputVoltage, string outputNet = "VOUT")
    {
        if (!(lowerOutputVoltage < upperOutputVoltage)) throw new ArgumentException("The lower op-amp output limit must be below the upper limit.");
        var opAmps = graph.Components.Where(component => component.Type == ComponentType.OpAmp).OrderBy(component => component.Reference).ToArray();
        NetworkSolution? best = null; List<string>? bestSaturated = null;
        var combinations = (int)Math.Pow(3, opAmps.Length);
        for (var code = 0; code < combinations; code++)
        {
            var value = code; var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); var saturated = new List<string>();
            foreach (var opAmp in opAmps)
            {
                var digit = value % 3; value /= 3;
                var state = digit == 0 ? 0 : digit == 1 ? -1 : 1;
                states[opAmp.Reference] = state;
                if (state != 0) saturated.Add($"{opAmp.Reference} {(state < 0 ? "low" : "high")}");
            }
            NetworkSolution solution;
            try { solution = SolveNetwork(graph, fixedVoltages, states, lowerOutputVoltage, upperOutputVoltage); }
            catch (InvalidOperationException) { continue; }
            if (!IsConsistent(graph, opAmps, states, solution.Nodes, lowerOutputVoltage, upperOutputVoltage)) continue;
            if (best is null || saturated.Count < bestSaturated!.Count) { best = solution; bestSaturated = saturated; }
        }
        if (best is null) throw new InvalidOperationException("No consistent rail-limited op-amp operating state was found for this input point.");
        return new(best.Nodes[outputNet], best.Nodes, bestSaturated!);
    }

    private static bool IsConsistent(CircuitGraph graph, IReadOnlyList<CircuitComponent> opAmps, IReadOnlyDictionary<string, int> states, IReadOnlyDictionary<string, double> nodes, double low, double high)
    {
        const double tolerance = 1e-7;
        foreach (var opAmp in opAmps)
        {
            var state = states[opAmp.Reference]; var output = nodes[Net(graph, opAmp, "OUT")];
            var error = nodes[Net(graph, opAmp, "IN+")] - nodes[Net(graph, opAmp, "IN-")];
            if (state == 0 && (output < low - tolerance || output > high + tolerance)) return false;
            if (state > 0 && error < high / OpenLoopGain - tolerance) return false;
            if (state < 0 && error > low / OpenLoopGain + tolerance) return false;
        }
        return true;
    }

    private static NetworkSolution SolveNetwork(CircuitGraph graph, IReadOnlyDictionary<string, double> suppliedFixedVoltages, IReadOnlyDictionary<string, int> opAmpStates, double low, double high)
    {
        var fixedNets = new Dictionary<string, double>(suppliedFixedVoltages, StringComparer.OrdinalIgnoreCase) { ["GND"] = 0 };
        var opAmps = graph.Components.Where(component => component.Type == ComponentType.OpAmp).ToArray();
        foreach (var opAmp in opAmps)
        {
            var state = opAmpStates.GetValueOrDefault(opAmp.Reference);
            if (state != 0) fixedNets[Net(graph, opAmp, "OUT")] = state < 0 ? low : high;
        }
        var unknownNets = graph.Nets.Where(net => !fixedNets.ContainsKey(net.Name)).Select(net => net.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        var nodeIndex = unknownNets.Select((name, index) => (name, index)).ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        var activeOpAmps = opAmps.Where(opAmp => opAmpStates.GetValueOrDefault(opAmp.Reference) == 0).ToArray();
        var size = unknownNets.Length + activeOpAmps.Length; var matrix = new double[size, size]; var rhs = new double[size];
        foreach (var resistor in graph.Components.Where(component => component.Type == ComponentType.Resistor))
        {
            var net1 = Net(graph, resistor, "1"); var net2 = Net(graph, resistor, "2");
            StampConductance(net1, net2, 1d / resistor.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm), nodeIndex, fixedNets, matrix, rhs);
        }
        for (var index = 0; index < activeOpAmps.Length; index++)
        {
            var opAmp = activeOpAmps[index]; var branch = unknownNets.Length + index;
            var output = Net(graph, opAmp, "OUT"); var plus = Net(graph, opAmp, "IN+"); var minus = Net(graph, opAmp, "IN-");
            StampBranch(output, branch, 1, nodeIndex, fixedNets, matrix);
            StampEquation(output, branch, 1, nodeIndex, fixedNets, matrix, rhs);
            StampEquation(plus, branch, -OpenLoopGain, nodeIndex, fixedNets, matrix, rhs);
            StampEquation(minus, branch, OpenLoopGain, nodeIndex, fixedNets, matrix, rhs);
        }
        var solved = SolveLinear(matrix, rhs); var nodes = new Dictionary<string, double>(fixedNets, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in nodeIndex) nodes[pair.Key] = solved[pair.Value];
        return new(nodes);
    }

    private sealed record NetworkSolution(IReadOnlyDictionary<string, double> Nodes);
    private static string Net(CircuitGraph graph, CircuitComponent component, string pin) => graph.GetNetForPin(component.Reference, pin)?.Name ?? throw new InvalidOperationException($"{component.Reference}.{pin} is not connected.");
    private static void StampConductance(string a, string b, double g, IReadOnlyDictionary<string, int> nodes, IReadOnlyDictionary<string, double> fixedNets, double[,] matrix, double[] rhs)
    {
        if (nodes.TryGetValue(a, out var ai)) { matrix[ai, ai] += g; if (nodes.TryGetValue(b, out var bi)) matrix[ai, bi] -= g; else rhs[ai] += g * fixedNets[b]; }
        if (nodes.TryGetValue(b, out var bj)) { matrix[bj, bj] += g; if (nodes.TryGetValue(a, out var aj)) matrix[bj, aj] -= g; else rhs[bj] += g * fixedNets[a]; }
    }
    private static void StampBranch(string net, int branch, double value, IReadOnlyDictionary<string, int> nodes, IReadOnlyDictionary<string, double> fixedNets, double[,] matrix)
    { if (nodes.TryGetValue(net, out var node)) matrix[node, branch] += value; else if (!fixedNets.ContainsKey(net)) throw new InvalidOperationException($"Unknown fixed net {net}."); }
    private static void StampEquation(string net, int branch, double coefficient, IReadOnlyDictionary<string, int> nodes, IReadOnlyDictionary<string, double> fixedNets, double[,] matrix, double[] rhs)
    { if (nodes.TryGetValue(net, out var node)) matrix[branch, node] += coefficient; else rhs[branch] -= coefficient * fixedNets[net]; }
    private static double[] SolveLinear(double[,] matrix, double[] rhs)
    {
        var n = rhs.Length; if (n == 0) return [];
        var augmented = new double[n, n + 1];
        for (var row = 0; row < n; row++) { for (var column = 0; column < n; column++) augmented[row, column] = matrix[row, column]; augmented[row, n] = rhs[row]; }
        for (var column = 0; column < n; column++)
        {
            var pivot = column; for (var row = column + 1; row < n; row++) if (Math.Abs(augmented[row, column]) > Math.Abs(augmented[pivot, column])) pivot = row;
            if (Math.Abs(augmented[pivot, column]) < 1e-18) throw new InvalidOperationException("DC verification matrix is singular.");
            if (pivot != column) for (var c = column; c <= n; c++) (augmented[column, c], augmented[pivot, c]) = (augmented[pivot, c], augmented[column, c]);
            var divisor = augmented[column, column]; for (var c = column; c <= n; c++) augmented[column, c] /= divisor;
            for (var row = 0; row < n; row++) { if (row == column) continue; var factor = augmented[row, column]; if (Math.Abs(factor) < 1e-30) continue; for (var c = column; c <= n; c++) augmented[row, c] -= factor * augmented[column, c]; }
        }
        return Enumerable.Range(0, n).Select(row => augmented[row, n]).ToArray();
    }
}
