using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

internal sealed class PiecewiseOperationSynthesizer
{
    public OperationSynthesisResult Synthesize(AnalogComputeDesignRequest request) => request.Operation switch
    {
        AnalogComputeOperationType.PrecisionHalfWaveRectifier => HalfWave(request),
        AnalogComputeOperationType.PrecisionFullWaveRectifier => FullWave(request),
        AnalogComputeOperationType.PrecisionLimiter => Limiter(request),
        _ => throw new NotSupportedException($"{request.Operation} is not a piecewise operation.")
    };

    private static OperationSynthesisResult HalfWave(AnalogComputeDesignRequest request)
    {
        ValidateRangeAndGain(request);
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
        ValidateRangeAndGain(request);
        var network = SelectFullWave(request.GainK, request.Components);
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
            new("Verification model", "Ideal-diode state sweep", "121 bipolar samples", "Crossover accuracy depends on real diode and op-amp dynamics.")
        ]);
    }

    private static OperationSynthesisResult Limiter(AnalogComputeDesignRequest request)
    {
        ValidateRangeAndGain(request);
        if (!(request.LowerOutputLimitVoltage < 0 && request.UpperOutputLimitVoltage > 0 &&
              request.LowerOutputLimitVoltage < request.UpperOutputLimitVoltage))
            throw new NotSupportedException("This precision limiter topology requires VLOW < 0 < VHIGH so both references can be derived independently from VEE/GND and VCC/GND.");
        if (request.UpperOutputLimitVoltage >= request.SupplyVccVoltage ||
            request.LowerOutputLimitVoltage <= request.SupplyVeeVoltage)
            throw new ArgumentException("Limiter thresholds must lie strictly inside VEE and VCC.");
        var gain = SelectRatio(request.GainK, request.Components);
        var high = SelectDivider(request.UpperOutputLimitVoltage / request.SupplyVccVoltage, request.Components);
        var low = SelectDivider(request.LowerOutputLimitVoltage / request.SupplyVeeVoltage, request.Components);
        var unity = SelectRatio(1, request.Components);
        var graph = BuildLimiter(gain, high, low, unity);
        AnalogStandardSeries? series = request.Components.OptimizeStandardValues ? request.Components.ResistorSeries : null;
        var components = RatioComponents(gain, request, "Linear-region gain").Concat(new[]
        {
            AnalogSynthesisSupport.Component("R3","VCC to VREF_HIGH","Resistance",high.TopTheory,high.Top,"ohm",series),
            AnalogSynthesisSupport.Component("R4","VREF_HIGH to GND","Resistance",high.BottomTheory,high.Bottom,"ohm",series),
            AnalogSynthesisSupport.Component("R5","VEE to VREF_LOW","Resistance",low.TopTheory,low.Top,"ohm",series),
            AnalogSynthesisSupport.Component("R6","VREF_LOW to GND","Resistance",low.BottomTheory,low.Bottom,"ohm",series),
            AnalogSynthesisSupport.Component("R7","Output polarity input","Resistance",unity.InputTheory,unity.Input,"ohm",series),
            AnalogSynthesisSupport.Component("R8","Output polarity feedback","Resistance",unity.FeedbackTheory,unity.Feedback,"ohm",series)
        }).ToArray();
        return new(graph, components, "Precision op-amp gain stage with dual diode reference limiter",
            $"Vout = clamp({request.GainK:G9}*Vin, {request.LowerOutputLimitVoltage:G9}, {request.UpperOutputLimitVoltage:G9})",
        [
            new("Linear gain", "K=Rf/Rin", $"target {request.GainK:G9}; selected {gain.Feedback/gain.Input:G9}"),
            new("Upper reference", "VREF_HIGH=VCC*Rbottom/(Rtop+Rbottom)", $"{request.SupplyVccVoltage*high.Bottom/(high.Top+high.Bottom):G9} V"),
            new("Lower reference", "VREF_LOW=VEE*Rbottom/(Rtop+Rbottom)", $"{request.SupplyVeeVoltage*low.Bottom/(low.Top+low.Bottom):G9} V"),
            new("Reference policy", "Both thresholds are constructed from VCC/GND/VEE", "No independent VREF input")
        ]);
    }

    private sealed record RatioChoice(double InputTheory, double FeedbackTheory, double Input, double Feedback);
    private sealed record DividerChoice(double TopTheory, double BottomTheory, double Top, double Bottom);
    private sealed record FullWaveChoice(
        double RectifierInputTheory,double RectifierFeedbackTheory,double DirectInputTheory,double RectifiedInputTheory,double SummerFeedbackTheory,
        double RectifierInput,double RectifierFeedback,double DirectInput,double RectifiedInput,double SummerFeedback)
    {
        public double PositiveGain=>SummerFeedback/DirectInput;
        public double NegativeGain=>(SummerFeedback/RectifiedInput)*(RectifierFeedback/RectifierInput)-PositiveGain;
    }

    private static FullWaveChoice SelectFullWave(double gain,ComponentSelectionConstraints constraints)
    {
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
        foreach(var ri in AnalogSynthesisSupport.Candidates(rectifierInputTheory,true,constraints))
        foreach(var rf in AnalogSynthesisSupport.Candidates(rectifierFeedbackTheory,true,constraints))
        foreach(var rd in AnalogSynthesisSupport.Candidates(directTheory,true,constraints))
        foreach(var rh in AnalogSynthesisSupport.Candidates(rectifiedTheory,true,constraints))
        foreach(var rs in AnalogSynthesisSupport.Candidates(feedbackTheory,true,constraints))
        {
            var positive=rs/rd;
            var negative=(rs/rh)*(rf/ri)-positive;
            var errorPositive=(positive-gain)/gain;
            var errorNegative=(negative-gain)/gain;
            var symmetry=(positive-negative)/gain;
            var spread=new[]{ri,rf,rd,rh,rs}.Max()/new[]{ri,rf,rd,rh,rs}.Min();
            var score=errorPositive*errorPositive+errorNegative*errorNegative+symmetry*symmetry+.000001*Math.Log(spread);
            if(score<best.Score)best=(ri,rf,rd,rh,rs,score);
        }
        return new(rectifierInputTheory,rectifierFeedbackTheory,directTheory,rectifiedTheory,feedbackTheory,best.Ri,best.Rf,best.Rd,best.Rh,best.Rs);
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
        var inputs = AnalogSynthesisSupport.Candidates(inputTheory, true, constraints);
        var feedbacks = AnalogSynthesisSupport.Candidates(feedbackTheory, true, constraints);
        var best = (Input:0d,Feedback:0d,Score:double.PositiveInfinity);
        foreach(var input in inputs) foreach(var feedback in feedbacks)
        {
            var error=Math.Abs(feedback/input-target)/target;
            var spread=Math.Abs(Math.Log(feedback/input));
            var score=error*error+.000001*spread;
            if(score<best.Score) best=(input,feedback,score);
        }
        return new(inputTheory,feedbackTheory,best.Input,best.Feedback);
    }

    private static DividerChoice SelectDivider(double fraction, ComponentSelectionConstraints constraints)
    {
        if (!(fraction > 0 && fraction < 1)) throw new ArgumentException("Reference divider fraction must lie between zero and one.");
        var bottomTheory=Math.Sqrt(constraints.ResistorMinimumOhm*constraints.ResistorMaximumOhm);
        var topTheory=bottomTheory*(1-fraction)/fraction;
        if(topTheory<constraints.ResistorMinimumOhm||topTheory>constraints.ResistorMaximumOhm)
        { topTheory=Math.Sqrt(constraints.ResistorMinimumOhm*constraints.ResistorMaximumOhm); bottomTheory=topTheory*fraction/(1-fraction); }
        var best=(Top:0d,Bottom:0d,Score:double.PositiveInfinity);
        foreach(var top in AnalogSynthesisSupport.Candidates(topTheory,true,constraints))
        foreach(var bottom in AnalogSynthesisSupport.Candidates(bottomTheory,true,constraints))
        {
            var error=Math.Abs(bottom/(top+bottom)-fraction);
            if(error<best.Score)best=(top,bottom,error);
        }
        return new(topTheory,bottomTheory,best.Top,best.Bottom);
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

    private static CircuitGraph BuildLimiter(RatioChoice gain,DividerChoice high,DividerChoice low,RatioChoice unity)
    {
        var b=Base();var r1=b.Add(ComponentFactory.Resistor("R1",gain.Input));var r2=b.Add(ComponentFactory.Resistor("R2",gain.Feedback));var r3=b.Add(ComponentFactory.Resistor("R3",high.Top));var r4=b.Add(ComponentFactory.Resistor("R4",high.Bottom));var r5=b.Add(ComponentFactory.Resistor("R5",low.Top));var r6=b.Add(ComponentFactory.Resistor("R6",low.Bottom));var r7=b.Add(ComponentFactory.Resistor("R7",unity.Input));var r8=b.Add(ComponentFactory.Resistor("R8",unity.Feedback));var u1=b.Add(ComponentFactory.OpAmp("U1"));var u2=b.Add(ComponentFactory.OpAmp("U2"));var dh=b.Add(ComponentFactory.Diode("D1"));var dl=b.Add(ComponentFactory.Diode("D2"));
        b.Connect("VIN",r1.GetPin("1"));b.Connect("SUM_GAIN",r1.GetPin("2"),r2.GetPin("1"),u1.GetPin("IN-"));b.Connect("GAIN_INV",r2.GetPin("2"),u1.GetPin("OUT"),r7.GetPin("1"));b.Connect("SUM_RECOVERY",r7.GetPin("2"),r8.GetPin("1"),u2.GetPin("IN-"));b.Connect("VOUT",r8.GetPin("2"),u2.GetPin("OUT"),dh.GetPin("A"),dl.GetPin("K"));b.Connect("VREF_HIGH",r3.GetPin("2"),r4.GetPin("1"),dh.GetPin("K"));b.Connect("VREF_LOW",r5.GetPin("2"),r6.GetPin("1"),dl.GetPin("A"));b.Connect("VCC",r3.GetPin("1"));b.Connect("VEE",r5.GetPin("1"));b.Connect("GND",r4.GetPin("2"),r6.GetPin("2"),u1.GetPin("IN+"),u2.GetPin("IN+"));AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u1);AnalogSynthesisSupport.ConnectOpAmpSupplies(b,u2);return b.Build();
    }

    private static void ValidateRangeAndGain(AnalogComputeDesignRequest request)
    {
        if (!(request.GainK>0&&double.IsFinite(request.GainK)))throw new ArgumentException("Gain K must be positive and finite.");
        if (!(request.MinimumInputVoltage<request.MaximumInputVoltage))throw new ArgumentException("Input minimum must be below input maximum.");
    }
}
