using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Gain;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Bainter;

/// <summary>Combines a bounded set of independently MNA-qualified Bainter candidates.</summary>
public sealed class BainterNotchCascadeOptimizer
{
    private const int BeamWidth = 512;
    private readonly BainterNotchSectionSolver solver = new();

    public IReadOnlyList<SynthesizedSection> Optimize(IReadOnlyList<AnalogFilterSection> targets,
        FilterDesignRequest request, FilterPrototype prototype, GainDistributionPlan gainPlan)
    {
        var beam = new List<State> { new([], 0) };
        foreach (var target in targets)
        {
            var candidates = solver.SolveCandidates(target, request);
            beam = beam.SelectMany(state => candidates.Select(candidate =>
                    new State([.. state.Sections, candidate], PartialScore([.. state.Sections, candidate], request))))
                .OrderBy(state => state.Score).Take(BeamWidth).ToList();
        }
        return beam.OrderBy(state => FinalScore(state.Sections, request, prototype, gainPlan)).First().Sections;
    }

    private static double PartialScore(IReadOnlyList<SynthesizedSection> sections, FilterDesignRequest request)
    {
        var error = 0d;
        foreach (var frequency in Edges(request))
        {
            var actual = sections.Aggregate(Complex.One, (value, section) => value * Actual(section, frequency));
            var target = sections.Aggregate(Complex.One, (value, section) => value * Target(section.Target, frequency));
            error += Math.Pow(Complex.Abs(actual - target) / Math.Max(target.Magnitude, 1e-6), 2);
        }
        return error + sections.Sum(section => Math.Abs(section.FrequencyError) + Math.Abs(section.QError) + Math.Abs(section.GainError));
    }

    private static double FinalScore(IReadOnlyList<SynthesizedSection> sections, FilterDesignRequest request,
        FilterPrototype prototype, GainDistributionPlan gainPlan)
    {
        var scale = gainPlan.OutputScale;
        if (request.Approximation == FilterApproximation.ChebyshevTypeI && prototype.Order % 2 == 0)
            scale *= Math.Pow(10, -request.PassbandRippleDb / 20);
        var referenceDb = 20 * Math.Log10(request.PassbandGain);
        var passLimit = referenceDb - request.PassbandRippleDb - .1;
        var stopLimit = referenceDb - request.StopbandAttenuationDb + .1;
        var definitions = new[]
        {
            (request.PassbandLowerHz, true), (request.StopbandLowerHz, false),
            (request.StopbandUpperHz, false), (request.PassbandUpperHz, true)
        };
        var violation = 0d;
        foreach (var (frequency, passband) in definitions)
        {
            var db = 20 * Math.Log10(Math.Max(scale * sections.Aggregate(1d,
                (value, section) => value * Actual(section, frequency).Magnitude), 1e-300));
            var miss = passband ? Math.Max(0, passLimit - db) : Math.Max(0, db - stopLimit);
            violation += miss * miss;
        }
        var lowGain = scale * sections.Aggregate(1d, (value, section) => value * Actual(section, request.PassbandLowerHz / 100).Magnitude);
        var highGain = scale * sections.Aggregate(1d, (value, section) => value * Actual(section, request.PassbandUpperHz * 100).Magnitude);
        var gainError = Math.Abs(Math.Max(lowGain, highGain) - request.PassbandGain) / request.PassbandGain;
        var gainViolation = Math.Max(0, gainError - .05);
        return 100_000 * (violation + gainViolation * gainViolation) + PartialScore(sections, request);
    }

    private static IEnumerable<double> Edges(FilterDesignRequest request) =>
        [request.PassbandLowerHz, request.StopbandLowerHz, request.StopbandUpperHz, request.PassbandUpperHz];

    private static Complex Actual(SynthesizedSection section, double frequency)
    {
        var values = section.Components.ToDictionary(component => component.Role, component => component.SelectedValue);
        var zero = Math.Sqrt((values["R2"] / values["R1"]) /
                             (values["R3"] * values["R5"] * values["C1"] * values["C2"]));
        return BandStop(frequency, section.ActualPoleFrequencyHz * 2 * Math.PI, zero,
            section.ActualQ, section.ActualGain);
    }

    private static Complex Target(AnalogFilterSection section, double frequency)
    {
        var zero = Math.Sqrt(section.NumeratorCoefficients[^1] / section.NumeratorCoefficients[0]);
        return BandStop(frequency, section.PoleFrequencyHz * 2 * Math.PI, zero, section.Q, section.SectionGain);
    }

    private static Complex BandStop(double frequency, double pole, double zero, double q, double gain)
    {
        var s = new Complex(0, 2 * Math.PI * frequency);
        return gain * (s * s + zero * zero) / (s * s + pole / q * s + pole * pole);
    }

    private sealed record State(IReadOnlyList<SynthesizedSection> Sections, double Score);
}
