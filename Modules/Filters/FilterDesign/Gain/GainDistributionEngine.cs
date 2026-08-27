using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Gain;

/// <summary>
/// Converts the user's overall A0 into topology targets.  It deliberately does
/// not geometrically spread A0 over every biquad: a single selected filter stage
/// carries the required gain when that topology can realize it; attenuation is
/// represented separately as an interstage/output scale.
/// </summary>
public sealed class GainDistributionEngine
{
    public GainDistributionPlan Plan(IReadOnlyList<AnalogFilterSection> source, FilterDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (request.Mode != FilterMode.Active)
            return new(1, source, source.Select(s => new SectionGainAssignment(s.Index, s.SectionGain, s.SectionGain, 1,
                FilterStagePolarity.NonInverting)).ToArray(), 1, FilterStagePolarity.NonInverting);
        if (request.PassbandGain <= 0 || !double.IsFinite(request.PassbandGain))
            throw new ArgumentOutOfRangeException(nameof(request.PassbandGain));

        var sections = source.ToArray();
        var outputScale = 1d;
        var eligible = Enumerable.Range(0, sections.Length)
            .Where(index => request.Implementation != FilterImplementationType.MultipleFeedback ||
                            sections[index].Order == SectionOrder.Second)
            .ToArray();
        if (eligible.Length == 0) eligible = Enumerable.Range(0, sections.Length).ToArray();

        // The pole/section builder already supplies any response-normalization
        // gain (notably BP). A0 is an independent multiplier on that transfer.
        if (Math.Abs(request.PassbandGain - 1) > 1e-12)
        {
            if (request.Implementation is FilterImplementationType.SallenKey or FilterImplementationType.BainterNotch && request.PassbandGain < 1)
                outputScale = request.PassbandGain;
            else
            {
                var selected = eligible[^1];
                sections[selected] = Scale(sections[selected], request.PassbandGain);
            }
        }

        var assignments = sections.Select(section => new SectionGainAssignment(
            section.Index,
            source.Single(value => value.Index == section.Index).SectionGain,
            section.SectionGain,
            1,
            request.Implementation == FilterImplementationType.MultipleFeedback && section.Order == SectionOrder.Second
                ? FilterStagePolarity.Inverting
                : FilterStagePolarity.NonInverting)).ToArray();
        var inversions = assignments.Count(value => value.Polarity == FilterStagePolarity.Inverting);
        return new(request.PassbandGain, sections, assignments, outputScale,
            inversions % 2 == 0 ? FilterStagePolarity.NonInverting : FilterStagePolarity.Inverting);
    }

    public GainDistributionSummary Summarize(
        GainDistributionPlan plan,
        IReadOnlyList<SynthesizedSection> sections,
        double actualPassbandPeakGain,
        double referencePhaseDegrees)
    {
        var raw = sections.Aggregate(1d, (value, section) => value * section.ActualGain);
        var scale = plan.OutputScale * plan.Assignments.Aggregate(1d, (value, item) => value * item.InterstageScale);
        var warnings = new List<string>();
        var cumulative = 1d;
        foreach (var section in sections)
        {
            var localPeakEstimate = section.ActualGain * Math.Max(1, section.ActualQ);
            cumulative *= localPeakEstimate;
            if (cumulative > 8)
                warnings.Add($"Section {section.Target.Index} raises the conservative cumulative internal peak estimate to {cumulative:0.###} V/V. Check headroom against the eventual op-amp supply and input amplitude.");
        }
        var error = (actualPassbandPeakGain - plan.RequestedOverallGain) / plan.RequestedOverallGain;
        if (Math.Abs(error) > .05)
            warnings.Add($"Final passband peak gain is {actualPassbandPeakGain:0.####} V/V; requested A0 is {plan.RequestedOverallGain:0.####} V/V ({error:P2}).");
        return new(plan.RequestedOverallGain, raw, scale, actualPassbandPeakGain,
            plan.ExpectedOverallPolarity, referencePhaseDegrees, warnings);
    }

    private static AnalogFilterSection Scale(AnalogFilterSection section, double factor) => section with
    {
        SectionGain = section.SectionGain * factor,
        NumeratorCoefficients = section.NumeratorCoefficients.Select(value => value * factor).ToArray()
    };
}
