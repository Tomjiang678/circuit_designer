using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;

public sealed class FilterSpecificationNormalizer
{
    private readonly IReadOnlyDictionary<FilterApproximation, IFilterOrderCalculator> calculators;

    public FilterSpecificationNormalizer(IEnumerable<IFilterOrderCalculator> calculators)
    {
        this.calculators = calculators.ToDictionary(item => item.Approximation);
    }

    public NormalizedFilterSpecification Normalize(FilterDesignRequest request)
    {
        if (!calculators.TryGetValue(request.Approximation, out var calculator))
            throw new NotSupportedException($"No order calculator is registered for {request.Approximation}.");
        if (request.ResponseType is FilterResponseType.BandPass or FilterResponseType.BandStop)
            return NormalizeBand(request, calculator);
        var wp = 2 * Math.PI * request.PassbandEdgeHz;
        var ws = 2 * Math.PI * request.StopbandEdgeHz;
        var ratio = request.ResponseType == FilterResponseType.LowPass ? ws / wp : wp / ws;
        var (required, searchWarning) = CalculateRequiredOrder(request, calculator, ratio);
        var implemented = request.OrderMode == FilterOrderMode.SpecifyOrder ? request.SpecifiedOrder : required;
        var meets = implemented >= required;
        var warning = meets ? searchWarning : searchWarning ?? $"Specified order {implemented} is below the required minimum order {required}; the result will not meet the requested attenuation.";
        return new NormalizedFilterSpecification(
            request.ResponseType,
            request.Approximation,
            wp,
            ws,
            ratio,
            request.PassbandRippleDb,
            request.StopbandAttenuationDb,
            required,
            implemented,
            calculator.CalculateCutoffAngularFrequency(wp, request.PassbandRippleDb, implemented),
            meets,
            warning);
    }

    private static NormalizedFilterSpecification NormalizeBand(FilterDesignRequest request, IFilterOrderCalculator calculator)
    {
        ValidateBandEdges(request);
        var wp1 = 2 * Math.PI * request.PassbandLowerHz;
        var wp2 = 2 * Math.PI * request.PassbandUpperHz;
        var ws1 = 2 * Math.PI * request.StopbandLowerHz;
        var ws2 = 2 * Math.PI * request.StopbandUpperHz;
        var center = Math.Sqrt(wp1 * wp2);
        var bandwidth = wp2 - wp1;
        var lowerRatio = MapBandStopFrequency(request.ResponseType, ws1, center, bandwidth);
        var upperRatio = MapBandStopFrequency(request.ResponseType, ws2, center, bandwidth);
        var ratio = Math.Min(lowerRatio, upperRatio);
        if (!double.IsFinite(ratio) || ratio <= 1)
            throw new ArgumentException("The band stop edges must map outside the normalized passband (Ωs > 1).");
        var (required, searchWarning) = CalculateRequiredOrder(request, calculator, ratio);
        var implemented = request.OrderMode == FilterOrderMode.SpecifyOrder ? request.SpecifiedOrder : required;
        var meets = implemented >= required;
        var warning = meets ? searchWarning : searchWarning ?? $"Specified prototype order {implemented} is below the required minimum order {required}; the result will not meet the requested attenuation.";
        return new NormalizedFilterSpecification(
            request.ResponseType, request.Approximation, 1, ratio, ratio,
            request.PassbandRippleDb, request.StopbandAttenuationDb,
            required, implemented, 1, meets, warning, center, bandwidth);
    }

    private static (int RequiredOrder,string? Warning) CalculateRequiredOrder(FilterDesignRequest request,IFilterOrderCalculator calculator,double ratio)
    {
        try{return(calculator.CalculateMinimumOrder(request.PassbandRippleDb,request.StopbandAttenuationDb,ratio),null);}
        catch(NotSupportedException exception) when(request.OrderMode==FilterOrderMode.SpecifyOrder)
        {return(11,$"{exception.Message} The manually specified order {request.SpecifiedOrder} will be generated but cannot meet the requested attenuation.");}
    }

    private static double MapBandStopFrequency(FilterResponseType response, double frequency, double center, double bandwidth)
    {
        var denominator = Math.Abs(frequency * frequency - center * center);
        return response == FilterResponseType.BandPass
            ? denominator / (bandwidth * frequency)
            : bandwidth * frequency / denominator;
    }

    private static void ValidateBandEdges(FilterDesignRequest request)
    {
        var allPositive = request.StopbandLowerHz > 0 && request.PassbandLowerHz > 0 && request.PassbandUpperHz > 0 && request.StopbandUpperHz > 0;
        if (!allPositive) throw new ArgumentException("All band-edge frequencies must be positive.");
        if (request.ResponseType == FilterResponseType.BandPass &&
            !(request.StopbandLowerHz < request.PassbandLowerHz && request.PassbandLowerHz < request.PassbandUpperHz && request.PassbandUpperHz < request.StopbandUpperHz))
            throw new ArgumentException("Band-Pass requires fs1 < fp1 < fp2 < fs2.");
        if (request.ResponseType == FilterResponseType.BandStop &&
            !(request.PassbandLowerHz < request.StopbandLowerHz && request.StopbandLowerHz < request.StopbandUpperHz && request.StopbandUpperHz < request.PassbandUpperHz))
            throw new ArgumentException("Band-Stop requires fp1 < fs1 < fs2 < fp2.");
    }
}
