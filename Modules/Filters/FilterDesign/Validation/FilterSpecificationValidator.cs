using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;

public static class FilterSpecificationValidator
{
    public static IReadOnlyList<string> Validate(FilterDesignState state)
    {
        var errors = new List<string>();
        if (state.PassbandRippleDb < 0) errors.Add("Rp must not be negative.");
        if (state.StopbandAttenuationDb < 0) errors.Add("As/Asb must not be negative.");
        if (state.Mode == FilterMode.Active && state.PassbandGain <= 0) errors.Add("A0 must be greater than zero.");
        if (state.OrderMode == FilterOrderMode.SpecifyOrder && state.SpecifiedOrder < 1) errors.Add("Order must be a positive integer.");
        if (state.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass)
        {
            if (state.PassbandEdgeHz <= 0 || state.StopbandEdgeHz <= 0) errors.Add("Frequencies must be greater than zero.");
            if (state.ResponseType == FilterResponseType.LowPass && state.PassbandEdgeHz >= state.StopbandEdgeHz) errors.Add("Low-pass requires fp < fs.");
            if (state.ResponseType == FilterResponseType.HighPass && state.StopbandEdgeHz >= state.PassbandEdgeHz) errors.Add("High-pass requires fs < fp.");
        }
        else
        {
            if (state.BandInputMode == BandFrequencyInputMode.CenterAndBandwidth)
            {
                if (state.CenterFrequencyHz <= 0 || state.PassbandBandwidthHz <= 0 || state.StopbandBandwidthHz <= 0) errors.Add("Center frequency and bandwidths must be greater than zero.");
                else if (state.ResponseType == FilterResponseType.BandPass && state.StopbandBandwidthHz <= state.PassbandBandwidthHz) errors.Add("Band-pass requires BWs > BWp.");
                else if (state.ResponseType == FilterResponseType.BandStop && state.PassbandBandwidthHz <= state.StopbandBandwidthHz) errors.Add("Band-stop requires BWp > BWs.");
                else if (state.CenterFrequencyHz - Math.Max(state.PassbandBandwidthHz, state.StopbandBandwidthHz) / 2 <= 0) errors.Add("The lower band edge must be greater than zero.");
            }
            else if (state.StopbandLowerHz <= 0 || state.PassbandLowerHz <= 0 || state.PassbandUpperHz <= 0 || state.StopbandUpperHz <= 0)
                errors.Add("Frequencies must be greater than zero.");
            else if (state.ResponseType == FilterResponseType.BandPass && !(state.StopbandLowerHz < state.PassbandLowerHz && state.PassbandLowerHz < state.PassbandUpperHz && state.PassbandUpperHz < state.StopbandUpperHz))
                errors.Add("Band-pass requires fs1 < fp1 < fp2 < fs2.");
            else if (state.ResponseType == FilterResponseType.BandStop && !(state.PassbandLowerHz < state.StopbandLowerHz && state.StopbandLowerHz < state.StopbandUpperHz && state.StopbandUpperHz < state.PassbandUpperHz))
                errors.Add("Band-stop requires fp1 < fs1 < fs2 < fp2.");
        }
        return errors;
    }
}
