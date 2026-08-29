using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterComponentsStepControl : FilterStepBase
{
    public FilterComponentsStepControl(FilterDesignState state) : base(state)
    {
        var layout = FormLayout();
        var resistorSeries = Combo(Enum.GetValues<StandardComponentSeries>(), DisplayName);
        var capacitorSeries = Combo(Enum.GetValues<StandardComponentSeries>(), DisplayName);
        var inductorSeries = Combo(Enum.GetValues<StandardComponentSeries>(), DisplayName);
        resistorSeries.SelectedIndex = (int)state.ResistorSeries;
        capacitorSeries.SelectedIndex = (int)state.CapacitorSeries;
        inductorSeries.SelectedIndex = (int)state.InductorSeries;

        var resistorTolerance = Number(state.ResistorTolerancePercent, 0, 100);
        var capacitorTolerance = Number(state.CapacitorTolerancePercent, 0, 100);
        var inductorTolerance = Number(state.InductorTolerancePercent, 0, 100);
        var optimize = new CheckBox
        {
            Text = "Optimize using standard component values",
            AutoSize = true,
            Checked = state.OptimizeStandardValues
        };
        var target = Combo(Enum.GetValues<FilterOptimizationTarget>(), value =>
            value == FilterOptimizationTarget.MinimizeFrequencyError ? "Minimize frequency error" : "Minimize Q error");
        target.Width = 360;
        target.DropDownWidth = 400;
        target.SelectedIndex = (int)state.OptimizationTarget;

        AddRow(layout, "Resistor Series:", resistorSeries);
        AddRow(layout, "Capacitor Series:", capacitorSeries);
        AddRow(layout, "Resistor Tolerance, %:", resistorTolerance);
        AddRow(layout, "Capacitor Tolerance, %:", capacitorTolerance);
        AddRow(layout, "Standard Value Optimization:", optimize);
        AddRow(layout, "Optimization Target:", target);

        EngineeringValueInput? inductorMinimum = null;
        EngineeringValueInput? inductorMaximum = null;
        EngineeringValueInput? capacitorMinimum = null;
        EngineeringValueInput? capacitorMaximum = null;
        if (state.Mode == FilterMode.Passive && state.Implementation == FilterImplementationType.Lc)
        {
            AddRow(layout, "Inductor Series:", inductorSeries);
            AddRow(layout, "Inductor Tolerance, %:", inductorTolerance);
            inductorMinimum = new EngineeringValueInput(state.InductorMinimumH, EngineeringUnits.Inductance, 1e-12, 1e3);
            inductorMaximum = new EngineeringValueInput(state.InductorMaximumH, EngineeringUnits.Inductance, 1e-12, 1e3);
            capacitorMinimum = new EngineeringValueInput(state.PassiveLcCapacitorMinimumF, EngineeringUnits.Capacitance, 1e-15, 1);
            capacitorMaximum = new EngineeringValueInput(state.PassiveLcCapacitorMaximumF, EngineeringUnits.Capacitance, 1e-15, 1);
            AddRow(layout, "Inductor Minimum:", inductorMinimum);
            AddRow(layout, "Inductor Maximum:", inductorMaximum);
            AddRow(layout, "LC Capacitor Minimum:", capacitorMinimum);
            AddRow(layout, "LC Capacitor Maximum:", capacitorMaximum);
        }

        resistorSeries.SelectedIndexChanged += (_, _) =>
        {
            state.ResistorSeries = ((EnumItem<StandardComponentSeries>)resistorSeries.SelectedItem!).Value;
            Changed();
        };
        capacitorSeries.SelectedIndexChanged += (_, _) =>
        {
            state.CapacitorSeries = ((EnumItem<StandardComponentSeries>)capacitorSeries.SelectedItem!).Value;
            Changed();
        };
        resistorTolerance.ValueChanged += (_, _) => { state.ResistorTolerancePercent = (double)resistorTolerance.Value; Changed(); };
        capacitorTolerance.ValueChanged += (_, _) => { state.CapacitorTolerancePercent = (double)capacitorTolerance.Value; Changed(); };
        optimize.CheckedChanged += (_, _) => { state.OptimizeStandardValues = optimize.Checked; Changed(); };
        target.SelectedIndexChanged += (_, _) =>
        {
            state.OptimizationTarget = ((EnumItem<FilterOptimizationTarget>)target.SelectedItem!).Value;
            Changed();
        };
        inductorSeries.SelectedIndexChanged += (_, _) =>
        {
            state.InductorSeries = ((EnumItem<StandardComponentSeries>)inductorSeries.SelectedItem!).Value;
            Changed();
        };
        inductorTolerance.ValueChanged += (_, _) => { state.InductorTolerancePercent = (double)inductorTolerance.Value; Changed(); };

        if (inductorMinimum is not null)
        {
            inductorMinimum.ValueChanged += (_, _) => { state.InductorMinimumH = inductorMinimum.ValueBaseUnit; Changed(); };
            inductorMaximum!.ValueChanged += (_, _) => { state.InductorMaximumH = inductorMaximum.ValueBaseUnit; Changed(); };
            capacitorMinimum!.ValueChanged += (_, _) => { state.PassiveLcCapacitorMinimumF = capacitorMinimum.ValueBaseUnit; Changed(); };
            capacitorMaximum!.ValueChanged += (_, _) => { state.PassiveLcCapacitorMaximumF = capacitorMaximum.ValueBaseUnit; Changed(); };
        }

        Controls.Add(CreateSection("Component Constraints", layout));
    }
}
