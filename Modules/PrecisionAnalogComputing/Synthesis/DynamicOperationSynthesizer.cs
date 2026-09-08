using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

internal sealed class DynamicOperationSynthesizer
{
    public OperationSynthesisResult Synthesize(AnalogComputeDesignRequest request) => request.Operation switch
    {
        AnalogComputeOperationType.PracticalIntegrator => Integrator(request),
        AnalogComputeOperationType.PracticalDifferentiator => Differentiator(request),
        _ => throw new NotSupportedException($"{request.Operation} is not a dynamic operation.")
    };

    private static OperationSynthesisResult Integrator(AnalogComputeDesignRequest request)
    {
        var ki = request.IntegrationConstantPerSecond;
        var leak = request.LowerCornerHz;
        if (!(ki > 0 && leak > 0 && request.UpperCornerHz > leak))
            throw new ArgumentException("Integrator Ki, leak corner, and upper verification frequency must be positive with upper frequency above the leak corner.");
        var feasibleCMinimum=Math.Max(request.Components.CapacitorMinimumFarad,
            Math.Max(1/(ki*request.Components.ResistorMaximumOhm),1/(2*Math.PI*leak*request.Components.ResistorMaximumOhm)));
        var feasibleCMaximum=Math.Min(request.Components.CapacitorMaximumFarad,
            Math.Min(1/(ki*request.Components.ResistorMinimumOhm),1/(2*Math.PI*leak*request.Components.ResistorMinimumOhm)));
        if(feasibleCMinimum>feasibleCMaximum)
            throw new InvalidOperationException("Integrator Ki and leak corner have no positive Rin/Rf/Cf solution inside the configured component ranges.");
        var c0 = Math.Sqrt(feasibleCMinimum * feasibleCMaximum);
        var rin0 = 1 / (ki * c0);
        var rf0 = 1 / (2 * Math.PI * leak * c0);
        var candidates = from rin in AnalogSynthesisSupport.Candidates(rin0, true, request.Components)
                         from cf in AnalogSynthesisSupport.Candidates(c0, false, request.Components)
                         from rf in AnalogSynthesisSupport.Candidates(rf0, true, request.Components)
                         let actualKi = 1 / (rin * cf)
                         let actualLeak = 1 / (2 * Math.PI * rf * cf)
                         let score = SqLog(actualKi / ki) + SqLog(actualLeak / leak)
                         orderby score select (rin, cf, rf);
        var best = candidates.FirstOrDefault();
        if (!(best.rin > 0)) throw new InvalidOperationException("No coupled R/C standard-value set realizes the practical integrator inside the selected ranges.");
        var graph = BuildIntegrator(best.rin, best.rf, best.cf);
        AnalogStandardSeries? seriesR = request.Components.OptimizeStandardValues ? request.Components.ResistorSeries : null;
        AnalogStandardSeries? seriesC = request.Components.OptimizeStandardValues ? request.Components.CapacitorSeries : null;
        return new(graph,
        [
            AnalogSynthesisSupport.Component("R1", "Input resistor Rin", "Resistance", rin0, best.rin, "ohm", seriesR),
            AnalogSynthesisSupport.Component("R2", "Leakage feedback resistor Rf", "Resistance", rf0, best.rf, "ohm", seriesR),
            AnalogSynthesisSupport.Component("C1", "Integration feedback capacitor Cf", "Capacitance", c0, best.cf, "F", seriesC)
        ], "Inverting leaky integrator: Rin with Rf || Cf", "Vout/Vin = -(Rf/Rin)/(1+s Rf Cf)",
        [
            new("Integration constant", "Ki=1/(Rin*Cf)", $"target {ki:G9} 1/s; selected {1/(best.rin*best.cf):G9} 1/s"),
            new("Leak corner", "fL=1/(2*pi*Rf*Cf)", $"target {leak:G9} Hz; selected {1/(2*Math.PI*best.rf*best.cf):G9} Hz"),
            new("DC gain", "-Rf/Rin", $"{-best.rf/best.rin:G9} V/V")
        ]);
    }

