using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterScalingStepControl : FilterStepBase
{
    public FilterScalingStepControl(FilterDesignState state) : base(state)
    {
        var layout = FormLayout();
        if (state.Mode == FilterMode.Passive && state.Implementation == FilterImplementationType.Lc)
        {
            BuildLcScaling(layout, state);
        }
        else if (state.Mode == FilterMode.Passive)
        {
            var resistorMinimum = Resistance(state.PreferredResistorMinOhm, 0.001);
            var resistorMaximum = Resistance(state.PreferredResistorMaxOhm, 0.001);
            var capacitorMinimum = Capacitance(state.PreferredCapacitorMinF);
            var capacitorMaximum = Capacitance(state.PreferredCapacitorMaxF);
            AddRow(layout, "Preferred Resistor Minimum:", resistorMinimum);
            AddRow(layout, "Preferred Resistor Maximum:", resistorMaximum);
            AddRow(layout, "Preferred Capacitor Minimum:", capacitorMinimum);
            AddRow(layout, "Preferred Capacitor Maximum:", capacitorMaximum);
            resistorMinimum.ValueChanged += (_, _) => { state.PreferredResistorMinOhm = resistorMinimum.ValueBaseUnit; Changed(); };
            resistorMaximum.ValueChanged += (_, _) => { state.PreferredResistorMaxOhm = resistorMaximum.ValueBaseUnit; Changed(); };
            capacitorMinimum.ValueChanged += (_, _) => { state.PreferredCapacitorMinF = capacitorMinimum.ValueBaseUnit; Changed(); };
            capacitorMaximum.ValueChanged += (_, _) => { state.PreferredCapacitorMaxF = capacitorMaximum.ValueBaseUnit; Changed(); };
        }
        else
        {
            var resistorMinimum = Resistance(state.PreferredResistorMinOhm, 0.001);
            var resistorMaximum = Resistance(state.PreferredResistorMaxOhm, 0.001);
            var capacitorMinimum = Capacitance(state.PreferredCapacitorMinF);
            var capacitorMaximum = Capacitance(state.PreferredCapacitorMaxF);
            AddRow(layout, "Preferred Resistor Minimum:", resistorMinimum);
            AddRow(layout, "Preferred Resistor Maximum:", resistorMaximum);
            AddRow(layout, "Preferred Capacitor Minimum:", capacitorMinimum);
            AddRow(layout, "Preferred Capacitor Maximum:", capacitorMaximum);
            resistorMinimum.ValueChanged += (_, _) => { state.PreferredResistorMinOhm = resistorMinimum.ValueBaseUnit; Changed(); };
            resistorMaximum.ValueChanged += (_, _) => { state.PreferredResistorMaxOhm = resistorMaximum.ValueBaseUnit; Changed(); };
            capacitorMinimum.ValueChanged += (_, _) => { state.PreferredCapacitorMinF = capacitorMinimum.ValueBaseUnit; Changed(); };
            capacitorMaximum.ValueChanged += (_, _) => { state.PreferredCapacitorMaxF = capacitorMaximum.ValueBaseUnit; Changed(); };
        }

        Controls.Add(CreateSection("Impedance / Component Scaling", layout));
    }

    private void BuildLcScaling(TableLayoutPanel layout, FilterDesignState state)
    {
        var preset = Combo(new[] { "50 Ω", "75 Ω", "600 Ω", "Custom" }, value => value);
        var sourceResistance = Resistance(state.SourceResistanceOhm, 0);
        var loadResistance = Resistance(state.LoadResistanceOhm, 0.001);
        AddRow(layout, "Impedance Preset:", preset);
        AddRow(layout, "Series Source Resistance (Rs):", sourceResistance);
        AddRow(layout, "Load Resistance (RL, VOUT to GND):", loadResistance);
        AddRow(layout, "Termination Model:", new Label
        {
            Text = "Rs is a series source resistance; 0 Ω omits it from Analysis and CIR. " +
                   "RL is the shunt load and supplies the LC impedance scale when Rs is 0 Ω.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = Color.DimGray
        });
        preset.SelectedIndex = state.SourceResistanceOhm == 50 && state.LoadResistanceOhm == 50 ? 0
            : state.SourceResistanceOhm == 75 && state.LoadResistanceOhm == 75 ? 1
            : state.SourceResistanceOhm == 600 && state.LoadResistanceOhm == 600 ? 2
            : 3;
        sourceResistance.ValueChanged += (_, _) => { state.SourceResistanceOhm = sourceResistance.ValueBaseUnit; Changed(); };
        loadResistance.ValueChanged += (_, _) => { state.LoadResistanceOhm = loadResistance.ValueBaseUnit; Changed(); };
        preset.SelectedIndexChanged += (_, _) =>
        {
            if (preset.SelectedIndex >= 3) return;
            var value = new[] { 50d, 75d, 600d }[preset.SelectedIndex];
            sourceResistance.ValueBaseUnit = value;
            loadResistance.ValueBaseUnit = value;
        };
    }

    private static EngineeringValueInput Resistance(double valueOhm, double minimumOhm) =>
        new(valueOhm, EngineeringUnits.Resistance, minimumOhm, 1e12);

    private static EngineeringValueInput Capacitance(double valueFarad) =>
        new(valueFarad, EngineeringUnits.Capacitance, 1e-15, 1);
}
