using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Verification;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

/// <summary>
/// Synthesizes the Bainter notch biquad documented by Analog Devices. The K2
/// amplifier is part of the notch section and carries the allocated section gain.
/// </summary>
public sealed class BainterNotchSectionCalculator
{
    public SynthesizedSection Calculate(AnalogFilterSection section, FilterDesignRequest request) =>
        CalculateCandidates(section, request)[0];

    public IReadOnlyList<SynthesizedSection> CalculateCandidates(AnalogFilterSection section, FilterDesignRequest request)
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
        var k1Target = Math.Clamp(2 * z * section.Q * Math.Sqrt(targetGain),
            x.ResistorMinimumOhm / x.ResistorMaximumOhm,
            x.ResistorMaximumOhm / x.ResistorMinimumOhm);
        var ratioDesign = SelectRatioNetwork(k1Target, x);
        var coefficients = new[]
        {
            k1Target / (2 * z * section.Q * wp), // R3 includes K1=R2/R1
            targetGain / (2 * section.Q * wp),   // R4 includes K2
            2 * section.Q / wp                   // R5 = R6
        };
        var capacitorMinimum = Math.Max(x.CapacitorMinimumF, coefficients.Max(value => value / x.ResistorMaximumOhm));
        var capacitorMaximum = Math.Min(x.CapacitorMaximumF, coefficients.Min(value => value / x.ResistorMinimumOhm));
        if (!(capacitorMinimum <= capacitorMaximum) || !double.IsFinite(capacitorMinimum + capacitorMaximum))
            throw new InvalidOperationException($"This Bainter section (f0={section.PoleFrequencyHz:0.###} Hz, Q={section.Q:0.###}, K1={k1Target:0.###}) cannot fit R={x.ResistorMinimumOhm:G4}..{x.ResistorMaximumOhm:G4} ohm and C={x.CapacitorMinimumF:G4}..{x.CapacitorMaximumF:G4} F.");

        var capacitorTheory = Math.Sqrt(capacitorMinimum * capacitorMaximum);
        var capacitorCandidates = x.OptimizeStandardValues
            ? StandardValueSeries.GetNearby(capacitorTheory, x.CapacitorSeries, capacitorMinimum, capacitorMaximum, 3)
            : new[] { capacitorTheory };
        var gainDesign = SelectGainNetwork(targetGain, x);
        var generated = new List<Candidate>();
        foreach (var capacitor in capacitorCandidates)
        foreach (var ratioCandidate in ratioDesign.Candidates)
        {
            var r3Coefficient = ratioCandidate.Ratio / (2 * z * section.Q * wp);
            var r3Candidates = Nearby(r3Coefficient / capacitor, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues, 3);
            var r4Candidates = Nearby(coefficients[1] / capacitor, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues, 3);
            var r5Candidates = Nearby(coefficients[2] / capacitor, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues, 3);
            foreach (var r3 in r3Candidates)
            foreach (var r4 in r4Candidates)
            foreach (var r5 in r5Candidates)
            foreach (var gainCandidate in gainDesign.Candidates)
            {
                var candidate = Verify(ratioCandidate.R1, ratioCandidate.R2, r3, r4, r5, r5, capacitor, capacitor, gainCandidate.Gain, gainCandidate.R7, gainCandidate.R8, wp, wz, section.Q, targetGain);
                generated.Add(candidate);
            }
        }
        var requiredMagnitude = Math.Pow(10, -request.StopbandAttenuationDb / 20);
        var theories = new[] { ratioDesign.R1, ratioDesign.R2, coefficients[0] / capacitorTheory, coefficients[1] / capacitorTheory, coefficients[2] / capacitorTheory, coefficients[2] / capacitorTheory };
        var verifiedCandidates = new List<VerifiedCandidate>();
        foreach (var candidate in generated.OrderBy(candidate => candidate.Score +
                     8 * Math.Max(0, candidate.NotchMagnitude / requiredMagnitude - 1)).Take(256))
        {
            var candidateComponents = CreateComponents(candidate, theories, capacitorTheory, gainDesign, x);
            var provisionalCandidate = new SynthesizedSection(section, candidateComponents,
                candidate.PoleFrequency / (2 * Math.PI), candidate.Q, candidate.Gain,
                candidate.FrequencyError, candidate.QError, candidate.GainError);
            var graph = new FilterCircuitAssembler().BuildActiveSection(request, provisionalCandidate);
            var measurement = new CircuitSectionMnaVerifier().Verify(graph, SectionTransferTarget.FromSection(section),
                new SectionTransferVerificationOptions(1, 1, PointsPerDecade: 6));
            var notchPenalty = measurement.TargetZeroGainMagnitude is double notch
                ? Math.Max(0, notch / requiredMagnitude - 1)
                : 1e6;
            var score = Math.Abs(measurement.FrequencyError) + 1.5 * Math.Abs(measurement.QError) +
                        Math.Abs(measurement.ZeroFrequencyError ?? 1) + 2 * Math.Abs(measurement.GainError) +
                        4 * measurement.MaximumSampleRelativeError + 20 * EdgeTransferError(measurement, section, request) +
                        8 * notchPenalty + .002 * ComponentSpread(candidateComponents);
            verifiedCandidates.Add(new(candidate, candidateComponents, measurement, score));
        }
        var verified = verifiedCandidates.MinBy(candidate => candidate.Score);
        if (verified == null)
            throw new InvalidOperationException("No Bainter component candidate could be generated and verified inside the selected ranges.");
        var selectedComponents = verified.Components;
        var measured = verified.Measurement;
        if (measured.TargetZeroGainMagnitude is not double notchMagnitude || notchMagnitude > requiredMagnitude)
            throw new InvalidOperationException($"BainterNotchDepthInsufficientWithSelectedSeries: selected {x.ResistorSeries}/{x.CapacitorSeries} values provide {measured.NotchAttenuationDb:0.##} dB nominal attenuation at the requested notch; {request.StopbandAttenuationDb:0.##} dB is required. Use E96, tighter tolerance, disable standard-value optimization, or lower As.");
        if (Math.Abs(measured.FrequencyError) > .05 || Math.Abs(measured.QError) > .08 || Math.Abs(measured.GainError) > .08 ||
            Math.Abs(measured.ZeroFrequencyError ?? double.PositiveInfinity) > .02 || measured.MaximumSampleRelativeError > .20)
            throw new InvalidOperationException($"Bainter selected values exceed the verification limit (f0 error {measured.FrequencyError:P2}, Q error {measured.QError:P2}, zero error {measured.ZeroFrequencyError:P2}, gain error {measured.GainError:P2}, transfer sample error {measured.MaximumSampleRelativeError:P2}).");

