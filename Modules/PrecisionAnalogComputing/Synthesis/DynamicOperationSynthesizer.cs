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
        var best = SelectIntegrator(ki, leak, c0, request.Components,request.MaximumFunctionErrorPercent);
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
            ,new("Standard-value optimization audit","Coupled Rin-Cf and Rf-Cf product search",
                $"evaluated {best.Evaluated} candidates; scalar-feasible {best.ScalarFeasible}",request.Components.OptimizeStandardValues?"Strict selected E-series search.":"Optimize=false continuous component path.")
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
            ,new("Standard-value optimization audit","Coupled (Rin,Cin), (Rf,Cf), and Rf*Cin search",
                $"evaluated {best.Evaluated} candidates; scalar-feasible {best.ScalarFeasible}",request.Components.OptimizeStandardValues?"Strict selected E-series search.":"Optimize=false continuous component path.")
        ]);
    }

    private static (double Rin,double Rf,double Cf,long Evaluated,long ScalarFeasible) SelectIntegrator(double ki,double leak,double continuousCf,ComponentSelectionConstraints constraints,double errorPercent)
    {
        if(!constraints.OptimizeStandardValues)
            return(1/(ki*continuousCf),1/(2*Math.PI*leak*continuousCf),continuousCf,1,1);
        var capacitorCandidates=constraints.OptimizeStandardValues
            ? AnalogPreferredValueSeries.All(constraints.CapacitorSeries,constraints.CapacitorMinimumFarad,constraints.CapacitorMaximumFarad)
            : [continuousCf];
        var resistorCandidates=AnalogPreferredValueSeries.All(constraints.ResistorSeries,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm);
        var relativeTolerance=errorPercent/100;
        var best=(Rin:0d,Rf:0d,Cf:0d,Score:double.PositiveInfinity);
        long evaluated=0,scalarFeasible=0;
        foreach(var cf in capacitorCandidates)
        {
            var rinTheory=1/(ki*cf);var rfTheory=1/(2*Math.PI*leak*cf);
            if(!InRange(rinTheory,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm)||
               !InRange(rfTheory,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm))continue;
            var rins=AnalogPreferredValueSeries.ProductCandidates(cf,1/ki,resistorCandidates,relativeTolerance);
            var rfs=AnalogPreferredValueSeries.ProductCandidates(cf,1/(2*Math.PI*leak),resistorCandidates,relativeTolerance);
            foreach(var rin in rins)
            foreach(var rf in rfs)
            {
                evaluated++;
                var actualKi=1/(rin*cf);var actualLeak=1/(2*Math.PI*rf*cf);
                if(Math.Abs(actualKi/ki-1)<=relativeTolerance+1e-12&&Math.Abs(actualLeak/leak-1)<=relativeTolerance+1e-12)scalarFeasible++;
                var score=IntegratorErrorScore(ki,leak,actualKi,actualLeak,errorPercent)+1e-9*SqLog(rf/rin);
                if(score<best.Score)best=(rin,rf,cf,score);
            }
        }
        if(!(best.Rin>0))throw new InvalidOperationException("No coupled R/C standard-value set realizes the practical integrator inside the selected ranges.");
        return(best.Rin,best.Rf,best.Cf,evaluated,scalarFeasible);
    }

    private static double IntegratorErrorScore(double ki,double leak,double actualKi,double actualLeak,double errorPercent)
    {
        var relativeTolerance=errorPercent/100;
        var score=Math.Max(Math.Abs(actualKi/ki-1),Math.Abs(actualLeak/leak-1))/relativeTolerance;
        var start=Math.Max(.001,leak/10);var stop=leak*10_000;
        var allowedDb=20*Math.Log10(1+relativeTolerance)+.015;
        var allowedPhase=Math.Max(.5,errorPercent*.75);
        for(var index=0;index<=12;index++)
        {
            var frequency=start*Math.Pow(stop/start,index/12d);
            var target=DynamicOperationParameters.IntegratorTransfer(frequency,ki,leak);
            var actual=DynamicOperationParameters.IntegratorTransfer(frequency,actualKi,actualLeak);
            var magnitudeError=Math.Abs(20*Math.Log10(Math.Max(actual.Magnitude,1e-15))-20*Math.Log10(Math.Max(target.Magnitude,1e-15)));
            var phaseError=PhaseDifference(Math.Atan2(actual.Imaginary,actual.Real)*180/Math.PI,
                Math.Atan2(target.Imaginary,target.Real)*180/Math.PI);
            score=Math.Max(score,Math.Max(magnitudeError/allowedDb,phaseError/allowedPhase));
        }
        return score;
    }

    private static (double Rin,double Cin,double Rf,double Cf,long Evaluated,long ScalarFeasible) SelectDifferentiator(double kd,double first,double second,double continuousCin,ComponentSelectionConstraints constraints,double errorPercent)
    {
        if(!constraints.OptimizeStandardValues)
        {
            var rf=kd/continuousCin;var rin=1/(2*Math.PI*first*continuousCin);var cf=1/(2*Math.PI*second*rf);
            return(rin,continuousCin,rf,cf,1,1);
        }
        var capacitorCandidates=constraints.OptimizeStandardValues
            ? AnalogPreferredValueSeries.All(constraints.CapacitorSeries,constraints.CapacitorMinimumFarad,constraints.CapacitorMaximumFarad)
            : [continuousCin];
        var resistorCandidates=AnalogPreferredValueSeries.All(constraints.ResistorSeries,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm);
        var relativeTolerance=errorPercent/100;
        var best=(Rin:0d,Cin:0d,Rf:0d,Cf:0d,Score:double.PositiveInfinity);
        long evaluated=0,scalarFeasible=0;
        foreach(var cin in capacitorCandidates)
        {
            var rfTheory=kd/cin;var rinTheory=1/(2*Math.PI*first*cin);
            if(!InRange(rfTheory,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm)||
               !InRange(rinTheory,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm))continue;
            var rins=AnalogPreferredValueSeries.ProductCandidates(cin,1/(2*Math.PI*first),resistorCandidates,relativeTolerance);
            var rfs=AnalogPreferredValueSeries.ProductCandidates(cin,kd,resistorCandidates,relativeTolerance);
            foreach(var rin in rins)
            foreach(var rf in rfs)
            {
                var cfTheory=1/(2*Math.PI*second*rf);
                if(!InRange(cfTheory,constraints.CapacitorMinimumFarad,constraints.CapacitorMaximumFarad))continue;
                var cfs=AnalogPreferredValueSeries.ProductCandidates(rf,1/(2*Math.PI*second),capacitorCandidates,relativeTolerance);
                foreach(var cf in cfs)
                {
                    evaluated++;
                    var actualKd=rf*cin;var actualFirst=1/(2*Math.PI*rin*cin);var actualSecond=1/(2*Math.PI*rf*cf);
                    if(Math.Abs(actualKd/kd-1)<=relativeTolerance+1e-12&&Math.Abs(actualFirst/first-1)<=relativeTolerance+1e-12&&Math.Abs(actualSecond/second-1)<=relativeTolerance+1e-12)scalarFeasible++;
                    var score = DifferentiatorErrorScore(kd,first,second,actualKd,actualFirst,actualSecond,errorPercent);
                    if(score<best.Score)best=(rin,cin,rf,cf,score);
                }
            }
        }
        if(!(best.Rin>0))throw new InvalidOperationException("No coupled R/C standard-value set realizes the bounded differentiator inside the selected ranges.");
        return(best.Rin,best.Cin,best.Rf,best.Cf,evaluated,scalarFeasible);
    }

    private static double DifferentiatorErrorScore(double kd,double first,double second,
        double actualKd,double actualFirst,double actualSecond,double errorPercent)
    {
        var relativeTolerance=errorPercent/100;
        var score=Math.Max(Math.Abs(actualKd/kd-1),
            Math.Max(Math.Abs(actualFirst/first-1),Math.Abs(actualSecond/second-1)))/relativeTolerance;
        var start=Math.Max(.001,first/500);var stop=second*10;
        var allowedDb=20*Math.Log10(1+relativeTolerance)+.015;
        var allowedPhase=Math.Max(.5,errorPercent*.75);
        for(var index=0;index<=12;index++)
        {
            var frequency=start*Math.Pow(stop/start,index/12d);
            var target=DynamicOperationParameters.DifferentiatorTransfer(frequency,kd,first,second);
            var actual=DynamicOperationParameters.DifferentiatorTransfer(frequency,actualKd,actualFirst,actualSecond);
            var magnitudeError=Math.Abs(20*Math.Log10(Math.Max(actual.Magnitude,1e-15))-20*Math.Log10(Math.Max(target.Magnitude,1e-15)));
            var phaseError=PhaseDifference(Math.Atan2(actual.Imaginary,actual.Real)*180/Math.PI,
                Math.Atan2(target.Imaginary,target.Real)*180/Math.PI);
            score=Math.Max(score,Math.Max(magnitudeError/allowedDb,phaseError/allowedPhase));
        }
        return score;
    }

    private static double PhaseDifference(double first,double second)
    {
        var difference=Math.Abs(first-second)%360;
        return difference>180?360-difference:difference;
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
