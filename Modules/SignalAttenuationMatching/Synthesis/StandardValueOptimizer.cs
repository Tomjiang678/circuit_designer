using System.Globalization;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Analysis;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Synthesis;

public sealed record OptimizedNetwork(
    NetworkTopologySpec Spec,
    CircuitGraph Graph,
    NetworkSolution Solution,
    IReadOnlyList<AttenuationMatchingComponent> Components,
    bool HardConstraintsPass,
    int CombinationsEvaluated,
    IReadOnlyList<AttenuationMatchingDiagnostic> Diagnostics);

/// <summary>
/// Continuous values are one candidate. E-series designs enumerate a bounded
/// neighborhood of real standard values and evaluate the complete network for
/// every combination; the ideal values are never used to claim standard-value
/// performance.
/// </summary>
public sealed class StandardValueOptimizer
{
    private readonly TwoPortNetworkAnalyzer analyzer = new();

    public OptimizedNetwork Optimize(NetworkTopologySpec ideal, AttenuationMatchingDesignRequest request, bool enforceObjectiveHardLimits = true)
    {
        ArgumentNullException.ThrowIfNull(ideal);
        ArgumentNullException.ThrowIfNull(request);
        var modes = ValueSets(ideal, request.Components);
        if (modes is null) return Fail(ideal, request, "No standard component values exist inside the selected ranges for one or more elements.");
        var combinations = Cartesian(modes, request.Components.MaximumSearchCombinations);
        if (combinations.Count == 0) return Fail(ideal, request, "The standard-value search space is empty.");

        OptimizedNetwork? best = null;
        var bestScore = double.PositiveInfinity;
        var hardCount = 0;
        foreach (var values in combinations)
        {
            var selected = ideal with
            {
                Elements = ideal.Elements.Select((element, index) => element with { Value = values[index] }).ToArray()
            };
            CircuitGraph graph;
            try { graph = NetworkTopologyBuilder.Build(selected, request.Components, request.DesignFrequencyHz); }
            catch { continue; }
            NetworkSolution solution;
            try { solution = analyzer.Analyze(graph, request, request.DesignFrequencyHz); }
            catch { continue; }
            var hard = EvaluateHardConstraints(solution.Metrics, selected, request, out var diagnostics);
            if (hard) hardCount++;
            var score = Score(solution.Metrics, selected, request, hard);
            if (score < bestScore - 1e-12)
            {
                bestScore = score;
                best = new OptimizedNetwork(selected, graph, solution, BuildComponents(selected, ideal, solution, request), hard,
                    combinations.Count, diagnostics);
            }
        }
        if (best is null) return Fail(ideal, request, "Every standard-value combination failed numerical network analysis.");
        if (enforceObjectiveHardLimits && hardCount == 0)
        {
            var diagnostics = best.Diagnostics.Concat([new AttenuationMatchingDiagnostic(
                "STANDARD_VALUES_MISS_HARD_CONSTRAINT", DiagnosticSeverity.Error,
                "No standard-value combination satisfies all hard attenuation/matching constraints.",
                "Attenuation/return loss", null, "Check the selected E-series, component ranges, or relax a requirement.",
                "Use a denser series, continuous values, a different topology, or relax the specification.")]).ToArray();
            best = best with { HardConstraintsPass = false, Diagnostics = diagnostics };
        }
        return best;
    }

    private static OptimizedNetwork Fail(NetworkTopologySpec ideal, AttenuationMatchingDesignRequest request, string message)
    {
        var graph = NetworkTopologyBuilder.Build(ideal, request.Components, request.DesignFrequencyHz);
        var solution = new TwoPortNetworkAnalyzer().Analyze(graph, request, request.DesignFrequencyHz);
        return new OptimizedNetwork(ideal, graph, solution, BuildComponents(ideal, ideal, solution, request), false, 0,
            [new AttenuationMatchingDiagnostic("STANDARD_VALUE_SEARCH_FAILED", DiagnosticSeverity.Error, message, "Component values", null, "At least one valid value per element")]);
    }

