using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Bainter;

public sealed class BainterNotchSectionSolver
{
    private readonly BainterNotchSectionCalculator calculator = new();

    public SynthesizedSection Solve(AnalogFilterSection section, FilterDesignRequest request)
    {
        var solved = calculator.Calculate(section, request);
        return solved with
        {
            TopologyGain = new TopologyGainRealization(
                solved.ActualGain,
                1,
                double.PositiveInfinity,
                1,
                solved.ActualGain)
        };
    }
}
