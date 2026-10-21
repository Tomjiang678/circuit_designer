using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Comparators.Models;

namespace CircuitModuleDesigner.Modules.Comparators.Validation;

public static class ComparatorRequestValidator
{
    public static IReadOnlyList<string> Validate(ComparatorDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        var values = new Dictionary<string, double>
        {
            ["lower threshold"] = request.LowerThresholdVoltage, ["upper threshold"] = request.UpperThresholdVoltage,
            ["VCC"] = request.SupplyHighVoltage, ["VEE/GND"] = request.SupplyLowVoltage,
            ["output high"] = request.OutputHighVoltage, ["output low"] = request.OutputLowVoltage,
            ["minimum resistance"] = request.MinimumResistanceOhm, ["maximum resistance"] = request.MaximumResistanceOhm,
            ["lower boundary hysteresis"] = request.LowerBoundaryHysteresisVoltage, ["upper boundary hysteresis"] = request.UpperBoundaryHysteresisVoltage,
            ["target frequency"] = request.TargetFrequencyHz, ["duty cycle"] = request.DutyCyclePercent
        };
        foreach (var pair in values) if (!double.IsFinite(pair.Value)) errors.Add($"{pair.Key} must be a finite number (not NaN or Infinity).");
        if (errors.Count > 0) return errors;
        if (request.Kind != ComparatorDesignerKind.RelaxationOscillator && request.LowerThresholdVoltage > request.UpperThresholdVoltage)
            errors.Add("Lower Threshold must not exceed Upper Threshold.");
        if (request.Kind == ComparatorDesignerKind.WindowComparator && request.LowerThresholdVoltage == request.UpperThresholdVoltage)
            errors.Add("Window Comparator requires Lower Threshold to be strictly less than Upper Threshold.");
        if (request.SupplyLowVoltage >= request.SupplyHighVoltage) errors.Add("VEE/GND must be lower than VCC.");
        if (request.OutputLowVoltage >= request.OutputHighVoltage) errors.Add("Output low must be lower than output high.");
        if (request.OutputLowVoltage < request.SupplyLowVoltage || request.OutputHighVoltage > request.SupplyHighVoltage) errors.Add("Output levels must lie inside the selected supply rails.");
        if (request.Kind != ComparatorDesignerKind.RelaxationOscillator &&
            (request.LowerThresholdVoltage < request.SupplyLowVoltage || request.UpperThresholdVoltage > request.SupplyHighVoltage))
            errors.Add("Thresholds must lie inside the selected supply range.");
        if (!(request.MinimumResistanceOhm > 0) || request.MaximumResistanceOhm < request.MinimumResistanceOhm) errors.Add("Resistance range must be positive and ordered.");
        if (request.LowerBoundaryHysteresisVoltage < 0 || request.UpperBoundaryHysteresisVoltage < 0) errors.Add("Window boundary hysteresis cannot be negative.");
        var outputSpan = request.OutputHighVoltage - request.OutputLowVoltage;
        if (request.Kind == ComparatorDesignerKind.SchmittTrigger && request.UpperThresholdVoltage - request.LowerThresholdVoltage >= outputSpan)
            errors.Add("Schmitt hysteresis must be smaller than the available output swing.");
        if (request.Kind == ComparatorDesignerKind.WindowComparator)
        {
            if (request.LowerBoundaryHysteresisVoltage >= outputSpan || request.UpperBoundaryHysteresisVoltage >= outputSpan)
                errors.Add("Each boundary hysteresis must be smaller than the available output swing.");
            var lowerRise = request.LowerThresholdVoltage + request.LowerBoundaryHysteresisVoltage / 2;
            var upperFall = request.UpperThresholdVoltage - request.UpperBoundaryHysteresisVoltage / 2;
            if (lowerRise >= upperFall) errors.Add("Boundary hysteresis consumes the window: lower rising threshold must remain below upper falling threshold.");
        }
        if(request.Kind==ComparatorDesignerKind.RelaxationOscillator)
        {
            if(!(request.TargetFrequencyHz>0))errors.Add("Target frequency must be positive.");
            if(request.DutyCyclePercent is <20 or >80)errors.Add("Duty cycle must be between 20% and 80%.");
            if(request.OutputBehavior!=ComparatorOutputBehavior.PushPull)errors.Add("The relaxation oscillator currently requires a push-pull comparator output.");
        }
        return errors;
    }
}
