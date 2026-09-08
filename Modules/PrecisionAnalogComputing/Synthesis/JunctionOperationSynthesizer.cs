using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

internal sealed class JunctionOperationSynthesizer
{
    public OperationSynthesisResult Synthesize(AnalogComputeDesignRequest request) => request.Operation switch
    {
        AnalogComputeOperationType.LogarithmicAmplifier => Log(request),
        AnalogComputeOperationType.ExponentialAmplifier => Antilog(request),
        _ => throw new NotSupportedException($"{request.Operation} is not a junction operation.")
    };

    private static OperationSynthesisResult Log(AnalogComputeDesignRequest request)
    {
        ValidateJunction(request);
        if (!(request.MinimumInputVoltage>0&&request.MaximumInputVoltage>request.MinimumInputVoltage))
            throw new ArgumentException("Log amplifier requires 0 < Vin minimum < Vin maximum.");
        if (!(request.LogScaleVoltage>0))throw new ArgumentException("Log scale S must be positive.");
        var baseResistance=ChooseBaseResistance(request);
        var scaleTarget=request.LogScaleVoltage/(request.Junction.EmissionCoefficient*request.Junction.ThermalVoltage);
        var scale=SelectRatio(scaleTarget,request.Components);
        var reference=SelectDivider(request.ReferenceVoltage/request.SupplyVccVoltage,request.Components);
        var graph=BuildLog(baseResistance,scale,reference);
        AnalogStandardSeries? series=request.Components.OptimizeStandardValues?request.Components.ResistorSeries:null;
        return new(graph,
        [
            C("R1","Input current resistor","Resistance",baseResistance,baseResistance,"ohm",series),
            C("R2","Reference current resistor","Resistance",baseResistance,baseResistance,"ohm",series),
            C("R3","Log scale input resistor","Resistance",scale.InputTheory,scale.Input,"ohm",series),
            C("R4","Log scale feedback resistor","Resistance",scale.FeedbackTheory,scale.Feedback,"ohm",series),
            C("R5","VCC reference divider top","Resistance",reference.TopTheory,reference.Top,"ohm",series),
            C("R6","Reference divider bottom","Resistance",reference.BottomTheory,reference.Bottom,"ohm",series),
            C("R7","Reference difference input","Resistance",scale.InputTheory,scale.Input,"ohm",series),
            C("R8","Reference difference shunt","Resistance",scale.FeedbackTheory,scale.Feedback,"ohm",series),
            C("D1","Input logarithmic junction","Saturation current",request.Junction.SaturationCurrentAmpere,request.Junction.SaturationCurrentAmpere,"A",null),
            C("D2","Matched reference junction","Saturation current",request.Junction.SaturationCurrentAmpere,request.Junction.SaturationCurrentAmpere,"A",null)
        ],"Matched-junction logarithmic front end with affine scale stage",
        $"Vout = -{request.LogScaleVoltage:G9}*ln(Vin/{request.ReferenceVoltage:G9})",
        [
            new("Thermal voltage","Vt=kT/q",$"{request.Junction.ThermalVoltage:G9} V at {request.Junction.TemperatureCelsius:G6} C"),
            new("Raw log difference","Vd=n*Vt*ln(Vin/Vref)","Matched junction/reference-current architecture cancels nominal Is"),
            new("Output scale","S=(R4/R3)*n*Vt",$"target {request.LogScaleVoltage:G9} V; selected {(scale.Feedback/scale.Input)*request.Junction.EmissionCoefficient*request.Junction.ThermalVoltage:G9} V"),
            new("Junction model","I=Is*(exp(Vd/(n*Vt))-1)",$"Is={request.Junction.SaturationCurrentAmpere:G9} A; n={request.Junction.EmissionCoefficient:G6}")
        ]);
    }

