using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

/// <summary>
/// Allocates the requested passband gain to realizable filter sections. This keeps
/// gain inside the filter biquads instead of appending an unrelated output stage.
/// </summary>
public sealed class FilterSectionGainAllocator
{
    public IReadOnlyList<AnalogFilterSection> Allocate(
        IReadOnlyList<AnalogFilterSection> sections,
        FilterDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (request.Mode != FilterMode.Active || Math.Abs(request.PassbandGain - 1) <= 1e-12)
            return sections;
        if (request.PassbandGain <= 0 || !double.IsFinite(request.PassbandGain))
            throw new ArgumentOutOfRangeException(nameof(request.PassbandGain), "Passband gain A0 must be positive and finite.");

        var eligible = request.Implementation == FilterImplementationType.MultipleFeedback
            ? sections.Select((section, index) => (section, index)).Where(item => item.section.Order == SectionOrder.Second).Select(item => item.index).ToArray()
            : Enumerable.Range(0, sections.Count).ToArray();

        if (eligible.Length == 0)
        {
            if (request.PassbandGain < 1)
                throw new NotSupportedException("This first-order active realization cannot provide A0 below 1 without a separate attenuator.");
            eligible = Enumerable.Range(0, sections.Count).ToArray();
        }
        if (request.Implementation is FilterImplementationType.SallenKey or FilterImplementationType.BainterNotch && request.PassbandGain < 1)
            throw new NotSupportedException($"{request.Implementation} uses non-inverting filter amplifiers and cannot realize A0 below 1 without a separate attenuation stage. Choose MFB or set A0 >= 1.");

        var factor = Math.Pow(request.PassbandGain, 1d / eligible.Length);
        var eligibleSet = eligible.ToHashSet();
        return sections.Select((section, index) => eligibleSet.Contains(index) ? Apply(section, factor) : section).ToArray();
    }

    private static AnalogFilterSection Apply(AnalogFilterSection section, double factor) => section with
    {
        SectionGain = section.SectionGain * factor,
        NumeratorCoefficients = section.NumeratorCoefficients.Select(value => value * factor).ToArray()
    };
}
