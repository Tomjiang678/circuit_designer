using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public sealed class MfbBandPassSectionCalculator
{
    public SynthesizedSection Calculate(AnalogFilterSection section, FilterDesignRequest request)
    {
        if (section.Order != SectionOrder.Second || section.ResponseType != FilterResponseType.BandPass)
            throw new NotSupportedException("This MFB calculator implements second-order Band-Pass sections only.");
        if (section.Q <= Math.Sqrt(.5)) throw new NotSupportedException("The single-amplifier MFB band-pass section requires Q > 0.707.");
        var x = request.ComponentConstraints;
        var w0 = 2 * Math.PI * section.PoleFrequencyHz;
        var targetGain = section.SectionGain;
        if (targetGain <= 0 || targetGain >= 2 * section.Q * section.Q) throw new NotSupportedException("The requested MFB band-pass section gain is outside its positive-component realization range.");
        var factors=new[]{1d,2*targetGain,targetGain/(2*section.Q*section.Q-targetGain)};
        var capacitorCoefficient=section.Q/(targetGain*w0);
        var r1Minimum=Math.Max(factors.Max(f=>x.ResistorMinimumOhm/f),capacitorCoefficient/x.CapacitorMaximumF);
        var r1Maximum=Math.Min(factors.Min(f=>x.ResistorMaximumOhm/f),capacitorCoefficient/x.CapacitorMinimumF);
        if(r1Minimum>r1Maximum)throw new InvalidOperationException($"This MFB band-pass section cannot fit the selected R/C ranges (f0={section.PoleFrequencyHz:0.###} Hz, Q={section.Q:0.###}).");
        var r1=Math.Sqrt(r1Minimum*r1Maximum);
        var c = section.Q / (targetGain * w0 * r1); var theory = new Candidate(r1, 2 * targetGain * r1, targetGain * r1 / (2 * section.Q * section.Q - targetGain), c, c);
        var best = x.OptimizeStandardValues ? Optimize(theory, section, x) : theory;
        var actual = Verify(best.R1, best.R2, best.Rb, best.C1, best.C2);
        var fe = Relative(actual.FrequencyHz, section.PoleFrequencyHz); var qe = Relative(actual.Q, section.Q); var ge = Relative(actual.GainMagnitude, targetGain);
        if (Math.Abs(fe) > .08 || Math.Abs(qe) > .12 || Math.Abs(ge) > .08) throw new InvalidOperationException($"MFB band-pass values exceed error limits (f0 {fe:P2}, Q {qe:P2}, gain {ge:P2}).");
        return new(section,
        [
            V("R1", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory.R1, best.R1, x.ResistorSeries, x.ResistorTolerancePercent),
            V("R2", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory.R2, best.R2, x.ResistorSeries, x.ResistorTolerancePercent),
            V("RB", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory.Rb, best.Rb, x.ResistorSeries, x.ResistorTolerancePercent),
            V("C1", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory.C1, best.C1, x.CapacitorSeries, x.CapacitorTolerancePercent),
            V("C2", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory.C2, best.C2, x.CapacitorSeries, x.CapacitorTolerancePercent)
        ], actual.FrequencyHz, actual.Q, actual.GainMagnitude, fe, qe, ge);
    }

    public static MfbActualParameters Verify(double r1, double r2, double rb, double c1, double c2)
    {
        if (new[] { r1, r2, rb, c1, c2 }.Any(v => v <= 0 || !double.IsFinite(v))) throw new ArgumentOutOfRangeException(nameof(r1));
        var conductance = 1 / r1 + 1 / rb; var w0 = Math.Sqrt(conductance / (c1 * c2 * r2));
        var q = w0 * c1 * c2 * r2 / (c1 + c2); var gain = q / (w0 * r1 * c2);
        return new(w0 / (2 * Math.PI), q, gain);
    }

    private static Candidate Optimize(Candidate t, AnalogFilterSection s, ComponentSelectionConstraints x)
    {
        var sets = new[] { StandardValueSeries.GetNearby(t.R1,x.ResistorSeries,x.ResistorMinimumOhm,x.ResistorMaximumOhm), StandardValueSeries.GetNearby(t.R2,x.ResistorSeries,x.ResistorMinimumOhm,x.ResistorMaximumOhm), StandardValueSeries.GetNearby(t.Rb,x.ResistorSeries,x.ResistorMinimumOhm,x.ResistorMaximumOhm) };
        var ca=StandardValueSeries.GetNearby(t.C1,x.CapacitorSeries,x.CapacitorMinimumF,x.CapacitorMaximumF); var cb=StandardValueSeries.GetNearby(t.C2,x.CapacitorSeries,x.CapacitorMinimumF,x.CapacitorMaximumF);
        Candidate? best=null; var score=double.PositiveInfinity;
        foreach(var a in sets[0]) foreach(var b in sets[1]) foreach(var r in sets[2]) foreach(var c in ca) foreach(var d in cb){var p=Verify(a,b,r,c,d);var z=Math.Abs(Relative(p.FrequencyHz,s.PoleFrequencyHz))+1.5*Math.Abs(Relative(p.Q,s.Q))+.5*Math.Abs(Relative(p.GainMagnitude,s.SectionGain));if(z<score){score=z;best=new(a,b,r,c,d);}}
        return best ?? throw new InvalidOperationException("No valid MFB band-pass candidate was found.");
    }
    private static SynthesizedComponentValue V(string role,ComponentType type,string parameter,double theory,double selected,StandardComponentSeries series,double tolerance)=>new(role,type,parameter,theory,selected,selected==theory?null:series,tolerance);
    private static double Relative(double a,double t)=>(a-t)/t;
    private sealed record Candidate(double R1,double R2,double Rb,double C1,double C2);
}
