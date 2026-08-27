using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.SallenKey;

public sealed class SallenKeyHighPassSectionSolver
{
    private readonly SallenKeySectionCalculator calculator = new();

    public SynthesizedSection Solve(AnalogFilterSection section, FilterDesignRequest request)
    {
        if (section.ResponseType != FilterResponseType.HighPass)
            throw new NotSupportedException("The Sallen-Key high-pass solver only accepts High-Pass sections.");
        return calculator.Calculate(section, request);
    }
}