    private static IReadOnlyList<AttenuationMatchingComponent> BuildComponents(
        NetworkTopologySpec selected, NetworkTopologySpec ideal, NetworkSolution solution, AttenuationMatchingDesignRequest request)
    {
        var components = new List<AttenuationMatchingComponent>();
        foreach (var element in selected.Elements)
        {
            var idealElement = ideal.Elements.FirstOrDefault(item => item.Reference.Equals(element.Reference, StringComparison.OrdinalIgnoreCase)) ?? element;
            var error = Math.Abs(idealElement.Value) > 0 ? 100 * (element.Value / idealElement.Value - 1) : 0;
            var unit = NetworkTopologyBuilder.UnitFor(element.Type);
            var dissipation = solution.ResistorDissipationWatt.TryGetValue(element.Reference, out var power) ? power : 0;
            var rating = element.Type == ComponentType.Resistor
                ? $"P={dissipation:G5} W; limit {request.Components.MaximumResistorDissipationWatt:G4} W"
                : element.Type == ComponentType.Capacitor
                    ? $"ESR {request.Components.CapacitorEsrOhm:G4} ohm; useful to {request.DesignFrequencyHz * 10:G5} Hz"
                    : $"Q {request.Components.InductorQ:G5}; SRF not modeled";
            components.Add(new AttenuationMatchingComponent(element.Reference, element.Type, idealElement.Value, element.Value, unit, error, dissipation, rating));
        }
        return components;
    }

    private static IReadOnlyList<IReadOnlyList<double>>? ValueSets(NetworkTopologySpec ideal, ComponentConstraints constraints)
    {
        var sets = new List<IReadOnlyList<double>>();
        foreach (var element in ideal.Elements)
        {
            var minimum = element.Type switch
            {
                ComponentType.Resistor => constraints.MinimumResistanceOhm,
                ComponentType.Capacitor => constraints.MinimumCapacitanceFarad,
                ComponentType.Inductor => constraints.MinimumInductanceHenry,
                _ => throw new ArgumentOutOfRangeException()
            };
            var maximum = element.Type switch
            {
                ComponentType.Resistor => constraints.MaximumResistanceOhm,
                ComponentType.Capacitor => constraints.MaximumCapacitanceFarad,
                ComponentType.Inductor => constraints.MaximumInductanceHenry,
                _ => throw new ArgumentOutOfRangeException()
            };
            if (constraints.ValueMode == ComponentValueMode.Continuous)
            {
                if (!(element.Value >= minimum) || !(element.Value <= maximum)) return null;
                sets.Add([element.Value]);
                continue;
            }
            var series = ToSeries(constraints.ValueMode);
            IReadOnlyList<double> nearby;
            try { nearby = StandardValueSeries.GetNearby(element.Value, series, minimum, maximum, 2); }
            catch { return null; }
            if (nearby.Count == 0) return null;
            sets.Add(nearby.Distinct().OrderBy(value => Math.Abs(Math.Log(value / element.Value))).ToArray());
        }
        return sets;
    }

    private static IReadOnlyList<IReadOnlyList<double>> Cartesian(IReadOnlyList<IReadOnlyList<double>> sets, int maximum)
    {
        var results = new List<IReadOnlyList<double>> { Array.Empty<double>() };
        foreach (var set in sets)
        {
            if (results.Count * set.Count > maximum)
            {
                // Deterministic, lossless truncation: keep the values nearest the
                // ideal target, then stop; the UI reports the bounded search.
                var limited = new List<IReadOnlyList<double>>(maximum);
                foreach (var prefix in results)
                {
                    foreach (var value in set)
                    {
                        limited.Add(prefix.Concat([value]).ToArray());
                        if (limited.Count >= maximum) return limited;
                    }
                }
                return limited;
            }
            var next = new List<IReadOnlyList<double>>(results.Count * set.Count);
            foreach (var prefix in results)
                foreach (var value in set)
                    next.Add(prefix.Concat([value]).ToArray());
            results = next;
        }
        return results;
    }

