using System.ComponentModel;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Controls;

/// <summary>
/// Compact engineering-value input for this module. Unlike a generic auto-sized
/// container it keeps an explicit 260 px footprint, so MHz/kHz labels and large
/// values are not squeezed by a parent TableLayoutPanel.
/// </summary>
public sealed class EngineeringNumberInput : UserControl
{
    private readonly NumericUpDown number = new() { DecimalPlaces = 9, Increment = .1m, ThousandsSeparator = true, Width = 160 };
    private readonly ComboBox unit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
    private readonly IReadOnlyList<EngineeringUnit> units;
    private readonly double minimum;
    private readonly double maximum;
    private double baseValue;
    private bool synchronizing;

    public EngineeringNumberInput(double value, IReadOnlyList<EngineeringUnit> units, double minimum = 0, double maximum = 1e15)
    {
        this.units = units;
        this.minimum = minimum;
        this.maximum = maximum;
        baseValue = Math.Clamp(value, minimum, maximum);
        Width = 280; Height = 32; MinimumSize = new Size(280, 32); Margin = new Padding(2);
        var row = new FlowLayoutPanel { AutoSize = false, Dock = DockStyle.Fill, Width = 280, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        row.Controls.Add(number); row.Controls.Add(unit);
        Controls.Add(row);
        foreach (var engineeringUnit in units) unit.Items.Add(engineeringUnit);
        unit.SelectedItem = EngineeringUnits.BestFor(baseValue, units);
        Synchronize();
        number.ValueChanged += (_, _) =>
        {
            if (synchronizing) return;
            baseValue = Math.Clamp(((EngineeringUnit)unit.SelectedItem!).ToBase((double)number.Value), minimum, maximum);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
        unit.SelectedIndexChanged += (_, _) => Synchronize();
    }

    public override Size GetPreferredSize(Size proposedSize) => new(280, 32);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ValueBaseUnit
    {
        get => baseValue;
        set
        {
            var clamped = Math.Clamp(value, minimum, maximum);
            if (Math.Abs(clamped - baseValue) <= Math.Max(1, Math.Abs(baseValue)) * 1e-15) return;
            baseValue = clamped;
            Synchronize();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;

    private void Synchronize()
    {
        if (unit.SelectedItem is not EngineeringUnit engineeringUnit) return;
        synchronizing = true;
        try
        {
            number.Minimum = ToDecimal(minimum / engineeringUnit.BaseUnitFactor);
            number.Maximum = ToDecimal(maximum / engineeringUnit.BaseUnitFactor);
            number.Value = Math.Clamp(ToDecimal(baseValue / engineeringUnit.BaseUnitFactor), number.Minimum, number.Maximum);
        }
        finally { synchronizing = false; }
    }

    private static decimal ToDecimal(double value) => value >= 1e15 ? 1_000_000_000_000_000m : value <= -1e15 ? -1_000_000_000_000_000m : (decimal)value;
}
