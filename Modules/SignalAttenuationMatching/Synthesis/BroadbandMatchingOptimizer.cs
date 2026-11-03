using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Analysis;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Synthesis;

/// <summary>Small bounded log-domain pattern search for single-section multi-point matching.</summary>
public sealed class BroadbandMatchingOptimizer
{
    private readonly MatchingPlanEvaluator evaluator = new();
    private readonly ComponentRatingEvaluator ratingEvaluator = new();

    public NetworkTopologySpec Optimize(NetworkTopologySpec seed, AttenuationMatchingDesignRequest request)
    {
        if (request.Mode != DesignMode.ImpedanceMatching || request.MatchingPlan.Kind == MatchingDesignKind.SingleFrequency || seed.Elements.Count == 0)
            return seed;

        var values = seed.Elements.Select(element => element.Value).ToArray();
        var best = Score(seed, values, request);
        if (!double.IsFinite(best)) return seed;
        var step = .42;
        for (var pass = 0; pass < 14 && step > .012; pass++)
        {
            var improved = false;
            for (var index = 0; index < values.Length; index++)
            {
                var original = values[index];
                foreach (var direction in new[] { -1d, 1d })
                {
                    var candidate = Bound(seed.Elements[index].Type, original * Math.Exp(direction * step), request.Components);
                    values[index] = candidate;
                    var score = Score(seed, values, request);
                    if (score + 1e-10 < best) { best = score; original = candidate; improved = true; }
                    else values[index] = original;
                }
                values[index] = original;
            }
            if (!improved) step *= .55;
        }
        return seed with { Elements = seed.Elements.Select((element, index) => element with { Value = values[index] }).ToArray() };
    }

    private double Score(NetworkTopologySpec seed, IReadOnlyList<double> values, AttenuationMatchingDesignRequest request)
    {
        try
        {
            var spec = seed with { Elements = seed.Elements.Select((element, index) => element with { Value = values[index] }).ToArray() };
            var graph = NetworkTopologyBuilder.Build(spec, request.Components, request.DesignFrequencyHz);
            var matching = evaluator.Evaluate(graph, request, request.MatchingPlan.MinimumPointsPerBand);
            var ratings = ratingEvaluator.Evaluate(graph, request);
            return matching.Score + ratings.Penalty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or ArithmeticException)
        {
            return double.PositiveInfinity;
        }
    }

    private static double Bound(ComponentType type, double value, ComponentConstraints constraints)
    {
        var (minimum, maximum) = type switch
        {
            ComponentType.Resistor => (constraints.MinimumResistanceOhm, constraints.MaximumResistanceOhm),
            ComponentType.Capacitor => (constraints.MinimumCapacitanceFarad, constraints.MaximumCapacitanceFarad),
            ComponentType.Inductor => (constraints.MinimumInductanceHenry, constraints.MaximumInductanceHenry),
            _ => (double.Epsilon, double.MaxValue)
        };
        return Math.Clamp(value, minimum, maximum);
    }
}
