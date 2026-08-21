using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public sealed record TransformedFilterPoles(
    FilterResponseType ResponseType,
    int PrototypeOrder,
    int RealizedOrder,
    IReadOnlyList<Complex> Poles,
    double? CenterAngularFrequency = null);

public sealed class AnalogFrequencyTransformationService
{
    public TransformedFilterPoles TransformLowPassOrHighPass(
        FilterPrototype prototype,
        FilterResponseType responseType,
        double cutoffAngularFrequency)
    {
        if (cutoffAngularFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(cutoffAngularFrequency));
        var poles = responseType switch
        {
            FilterResponseType.LowPass => prototype.Poles.Select(pole => pole * cutoffAngularFrequency).ToArray(),
            FilterResponseType.HighPass => prototype.Poles.Select(pole => cutoffAngularFrequency / pole).ToArray(),
            _ => throw new NotSupportedException("Use the band transformation methods for BP/BS designs.")
        };
        return new TransformedFilterPoles(responseType, prototype.Order, prototype.Order, poles);
    }

    public TransformedFilterPoles TransformBandPass(FilterPrototype prototype, double centerAngularFrequency, double bandwidthAngularFrequency)
    {
        ValidateBand(centerAngularFrequency, bandwidthAngularFrequency);
        var poles = new List<Complex>(prototype.Order * 2);
        foreach (var pole in prototype.Poles)
        {
            var bp = bandwidthAngularFrequency * pole;
            var discriminant = Complex.Sqrt(bp * bp - 4 * centerAngularFrequency * centerAngularFrequency);
            poles.Add((bp + discriminant) / 2);
            poles.Add((bp - discriminant) / 2);
        }
        return new TransformedFilterPoles(FilterResponseType.BandPass, prototype.Order, prototype.Order * 2, poles, centerAngularFrequency);
    }

    public TransformedFilterPoles TransformBandStop(FilterPrototype prototype, double centerAngularFrequency, double bandwidthAngularFrequency)
    {
        ValidateBand(centerAngularFrequency, bandwidthAngularFrequency);
        var poles = new List<Complex>(prototype.Order * 2);
        foreach (var pole in prototype.Poles)
        {
            var coefficient = bandwidthAngularFrequency / pole;
            var discriminant = Complex.Sqrt(coefficient * coefficient - 4 * centerAngularFrequency * centerAngularFrequency);
            poles.Add((coefficient + discriminant) / 2);
            poles.Add((coefficient - discriminant) / 2);
        }
        return new TransformedFilterPoles(FilterResponseType.BandStop, prototype.Order, prototype.Order * 2, poles, centerAngularFrequency);
    }

    private static void ValidateBand(double center, double bandwidth)
    {
        if (center <= 0 || bandwidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(center), "Center frequency and bandwidth must be positive.");
    }
}
