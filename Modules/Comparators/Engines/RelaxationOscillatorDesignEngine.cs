using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Comparators.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

namespace CircuitModuleDesigner.Modules.Comparators.Engines;

internal sealed class RelaxationOscillatorDesignEngine
{
    private const double HysteresisFraction=1d/3;
    private const double CapacitorMinimumFarad=100e-12;
    private const double CapacitorMaximumFarad=10e-6;
    private const double PreferredTimingResistanceOhm=100_000;

    public ComparatorDesignResult Synthesize(ComparatorDesignRequest request)
    {
        var targetDuty=request.DutyCyclePercent/100;
        var lowerFraction=SolveLowerFraction(targetDuty);
        var upperFraction=lowerFraction+HysteresisFraction;
        var outputSpan=request.OutputHighVoltage-request.OutputLowVoltage;
        var effective=request with
        {
            Kind=ComparatorDesignerKind.RelaxationOscillator,
            Topology=SchmittTopology.Inverting,
            LowerThresholdVoltage=request.OutputLowVoltage+lowerFraction*outputSpan,
            UpperThresholdVoltage=request.OutputLowVoltage+upperFraction*outputSpan
        };
        var thresholdDesign=ComparatorDesignEngine.SynthesizeSchmitt(effective);
        var thresholds=thresholdDesign.ActualThresholds!;
        var logarithmicFactor=PeriodFactor(effective,thresholds);
        var targetRc=1/(request.TargetFrequencyHz*logarithmicFactor);
        var timing=SelectTimingPair(effective,targetRc,logarithmicFactor);
        var highTime=timing.Resistance*timing.Capacitance*HighFactor(effective,thresholds);
        var lowTime=timing.Resistance*timing.Capacitance*LowFactor(effective,thresholds);
        var actualFrequency=1/(highTime+lowTime);
        var actualDuty=100*highTime/(highTime+lowTime);
        var timingResistor=new ComparatorSelectedResistor("RT",timing.ContinuousResistance,timing.Resistance);
        var timingCapacitor=new ComparatorSelectedCapacitor("CT",timing.ContinuousCapacitance,timing.Capacitance);
        var resistors=thresholdDesign.Resistors.Concat([timingResistor]).ToArray();
        var graph=BuildGraph(effective,resistors,timingCapacitor);
        var trace=thresholdDesign.CalculationTrace.Concat(new[]
        {
            $"Automatic threshold placement: normalized hysteresis={HysteresisFraction:G8}; target duty={request.DutyCyclePercent:G8}%; VTL={effective.LowerThresholdVoltage:G8} V, VTH={effective.UpperThresholdVoltage:G8} V.",
            $"Timing equation: T=RT*CT*[ln((VOH-VTL)/(VOH-VTH))+ln((VTH-VOL)/(VTL-VOL))]; required RT*CT={targetRc:G9} s.",
            $"Selected timing pair: RT={timing.Resistance:G9} ohm, CT={timing.Capacitance:G9} F; actual f={actualFrequency:G9} Hz, duty={actualDuty:G7}%.",
            "Frequency and duty calculations use an ideal zero-delay push-pull comparator model."
        }).ToArray();
        return new ComparatorDesignResult
        {
            Request=effective,Status=ComparatorDesignStatus.Pass,Message="Synthesized",CircuitGraph=graph,
            ActualThresholds=thresholds,BiasNodeVoltage=thresholdDesign.BiasNodeVoltage,
            Resistors=resistors,Capacitors=[timingCapacitor],CalculationTrace=trace,
            Oscillator=new(request.TargetFrequencyHz,actualFrequency,request.DutyCyclePercent,actualDuty,highTime,lowTime)
        };
    }

    private static double SolveLowerFraction(double targetDuty)
    {
        var low=1e-6;
        var high=1-HysteresisFraction-1e-6;
        for(var iteration=0;iteration<100;iteration++)
        {
            var middle=(low+high)/2;
            if(NormalizedDuty(middle)<targetDuty)low=middle;else high=middle;
        }
        return (low+high)/2;
    }

    private static double NormalizedDuty(double lower)
    {
        var upper=lower+HysteresisFraction;
        var high=Math.Log((1-lower)/(1-upper));
        var low=Math.Log(upper/lower);
        return high/(high+low);
    }

