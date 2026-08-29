using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Gain;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

/// <summary>Bounded cross-section refinement for quantized Sallen-Key band-pass cascades.</summary>
public sealed class SallenKeyBandPassCascadeOptimizer
{
    private const int BeamWidth = 10;
    private const int MaximumDepth = 8;

    public IReadOnlyList<SynthesizedSection> Optimize(FilterDesignRequest request, FilterPrototype prototype,
        GainDistributionPlan gainPlan, IReadOnlyList<SynthesizedSection> source)
    {
        if (!request.ComponentConstraints.OptimizeStandardValues || request.Mode != FilterMode.Active ||
            request.ResponseType != FilterResponseType.BandPass || request.Implementation != FilterImplementationType.SallenKey)
            return source;
        var initial = source.ToArray();
        var beam = new List<State> { new(initial, Score(request, prototype, gainPlan, initial)) };
        var visited = new HashSet<string>(StringComparer.Ordinal) { Key(initial) };
        for (var depth = 0; depth < MaximumDepth; depth++)
        {
            var expanded = new List<State>(beam);
            foreach (var state in beam)
            for (var sectionIndex = 0; sectionIndex < state.Sections.Length; sectionIndex++)
            for (var componentIndex = 0; componentIndex < state.Sections[sectionIndex].Components.Count; componentIndex++)
            {
                var component = state.Sections[sectionIndex].Components[componentIndex];
                if (component.ComponentType is not (ComponentType.Resistor or ComponentType.Capacitor)) continue;
                foreach (var value in Nearby(component, request.ComponentConstraints))
                {
                    if (Math.Abs(value - component.SelectedValue) <= 1e-14 * value) continue;
                    var components = state.Sections[sectionIndex].Components.ToArray();
                    components[componentIndex] = component with { SelectedValue = value };
                    if (!TryRecalculate(state.Sections[sectionIndex] with { Components = components }, out var recalculated)) continue;
                    var sections = (SynthesizedSection[])state.Sections.Clone();
                    sections[sectionIndex] = recalculated;
                    if (!visited.Add(Key(sections))) continue;
                    expanded.Add(new(sections, Score(request, prototype, gainPlan, sections)));
                }
            }
            beam = expanded.OrderBy(state => state.Score).Take(BeamWidth).ToList();
        }
        return beam[0].Score + 1e-12 < Score(request, prototype, gainPlan, initial) ? beam[0].Sections : source;
    }

    private static IEnumerable<double> Nearby(SynthesizedComponentValue component, ComponentSelectionConstraints constraints)
    {
        var (series, minimum, maximum) = component.ComponentType == ComponentType.Resistor
            ? (constraints.ResistorSeries, constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm)
            : (constraints.CapacitorSeries, constraints.CapacitorMinimumF, constraints.CapacitorMaximumF);
        return StandardValueSeries.GetNearby(component.SelectedValue, series, minimum, maximum, 1);
    }

    private static bool TryRecalculate(SynthesizedSection section, out SynthesizedSection result)
    {
        result = section;
        var value = section.Components.ToDictionary(component => component.Role, component => component.SelectedValue);
        var actual = SallenKeyBandPassSectionCalculator.Verify(value["R1"], value["R2"], value["R3"], value["C1"], value["C2"],
            value["R4"], value["R5"], value["RA"], value["RB"]);
        var frequencyError = Relative(actual.FrequencyHz, section.Target.PoleFrequencyHz);
        var qError = Relative(actual.Q, section.Target.Q);
        var gainError = Relative(actual.GainMagnitude, section.Target.SectionGain);
        if (!double.IsFinite(actual.Q) || actual.Q <= 0 || Math.Abs(frequencyError) > .08 || Math.Abs(qError) > .12 || Math.Abs(gainError) > .08 ||
            TransferError(actual, section.Target) > .20) return false;
        var intrinsicGainNetwork = 1 + value["R4"] / value["R5"];
        var intrinsicCenterGain = intrinsicGainNetwork * actual.Q;
        var outputScale = value["RB"] / (value["RA"] + value["RB"]);
        result = section with
        {
            ActualPoleFrequencyHz = actual.FrequencyHz, ActualQ = actual.Q, ActualGain = actual.GainMagnitude,
            FrequencyError = frequencyError, QError = qError, GainError = gainError,
            TopologyGain = new(intrinsicCenterGain, 0, intrinsicCenterGain, outputScale, actual.GainMagnitude)
        };
        return true;
    }

