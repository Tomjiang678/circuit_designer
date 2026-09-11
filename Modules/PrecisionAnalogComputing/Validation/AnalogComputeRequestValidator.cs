using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

internal static class AnalogComputeRequestValidator
{
    public static AnalogComputeCapabilityEvaluation? Validate(AnalogComputeDesignRequest request)
    {
        if (!Enum.IsDefined(request.Operation)) return Failure(AnalogCapabilityReasonCode.TopologyCapabilityUnsupported, "No verified topology is registered for the requested operation.");
        if (!Finite(request.SupplyVccVoltage, request.SupplyVeeVoltage) || request.SupplyVccVoltage <= 0 || request.SupplyVeeVoltage >= 0 || request.SupplyVccVoltage <= request.SupplyVeeVoltage)
            return Failure(AnalogCapabilityReasonCode.SupplyRailsInvalid, "VCC and VEE must be finite, with VCC > 0 V and VEE < 0 V.");
        if (!Finite(request.MaximumFunctionErrorPercent) || request.MaximumFunctionErrorPercent <= 0)
            return Failure(AnalogCapabilityReasonCode.InvalidInput, "Maximum function error must be finite and greater than zero.");
        if(request.Components is not { } components)
            return Failure(AnalogCapabilityReasonCode.InvalidInput,"Component constraints are required.");
        if (!Enum.IsDefined(components.ResistorSeries) || !Enum.IsDefined(components.CapacitorSeries) ||
            !PositiveRange(components.ResistorMinimumOhm,components.ResistorMaximumOhm) ||
            !PositiveRange(components.CapacitorMinimumFarad,components.CapacitorMaximumFarad))
            return Failure(AnalogCapabilityReasonCode.InvalidInput, "Component series and positive finite R/C ranges must be valid and ordered.");
        if(request.OpAmp is not { } opAmp)
            return Failure(AnalogCapabilityReasonCode.InvalidInput,"Op-amp constraints are required.");
        if (!Finite(opAmp.OutputSwingHeadroomVoltage) || opAmp.OutputSwingHeadroomVoltage < 0 ||
            opAmp.OutputSwingHeadroomVoltage >= Math.Min(request.SupplyVccVoltage,-request.SupplyVeeVoltage))
            return Failure(AnalogCapabilityReasonCode.SupplyRailsInvalid, "Output-swing headroom must be finite, nonnegative, and smaller than both supply magnitudes.");
        if (!opAmp.UseIdealOpAmp && (!Positive(opAmp.GainBandwidthHz) || !Positive(opAmp.SlewRateVoltPerSecond)))
            return Failure(AnalogCapabilityReasonCode.InvalidInput, "A practical op-amp requires finite positive GBW and slew rate.");

        return request.Operation switch
        {
            AnalogComputeOperationType.GeneralLinearCombination => ValidateAffine(request),
            AnalogComputeOperationType.VoltageRangeMapper => ValidateMapper(request),
            AnalogComputeOperationType.PrecisionHalfWaveRectifier or AnalogComputeOperationType.PrecisionFullWaveRectifier => ValidatePiecewise(request),
            AnalogComputeOperationType.LogarithmicAmplifier or AnalogComputeOperationType.ExponentialAmplifier => ValidateJunction(request),
            AnalogComputeOperationType.PracticalIntegrator =>
                ValidInputRange(request) && Positive(request.IntegrationConstantPerSecond) && Positive(request.LowerCornerHz) && Positive(request.UpperCornerHz) && request.UpperCornerHz>request.LowerCornerHz
                    ? null : Failure(AnalogCapabilityReasonCode.InvalidInput,"Integrator Ki and verification corners must be finite and positive, with upper corner above leak fL."),
            AnalogComputeOperationType.PracticalDifferentiator =>
                ValidInputRange(request) && Positive(request.DifferentiationConstantSecond) &&
                ((Positive(request.DifferentiationUsefulUpperFrequencyHz)) ||
                 (Positive(request.LowerCornerHz) && Positive(request.UpperCornerHz) && request.UpperCornerHz>request.LowerCornerHz))
                    ? null : Failure(AnalogCapabilityReasonCode.InvalidInput,"Differentiator Kd and useful frequency (or both limiting corners) must be finite and positive."),
            _ => Failure(AnalogCapabilityReasonCode.TopologyCapabilityUnsupported,"No verified topology is registered for the requested operation.")
        };
    }

