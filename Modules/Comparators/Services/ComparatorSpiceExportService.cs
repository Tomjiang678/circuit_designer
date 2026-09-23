using CircuitModuleDesigner.Core.Circuit.Spice;
using CircuitModuleDesigner.Modules.Comparators.Models;

namespace CircuitModuleDesigner.Modules.Comparators.Services;

public sealed class ComparatorSpiceExportService
{
    public string Export(ComparatorDesignResult result)
    {
        if (!result.Success || result.CircuitGraph is null) throw new InvalidOperationException("A successful comparator design is required for SPICE export.");
        var r = result.Request; var sources = new List<SpiceIndependentSource>
        {
            new("V_INPUT", SpiceSourceKind.Voltage, "VIN", "0", r.SupplyLowVoltage),
            new("V_CC", SpiceSourceKind.Voltage, "VCC", "0", r.SupplyHighVoltage)
        };
        if (r.SupplyLowVoltage != 0) sources.Add(new("V_EE", SpiceSourceKind.Voltage, "VEE", "0", r.SupplyLowVoltage));
        var start = r.SupplyLowVoltage; var stop = r.SupplyHighVoltage; var step = Math.Max((stop - start) / 500, 1e-6);
        var options = new CircuitSpiceExportOptions("Comparator threshold design")
        {
            Sources = sources, Analyses = [new SpiceDcAnalysis("V_INPUT", start, stop, step)], OutputExpressions = ["V(VOUT)"], HighImpedanceOutputLoadOhm = 1e9,
            AdditionalComments = [$"Static GenericComparatorModel; VOH={r.OutputHighVoltage:G9} V, VOL={r.OutputLowVoltage:G9} V.", "Behavioral comparator export intentionally makes no propagation-delay, offset, bias-current, or noise claim."]
        };
        return new LtspiceNetlistExporter().Export(result.CircuitGraph, options);
    }
}
