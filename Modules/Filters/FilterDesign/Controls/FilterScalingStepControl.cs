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
            var sourceResistance = Resistance(state.SourceResistanceOhm, 0);
            var loadResistance = Resistance(state.LoadResistanceOhm, 0.001);
            var resistorMinimum = Resistance(state.PreferredResistorMinOhm, 0.001);
            var resistorMaximum = Resistance(state.PreferredResistorMaxOhm, 0.001);
            var capacitorMinimum = Capacitance(state.PreferredCapacitorMinF);
            var capacitorMaximum = Capacitance(state.PreferredCapacitorMaxF);
            AddRow(layout, "Source Resistance (Rs):", sourceResistance);
            AddRow(layout, "Load Resistance (RL):", loadResistance);
            AddRow(layout, "Preferred Resistor Minimum:", resistorMinimum);
            AddRow(layout, "Preferred Resistor Maximum:", resistorMaximum);
            AddRow(layout, "Preferred Capacitor Minimum:", capacitorMinimum);
            AddRow(layout, "Preferred Capacitor Maximum:", capacitorMaximum);
            sourceResistance.ValueChanged += (_, _) =>
            {
                state.SourceResistanceOhm = sourceResistance.ValueBaseUnit;
                state.PassiveRcSourceResistanceOhm = state.SourceResistanceOhm;
                Changed();
            };
            loadResistance.ValueChanged += (_, _) =>
            {
                state.LoadResistanceOhm = loadResistance.ValueBaseUnit;
                state.PassiveRcLoadResistanceOhm = state.LoadResistanceOhm;
                Changed();
            };
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
        var preset = Combo(new[] { "50 Ω", "75 Ω", "600 Ω", "1 kΩ", "Custom" }, value => value);
        var policy = Combo(Enum.GetValues<LcTerminationPolicy>(), value => value switch
        {
            LcTerminationPolicy.MatchedUserTermination => "Matched User Termination",
            LcTerminationPolicy.PrototypeNativeTermination => "Prototype-Native Termination",
            _ => "Custom Independent Termination"
        });
        var orientation = Combo(Enum.GetValues<LcLadderOrientation>(), value => value switch
        {
            LcLadderOrientation.SeriesFirst => "Series First",
            LcLadderOrientation.ShuntFirst => "Shunt First",
            _ => "Auto"
        });
        var link = new CheckBox { Text = "Link Rs and RL", AutoSize = true, Checked = state.LinkLcSourceAndLoad };
        var sourceResistance = Resistance(state.SourceResistanceOhm, 0);
        var loadResistance = Resistance(state.LoadResistanceOhm, 0.001);
        AddRow(layout, "Impedance Preset:", preset);
        AddRow(layout, "Termination Policy:", policy);
        AddRow(layout, "Ladder Orientation:", orientation);
        AddRow(layout, "Matched System:", link);
        AddRow(layout, "Series Source Resistance (Rs):", sourceResistance);
        AddRow(layout, "Load Resistance (RL, VOUT to GND):", loadResistance);
        AddRow(layout, "Termination Model:", new Label
        {
            Text = "Rs and RL are independently editable. Rs > 0 selects the verified doubly terminated insertion-loss model. " +
                   "Rs = 0 selects Ideal Voltage Source mode, which is not supported by the current LC synthesis.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = Color.DimGray
        });
        preset.SelectedIndex = state.SourceResistanceOhm == 50 && state.LoadResistanceOhm == 50 ? 0
            : state.SourceResistanceOhm == 75 && state.LoadResistanceOhm == 75 ? 1
            : state.SourceResistanceOhm == 600 && state.LoadResistanceOhm == 600 ? 2
            : state.SourceResistanceOhm == 1_000 && state.LoadResistanceOhm == 1_000 ? 3
            : 4;
        policy.SelectedIndex = (int)state.LcTerminationPolicy;
        orientation.SelectedIndex = (int)state.LcLadderOrientation;
        var synchronizing = false;
        sourceResistance.ValueChanged += (_, _) =>
        {
            state.SourceResistanceOhm = sourceResistance.ValueBaseUnit;
            state.PassiveLcSourceResistanceOhm = state.SourceResistanceOhm;
            state.LcTerminationMode = state.SourceResistanceOhm > 0
                ? LcTerminationMode.DoublyTerminated
                : LcTerminationMode.IdealVoltageSource;
            if (link.Checked && !synchronizing)
            {
                synchronizing = true;
                loadResistance.ValueBaseUnit = sourceResistance.ValueBaseUnit;
                synchronizing = false;
            }
            Changed();
        };
        loadResistance.ValueChanged += (_, _) =>
        {
            state.LoadResistanceOhm = loadResistance.ValueBaseUnit;
            state.PassiveLcLoadResistanceOhm = state.LoadResistanceOhm;
            if (link.Checked && !synchronizing)
            {
                synchronizing = true;
                sourceResistance.ValueBaseUnit = loadResistance.ValueBaseUnit;
                synchronizing = false;
            }
            Changed();
        };
        link.CheckedChanged += (_, _) =>
        {
            if (synchronizing) return;
            state.LinkLcSourceAndLoad = link.Checked;
            state.LcTerminationPolicy = link.Checked
                ? LcTerminationPolicy.MatchedUserTermination
                : LcTerminationPolicy.CustomIndependentTermination;
            synchronizing = true;
            policy.SelectedIndex = (int)state.LcTerminationPolicy;
            if (link.Checked) loadResistance.ValueBaseUnit = sourceResistance.ValueBaseUnit;
            synchronizing = false;
            Changed();
        };
        policy.SelectedIndexChanged += (_, _) =>
        {
            if (synchronizing) return;
            state.LcTerminationPolicy = ((EnumItem<LcTerminationPolicy>)policy.SelectedItem!).Value;
            synchronizing = true;
            link.Checked = state.LcTerminationPolicy == LcTerminationPolicy.MatchedUserTermination;
            state.LinkLcSourceAndLoad = link.Checked;
            loadResistance.Enabled = state.LcTerminationPolicy != LcTerminationPolicy.PrototypeNativeTermination;
            if (link.Checked) loadResistance.ValueBaseUnit = sourceResistance.ValueBaseUnit;
            synchronizing = false;
            Changed();
        };
        orientation.SelectedIndexChanged += (_, _) =>
        {
            state.LcLadderOrientation = ((EnumItem<LcLadderOrientation>)orientation.SelectedItem!).Value;
            Changed();
        };
        loadResistance.Enabled = state.LcTerminationPolicy != LcTerminationPolicy.PrototypeNativeTermination;
        preset.SelectedIndexChanged += (_, _) =>
        {
            if (preset.SelectedIndex >= 4) return;
            var value = new[] { 50d, 75d, 600d, 1_000d }[preset.SelectedIndex];
            link.Checked = true;
            sourceResistance.ValueBaseUnit = value;
            loadResistance.ValueBaseUnit = value;
        };
    }

    private static EngineeringValueInput Resistance(double valueOhm, double minimumOhm) =>
        new(valueOhm, EngineeringUnits.Resistance, minimumOhm, 1e12);

    private static EngineeringValueInput Capacitance(double valueFarad) =>
        new(valueFarad, EngineeringUnits.Capacitance, 1e-15, 1);
}