    private static OperationSynthesisResult Differentiator(AnalogComputeDesignRequest request)
    {
        var kd = request.DifferentiationConstantSecond;
        var lower = request.LowerCornerHz;
        var upper = request.UpperCornerHz;
        if (!(kd > 0 && lower > 0 && upper > lower))
            throw new ArgumentException("Differentiator Kd and both limiting corners must be positive with upper corner above lower corner.");
        var rf0 = Math.Sqrt(request.Components.ResistorMinimumOhm * request.Components.ResistorMaximumOhm);
        var cin0 = kd / rf0;
        if (cin0 < request.Components.CapacitorMinimumFarad || cin0 > request.Components.CapacitorMaximumFarad)
        {
            cin0 = Math.Sqrt(request.Components.CapacitorMinimumFarad * request.Components.CapacitorMaximumFarad);
            rf0 = kd / cin0;
        }
        var rin0 = 1 / (2 * Math.PI * lower * cin0);
        var cf0 = 1 / (2 * Math.PI * upper * rf0);
        var candidates = from rin in AnalogSynthesisSupport.Candidates(rin0, true, request.Components)
                         from cin in AnalogSynthesisSupport.Candidates(cin0, false, request.Components)
                         from rf in AnalogSynthesisSupport.Candidates(rf0, true, request.Components)
                         from cf in AnalogSynthesisSupport.Candidates(cf0, false, request.Components)
                         let actualKd = rf * cin
                         let actualLower = 1 / (2 * Math.PI * rin * cin)
                         let actualUpper = 1 / (2 * Math.PI * rf * cf)
                         let score = SqLog(actualKd/kd) + SqLog(actualLower/lower) + SqLog(actualUpper/upper)
                         orderby score select (rin, cin, rf, cf);
        var best = candidates.FirstOrDefault();
        if (!(best.rin > 0)) throw new InvalidOperationException("No coupled R/C standard-value set realizes the bounded differentiator inside the selected ranges.");
        var graph = BuildDifferentiator(best.rin, best.cin, best.rf, best.cf);
        AnalogStandardSeries? seriesR = request.Components.OptimizeStandardValues ? request.Components.ResistorSeries : null;
        AnalogStandardSeries? seriesC = request.Components.OptimizeStandardValues ? request.Components.CapacitorSeries : null;
        return new(graph,
        [
            AnalogSynthesisSupport.Component("R1", "Input limiting resistor Rin", "Resistance", rin0, best.rin, "ohm", seriesR),
            AnalogSynthesisSupport.Component("C1", "Differentiating input capacitor Cin", "Capacitance", cin0, best.cin, "F", seriesC),
            AnalogSynthesisSupport.Component("R2", "Feedback resistor Rf", "Resistance", rf0, best.rf, "ohm", seriesR),
            AnalogSynthesisSupport.Component("C2", "High-frequency feedback capacitor Cf", "Capacitance", cf0, best.cf, "F", seriesC)
        ], "Bounded inverting differentiator: Cin-Rin input, Rf || Cf feedback",
        "Vout/Vin = -s Rf Cin/((1+s Rin Cin)(1+s Rf Cf))",
        [
            new("Differentiation constant", "Kd=Rf*Cin", $"target {kd:G9} s; selected {best.rf*best.cin:G9} s"),
            new("Input limiting corner", "f1=1/(2*pi*Rin*Cin)", $"target {lower:G9} Hz; selected {1/(2*Math.PI*best.rin*best.cin):G9} Hz"),
            new("Feedback limiting corner", "f2=1/(2*pi*Rf*Cf)", $"target {upper:G9} Hz; selected {1/(2*Math.PI*best.rf*best.cf):G9} Hz")
        ]);
    }

    private static CircuitGraph BuildIntegrator(double rinValue, double rfValue, double cfValue)
    {
        var b = BaseBuilder(); var rin=b.Add(ComponentFactory.Resistor("R1",rinValue)); var rf=b.Add(ComponentFactory.Resistor("R2",rfValue)); var cf=b.Add(ComponentFactory.Capacitor("C1",cfValue)); var u=b.Add(ComponentFactory.OpAmp("U1"));
        b.Connect("VIN",rin.GetPin("1")); b.Connect("SUM",rin.GetPin("2"),rf.GetPin("1"),cf.GetPin("1"),u.GetPin("IN-")); b.Connect("VOUT",rf.GetPin("2"),cf.GetPin("2"),u.GetPin("OUT")); b.Connect("GND",u.GetPin("IN+")); AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u); return b.Build();
    }

    private static CircuitGraph BuildDifferentiator(double rinValue, double cinValue, double rfValue, double cfValue)
    {
        var b=BaseBuilder(); var cin=b.Add(ComponentFactory.Capacitor("C1",cinValue)); var rin=b.Add(ComponentFactory.Resistor("R1",rinValue)); var rf=b.Add(ComponentFactory.Resistor("R2",rfValue)); var cf=b.Add(ComponentFactory.Capacitor("C2",cfValue)); var u=b.Add(ComponentFactory.OpAmp("U1"));
        b.Connect("VIN",cin.GetPin("1")); b.Connect("N_INPUT",cin.GetPin("2"),rin.GetPin("1")); b.Connect("SUM",rin.GetPin("2"),rf.GetPin("1"),cf.GetPin("1"),u.GetPin("IN-")); b.Connect("VOUT",rf.GetPin("2"),cf.GetPin("2"),u.GetPin("OUT")); b.Connect("GND",u.GetPin("IN+")); AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u); return b.Build();
    }

    private static CircuitBuilder BaseBuilder() { var b=new CircuitBuilder(); foreach(var net in new[]{"GND","VCC","VEE","VIN","VOUT"}) b.CreateNet(net); return b; }
    private static double SqLog(double value) { var x=Math.Log(value); return x*x; }
}