    private static double Score(FilterDesignRequest request, FilterPrototype prototype, GainDistributionPlan gainPlan,
        IReadOnlyList<SynthesizedSection> sections)
    {
        var fixedScale = gainPlan.OutputScale;
        if (request.Approximation == FilterApproximation.ChebyshevTypeI && prototype.Order % 2 == 0)
            fixedScale *= Math.Pow(10, -request.PassbandRippleDb / 20);
        var referenceDb = 20 * Math.Log10(request.PassbandGain);
        var passLimit = referenceDb - request.PassbandRippleDb - .1;
        var stopLimit = referenceDb - request.StopbandAttenuationDb + .1;
        var edges = new[]
        {
            (request.StopbandLowerHz, false), (request.PassbandLowerHz, true),
            (request.PassbandUpperHz, true), (request.StopbandUpperHz, false)
        };
        var violation = 0d;
        foreach (var (frequency, passband) in edges)
        {
            var db = 20 * Math.Log10(Math.Max(CascadeMagnitude(sections, fixedScale, frequency), 1e-300));
            var miss = passband ? Math.Max(0, passLimit - db) : Math.Max(0, db - stopLimit);
            violation += miss * miss;
        }
        var peak = Enumerable.Range(0, 25).Select(index => request.PassbandLowerHz *
                Math.Pow(request.PassbandUpperHz / request.PassbandLowerHz, index / 24d))
            .Max(frequency => CascadeMagnitude(sections, fixedScale, frequency));
        var gainError = Math.Abs(peak - request.PassbandGain) / request.PassbandGain;
        var gainViolation = Math.Max(0, gainError - .05);
        var fidelity = sections.Sum(section => Math.Abs(section.FrequencyError) + 1.5 * Math.Abs(section.QError) +
            Math.Abs(section.GainError) + TransferError(new(section.ActualPoleFrequencyHz, section.ActualQ, section.ActualGain), section.Target));
        return 10_000 * (violation + gainViolation * gainViolation) + 20 * gainError * gainError + fidelity;
    }

    private static double CascadeMagnitude(IReadOnlyList<SynthesizedSection> sections, double scale, double frequency) =>
        scale * sections.Aggregate(1d, (magnitude, section) => magnitude * BandPass(frequency,
            section.ActualPoleFrequencyHz, section.ActualQ, section.ActualGain).Magnitude);

    private static double TransferError(MfbActualParameters actual, Sections.AnalogFilterSection target)
    {
        var maximum = 0d;
        foreach (var ratio in new[] { .01, .025, .063, .158, .398, 1d, 2.512, 6.31, 15.85, 39.81, 100d })
        {
            var frequency = target.PoleFrequencyHz * ratio;
            maximum = Math.Max(maximum, Complex.Abs(
                BandPass(frequency, actual.FrequencyHz, actual.Q, actual.GainMagnitude) -
                BandPass(frequency, target.PoleFrequencyHz, target.Q, target.SectionGain)) / Math.Max(target.SectionGain, 1e-12));
        }
        return maximum;
    }

    private static Complex BandPass(double frequency, double poleFrequency, double q, double gain)
    {
        var s = Complex.ImaginaryOne * frequency / poleFrequency;
        return gain * (s / q) / (s * s + s / q + 1);
    }

    private static string Key(IEnumerable<SynthesizedSection> sections) => string.Join('|', sections.SelectMany(section =>
        section.Components.Select(component => component.SelectedValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
    private static double Relative(double actual, double target) => (actual - target) / target;
    private sealed record State(SynthesizedSection[] Sections, double Score);
}