        var valid = verifiedCandidates.Where(candidate =>
                candidate.Measurement.TargetZeroGainMagnitude is double notch && notch <= requiredMagnitude &&
                Math.Abs(candidate.Measurement.FrequencyError) <= .05 && Math.Abs(candidate.Measurement.QError) <= .08 &&
                Math.Abs(candidate.Measurement.GainError) <= .08 &&
                Math.Abs(candidate.Measurement.ZeroFrequencyError ?? double.PositiveInfinity) <= .02 &&
                candidate.Measurement.MaximumSampleRelativeError <= .20)
            .OrderBy(candidate => candidate.Score).Take(48)
            .Select(candidate => new SynthesizedSection(section, candidate.Components,
                candidate.Measurement.PoleAngularFrequency / (2 * Math.PI), candidate.Measurement.Q,
                candidate.Measurement.GainMagnitude, candidate.Measurement.FrequencyError,
                candidate.Measurement.QError, candidate.Measurement.GainError))
            .ToArray();
        return valid.Length > 0 ? valid : throw new InvalidOperationException("No Bainter candidate passed all local selected-value verification gates.");
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

    private static RatioDesign SelectRatioNetwork(double target, ComponentSelectionConstraints x)
    {
        var r1Minimum = Math.Max(x.ResistorMinimumOhm, x.ResistorMinimumOhm / target);
        var r1Maximum = Math.Min(x.ResistorMaximumOhm, x.ResistorMaximumOhm / target);
        if (r1Minimum > r1Maximum)
            throw new InvalidOperationException($"Bainter K1 ratio {target:0.###} cannot fit R1/R2 inside the selected resistor range.");
        var r1 = Math.Sqrt(r1Minimum * r1Maximum);
        var r2 = target * r1;
        if (!x.OptimizeStandardValues) return new(r1, r2, [new RatioCandidate(r1, r2, target)]);
        var r1Values = StandardValueSeries.GetNearby(r1, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, 2);
        var r2Values = StandardValueSeries.GetNearby(r2, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, 2);
        var candidates = r1Values.SelectMany(first => r2Values.Select(second => new RatioCandidate(first, second, second / first)))
            .OrderBy(candidate => Math.Abs(Math.Log(candidate.Ratio / target))).Take(20).ToArray();
        return new(r1, r2, candidates);
    }

