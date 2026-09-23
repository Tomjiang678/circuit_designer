using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

internal sealed record OperationSynthesisResult(
    CircuitGraph Circuit,
    IReadOnlyList<SelectedAnalogComponent> Components,
    string Topology,
    string TargetRelation,
    IReadOnlyList<CalculationRecord> Calculations);

internal static class AnalogSynthesisSupport
{
    public static IReadOnlyList<double> Candidates(double theoretical, bool resistor, ComponentSelectionConstraints constraints, int radius = 3)
    {
        var minimum = resistor ? constraints.ResistorMinimumOhm : constraints.CapacitorMinimumFarad;
        var maximum = resistor ? constraints.ResistorMaximumOhm : constraints.CapacitorMaximumFarad;
        if (!(theoretical >= minimum && theoretical <= maximum) || !double.IsFinite(theoretical))
            throw new InvalidOperationException($"Theoretical {(resistor ? "resistance" : "capacitance")} {theoretical:G9} is outside [{minimum:G9}, {maximum:G9}].");
        if (!constraints.OptimizeStandardValues) return [theoretical];
        return AnalogPreferredValueSeries.Nearby(theoretical,
            resistor ? constraints.ResistorSeries : constraints.CapacitorSeries, minimum, maximum, radius);
    }

    public static SelectedAnalogComponent Component(string reference, string role, string quantity,
        double theoretical, double selected, string unit, AnalogStandardSeries? series) =>
        new(reference, role, quantity, theoretical, selected, unit, series);

    public static void ConnectOpAmpSupplies(CircuitBuilder builder, CircuitComponent opAmp)
    {
        builder.Connect("VCC", opAmp.GetPin("V+"));
        builder.Connect("VEE", opAmp.GetPin("V-"));
    }
}
