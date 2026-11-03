using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Analysis;

public sealed record MatchingPlanEvaluation(
    double Score,
    bool Passed,
    IReadOnlyList<MatchingBandMetrics> Bands);

/// <summary>Shared multi-point objective used by synthesis, standard-value selection and final verification.</summary>
public sealed class MatchingPlanEvaluator
{
    private readonly TwoPortNetworkAnalyzer analyzer = new();

    public MatchingPlanEvaluation Evaluate(CircuitGraph graph, AttenuationMatchingDesignRequest request, int? pointsPerBand = null)
    {
        if (request.Mode != DesignMode.ImpedanceMatching)
            return new MatchingPlanEvaluation(0, true, []);

        var results = new List<MatchingBandMetrics>();
        var total = 0d;
        var allPass = true;
        var bands = request.MatchingPlan.EffectiveBands(request);
        for (var bandIndex = 0; bandIndex < bands.Count; bandIndex++)
        {
            var band = bands[bandIndex];
            var samples = Frequencies(band, pointsPerBand ?? request.MatchingPlan.MinimumPointsPerBand);
            var worstInput = double.PositiveInfinity;
            var worstOutput = double.PositiveInfinity;
            var maximumLoss = double.NegativeInfinity;
            var worstFrequency = samples[0];
            var worstError = double.NegativeInfinity;
            var smooth = 0d;
            foreach (var frequency in samples)
            {
                var metrics = analyzer.Analyze(graph, request, frequency, includeSWeepWarnings: false).Metrics;
                worstInput = Math.Min(worstInput, metrics.InputReturnLossDb);
                worstOutput = Math.Min(worstOutput, metrics.OutputReturnLossDb);
                maximumLoss = Math.Max(maximumLoss, metrics.AttenuationDb);
                var inputMiss = Math.Max(0, band.TargetInputReturnLossDb - metrics.InputReturnLossDb);
                var outputMiss = band.TargetOutputReturnLossDb is { } output ? Math.Max(0, output - metrics.OutputReturnLossDb) : 0;
                var lossMiss = Math.Max(0, metrics.AttenuationDb - band.MaximumInsertionLossDb);
                var error = Math.Max(inputMiss, Math.Max(outputMiss, lossMiss));
                if (error > worstError) { worstError = error; worstFrequency = frequency; }
                // The soft term keeps improving equal-ripple margin after all hard masks pass.
                smooth += error * error + .002 * Math.Exp(-Math.Min(metrics.InputReturnLossDb, metrics.OutputReturnLossDb) / 10)
                    + .001 * Math.Max(0, metrics.AttenuationDb);
            }
            var passed = worstError <= 1e-8;
            allPass &= passed;
            total += band.Weight * (100 * Math.Max(0, worstError) + smooth / samples.Count);
            results.Add(new MatchingBandMetrics(bandIndex, band.StartFrequencyHz, band.StopFrequencyHz,
                worstInput, worstOutput, maximumLoss, worstFrequency, passed));
        }
        return new MatchingPlanEvaluation(total, allPass, results);
    }

    public static IReadOnlyList<double> Frequencies(MatchingBand band, int count)
    {
        if (band.IsPoint) return [band.StartFrequencyHz];
        count = Math.Clamp(count, 5, 401);
        var frequencies = new double[count];
        for (var index = 0; index < count; index++)
        {
            var fraction = index / (double)(count - 1);
            frequencies[index] = band.StartFrequencyHz * Math.Pow(band.StopFrequencyHz / band.StartFrequencyHz, fraction);
        }
        return frequencies;
    }

    /// <summary>
    /// Canonical hard-constraint grid. Synthesis, standard-value selection,
    /// rating checks and final verification must all use this exact set.
    /// </summary>
    public static IReadOnlyList<double> RequiredFrequencies(AttenuationMatchingDesignRequest request)
    {
        if (request.Mode != DesignMode.ImpedanceMatching)
            return [request.DesignFrequencyHz];
        return request.MatchingPlan.EffectiveBands(request)
            .SelectMany(band => Frequencies(band, request.MatchingPlan.MinimumPointsPerBand))
            .Where(frequency => frequency > 0 && double.IsFinite(frequency))
            .DistinctBy(frequency => Math.Round(Math.Log10(frequency), 12))
            .OrderBy(frequency => frequency)
            .ToArray();
    }
}
