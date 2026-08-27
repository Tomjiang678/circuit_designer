using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

/// <summary>Verified first-order passive RC realization; higher orders are rejected by the support/implementation gate.</summary>
public sealed class PassiveRcSectionCalculator
{
    private readonly SallenKeySectionCalculator commonFirstOrderMath = new();

    public SynthesizedSection Calculate(AnalogFilterSection section, FilterDesignRequest request)
    {
        if (section.Order != SectionOrder.First)
            throw new NotSupportedException("Passive RC synthesis is restricted to one first-order section.");
        return commonFirstOrderMath.Calculate(section, request);
    }
}
