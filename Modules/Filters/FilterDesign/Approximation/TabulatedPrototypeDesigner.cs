using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public sealed class TabulatedPrototypeDesigner(FilterApproximation approximation) : IFilterPrototypeDesigner
{
    public FilterApproximation Approximation { get; } = approximation;
    public FilterPrototype Design(int order, double passbandRippleDb)
    {
        var transfer = new FilterPrototype(Approximation, order, TabulatedPrototypeCatalog.GetPoles(Approximation, order), Array.Empty<double>(),
            Normalization: Metadata(Approximation),
            Description: Description(Approximation),
            Parameters: PhaseError(Approximation));
        var capability = LcPrototypeCapabilityRegistry.Evaluate(Approximation, order);
        if (capability.IsSupported && capability.Prototype != null)
            transfer = transfer with
            {
                LadderGValues = capability.Prototype.AsLegacyGValues(),
                LcLadderPrototype = capability.Prototype with { Normalization = transfer.Normalization?.Name ?? capability.Prototype.Normalization }
            };
        return transfer;
    }

    private static PrototypeNormalizationInfo Metadata(FilterApproximation approximation) => new(
        PrototypeNormalizationKind.Minus3DbMagnitude,
        "ADI AN-649 -3 dB normalized all-pole table",
        "Published all-pole coordinates are normalized to a complete-filter -3 dB cutoff of 1; the wizard rescales the prototype so Rp occurs at fp.",
        $"Numerical response search over published orders {string.Join(", ", TabulatedPrototypeCatalog.GetOrders(approximation))}",
        approximation switch
        {
            FilterApproximation.GaussianTo6Db => "Analog Devices AN-649 Table XII, Gaussian-to-6 dB transitional response",
            FilterApproximation.GaussianTo12Db => "Analog Devices AN-649 Table XI, Gaussian-to-12 dB transitional response",
            FilterApproximation.LinearPhase005Degree => "Analog Devices AN-649 Table IX, linear phase with 0.05 degree equiripple error",
            _ => "Analog Devices AN-649 Table X, linear phase with 0.5 degree equiripple error"
        });

    private static string Description(FilterApproximation approximation) => approximation switch
    {
        FilterApproximation.GaussianTo6Db => "Analog Devices Gaussian-to-6 dB transitional all-pole response; smooth passband and Butterworth-like roll-off beyond its breakpoint.",
        FilterApproximation.GaussianTo12Db => "Analog Devices Gaussian-to-12 dB transitional all-pole response emphasizing transient fidelity through the 12 dB breakpoint.",
        FilterApproximation.LinearPhase005Degree => "Analog linear-phase all-pole response with 0.05 degree equiripple phase-error design target.",
        _ => "Analog linear-phase all-pole response with 0.5 degree equiripple phase-error design target."
    };

    private static IReadOnlyDictionary<string, double>? PhaseError(FilterApproximation approximation) => approximation switch
    {
        FilterApproximation.LinearPhase005Degree => new Dictionary<string, double> { ["EquiripplePhaseErrorDegree"] = .05 },
        FilterApproximation.LinearPhase05Degree => new Dictionary<string, double> { ["EquiripplePhaseErrorDegree"] = .5 },
        FilterApproximation.GaussianTo6Db => new Dictionary<string, double> { ["TransitionBreakpointDb"] = 6 },
        FilterApproximation.GaussianTo12Db => new Dictionary<string, double> { ["TransitionBreakpointDb"] = 12 },
        _ => null
    };
}
