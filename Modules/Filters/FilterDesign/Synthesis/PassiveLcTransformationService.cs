using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public enum LcBranchConnection { Series, Shunt }

public sealed record LcRealizationPart(ComponentType ComponentType, double Value)
{
    public double TheoreticalValue { get; init; } = Value;
    public StandardComponentSeries? SelectedSeries { get; init; }
    public double TolerancePercent { get; init; }
    public double RelativeError => (Value - TheoreticalValue) / TheoreticalValue;
}

public sealed record LcRealizationBranch(
    int PrototypeIndex,
    LcBranchConnection Connection,
    bool PartsInParallel,
    IReadOnlyList<LcRealizationPart> Parts);

public sealed class PassiveLcTransformationService
{
    public IReadOnlyList<LcRealizationBranch> Transform(
        NormalizedLcLadderPrototype prototype,
        FilterResponseType response,
        double impedanceOhm,
        NormalizedFilterSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(prototype);
        if (prototype.Elements.Count != prototype.Order || prototype.Elements.Any(value => value.GValue <= 0 || !double.IsFinite(value.GValue)))
            throw new ArgumentException("The normalized LC ladder prototype is incomplete or contains a non-positive element.", nameof(prototype));
        if (impedanceOhm <= 0) throw new ArgumentOutOfRangeException(nameof(impedanceOhm));
        return prototype.Elements.OrderBy(value => value.Index)
            .Select(element => TransformElement(element, response, impedanceOhm, specification))
            .ToArray();
    }

    private static LcRealizationBranch TransformElement(LcPrototypeElement element, FilterResponseType response, double r0, NormalizedFilterSpecification spec)
    {
        var index = element.Index;
        var g = element.GValue;
        var seriesPrototype = element.Connection == LcPrototypeConnection.Series;
        var connection = seriesPrototype ? LcBranchConnection.Series : LcBranchConnection.Shunt;
        return response switch
        {
            FilterResponseType.LowPass => new(index, connection, false,
                [seriesPrototype
                    ? new LcRealizationPart(ComponentType.Inductor, r0 * g / spec.CutoffAngularFrequency)
                    : new LcRealizationPart(ComponentType.Capacitor, g / (r0 * spec.CutoffAngularFrequency))]),
            FilterResponseType.HighPass => new(index, connection, false,
                [seriesPrototype
                    ? new LcRealizationPart(ComponentType.Capacitor, 1 / (r0 * spec.CutoffAngularFrequency * g))
                    : new LcRealizationPart(ComponentType.Inductor, r0 / (spec.CutoffAngularFrequency * g))]),
            FilterResponseType.BandPass => TransformBandPass(index, g, r0, spec, seriesPrototype),
            FilterResponseType.BandStop => TransformBandStop(index, g, r0, spec, seriesPrototype),
            _ => throw new NotSupportedException($"LC transformation is not implemented for {response}.")
        };
    }

    private static LcRealizationBranch TransformBandPass(int index, double g, double r0, NormalizedFilterSpecification spec, bool seriesPrototype)
    {
        var w0 = spec.CenterAngularFrequency; var bandwidth = spec.BandwidthAngularFrequency;
        if (seriesPrototype)
            return new(index, LcBranchConnection.Series, false,
            [
                new LcRealizationPart(ComponentType.Inductor, r0 * g / bandwidth),
                new LcRealizationPart(ComponentType.Capacitor, bandwidth / (r0 * g * w0 * w0))
            ]);
        return new(index, LcBranchConnection.Shunt, true,
        [
            new LcRealizationPart(ComponentType.Inductor, r0 * bandwidth / (g * w0 * w0)),
            new LcRealizationPart(ComponentType.Capacitor, g / (r0 * bandwidth))
        ]);
    }

    private static LcRealizationBranch TransformBandStop(int index, double g, double r0, NormalizedFilterSpecification spec, bool seriesPrototype)
    {
        var w0 = spec.CenterAngularFrequency; var bandwidth = spec.BandwidthAngularFrequency;
        if (seriesPrototype)
            return new(index, LcBranchConnection.Series, true,
            [
                new LcRealizationPart(ComponentType.Inductor, r0 * g * bandwidth / (w0 * w0)),
                new LcRealizationPart(ComponentType.Capacitor, 1 / (r0 * g * bandwidth))
            ]);
        return new(index, LcBranchConnection.Shunt, false,
        [
            new LcRealizationPart(ComponentType.Inductor, r0 / (g * bandwidth)),
            new LcRealizationPart(ComponentType.Capacitor, g * bandwidth / (r0 * w0 * w0))
        ]);
    }
}
