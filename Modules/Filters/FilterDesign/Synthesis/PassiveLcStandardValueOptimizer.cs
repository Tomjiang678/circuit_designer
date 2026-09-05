using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public sealed class PassiveLcStandardValueOptimizer
{
    public IReadOnlyList<LcRealizationBranch> Optimize(
        IReadOnlyList<LcRealizationBranch> branches,
        ComponentSelectionConstraints constraints,
        NormalizedFilterSpecification specification,
        Func<IReadOnlyList<LcRealizationBranch>, double>? loadedCircuitScore = null)
    {
        EnsureTheoreticalValuesWithinRange(branches, constraints);
        if(!constraints.OptimizeStandardValues)return branches;
        var selected = branches.Select(branch=>branch with { Parts=OptimizeParts(branch.Parts,constraints,specification) }).ToArray();
        return loadedCircuitScore == null ? selected : OptimizeWholeLoadedCircuit(selected, constraints, loadedCircuitScore);
    }

    private static void EnsureTheoreticalValuesWithinRange(
        IReadOnlyList<LcRealizationBranch> branches,
        ComponentSelectionConstraints constraints)
    {
        foreach (var branch in branches)
        foreach (var part in branch.Parts)
        {
            if (part.ComponentType is not (ComponentType.Inductor or ComponentType.Capacitor)) continue;
            var (minimum, maximum, unit) = part.ComponentType == ComponentType.Inductor
                ? (constraints.InductorMinimumH, constraints.InductorMaximumH, "H")
                : (constraints.CapacitorMinimumF, constraints.CapacitorMaximumF, "F");
            if (double.IsFinite(part.TheoreticalValue) && part.TheoreticalValue > 0 &&
                part.TheoreticalValue >= minimum && part.TheoreticalValue <= maximum)
                continue;
            throw new InvalidOperationException(
                $"ComponentRangeInfeasible: theoretical LC g{branch.PrototypeIndex} {part.ComponentType} value " +
                $"{part.TheoreticalValue:G9} {unit} is outside the selected range [{minimum:G9}, {maximum:G9}] {unit}.");
        }
    }

    private static IReadOnlyList<LcRealizationBranch> OptimizeWholeLoadedCircuit(
        IReadOnlyList<LcRealizationBranch> initial,
        ComponentSelectionConstraints constraints,
        Func<IReadOnlyList<LcRealizationBranch>, double> scoreCircuit)
    {
        var best = initial.ToArray();
        var bestScore = TotalScore(best, scoreCircuit);
        // Two bounded coordinate-descent passes jointly retune interacting ladder elements.
        // Each candidate is a real E-series value and is scored from a newly assembled loaded CircuitGraph.
        for (var pass = 0; pass < 2; pass++)
        for (var branchIndex = 0; branchIndex < best.Length; branchIndex++)
        for (var partIndex = 0; partIndex < best[branchIndex].Parts.Count; partIndex++)
        {
            var part = best[branchIndex].Parts[partIndex];
            var (series, minimum, maximum) = part.ComponentType == ComponentType.Inductor
                ? (constraints.InductorSeries, constraints.InductorMinimumH, constraints.InductorMaximumH)
                : (constraints.CapacitorSeries, constraints.CapacitorMinimumF, constraints.CapacitorMaximumF);
            foreach (var value in StandardValueSeries.GetNearby(part.TheoreticalValue, series, minimum, maximum, 2))
            {
                var candidate = Replace(best, branchIndex, partIndex, part with { Value = value, SelectedSeries = series });
                var candidateScore = TotalScore(candidate, scoreCircuit);
                if (candidateScore + 1e-12 >= bestScore) continue;
                best = candidate;
                bestScore = candidateScore;
            }
        }
        return best;
    }

    private static LcRealizationBranch[] Replace(IReadOnlyList<LcRealizationBranch> source, int branchIndex, int partIndex, LcRealizationPart replacement)
    {
        var result = source.ToArray();
        var parts = result[branchIndex].Parts.ToArray();
        parts[partIndex] = replacement;
        result[branchIndex] = result[branchIndex] with { Parts = parts };
        return result;
    }

    private static double TotalScore(IReadOnlyList<LcRealizationBranch> branches, Func<IReadOnlyList<LcRealizationBranch>, double> scoreCircuit)
    {
        var componentError = branches.SelectMany(value => value.Parts)
            .Sum(value => Math.Abs(Math.Log(value.Value / value.TheoreticalValue)));
        var spreadPenalty = branches.SelectMany(value => value.Parts).GroupBy(value => value.ComponentType)
            .Sum(group => .001 * Math.Log(group.Max(value => value.Value) / group.Min(value => value.Value)));
        return scoreCircuit(branches) + .015 * componentError + spreadPenalty;
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