    private static IReadOnlyList<double> Nearby(double theory, StandardComponentSeries series, double minimum, double maximum, bool optimize, int radius = 1)
    {
        if (theory < minimum || theory > maximum)
            throw new InvalidOperationException($"Theoretical resistance {theory:G6} ohm is outside {minimum:G6}..{maximum:G6} ohm.");
        return optimize ? StandardValueSeries.GetNearby(theory, series, minimum, maximum, radius) : [theory];
    }

    private static IReadOnlyList<SynthesizedComponentValue> CreateComponents(Candidate selected, IReadOnlyList<double> theories,
        double capacitorTheory, GainDesign gainDesign, ComponentSelectionConstraints x)
    {
        var resistors = new[] { selected.R1, selected.R2, selected.R3, selected.R4, selected.R5, selected.R6 };
        var components = Enumerable.Range(0, 6).Select(index => R($"R{index + 1}", theories[index], resistors[index], x))
            .Concat(new[] { C("C1", capacitorTheory, selected.C1, x), C("C2", capacitorTheory, selected.C2, x) }).ToList();
        if (gainDesign.R7 > 0)
        {
            components.Add(R("R7", gainDesign.R7, selected.R7, x));
            components.Add(R("R8", gainDesign.R8, selected.R8, x));
        }
        return components;
    }

    private static double ComponentSpread(IReadOnlyList<SynthesizedComponentValue> components)
    {
        static double Spread(IEnumerable<double> values)
        {
            var selected = values.ToArray();
            return selected.Length == 0 ? 1 : selected.Max() / selected.Min();
        }
        var resistorSpread = Spread(components.Where(value => value.ComponentType == ComponentType.Resistor).Select(value => value.SelectedValue));
        var capacitorSpread = Spread(components.Where(value => value.ComponentType == ComponentType.Capacitor).Select(value => value.SelectedValue));
        return Math.Log10(Math.Max(1, resistorSpread * capacitorSpread));
    }

    private static double EdgeTransferError(SectionTransferMeasurement measurement, AnalogFilterSection section,
        FilterDesignRequest request)
    {
        if (measurement.ZeroAngularFrequency is not > 0) return 1e6;
        var target = SectionTransferTarget.FromSection(section);
        var maximum = 0d;
        foreach (var frequency in new[] { request.PassbandLowerHz, request.PassbandUpperHz })
        {
            var s = new Complex(0, 2 * Math.PI * frequency);
            var actual = measurement.GainMagnitude * (s * s + measurement.ZeroAngularFrequency.Value * measurement.ZeroAngularFrequency.Value) /
                         (s * s + measurement.PoleAngularFrequency / measurement.Q * s +
                          measurement.PoleAngularFrequency * measurement.PoleAngularFrequency);
            maximum = Math.Max(maximum, Complex.Abs(actual - target.Evaluate(frequency)) /
                Math.Max(section.SectionGain, 1e-12));
        }
        return maximum;
    }

    private static SynthesizedComponentValue R(string role, double theory, double selected, ComponentSelectionConstraints x) =>
        new(role, ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, theory, selected, selected == theory ? null : x.ResistorSeries, x.ResistorTolerancePercent);
    private static SynthesizedComponentValue C(string role, double theory, double selected, ComponentSelectionConstraints x) =>
        new(role, ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, theory, selected, selected == theory ? null : x.CapacitorSeries, x.CapacitorTolerancePercent);

    private sealed record GainCandidate(double R7, double R8, double Gain);
    private sealed record GainDesign(double R7, double R8, IReadOnlyList<GainCandidate> Candidates);
    private sealed record RatioCandidate(double R1, double R2, double Ratio);
    private sealed record RatioDesign(double R1, double R2, IReadOnlyList<RatioCandidate> Candidates);
    private sealed record Candidate(double R1, double R2, double R3, double R4, double R5, double R6, double R7, double R8, double C1, double C2,
        double PoleFrequency, double Q, double Gain, double NotchMagnitude, double FrequencyError, double QError, double GainError, double Score);
    private sealed record VerifiedCandidate(Candidate Candidate, IReadOnlyList<SynthesizedComponentValue> Components,
        SectionTransferMeasurement Measurement, double Score);
}
