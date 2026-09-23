using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Engines;

public sealed class PrecisionAnalogDesignEngine
{
    private readonly AffineNetworkSynthesizer affine = new();
    private readonly AffineDesignVerifier affineVerifier = new();
    private readonly DynamicOperationSynthesizer dynamic = new();
    private readonly DynamicOperationVerifier dynamicVerifier = new();
    private readonly PiecewiseOperationSynthesizer piecewise = new();
    private readonly PiecewiseOperationVerifier piecewiseVerifier = new();
    private readonly JunctionOperationSynthesizer junction = new();
    private readonly JunctionOperationVerifier junctionVerifier = new();

    public AnalogComputeCapabilityEvaluation EvaluateCapability(AnalogComputeDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return AnalogComputeRequestValidator.Validate(request) ?? AnalogComputeCapabilityEvaluation.Supported();
    }

    public AnalogComputeDesignResult Design(AnalogComputeDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var capability = EvaluateCapability(request);
        if (!capability.IsSupported) return Failure(request, capability, AnalogDesignFailureStage.SpecificationValidation);
        var effective = request.Operation == AnalogComputeOperationType.VoltageRangeMapper ? ToAffineRequest(request) : request;
        OperationSynthesisResult synthesis;
        try
        {
            synthesis = Synthesize(effective);
        }
        catch(ArgumentException exception)
        {
            return Failure(request,AnalogComputeCapabilityEvaluation.Failure(AnalogCapabilityReasonCode.InvalidInput,exception.Message),AnalogDesignFailureStage.SpecificationValidation);
        }
        catch(NotSupportedException exception)
        {
            return Failure(request,AnalogComputeCapabilityEvaluation.Failure(AnalogCapabilityReasonCode.TopologyCapabilityUnsupported,exception.Message),AnalogDesignFailureStage.Synthesis);
        }
        catch(InvalidOperationException exception)
        {
            var reason=exception.Message.Contains("junction current",StringComparison.OrdinalIgnoreCase)
                ? AnalogCapabilityReasonCode.JunctionCurrentOutOfRange
                : AnalogCapabilityReasonCode.ComponentRangeImpossible;
            return Failure(request,AnalogComputeCapabilityEvaluation.Failure(reason,exception.Message,
                "Widen the component range or reduce the requested gain/frequency/dynamic range."),AnalogDesignFailureStage.Synthesis);
        }
        catch(Exception exception)
        {
            return Failure(request,AnalogComputeCapabilityEvaluation.Failure(AnalogCapabilityReasonCode.InternalError,
                $"Unexpected synthesis error: {exception.Message}"),AnalogDesignFailureStage.Synthesis);
        }

        try
        {
            var verification = Verify(effective, synthesis);
            var failed = verification.Metrics.Where(metric => !metric.Pass).ToArray();
            if (failed.Length > 0)
            {
                var reason = FailureReason(failed, effective);
                var details = string.Join("; ", failed.Take(4).Select(metric =>
                    $"{metric.Name}: target {metric.Target:G6}, actual {metric.Actual:G6}, tolerance {metric.Tolerance:G4}"));
                var alternative = reason switch
                {
                    AnalogCapabilityReasonCode.GbBandwidthInsufficient => "Use a faster abstract op-amp constraint or reduce the operating band/noise gain.",
                    AnalogCapabilityReasonCode.SlewRateInsufficient => "Increase slew-rate capability or reduce frequency/output amplitude.",
                    AnalogCapabilityReasonCode.OperatingEnvelopeExceeded => "Reduce the declared input/output envelope or provide supply rails and output headroom that cover the complete requested range.",
                    AnalogCapabilityReasonCode.JunctionCurrentOutOfRange => "Reduce the exponential range, increase resistance, or raise the allowed junction current.",
                    _ => effective.Components.ResistorSeries == AnalogStandardSeries.E24
                        ? "Use E48/E96, widen component ranges, or relax the function-error limit."
                        : "Use a denser E-series, wider component ranges, or relax the function-error limit."
                };
                return new()
                {
                    Success=false,
                    Capability=AnalogComputeCapabilityEvaluation.Failure(reason,
                        $"The selected-value CircuitGraph did not pass independent verification. {details}",alternative),
                    Request=effective,TopologyName=synthesis.Topology,TargetRelation=synthesis.TargetRelation,
                    FailureStage=AnalogDesignFailureStage.IndependentVerification,
                    CircuitGraph=synthesis.Circuit,Components=synthesis.Components,VerificationMetrics=verification.Metrics,
                    Calculations=synthesis.Calculations,Warnings=verification.Warnings,Analysis=verification.Analysis,
                    RequiredVccVoltage=effective.SupplyVccVoltage,RequiredVeeVoltage=effective.SupplyVeeVoltage
                };
            }
            var calculations=synthesis.Calculations.ToList();
            if(request.Operation==AnalogComputeOperationType.VoltageRangeMapper)
            {
                var scale=effective.Inputs[0].Coefficient;
                calculations.Insert(0,new("Range mapping","a=(VoutH-VoutL)/(VinH-VinL); b=VoutL-a*VinL",
                    $"a={scale:G9}, b={effective.OutputOffsetVoltage:G9} V"));
            }
            return new()
            {
                Success=true,
                Capability=AnalogComputeCapabilityEvaluation.Supported("Selected-value CircuitGraph passed its independent operation-specific verifier."),
                Request=effective,TopologyName=synthesis.Topology,TargetRelation=synthesis.TargetRelation,
                FailureStage=AnalogDesignFailureStage.None,
                CircuitGraph=synthesis.Circuit,Components=synthesis.Components,VerificationMetrics=verification.Metrics,
                Calculations=calculations,Warnings=verification.Warnings,Analysis=verification.Analysis,
                RequiredVccVoltage=effective.SupplyVccVoltage,RequiredVeeVoltage=effective.SupplyVeeVoltage
            };
        }
        catch(ArgumentException exception)
        {
            return Failure(request,AnalogComputeCapabilityEvaluation.Failure(AnalogCapabilityReasonCode.CircuitVerificationFailed,exception.Message),AnalogDesignFailureStage.IndependentVerification);
        }
        catch(NotSupportedException exception)
        {
            return Failure(request,AnalogComputeCapabilityEvaluation.Failure(AnalogCapabilityReasonCode.CircuitVerificationFailed,exception.Message),AnalogDesignFailureStage.IndependentVerification);
        }
        catch(InvalidOperationException exception)
        {
            return Failure(request,AnalogComputeCapabilityEvaluation.Failure(AnalogCapabilityReasonCode.CircuitVerificationFailed,exception.Message),AnalogDesignFailureStage.IndependentVerification);
        }
        catch(Exception exception)
        {
            return Failure(request,AnalogComputeCapabilityEvaluation.Failure(AnalogCapabilityReasonCode.InternalError,
                $"Unexpected independent-verification error: {exception.Message}"),AnalogDesignFailureStage.IndependentVerification);
        }
    }

