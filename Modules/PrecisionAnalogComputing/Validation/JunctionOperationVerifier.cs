using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

internal sealed class JunctionOperationVerifier
{
    public (IReadOnlyList<VerificationMetric> Metrics,AnalogAnalysisResult Analysis,IReadOnlyList<string> Warnings) Verify(
        AnalogComputeDesignRequest request,CircuitGraph graph)
    {
        if(graph.Components.Count(component=>component.Type==ComponentType.Diode)!=2)
            throw new InvalidOperationException("Matched-junction CircuitGraph must contain exactly two diode junctions.");
        if(graph.Components.Any(component=>component.Pins.Any(pin=>graph.GetNetForPin(pin) is null)))
            throw new InvalidOperationException("Junction CircuitGraph contains an unconnected component pin.");
        var r=graph.Components.Where(component=>component.Type==ComponentType.Resistor).ToDictionary(component=>component.Reference,component=>component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm));
        var nvt=request.Junction.EmissionCoefficient*request.Junction.ThermalVoltage;
        var metrics=new List<VerificationMetric>();var points=new List<AnalogAnalysisPoint>();var warnings=new List<string>
        {
            "Absolute precision is not vendor-independent: Is matching, emission coefficient, self-heating and temperature gradients require calibration and matched devices.",
            $"Nominal temperature coefficient follows thermal voltage ({request.Junction.TemperatureCelsius:G6} C model point)."
        };
        Func<double,double> target;Func<double,double> actual;double maxCurrent=0;
        if(request.Operation==AnalogComputeOperationType.LogarithmicAmplifier)
        {
            if(request.MinimumInputVoltage<=0)throw new ArgumentException("Log verification domain requires Vin > 0.");
            var actualReference=request.SupplyVccVoltage*r["R6"]/(r["R5"]+r["R6"]);
            var actualScale=(r["R4"]/r["R3"])*nvt;
            var referenceScale=r["R8"]/r["R7"];
            target=x=>-request.LogScaleVoltage*Math.Log(x/request.ReferenceVoltage);
            actual=x=>-actualScale*Math.Log((x/r["R1"])/(actualReference/r["R2"]));
            Add(metrics,"Log scale S",request.LogScaleVoltage,actualScale,request.MaximumFunctionErrorPercent,"V/neper");
            Add(metrics,"Log difference ratio matching",r["R4"]/r["R3"],referenceScale,request.MaximumFunctionErrorPercent,string.Empty);
            Add(metrics,"Vref calibration point",request.ReferenceVoltage,actualReference,request.MaximumFunctionErrorPercent,"V");
            maxCurrent=request.MaximumInputVoltage/r["R1"];
        }
        else if(request.Operation==AnalogComputeOperationType.ExponentialAmplifier)
        {
            var exponentMax=request.MaximumInputVoltage/request.LogScaleVoltage;
            var exponentMin=request.MinimumInputVoltage/request.LogScaleVoltage;
            if(!double.IsFinite(exponentMax)||Math.Max(Math.Abs(exponentMin),Math.Abs(exponentMax))>80)
                throw new ArgumentException("Antilog exponent range exceeds the guarded exp range (|Vin/S| <= 80).");
            var attenuation=r["R2"]/(r["R1"]+r["R2"]);
            var actualScale=nvt/attenuation;
            var actualReference=request.SupplyVccVoltage*r["R4"]/(r["R3"]+r["R4"])*(r["R6"]/r["R5"]);
            target=x=>request.ReferenceVoltage*Math.Exp(x/request.LogScaleVoltage);
            actual=x=>actualReference*Math.Exp(x/actualScale);
            Add(metrics,"Antilog input scale S",request.LogScaleVoltage,actualScale,request.MaximumFunctionErrorPercent,"V");
            Add(metrics,"Output at Vin=0",request.ReferenceVoltage,actualReference,request.MaximumFunctionErrorPercent,"V");
            maxCurrent=request.Junction.SaturationCurrentAmpere*Math.Exp(Math.Min(80,request.MaximumInputVoltage/actualScale));
        }
        else throw new ArgumentException("Unsupported junction operation.");
        var low=request.SupplyVeeVoltage+request.OpAmp.OutputSwingHeadroomVoltage;var high=request.SupplyVccVoltage-request.OpAmp.OutputSwingHeadroomVoltage;var maximumError=0d;var limitedCount=0;
        for(var index=0;index<=120;index++)
        {
            var fraction=index/120d;
            var x=request.Operation==AnalogComputeOperationType.LogarithmicAmplifier
                ? request.MinimumInputVoltage*Math.Pow(request.MaximumInputVoltage/request.MinimumInputVoltage,fraction)
                : request.MinimumInputVoltage+(request.MaximumInputVoltage-request.MinimumInputVoltage)*fraction;
            var desired=target(x);var raw=actual(x);var selected=Math.Clamp(raw,low,high);var limited=selected!=raw;
            if(limited)limitedCount++;maximumError=Math.Max(maximumError,Math.Abs(selected-desired));points.Add(new(x,desired,selected,limited,raw));
        }
        var peak=points.Max(point=>Math.Abs(point.Target));var tolerance=Math.Max(1e-6,Math.Max(1,peak)*request.MaximumFunctionErrorPercent/100);
        metrics.Add(new("Maximum nonlinear transfer error",0,maximumError,maximumError,tolerance,maximumError<=tolerance,"V"));
        metrics.Add(new("Output swing exceeded samples",0,limitedCount,limitedCount,0,limitedCount==0));
        metrics.Add(new("Maximum junction current",request.MaximumJunctionCurrentAmpere,maxCurrent,maxCurrent-request.MaximumJunctionCurrentAmpere,0,maxCurrent<=request.MaximumJunctionCurrentAmpere,"A"));
        return(metrics,new("Nonlinear Vin-Vout transfer: Target / Theoretical / Selected",points,metrics),warnings);
    }

    private static void Add(List<VerificationMetric> metrics,string name,double target,double actual,double percent,string unit)
    {var tolerance=Math.Max(1e-12,Math.Abs(target)*percent/100);metrics.Add(new(name,target,actual,actual-target,tolerance,Math.Abs(actual-target)<=tolerance,unit));}
}
