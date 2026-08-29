using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Bainter;

public sealed class BainterNotchSectionSolver
{
    private readonly BainterNotchSectionCalculator calculator = new();

    public SynthesizedSection Solve(AnalogFilterSection section, FilterDesignRequest request)
    {
        return WithTopologyGain(calculator.Calculate(section, request));
    }

    public IReadOnlyList<SynthesizedSection> SolveCandidates(AnalogFilterSection section, FilterDesignRequest request) =>
        calculator.CalculateCandidates(section, request).Select(WithTopologyGain).ToArray();

    private static SynthesizedSection WithTopologyGain(SynthesizedSection solved) => solved with
        {
            TopologyGain = new TopologyGainRealization(
                solved.ActualGain,
                1,
                double.PositiveInfinity,
                1,
                solved.ActualGain)
        };
}
