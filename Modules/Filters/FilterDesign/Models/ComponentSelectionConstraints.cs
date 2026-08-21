using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

public sealed class ComponentSelectionConstraints
{
    public StandardComponentSeries ResistorSeries { get; set; }
    public StandardComponentSeries CapacitorSeries { get; set; }
    public StandardComponentSeries InductorSeries { get; set; }
    public double ResistorTolerancePercent { get; set; }
    public double CapacitorTolerancePercent { get; set; }
    public double InductorTolerancePercent { get; set; }
    public double ResistorMinimumOhm { get; set; }
    public double ResistorMaximumOhm { get; set; }
    public double CapacitorMinimumF { get; set; }
    public double CapacitorMaximumF { get; set; }
    public double InductorMinimumH { get; set; } = 100e-9;
    public double InductorMaximumH { get; set; } = 10;
    public bool OptimizeStandardValues { get; set; }
    public FilterOptimizationTarget OptimizationTarget { get; set; }
}