    private static OperationSynthesisResult Antilog(AnalogComputeDesignRequest request)
    {
        ValidateJunction(request);
        if (!(request.LogScaleVoltage>0))throw new ArgumentException("Antilog input scale S must be positive.");
        var nvt=request.Junction.EmissionCoefficient*request.Junction.ThermalVoltage;
        var attenuation=nvt/request.LogScaleVoltage;
        if (!(attenuation>0&&attenuation<1))
            throw new NotSupportedException($"The passive input attenuator requires S > n*Vt ({nvt:G9} V) for this Antilog topology.");
        var inputScale=SelectDivider(attenuation,request.Components);
        var reference=SelectDivider(request.ReferenceVoltage/request.SupplyVccVoltage,request.Components);
        var transimpedance=SelectRatio(1,request.Components);
        var graph=BuildAntilog(inputScale,reference,transimpedance);
        AnalogStandardSeries? series=request.Components.OptimizeStandardValues?request.Components.ResistorSeries:null;
        return new(graph,
        [
            C("R1","Input exponent divider top","Resistance",inputScale.TopTheory,inputScale.Top,"ohm",series),
            C("R2","Input exponent divider bottom","Resistance",inputScale.BottomTheory,inputScale.Bottom,"ohm",series),
            C("R3","VCC reference divider top","Resistance",reference.TopTheory,reference.Top,"ohm",series),
            C("R4","Reference divider bottom","Resistance",reference.BottomTheory,reference.Bottom,"ohm",series),
            C("R5","Reference current resistor","Resistance",transimpedance.InputTheory,transimpedance.Input,"ohm",series),
            C("R6","Output transimpedance resistor","Resistance",transimpedance.FeedbackTheory,transimpedance.Feedback,"ohm",series),
            C("D1","Exponential junction","Saturation current",request.Junction.SaturationCurrentAmpere,request.Junction.SaturationCurrentAmpere,"A",null),
            C("D2","Matched reference junction","Saturation current",request.Junction.SaturationCurrentAmpere,request.Junction.SaturationCurrentAmpere,"A",null)
        ],"Matched-junction exponential stage with bounded input attenuation and transimpedance output",
        $"Vout = {request.ReferenceVoltage:G9}*exp(Vin/{request.LogScaleVoltage:G9})",
        [
            new("Thermal voltage","Vt=kT/q",$"{request.Junction.ThermalVoltage:G9} V at {request.Junction.TemperatureCelsius:G6} C"),
            new("Exponent scale","Vin/S = alpha*Vin/(n*Vt)",$"target alpha={attenuation:G9}; selected {inputScale.Bottom/(inputScale.Top+inputScale.Bottom):G9}"),
            new("Matched current ratio","Iout/Iref=exp(Vin/S)","Nominal Is cancels only for matched junctions at equal temperature"),
            new("Overflow guard","abs(Vin/S) must remain numerically and electrically bounded",$"input range {request.MinimumInputVoltage:G6} to {request.MaximumInputVoltage:G6} V")
        ]);
    }

    private sealed record Ratio(double InputTheory,double FeedbackTheory,double Input,double Feedback);
    private sealed record Divider(double TopTheory,double BottomTheory,double Top,double Bottom);
    private static SelectedAnalogComponent C(string reference,string role,string quantity,double theory,double selected,string unit,AnalogStandardSeries? series)=>
        AnalogSynthesisSupport.Component(reference,role,quantity,theory,selected,unit,series);

    private static double ChooseBaseResistance(AnalogComputeDesignRequest request)
    {
        var target=Math.Sqrt(request.Components.ResistorMinimumOhm*request.Components.ResistorMaximumOhm);
        var selected=request.Components.OptimizeStandardValues
            ? AnalogPreferredValueSeries.Nearest(target,request.Components.ResistorSeries,request.Components.ResistorMinimumOhm,request.Components.ResistorMaximumOhm)
            : target;
        if(request.MaximumInputVoltage/selected>request.MaximumJunctionCurrentAmpere)
            throw new InvalidOperationException("The input range requires junction current above the configured maximum inside the resistor range.");
        return selected;
    }

