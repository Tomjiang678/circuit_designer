using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

internal sealed class PiecewiseOperationSynthesizer
{
    public OperationSynthesisResult Synthesize(AnalogComputeDesignRequest request) => request.Operation switch
    {
        AnalogComputeOperationType.PrecisionHalfWaveRectifier => HalfWave(request),
        AnalogComputeOperationType.PrecisionFullWaveRectifier => FullWave(request),
        _ => throw new NotSupportedException($"{request.Operation} is not a piecewise operation.")
    };

    private static OperationSynthesisResult HalfWave(AnalogComputeDesignRequest request)
    {
        var ratio = SelectRatio(request.GainK, request.Components);
        // The feedback-clamped core conducts for a negative drive voltage.  A
        // unity input inverter is therefore required for positive half-wave.
        var reverseInput = request.HalfWavePolarity == HalfWavePolarity.Positive;
        var unity = reverseInput ? SelectRatio(1, request.Components) : null;
        var graph = BuildHalfWave(ratio.Input, ratio.Feedback, unity);
        var sign = request.HalfWavePolarity == HalfWavePolarity.Positive ? "" : "-";
        var components = RatioComponents(ratio, request, "Rectifier").ToList();
        if (unity is not null)
            components.AddRange(RatioComponents(unity, request, "Input polarity recovery", 3));
        return new(graph, components,
            "Two-diode feedback-clamped precision half-wave rectifier",
            $"Vout = {request.GainK:G9}*max({sign}Vin,0)",
        [
            new("Conducting gain", "K=Rf/Rin", $"target {request.GainK:G9}; selected {ratio.Feedback/ratio.Input:G9}"),
            new("Diode states", "D1 conducts on the selected half-cycle; D2 preserves local feedback on the blocked half-cycle", request.HalfWavePolarity.ToString()),
            new("Verification model", "Ideal-switch topology state sweep", "101 samples across the entered Vin range", "Real zero-crossing and recovery depend on diode/op-amp selection.")
        ]);
    }

    private static OperationSynthesisResult FullWave(AnalogComputeDesignRequest request)
    {
        var network = SelectFullWave(request);
        var negativeOutput=request.OutputPolarity==RectifierOutputPolarity.Negative;
        var recovery = negativeOutput ? null : SelectRatio(1, request.Components);
        var graph = BuildFullWave(network, recovery);
        var polarity = request.OutputPolarity == RectifierOutputPolarity.Positive ? 1 : -1;
        AnalogStandardSeries? series = request.Components.OptimizeStandardValues ? request.Components.ResistorSeries : null;
        var components = new List<SelectedAnalogComponent>
        {
            AnalogSynthesisSupport.Component("R1","Precision half-wave input","Resistance",network.RectifierInputTheory,network.RectifierInput,"ohm",series),
            AnalogSynthesisSupport.Component("R2","Precision half-wave feedback","Resistance",network.RectifierFeedbackTheory,network.RectifierFeedback,"ohm",series),
            AnalogSynthesisSupport.Component("R3","Direct Vin summing path","Resistance",network.DirectInputTheory,network.DirectInput,"ohm",series),
            AnalogSynthesisSupport.Component("R4","Rectified 2K summing path","Resistance",network.RectifiedInputTheory,network.RectifiedInput,"ohm",series),
            AnalogSynthesisSupport.Component("R5","Absolute-value summer feedback","Resistance",network.SummerFeedbackTheory,network.SummerFeedback,"ohm",series)
        };
        if(recovery is not null)
        {
            components.Add(AnalogSynthesisSupport.Component("R6","Positive-output recovery input","Resistance",recovery.InputTheory,recovery.Input,"ohm",series));
            components.Add(AnalogSynthesisSupport.Component("R7","Positive-output recovery feedback","Resistance",recovery.FeedbackTheory,recovery.Feedback,"ohm",series));
        }
        return new(graph, components, "Precision half-wave core with weighted absolute-value summer",
            $"Vout = {polarity*request.GainK:G9}*abs(Vin)",
        [
            new("Absolute-value identity", "|Vin| = Vin + 2*max(-Vin,0)", "The half-wave core and both summer ratios are selected together."),
            new("Half-axis matching", "K+=R5/R3; K-=(R5/R4)*(R2/R1)-R5/R3", $"target {request.GainK:G9}; selected +{network.PositiveGain:G9}/-{network.NegativeGain:G9}"),
            new("Standard-value optimization audit","Coupled rectifier ratio + weighted-summer ratio + impedance-scale search",
                $"evaluated {network.Evaluated} candidates; scalar-feasible {network.ScalarFeasible}; best normalized score {network.Score:G9}",request.Components.OptimizeStandardValues?"Strict selected E-series search.":"Optimize=false continuous component path."),
            new("Verification model", "Ideal-diode state sweep", "121 bipolar samples", "Crossover accuracy depends on real diode and op-amp dynamics.")
        ]);
    }

