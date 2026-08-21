using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

/// <summary>
/// Synthesizes the Bainter notch biquad documented by Analog Devices. The K2
/// amplifier is part of the notch section and carries the allocated section gain.
/// </summary>
public sealed class BainterNotchSectionCalculator
{
    public SynthesizedSection Calculate(AnalogFilterSection section, FilterDesignRequest request)
    {
        if (section.Order != SectionOrder.Second || section.ResponseType != FilterResponseType.BandStop)
            throw new NotSupportedException("Bainter synthesis requires a second-order Band-Stop section.");

        var x = request.ComponentConstraints;
        var targetGain = section.SectionGain;
        if (targetGain < 1 - 1e-12)
            throw new NotSupportedException("The Bainter K2 filter amplifier cannot realize gain below 1 without a separate attenuator.");
        var wp = 2 * Math.PI * section.PoleFrequencyHz;
        var wz = Math.Sqrt(section.NumeratorCoefficients[^1] / section.NumeratorCoefficients[0]);
        var z = Math.Pow(wz / wp, 2);
        var coefficients = new[]
        {
            1d / (2 * z * section.Q * wp), // R3; K1 is set by R2/R1
            targetGain / (2 * section.Q * wp), // R4 includes K2
            2 * section.Q / wp             // R5 = R6
        };
        var capacitorMinimum = Math.Max(x.CapacitorMinimumF, coefficients.Max(value => value / x.ResistorMaximumOhm));
        var capacitorMaximum = Math.Min(x.CapacitorMaximumF, coefficients.Min(value => value / x.ResistorMinimumOhm));
        if (!(capacitorMinimum <= capacitorMaximum) || !double.IsFinite(capacitorMinimum + capacitorMaximum))
            throw new InvalidOperationException($"This Bainter section (f0={section.PoleFrequencyHz:0.###} Hz, Q={section.Q:0.###}) cannot fit R={x.ResistorMinimumOhm:G4}..{x.ResistorMaximumOhm:G4} ohm and C={x.CapacitorMinimumF:G4}..{x.CapacitorMaximumF:G4} F.");

        var capacitorTheory = Math.Sqrt(capacitorMinimum * capacitorMaximum);
        var resistorTheory = Math.Sqrt(x.ResistorMinimumOhm * x.ResistorMaximumOhm);
        var capacitorCandidates = x.OptimizeStandardValues
            ? StandardValueSeries.GetNearby(capacitorTheory, x.CapacitorSeries, capacitorMinimum, capacitorMaximum, 4)
            : new[] { capacitorTheory };
        var gainDesign = SelectGainNetwork(targetGain, x);
        Candidate? best = null;
        foreach (var capacitor in capacitorCandidates)
        {
            var r1 = Select(resistorTheory, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues);
            var r2 = r1;
            var r3 = Select(coefficients[0] / capacitor, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues);
            var r4 = Select(coefficients[1] / capacitor, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues);
            var r5 = Select(coefficients[2] / capacitor, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues);
            foreach (var gainCandidate in gainDesign.Candidates)
            {
                var candidate = Verify(r1, r2, r3, r4, r5, r5, capacitor, capacitor, gainCandidate.Gain, gainCandidate.R7, gainCandidate.R8, wp, wz, section.Q, targetGain);
                if (best == null || candidate.Score < best.Score) best = candidate;
            }
        }
        var selected = best ?? throw new InvalidOperationException("No Bainter component candidate could be generated inside the selected ranges.");
        if (Math.Abs(selected.FrequencyError) > .05 || Math.Abs(selected.QError) > .08 || Math.Abs(selected.GainError) > .08)
            throw new InvalidOperationException($"Bainter standard values exceed the verification limit (f0 error {selected.FrequencyError:P2}, Q error {selected.QError:P2}, gain error {selected.GainError:P2}).");
        var requiredMagnitude = Math.Pow(10, -request.StopbandAttenuationDb / 20);
        if (selected.NotchMagnitude > requiredMagnitude)
            throw new InvalidOperationException($"Selected Bainter values provide {-20 * Math.Log10(selected.NotchMagnitude):0.##} dB nominal attenuation at the requested notch; {request.StopbandAttenuationDb:0.##} dB is required. Choose a finer E-series or disable standard-value optimization.");

        var theories = new[] { resistorTheory, resistorTheory, coefficients[0] / capacitorTheory, coefficients[1] / capacitorTheory, coefficients[2] / capacitorTheory, coefficients[2] / capacitorTheory };
        var resistors = new[] { selected.R1, selected.R2, selected.R3, selected.R4, selected.R5, selected.R6 };
        var components = Enumerable.Range(0, 6).Select(index => R($"R{index + 1}", theories[index], resistors[index], x))
            .Concat(new[] { C("C1", capacitorTheory, selected.C1, x), C("C2", capacitorTheory, selected.C2, x) }).ToList();
        if (gainDesign.R7 > 0)
        {
            components.Add(R("R7", gainDesign.R7, selected.R7, x));
            components.Add(R("R8", gainDesign.R8, selected.R8, x));
        }
        return new SynthesizedSection(section, components, selected.PoleFrequency / (2 * Math.PI), selected.Q, selected.Gain,
            selected.FrequencyError, selected.QError, selected.GainError);
    }

