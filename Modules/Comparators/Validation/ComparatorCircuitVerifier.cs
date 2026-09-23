using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Comparators.Models;

namespace CircuitModuleDesigner.Modules.Comparators.Validation;

/// <summary>Derives switching points only from final graph connectivity and resistor values.</summary>
public sealed class ComparatorCircuitVerifier
{
    public IReadOnlyList<ComparatorVerificationMetric> Verify(ComparatorDesignResult result)
    {
        var graph = result.CircuitGraph ?? throw new ArgumentException("A final CircuitGraph is required.", nameof(result));
        var request = result.Request;
        var thresholdSpan = request.UpperThresholdVoltage - request.LowerThresholdVoltage;
        // A zero-hysteresis comparator has no hysteresis width from which to derive
        // the threshold tolerance. Use 0.5% of the supply span for this degenerate
        // case; retain the existing 8%-of-hysteresis rule for every Schmitt/window
        // design with distinct thresholds.
        var tolerance = thresholdSpan == 0 && request.Kind == ComparatorDesignerKind.SchmittTrigger
            ? Math.Max(1e-6, (request.SupplyHighVoltage - request.SupplyLowVoltage) * .005)
            : Math.Max(1e-6, thresholdSpan * .08);
        if (request.Kind == ComparatorDesignerKind.SchmittTrigger)
        {
            var low = SolveComparatorThreshold(graph, "U1", request, request.Topology == SchmittTopology.Inverting ? request.OutputLowVoltage : request.OutputHighVoltage);
            var high = SolveComparatorThreshold(graph, "U1", request, request.Topology == SchmittTopology.Inverting ? request.OutputHighVoltage : request.OutputLowVoltage);
            return [Metric("VTL", request.LowerThresholdVoltage, low, tolerance), Metric("VTH", request.UpperThresholdVoltage, high, tolerance)];
        }
        var targetLowerFalling = request.LowerThresholdVoltage - request.LowerBoundaryHysteresisVoltage / 2;
        var targetLowerRising = request.LowerThresholdVoltage + request.LowerBoundaryHysteresisVoltage / 2;
        var targetUpperFalling = request.UpperThresholdVoltage - request.UpperBoundaryHysteresisVoltage / 2;
        var targetUpperRising = request.UpperThresholdVoltage + request.UpperBoundaryHysteresisVoltage / 2;
        var lf = SolveComparatorThreshold(graph, "ULOW", request, request.OutputLowVoltage);
        var lr = SolveComparatorThreshold(graph, "ULOW", request, request.OutputHighVoltage);
        var uf = SolveComparatorThreshold(graph, "UHIGH", request, request.OutputLowVoltage);
        var ur = SolveComparatorThreshold(graph, "UHIGH", request, request.OutputHighVoltage);
        VerifyWindowCombiner(graph, request);
        return [Metric("Lower falling", targetLowerFalling, lf, tolerance), Metric("Lower rising", targetLowerRising, lr, tolerance),
            Metric("Upper falling", targetUpperFalling, uf, tolerance), Metric("Upper rising", targetUpperRising, ur, tolerance)];
    }

