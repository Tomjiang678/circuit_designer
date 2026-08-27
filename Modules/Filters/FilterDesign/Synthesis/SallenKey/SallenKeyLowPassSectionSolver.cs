using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.SallenKey;

public sealed class SallenKeyLowPassSectionSolver
{
    private readonly SallenKeySectionCalculator calculator = new();

    public SynthesizedSection Solve(AnalogFilterSection section, FilterDesignRequest request)
    {
        if (section.ResponseType != FilterResponseType.LowPass)
            throw new NotSupportedException("The Sallen-Key low-pass solver only accepts Low-Pass sections.");
        return calculator.Calculate(section, request);
    }
}