    private static (double ContinuousResistance,double ContinuousCapacitance,double Resistance,double Capacitance) SelectTimingPair(
        ComparatorDesignRequest request,double targetRc,double logarithmicFactor)
    {
        var feasibleMinimum=Math.Max(CapacitorMinimumFarad,targetRc/request.MaximumResistanceOhm);
        var feasibleMaximum=Math.Min(CapacitorMaximumFarad,targetRc/request.MinimumResistanceOhm);
        if(feasibleMinimum>feasibleMaximum)
            throw new InvalidOperationException("The requested frequency has no timing R-C pair inside the supported component ranges.");
        var continuousCapacitance=Math.Clamp(targetRc/PreferredTimingResistanceOhm,feasibleMinimum,feasibleMaximum);
        var continuousResistance=targetRc/continuousCapacitance;
        if(!request.OptimizeStandardValues)
            return (continuousResistance,continuousCapacitance,continuousResistance,continuousCapacitance);
        var capacitors=StandardValueSeries.GetNearby(continuousCapacitance,StandardComponentSeries.E24,
            CapacitorMinimumFarad,CapacitorMaximumFarad,16);
        var candidates=new List<(double R,double C,double Score)>();
        foreach(var capacitor in capacitors)
        {
            var desiredResistance=targetRc/capacitor;
            if(desiredResistance<request.MinimumResistanceOhm||desiredResistance>request.MaximumResistanceOhm)continue;
            foreach(var resistor in StandardValueSeries.GetNearby(desiredResistance,request.ResistorSeries,
                        request.MinimumResistanceOhm,request.MaximumResistanceOhm,8))
            {
                var frequency=1/(resistor*capacitor*logarithmicFactor);
                var error=Math.Abs(frequency/request.TargetFrequencyHz-1);
                var preference=1e-5*Math.Abs(Math.Log(resistor/PreferredTimingResistanceOhm));
                candidates.Add((resistor,capacitor,error+preference));
            }
        }
        if(candidates.Count==0)throw new InvalidOperationException("No standard timing R-C pair exists inside the supported component ranges.");
        var best=candidates.MinBy(candidate=>candidate.Score);
        return (continuousResistance,continuousCapacitance,best.R,best.C);
    }

    private static double PeriodFactor(ComparatorDesignRequest request,ComparatorThresholds thresholds)=>
        HighFactor(request,thresholds)+LowFactor(request,thresholds);

    private static double HighFactor(ComparatorDesignRequest request,ComparatorThresholds thresholds)=>
        Math.Log((request.OutputHighVoltage-thresholds.Lower)/(request.OutputHighVoltage-thresholds.Upper));

    private static double LowFactor(ComparatorDesignRequest request,ComparatorThresholds thresholds)=>
        Math.Log((thresholds.Upper-request.OutputLowVoltage)/(thresholds.Lower-request.OutputLowVoltage));

    private static CircuitGraph BuildGraph(ComparatorDesignRequest request,IReadOnlyList<ComparatorSelectedResistor> values,
        ComparatorSelectedCapacitor timingCapacitor)
    {
        double R(string reference)=>values.Single(item=>item.Reference==reference).SelectedOhm;
        var builder=new CircuitBuilder();
        var comparator=builder.Add(ComponentFactory.Comparator("U1",new GenericComparatorModel(
            request.OutputLowVoltage,request.OutputHighVoltage,request.OutputBehavior).Validate()));
        var feedback=builder.Add(ComponentFactory.Resistor("RFB",R("RFB")));
        var reference=builder.Add(ComponentFactory.Resistor("RREF",R("RREF")));
        var dividerTop=builder.Add(ComponentFactory.Resistor("RVT",R("RVT")));
        var dividerBottom=builder.Add(ComponentFactory.Resistor("RVB",R("RVB")));
        var timingResistor=builder.Add(ComponentFactory.Resistor("RT",R("RT")));
        var capacitor=builder.Add(ComponentFactory.Capacitor("CT",timingCapacitor.SelectedFarad));
        var lowRail=request.SupplyLowVoltage==0?"GND":"VEE";
        builder.Connect("VCC",dividerTop.GetPin("1"),comparator.GetPin("V+"));
        builder.Connect("NBIAS",dividerTop.GetPin("2"),dividerBottom.GetPin("1"),reference.GetPin("1"));
        builder.Connect(lowRail,dividerBottom.GetPin("2"),capacitor.GetPin("2"),comparator.GetPin("V-"));
        builder.Connect("NTH",reference.GetPin("2"),feedback.GetPin("2"),comparator.GetPin("IN+"));
        builder.Connect("NCAP",timingResistor.GetPin("2"),capacitor.GetPin("1"),comparator.GetPin("IN-"));
        builder.Connect("VOUT",comparator.GetPin("OUT"),feedback.GetPin("1"),timingResistor.GetPin("1"));
        return builder.Build();
    }
}
