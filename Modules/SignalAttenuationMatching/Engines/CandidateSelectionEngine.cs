using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Synthesis;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Engines;

public sealed class CandidateSelectionEngine
{
    private readonly ResistiveAttenuatorSynthesizer padSynthesizer = new();
    private readonly LMatchingSynthesizer lMatching = new();
    private readonly PiMatchingSynthesizer piMatching = new();
    private readonly TMatchingSynthesizer tMatching = new();

    public IReadOnlyList<SynthesisAttempt> Generate(AttenuationMatchingDesignRequest request, int maximumAttempts = 18)
    {
        ArgumentNullException.ThrowIfNull(request);
        var attempts = new List<SynthesisAttempt>();
        var manual = request.Topology.Mode == TopologySelectionMode.Manual ? request.Topology.Family : null;
        switch (request.Mode)
        {
            case DesignMode.Attenuation:
                foreach (var family in manual is { } selected
                             ? new[] { selected }
                             : new[] { TopologyFamily.TPads, TopologyFamily.PiPads, TopologyFamily.LPads, TopologyFamily.CascadedPads })
                {
                    if (family is not (TopologyFamily.TPads or TopologyFamily.PiPads or TopologyFamily.LPads or TopologyFamily.CascadedPads)) continue;
                    attempts.Add(padSynthesizer.Synthesize(request, family));
                }
                break;

            case DesignMode.ImpedanceMatching:
                var distributed = request.MatchingPlan.Kind != MatchingDesignKind.SingleFrequency;
                if (!distributed && (manual is null or TopologyFamily.LMatching))
                    foreach (var variant in Enum.GetValues<LNetworkVariant>())
                        attempts.Add(lMatching.Synthesize(request, variant));
                if (manual is null or TopologyFamily.PiMatching)
                    foreach (var response in Responses(request))
                        attempts.Add(piMatching.Synthesize(request, response));
                if (manual is null or TopologyFamily.TMatching)
                    foreach (var response in Responses(request))
                        attempts.Add(tMatching.Synthesize(request, response));
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        return attempts
            .Where(attempt => attempt.Feasible && attempt.Topology is not null)
            .GroupBy(attempt => string.Join("|", attempt.Topology!.Elements.Select(element => $"{element.Reference}:{element.Type}:{element.Placement}:{Math.Round(element.Value, 9)}")))
            .Select(group => group.First())
            .Take(maximumAttempts)
            .ToArray();
    }

    private static IEnumerable<MatchingResponse> Responses(AttenuationMatchingDesignRequest request)
    {
        if (request.Topology.Mode == TopologySelectionMode.Manual)
        {
            yield return request.Topology.Response;
            yield break;
        }
        yield return MatchingResponse.LowPass;
        yield return MatchingResponse.HighPass;
        if (request.Topology.AllowMixedReactance) yield return MatchingResponse.Mixed;
    }
}
