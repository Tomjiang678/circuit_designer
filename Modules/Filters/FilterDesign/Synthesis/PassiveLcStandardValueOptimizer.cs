using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public sealed class PassiveLcStandardValueOptimizer
{
    public IReadOnlyList<LcRealizationBranch> Optimize(IReadOnlyList<LcRealizationBranch> branches,ComponentSelectionConstraints constraints,NormalizedFilterSpecification specification)
    {
        if(!constraints.OptimizeStandardValues)return branches;
        return branches.Select(branch=>branch with { Parts=OptimizeParts(branch.Parts,constraints,specification) }).ToArray();
    }

    private static IReadOnlyList<LcRealizationPart> OptimizeParts(IReadOnlyList<LcRealizationPart> parts,ComponentSelectionConstraints x,NormalizedFilterSpecification specification)
    {
        if(parts.Count==2&&parts.Any(p=>p.ComponentType==ComponentType.Inductor)&&parts.Any(p=>p.ComponentType==ComponentType.Capacitor))
        {
            var l=parts.Single(p=>p.ComponentType==ComponentType.Inductor);var c=parts.Single(p=>p.ComponentType==ComponentType.Capacitor);var best=SelectResonator(l,c,x,specification.CenterAngularFrequency);
            return parts.Select(p=>p.ComponentType==ComponentType.Inductor?Select(p,best.L,x.InductorSeries,x.InductorTolerancePercent):Select(p,best.C,x.CapacitorSeries,x.CapacitorTolerancePercent)).ToArray();
        }
        return parts.Select(part=>part.ComponentType switch
        {
            ComponentType.Inductor=>SelectNearest(part,x.InductorSeries,x.InductorMinimumH,x.InductorMaximumH,x.InductorTolerancePercent),
            ComponentType.Capacitor=>SelectNearest(part,x.CapacitorSeries,x.CapacitorMinimumF,x.CapacitorMaximumF,x.CapacitorTolerancePercent),
            _=>part
        }).ToArray();
    }

    private static (double L,double C) SelectResonator(LcRealizationPart l,LcRealizationPart c,ComponentSelectionConstraints x,double targetW)
    {
        var ls=StandardValueSeries.GetNearby(l.Value,x.InductorSeries,x.InductorMinimumH,x.InductorMaximumH);var cs=StandardValueSeries.GetNearby(c.Value,x.CapacitorSeries,x.CapacitorMinimumF,x.CapacitorMaximumF);var best=(L:0d,C:0d,Score:double.PositiveInfinity);
        foreach(var lv in ls)foreach(var cv in cs){var resonance=1/Math.Sqrt(lv*cv);var score=2*Math.Abs((resonance-targetW)/targetW)+.25*Math.Abs((lv-l.Value)/l.Value)+.25*Math.Abs((cv-c.Value)/c.Value);if(score<best.Score)best=(lv,cv,score);}
        if(best.L==0)throw new InvalidOperationException("No LC resonator standard-value pair was found.");
        var error=Math.Abs((1/Math.Sqrt(best.L*best.C)-targetW)/targetW);if(error>.10)throw new InvalidOperationException($"Selected LC resonator values exceed the 10% center-frequency error limit ({error:P2}).");
        return(best.L,best.C);
    }
    private static LcRealizationPart SelectNearest(LcRealizationPart part,StandardComponentSeries series,double min,double max,double tolerance)=>Select(part,StandardValueSeries.GetNearby(part.Value,series,min,max).MinBy(v=>Math.Abs(Math.Log(v/part.Value))),series,tolerance);
    private static LcRealizationPart Select(LcRealizationPart part,double value,StandardComponentSeries series,double tolerance)=>part with { Value=value,SelectedSeries=series,TolerancePercent=tolerance };
}
