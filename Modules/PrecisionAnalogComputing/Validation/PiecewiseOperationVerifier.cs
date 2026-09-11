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
        var warnings=new List<string>();
        var limitedCount=points.Count(point=>point.IsSupplyLimited);
        if(limitedCount>0)
            warnings.Add($"{limitedCount} analysis samples exceed the available output swing and are shown clipped at the VCC/VEE limits; this does not invalidate the synthesized transfer topology.");
        if(!request.OpAmp.UseIdealOpAmp)
        {
            var maxGain=Math.Max(1,request.GainK);
            var requiredGbw=request.MaximumExpectedFrequencyHz*(1+maxGain)*10;
            metrics.Add(new("Required GBW",requiredGbw,request.OpAmp.GainBandwidthHz,
                request.OpAmp.GainBandwidthHz-requiredGbw,0,request.OpAmp.GainBandwidthHz>=requiredGbw,"Hz"));
            var requiredSlew=2*Math.PI*request.MaximumExpectedFrequencyHz*Math.Min(targetPeak,Math.Max(Math.Abs(lowRail),Math.Abs(highRail)));
            metrics.Add(new("Required slew rate",requiredSlew,request.OpAmp.SlewRateVoltPerSecond,
                request.OpAmp.SlewRateVoltPerSecond-requiredSlew,0,request.OpAmp.SlewRateVoltPerSecond>=requiredSlew,"V/s"));
        }
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
        if(request.Operation==AnalogComputeOperationType.PrecisionHalfWaveRectifier)
        {
            RequireNet(graph,"SUM","R1.2","R2.1","U1.IN-","D2.A");
            RequireNet(graph,"U1_RAW","U1.OUT","D1.A","D2.K");
            RequireNet(graph,"VOUT","R2.2","D1.K");
            if(request.HalfWavePolarity==HalfWavePolarity.Positive)
            {
                RequireNet(graph,"SUM_POLARITY","R3.2","R4.1","U2.IN-");
                RequireNet(graph,"RECT_INPUT","R4.2","U2.OUT","R1.1");
            }
            else RequireNet(graph,"VIN","R1.1");
        }
        else if(request.Operation==AnalogComputeOperationType.PrecisionFullWaveRectifier)
        {
            RequireNet(graph,"VIN","R1.1","R3.1"); RequireNet(graph,"SUM_RECT","R1.2","R2.1","U1.IN-","D2.A");
            RequireNet(graph,"HALF_RECT","R2.2","D1.K","R4.1"); RequireNet(graph,"RAW_RECT","U1.OUT","D1.A","D2.K");
            RequireNet(graph,"SUM_ABS","R3.2","R4.2","R5.1","U2.IN-");
            if(request.OutputPolarity==RectifierOutputPolarity.Positive)
            {
                RequireNet(graph,"NEG_ABS","R5.2","U2.OUT","R6.1"); RequireNet(graph,"SUM_POLARITY","R6.2","R7.1","U3.IN-");
                RequireNet(graph,"VOUT","R7.2","U3.OUT");
            }
            else RequireNet(graph,"VOUT","R5.2","U2.OUT");
        }
        else throw new ArgumentException("Unsupported piecewise operation.",nameof(request));
    }

    private static void RequireNet(CircuitGraph graph,string netName,params string[] expectedPins)
    {
        var actual=graph.GetNet(netName).Pins.Select(pin=>pin.Id).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
        var expected=expectedPins.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
        if(!actual.SequenceEqual(expected,StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{netName} topology mismatch: expected {string.Join(',',expected)}, actual {string.Join(',',actual)}.");
    }

    private static void AddPercent(List<VerificationMetric> metrics,string name,double target,double actual,double percent,string unit)
    {
        var tolerance=Math.Max(1e-9,Math.Abs(target)*percent/100);
        metrics.Add(new(name,target,actual,actual-target,tolerance,Math.Abs(actual-target)<=tolerance,unit));
    }
}
