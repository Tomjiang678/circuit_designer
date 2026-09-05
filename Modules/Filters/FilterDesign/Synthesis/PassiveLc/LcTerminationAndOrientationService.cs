using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

public sealed record LcTerminationResolution(
    LcTerminationPolicy Policy,
    double ImpedanceScaleOhm,
    double RequiredSourceResistanceOhm,
    double RequiredLoadResistanceOhm,
    double PrototypeTerminationRatio,
    string Summary);

public sealed class LcTerminationPolicyService
{
    private const double RatioTolerance = 1e-4;

    public (FilterDesignRequest Request, LcTerminationResolution Resolution) Resolve(
        FilterDesignRequest request, NormalizedLcLadderPrototype prototype)
    {
        if (request.LcTerminationMode != LcTerminationMode.DoublyTerminated || request.SourceResistanceOhm <= 0)
            throw new NotSupportedException("LcTerminationModeMismatch: LC ladder synthesis requires Rs > 0 and a doubly terminated boundary.");
        var g0 = prototype.SourceTerminationG0;
        var gn = prototype.LoadTerminationGN1;
        if (!(g0 > 0 && gn > 0 && double.IsFinite(g0) && double.IsFinite(gn)))
            throw new InvalidOperationException("LcTerminationRatioUnsupported: normalized source/load termination values are invalid.");
        var scale = request.SourceResistanceOhm / g0;
        var requiredLoad = scale * gn;
        var ratio = gn / g0;
        switch (request.LcTerminationPolicy)
        {
            case LcTerminationPolicy.MatchedUserTermination:
                if (Math.Abs(ratio - 1) > RatioTolerance)
                    throw new NotSupportedException($"LcPrototypeRequiresNativeTermination: {request.Approximation} order {prototype.Order} requires RL/Rs={ratio:G9}, not a matched termination.");
                if (Math.Abs(request.LoadResistanceOhm - request.SourceResistanceOhm) / request.SourceResistanceOhm > RatioTolerance)
                    throw new NotSupportedException("LcTerminationRatioUnsupported: Matched termination requires RL = Rs.");
                requiredLoad = request.SourceResistanceOhm;
                break;
            case LcTerminationPolicy.PrototypeNativeTermination:
                request = request with { LoadResistanceOhm = requiredLoad };
                break;
            case LcTerminationPolicy.CustomIndependentTermination:
                if (Math.Abs(request.LoadResistanceOhm - requiredLoad) / requiredLoad > RatioTolerance)
                    throw new NotSupportedException($"LcTerminationRatioUnsupported: this ladder requires RL={requiredLoad:G9} Ω for Rs={request.SourceResistanceOhm:G9} Ω; entered RL={request.LoadResistanceOhm:G9} Ω.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request.LcTerminationPolicy));
        }
        return (request, new(request.LcTerminationPolicy, scale, request.SourceResistanceOhm, requiredLoad, ratio,
            $"g0={g0:G9}, gN+1={gn:G9}; R0=Rs/g0={scale:G9} Ω; required RL=R0*gN+1={requiredLoad:G9} Ω"));
    }
}

public sealed class LcLadderOrientationService
{
    public NormalizedLcLadderPrototype Select(NormalizedLcLadderPrototype prototype, LcLadderOrientation requested,
        FilterDesignRequest request, NormalizedFilterSpecification specification)
    {
        if (requested == LcLadderOrientation.SeriesFirst) return prototype;
        if (requested == LcLadderOrientation.ShuntFirst)
        {
            if (Math.Abs(prototype.SourceTerminationG0 - prototype.LoadTerminationGN1) > 1e-6)
                throw new NotSupportedException("LcTerminationRatioUnsupported: Shunt-First dual orientation is currently qualified only for equal prototype terminations.");
            return Dual(prototype);
        }
        if (Math.Abs(prototype.SourceTerminationG0 - prototype.LoadTerminationGN1) > 1e-6) return prototype;
        var series = Spread(prototype, request, specification);
        var dual = Dual(prototype);
        var shunt = Spread(dual, request, specification);
        return shunt < series ? dual : prototype;
    }

    private static NormalizedLcLadderPrototype Dual(NormalizedLcLadderPrototype prototype) => prototype with
    {
        StartElement = prototype.StartElement == LcPrototypeConnection.Series ? LcPrototypeConnection.Shunt : LcPrototypeConnection.Series,
        Elements = prototype.Elements.Select(element => element with
        {
            Connection = element.Connection == LcPrototypeConnection.Series ? LcPrototypeConnection.Shunt : LcPrototypeConnection.Series,
            Kind = element.Kind == LcPrototypeElementKind.NormalizedInductor ? LcPrototypeElementKind.NormalizedCapacitor : LcPrototypeElementKind.NormalizedInductor
        }).ToArray(),
        SynthesisMethod = prototype.SynthesisMethod + "; dual Shunt-First orientation"
    };

    private static double Spread(NormalizedLcLadderPrototype prototype, FilterDesignRequest request, NormalizedFilterSpecification specification)
    {
        var r0 = request.SourceResistanceOhm / prototype.SourceTerminationG0;
        var parts = new PassiveLcTransformationService().Transform(prototype, request.ResponseType, r0, specification)
            .SelectMany(branch => branch.Parts).Where(part => part.Value > 0 && double.IsFinite(part.Value)).ToArray();
        if (parts.Length == 0) return double.PositiveInfinity;
        return parts.GroupBy(part => part.ComponentType)
            .Sum(group => group.Count() < 2 ? 0 : Math.Log(group.Max(part => part.Value) / group.Min(part => part.Value)));
    }
}
