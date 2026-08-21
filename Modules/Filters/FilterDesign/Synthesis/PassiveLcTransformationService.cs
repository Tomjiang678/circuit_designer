using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

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
        IReadOnlyList<double> ladderGValues,
        int prototypeOrder,
        FilterResponseType response,
        double impedanceOhm,
        NormalizedFilterSpecification specification)
    {
        if (ladderGValues.Count != prototypeOrder + 2)
            throw new ArgumentException("The normalized prototype does not contain g0...g(n+1).", nameof(ladderGValues));
        if (impedanceOhm <= 0) throw new ArgumentOutOfRangeException(nameof(impedanceOhm));
        return Enumerable.Range(1, prototypeOrder)
            .Select(index => TransformElement(index, ladderGValues[index], response, impedanceOhm, specification))
            .ToArray();
    }

    private static LcRealizationBranch TransformElement(int index, double g, FilterResponseType response, double r0, NormalizedFilterSpecification spec)
    {
        var seriesPrototype = index % 2 == 1;
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