    private static Ratio SelectRatio(double target,ComponentSelectionConstraints constraints)
    {
        var inputTheory=Math.Sqrt(constraints.ResistorMinimumOhm*constraints.ResistorMaximumOhm);
        var feedbackTheory=inputTheory*target;
        if(feedbackTheory>constraints.ResistorMaximumOhm){feedbackTheory=constraints.ResistorMaximumOhm;inputTheory=feedbackTheory/target;}
        if(feedbackTheory<constraints.ResistorMinimumOhm){feedbackTheory=constraints.ResistorMinimumOhm;inputTheory=feedbackTheory/target;}
        var best=(Input:0d,Feedback:0d,Error:double.PositiveInfinity);
        foreach(var input in AnalogSynthesisSupport.Candidates(inputTheory,true,constraints))
        foreach(var feedback in AnalogSynthesisSupport.Candidates(feedbackTheory,true,constraints))
        {var error=Math.Abs(feedback/input-target)/target;if(error<best.Error)best=(input,feedback,error);}
        return new(inputTheory,feedbackTheory,best.Input,best.Feedback);
    }

    private static Divider SelectDivider(double fraction,ComponentSelectionConstraints constraints)
    {
        if(!(fraction>0&&fraction<1))throw new ArgumentException("Reference voltage must lie between GND and VCC.");
        var bottomTheory=Math.Sqrt(constraints.ResistorMinimumOhm*constraints.ResistorMaximumOhm);
        var topTheory=bottomTheory*(1-fraction)/fraction;
        if(topTheory>constraints.ResistorMaximumOhm){topTheory=constraints.ResistorMaximumOhm;bottomTheory=topTheory*fraction/(1-fraction);}
        if(topTheory<constraints.ResistorMinimumOhm){topTheory=constraints.ResistorMinimumOhm;bottomTheory=topTheory*fraction/(1-fraction);}
        var best=(Top:0d,Bottom:0d,Error:double.PositiveInfinity);
        foreach(var top in AnalogSynthesisSupport.Candidates(topTheory,true,constraints))
        foreach(var bottom in AnalogSynthesisSupport.Candidates(bottomTheory,true,constraints))
        {var error=Math.Abs(bottom/(top+bottom)-fraction);if(error<best.Error)best=(top,bottom,error);}
        return new(topTheory,bottomTheory,best.Top,best.Bottom);
    }

