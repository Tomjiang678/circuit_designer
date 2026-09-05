using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;

public sealed record FilterV1CapabilityEntry(
    FilterMode Mode,
    FilterApproximation? Approximation,
    FilterResponseType Response,
    FilterImplementationType Implementation,
    string Conditions,
    bool IsSupported,
    string Reason);

public static class FilterV1Status
{
    public const bool FilterV1Frozen = true;
    public const bool SingleEndedActiveComplete = true;
    public const string SingleEndedActiveCapability =
        "Sallen-Key LP/HP/BP; MFB LP/HP/BP; Bainter Notch BS.";

    public static IReadOnlyList<FilterV1CapabilityEntry> BuildSupportMatrix()
    {
        var entries = new List<FilterV1CapabilityEntry>();
        foreach (var response in Enum.GetValues<FilterResponseType>())
        {
            entries.Add(new(FilterMode.Active, null, response, FilterImplementationType.SallenKey,
                "Single-ended; per-section feasibility and final MNA required",
                response is FilterResponseType.LowPass or FilterResponseType.HighPass or FilterResponseType.BandPass,
                response == FilterResponseType.BandStop ? "Use Bainter Notch; Sallen-Key BS is outside v1." : "Verified response-specific solver."));
            entries.Add(new(FilterMode.Active, null, response, FilterImplementationType.MultipleFeedback,
                "Single-ended; per-section feasibility and final MNA required",
                response is FilterResponseType.LowPass or FilterResponseType.HighPass or FilterResponseType.BandPass,
                response == FilterResponseType.BandStop ? "Use Bainter Notch; MFB BS has no v1 engineering advantage." : "Verified response-specific solver."));
            entries.Add(new(FilterMode.Active, null, response, FilterImplementationType.BainterNotch,
                "Single-ended; selected-value notch-depth feasibility required", response == FilterResponseType.BandStop,
                response == FilterResponseType.BandStop ? "Verified Bainter BS solver and MNA." : "Bainter is BS-specific."));
        }
        foreach (var response in Enum.GetValues<FilterResponseType>())
        {
            var rcTopology = response switch
            {
                FilterResponseType.LowPass => "First-order loaded RC Low-Pass",
                FilterResponseType.HighPass => "First-order loaded RC High-Pass",
                FilterResponseType.BandPass => "Two-corner loaded RC Band-Pass (not a Butterworth/Chebyshev prototype)",
                FilterResponseType.BandStop => "Loaded passive Twin-T notch (not an approximation-family Band-Stop)",
                _ => throw new ArgumentOutOfRangeException()
            };
            entries.Add(new(FilterMode.Passive, null, response, FilterImplementationType.Rc,
                $"Topology-based; {rcTopology}; final loaded CircuitGraph verification required", true,
                "Verified with explicit Rs/RL and AbsoluteVoltageTransfer VOUT/VSRC."));
            foreach (var approximation in new[] { FilterApproximation.Butterworth, FilterApproximation.ChebyshevTypeI })
                entries.Add(new(FilterMode.Passive, approximation, response, FilterImplementationType.Lc,
                    "DoublyTerminated; Matched only when gN+1/g0=1, otherwise Prototype-Native; loaded selected-value verification required",
                    true, "Closed-form classic ladder with explicit termination ratio and loaded MNA verification."));
            entries.Add(new(FilterMode.Passive, FilterApproximation.Bessel, response, FilterImplementationType.Lc,
                "DoublyTerminated; prototype order 1-6", true, "Insertion-loss/spectral-factor/positive-real/Cauer synthesis verified."));
            foreach (var approximation in new[] { FilterApproximation.GaussianTo6Db, FilterApproximation.GaussianTo12Db,
                         FilterApproximation.LinearPhase005Degree, FilterApproximation.LinearPhase05Degree })
            foreach (var order in TabulatedPrototypeCatalog.GetOrders(approximation))
            {
                var audit = LcPrototypeCapabilityRegistry.Evaluate(approximation, order);
                entries.Add(new(FilterMode.Passive, approximation, response, FilterImplementationType.Lc,
                    $"DoublyTerminated; prototype order {order}", audit.IsSupported, audit.ReasonCode));
            }
        }
        return entries;
    }
}
