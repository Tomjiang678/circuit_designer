using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Engines;

public sealed class PrecisionAnalogDesignEngine
{
    private readonly AffineNetworkSynthesizer affine = new();
    private readonly AffineDesignVerifier verifier = new();

    public AnalogComputeCapabilityEvaluation EvaluateCapability(AnalogComputeDesignRequest request)
    {
        if (request.SupplyVccVoltage <= 0 || request.SupplyVeeVoltage >= 0)
            return AnalogComputeCapabilityEvaluation.Failure(AnalogCapabilityReasonCode.SupplyRailsInvalid, "VCC must be greater than 0 V and VEE must be less than 0 V.");
        if (request.Operation is AnalogComputeOperationType.GeneralLinearCombination or AnalogComputeOperationType.VoltageRangeMapper)
            return AnalogComputeCapabilityEvaluation.Supported();
        return AnalogComputeCapabilityEvaluation.Failure(
            AnalogCapabilityReasonCode.OperationNotImplemented,
            $"{DisplayName(request.Operation)} is present in the v1 plan but has not yet passed its operation-specific CircuitGraph verification.",
            "Use General Linear Combination or Voltage Range Mapper while this operation remains unavailable.");
    }

    public AnalogComputeDesignResult Design(AnalogComputeDesignRequest request)
    {
        var capability = EvaluateCapability(request);
        if (!capability.IsSupported) return Failure(request, capability);
        try
        {
            var effective = request.Operation == AnalogComputeOperationType.VoltageRangeMapper ? ToAffineRequest(request) : request;
            var synthesis = affine.Synthesize(effective);
            var verification = verifier.Verify(effective, synthesis.Circuit);
            var failed = verification.Metrics.Where(metric => !metric.Pass).ToArray();
            if (failed.Length > 0)
            {
                var swingFailure = failed.Any(metric => metric.Name.Contains("swing", StringComparison.OrdinalIgnoreCase));
                var details = string.Join("; ", failed.Take(4).Select(metric => $"{metric.Name}: target {metric.Target:G6}, actual {metric.Actual:G6}, allowed ±{metric.Tolerance:G4}"));
                var standardValueAlternative = effective.Components.ResistorSeries == AnalogStandardSeries.E12
                    ? "E12 has only 12 nominal values per decade. Increase Allowed Function Error or select E24/E48/E96."
                    : "Select a denser E-series, increase the error allowance, or widen the resistor range.";
                var failure = AnalogComputeCapabilityEvaluation.Failure(
                    swingFailure ? AnalogCapabilityReasonCode.OutputSwingInsufficient : AnalogCapabilityReasonCode.StandardValueAccuracyInsufficient,
                    $"The best selected-value CircuitGraph did not meet all requested tolerances. {details}",
                    swingFailure ? "Increase VCC/VEE range, reduce the requested output range, or enter practical output headroom." : standardValueAlternative);
                return new()
                {
                    Success = false, Capability = failure, Request = effective, TopologyName = synthesis.Topology,
                    TargetRelation = Relation(effective), CircuitGraph = synthesis.Circuit, Components = synthesis.Components,
                    VerificationMetrics = verification.Metrics, Calculations = synthesis.Calculations, Warnings = verification.Warnings,
                    Analysis = verification.Analysis, RequiredVccVoltage = effective.SupplyVccVoltage, RequiredVeeVoltage = effective.SupplyVeeVoltage
                };
            }
            var calculations = synthesis.Calculations.ToList();
            if (request.Operation == AnalogComputeOperationType.VoltageRangeMapper)
            {
                var scale = effective.Inputs[0].Coefficient;
                calculations.Insert(0, new("Range mapping", "a=(VoutH−VoutL)/(VinH−VinL); b=VoutL−a·VinL", $"a={scale:G9}, b={effective.OutputOffsetVoltage:G9} V"));
            }
            return new()
            {
                Success = true, Capability = AnalogComputeCapabilityEvaluation.Supported("Selected-value CircuitGraph passed independent DC sensitivity extraction."),
                Request = effective, TopologyName = synthesis.Topology, TargetRelation = Relation(effective), CircuitGraph = synthesis.Circuit,
                Components = synthesis.Components, VerificationMetrics = verification.Metrics, Calculations = calculations,
                Warnings = verification.Warnings, Analysis = verification.Analysis,
                RequiredVccVoltage = effective.SupplyVccVoltage, RequiredVeeVoltage = effective.SupplyVeeVoltage
            };
        }
        catch (ArgumentException exception)
        {
            return Failure(request, AnalogComputeCapabilityEvaluation.Failure(AnalogCapabilityReasonCode.InvalidInput, exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return Failure(request, AnalogComputeCapabilityEvaluation.Failure(AnalogCapabilityReasonCode.ComponentRangeImpossible, exception.Message, "Widen the component range or use a less extreme coefficient."));
        }
    }

    private static AnalogComputeDesignRequest ToAffineRequest(AnalogComputeDesignRequest request)
    {
        var deltaInput = request.InputHighVoltage - request.InputLowVoltage;
        if (!double.IsFinite(deltaInput) || Math.Abs(deltaInput) < 1e-15) throw new ArgumentException("Range Mapper input endpoints must be different.");
        var scale = (request.OutputHighVoltage - request.OutputLowVoltage) / deltaInput;
        var offset = request.OutputLowVoltage - scale * request.InputLowVoltage;
        var minimum = Math.Min(request.InputLowVoltage, request.InputHighVoltage);
        var maximum = Math.Max(request.InputLowVoltage, request.InputHighVoltage);
        return request with
        {
            Inputs = [new AnalogInputTerm("VIN", scale, minimum, maximum, (minimum + maximum) / 2)],
            OutputOffsetVoltage = offset
        };
    }

    private static string Relation(AnalogComputeDesignRequest request) =>
        $"Vout = {request.OutputOffsetVoltage:G6} V" + string.Concat(request.Inputs.Select(input => $" {(input.Coefficient < 0 ? "−" : "+")} {Math.Abs(input.Coefficient):G6}·{input.Name}"));

    private static AnalogComputeDesignResult Failure(AnalogComputeDesignRequest request, AnalogComputeCapabilityEvaluation capability) =>
        new() { Success = false, Capability = capability, Request = request, RequiredVccVoltage = request.SupplyVccVoltage, RequiredVeeVoltage = request.SupplyVeeVoltage };

    public static string DisplayName(AnalogComputeOperationType operation) => operation switch
    {
        AnalogComputeOperationType.GeneralLinearCombination => "Linear Combination",
        AnalogComputeOperationType.VoltageRangeMapper => "Range Mapper",
        AnalogComputeOperationType.PrecisionHalfWaveRectifier => "Half-Wave",
        AnalogComputeOperationType.PrecisionFullWaveRectifier => "Full-Wave",
        AnalogComputeOperationType.LogarithmicAmplifier => "Log",
        AnalogComputeOperationType.ExponentialAmplifier => "Antilog",
        AnalogComputeOperationType.PracticalIntegrator => "Integrator",
        AnalogComputeOperationType.PracticalDifferentiator => "Differentiator",
        AnalogComputeOperationType.PrecisionLimiter => "Limiter",
        _ => operation.ToString()
    };
}
