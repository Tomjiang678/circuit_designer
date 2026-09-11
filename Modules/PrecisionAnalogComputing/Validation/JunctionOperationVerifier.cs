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
        ValidateTopology(request.Operation,graph);
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
            var referenceCurrent=actualReference/r["R2"];
            target=x=>-request.LogScaleVoltage*Math.Log(x/request.ReferenceVoltage);
            actual=x=>
            {
                var rawLog=-nvt*Math.Log(1+x/(r["R1"]*request.Junction.SaturationCurrentAmpere));
                var rawReference=-nvt*Math.Log(1+referenceCurrent/request.Junction.SaturationCurrentAmpere);
                var plus=rawLog*r["R8"]/(r["R7"]+r["R8"]);
                return (1+r["R4"]/r["R3"])*plus-(r["R4"]/r["R3"])*rawReference;
            };
            Add(metrics,"Log scale S",request.LogScaleVoltage,actualScale,request.MaximumFunctionErrorPercent,"V/neper");
            Add(metrics,"Log difference ratio matching",r["R4"]/r["R3"],referenceScale,request.MaximumFunctionErrorPercent,string.Empty);
            Add(metrics,"Vref calibration point",request.ReferenceVoltage,actualReference,request.MaximumFunctionErrorPercent,"V");
            maxCurrent=Math.Max(request.MaximumInputVoltage/r["R1"],referenceCurrent);
        }
        else if(request.Operation==AnalogComputeOperationType.ExponentialAmplifier)
        {
            var exponentMax=request.MaximumInputVoltage/request.LogScaleVoltage;
            var exponentMin=request.MinimumInputVoltage/request.LogScaleVoltage;
            if(!double.IsFinite(exponentMax)||Math.Max(Math.Abs(exponentMin),Math.Abs(exponentMax))>80)
                throw new ArgumentException("Antilog exponent range exceeds the guarded exp range (|Vin/S| <= 80).");
            var attenuation=r["R2"]/r["R1"];
            var actualScale=nvt/attenuation;
            var dividerReference=request.SupplyVccVoltage*r["R6"]/(r["R5"]+r["R6"]);
            var referenceCurrent=dividerReference/r["R7"];
            var referenceJunctionVoltage=nvt*Math.Log(1+referenceCurrent/request.Junction.SaturationCurrentAmpere);
            var actualReference=referenceCurrent*r["R8"];
            target=x=>request.ReferenceVoltage*Math.Exp(x/request.LogScaleVoltage);
            actual=x=>r["R8"]*request.Junction.SaturationCurrentAmpere*(Math.Exp(
                (referenceJunctionVoltage+attenuation*x)/nvt)-1);
            Add(metrics,"Antilog input scale S",request.LogScaleVoltage,actualScale,request.MaximumFunctionErrorPercent,"V");
            Add(metrics,"Output at Vin=0",request.ReferenceVoltage,actualReference,request.MaximumFunctionErrorPercent,"V");
            maxCurrent=Math.Max(referenceCurrent,request.Junction.SaturationCurrentAmpere*(Math.Exp(
                Math.Min(700,(referenceJunctionVoltage+attenuation*request.MaximumInputVoltage)/nvt))-1));
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
            if(limited)limitedCount++;maximumError=Math.Max(maximumError,Math.Abs(raw-desired));points.Add(new(x,desired,selected,limited,raw));
        }
        var peak=points.Max(point=>Math.Abs(point.Target));var tolerance=Math.Max(1e-6,Math.Max(1,peak)*request.MaximumFunctionErrorPercent/100);
        metrics.Add(new("Maximum nonlinear transfer error",0,maximumError,maximumError,tolerance,maximumError<=tolerance,"V"));
        if(limitedCount>0)
            warnings.Add($"{limitedCount} analysis samples exceed the available output swing and are shown clipped at the VCC/VEE limits; component/function verification uses the unclipped transfer.");
        metrics.Add(new("Maximum junction current",request.MaximumJunctionCurrentAmpere,maxCurrent,maxCurrent-request.MaximumJunctionCurrentAmpere,0,maxCurrent<=request.MaximumJunctionCurrentAmpere,"A"));
        return(metrics,new("Nonlinear Vin-Vout transfer: Target / Theoretical / Selected",points,metrics),warnings);
    }

    private static void Add(List<VerificationMetric> metrics,string name,double target,double actual,double percent,string unit)
    {var tolerance=Math.Max(1e-12,Math.Abs(target)*percent/100);metrics.Add(new(name,target,actual,actual-target,tolerance,Math.Abs(actual-target)<=tolerance,unit));}

    private static void ValidateTopology(AnalogComputeOperationType operation,CircuitGraph graph)
    {
        if(operation==AnalogComputeOperationType.LogarithmicAmplifier)
        {
            RequireNet(graph,"SUM_LOG","R1.2","D1.A","U1.IN-"); RequireNet(graph,"RAW_LOG","D1.K","U1.OUT","R7.1");
            RequireNet(graph,"SUM_REF","R2.2","D2.A","U2.IN-"); RequireNet(graph,"RAW_REF","D2.K","U2.OUT","R3.1");
            RequireNet(graph,"PLUS_OUT","R7.2","R8.1","U3.IN+"); RequireNet(graph,"SUM_OUT","R3.2","R4.1","U3.IN-");
            RequireNet(graph,"VOUT","R4.2","U3.OUT");
        }
        else if(operation==AnalogComputeOperationType.ExponentialAmplifier)
        {
            RequireNet(graph,"SUM_REF","R7.2","D2.A","U1.IN-"); RequireNet(graph,"RAW_REF","D2.K","U1.OUT","R3.1");
            RequireNet(graph,"SUM_DRIVE","R1.2","R2.1","U2.IN-"); RequireNet(graph,"RAW_EXP","R2.2","U2.OUT","D1.K");
            RequireNet(graph,"PLUS_DRIVE","R3.2","R4.1","U2.IN+"); RequireNet(graph,"SUM_OUT","D1.A","R8.1","U3.IN-");
            RequireNet(graph,"VOUT","R8.2","U3.OUT");
        }
        else throw new ArgumentException("Unsupported junction operation.");
    }

    private static void RequireNet(CircuitGraph graph,string netName,params string[] expectedPins)
    {
        var actual=graph.GetNet(netName).Pins.Select(pin=>pin.Id).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
        var expected=expectedPins.OrderBy(id=>id,StringComparer.OrdinalIgnoreCase).ToArray();
        if(!actual.SequenceEqual(expected,StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{netName} topology mismatch: expected {string.Join(',',expected)}, actual {string.Join(',',actual)}.");
    }
}
