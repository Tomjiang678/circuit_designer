using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public sealed class SallenKeySectionCalculator
{
    public SynthesizedSection Calculate(AnalogFilterSection section, FilterDesignRequest request)
    {
        if (section.ResponseType is not (FilterResponseType.LowPass or FilterResponseType.HighPass))
            throw new NotSupportedException("This Sallen-Key calculator implements Low-Pass and High-Pass sections only.");
        if (section.SectionGain < 1 - 1e-12)
            throw new NotSupportedException("A Sallen-Key non-inverting filter section cannot realize gain below 1 without a separate attenuator.");
        return section.Order == SectionOrder.First
            ? CalculateFirstOrder(section, request.ComponentConstraints)
            : CalculateSecondOrder(section, request.ComponentConstraints);
    }

    private static SynthesizedSection CalculateFirstOrder(AnalogFilterSection section, ComponentSelectionConstraints x)
    {
        var w = 2 * Math.PI * section.PoleFrequencyHz;
        var resistance = ChooseScale([1], [1 / w], x, section);
        var capacitance = 1 / (w * resistance);
        var gain = GainNetwork(section.SectionGain, x);
        var rCandidates = Candidates(resistance, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues);
        var cCandidates = Candidates(capacitance, x.CapacitorSeries, x.CapacitorMinimumF, x.CapacitorMaximumF, x.OptimizeStandardValues);
        FirstCandidate? best = null;
        foreach (var r in rCandidates)
        foreach (var c in cCandidates)
        foreach (var pair in gain.Candidates)
        {
            var actualFrequency = 1 / (2 * Math.PI * r * c);
            var actualGain = pair.Gain;
            var fe = Relative(actualFrequency, section.PoleFrequencyHz);
            var ge = Relative(actualGain, section.SectionGain);
            var score = Math.Abs(fe) + .5 * Math.Abs(ge);
            if (best == null || score < best.Score) best = new(r, c, pair.Rf, pair.Rg, actualFrequency, actualGain, fe, ge, score);
        }
        var choice = best ?? throw new InvalidOperationException("No valid first-order Sallen-Key candidate was found.");
        EnsureErrors("first-order Sallen-Key", choice.FrequencyError, 0, choice.GainError);
        var values = new List<SynthesizedComponentValue>
        {
            Value("R", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, resistance, choice.R, x.ResistorSeries, x.ResistorTolerancePercent),
            Value("C", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, capacitance, choice.C, x.CapacitorSeries, x.CapacitorTolerancePercent)
        };
        AddGainValues(values, gain, choice.Rf, choice.Rg, x);
        return new(section, values, choice.Frequency, 0, choice.Gain, choice.FrequencyError, 0, choice.GainError);
    }

    private static SynthesizedSection CalculateSecondOrder(AnalogFilterSection section, ComponentSelectionConstraints x)
    {
        var k = section.SectionGain;
        var shapeRoot = ShapeRoot(section.Q, k);
        var ratio = shapeRoot * shapeRoot;
        var w = 2 * Math.PI * section.PoleFrequencyHz;
        double[] resistorFactors;
        double[] capacitorCoefficients;
        if (section.ResponseType == FilterResponseType.LowPass)
        {
            resistorFactors = [1, 1];
            capacitorCoefficients = [1 / (w * shapeRoot), shapeRoot / w];
        }
        else
        {
            resistorFactors = [ratio, 1];
            capacitorCoefficients = [1 / (w * shapeRoot), 1 / (w * shapeRoot)];
        }
        var scale = ChooseScale(resistorFactors, capacitorCoefficients, x, section);
        var theory = section.ResponseType == FilterResponseType.LowPass
            ? new PassiveCandidate(scale, scale, capacitorCoefficients[0] / scale, capacitorCoefficients[1] / scale)
            : new PassiveCandidate(ratio * scale, scale, capacitorCoefficients[0] / scale, capacitorCoefficients[1] / scale);
        var gain = GainNetwork(k, x);

        FullCandidate? best = null;
        foreach (var r1 in Candidates(theory.R1, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues))
        foreach (var r2 in Candidates(theory.R2, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues))
        foreach (var c1 in Candidates(theory.C1, x.CapacitorSeries, x.CapacitorMinimumF, x.CapacitorMaximumF, x.OptimizeStandardValues))
        foreach (var c2 in Candidates(theory.C2, x.CapacitorSeries, x.CapacitorMinimumF, x.CapacitorMaximumF, x.OptimizeStandardValues))
        foreach (var pair in gain.Candidates)
        {
            var actual = Verify(section.ResponseType, r1, r2, c1, c2, pair.Gain);
            if (!double.IsFinite(actual.q) || actual.q <= 0) continue;
            var fe = Relative(actual.frequency, section.PoleFrequencyHz);
            var qe = Relative(actual.q, section.Q);
            var ge = Relative(pair.Gain, k);
            var score = Math.Abs(fe) + 1.5 * Math.Abs(qe) + .5 * Math.Abs(ge);
            if (best == null || score < best.Score) best = new(r1, r2, c1, c2, pair.Rf, pair.Rg, actual.frequency, actual.q, pair.Gain, fe, qe, ge, score);
        }
        var choice = best ?? throw new InvalidOperationException("No stable Sallen-Key component candidate was found for the requested Q and section gain.");
        EnsureErrors("Sallen-Key", choice.FrequencyError, choice.QError, choice.GainError);
        var values = new List<SynthesizedComponentValue>
        {
            Value("R1", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory.R1, choice.R1, x.ResistorSeries, x.ResistorTolerancePercent),
            Value("R2", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory.R2, choice.R2, x.ResistorSeries, x.ResistorTolerancePercent),
            Value("C1", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory.C1, choice.C1, x.CapacitorSeries, x.CapacitorTolerancePercent),
            Value("C2", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory.C2, choice.C2, x.CapacitorSeries, x.CapacitorTolerancePercent)
        };
        AddGainValues(values, gain, choice.Rf, choice.Rg, x);
        return new(section, values, choice.Frequency, choice.Q, choice.Gain, choice.FrequencyError, choice.QError, choice.GainError);
    }

    public static (double frequency, double q) Verify(FilterResponseType responseType, double r1, double r2, double c1, double c2, double gain = 1)
    {
        var root = Math.Sqrt(r1 * r2 * c1 * c2);
        var damping = responseType == FilterResponseType.LowPass
            ? c1 * (r1 + r2) + r1 * c2 * (1 - gain)
            : r2 * (c1 + c2) + r1 * c2 * (1 - gain);
        return (1 / (2 * Math.PI * root), root / damping);
    }

    private static double ShapeRoot(double q, double gain)
    {
        if (Math.Abs(gain - 1) <= 1e-12) return 2 * q;
        return 4 / (Math.Sqrt(1 / (q * q) + 8 * (gain - 1)) + 1 / q);
    }

    private static double ChooseScale(double[] resistorFactors, double[] capacitorCoefficients, ComponentSelectionConstraints x, AnalogFilterSection section)
    {
        var minimum = Math.Max(resistorFactors.Max(f => x.ResistorMinimumOhm / f), capacitorCoefficients.Max(c => c / x.CapacitorMaximumF));
        var maximum = Math.Min(resistorFactors.Min(f => x.ResistorMaximumOhm / f), capacitorCoefficients.Min(c => c / x.CapacitorMinimumF));
        if (minimum > maximum || minimum <= 0 || !double.IsFinite(minimum + maximum))
            throw new InvalidOperationException($"This Sallen-Key section cannot fit the selected R/C ranges (f0={section.PoleFrequencyHz:0.###} Hz, Q={section.Q:0.###}, gain={section.SectionGain:0.###}).");
        return Math.Sqrt(minimum * maximum);
    }

    private static GainDesign GainNetwork(double target, ComponentSelectionConstraints x)
    {
        if (Math.Abs(target - 1) <= 1e-12) return new(0, 0, [new GainCandidate(0, 0, 1)]);
        var factor = target - 1;
        var minimum = Math.Max(x.ResistorMinimumOhm, x.ResistorMinimumOhm / factor);
        var maximum = Math.Min(x.ResistorMaximumOhm, x.ResistorMaximumOhm / factor);
        if (minimum > maximum)
            throw new InvalidOperationException($"Gain {target:0.###} cannot fit RF/RG inside the selected resistor range.");
        var rg = Math.Sqrt(minimum * maximum); var rf = factor * rg;
        var rfValues = Candidates(rf, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues);
        var rgValues = Candidates(rg, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues);
        return new(rf, rg, rfValues.SelectMany(a => rgValues.Select(b => new GainCandidate(a, b, 1 + a / b))).ToArray());
    }

    private static void AddGainValues(List<SynthesizedComponentValue> values, GainDesign design, double rf, double rg, ComponentSelectionConstraints x)
    {
        if (design.Rf == 0) return;
        values.Add(Value("RF", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, design.Rf, rf, x.ResistorSeries, x.ResistorTolerancePercent));
        values.Add(Value("RG", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, design.Rg, rg, x.ResistorSeries, x.ResistorTolerancePercent));
    }

    private static void EnsureErrors(string name, double frequency, double q, double gain)
    {
        if (Math.Abs(frequency) > .08 || Math.Abs(q) > .12 || Math.Abs(gain) > .08)
            throw new InvalidOperationException($"{name} selected values exceed error limits (f0 {frequency:P2}, Q {q:P2}, gain {gain:P2}).");
    }
    private static IReadOnlyList<double> Candidates(double theory, StandardComponentSeries series, double min, double max, bool optimize) =>
        optimize ? StandardValueSeries.GetNearby(theory, series, min, max) : [theory];
    private static SynthesizedComponentValue Value(string role, ComponentType type, string parameter, double theory, double selected, StandardComponentSeries series, double tolerance) =>
        new(role, type, parameter, theory, selected, selected == theory ? null : series, tolerance);
    private static double Relative(double actual, double target) => (actual - target) / target;

    private sealed record PassiveCandidate(double R1, double R2, double C1, double C2);
    private sealed record GainCandidate(double Rf, double Rg, double Gain);
    private sealed record GainDesign(double Rf, double Rg, IReadOnlyList<GainCandidate> Candidates);
    private sealed record FirstCandidate(double R, double C, double Rf, double Rg, double Frequency, double Gain, double FrequencyError, double GainError, double Score);
    private sealed record FullCandidate(double R1, double R2, double C1, double C2, double Rf, double Rg, double Frequency, double Q, double Gain, double FrequencyError, double QError, double GainError, double Score);
}
