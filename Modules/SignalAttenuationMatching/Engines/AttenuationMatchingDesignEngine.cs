using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Analysis;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Synthesis;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Validation;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Engines;

/// <summary>
/// End-to-end design engine: normalize ports, validate, generate candidate
/// topologies, synthesize/standardize, then run the independent final-graph
/// verifier before ranking and selecting a candidate.
/// </summary>
public sealed class AttenuationMatchingDesignEngine
{
    private readonly CandidateSelectionEngine candidates = new();
    private readonly StandardValueOptimizer optimizer = new();
    private readonly BroadbandMatchingOptimizer broadbandOptimizer = new();
    private readonly AttenuationMatchingCircuitVerifier verifier = new();
    private readonly TwoPortNetworkAnalyzer analyzer = new();

    public AttenuationMatchingDesignResult Design(AttenuationMatchingDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var referenceFrequency = request.DesignFrequencyHz;
        var normalized = request with
        {
            DesignFrequencyHz = referenceFrequency,
            Source = request.Source.WithReferenceFrequency(referenceFrequency),
            Load = request.Load.WithReferenceFrequency(referenceFrequency)
        };
        var validation = DesignRequestValidator.Validate(normalized);
        if (validation.Any(item => item.Severity == DiagnosticSeverity.Error))
            return new AttenuationMatchingDesignResult
            {
                Request = normalized,
                Status = DesignStatus.Failed,
                Message = "The design request is invalid: " + string.Join(" ", validation.Where(item => item.Severity == DiagnosticSeverity.Error).Select(item => item.Description)),
                Diagnostics = validation
            };

        if (normalized.Mode == DesignMode.ImpedanceMatching && normalized.MatchingPlan.Kind == MatchingDesignKind.SingleFrequency && IsDirectlyMatched(normalized))
            return BuildDirectResult(normalized, validation);
        var attempts = candidates.Generate(normalized);
        if (attempts.Count == 0)
            return new AttenuationMatchingDesignResult
            {
                Request = normalized,
                Status = DesignStatus.Failed,
                Message = "No physical topology could be synthesized for the selected mode, port impedances and constraints.",
                Diagnostics = validation.Concat([new AttenuationMatchingDiagnostic(
                    "NO_TOPOLOGY_CANDIDATE", DiagnosticSeverity.Error,
                    "The selected topology family cannot match these ports with the requested attenuation, or the component ranges are too narrow.",
                    "Topology", null, "A feasible topology", "Try Automatic topology, a different high-side L form, a Pi/T network, or continuous values.")]).ToArray()
            };

        var built = new List<TopologyCandidate>();
        var attemptFailures = new List<AttenuationMatchingDiagnostic>();
        var index = 0;
        foreach (var attempt in attempts)
        {
            if (attempt.Topology is null) continue;
            index++;
            OptimizedNetwork optimized;
            try
            {
                var multiPointTopology = broadbandOptimizer.Optimize(attempt.Topology, normalized);
                optimized = optimizer.Optimize(multiPointTopology, normalized);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or ArithmeticException)
            {
                attemptFailures.Add(new("SYNTHESIS_FAILED", DiagnosticSeverity.Warning,
                    $"{attempt.Name} could not be optimized: {exception.Message}", "Topology", null, null, "Try another topology."));
                continue;
            }
            var initial = new TopologyCandidate
            {
                Id = $"{attempt.Topology.Family}-{index}",
                TopologyName = attempt.Name,
                Family = attempt.Topology.Family,
                TopologySpec = optimized.Spec,
                Response = attempt.Response,
                Graph = optimized.Graph,
                Components = optimized.Components,
                Metrics = optimized.Solution.Metrics,
                Sweep = new AcSweepResult([], null, null, null, null, false, []),
                Diagnostics = [.. optimized.Diagnostics],
                IsStandardValueDesign = attempt.Topology.Elements.Count > 0 && normalized.Components.ValueMode != ComponentValueMode.Continuous,
                TopologyDescription = attempt.Description,
                LimitNote = attempt.Topology.LimitNote,
                StatusMessage = attempt.Reason
            };
            VerificationOutcome verification;
            try { verification = verifier.Verify(initial, normalized); }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                attemptFailures.Add(new("VERIFICATION_FAILED", DiagnosticSeverity.Warning,
                    $"{attempt.Name} failed independent verification: {exception.Message}", "Topology", null, null, "Try another topology."));
                continue;
            }
            var planningWarnings = validation.Where(item => item.Code == "FANO_BANDWIDTH_SCREEN").ToArray();
            var combinedDiagnostics = verification.Diagnostics.Concat(initial.Diagnostics).Concat(planningWarnings).ToArray();
            var combinedStatus = combinedDiagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)
                ? DesignStatus.Failed
                : combinedDiagnostics.Any(item => item.Severity == DiagnosticSeverity.Warning)
                    ? DesignStatus.Warning
                    : verification.Status;
            var candidate = new TopologyCandidate
            {
                Id = initial.Id,
                TopologyName = initial.TopologyName,
                Family = initial.Family,
                TopologySpec = initial.TopologySpec,
                Response = initial.Response,
                Graph = initial.Graph,
                Components = verification.Components,
                Metrics = verification.Metrics,
                Sweep = verification.Sweep,
                Diagnostics = [.. combinedDiagnostics],
                IsStandardValueDesign = initial.IsStandardValueDesign,
                TopologyDescription = initial.TopologyDescription,
                LimitNote = initial.LimitNote,
                Status = combinedStatus,
                StatusMessage = combinedStatus switch
                {
                    DesignStatus.Supported => "All hard constraints pass independent final-graph verification.",
                    DesignStatus.Warning => "Hard constraints pass with engineering warnings.",
                    _ => combinedDiagnostics.FirstOrDefault(item => item.Severity == DiagnosticSeverity.Error)?.Description ?? "Independent verification failed."
                }
            };
            candidate.Score = Score(candidate, normalized);
            built.Add(candidate);
        }

        if (built.Count == 0)
            return new AttenuationMatchingDesignResult
            {
                Request = normalized,
                Status = DesignStatus.Failed,
                Message = "All synthesized topologies failed numerical analysis or independent verification.",
                Diagnostics = validation.Concat(attemptFailures).Concat([new AttenuationMatchingDiagnostic(
                    "ALL_CANDIDATES_FAILED", DiagnosticSeverity.Error,
                    "No candidate produced a physically valid final CircuitGraph that can be exported.",
                    "Candidates", null, "At least one verified candidate", "Relax the constraints, select another topology, or use continuous values.")]).ToArray()
            };

        built = built.OrderBy(candidate => candidate.Status switch
            {
                DesignStatus.Supported => 0,
                DesignStatus.Warning => 1,
                _ => 2
            })
            .ThenBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Components.Count)
            .Take(normalized.Components.MaximumCandidateCount)
            .ToList();
        for (var rank = 0; rank < built.Count; rank++) built[rank].Rank = rank + 1;
        if (built.All(candidate => candidate.Status == DesignStatus.Failed))
        {
            var bestFailure = built[0];
            return new AttenuationMatchingDesignResult
            {
                Request = normalized,
                Status = DesignStatus.Failed,
                Message = normalized.MatchingPlan.Kind == MatchingDesignKind.SingleFrequency
                    ? "No topology passed independent verification. Failed trials are retained for diagnostics only."
                    : "No Pi/T topology meets all multi-frequency/broadband targets. Failed trials are retained for diagnostics only; no topology is selected.",
                Candidates = built,
                SelectedIndex = -1,
                Diagnostics = bestFailure.Diagnostics.Concat(attemptFailures).Concat(validation).ToArray()
            };
        }
        var selected = built[0];
        var status = selected.Status;
        var message = status switch
        {
            DesignStatus.Supported => $"Selected {selected.TopologyName}: {selected.Metrics.AttenuationDb:G5} dB, input return loss {selected.Metrics.InputReturnLossDb:G5} dB.",
            DesignStatus.Warning => $"Selected {selected.TopologyName} with warnings: {selected.StatusMessage}",
            _ => $"No supported candidate; best failed candidate is {selected.TopologyName}: {selected.StatusMessage}"
        };
        var diagnostics = selected.Diagnostics.Concat(attemptFailures).Concat(validation.Where(item => item.Severity == DiagnosticSeverity.Warning)).ToArray();
        return new AttenuationMatchingDesignResult
        {
            Request = normalized,
            Status = status,
            Message = message,
            Candidates = built,
            SelectedIndex = 0,
            Diagnostics = diagnostics
        };
    }

    private AttenuationMatchingDesignResult BuildDirectResult(AttenuationMatchingDesignRequest request, IReadOnlyList<AttenuationMatchingDiagnostic> validation)
    {
        var spec = new NetworkTopologySpec("Direct connection", TopologyFamily.DirectConnection, MatchingResponse.LowPass,
            [], string.Empty);
        var optimized = optimizer.Optimize(spec, request);
        var initial = new TopologyCandidate
        {
            Id = "DirectConnection-1", TopologyName = "Direct connection", Family = TopologyFamily.DirectConnection,
            TopologySpec = spec, Response = MatchingResponse.LowPass, Graph = optimized.Graph,
            Components = [], Metrics = optimized.Solution.Metrics,
            Sweep = new AcSweepResult([], null, null, null, null, false, []),
            Diagnostics = [], IsStandardValueDesign = false,
            TopologyDescription = "Direct connection", LimitNote = string.Empty,
            StatusMessage = "Direct connection"
        };
        var verification = verifier.Verify(initial, request);
        var candidate = new TopologyCandidate
        {
            Id = initial.Id, TopologyName = initial.TopologyName, Family = initial.Family,
            TopologySpec = initial.TopologySpec, Response = initial.Response, Graph = initial.Graph,
            Components = verification.Components, Metrics = verification.Metrics, Sweep = verification.Sweep,
            Diagnostics = [.. verification.Diagnostics], IsStandardValueDesign = false,
            TopologyDescription = initial.TopologyDescription, LimitNote = initial.LimitNote,
            Status = verification.Status,
            StatusMessage = verification.Status switch
            {
                DesignStatus.Supported => "Direct connection",
                DesignStatus.Warning => "Direct connection (warning)",
                _ => verification.Diagnostics.FirstOrDefault(item => item.Severity == DiagnosticSeverity.Error)?.Description ?? "Direct connection failed verification."
            }
        };
        candidate.Score = Score(candidate, request);
        return new AttenuationMatchingDesignResult
        {
            Request = request,
            Status = verification.Status,
            Message = verification.Status == DesignStatus.Supported ? "Direct connection" : candidate.StatusMessage,
            Candidates = [candidate],
            SelectedIndex = 0,
            Diagnostics = [.. validation, .. verification.Diagnostics]
        };
    }
    private static bool IsDirectlyMatched(AttenuationMatchingDesignRequest request)
    {
        var source = request.Source.ImpedanceAt(request.DesignFrequencyHz);
        var load = request.Load.ImpedanceAt(request.DesignFrequencyHz);
        var difference = System.Numerics.Complex.Abs(load - System.Numerics.Complex.Conjugate(source));
        var scale = Math.Max(1, Math.Max(System.Numerics.Complex.Abs(source), System.Numerics.Complex.Abs(load)));
        return difference <= scale * 1e-9;
    }
    private static double Score(TopologyCandidate candidate, AttenuationMatchingDesignRequest request)
    {
        var distributed = request.Mode is DesignMode.ImpedanceMatching && request.MatchingPlan.Kind != MatchingDesignKind.SingleFrequency;
        var attenuationError = request.Mode is DesignMode.Attenuation
            ? Math.Abs(candidate.Metrics.AttenuationDb - request.Attenuation.TargetAttenuationDb)
            : 0;
        var inputMiss = !distributed ? Math.Max(0, (request.Mode is DesignMode.ImpedanceMatching
            ? request.Matching.TargetInputReturnLossDb
            : request.Attenuation.MinimumInputReturnLossDb) - candidate.Metrics.InputReturnLossDb) : 0;
        var outputMiss = !distributed ? Math.Max(0, (request.Mode is DesignMode.ImpedanceMatching
            ? request.Matching.TargetOutputReturnLossDb ?? 0
            : request.Attenuation.MinimumOutputReturnLossDb) - candidate.Metrics.OutputReturnLossDb) : 0;
        var bandwidthShortfall = !distributed && request.Matching.TargetBandwidthHz > 0 && (request.Mode is DesignMode.ImpedanceMatching)
            ? Math.Max(0, request.Matching.TargetBandwidthHz - (candidate.Sweep.BandwidthHz ?? 0)) / Math.Max(1, request.Matching.TargetBandwidthHz)
            : 0;
        var planPenalty = 0d;
        if (distributed)
        {
            var bands = request.MatchingPlan.EffectiveBands(request);
            foreach (var metrics in candidate.Sweep.BandMetrics.Where(item => item.BandIndex >= 0 && item.BandIndex < bands.Count))
            {
                var band = bands[metrics.BandIndex];
                planPenalty += Math.Max(0, band.TargetInputReturnLossDb - metrics.WorstInputReturnLossDb)
                    + Math.Max(0, (band.TargetOutputReturnLossDb ?? 0) - metrics.WorstOutputReturnLossDb)
                    + Math.Max(0, metrics.MaximumInsertionLossDb - band.MaximumInsertionLossDb);
                if (!metrics.Passed) planPenalty += 20;
            }
        }
        var objective = request.Objective switch
        {
            OptimizationObjective.BestAttenuationAccuracy => attenuationError * 20 + inputMiss + outputMiss,
            OptimizationObjective.BestMatching => inputMiss * 10 + outputMiss * 10 + attenuationError,
            OptimizationObjective.BestBandwidth => bandwidthShortfall * 20 + inputMiss * 3 + outputMiss * 3,
            OptimizationObjective.MinimumInsertionLoss => candidate.Metrics.AttenuationDb + inputMiss + outputMiss,
            OptimizationObjective.MinimumComponents => candidate.Components.Count * 5 + attenuationError + inputMiss + outputMiss,
            OptimizationObjective.MinimumComponentStress => candidate.Metrics.MaximumResistorDissipationWatt * 100 + attenuationError,
            _ => attenuationError * 5 + inputMiss * 2 + outputMiss * 2 + bandwidthShortfall * 5 + candidate.Components.Count * .05
        };
        return (candidate.Status == DesignStatus.Failed ? 10000 : 0) + objective + planPenalty * 10;
    }}
