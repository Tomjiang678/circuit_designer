using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

public sealed class FilterSectionBuilder
{
    public IReadOnlyList<AnalogFilterSection> Build(TransformedFilterPoles transformed)
    {
        const double tolerance = 1e-8;
        var remaining = transformed.Poles.ToList();
        var sections = new List<AnalogFilterSection>();
        var index = 1;

        while (remaining.Count > 0)
        {
            var pole = remaining[0];
            remaining.RemoveAt(0);
            if (Math.Abs(pole.Imaginary) <= tolerance)
            {
                if (transformed.ResponseType is FilterResponseType.BandPass or FilterResponseType.BandStop)
                {
                    var center = transformed.CenterAngularFrequency ?? throw new InvalidOperationException("Band transformations require a center frequency.");
                    var partnerIndex = remaining.FindIndex(candidate => Math.Abs(candidate.Imaginary) <= tolerance &&
                        Math.Abs(pole.Real * candidate.Real - center * center) <= tolerance * center * center);
                    if (partnerIndex < 0)
                        throw new InvalidOperationException("A transformed real pole does not have its matching band-section pole.");
                    var partner = remaining[partnerIndex]; remaining.RemoveAt(partnerIndex);
                    var bandAngularFrequency = Math.Sqrt(pole.Real * partner.Real);
                    var bandQ = bandAngularFrequency / -(pole.Real + partner.Real);
                    if (bandQ <= 0) throw new InvalidOperationException("An unstable transformed real-pole section was produced.");
                    sections.Add(CreateSecondOrder(index++, transformed.ResponseType, bandAngularFrequency, bandQ, center));
                    continue;
                }
                var angularPole = -pole.Real;
                if (angularPole <= 0)
                    throw new InvalidOperationException("An unstable real pole was produced.");
                sections.Add(CreateFirstOrder(index++, transformed.ResponseType, angularPole));
                continue;
            }

            var conjugateIndex = remaining.FindIndex(candidate => Complex.Abs(candidate - Complex.Conjugate(pole)) < tolerance * Math.Max(1, Complex.Abs(pole)));
            if (conjugateIndex < 0)
                throw new InvalidOperationException($"Pole '{pole}' does not have a conjugate partner.");
            remaining.RemoveAt(conjugateIndex);
            var angularFrequency = Complex.Abs(pole);
            var q = angularFrequency / (-2 * pole.Real);
            if (pole.Real >= 0 || q <= 0)
                throw new InvalidOperationException("An unstable second-order pole pair was produced.");
            sections.Add(CreateSecondOrder(index++, transformed.ResponseType, angularFrequency, q, transformed.CenterAngularFrequency));
        }

        var ordered = sections
            .OrderBy(section => section.Order)
            .ThenBy(section => section.Q)
            .Select((section, position) => section with { Index = position + 1 })
            .ToArray();
        return transformed.ResponseType == FilterResponseType.BandPass ? NormalizeBandPassGain(ordered, transformed.Poles) : ordered;
    }

    private static IReadOnlyList<AnalogFilterSection> NormalizeBandPassGain(AnalogFilterSection[] sections, IReadOnlyList<Complex> poles)
    {
        var center = Math.Exp(poles.Average(pole => Math.Log(Complex.Abs(pole))));
        var magnitude = 1d;
        foreach (var section in sections)
        {
            var w0 = 2 * Math.PI * section.PoleFrequencyHz;
            var numerator = center * w0 / section.Q;
            var denominator = Complex.Abs(new Complex(w0 * w0 - center * center, center * w0 / section.Q));
            magnitude *= numerator / denominator;
        }
        var sectionGain = Math.Pow(1 / magnitude, 1d / sections.Length);
        return sections.Select(section => section with
        {
            SectionGain = sectionGain,
            NumeratorCoefficients = section.NumeratorCoefficients.Select(value => value * sectionGain).ToArray()
        }).ToArray();
    }

    private static AnalogFilterSection CreateFirstOrder(int index, FilterResponseType response, double pole)
    {
        var numerator = response == FilterResponseType.LowPass ? new[] { pole } : new[] { 1d, 0d };
        return new AnalogFilterSection(index, SectionOrder.First, response, pole / (2 * Math.PI), 0, 1, numerator, new[] { 1d, pole });
    }

    private static AnalogFilterSection CreateSecondOrder(int index, FilterResponseType response, double w0, double q, double? centerAngularFrequency)
    {
        var numerator = response switch
        {
            FilterResponseType.LowPass => new[] { w0 * w0 },
            FilterResponseType.BandPass => new[] { w0 / q, 0d },
            FilterResponseType.BandStop => CreateBandStopNumerator(centerAngularFrequency),
            _ => new[] { 1d, 0d, 0d }
        };
        return new AnalogFilterSection(index, SectionOrder.Second, response, w0 / (2 * Math.PI), q, 1, numerator, new[] { 1d, w0 / q, w0 * w0 });
    }

    private static double[] CreateBandStopNumerator(double? centerAngularFrequency)
    {
        var center = centerAngularFrequency ?? throw new InvalidOperationException("Band-stop sections require a shared notch frequency.");
        return new[] { 1d, 0d, center * center };
    }
}
