using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.SallenKey;

/// <summary>
/// Response-specific Sallen-Key band-pass solver.  The underlying calculator is
/// retained as a compatibility API while callers migrate to this responsibility.
/// </summary>
public sealed class SallenKeyBandPassSectionSolver
{
    private readonly SallenKeyBandPassSectionCalculator calculator = new();

    public SynthesizedSection Solve(AnalogFilterSection section, FilterDesignRequest request)
    {
        var solved = calculator.Calculate(section, request);
        var selected = solved.Components.ToDictionary(value => value.Role);
        var intrinsicGainNetwork = 1 + selected["R4"].SelectedValue / selected["R5"].SelectedValue;
        var intrinsicCenterGain = intrinsicGainNetwork * solved.ActualQ;
        var outputScale = selected["RB"].SelectedValue /
                          (selected["RA"].SelectedValue + selected["RB"].SelectedValue);
        return solved with
        {
            TopologyGain = new TopologyGainRealization(
                intrinsicCenterGain,
                0,
                intrinsicCenterGain,
                outputScale,
                solved.ActualGain)
        };
    }
}
