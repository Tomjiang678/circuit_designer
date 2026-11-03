using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Analysis;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Validation;

public sealed record VerificationOutcome(
    DesignStatus Status,
    NetworkMetrics Metrics,
    AcSweepResult Sweep,
    IReadOnlyList<AttenuationMatchingDiagnostic> Diagnostics,
    IReadOnlyList<AttenuationMatchingComponent> Components);

/// <summary>
/// Reads only the final CircuitGraph and the explicit port/test conditions. It
/// never trusts the synthesis-time metrics; all electrical values are recomputed
/// from the graph before a candidate can be exported or marked Supported.
/// </summary>
public sealed class AttenuationMatchingCircuitVerifier
{
    private readonly TwoPortNetworkAnalyzer analyzer = new();

    public VerificationOutcome Verify(TopologyCandidate candidate, AttenuationMatchingDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = new List<AttenuationMatchingDiagnostic>();
        void Error(string code, string message, string? parameter = null, double? actual = null, string? expected = null, string? action = null) =>
            diagnostics.Add(new(code, DiagnosticSeverity.Error, message, parameter, actual, expected, action));
        void Warning(string code, string message, string? parameter = null, double? actual = null, string? expected = null) =>
            diagnostics.Add(new(code, DiagnosticSeverity.Warning, message, parameter, actual, expected));

        var graph = candidate.Graph;
        if (!graph.AreConnected(NetworkTopologyBuilder.InputNet, NetworkTopologyBuilder.OutputNet))
        {
        foreach (var component in graph.Components)
        {
            if (component.Type is not (ComponentType.Resistor or ComponentType.Capacitor or ComponentType.Inductor))
                Error("NON_PASSIVE_COMPONENT", $"Component {component.Reference} is {component.Type}; only R/L/C are allowed.", "Component type");
            var value = ComponentValue(component);
            if (!(value > 0) || !double.IsFinite(value))
                Error("INVALID_COMPONENT_VALUE", $"Component {component.Reference} has a non-finite or non-positive value.", "Component value", value, "> 0");
            if (component.Reference.StartsWith("R_PAR_", StringComparison.OrdinalIgnoreCase)) continue;
            var (minimum, maximum) = Bounds(component.Type, request.Components);
            if (value < minimum || value > maximum)
                Error("COMPONENT_VALUE_OUT_OF_RANGE", $"Component {component.Reference} value is outside the selected range.", "Component value", value, $"{minimum:G6}..{maximum:G6}");
            if (request.Components.ValueMode != ComponentValueMode.Continuous &&
                !StandardValueSeries.IsSeriesValue(value, ToSeries(request.Components.ValueMode)))
                Error("COMPONENT_NOT_STANDARD_VALUE", $"Component {component.Reference} is not a standard value.", "Component value", value, request.Components.ValueMode.ToString());
        }
        if (!graph.Nets.Any(net => net.Name.Equals(NetworkTopologyBuilder.InputNet, StringComparison.OrdinalIgnoreCase)) ||
            !graph.Nets.Any(net => net.Name.Equals(NetworkTopologyBuilder.OutputNet, StringComparison.OrdinalIgnoreCase)))
            Error("PORT_NET_MISSING", "The final CircuitGraph must expose VIN and VOUT port nets.", "Port nets");
        if (graph.Components.Count == 0)
            Error("EMPTY_NETWORK", "The final CircuitGraph contains no components.", "Component count", 0, ">= 1");
        foreach (var net in graph.Nets.Where(net => !net.Name.Equals(NetworkTopologyBuilder.GroundNet, StringComparison.OrdinalIgnoreCase)))
        {
            if (net.Pins.Count == 0)
                Error("FLOATING_NET", $"Net {net.Name} has no pins.", "Connectivity", null, "At least one connected pin");
            else if (net.Pins.Count == 1 && !net.Name.Equals(NetworkTopologyBuilder.InputNet, StringComparison.OrdinalIgnoreCase) &&
                     !net.Name.Equals(NetworkTopologyBuilder.OutputNet, StringComparison.OrdinalIgnoreCase))
                Error("DANGLING_NET", $"Net {net.Name} has only one pin and is not a declared port.", "Connectivity", net.Pins.Count, ">= 2 or a declared port");
        }

        if (candidate.TopologySpec is { } spec)
        {
            var parasiticComponents = graph.Components.Where(component => component.Reference.StartsWith("R_PAR_", StringComparison.OrdinalIgnoreCase)).ToArray();
            var expectedComponentCount = spec.Elements.Count + parasiticComponents.Length;
            var mainComponentCount = graph.Components.Count - parasiticComponents.Length;
            if (mainComponentCount != spec.Elements.Count || graph.Components.Count != expectedComponentCount)
                Error("TOPOLOGY_MISMATCH", "The final CircuitGraph component count does not match the declared topology + parasitic model.", "Component count", graph.Components.Count, $"{spec.Elements.Count} main + {parasiticComponents.Length} parasitic");
            foreach (var element in spec.Elements)
            {
                var component = graph.Components.FirstOrDefault(item => item.Reference.Equals(element.Reference, StringComparison.OrdinalIgnoreCase));
                if (component is null)
                {
                    Error("TOPOLOGY_MISMATCH", $"Declared component {element.Reference} is missing from the final CircuitGraph.", "Component", null, element.Reference);
                    continue;
                }
                if (component.Type != element.Type)
                    Error("TOPOLOGY_MISMATCH", $"Component {element.Reference} has type {component.Type}, expected {element.Type}.", "Component type");
                var value = ComponentValue(component);
                if (Math.Abs(value / element.Value - 1) > 1e-6)
                    Error("STALE_TOPOLOGY_VALUE", $"Component {element.Reference} in the graph differs from the declared selected value.", "Component value", value, $"{element.Value:G9}");
            }
        }

        }
        NetworkSolution solution;
        AcSweepResult sweep;
        try
        {
            solution = analyzer.Analyze(graph, request, request.DesignFrequencyHz);
            sweep = analyzer.Sweep(graph, request);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return new VerificationOutcome(DesignStatus.Failed,
                new NetworkMetrics { FrequencyHz = request.DesignFrequencyHz, ComponentCount = graph.Components.Count },
                new AcSweepResult([], null, null, null, null, false, []),
                [.. diagnostics, new("NUMERICAL_SINGULARITY", DiagnosticSeverity.Error, "Independent analysis failed: " + exception.Message, "Network", null, "A solvable passive network", "Check for floating nodes or an invalid topology.")],
                []);
        }

        var metrics = solution.Metrics;
        if (!metrics.IsPassive)
            Error("PASSIVITY_VIOLATION", "The network violates passive power conservation.", "Transducer gain", metrics.TransducerGain, "0..1");
        if (metrics.MaximumResistorDissipationWatt > request.Components.MaximumResistorDissipationWatt + 1e-12)
            Error("POWER_DISSIPATION_EXCEEDED", "A resistor exceeds the allowed dissipation.", "Resistor dissipation", metrics.MaximumResistorDissipationWatt, $"<= {request.Components.MaximumResistorDissipationWatt:G6} W");
        if (request.Attenuation.MaximumInputPowerWatt is { } maxInput && request.Signal.AvailablePowerWatt(request.Source.ResistanceOhm) > maxInput)
            Error("INPUT_POWER_EXCEEDED", "The source exceeds the specified input power limit.", "Available source power", request.Signal.AvailablePowerWatt(request.Source.ResistanceOhm), $"<= {maxInput:G6} W");

        if (request.Mode is DesignMode.Attenuation)
        {
            var error = metrics.AttenuationDb - request.Attenuation.TargetAttenuationDb;
            if (Math.Abs(error) > request.Attenuation.AllowedAttenuationErrorDb + 1e-9)
                Error("ATTENUATION_OUT_OF_TOLERANCE", "Actual attenuation does not meet the requested tolerance.", "Attenuation", metrics.AttenuationDb, $"{request.Attenuation.TargetAttenuationDb - request.Attenuation.AllowedAttenuationErrorDb:G6}..{request.Attenuation.TargetAttenuationDb + request.Attenuation.AllowedAttenuationErrorDb:G6} dB");
            if (metrics.InputReturnLossDb + 1e-9 < request.Attenuation.MinimumInputReturnLossDb)
                Error("RETURN_LOSS_REQUIREMENT_NOT_MET", "Input return loss is below the attenuation-mode requirement.", "Input return loss", metrics.InputReturnLossDb, $">= {request.Attenuation.MinimumInputReturnLossDb:G6} dB");
            if (metrics.OutputReturnLossDb + 1e-9 < request.Attenuation.MinimumOutputReturnLossDb)
                Error("RETURN_LOSS_REQUIREMENT_NOT_MET", "Output return loss is below the attenuation-mode requirement.", "Output return loss", metrics.OutputReturnLossDb, $">= {request.Attenuation.MinimumOutputReturnLossDb:G6} dB");
        }
        if (request.Mode is DesignMode.ImpedanceMatching && request.MatchingPlan.Kind == MatchingDesignKind.SingleFrequency)
        {
            if (metrics.InputReturnLossDb + 1e-9 < request.Matching.TargetInputReturnLossDb)
                Error("RETURN_LOSS_REQUIREMENT_NOT_MET", "Input return loss is below the matching requirement.", "Input return loss", metrics.InputReturnLossDb, $">= {request.Matching.TargetInputReturnLossDb:G6} dB");
            if (request.Matching.TargetOutputReturnLossDb is { } output && metrics.OutputReturnLossDb + 1e-9 < output)
                Error("RETURN_LOSS_REQUIREMENT_NOT_MET", "Output return loss is below the matching requirement.", "Output return loss", metrics.OutputReturnLossDb, $">= {output:G6} dB");
            if (metrics.AttenuationDb > request.Matching.MaximumInsertionLossDb + 1e-9)
                Error("INSERTION_LOSS_REQUIREMENT_NOT_MET", "Insertion loss is above the matching requirement.", "Insertion loss", metrics.AttenuationDb, $"<= {request.Matching.MaximumInsertionLossDb:G6} dB");
        }
        if (request.Mode is DesignMode.ImpedanceMatching && request.MatchingPlan.Kind != MatchingDesignKind.SingleFrequency)
        {
            foreach (var band in sweep.BandMetrics.Where(item => !item.Passed))
            {
                var target = request.MatchingPlan.EffectiveBands(request)[band.BandIndex];
                Error("FREQUENCY_PLAN_REQUIREMENT_NOT_MET",
                    $"Frequency target {band.BandIndex + 1} fails at or near {band.WorstFrequencyHz:G6} Hz.",
                    "Frequency plan", band.WorstInputReturnLossDb,
                    $"RL in >= {target.TargetInputReturnLossDb:G6} dB; RL out >= {target.TargetOutputReturnLossDb?.ToString("G6") ?? "N/A"} dB; IL <= {target.MaximumInsertionLossDb:G6} dB",
                    "Relax the mask, narrow the band, or select a higher-order topology when available.");
            }
        }
        if (request.Mode == DesignMode.ImpedanceMatching && request.MatchingPlan.Kind == MatchingDesignKind.SingleFrequency && request.Matching.TargetBandwidthHz > 0)
        {
            if (sweep.BandwidthHz is not { } bandwidth || bandwidth + 1e-9 < request.Matching.TargetBandwidthHz)
                Error("BANDWIDTH_REQUIREMENT_NOT_MET", "The measured matching bandwidth is below the target.", "Bandwidth", sweep.BandwidthHz, $">= {request.Matching.TargetBandwidthHz:G6} Hz");
            else if (sweep.BandwidthLimitedBySweep)
                Warning("BANDWIDTH_LIMITED_BY_SWEEP", "The matched band touches a sweep boundary; the target is met only within the simulated range.", "Bandwidth", bandwidth, $">= {request.Matching.TargetBandwidthHz:G6} Hz");
        }
        var ratingEvaluation = new ComponentRatingEvaluator().Evaluate(graph, request, collectUnconstrainedStress: true);
        diagnostics.AddRange(ratingEvaluation.Diagnostics);
        var stressMap = ratingEvaluation.WorstStress;
        var availablePower = request.Signal.AvailablePowerWatt(request.Source.ResistanceOhm);
        if (availablePower > .1 &&
            (graph.Components.Any(component => component.Type == ComponentType.Capacitor) &&
             (request.Components.CapacitorMaximumVoltageVolt is null || request.Components.CapacitorMaximumRippleCurrentAmpere is null) ||
             graph.Components.Any(component => component.Type == ComponentType.Inductor) &&
             (request.Components.InductorMaximumRmsCurrentAmpere is null || request.Components.InductorSaturationCurrentAmpere is null)))
            Warning("COMPONENT_RATING_UNSPECIFIED", "High-power LC design has no complete capacitor/inductor voltage/current ratings; component stresses were calculated but cannot be fully judged.", "Component ratings");
        if (candidate.Metrics.TransducerGain is var reported &&
            Math.Abs(reported - metrics.TransducerGain) > Math.Max(1e-9, Math.Abs(metrics.TransducerGain) * 1e-6))
            Error("STALE_RESULT", "The candidate performance differs from the independent final-graph analysis.", "Transducer gain", reported, $"{metrics.TransducerGain:G9}");
        foreach (var warning in metrics.Warnings) Warning("ANALYSIS_MODEL_LIMITATION", warning);

        var status = diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
            ? DesignStatus.Failed
            : diagnostics.Any(item => item.Severity == DiagnosticSeverity.Warning) ? DesignStatus.Warning : DesignStatus.Supported;
        var components = BuildComponents(candidate.TopologySpec, graph, solution, request, stressMap);
        return new VerificationOutcome(status, metrics, sweep, diagnostics, components);
    }