    private OperationSynthesisResult Synthesize(AnalogComputeDesignRequest request)
    {
        if(request.Operation is AnalogComputeOperationType.GeneralLinearCombination or AnalogComputeOperationType.VoltageRangeMapper)
        {
            var value=affine.Synthesize(request);
            return new(value.Circuit,value.Components,value.Topology,Relation(request),value.Calculations);
        }
        return request.Operation switch
        {
            AnalogComputeOperationType.PracticalIntegrator or AnalogComputeOperationType.PracticalDifferentiator => dynamic.Synthesize(request),
            AnalogComputeOperationType.PrecisionHalfWaveRectifier or AnalogComputeOperationType.PrecisionFullWaveRectifier => piecewise.Synthesize(request),
            AnalogComputeOperationType.LogarithmicAmplifier or AnalogComputeOperationType.ExponentialAmplifier => junction.Synthesize(request),
            _ => throw new NotSupportedException($"No synthesizer is registered for {request.Operation}.")
        };
    }

    private (IReadOnlyList<VerificationMetric> Metrics,AnalogAnalysisResult Analysis,IReadOnlyList<string> Warnings) Verify(
        AnalogComputeDesignRequest request,OperationSynthesisResult synthesis) => request.Operation switch
    {
        AnalogComputeOperationType.GeneralLinearCombination or AnalogComputeOperationType.VoltageRangeMapper =>
            affineVerifier.Verify(request,synthesis.Circuit),
        AnalogComputeOperationType.PracticalIntegrator or AnalogComputeOperationType.PracticalDifferentiator =>
            dynamicVerifier.Verify(request,synthesis.Circuit,synthesis.Components),
        AnalogComputeOperationType.PrecisionHalfWaveRectifier or AnalogComputeOperationType.PrecisionFullWaveRectifier =>
            piecewiseVerifier.Verify(request,synthesis.Circuit),
        AnalogComputeOperationType.LogarithmicAmplifier or AnalogComputeOperationType.ExponentialAmplifier =>
            junctionVerifier.Verify(request,synthesis.Circuit),
        _ => throw new NotSupportedException($"No independent verifier is registered for {request.Operation}.")
    };