    private static bool EvaluateHardConstraints(NetworkMetrics metrics, NetworkTopologySpec spec, AttenuationMatchingDesignRequest request, out IReadOnlyList<AttenuationMatchingDiagnostic> diagnostics)
    {
        var issues = new List<AttenuationMatchingDiagnostic>();
        if (!metrics.IsPassive)
            issues.Add(new("PASSIVITY_VIOLATION", DiagnosticSeverity.Error, "The network delivers more power than the available source power.", "Transducer gain", metrics.TransducerGain, "0..1"));
        if ((request.Mode is DesignMode.Attenuation))
        {
            var error = metrics.AttenuationDb - request.Attenuation.TargetAttenuationDb;
            if (Math.Abs(error) > request.Attenuation.AllowedAttenuationErrorDb + 1e-9)
                issues.Add(new("ATTENUATION_OUT_OF_TOLERANCE", DiagnosticSeverity.Error, "Actual attenuation does not meet the requested tolerance.", "Attenuation", metrics.AttenuationDb, $"{request.Attenuation.TargetAttenuationDb - request.Attenuation.AllowedAttenuationErrorDb:G6}..{request.Attenuation.TargetAttenuationDb + request.Attenuation.AllowedAttenuationErrorDb:G6} dB"));
        }
        if (metrics.InputReturnLossDb + 1e-9 < request.Attenuation.MinimumInputReturnLossDb && (request.Mode is DesignMode.Attenuation))
            issues.Add(new("INPUT_RETURN_LOSS_NOT_MET", DiagnosticSeverity.Error, "Input return loss is below the attenuation-mode requirement.", "Input return loss", metrics.InputReturnLossDb, $">= {request.Attenuation.MinimumInputReturnLossDb:G6} dB"));
        if (metrics.OutputReturnLossDb + 1e-9 < request.Attenuation.MinimumOutputReturnLossDb && (request.Mode is DesignMode.Attenuation))
            issues.Add(new("OUTPUT_RETURN_LOSS_NOT_MET", DiagnosticSeverity.Error, "Output return loss is below the attenuation-mode requirement.", "Output return loss", metrics.OutputReturnLossDb, $">= {request.Attenuation.MinimumOutputReturnLossDb:G6} dB"));
        if ((request.Mode is DesignMode.ImpedanceMatching))
        {
            if (metrics.InputReturnLossDb + 1e-9 < request.Matching.TargetInputReturnLossDb)
                issues.Add(new("INPUT_RETURN_LOSS_NOT_MET", DiagnosticSeverity.Error, "Input return loss is below the matching requirement.", "Input return loss", metrics.InputReturnLossDb, $">= {request.Matching.TargetInputReturnLossDb:G6} dB"));
            if (request.Matching.TargetOutputReturnLossDb is { } output && metrics.OutputReturnLossDb + 1e-9 < output)
                issues.Add(new("OUTPUT_RETURN_LOSS_NOT_MET", DiagnosticSeverity.Error, "Output return loss is below the matching requirement.", "Output return loss", metrics.OutputReturnLossDb, $">= {output:G6} dB"));
            if (metrics.AttenuationDb > request.Matching.MaximumInsertionLossDb + 1e-9)
                issues.Add(new("INSERTION_LOSS_NOT_MET", DiagnosticSeverity.Error, "Insertion loss exceeds the matching requirement.", "Insertion loss", metrics.AttenuationDb, $"<= {request.Matching.MaximumInsertionLossDb:G6} dB"));
        }
        if (metrics.MaximumResistorDissipationWatt > request.Components.MaximumResistorDissipationWatt + 1e-12)
            issues.Add(new("POWER_DISSIPATION_EXCEEDED", DiagnosticSeverity.Error, "A resistor exceeds the allowed dissipation.", "Maximum resistor dissipation", metrics.MaximumResistorDissipationWatt, $"<= {request.Components.MaximumResistorDissipationWatt:G6} W"));
        if (request.Attenuation.MaximumInputPowerWatt is { } maxInput && request.Signal.AvailablePowerWatt(request.Source.ResistanceOhm) > maxInput)
            issues.Add(new("INPUT_POWER_EXCEEDED", DiagnosticSeverity.Error, "The source exceeds the specified maximum input power.", "Available source power", request.Signal.AvailablePowerWatt(request.Source.ResistanceOhm), $"<= {maxInput:G6} W"));
        if (request.MaximumComponentCount is { } maxComponents && spec.Elements.Count > maxComponents)
            issues.Add(new("COMPONENT_COUNT_EXCEEDED", DiagnosticSeverity.Error, "The network uses more components than the requested maximum.", "Component count", spec.Elements.Count, $"<= {maxComponents}"));
        if (request.Components.ValueMode != ComponentValueMode.Continuous)
        {
            var series = ToSeries(request.Components.ValueMode);
            foreach (var element in spec.Elements)
            {
                var minimum = element.Type switch
                {
                    ComponentType.Resistor => request.Components.MinimumResistanceOhm,
                    ComponentType.Capacitor => request.Components.MinimumCapacitanceFarad,
                    ComponentType.Inductor => request.Components.MinimumInductanceHenry,
                    _ => 0
                };
                var maximum = element.Type switch
                {
                    ComponentType.Resistor => request.Components.MaximumResistanceOhm,
                    ComponentType.Capacitor => request.Components.MaximumCapacitanceFarad,
                    ComponentType.Inductor => request.Components.MaximumInductanceHenry,
                    _ => 0
                };
                if (element.Value < minimum || element.Value > maximum || !StandardValueSeries.IsSeriesValue(element.Value, series))
                    issues.Add(new("COMPONENT_NOT_STANDARD_VALUE", DiagnosticSeverity.Error, $"Component {element.Reference} is not a valid {series} value in range.", "Component value", element.Value, $"{minimum:G6}..{maximum:G6} {series}"));
            }
        }
        diagnostics = issues;
        return issues.All(issue => issue.Severity != DiagnosticSeverity.Error);
    }