    private static IReadOnlyList<AttenuationMatchingComponent> BuildComponents(
        NetworkTopologySpec? spec, CircuitGraph graph, NetworkSolution solution, AttenuationMatchingDesignRequest request,
        IReadOnlyDictionary<string, ComponentStress>? stressOverride = null)
    {
        var result = new List<AttenuationMatchingComponent>();
        foreach (var component in graph.Components.OrderBy(component => component.Reference, StringComparer.OrdinalIgnoreCase))
        {
            var ideal = spec?.Elements.FirstOrDefault(element => element.Reference.Equals(component.Reference, StringComparison.OrdinalIgnoreCase))?.Value
                ?? ComponentValue(component);
            var value = ComponentValue(component);
            var power = solution.ResistorDissipationWatt.TryGetValue(component.Reference, out var dissipation) ? dissipation : 0;
            var stress = stressOverride is { } overrideStress && overrideStress.TryGetValue(component.Reference, out var overrideItem)
                ? overrideItem
                : solution.ComponentStress is { } stresses && stresses.TryGetValue(component.Reference, out var item) ? item : null;
            var rating = component.Type switch
            {
                ComponentType.Resistor => $"P={power:G5} W; limit {request.Components.MaximumResistorDissipationWatt:G4} W",
                ComponentType.Capacitor when stress is not null =>
                    $"Vpk={stress.VoltagePeak:G4} V; Irms={stress.CurrentRms:G4} A; ESR {request.Components.CapacitorEsrOhm:G4} ohm" +
                    (request.Components.CapacitorMaximumVoltageVolt is { } maxV ? $"; Vlimit {maxV:G4} V" : "") +
                    (request.Components.CapacitorMaximumRippleCurrentAmpere is { } maxI ? $"; Ilimit {maxI:G4} A" : ""),
                ComponentType.Inductor when stress is not null =>
                    $"Irms={stress.CurrentRms:G4} A; Ipk={stress.CurrentPeak:G4} A; Q {request.Components.InductorQ:G5}" +
                    (request.Components.InductorMaximumRmsCurrentAmpere is { } maxRms ? $"; Irms limit {maxRms:G4} A" : "") +
                    (request.Components.InductorSaturationCurrentAmpere is { } saturation ? $"; Isat {saturation:G4} A" : ""),
                _ => "No stress model"
            };
            result.Add(new AttenuationMatchingComponent(component.Reference, component.Type, ideal, value,
                NetworkTopologyBuilder.UnitFor(component.Type),
                Math.Abs(ideal) > 0 ? 100 * (value / ideal - 1) : 0,
                power, rating));
        }
        return result;
    }

