using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public sealed class SallenKeyBandPassSectionCalculator
{
    public SynthesizedSection Calculate(AnalogFilterSection section, FilterDesignRequest request)
    {
        if (section.Order != SectionOrder.Second || section.ResponseType != FilterResponseType.BandPass) throw new NotSupportedException("Sallen-Key band-pass requires a second-order band-pass section.");
        var q=section.Q; var innerGain=(6.5-1/q)/3;
        if (innerGain <= 1) throw new NotSupportedException("This Sallen-Key band-pass realization requires Q > 0.2857 so its non-inverting gain exceeds unity.");
        var naturalCenterGain=innerGain*q; var attenuation=section.SectionGain/naturalCenterGain;
        if (attenuation <= 0 || attenuation > 1) throw new NotSupportedException("The Sallen-Key band-pass section requires gain greater than its Q-coupled natural gain; passive output normalization cannot realize it.");
        var x=request.ComponentConstraints; var w=2*Math.PI*section.PoleFrequencyHz;
        var factors=new[]{1d,1d/3,2d,1d,1d/(innerGain-1),(1-attenuation)/attenuation,1d};
        var capacitorCoefficients=new[]{2/w,1/w};
        var scaleMinimum=Math.Max(factors.Max(f=>x.ResistorMinimumOhm/f),capacitorCoefficients.Max(c=>c/x.CapacitorMaximumF));
        var scaleMaximum=Math.Min(factors.Min(f=>x.ResistorMaximumOhm/f),capacitorCoefficients.Min(c=>c/x.CapacitorMinimumF));
        if(scaleMinimum>scaleMaximum)throw new InvalidOperationException($"This Sallen-Key band-pass section cannot fit the selected R/C ranges (f0={section.PoleFrequencyHz:0.###} Hz, Q={q:0.###}).");
        var scale=Math.Sqrt(scaleMinimum*scaleMaximum);
        var c1=2/(w*scale); var r1=scale; var theory=new Candidate(r1,r1/3,2*r1,c1,c1/2,scale,scale/(innerGain-1),scale*(1-attenuation)/attenuation,scale);
        var selection=x.OptimizeStandardValues?Optimize(theory,section,x):new Selection(theory,theory);
        theory=selection.Theory; var best=selection.Selected; var actual=Verify(best);
        var fe=Rel(actual.FrequencyHz,section.PoleFrequencyHz);var qe=Rel(actual.Q,q);var ge=Rel(actual.GainMagnitude,section.SectionGain);
        if(Math.Abs(fe)>.08||Math.Abs(qe)>.12||Math.Abs(ge)>.08)throw new InvalidOperationException($"Sallen-Key BP values exceed error limits (f0 {fe:P2}, Q {qe:P2}, gain {ge:P2}).");
        return new(section,[
            V("R1",ComponentType.Resistor,ComponentParameterNames.ResistanceOhm,theory.R1,best.R1,x.ResistorSeries,x.ResistorTolerancePercent),V("R2",ComponentType.Resistor,ComponentParameterNames.ResistanceOhm,theory.R2,best.R2,x.ResistorSeries,x.ResistorTolerancePercent),V("R3",ComponentType.Resistor,ComponentParameterNames.ResistanceOhm,theory.R3,best.R3,x.ResistorSeries,x.ResistorTolerancePercent),
            V("C1",ComponentType.Capacitor,ComponentParameterNames.CapacitanceFarad,theory.C1,best.C1,x.CapacitorSeries,x.CapacitorTolerancePercent),V("C2",ComponentType.Capacitor,ComponentParameterNames.CapacitanceFarad,theory.C2,best.C2,x.CapacitorSeries,x.CapacitorTolerancePercent),
            V("R4",ComponentType.Resistor,ComponentParameterNames.ResistanceOhm,theory.R4,best.R4,x.ResistorSeries,x.ResistorTolerancePercent),V("R5",ComponentType.Resistor,ComponentParameterNames.ResistanceOhm,theory.R5,best.R5,x.ResistorSeries,x.ResistorTolerancePercent),
            V("RA",ComponentType.Resistor,ComponentParameterNames.ResistanceOhm,theory.Ra,best.Ra,x.ResistorSeries,x.ResistorTolerancePercent),V("RB",ComponentType.Resistor,ComponentParameterNames.ResistanceOhm,theory.Rb,best.Rb,x.ResistorSeries,x.ResistorTolerancePercent)
        ],actual.FrequencyHz,actual.Q,actual.GainMagnitude,fe,qe,ge);
    }

    public static MfbActualParameters Verify(double r1,double r2,double r3,double c1,double c2,double r4,double r5,double ra,double rb)=>Verify(new(r1,r2,r3,c1,c2,r4,r5,ra,rb));
    private static MfbActualParameters Verify(Candidate p)
    {
        if(new[]{p.R1,p.R2,p.R3,p.C1,p.C2,p.R4,p.R5,p.Ra,p.Rb}.Any(v=>v<=0||!double.IsFinite(v)))throw new ArgumentOutOfRangeException(nameof(p));
        var h=1+p.R4/p.R5; var a=(p.C1/p.R3+(p.C1+p.C2)/p.R1+p.C2/p.R2+p.C1*(1-h)/p.R2)/(p.C1*p.C2);
        var w2=(p.R1+p.R2)/(p.R1*p.R2*p.R3*p.C1*p.C2); var w=Math.Sqrt(w2); var q=w/a;
        var centerGain=(h/(p.R1*p.C2))*q/w; var divider=p.Rb/(p.Ra+p.Rb);
        return new(w/(2*Math.PI),q,centerGain*divider);
    }

    private static Selection Optimize(Candidate t,AnalogFilterSection s,ComponentSelectionConstraints x)
    {
        IReadOnlyList<double> R(double v)=>StandardValueSeries.GetNearby(v,x.ResistorSeries,x.ResistorMinimumOhm,x.ResistorMaximumOhm)
            .OrderBy(candidate=>Math.Abs(Math.Log(candidate/v))).Take(3).ToArray();
        IReadOnlyList<double> C(double v)=>StandardValueSeries.GetNearby(v,x.CapacitorSeries,x.CapacitorMinimumF,x.CapacitorMaximumF)
            .OrderBy(candidate=>Math.Abs(Math.Log(candidate/v))).Take(3).ToArray();
        Candidate? best=null; Candidate? bestTheory=null;var score=double.PositiveInfinity;
        foreach(var scale in EquivalentScales(t,x))
        {
            var q=new Candidate(t.R1*scale,t.R2*scale,t.R3*scale,t.C1/scale,t.C2/scale,t.R4*scale,t.R5*scale,t.Ra*scale,t.Rb*scale);
            foreach(var r1 in R(q.R1))foreach(var r2 in R(q.R2))foreach(var r3 in R(q.R3))foreach(var c1 in C(q.C1))foreach(var c2 in C(q.C2))foreach(var r4 in R(q.R4))foreach(var r5 in R(q.R5))foreach(var ra in R(q.Ra))foreach(var rb in R(q.Rb))
            {
                var p=new Candidate(r1,r2,r3,c1,c2,r4,r5,ra,rb);var a=Verify(p);if(!double.IsFinite(a.Q)||a.Q<=0)continue;
                var z=Math.Abs(Rel(a.FrequencyHz,s.PoleFrequencyHz))+1.5*Math.Abs(Rel(a.Q,s.Q))+2*Math.Abs(Rel(a.GainMagnitude,s.SectionGain));
                if(z<score){score=z;best=p;bestTheory=q;}
            }
        }
        return new(bestTheory??throw new InvalidOperationException("No valid standard-value Sallen-Key band-pass scale was found."),
            best??throw new InvalidOperationException("No valid standard-value Sallen-Key band-pass candidate was found."));
    }

    private static IEnumerable<double> EquivalentScales(Candidate theory,ComponentSelectionConstraints x)
    {
        var resistors=new[]{theory.R1,theory.R2,theory.R3,theory.R4,theory.R5,theory.Ra,theory.Rb};
        var capacitors=new[]{theory.C1,theory.C2};
        var minimum=Math.Max(resistors.Max(value=>x.ResistorMinimumOhm/value),capacitors.Max(value=>value/x.CapacitorMaximumF));
        var maximum=Math.Min(resistors.Min(value=>x.ResistorMaximumOhm/value),capacitors.Min(value=>value/x.CapacitorMinimumF));
        if(minimum>maximum)yield break;
        const int samples=3;
        for(var index=0;index<samples;index++)yield return minimum*Math.Pow(maximum/minimum,index/(samples-1d));
        if(minimum<=1&&maximum>=1)yield return 1;
    }

    private static SynthesizedComponentValue V(string role,ComponentType type,string parameter,double theory,double selected,StandardComponentSeries series,double tolerance)=>new(role,type,parameter,theory,selected,selected==theory?null:series,tolerance);
    private static double Rel(double a,double t)=>(a-t)/t;
    private sealed record Candidate(double R1,double R2,double R3,double C1,double C2,double R4,double R5,double Ra,double Rb);
    private sealed record Selection(Candidate Theory,Candidate Selected);
}
