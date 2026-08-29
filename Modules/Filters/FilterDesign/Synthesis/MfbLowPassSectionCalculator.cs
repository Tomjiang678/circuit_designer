using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public sealed class MfbLowPassSectionCalculator
{
    public SynthesizedSection Calculate(AnalogFilterSection section, FilterDesignRequest request)
    {
        if (section.Order != SectionOrder.Second || section.ResponseType != FilterResponseType.LowPass)
            throw new NotSupportedException("This MFB calculator implements second-order Low-Pass sections only.");
        var constraints = request.ComponentConstraints;
        var gain = section.SectionGain;
        if (gain <= 0 || !double.IsFinite(gain)) throw new NotSupportedException("MFB section gain must be positive and finite.");
        const double m = 0.5;
        var n = section.Q * section.Q * Math.Pow(m + gain + gain * m, 2) / (gain * m);
        var w0 = 2 * Math.PI * section.PoleFrequencyHz;
        var resistorFactors = new[] { 1d, gain, m };
        var c1Coefficient = 1 / (w0 * Math.Sqrt(gain * m * n));
        var capacitorCoefficients = new[] { c1Coefficient, n * c1Coefficient };
        var minimum = Math.Max(resistorFactors.Max(f => constraints.ResistorMinimumOhm / f), capacitorCoefficients.Max(c => c / constraints.CapacitorMaximumF));
        var maximum = Math.Min(resistorFactors.Min(f => constraints.ResistorMaximumOhm / f), capacitorCoefficients.Min(c => c / constraints.CapacitorMinimumF));
        if (minimum > maximum) throw new InvalidOperationException($"This MFB low-pass section cannot fit the selected R/C ranges (f0={section.PoleFrequencyHz:0.###} Hz, Q={section.Q:0.###}, gain={gain:0.###}).");
        var resistance = Math.Sqrt(minimum * maximum);
        var theory = new Candidate(resistance, gain * resistance, m * resistance, c1Coefficient / resistance, n * c1Coefficient / resistance);
        var selection = SelectCandidate(theory, section, constraints);
        theory = selection.Theory;
        var best = selection.Selected;
        var actual = Verify(best.R1, best.R2, best.R3, best.C1, best.C2);
        var frequencyError = Relative(actual.FrequencyHz, section.PoleFrequencyHz);
        var qError = Relative(actual.Q, section.Q);
        var gainError = Relative(actual.GainMagnitude, section.SectionGain);
        if (Math.Abs(frequencyError) > 0.08 || Math.Abs(qError) > 0.12 || Math.Abs(gainError) > 0.08)
            throw new InvalidOperationException($"MFB selected values exceed error limits (f0 {frequencyError:P2}, Q {qError:P2}, gain {gainError:P2}).");
        return new SynthesizedSection(section,
        [
            Value("R1", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory.R1, best.R1, constraints.ResistorSeries, constraints.ResistorTolerancePercent),
            Value("R2", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory.R2, best.R2, constraints.ResistorSeries, constraints.ResistorTolerancePercent),
            Value("R3", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory.R3, best.R3, constraints.ResistorSeries, constraints.ResistorTolerancePercent),
            Value("C1", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory.C1, best.C1, constraints.CapacitorSeries, constraints.CapacitorTolerancePercent),
            Value("C2", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory.C2, best.C2, constraints.CapacitorSeries, constraints.CapacitorTolerancePercent)
        ], actual.FrequencyHz, actual.Q, actual.GainMagnitude, frequencyError, qError, gainError);
    }

    public static MfbActualParameters Verify(double r1, double r2, double r3, double c1, double c2)
    {
        if (new[] { r1, r2, r3, c1, c2 }.Any(value => value <= 0 || !double.IsFinite(value))) throw new ArgumentOutOfRangeException(nameof(r1));
        var root = Math.Sqrt(r2 * r3 * c1 * c2);
        var frequency = 1 / (2 * Math.PI * root);
        var q = root / (r3 * c1 + r2 * c1 + r2 * r3 * c1 / r1);
        return new MfbActualParameters(frequency, q, r2 / r1);
    }

    private static Selection SelectCandidate(Candidate theory, AnalogFilterSection section, ComponentSelectionConstraints constraints)
    {
        if (!constraints.OptimizeStandardValues) return new(theory, theory);
        Candidate? best = null; Candidate? bestTheory = null; var bestScore = double.PositiveInfinity;
        foreach (var scale in EquivalentScales(theory, constraints))
        {
            var scaled = new Candidate(theory.R1 * scale, theory.R2 * scale, theory.R3 * scale, theory.C1 / scale, theory.C2 / scale);
            var r1 = StandardValueSeries.GetNearby(scaled.R1, constraints.ResistorSeries, constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm);
            var r2 = StandardValueSeries.GetNearby(scaled.R2, constraints.ResistorSeries, constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm);
            var r3 = StandardValueSeries.GetNearby(scaled.R3, constraints.ResistorSeries, constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm);
            var c1 = StandardValueSeries.GetNearby(scaled.C1, constraints.CapacitorSeries, constraints.CapacitorMinimumF, constraints.CapacitorMaximumF);
            var c2 = StandardValueSeries.GetNearby(scaled.C2, constraints.CapacitorSeries, constraints.CapacitorMinimumF, constraints.CapacitorMaximumF);
            foreach (var a in r1) foreach (var b in r2) foreach (var c in r3) foreach (var d in c1) foreach (var e in c2)
            {
                var actual = Verify(a, b, c, d, e);
                var score = Math.Abs(Relative(actual.FrequencyHz, section.PoleFrequencyHz)) + 1.5 * Math.Abs(Relative(actual.Q, section.Q)) + 0.5 * Math.Abs(Relative(actual.GainMagnitude, section.SectionGain));
                if (score < bestScore) { bestScore = score; best = new Candidate(a, b, c, d, e); bestTheory = scaled; }
            }
        }
        return new(bestTheory ?? throw new InvalidOperationException("No valid MFB standard-value scale was found."),
            best ?? throw new InvalidOperationException("No valid MFB standard-value candidate was found."));
    }

    private static IEnumerable<double> EquivalentScales(Candidate theory, ComponentSelectionConstraints constraints)
    {
        var minimum = new[]
        {
            constraints.ResistorMinimumOhm / theory.R1, constraints.ResistorMinimumOhm / theory.R2,
            constraints.ResistorMinimumOhm / theory.R3, theory.C1 / constraints.CapacitorMaximumF,
            theory.C2 / constraints.CapacitorMaximumF
        }.Max();
        var maximum = new[]
        {
            constraints.ResistorMaximumOhm / theory.R1, constraints.ResistorMaximumOhm / theory.R2,
            constraints.ResistorMaximumOhm / theory.R3, theory.C1 / constraints.CapacitorMinimumF,
            theory.C2 / constraints.CapacitorMinimumF
        }.Min();
        if (minimum > maximum) yield break;
        const int samples = 13;
        for (var index = 0; index < samples; index++)
            yield return minimum * Math.Pow(maximum / minimum, index / (samples - 1d));
        if (minimum <= 1 && maximum >= 1) yield return 1;
    }

    private static SynthesizedComponentValue Value(string role, ComponentType type, string parameter, double theory, double selected, StandardComponentSeries series, double tolerance) =>
        new(role, type, parameter, theory, selected, selected == theory ? null : series, tolerance);
    private static double Relative(double actual, double target) => (actual - target) / target;
    private sealed record Candidate(double R1, double R2, double R3, double C1, double C2);
    private sealed record Selection(Candidate Theory, Candidate Selected);
}

public sealed record MfbActualParameters(double FrequencyHz, double Q, double GainMagnitude);
