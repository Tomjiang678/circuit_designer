using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Analysis;

public sealed record ComponentRatingEvaluation(
    bool Passed,
    double Penalty,
    IReadOnlyDictionary<string, ComponentStress> WorstStress,
    IReadOnlyList<AttenuationMatchingDiagnostic> Diagnostics);

/// <summary>
/// Central LC rating evaluation used during optimization and final verification.
/// An unspecified nullable rating is not interpreted as zero.
/// </summary>
public sealed class ComponentRatingEvaluator
{
    private readonly TwoPortNetworkAnalyzer analyzer = new();

    public ComponentRatingEvaluation Evaluate(CircuitGraph graph, AttenuationMatchingDesignRequest request, bool collectUnconstrainedStress = false)
    {
        var constraints = request.Components;
        var hasStressLimits = constraints.CapacitorMaximumVoltageVolt is not null ||
                              constraints.CapacitorMaximumRippleCurrentAmpere is not null ||
                              constraints.InductorMaximumRmsCurrentAmpere is not null ||
                              constraints.InductorSaturationCurrentAmpere is not null;
        var hasSrfLimits = constraints.CapacitorSelfResonantFrequencyHz is not null ||
                           constraints.InductorSelfResonantFrequencyHz is not null;
        if (!hasStressLimits && !hasSrfLimits && !collectUnconstrainedStress)
            return new ComponentRatingEvaluation(true, 0, new Dictionary<string, ComponentStress>(), []);

        var diagnostics = new List<AttenuationMatchingDiagnostic>();
        var stresses = new Dictionary<string, ComponentStress>(StringComparer.OrdinalIgnoreCase);
        if (hasStressLimits || collectUnconstrainedStress)
        {
            foreach (var frequency in MatchingPlanEvaluator.RequiredFrequencies(request))
            {
                NetworkSolution solution;
                try { solution = analyzer.Analyze(graph, request, frequency, includeSWeepWarnings: false); }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
                {
                    diagnostics.Add(new("RATING_ANALYSIS_FAILED", DiagnosticSeverity.Error,
                        "LC rating analysis failed: " + exception.Message, "Component ratings"));
                    return new ComponentRatingEvaluation(false, 1_000_000, stresses, diagnostics);
                }
                if (solution.ComponentStress is null) continue;
                foreach (var pair in solution.ComponentStress)
                {
                    if (stresses.TryGetValue(pair.Key, out var current))
                        stresses[pair.Key] = new ComponentStress(
                            Math.Max(current.VoltageRms, pair.Value.VoltageRms),
                            Math.Max(current.CurrentRms, pair.Value.CurrentRms));
                    else
                        stresses[pair.Key] = pair.Value;
                }
            }
        }

        var maximumFrequency = MatchingPlanEvaluator.RequiredFrequencies(request).DefaultIfEmpty(request.DesignFrequencyHz).Max();
        var penalty = 0d;
        foreach (var component in graph.Components)
        {
            stresses.TryGetValue(component.Reference, out var stress);
            if (component.Type == ComponentType.Capacitor)
            {
                AddLimit("CAPACITOR_VOLTAGE_EXCEEDED", $"Capacitor {component.Reference} peak voltage exceeds its rating.",
                    "Capacitor voltage", stress?.VoltagePeak, constraints.CapacitorMaximumVoltageVolt, "V");
                AddLimit("CAPACITOR_RIPPLE_EXCEEDED", $"Capacitor {component.Reference} RMS ripple current exceeds its rating.",
                    "Capacitor ripple", stress?.CurrentRms, constraints.CapacitorMaximumRippleCurrentAmpere, "A");
                AddLimit("CAPACITOR_SRF_EXCEEDED", $"Capacitor {component.Reference} is used above its self-resonant frequency.",
                    "Capacitor SRF", maximumFrequency, constraints.CapacitorSelfResonantFrequencyHz, "Hz");
            }
            else if (component.Type == ComponentType.Inductor)
            {
                AddLimit("INDUCTOR_RMS_EXCEEDED", $"Inductor {component.Reference} RMS current exceeds its rating.",
                    "Inductor RMS current", stress?.CurrentRms, constraints.InductorMaximumRmsCurrentAmpere, "A");
                AddLimit("INDUCTOR_SATURATION_EXCEEDED", $"Inductor {component.Reference} peak current exceeds its saturation rating.",
                    "Inductor saturation", stress?.CurrentPeak, constraints.InductorSaturationCurrentAmpere, "A");
                AddLimit("INDUCTOR_SRF_EXCEEDED", $"Inductor {component.Reference} is used above its self-resonant frequency.",
                    "Inductor SRF", maximumFrequency, constraints.InductorSelfResonantFrequencyHz, "Hz");
            }
        }
        return new ComponentRatingEvaluation(diagnostics.Count == 0, penalty, stresses, diagnostics);

        void AddLimit(string code, string description, string parameter, double? actual, double? limit, string unit)
        {
            if (actual is not { } measured || limit is not { } maximum || !(maximum > 0)) return;
            var utilization = measured / maximum;
            penalty += .05 * utilization * utilization;
            if (utilization <= 1 + 1e-12) return;
            penalty += 10_000 * (utilization - 1);
            diagnostics.Add(new(code, DiagnosticSeverity.Error, description, parameter, measured, $"<= {maximum:G6} {unit}"));
        }
    }
}