    private sealed record RatioChoice(double InputTheory, double FeedbackTheory, double Input, double Feedback);
    private sealed record FullWaveChoice(
        double RectifierInputTheory,double RectifierFeedbackTheory,double DirectInputTheory,double RectifiedInputTheory,double SummerFeedbackTheory,
        double RectifierInput,double RectifierFeedback,double DirectInput,double RectifiedInput,double SummerFeedback,
        long Evaluated,long ScalarFeasible,double Score)
    {
        public double PositiveGain=>SummerFeedback/DirectInput;
        public double NegativeGain=>(SummerFeedback/RectifiedInput)*(RectifierFeedback/RectifierInput)-PositiveGain;
    }

    private sealed record StandardRatio(double Ratio,double Input,double Feedback);

    private static FullWaveChoice SelectFullWave(AnalogComputeDesignRequest request)
    {
        var gain=request.GainK;var constraints=request.Components;
        var feedbackTheory=Math.Sqrt(constraints.ResistorMinimumOhm*constraints.ResistorMaximumOhm);
        var rectifierInputTheory=feedbackTheory;
        var rectifierFeedbackTheory=feedbackTheory;
        var directTheory=feedbackTheory/gain;
        var rectifiedTheory=feedbackTheory/(2*gain);
        if(new[]{directTheory,rectifiedTheory}.Any(value=>value<constraints.ResistorMinimumOhm||value>constraints.ResistorMaximumOhm))
        {
            directTheory=Math.Sqrt(constraints.ResistorMinimumOhm*constraints.ResistorMaximumOhm);
            feedbackTheory=directTheory*gain;
            rectifiedTheory=feedbackTheory/(2*gain);
        }
        var best=(Ri:0d,Rf:0d,Rd:0d,Rh:0d,Rs:0d,Score:double.PositiveInfinity);
        var summerCandidates=constraints.OptimizeStandardValues
            ? AnalogPreferredValueSeries.All(constraints.ResistorSeries,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm)
            : [feedbackTheory];
        var allValues=constraints.OptimizeStandardValues
            ? AnalogPreferredValueSeries.All(constraints.ResistorSeries,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm)
            : [rectifierInputTheory,rectifierFeedbackTheory];
        var standardRatios=BuildStandardRatios(allValues);
        var gainTolerance=Math.Max(1e-9,gain*request.MaximumFunctionErrorPercent/100);
        var positivePeak=Math.Max(0,request.MaximumInputVoltage);
        var negativePeak=Math.Max(0,-request.MinimumInputVoltage);
        var targetPeak=gain*Math.Max(positivePeak,negativePeak);
        var voltageTolerance=Math.Max(1e-6,Math.Max(1,targetPeak)*request.MaximumFunctionErrorPercent/100);
        long evaluated=0,scalarFeasible=0;
        void Evaluate(bool feasibleOnly)
        {
        foreach(var rs in summerCandidates)
        {
            var scaledDirect=rs/gain;var scaledRectified=rs/(2*gain);
            if(scaledDirect<constraints.ResistorMinimumOhm||scaledDirect>constraints.ResistorMaximumOhm)continue;
            var positiveVoltageTolerance=positivePeak>0?voltageTolerance/positivePeak:double.PositiveInfinity;
            var positiveLimit=Math.Min(gainTolerance,positiveVoltageTolerance);
            var directCandidates=!constraints.OptimizeStandardValues?[scaledDirect]
                :feasibleOnly?allValues.Where(value=>Math.Abs(rs/value-gain)<=positiveLimit+1e-12).ToArray()
                :AnalogPreferredValueSeries.Nearby(scaledDirect,allValues,3).ToArray();
            if(directCandidates.Length==0)continue;
            // Rh is not an independent nearest-value choice: it couples the absolute-value
            // summer ratio to the rectifier Ri/Rf ratio. Search every bounded series value.
            var rectifiedCandidates=constraints.OptimizeStandardValues?allValues:[scaledRectified];
            foreach(var rd in directCandidates)
            foreach(var rh in rectifiedCandidates)
            {
                var positive=rs/rd;
                var negativeVoltageTolerance=negativePeak>0?voltageTolerance/negativePeak:double.PositiveInfinity;
                var negativeLow=Math.Max(gain-gainTolerance,Math.Max(positive-gainTolerance,gain-negativeVoltageTolerance));
                var negativeHigh=Math.Min(gain+gainTolerance,Math.Min(positive+gainTolerance,gain+negativeVoltageTolerance));
                var ratioLow=(negativeLow+positive)*rh/rs;
                var ratioHigh=(negativeHigh+positive)*rh/rs;
                var desiredNegative=(gain+positive)/2;
                var desiredRectifierRatio=(desiredNegative+positive)*rh/rs;
                // For fixed Rs/Rd/Rh every verification error is monotone in the
                // distance between the rectifier ratio and desiredRectifierRatio.
                // Therefore the two standard ratios bracketing that target contain
                // the optimum, and are also sufficient to prove whether the
                // admissible [ratioLow,ratioHigh] interval contains a solution.
                // This preserves the complete pass/fail search while avoiding a
                // repeated scan of every Ri/Rf ratio for every impedance scale.
                var ratioCandidates=BracketingRatios(standardRatios,desiredRectifierRatio,
                    feasibleOnly?ratioLow:double.NegativeInfinity,
                    feasibleOnly?ratioHigh:double.PositiveInfinity);
                foreach(var ratio in ratioCandidates)
                {
                    evaluated++;
                    var negative=(rs/rh)*ratio.Ratio-positive;
                    var positiveError=Math.Abs(positive-gain);
                    var negativeError=Math.Abs(negative-gain);
                    var symmetry=Math.Abs(positive-negative);
                    var maximumTransferError=Math.Max(positiveError*positivePeak,negativeError*negativePeak);
                    var worst=Math.Max(Math.Max(positiveError/gainTolerance,negativeError/gainTolerance),
                        Math.Max(symmetry/gainTolerance,maximumTransferError/voltageTolerance));
                    if(worst<=1+1e-12)scalarFeasible++;
                    var sum=(positiveError+negativeError+symmetry)/gainTolerance+maximumTransferError/voltageTolerance;
                    var spread=new[]{ratio.Input,ratio.Feedback,rd,rh,rs}.Max()/new[]{ratio.Input,ratio.Feedback,rd,rh,rs}.Min();
                    var score=worst+.001*sum+1e-9*Math.Log(spread);
                    if(score<best.Score)best=(ratio.Input,ratio.Feedback,rd,rh,rs,score);
                }
            }
        }
        }
        Evaluate(true);
        if(scalarFeasible==0)Evaluate(false);
        if(!(best.Ri>0))throw new InvalidOperationException("No joint full-wave E-series network exists inside the selected resistor range.");
        return new(rectifierInputTheory,rectifierFeedbackTheory,directTheory,rectifiedTheory,feedbackTheory,best.Ri,best.Rf,best.Rd,best.Rh,best.Rs,
            evaluated,scalarFeasible,best.Score);
    }