    private static AnalogComputeCapabilityEvaluation? ValidateAffine(AnalogComputeDesignRequest request)
    {
        if (!Finite(request.OutputOffsetVoltage) || request.Inputs is null || request.Inputs.Count is < 1 or > 8)
            return Failure(AnalogCapabilityReasonCode.InvalidInput,"Linear combination requires a finite offset and 1 to 8 inputs.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var input in request.Inputs)
            if(input is null||string.IsNullOrWhiteSpace(input.Name)||!names.Add(input.Name)||!Finite(input.Coefficient,input.MinimumVoltage,input.MaximumVoltage,input.NominalVoltage)||input.MaximumVoltage<=input.MinimumVoltage||input.NominalVoltage<input.MinimumVoltage||input.NominalVoltage>input.MaximumVoltage)
                return Failure(AnalogCapabilityReasonCode.InvalidInput,"Every linear input needs a unique name, finite coefficient/voltages, and an ordered input range.");
        if(request.Inputs.All(input=>input.Coefficient==0)&&request.OutputOffsetVoltage==0)
            return Failure(AnalogCapabilityReasonCode.InvalidInput,"At least one coefficient or the output offset must be non-zero.");
        return null;
    }

    private static AnalogComputeCapabilityEvaluation? ValidateMapper(AnalogComputeDesignRequest request) =>
        Finite(request.InputLowVoltage,request.InputHighVoltage,request.OutputLowVoltage,request.OutputHighVoltage) && request.InputHighVoltage!=request.InputLowVoltage && request.OutputHighVoltage!=request.OutputLowVoltage
            ? null : Failure(AnalogCapabilityReasonCode.InvalidInput,"Range Mapper endpoints must be finite and both input and output endpoint pairs must differ.");

    private static AnalogComputeCapabilityEvaluation? ValidatePiecewise(AnalogComputeDesignRequest request) =>
        Enum.IsDefined(request.HalfWavePolarity) && Enum.IsDefined(request.OutputPolarity) && Positive(request.GainK) && Finite(request.MinimumInputVoltage,request.MaximumInputVoltage,request.MaximumExpectedFrequencyHz) &&
        request.MaximumInputVoltage>request.MinimumInputVoltage && request.MaximumExpectedFrequencyHz>0
            ? null : Failure(AnalogCapabilityReasonCode.InvalidInput,"Rectifier gain/frequency must be finite and positive, and Vin Max must exceed Vin Min.");

    private static AnalogComputeCapabilityEvaluation? ValidateJunction(AnalogComputeDesignRequest request)
    {
        if(request.Junction is not { } junction)
            return Failure(AnalogCapabilityReasonCode.InvalidInput,"Junction model parameters are required.");
        if(junction.Behavior!=DiodeBehaviorModel.ShockleyJunction || !Positive(junction.SaturationCurrentAmpere) || !Positive(junction.EmissionCoefficient) ||
           !Finite(junction.TemperatureCelsius) || junction.TemperatureCelsius<=-273.15 || !Positive(request.ReferenceVoltage) || request.ReferenceVoltage>=request.SupplyVccVoltage ||
           !Positive(request.LogScaleVoltage) || !Finite(request.MinimumInputVoltage,request.MaximumInputVoltage,request.MaximumJunctionCurrentAmpere) ||
           request.MaximumInputVoltage<=request.MinimumInputVoltage || request.MaximumJunctionCurrentAmpere<=0)
            return Failure(AnalogCapabilityReasonCode.InvalidInput,"Log/Antilog requires a valid Shockley junction, positive scale/current limit, Vref inside (0,VCC), and a finite ordered input range.");
        if(request.Operation==AnalogComputeOperationType.LogarithmicAmplifier && request.MinimumInputVoltage<=0)
            return Failure(AnalogCapabilityReasonCode.InvalidInput,"Log amplifier requires Vin Min > 0.");
        if(request.Operation==AnalogComputeOperationType.ExponentialAmplifier &&
           Math.Max(Math.Abs(request.MinimumInputVoltage/request.LogScaleVoltage),Math.Abs(request.MaximumInputVoltage/request.LogScaleVoltage))>80)
            return Failure(AnalogCapabilityReasonCode.InvalidInput,"Antilog exponent range must satisfy |Vin/S| <= 80.");
        return null;
    }

    private static bool PositiveRange(double minimum,double maximum)=>Positive(minimum)&&Positive(maximum)&&maximum>=minimum;
    private static bool ValidInputRange(AnalogComputeDesignRequest request)=>Finite(request.MinimumInputVoltage,request.MaximumInputVoltage)&&request.MaximumInputVoltage>request.MinimumInputVoltage;
    private static bool Positive(double value)=>double.IsFinite(value)&&value>0;
    private static bool Finite(params double[] values)=>values.All(double.IsFinite);
    private static AnalogComputeCapabilityEvaluation Failure(AnalogCapabilityReasonCode reason,string message)=>AnalogComputeCapabilityEvaluation.Failure(reason,message);
}