    private static CircuitBuilder Base(){var b=new CircuitBuilder();foreach(var net in new[]{"GND","VCC","VEE","VIN","VOUT"})b.CreateNet(net);return b;}
    private static CircuitGraph BuildLog(double baseR,Ratio scale,Divider reference)
    {
        var b=Base();var r1=b.Add(ComponentFactory.Resistor("R1",baseR));var r2=b.Add(ComponentFactory.Resistor("R2",baseR));var r3=b.Add(ComponentFactory.Resistor("R3",scale.Input));var r4=b.Add(ComponentFactory.Resistor("R4",scale.Feedback));var r5=b.Add(ComponentFactory.Resistor("R5",reference.Top));var r6=b.Add(ComponentFactory.Resistor("R6",reference.Bottom));var r7=b.Add(ComponentFactory.Resistor("R7",scale.Input));var r8=b.Add(ComponentFactory.Resistor("R8",scale.Feedback));var d1=b.Add(ComponentFactory.Diode("D1"));var d2=b.Add(ComponentFactory.Diode("D2"));var u1=b.Add(ComponentFactory.OpAmp("U1"));var u2=b.Add(ComponentFactory.OpAmp("U2"));var u3=b.Add(ComponentFactory.OpAmp("U3"));
        b.Connect("VIN",r1.GetPin("1"));b.Connect("SUM_LOG",r1.GetPin("2"),d1.GetPin("A"),u1.GetPin("IN-"));b.Connect("RAW_LOG",d1.GetPin("K"),u1.GetPin("OUT"),r3.GetPin("1"));
        b.Connect("VCC",r5.GetPin("1"));b.Connect("VREF",r5.GetPin("2"),r6.GetPin("1"),r2.GetPin("1"));b.Connect("SUM_REF",r2.GetPin("2"),d2.GetPin("A"),u2.GetPin("IN-"));b.Connect("RAW_REF",d2.GetPin("K"),u2.GetPin("OUT"),r7.GetPin("1"));
        b.Connect("PLUS_OUT",r7.GetPin("2"),r8.GetPin("1"),u3.GetPin("IN+"));b.Connect("SUM_OUT",r3.GetPin("2"),r4.GetPin("1"),u3.GetPin("IN-"));b.Connect("VOUT",r4.GetPin("2"),u3.GetPin("OUT"));b.Connect("GND",r6.GetPin("2"),r8.GetPin("2"),u1.GetPin("IN+"),u2.GetPin("IN+"));AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u1);AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u2);AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u3);return b.Build();
    }

    private static CircuitGraph BuildAntilog(Divider inputScale,Divider reference,Ratio trans)
    {
        var b=Base();var r1=b.Add(ComponentFactory.Resistor("R1",inputScale.Top));var r2=b.Add(ComponentFactory.Resistor("R2",inputScale.Bottom));var r3=b.Add(ComponentFactory.Resistor("R3",reference.Top));var r4=b.Add(ComponentFactory.Resistor("R4",reference.Bottom));var r5=b.Add(ComponentFactory.Resistor("R5",trans.Input));var r6=b.Add(ComponentFactory.Resistor("R6",trans.Feedback));var d1=b.Add(ComponentFactory.Diode("D1"));var d2=b.Add(ComponentFactory.Diode("D2"));var u1=b.Add(ComponentFactory.OpAmp("U1"));var u2=b.Add(ComponentFactory.OpAmp("U2"));
        b.Connect("VIN",r1.GetPin("1"));b.Connect("EXP_DRIVE",r1.GetPin("2"),r2.GetPin("1"),d1.GetPin("A"));b.Connect("GND",r2.GetPin("2"),r4.GetPin("2"),u1.GetPin("IN+"),u2.GetPin("IN+"));
        b.Connect("VCC",r3.GetPin("1"));b.Connect("VREF",r3.GetPin("2"),r4.GetPin("1"),r5.GetPin("1"),d2.GetPin("A"));b.Connect("REF_JUNCTION",d2.GetPin("K"),u1.GetPin("OUT"),u1.GetPin("IN-"));
        b.Connect("SUM_OUT",d1.GetPin("K"),r5.GetPin("2"),r6.GetPin("1"),u2.GetPin("IN-"));b.Connect("VOUT",r6.GetPin("2"),u2.GetPin("OUT"));AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u1);AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u2);return b.Build();
    }

    private static void ValidateJunction(AnalogComputeDesignRequest request)
    {
        if(request.Junction.Behavior!=DiodeBehaviorModel.ShockleyJunction)throw new ArgumentException("Log and Antilog require the ShockleyJunction model.");
        if(!(request.Junction.SaturationCurrentAmpere>0&&request.Junction.EmissionCoefficient>0&&request.Junction.TemperatureCelsius>-273.15))
            throw new ArgumentException("Junction Is, emission coefficient n, and absolute temperature must be positive.");
        if(!(request.ReferenceVoltage>0&&request.ReferenceVoltage<request.SupplyVccVoltage))throw new ArgumentException("Vref must be positive and below VCC so it can be synthesized from VCC/GND.");
        if(!(request.MinimumInputVoltage<request.MaximumInputVoltage))throw new ArgumentException("Input minimum must be below input maximum.");
    }
}
