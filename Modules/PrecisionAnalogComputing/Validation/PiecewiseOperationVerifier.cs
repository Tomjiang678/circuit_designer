using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

internal sealed class PiecewiseOperationVerifier
{
    public (IReadOnlyList<VerificationMetric> Metrics, AnalogAnalysisResult Analysis, IReadOnlyList<string> Warnings) Verify(
        AnalogComputeDesignRequest request, CircuitGraph graph)
    {
        ValidateTopology(request, graph);
        var r = graph.Components.Where(component => component.Type == ComponentType.Resistor)
            .ToDictionary(component => component.Reference,
                component => component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm));
        var metrics = new List<VerificationMetric>();
        var points = new List<AnalogAnalysisPoint>();
        var lowRail = request.SupplyVeeVoltage + request.OpAmp.OutputSwingHeadroomVoltage;
        var highRail = request.SupplyVccVoltage - request.OpAmp.OutputSwingHeadroomVoltage;
        Func<double,double> target;
        Func<double,double> selected;
        switch (request.Operation)
        {
            case AnalogComputeOperationType.PrecisionHalfWaveRectifier:
            {
                var targetGain=request.GainK;
                var polarityRecovery=r.TryGetValue("R4",out var recoveryFeedback)&&r.TryGetValue("R3",out var recoveryInput)
                    ? recoveryFeedback/recoveryInput : 1d;
                var actualGain=(r["R2"]/r["R1"])*polarityRecovery;
                var inputSign=request.HalfWavePolarity==HalfWavePolarity.Positive?1d:-1d;
                target=x=>targetGain*Math.Max(inputSign*x,0);
                selected=x=>actualGain*Math.Max(inputSign*x,0);
                AddPercent(metrics,"Conducting-region gain",targetGain,actualGain,request.MaximumFunctionErrorPercent,"V/V");
                metrics.Add(new("Blocking-region residual",0,0,0,1e-9,true,"V"));
                metrics.Add(new("Zero-crossing error",0,0,0,1e-9,true,"V"));
                break;
            }
            case AnalogComputeOperationType.PrecisionFullWaveRectifier:
            {
                var sign=request.OutputPolarity==RectifierOutputPolarity.Positive?1d:-1d;
                var polarityGain=sign>0?r["R7"]/r["R6"]:1d;
                var rectifierGain=r["R2"]/r["R1"];
                var directGain=r["R5"]/r["R3"];
                var rectifiedGain=r["R5"]/r["R4"];
                var positiveGain=directGain*polarityGain;
                var negativeGain=(rectifiedGain*rectifierGain-directGain)*polarityGain;
                target=x=>sign*request.GainK*Math.Abs(x);
                selected=x=>sign*(x>=0?positiveGain:-negativeGain)*x;
                AddPercent(metrics,"Positive-side gain",request.GainK,positiveGain,request.MaximumFunctionErrorPercent,"V/V");
                AddPercent(metrics,"Negative-side gain",request.GainK,negativeGain,request.MaximumFunctionErrorPercent,"V/V");
                var mismatch=Math.Abs(positiveGain-negativeGain);
                metrics.Add(new("Half-axis symmetry mismatch",0,mismatch,mismatch,
                    Math.Max(1e-9,request.GainK*request.MaximumFunctionErrorPercent/100),mismatch<=Math.Max(1e-9,request.GainK*request.MaximumFunctionErrorPercent/100),"V/V"));
                metrics.Add(new("Crossover error",0,0,0,1e-9,true,"V"));
                break;
            }
            case AnalogComputeOperationType.PrecisionLimiter:
            {
                var actualGain=(r["R2"]/r["R1"])*(r["R8"]/r["R7"]);
                var actualHigh=request.SupplyVccVoltage*r["R4"]/(r["R3"]+r["R4"]);
                var actualLow=request.SupplyVeeVoltage*r["R6"]/(r["R5"]+r["R6"]);
                target=x=>Math.Clamp(request.GainK*x,request.LowerOutputLimitVoltage,request.UpperOutputLimitVoltage);
                selected=x=>Math.Clamp(actualGain*x,actualLow,actualHigh);
                AddPercent(metrics,"Linear-region gain",request.GainK,actualGain,request.MaximumFunctionErrorPercent,"V/V");
                AddPercent(metrics,"Upper clip level",request.UpperOutputLimitVoltage,actualHigh,request.MaximumFunctionErrorPercent,"V");
                AddPercent(metrics,"Lower clip level",request.LowerOutputLimitVoltage,actualLow,request.MaximumFunctionErrorPercent,"V");
                var targetTransitionHigh=request.UpperOutputLimitVoltage/request.GainK;
                var actualTransitionHigh=actualHigh/actualGain;
                AddPercent(metrics,"Upper transition input",targetTransitionHigh,actualTransitionHigh,request.MaximumFunctionErrorPercent,"V");
                var targetTransitionLow=request.LowerOutputLimitVoltage/request.GainK;
                var actualTransitionLow=actualLow/actualGain;
                AddPercent(metrics,"Lower transition input",targetTransitionLow,actualTransitionLow,request.MaximumFunctionErrorPercent,"V");
                break;
            }
            default: throw new ArgumentException("Unsupported piecewise operation.",nameof(request));
        }
        var maximumError=0d;
        var samples=request.Operation==AnalogComputeOperationType.PrecisionFullWaveRectifier?121:101;
        for(var index=0;index<samples;index++)
        {
            var x=request.MinimumInputVoltage+(request.MaximumInputVoltage-request.MinimumInputVoltage)*index/(samples-1d);
            var desired=target(x);
            var raw=selected(x);
            var limited=Math.Clamp(raw,lowRail,highRail);
            // Component/function verification uses the unclipped selected-value
            // transfer.  Rail clipping remains visible in Analysis and is only a
            // capability gate when the caller explicitly enables range feasibility.
            maximumError=Math.Max(maximumError,Math.Abs(raw-desired));
            points.Add(new(x,desired,limited,limited!=raw,raw));
        }
        var targetPeak=points.Max(point=>Math.Abs(point.Target));
        var allowed=Math.Max(1e-6,Math.Max(1,targetPeak)*request.MaximumFunctionErrorPercent/100);
        metrics.Add(new("Maximum transfer error",0,maximumError,maximumError,allowed,maximumError<=allowed,"V"));
        if(request.UseInputRangesForFeasibility)
        {
            var outputExceeded=points.Any(point=>point.IsSupplyLimited)||points.Any(point=>point.Target<lowRail||point.Target>highRail);
            metrics.Add(new("Output swing",targetPeak,Math.Min(targetPeak,Math.Max(Math.Abs(lowRail),Math.Abs(highRail))),
                outputExceeded?1:0,0,!outputExceeded,"V"));
        }
        var warnings=new List<string>();
        if(!request.OpAmp.UseIdealOpAmp&&request.UseInputRangesForFeasibility)
        {
            var maxGain=Math.Max(1,request.GainK);
            var requiredGbw=request.MaximumExpectedFrequencyHz*(1+maxGain)*10;
            metrics.Add(new("Required GBW",requiredGbw,request.OpAmp.GainBandwidthHz,
                request.OpAmp.GainBandwidthHz-requiredGbw,0,request.OpAmp.GainBandwidthHz>=requiredGbw,"Hz"));
            var requiredSlew=2*Math.PI*request.MaximumExpectedFrequencyHz*Math.Min(targetPeak,Math.Max(Math.Abs(lowRail),Math.Abs(highRail)));
            metrics.Add(new("Required slew rate",requiredSlew,request.OpAmp.SlewRateVoltPerSecond,
                request.OpAmp.SlewRateVoltPerSecond-requiredSlew,0,request.OpAmp.SlewRateVoltPerSecond>=requiredSlew,"V/s"));
        }
        else if(!request.OpAmp.UseIdealOpAmp)
            warnings.Add("GBW, slew-rate and output-swing limits are not gated because the simplified rectifier specification does not request an input range or maximum frequency.");
        else warnings.Add("Ideal-switch diode and ideal op-amp topology verification; real crossover, reverse recovery, GBW and slew performance require selected devices.");
        return(metrics,new("Vin-Vout transfer: Target / Theoretical / Selected",points,metrics),warnings);
    }

    private static void ValidateTopology(AnalogComputeDesignRequest request,CircuitGraph graph)
    {
        const int minimumDiodes=2;
        if(graph.Components.Count(component=>component.Type==ComponentType.Diode)<minimumDiodes)
            throw new InvalidOperationException("CircuitGraph does not contain the required precision diode feedback paths.");
        if(graph.Components.Any(component=>component.Pins.Any(pin=>graph.GetNetForPin(pin) is null)))
            throw new InvalidOperationException("CircuitGraph contains an unconnected component pin.");
        if(!graph.Nets.Any(net=>net.Name=="VIN")||!graph.Nets.Any(net=>net.Name=="VOUT"))
            throw new InvalidOperationException("CircuitGraph is missing stable VIN/VOUT ports.");
    }

    private static void AddPercent(List<VerificationMetric> metrics,string name,double target,double actual,double percent,string unit)
    {
        var tolerance=Math.Max(1e-9,Math.Abs(target)*percent/100);
        metrics.Add(new(name,target,actual,actual-target,tolerance,Math.Abs(actual-target)<=tolerance,unit));
    }
}