    private static IReadOnlyList<StandardRatio> BuildStandardRatios(IReadOnlyList<double> values)
    {
        var center=Math.Sqrt(values.Min()*values.Max());
        return values.SelectMany(input=>values.Select(feedback=>new StandardRatio(feedback/input,input,feedback)))
            .GroupBy(item=>item.Ratio.ToString("G12",System.Globalization.CultureInfo.InvariantCulture),StringComparer.Ordinal)
            .Select(group=>group.MinBy(item=>Math.Abs(Math.Log(Math.Sqrt(item.Input*item.Feedback)/center)))!)
            .OrderBy(item=>item.Ratio).ToArray();
    }

    private static IReadOnlyList<StandardRatio> BracketingRatios(
        IReadOnlyList<StandardRatio> ratios,double target,double minimum,double maximum)
    {
        if(minimum>maximum||ratios.Count==0)return [];
        var query=Math.Max(minimum,Math.Min(maximum,target));
        var low=0;var high=ratios.Count;
        while(low<high)
        {
            var middle=(low+high)/2;
            if(ratios[middle].Ratio<query)low=middle+1;else high=middle;
        }
        var candidates=new List<StandardRatio>(2);
        void Add(int index)
        {
            if(index<0||index>=ratios.Count)return;
            var candidate=ratios[index];
            if(candidate.Ratio<minimum-1e-12||candidate.Ratio>maximum+1e-12)return;
            if(candidates.Count==0||!ReferenceEquals(candidates[0],candidate))candidates.Add(candidate);
        }
        Add(low-1);Add(low);
        return candidates;
    }