    private static Candidate Verify(double r1, double r2, double r3, double r4, double r5, double r6, double c1, double c2,
        double gain, double r7, double r8, double targetWp, double targetWz, double targetQ, double targetGain)
    {
        var wp = Math.Sqrt(gain / (r4 * r5 * c1 * c2));
        var wz = Math.Sqrt((r2 / r1) / (r3 * r5 * c1 * c2));
        var damping = (r5 + r6) / (r5 * r6 * c2);
        var q = wp / damping;
        var s = new Complex(0, targetWz);
        var transfer = gain * (s * s + wz * wz) / (s * s + damping * s + wp * wp);
        var frequencyError = (wp - targetWp) / targetWp;
        var qError = (q - targetQ) / targetQ;
        var zeroError = (wz - targetWz) / targetWz;
        var gainError = (gain - targetGain) / targetGain;
        return new Candidate(r1, r2, r3, r4, r5, r6, r7, r8, c1, c2, wp, q, gain, transfer.Magnitude,
            frequencyError, qError, gainError, Math.Abs(frequencyError) + Math.Abs(qError) + .5 * Math.Abs(gainError) + 4 * Math.Abs(zeroError));
    }

    private static GainDesign SelectGainNetwork(double target, ComponentSelectionConstraints x)
    {
        if (Math.Abs(target - 1) <= 1e-12) return new(0, 0, [new GainCandidate(0, 0, 1)]);
        var factor = target - 1;
        var minimum = Math.Max(x.ResistorMinimumOhm, x.ResistorMinimumOhm / factor);
        var maximum = Math.Min(x.ResistorMaximumOhm, x.ResistorMaximumOhm / factor);
        if (minimum > maximum) throw new InvalidOperationException($"Bainter gain {target:0.###} cannot fit R7/R8 inside the selected resistor range.");
        var r8 = Math.Sqrt(minimum * maximum); var r7 = factor * r8;
        var r7Values = x.OptimizeStandardValues ? StandardValueSeries.GetNearby(r7, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm) : [r7];
        var r8Values = x.OptimizeStandardValues ? StandardValueSeries.GetNearby(r8, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm) : [r8];
        return new(r7, r8, r7Values.SelectMany(a => r8Values.Select(b => new GainCandidate(a, b, 1 + a / b))).ToArray());
    }

    private static double Select(double theory, StandardComponentSeries series, double minimum, double maximum, bool optimize)
    {
        if (theory < minimum || theory > maximum)
            throw new InvalidOperationException($"Theoretical resistance {theory:G6} ohm is outside {minimum:G6}..{maximum:G6} ohm.");
        return optimize ? StandardValueSeries.GetNearby(theory, series, minimum, maximum).MinBy(value => Math.Abs(Math.Log(value / theory))) : theory;
    }

    private static SynthesizedComponentValue R(string role, double theory, double selected, ComponentSelectionConstraints x) =>
        new(role, ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory, selected, selected == theory ? null : x.ResistorSeries, x.ResistorTolerancePercent);
    private static SynthesizedComponentValue C(string role, double theory, double selected, ComponentSelectionConstraints x) =>
        new(role, ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory, selected, selected == theory ? null : x.CapacitorSeries, x.CapacitorTolerancePercent);

    private sealed record GainCandidate(double R7, double R8, double Gain);
    private sealed record GainDesign(double R7, double R8, IReadOnlyList<GainCandidate> Candidates);
    private sealed record Candidate(double R1, double R2, double R3, double R4, double R5, double R6, double R7, double R8, double C1, double C2,
        double PoleFrequency, double Q, double Gain, double NotchMagnitude, double FrequencyError, double QError, double GainError, double Score);
}
