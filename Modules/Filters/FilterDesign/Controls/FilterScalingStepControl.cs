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
            var preset = Combo(new[] { "50 Ω", "75 Ω", "600 Ω", "Custom" }, value => value);
            var rs = Number(state.SourceResistanceOhm, 0, 1e9);
            var rl = Number(state.LoadResistanceOhm, 0.001, 1e9);
            AddRow(layout, "Impedance Preset:", preset);
            AddRow(layout, "Series Source Resistance (Rs), Ω:", rs);
            AddRow(layout, "Load Resistance (RL, VOUT to GND), Ω:", rl);
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
            rs.ValueChanged += (_, _) => { state.SourceResistanceOhm = (double)rs.Value; Changed(); };
            rl.ValueChanged += (_, _) => { state.LoadResistanceOhm = (double)rl.Value; Changed(); };
            preset.SelectedIndexChanged += (_, _) =>
            {
                if (preset.SelectedIndex >= 3) return;
                var value = new[] { 50d, 75d, 600d }[preset.SelectedIndex];
                rs.Value = (decimal)value; rl.Value = (decimal)value;
            };
        }
        else if (state.Mode == FilterMode.Passive)
        {
            var resistance = Number(state.PreferredResistanceOhm, 0.001, 1e9);
            AddRow(layout, "Preferred Resistance Scale, Ω:", resistance);
            resistance.ValueChanged += (_, _) => { state.PreferredResistanceOhm = (double)resistance.Value; Changed(); };
        }
        else
        {
            var minimum = Number(state.PreferredResistorMinOhm, 0.001, 1e9);
            var maximum = Number(state.PreferredResistorMaxOhm, 0.001, 1e9);
            AddRow(layout, "Preferred Resistor Minimum, Ω:", minimum);
            AddRow(layout, "Preferred Resistor Maximum, Ω:", maximum);
            AddRow(layout, "Preferred Capacitor Range:", new Label { Text = "100 pF - 1 µF", AutoSize = true });
            minimum.ValueChanged += (_, _) => { state.PreferredResistorMinOhm = (double)minimum.Value; Changed(); };
            maximum.ValueChanged += (_, _) => { state.PreferredResistorMaxOhm = (double)maximum.Value; Changed(); };
        }
        Controls.Add(CreateSection("Impedance / Component Scaling", layout));
    }
}
