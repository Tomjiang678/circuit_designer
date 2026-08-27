using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;

public sealed class NumericalPrototypeOrderSearch
{
    public int FindMinimumOrder(
        double rp,
        double attenuation,
        double stopbandRatio,
        IEnumerable<int> candidateOrders,
        Func<int, FilterPrototype> prototypeFactory,
        out double lastAttenuationDb)
    {
        if (rp <= 0 || attenuation <= rp || stopbandRatio <= 1)
            throw new ArgumentException("Numerical order search requires Rp > 0, As > Rp and normalized stopband ratio > 1.");
        lastAttenuationDb = 0;
        foreach (var order in candidateOrders.Order())
        {
            var prototype = prototypeFactory(order);
            var passbandPoint = PrototypeResponseEvaluator.FindFrequencyForAttenuation(prototype, rp);
            lastAttenuationDb = PrototypeResponseEvaluator.AttenuationDb(prototype, passbandPoint * stopbandRatio);
            if (lastAttenuationDb >= attenuation) return order;
        }
        return 0;
    }

    public double CalculateCutoffAngularFrequency(
        double passbandAngularFrequency,
        double rp,
        FilterPrototype prototype) =>
        passbandAngularFrequency / PrototypeResponseEvaluator.FindFrequencyForAttenuation(prototype, rp);
}