    private static double ComponentValue(CircuitComponent component) => component.Type switch
    {
        ComponentType.Resistor => component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm),
        ComponentType.Capacitor => component.Parameters.GetRequiredValue(ComponentParameterNames.CapacitanceFarad),
        ComponentType.Inductor => component.Parameters.GetRequiredValue(ComponentParameterNames.InductanceHenry),
        _ => double.NaN
    };

    private static (double Minimum, double Maximum) Bounds(ComponentType type, ComponentConstraints constraints) => type switch
    {
        ComponentType.Resistor => (constraints.MinimumResistanceOhm, constraints.MaximumResistanceOhm),
        ComponentType.Capacitor => (constraints.MinimumCapacitanceFarad, constraints.MaximumCapacitanceFarad),
        ComponentType.Inductor => (constraints.MinimumInductanceHenry, constraints.MaximumInductanceHenry),
        _ => (0, double.MaxValue)
    };

    private static StandardComponentSeries ToSeries(ComponentValueMode mode) => mode switch
    {
        ComponentValueMode.E12 => StandardComponentSeries.E12,
        ComponentValueMode.E24 => StandardComponentSeries.E24,
        ComponentValueMode.E48 => StandardComponentSeries.E48,
        ComponentValueMode.E96 => StandardComponentSeries.E96,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
