using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Services;

public static class UnitConversion
{
    public static double ToHertz(double value, FrequencyUnit unit) => unit switch { FrequencyUnit.KHz => value * 1e3, FrequencyUnit.MHz => value * 1e6, _ => value };
    public static double FromHertz(double value, FrequencyUnit unit) => unit switch { FrequencyUnit.KHz => value / 1e3, FrequencyUnit.MHz => value / 1e6, _ => value };
    public static double ToOhms(double value, ResistanceUnit unit) => unit switch { ResistanceUnit.KOhm => value * 1e3, ResistanceUnit.MOhm => value * 1e6, _ => value };
    public static double ToFarads(double value, CapacitanceUnit unit) => unit switch { CapacitanceUnit.PF => value * 1e-12, CapacitanceUnit.NF => value * 1e-9, CapacitanceUnit.UF => value * 1e-6, CapacitanceUnit.MF => value * 1e-3, _ => value };
    public static double GainToDb(double linear) => linear > 0 ? 20 * Math.Log10(linear) : double.NaN;
}
