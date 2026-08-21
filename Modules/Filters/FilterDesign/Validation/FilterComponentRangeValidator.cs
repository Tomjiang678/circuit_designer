using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;

public static class FilterComponentRangeValidator
{
    public static void EnsureWithinConstraints(CircuitGraph graph, ComponentSelectionConstraints constraints)
    {
        foreach (var component in graph.Components)
        {
            var (parameter, minimum, maximum, unit) = component.Type switch
            {
                ComponentType.Resistor => (ComponentParameterNames.ResistanceOhm, constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm, "ohm"),
                ComponentType.Capacitor => (ComponentParameterNames.CapacitanceFarad, constraints.CapacitorMinimumF, constraints.CapacitorMaximumF, "F"),
                ComponentType.Inductor => (ComponentParameterNames.InductanceHenry, constraints.InductorMinimumH, constraints.InductorMaximumH, "H"),
                _ => (string.Empty, 0d, double.PositiveInfinity, string.Empty)
            };
            if (parameter.Length == 0 || !component.Parameters.TryGetValue(parameter, out var value)) continue;
            if (value < minimum || value > maximum)
                throw new InvalidOperationException($"{component.Reference} = {value:G6} {unit} is outside the selected {minimum:G6}..{maximum:G6} {unit} component range.");
        }
    }
}
