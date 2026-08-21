using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

public readonly record struct FrequencyValue(double Value, FrequencyUnit Unit)
{
    public double Hertz => Unit switch { FrequencyUnit.KHz => Value * 1e3, FrequencyUnit.MHz => Value * 1e6, _ => Value };
}