    private static RatioChoice SelectRatio(double target, ComponentSelectionConstraints constraints)
    {
        var feedbackTheory = Math.Sqrt(constraints.ResistorMinimumOhm * constraints.ResistorMaximumOhm);
        var inputTheory = feedbackTheory / target;
        if (inputTheory < constraints.ResistorMinimumOhm || inputTheory > constraints.ResistorMaximumOhm)
        {
            inputTheory = Math.Sqrt(constraints.ResistorMinimumOhm * constraints.ResistorMaximumOhm);
            feedbackTheory = inputTheory * target;
        }
        var feedbacks=constraints.OptimizeStandardValues
            ? AnalogPreferredValueSeries.All(constraints.ResistorSeries,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm)
            : [feedbackTheory];
        var allValues=constraints.OptimizeStandardValues
            ? AnalogPreferredValueSeries.All(constraints.ResistorSeries,constraints.ResistorMinimumOhm,constraints.ResistorMaximumOhm)
            : [inputTheory];
        var best = (Input:0d,Feedback:0d,Score:double.PositiveInfinity);
        foreach(var feedback in feedbacks)
        {
            var desiredInput=feedback/target;
            if(desiredInput<constraints.ResistorMinimumOhm||desiredInput>constraints.ResistorMaximumOhm)continue;
            var input=constraints.OptimizeStandardValues?AnalogPreferredValueSeries.Nearest(desiredInput,allValues):desiredInput;
            var error=Math.Abs(feedback/input-target)/target;
            var spread=Math.Abs(Math.Log(feedback/input));
            var score=error*error+.000001*spread;
            if(score<best.Score) best=(input,feedback,score);
        }
        if(!(best.Input>0))throw new InvalidOperationException("No standard-value resistor ratio exists inside the selected range.");
        return new(inputTheory,feedbackTheory,best.Input,best.Feedback);
    }

    private static IReadOnlyList<SelectedAnalogComponent> RatioComponents(RatioChoice ratio, AnalogComputeDesignRequest request, string role, int firstReference=1)
    {
        AnalogStandardSeries? series=request.Components.OptimizeStandardValues?request.Components.ResistorSeries:null;
        return
        [
            AnalogSynthesisSupport.Component($"R{firstReference}",$"{role} input resistor","Resistance",ratio.InputTheory,ratio.Input,"ohm",series),
            AnalogSynthesisSupport.Component($"R{firstReference+1}",$"{role} feedback resistor","Resistance",ratio.FeedbackTheory,ratio.Feedback,"ohm",series)
        ];
    }

    private static CircuitBuilder Base()
    {
        var b=new CircuitBuilder(); foreach(var net in new[]{"GND","VCC","VEE","VIN","VOUT"})b.CreateNet(net); return b;
    }

