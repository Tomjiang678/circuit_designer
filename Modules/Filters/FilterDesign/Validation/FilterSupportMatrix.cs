using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;

public sealed record FilterSupportDecision(bool IsSupported, string Message)
{
    public static FilterSupportDecision Supported(string message) => new(true, message);
    public static FilterSupportDecision Unsupported(string message) => new(false, message);
}

public static class FilterSupportMatrix
{
    public static IReadOnlyList<FilterImplementationType> GetSupportedImplementations(FilterDesignRequest request)
    {
        var candidates=request.Mode==FilterMode.Active
            ? new[]{FilterImplementationType.SallenKey,FilterImplementationType.MultipleFeedback,FilterImplementationType.BainterNotch}
            : new[]{FilterImplementationType.Rc,FilterImplementationType.Lc};
        return candidates.Where(implementation=>Evaluate(request with { Implementation=implementation }).IsSupported).ToArray();
    }

    public static FilterSupportDecision Evaluate(FilterDesignRequest request)
    {
        if (request.SignalArchitecture == FilterSignalArchitecture.FullyDifferential)
            return FilterSupportDecision.Unsupported("Fully Differential synthesis is not implemented yet.");
        if (request.Mode == FilterMode.Active)
        {
            if (request.ResponseType == FilterResponseType.BandStop)
                return request.Implementation == FilterImplementationType.BainterNotch
                    ? FilterSupportDecision.Supported("Band-Stop is realized as cascaded direct Bainter notch biquads; each section is verified from its final R/C values.")
                    : FilterSupportDecision.Unsupported("Active Band-Stop requires the direct Bainter Notch implementation. Sallen-Key and MFB are not used as composite LP/HP approximations.");
            if (request.Implementation == FilterImplementationType.BainterNotch)
                return FilterSupportDecision.Unsupported("Bainter Notch is only applicable to Active Band-Stop designs.");
            if (request.Implementation == FilterImplementationType.MultipleFeedback)
            {
                return FilterSupportDecision.Supported("MFB Low-Pass, High-Pass and Band-Pass synthesis is supported.");
            }
            if (request.Implementation != FilterImplementationType.SallenKey)
                return FilterSupportDecision.Unsupported("The selected active implementation is not supported.");
            return FilterSupportDecision.Supported("Sallen-Key LP/HP/BP synthesis is supported; BP uses Q-coupled gain with buffered output normalization.");
        }
        if (request.Implementation == FilterImplementationType.Rc)
        {
            if (request.Approximation is FilterApproximation.GaussianTo6Db or FilterApproximation.GaussianTo12Db or FilterApproximation.LinearPhase005Degree or FilterApproximation.LinearPhase05Degree)
                return FilterSupportDecision.Unsupported($"{request.Approximation} has no published first-order prototype and is not labeled as a passive RC response.");
            if (request.ResponseType is not (FilterResponseType.LowPass or FilterResponseType.HighPass))
                return FilterSupportDecision.Unsupported("Passive RC is limited to first-order Low-Pass or High-Pass.");
            return FilterSupportDecision.Supported("First-order passive RC synthesis is supported when the required/selected order is 1.");
        }
        if (request.Implementation == FilterImplementationType.Lc)
        {
            if (request.Approximation is not (FilterApproximation.Butterworth or FilterApproximation.ChebyshevTypeI))
                return FilterSupportDecision.Unsupported($"{request.Approximation} is an all-pole active-filter table and does not define the doubly terminated LC ladder g-values used by this synthesizer.");
            return FilterSupportDecision.Supported("Butterworth and Chebyshev-I LC ladder synthesis supports Low-Pass, High-Pass, Band-Pass and Band-Stop.");
        }
        return FilterSupportDecision.Unsupported("The selected implementation is not supported.");
    }
}
