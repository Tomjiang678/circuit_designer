using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public sealed record FilterPrototype(
    FilterApproximation Approximation,
    int Order,
    IReadOnlyList<Complex> Poles,
    IReadOnlyList<double> LadderGValues,
    IReadOnlyList<Complex>? FiniteZeros = null,
    double DcGain = 1,
    PrototypeNormalizationInfo? Normalization = null,
    string Description = "",
    IReadOnlyDictionary<string, double>? Parameters = null,
    NormalizedLcLadderPrototype? LcLadderPrototype = null)
{
    public IReadOnlyList<Complex> Zeros => FiniteZeros ?? Array.Empty<Complex>();
    public double NumeratorGain => DcGain * Poles.Aggregate(Complex.One, (value, pole) => value * -pole).Real /
                                   Zeros.Aggregate(Complex.One, (value, zero) => value * -zero).Real;
}
