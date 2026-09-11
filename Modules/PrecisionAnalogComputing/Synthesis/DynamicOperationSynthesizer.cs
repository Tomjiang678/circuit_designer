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
        var parameters = DynamicOperationParameters.Resolve(request);
        var leak = parameters.FirstCornerHz;
        var feasibleCMinimum=Math.Max(request.Components.CapacitorMinimumFarad,
            Math.Max(1/(ki*request.Components.ResistorMaximumOhm),1/(2*Math.PI*leak*request.Components.ResistorMaximumOhm)));
        var feasibleCMaximum=Math.Min(request.Components.CapacitorMaximumFarad,
            Math.Min(1/(ki*request.Components.ResistorMinimumOhm),1/(2*Math.PI*leak*request.Components.ResistorMinimumOhm)));
        if(feasibleCMinimum>feasibleCMaximum)
            throw new InvalidOperationException("Integrator Ki and leak corner have no positive Rin/Rf/Cf solution inside the configured component ranges.");
        var c0 = Math.Sqrt(feasibleCMinimum * feasibleCMaximum);
        var rin0 = 1 / (ki * c0);
        var rf0 = 1 / (2 * Math.PI * leak * c0);
        var best = SelectIntegrator(ki, leak, c0, request.Components);
        var graph = BuildIntegrator(best.Rin, best.Rf, best.Cf);
        AnalogStandardSeries? seriesR = request.Components.OptimizeStandardValues ? request.Components.ResistorSeries : null;
        AnalogStandardSeries? seriesC = request.Components.OptimizeStandardValues ? request.Components.CapacitorSeries : null;
        return new(graph,
        [
            AnalogSynthesisSupport.Component("R1", "Input resistor Rin", "Resistance", rin0, best.Rin, "ohm", seriesR),
            AnalogSynthesisSupport.Component("R2", "Leakage feedback resistor Rf", "Resistance", rf0, best.Rf, "ohm", seriesR),
            AnalogSynthesisSupport.Component("C1", "Integration feedback capacitor Cf", "Capacitance", c0, best.Cf, "F", seriesC)
        ], "Inverting leaky integrator: Rin with Rf || Cf", "Vout/Vin = -(Rf/Rin)/(1+s Rf Cf)",
        [
            new("Integration constant", "Ki=1/(Rin*Cf)", $"target {ki:G9} 1/s; selected {1/(best.Rin*best.Cf):G9} 1/s"),
            new("Leak corner", "fL=1/(2*pi*Rf*Cf)", $"target {leak:G9} Hz; selected {1/(2*Math.PI*best.Rf*best.Cf):G9} Hz"),
            new("DC gain", "-Rf/Rin", $"{-best.Rf/best.Rin:G9} V/V")
        ]);
    }

    private static OperationSynthesisResult Differentiator(AnalogComputeDesignRequest request)
    {
        var kd = request.DifferentiationConstantSecond;
        var parameters = DynamicOperationParameters.Resolve(request);
        var lower = parameters.FirstCornerHz;
        var upper = parameters.SecondCornerHz;
        var minimumCin = Math.Max(request.Components.CapacitorMinimumFarad, Math.Max(
            kd / request.Components.ResistorMaximumOhm,
            Math.Max(1 / (2 * Math.PI * lower * request.Components.ResistorMaximumOhm),
                request.Components.CapacitorMinimumFarad * 2 * Math.PI * upper * kd)));
        var maximumCin = Math.Min(request.Components.CapacitorMaximumFarad, Math.Min(
            kd / request.Components.ResistorMinimumOhm,
            Math.Min(1 / (2 * Math.PI * lower * request.Components.ResistorMinimumOhm),
                request.Components.CapacitorMaximumFarad * 2 * Math.PI * upper * kd)));
        if (minimumCin > maximumCin)
            throw new InvalidOperationException("Differentiator Kd and useful bandwidth have no positive Rin/Cin/Rf/Cf solution inside the configured component ranges.");
        var cin0 = Math.Sqrt(minimumCin * maximumCin);
        var rf0 = kd / cin0;
        var rin0 = 1 / (2 * Math.PI * lower * cin0);
        var cf0 = 1 / (2 * Math.PI * upper * rf0);
        var best = SelectDifferentiator(kd, lower, upper, cin0, request.Components, request.MaximumFunctionErrorPercent);
        var graph = BuildDifferentiator(best.Rin, best.Cin, best.Rf, best.Cf);
        AnalogStandardSeries? seriesR = request.Components.OptimizeStandardValues ? request.Components.ResistorSeries : null;
        AnalogStandardSeries? seriesC = request.Components.OptimizeStandardValues ? request.Components.CapacitorSeries : null;
        return new(graph,
        [
            AnalogSynthesisSupport.Component("R1", "Input limiting resistor Rin", "Resistance", rin0, best.Rin, "ohm", seriesR),
            AnalogSynthesisSupport.Component("C1", "Differentiating input capacitor Cin", "Capacitance", cin0, best.Cin, "F", seriesC),
            AnalogSynthesisSupport.Component("R2", "Feedback resistor Rf", "Resistance", rf0, best.Rf, "ohm", seriesR),
            AnalogSynthesisSupport.Component("C2", "High-frequency feedback capacitor Cf", "Capacitance", cf0, best.Cf, "F", seriesC)
        ], "Bounded inverting differentiator: Cin-Rin input, Rf || Cf feedback",
        "Vout/Vin = -s Rf Cin/((1+s Rin Cin)(1+s Rf Cf))",
        [
            new("Differentiation constant", "Kd=Rf*Cin", $"target {kd:G9} s; selected {best.Rf*best.Cin:G9} s"),
            new("Useful differentiation band", "0 < f <= fUseful; automatic limiting poles are 5*fUseful and 10*fUseful", $"fUseful={parameters.UsefulUpperFrequencyHz:G9} Hz"),
            new("Input limiting corner", "f1=1/(2*pi*Rin*Cin)", $"target {lower:G9} Hz; selected {1/(2*Math.PI*best.Rin*best.Cin):G9} Hz"),
            new("Feedback limiting corner", "f2=1/(2*pi*Rf*Cf)", $"target {upper:G9} Hz; selected {1/(2*Math.PI*best.Rf*best.Cf):G9} Hz")
        ]);
    }

    private static (double Rin,double Rf,double Cf) SelectIntegrator(double ki,double leak,double continuousCf,ComponentSelectionConstraints constraints)
    {
        var capacitorCandidates=constraints.OptimizeStandardValues
            ? AnalogPreferredValueSeries.All(constraints.CapacitorSeries,constraints.CapacitorMinimumFarad,constraints.CapacitorMaximumFarad)
            : [continuousCf];
        var best=(Rin:0d,Rf:0d,Cf:0d,Score:double.PositiveInfinity);
        foreach(var cf in capacitorCandidates)
        {
            var rinTheory=1/(ki*cf);var rfTheory=1/(2*Math.PI*leak*cf);
            if(!InRange(rinTheory,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm)||
               !InRange(rfTheory,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm))continue;
            foreach(var rin in AnalogSynthesisSupport.Candidates(rinTheory,true,constraints))
            foreach(var rf in AnalogSynthesisSupport.Candidates(rfTheory,true,constraints))
            {
                var actualKi=1/(rin*cf);var actualLeak=1/(2*Math.PI*rf*cf);
                var score=SqLog(actualKi/ki)+SqLog(actualLeak/leak)+1e-6*SqLog(rf/rin);
                if(score<best.Score)best=(rin,rf,cf,score);
            }
        }
        if(!(best.Rin>0))throw new InvalidOperationException("No coupled R/C standard-value set realizes the practical integrator inside the selected ranges.");
        return(best.Rin,best.Rf,best.Cf);
    }

    private static (double Rin,double Cin,double Rf,double Cf) SelectDifferentiator(double kd,double first,double second,double continuousCin,ComponentSelectionConstraints constraints,double errorPercent)
    {
        var capacitorCandidates=constraints.OptimizeStandardValues
            ? AnalogPreferredValueSeries.All(constraints.CapacitorSeries,constraints.CapacitorMinimumFarad,constraints.CapacitorMaximumFarad)
            : [continuousCin];
        var best=(Rin:0d,Cin:0d,Rf:0d,Cf:0d,Score:double.PositiveInfinity);
        foreach(var cin in capacitorCandidates)
        {
            var rfTheory=kd/cin;var rinTheory=1/(2*Math.PI*first*cin);
            if(!InRange(rfTheory,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm)||
               !InRange(rinTheory,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm))continue;
            foreach(var rin in AnalogSynthesisSupport.Candidates(rinTheory,true,constraints))
            foreach(var rf in AnalogSynthesisSupport.Candidates(rfTheory,true,constraints))
            {
                var cfTheory=1/(2*Math.PI*second*rf);
                if(!InRange(cfTheory,constraints.CapacitorMinimumFarad,constraints.CapacitorMaximumFarad))continue;
                foreach(var cf in AnalogSynthesisSupport.Candidates(cfTheory,false,constraints))
                {
                    var actualKd=rf*cin;var actualFirst=1/(2*Math.PI*rin*cin);var actualSecond=1/(2*Math.PI*rf*cf);
                    var score = DifferentiatorErrorScore(actualKd/kd, actualFirst/first, actualSecond/second, errorPercent);
                    if(score<best.Score)best=(rin,cin,rf,cf,score);
                }
            }
        }
        if(!(best.Rin>0))throw new InvalidOperationException("No coupled R/C standard-value set realizes the bounded differentiator inside the selected ranges.");
        return(best.Rin,best.Cin,best.Rf,best.Cf);
    }

    private static double DifferentiatorErrorScore(double kdRatio, double firstRatio, double secondRatio, double errorPercent)
    {
        var kdLog = Math.Log(kdRatio);
        var firstLog = Math.Log(firstRatio);
        var secondLog = Math.Log(secondRatio);
        // Each pole's log-magnitude contribution lies between zero and log(actual/target).
        // Bound their combined error over the entire band, including accumulation in the tail.
        var minimumLog = kdLog + Math.Min(0, firstLog) + Math.Min(0, secondLog);
        var maximumLog = kdLog + Math.Max(0, firstLog) + Math.Max(0, secondLog);
        var magnitudeBoundDb = 20 / Math.Log(10) * Math.Max(Math.Abs(minimumLog), Math.Abs(maximumLog));
        // |d atan(f/fc) / d log(fc)| <= 1/2 for every frequency.
        var phaseBoundDegrees = 90 / Math.PI * (Math.Abs(firstLog) + Math.Abs(secondLog));
        var parameterError = Math.Max(Math.Abs(kdRatio - 1),
            Math.Max(Math.Abs(firstRatio - 1), Math.Abs(secondRatio - 1)));
        return Math.Max(parameterError / (errorPercent / 100),
            Math.Max(magnitudeBoundDb / (20 * Math.Log10(1 + errorPercent / 100) + .015),
                phaseBoundDegrees / Math.Max(.5, errorPercent * .75)));
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
    private static bool InRange(double value,double minimum,double maximum)=>double.IsFinite(value)&&value>=minimum&&value<=maximum;
    private static double SqLog(double value) { var x=Math.Log(value); return x*x; }
}