    private static CircuitGraph BuildHalfWave(double rinValue,double rfValue,RatioChoice? polarityRecovery)
    {
        var b=Base(); var r1=b.Add(ComponentFactory.Resistor("R1",rinValue));var r2=b.Add(ComponentFactory.Resistor("R2",rfValue));var u=b.Add(ComponentFactory.OpAmp("U1"));var d1=b.Add(ComponentFactory.Diode("D1"));var d2=b.Add(ComponentFactory.Diode("D2"));
        var driveNet="VIN";
        if(polarityRecovery is not null)
        {
            var r3=b.Add(ComponentFactory.Resistor("R3",polarityRecovery.Input));var r4=b.Add(ComponentFactory.Resistor("R4",polarityRecovery.Feedback));var u2=b.Add(ComponentFactory.OpAmp("U2"));
            b.Connect("VIN",r3.GetPin("1"));b.Connect("SUM_POLARITY",r3.GetPin("2"),r4.GetPin("1"),u2.GetPin("IN-"));b.Connect("RECT_INPUT",r4.GetPin("2"),u2.GetPin("OUT"));b.Connect("GND",u2.GetPin("IN+"));AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u2);driveNet="RECT_INPUT";
        }
        b.Connect(driveNet,r1.GetPin("1"));b.Connect("SUM",r1.GetPin("2"),r2.GetPin("1"),u.GetPin("IN-"),d2.GetPin("A"));b.Connect("VOUT",r2.GetPin("2"),d1.GetPin("K"));b.Connect("U1_RAW",u.GetPin("OUT"),d1.GetPin("A"),d2.GetPin("K"));b.Connect("GND",u.GetPin("IN+"));AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u);return b.Build();
    }

    private static CircuitGraph BuildFullWave(FullWaveChoice values,RatioChoice? positiveRecovery)
    {
        var b=Base();
        var r1=b.Add(ComponentFactory.Resistor("R1",values.RectifierInput));var r2=b.Add(ComponentFactory.Resistor("R2",values.RectifierFeedback));
        var r3=b.Add(ComponentFactory.Resistor("R3",values.DirectInput));var r4=b.Add(ComponentFactory.Resistor("R4",values.RectifiedInput));var r5=b.Add(ComponentFactory.Resistor("R5",values.SummerFeedback));
        var u1=b.Add(ComponentFactory.OpAmp("U1"));var u2=b.Add(ComponentFactory.OpAmp("U2"));
        var d1=b.Add(ComponentFactory.Diode("D1"));var d2=b.Add(ComponentFactory.Diode("D2"));
        // U1/D1/D2: H=max(-Vin,0). D1 is RAW_RECT(A) -> HALF_RECT(K);
        // D2 is SUM_RECT(A) -> RAW_RECT(K), retaining feedback while D1 is off.
        b.Connect("VIN",r1.GetPin("1"),r3.GetPin("1"));
        b.Connect("SUM_RECT",r1.GetPin("2"),r2.GetPin("1"),u1.GetPin("IN-"),d2.GetPin("A"));
        b.Connect("HALF_RECT",r2.GetPin("2"),d1.GetPin("K"),r4.GetPin("1"));
        b.Connect("RAW_RECT",u1.GetPin("OUT"),d1.GetPin("A"),d2.GetPin("K"));
        b.Connect("SUM_ABS",r3.GetPin("2"),r4.GetPin("2"),r5.GetPin("1"),u2.GetPin("IN-"));
        var negativeAbsoluteNet=positiveRecovery is null?"VOUT":"NEG_ABS";
        b.Connect(negativeAbsoluteNet,r5.GetPin("2"),u2.GetPin("OUT"));
        b.Connect("GND",u1.GetPin("IN+"),u2.GetPin("IN+"));
        AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u1);AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u2);
        if(positiveRecovery is not null)
        {
            var r6=b.Add(ComponentFactory.Resistor("R6",positiveRecovery.Input));var r7=b.Add(ComponentFactory.Resistor("R7",positiveRecovery.Feedback));var u3=b.Add(ComponentFactory.OpAmp("U3"));
            b.Connect("NEG_ABS",r6.GetPin("1"));b.Connect("SUM_POLARITY",r6.GetPin("2"),r7.GetPin("1"),u3.GetPin("IN-"));
            b.Connect("VOUT",r7.GetPin("2"),u3.GetPin("OUT"));b.Connect("GND",u3.GetPin("IN+"));AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u3);
        }
        return b.Build();
    }

}
