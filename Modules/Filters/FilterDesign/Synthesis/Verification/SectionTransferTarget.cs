using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Verification;

public sealed record SectionTransferTarget(
    FilterResponseType ResponseType,
    double PoleAngularFrequency,
    double? ZeroAngularFrequency,
    double Q,
    double TargetGainMagnitude,
    int TargetPolarity,
    IReadOnlyList<double> NumeratorCoefficients,
    IReadOnlyList<double> DenominatorCoefficients)
{
    public static SectionTransferTarget FromSection(AnalogFilterSection section, int polarity = 1)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (polarity is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(polarity));
        var zero = section.ResponseType switch
        {
            FilterResponseType.BandPass => 0,
            FilterResponseType.BandStop => FindFiniteZero(section.NumeratorCoefficients),
            _ => null
        };
        return new(section.ResponseType, 2 * Math.PI * section.PoleFrequencyHz, zero,
            section.Q, section.SectionGain, polarity,
            section.NumeratorCoefficients.ToArray(), section.DenominatorCoefficients.ToArray());
    }

    public Complex Evaluate(double frequencyHz)
    {
        var s = new Complex(0, 2 * Math.PI * frequencyHz);
        return TargetPolarity * EvaluatePolynomial(NumeratorCoefficients, s) / EvaluatePolynomial(DenominatorCoefficients, s);
    }

    private static double? FindFiniteZero(IReadOnlyList<double> coefficients)
    {
        if (coefficients.Count != 3 || coefficients[0] <= 0 || coefficients[2] <= 0) return null;
        return Math.Sqrt(coefficients[2] / coefficients[0]);
    }

    private static Complex EvaluatePolynomial(IReadOnlyList<double> coefficients, Complex s)
    {
        Complex value = 0;
        foreach (var coefficient in coefficients) value = value * s + coefficient;
        return value;
    }
}
