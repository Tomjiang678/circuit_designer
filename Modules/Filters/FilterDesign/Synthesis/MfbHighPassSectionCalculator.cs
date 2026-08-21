using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public sealed class MfbHighPassSectionCalculator
{
    public SynthesizedSection Calculate(AnalogFilterSection section, FilterDesignRequest request)
    {
        if (section.Order != SectionOrder.Second || section.ResponseType != FilterResponseType.HighPass)
            throw new NotSupportedException("This MFB calculator implements second-order High-Pass sections only.");
        var constraints = request.ComponentConstraints;
        var gain = section.SectionGain;
        if (gain <= 0 || !double.IsFinite(gain)) throw new NotSupportedException("MFB section gain must be positive and finite.");
        var ratio = section.Q * section.Q * Math.Pow(gain + 2, 2);
        var w = 2 * Math.PI * section.PoleFrequencyHz;
        var capacitorCoefficient = 1 / (w * Math.Sqrt(ratio));
        var r5Minimum = Math.Max(Math.Max(constraints.ResistorMinimumOhm, constraints.ResistorMinimumOhm / ratio), Math.Max(capacitorCoefficient / constraints.CapacitorMaximumF, gain * capacitorCoefficient / constraints.CapacitorMaximumF));
        var r5Maximum = Math.Min(Math.Min(constraints.ResistorMaximumOhm, constraints.ResistorMaximumOhm / ratio), Math.Min(capacitorCoefficient / constraints.CapacitorMinimumF, gain * capacitorCoefficient / constraints.CapacitorMinimumF));
        if (r5Minimum > r5Maximum) throw new InvalidOperationException($"This MFB high-pass section cannot fit the selected R/C ranges (f0={section.PoleFrequencyHz:0.###} Hz, Q={section.Q:0.###}).");
        var r5 = Math.Sqrt(r5Minimum * r5Maximum);
        var r2 = ratio * r5;
        var capacitance = capacitorCoefficient / r5;
        var theory = new Candidate(gain * capacitance, r2, capacitance, capacitance, r5);
        var best = SelectCandidate(theory, section, constraints);
        var actual = Verify(best.C1, best.R2, best.C3, best.C4, best.R5);
        var frequencyError = Relative(actual.FrequencyHz, section.PoleFrequencyHz);
        var qError = Relative(actual.Q, section.Q);
        var gainError = Relative(actual.GainMagnitude, section.SectionGain);
        if (Math.Abs(frequencyError) > 0.08 || Math.Abs(qError) > 0.12 || Math.Abs(gainError) > 0.08)
            throw new InvalidOperationException($"MFB high-pass values exceed error limits (f0 {frequencyError:P2}, Q {qError:P2}, gain {gainError:P2}).");
        return new SynthesizedSection(section,
        [
            Value("C1", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory.C1, best.C1, constraints.CapacitorSeries, constraints.CapacitorTolerancePercent),
            Value("R2", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory.R2, best.R2, constraints.ResistorSeries, constraints.ResistorTolerancePercent),
            Value("C3", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory.C3, best.C3, constraints.CapacitorSeries, constraints.CapacitorTolerancePercent),
            Value("C4", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory.C4, best.C4, constraints.CapacitorSeries, constraints.CapacitorTolerancePercent),
            Value("R5", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory.R5, best.R5, constraints.ResistorSeries, constraints.ResistorTolerancePercent)
        ], actual.FrequencyHz, actual.Q, actual.GainMagnitude, frequencyError, qError, gainError);
    }

    public static MfbActualParameters Verify(double c1, double r2, double c3, double c4, double r5)
    {
        if (new[] { c1, r2, c3, c4, r5 }.Any(value => value <= 0 || !double.IsFinite(value))) throw new ArgumentOutOfRangeException(nameof(c1));
        var root = Math.Sqrt(c3 * c4 * r2 * r5);
        var frequency = 1 / (2 * Math.PI * root);
        var q = root / ((c4 + c3 + c1) * r5);
        return new MfbActualParameters(frequency, q, c1 / c4);
    }

    private static Candidate SelectCandidate(Candidate theory, AnalogFilterSection section, ComponentSelectionConstraints constraints)
    {
        if (!constraints.OptimizeStandardValues) return theory;
        var c1 = StandardValueSeries.GetNearby(theory.C1, constraints.CapacitorSeries, constraints.CapacitorMinimumF, constraints.CapacitorMaximumF);
        var r2 = StandardValueSeries.GetNearby(theory.R2, constraints.ResistorSeries, constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm);
        var c3 = StandardValueSeries.GetNearby(theory.C3, constraints.CapacitorSeries, constraints.CapacitorMinimumF, constraints.CapacitorMaximumF);
        var c4 = StandardValueSeries.GetNearby(theory.C4, constraints.CapacitorSeries, constraints.CapacitorMinimumF, constraints.CapacitorMaximumF);
        var r5 = StandardValueSeries.GetNearby(theory.R5, constraints.ResistorSeries, constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm);
        Candidate? best = null; var bestScore = double.PositiveInfinity;
        foreach (var a in c1) foreach (var b in r2) foreach (var c in c3) foreach (var d in c4) foreach (var e in r5)
        {
            var actual = Verify(a, b, c, d, e);
            var score = Math.Abs(Relative(actual.FrequencyHz, section.PoleFrequencyHz)) + 1.5 * Math.Abs(Relative(actual.Q, section.Q)) + .5 * Math.Abs(Relative(actual.GainMagnitude, section.SectionGain));
            if (score < bestScore) { bestScore = score; best = new Candidate(a, b, c, d, e); }
        }
        return best ?? throw new InvalidOperationException("No valid MFB high-pass standard-value candidate was found.");
    }

    private static SynthesizedComponentValue Value(string role, ComponentType type, string parameter, double theory, double selected, StandardComponentSeries series, double tolerance) =>
        new(role, type, parameter, theory, selected, selected == theory ? null : series, tolerance);
    private static double Relative(double actual, double target) => (actual - target) / target;
    private sealed record Candidate(double C1, double R2, double C3, double C4, double R5);
}
