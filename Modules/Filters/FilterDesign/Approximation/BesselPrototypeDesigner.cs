using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public sealed class BesselPrototypeDesigner : IFilterPrototypeDesigner
{
    public FilterApproximation Approximation => FilterApproximation.Bessel;
    public FilterPrototype Design(int order, double passbandRippleDb)
    {
        var transfer = new FilterPrototype(Approximation, order, BesselPrototypeMath.CreateMinus3DbPoles(order), Array.Empty<double>(),
            Normalization: new PrototypeNormalizationInfo(
                PrototypeNormalizationKind.Minus3DbMagnitude,
                "Reverse-Bessel polynomial, exact -3 dB normalization",
                "Roots of the reverse Bessel polynomial are frequency-scaled so the complete prototype has |H(j1)|=1/sqrt(2). The wizard subsequently scales it so Rp occurs at fp.",
                $"Numerical prototype-response search, orders 1-{BesselPrototypeMath.MaximumOrder}",
                "Reverse Bessel polynomial definition; compared with Analog Devices AN-649 Table VIII"),
            Description: "Bessel/Thomson response with maximally flat group delay at DC.");
        if (order is >= 1 and <= 6)
        {
            var ladder = new BesselLcPrototypeDesigner().Design(transfer, LcTerminationMode.DoublyTerminated);
            transfer = transfer with { LadderGValues = ladder.AsLegacyGValues(), LcLadderPrototype = ladder };
        }
        return transfer;
    }
}
