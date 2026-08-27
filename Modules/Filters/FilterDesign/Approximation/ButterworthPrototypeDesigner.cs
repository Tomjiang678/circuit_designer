using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public sealed class ButterworthPrototypeDesigner : IFilterPrototypeDesigner
{
    public FilterApproximation Approximation => FilterApproximation.Butterworth;

    public FilterPrototype Design(int order, double passbandRippleDb)
    {
        if (order < 1)
            throw new ArgumentOutOfRangeException(nameof(order));
        var poles = Enumerable.Range(0, order)
            .Select(index => Complex.FromPolarCoordinates(1, Math.PI * (2 * index + order + 1) / (2 * order)))
            .ToArray();
        var g = new double[order + 2];
        g[0] = 1;
        for (var index = 1; index <= order; index++)
            g[index] = 2 * Math.Sin((2 * index - 1) * Math.PI / (2 * order));
        g[^1] = 1;
        return new FilterPrototype(Approximation, order, poles, g,
            Normalization: new PrototypeNormalizationInfo(
                PrototypeNormalizationKind.UnitCutoff,
                "Unit cutoff all-pole prototype",
                "Poles lie on the unit circle; the specification normalizer scales the prototype so attenuation Rp occurs at fp.",
                "Closed-form Butterworth order equation",
                "Classical Butterworth maximally-flat magnitude polynomial"),
            Description: "Maximally flat passband magnitude with monotonic attenuation.",
            LcLadderPrototype: ClassicLcPrototypeFactory.Create(Approximation, order, g,
                "Unit-cutoff insertion-loss ladder", "Closed-form Butterworth g-values"));
    }
}