    private static AnalogCapabilityReasonCode FailureReason(IReadOnlyList<VerificationMetric> failed,AnalogComputeDesignRequest request)
    {
        if(failed.Any(metric=>metric.Name.Contains("GBW",StringComparison.OrdinalIgnoreCase)))return AnalogCapabilityReasonCode.GbBandwidthInsufficient;
        if(failed.Any(metric=>metric.Name.Contains("slew",StringComparison.OrdinalIgnoreCase)))return AnalogCapabilityReasonCode.SlewRateInsufficient;
        if(failed.Any(metric=>metric.Name.Contains("junction current",StringComparison.OrdinalIgnoreCase)))return AnalogCapabilityReasonCode.JunctionCurrentOutOfRange;
        if(failed.Any(metric=>metric.Name.Contains("Operating envelope",StringComparison.OrdinalIgnoreCase)))return AnalogCapabilityReasonCode.OperatingEnvelopeExceeded;
        return request.Components.OptimizeStandardValues?AnalogCapabilityReasonCode.StandardValueAccuracyInsufficient:AnalogCapabilityReasonCode.CircuitVerificationFailed;
    }

    private static AnalogComputeDesignRequest ToAffineRequest(AnalogComputeDesignRequest request)
    {
        var deltaInput=request.InputHighVoltage-request.InputLowVoltage;
        if(!double.IsFinite(deltaInput)||Math.Abs(deltaInput)<1e-15)throw new ArgumentException("Range Mapper input endpoints must be different.");
        var scale=(request.OutputHighVoltage-request.OutputLowVoltage)/deltaInput;
        var offset=request.OutputLowVoltage-scale*request.InputLowVoltage;
        var minimum=Math.Min(request.InputLowVoltage,request.InputHighVoltage);
        var maximum=Math.Max(request.InputLowVoltage,request.InputHighVoltage);
        return request with{Inputs=[new AnalogInputTerm("VIN",scale,minimum,maximum,(minimum+maximum)/2)],OutputOffsetVoltage=offset};
    }

    private static string Relation(AnalogComputeDesignRequest request)=>
        $"Vout = {request.OutputOffsetVoltage:G6} V"+string.Concat(request.Inputs.Select(input=>$" {(input.Coefficient<0?"-":"+")} {Math.Abs(input.Coefficient):G6}*{input.Name}"));

    private static AnalogComputeDesignResult Failure(AnalogComputeDesignRequest request,AnalogComputeCapabilityEvaluation capability,AnalogDesignFailureStage stage)=>
        new(){Success=false,Capability=capability,FailureStage=stage,Request=request,RequiredVccVoltage=request.SupplyVccVoltage,RequiredVeeVoltage=request.SupplyVeeVoltage};

    public static string DisplayName(AnalogComputeOperationType operation)=>operation switch
    {
        AnalogComputeOperationType.GeneralLinearCombination=>"Linear Combination",
        AnalogComputeOperationType.VoltageRangeMapper=>"Range Mapper",
        AnalogComputeOperationType.PrecisionHalfWaveRectifier=>"Half-Wave",
        AnalogComputeOperationType.PrecisionFullWaveRectifier=>"Full-Wave",
        AnalogComputeOperationType.LogarithmicAmplifier=>"Log",
        AnalogComputeOperationType.ExponentialAmplifier=>"Antilog",
        AnalogComputeOperationType.PracticalIntegrator=>"Integrator",
        AnalogComputeOperationType.PracticalDifferentiator=>"Differentiator",
        _=>operation.ToString()
    };
}