    private static double Score(NetworkMetrics metrics, NetworkTopologySpec spec, AttenuationMatchingDesignRequest request, bool hard)
    {
        var attenuationError = (request.Mode is DesignMode.Attenuation)
            ? Math.Abs(metrics.AttenuationDb - request.Attenuation.TargetAttenuationDb)
            : 0;
        var inputMiss = Math.Max(0, ((request.Mode is DesignMode.ImpedanceMatching) ? request.Matching.TargetInputReturnLossDb : request.Attenuation.MinimumInputReturnLossDb) - metrics.InputReturnLossDb);
        var outputMiss = Math.Max(0, ((request.Mode is DesignMode.ImpedanceMatching) ? request.Matching.TargetOutputReturnLossDb ?? 0 : request.Attenuation.MinimumOutputReturnLossDb) - metrics.OutputReturnLossDb);
        var lossMiss = (request.Mode is DesignMode.ImpedanceMatching) ? Math.Max(0, metrics.AttenuationDb - request.Matching.MaximumInsertionLossDb) : 0;
        var stress = Math.Max(0, metrics.MaximumResistorDissipationWatt / Math.Max(1e-12, request.Components.MaximumResistorDissipationWatt) - 1);
        var common = attenuationError * 10 + inputMiss * 2 + outputMiss * 2 + lossMiss * 2 + stress;
        var objective = request.Objective switch
        {
            OptimizationObjective.BestAttenuationAccuracy => attenuationError * 20 + inputMiss + outputMiss,
            OptimizationObjective.BestMatching => inputMiss * 10 + outputMiss * 10 + attenuationError,
            OptimizationObjective.BestBandwidth => inputMiss * 5 + outputMiss * 5 + spec.Elements.Count * .01 + attenuationError,
            OptimizationObjective.MinimumInsertionLoss => metrics.AttenuationDb + inputMiss,
            OptimizationObjective.MinimumComponents => spec.Elements.Count * 10 + common,
            OptimizationObjective.MinimumComponentStress => stress * 20 + common,
            _ => common
        };
        return (hard ? 0 : 1_000_000) + objective;
    }

    private static StandardComponentSeries ToSeries(ComponentValueMode mode) => mode switch
    {
        ComponentValueMode.E12 => StandardComponentSeries.E12,
        ComponentValueMode.E24 => StandardComponentSeries.E24,
        ComponentValueMode.E48 => StandardComponentSeries.E48,
        ComponentValueMode.E96 => StandardComponentSeries.E96,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