    private static double SolveComparatorThreshold(CircuitGraph graph, string reference, ComparatorDesignRequest request, double outputState)
    {
        var comparator = graph.GetComponent(reference); var plusNet = graph.GetNetForPin(comparator.Reference, "IN+")!; var minusNet = graph.GetNetForPin(comparator.Reference, "IN-")!;
        if (minusNet.Name.Equals("VIN", StringComparison.OrdinalIgnoreCase))
            return ResistorBranches(graph, plusNet).Any(branch => branch.Other.Name.Equals("NBIAS", StringComparison.OrdinalIgnoreCase))
                ? InvertingRailReferencedThreshold(graph, plusNet, request, outputState)
                : WeightedNodeVoltage(graph, plusNet, request, outputState);
        if (!minusNet.Name.Equals("NBIAS", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"{reference}.IN- is not an independently known VIN/bias node.");
        var biasVoltage = RailDividerVoltage(graph, minusNet, request);
        if (plusNet.Name.Equals("VIN", StringComparison.OrdinalIgnoreCase)) return biasVoltage;
        var branches = ResistorBranches(graph, plusNet).ToArray();
        var vin = branches.Single(branch => branch.Other.Name.Equals("VIN", StringComparison.OrdinalIgnoreCase));
        var fixedSum = branches.Where(branch => branch != vin).Sum(branch => branch.Conductance * KnownVoltage(branch.Other.Name, request, outputState));
        var total = branches.Sum(branch => branch.Conductance);
        return (biasVoltage * total - fixedSum) / vin.Conductance;
    }

    private static double InvertingRailReferencedThreshold(CircuitGraph graph, CircuitNet thresholdNode, ComparatorDesignRequest request, double outputState)
    {
        var branches = ResistorBranches(graph, thresholdNode).ToArray();
        var referenceBranch = branches.Single(branch => branch.Other.Name.Equals("NBIAS", StringComparison.OrdinalIgnoreCase));
        var outputBranch = branches.Single(branch => branch.Other.Name.Equals("VOUT", StringComparison.OrdinalIgnoreCase));
        var referenceNet = graph.GetNet("NBIAS");
        var dividerBranches = ResistorBranches(graph, referenceNet)
            .Where(branch => branch.Other.Name.Equals("VCC", StringComparison.OrdinalIgnoreCase) ||
                             branch.Other.Name.Equals("VEE", StringComparison.OrdinalIgnoreCase) ||
                             branch.Other.Name.Equals("GND", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var biasVoltage = dividerBranches.Sum(branch => branch.Conductance * KnownVoltage(branch.Other.Name, request, outputState)) /
                          dividerBranches.Sum(branch => branch.Conductance);
        var dividerResistance = 1 / dividerBranches.Sum(branch => branch.Conductance);
        var referenceResistance = 1 / referenceBranch.Conductance + dividerResistance;
        var outputResistance = 1 / outputBranch.Conductance;
        return (biasVoltage / referenceResistance + outputState / outputResistance) /
               (1 / referenceResistance + 1 / outputResistance);
    }

    private static double RailDividerVoltage(CircuitGraph graph, CircuitNet node, ComparatorDesignRequest request)
    {
        var branches = ResistorBranches(graph, node).ToArray();
        if (branches.Length != 2) throw new InvalidOperationException($"Rail-derived reference net {node.Name} must have exactly two resistor branches.");
        return branches.Sum(branch => branch.Conductance * KnownVoltage(branch.Other.Name, request, 0)) /
               branches.Sum(branch => branch.Conductance);
    }

    private static double WeightedNodeVoltage(CircuitGraph graph, CircuitNet node, ComparatorDesignRequest request, double outputState)
    {
        var branches = ResistorBranches(graph, node).ToArray();
        if (branches.Length == 0) throw new InvalidOperationException($"Comparator reference net {node.Name} has no resistor branches.");
        return branches.Sum(branch => branch.Conductance * KnownVoltage(branch.Other.Name, request, outputState)) / branches.Sum(branch => branch.Conductance);
    }

    private static void VerifyWindowCombiner(CircuitGraph graph, ComparatorDesignRequest request)
    {
        var output = graph.GetComponent("UOUT");
        if (output.Type != ComponentType.OpAmp) throw new InvalidOperationException("Window output combiner must be an op-amp stage.");
        var plus = graph.GetNetForPin(output.Reference, "IN+")!; var minus = graph.GetNetForPin(output.Reference, "IN-")!;
        var states = new[]
        {
            (A: request.OutputHighVoltage, B: request.OutputHighVoltage, Inside: false),
            (A: request.OutputLowVoltage, B: request.OutputHighVoltage, Inside: true),
            (A: request.OutputLowVoltage, B: request.OutputLowVoltage, Inside: false)
        };
        foreach (var state in states)
        {
            var plusVoltage = CombinerNodeVoltage(graph, plus, request, state.A, state.B);
            var minusVoltage = CombinerNodeVoltage(graph, minus, request, state.A, state.B);
            var actualHigh = plusVoltage > minusVoltage;
            var expectedHigh = request.OutputMode == WindowOutputMode.InsideWindowHigh ? state.Inside : !state.Inside;
            if (actualHigh != expectedHigh) throw new InvalidOperationException("Window op-amp output combiner polarity verification failed.");
        }
    }

    private static double CombinerNodeVoltage(CircuitGraph graph, CircuitNet node, ComparatorDesignRequest request, double lowComparatorOutput, double highComparatorOutput)
    {
        var branches = ResistorBranches(graph, node).ToArray();
        double Voltage(string net) => net.ToUpperInvariant() switch
        {
            "NLOW" => lowComparatorOutput, "NHIGH" => highComparatorOutput,
            "VCC" => request.SupplyHighVoltage, "VEE" or "GND" => request.SupplyLowVoltage,
            _ => throw new InvalidOperationException($"Unexpected window combiner branch {net}.")
        };
        return branches.Sum(branch => branch.Conductance * Voltage(branch.Other.Name)) / branches.Sum(branch => branch.Conductance);
    }

    private static IEnumerable<(CircuitNet Other, double Conductance)> ResistorBranches(CircuitGraph graph, CircuitNet node)
    {
        foreach (var pin in node.Pins)
        {
            var component = graph.GetComponent(pin.ComponentReference);
            if (component.Type != ComponentType.Resistor) continue;
            var otherPin = component.GetPin(pin.Name == "1" ? "2" : "1");
            var otherNet = graph.GetNetForPin(otherPin) ?? throw new InvalidOperationException($"{component.Reference} has an unconnected branch.");
            yield return (otherNet, 1 / component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm));
        }
    }

    private static double KnownVoltage(string net, ComparatorDesignRequest request, double outputState) => net.ToUpperInvariant() switch
    {
        "VCC" => request.SupplyHighVoltage, "VEE" or "GND" => request.SupplyLowVoltage, "VOUT" or "NLOW" or "NHIGH" => outputState,
        _ => throw new InvalidOperationException($"Net {net} is not an independent threshold-network voltage.")
    };

    private static ComparatorVerificationMetric Metric(string name, double target, double actual, double tolerance) =>
        new(name, target, actual, actual - target, Math.Abs(actual - target) <= tolerance);
}
