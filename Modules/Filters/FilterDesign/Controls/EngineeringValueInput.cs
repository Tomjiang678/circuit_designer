using System.ComponentModel;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed record EngineeringUnit(string Symbol, double BaseUnitFactor)
{
    public double ToBase(double displayedValue) => displayedValue * BaseUnitFactor;
    public double FromBase(double baseValue) => baseValue / BaseUnitFactor;
    public override string ToString() => Symbol;
}

public static class EngineeringUnits
{
    public static IReadOnlyList<EngineeringUnit> Frequency { get; } =
    [
        new("Hz", 1), new("kHz", 1e3), new("MHz", 1e6)
    ];

    public static IReadOnlyList<EngineeringUnit> Resistance { get; } =
    [
        new("Ω", 1), new("kΩ", 1e3), new("MΩ", 1e6)
    ];

    public static IReadOnlyList<EngineeringUnit> Capacitance { get; } =
    [
        new("pF", 1e-12), new("nF", 1e-9), new("µF", 1e-6), new("mF", 1e-3), new("F", 1)
    ];

    public static IReadOnlyList<EngineeringUnit> Inductance { get; } =
    [
        new("nH", 1e-9), new("µH", 1e-6), new("mH", 1e-3), new("H", 1)
    ];

    public static EngineeringUnit BestFor(double baseValue, IReadOnlyList<EngineeringUnit> units)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (units.Count == 0) throw new ArgumentException("At least one engineering unit is required.", nameof(units));
        if (baseValue == 0) return units[0];

        for (var index = units.Count - 1; index >= 0; index--)
            if (Math.Abs(units[index].FromBase(baseValue)) >= 1)
                return units[index];

        return units[0];
    }
}

public sealed class EngineeringValueInput : UserControl
{
    private const decimal DisplayMaximum = 1_000_000_000_000_000M;
    private readonly NumericUpDown number;
    private readonly ComboBox unitSelector;
    private readonly IReadOnlyList<EngineeringUnit> units;
    private readonly double minimumBaseValue;
    private readonly double maximumBaseValue;
    private double baseValue;
    private bool synchronizing;

    public event EventHandler? ValueChanged;

    public EngineeringValueInput(
        double valueBaseUnit,
        IReadOnlyList<EngineeringUnit> units,
        double minimumBaseValue = 0,
        double maximumBaseValue = 1e15)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (units.Count == 0) throw new ArgumentException("At least one engineering unit is required.", nameof(units));
        if (!double.IsFinite(minimumBaseValue) || !double.IsFinite(maximumBaseValue) || minimumBaseValue > maximumBaseValue)
            throw new ArgumentOutOfRangeException(nameof(minimumBaseValue));

        this.units = units;
        this.minimumBaseValue = minimumBaseValue;
        this.maximumBaseValue = maximumBaseValue;
        baseValue = Math.Clamp(valueBaseUnit, minimumBaseValue, maximumBaseValue);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;

        number = new NumericUpDown
        {
            DecimalPlaces = 9,
            Increment = 0.1M,
            ThousandsSeparator = true,
            Width = 150,
            Margin = new Padding(0, 0, 6, 0)
        };
        unitSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 76,
            Margin = Padding.Empty
        };
        foreach (var unit in units) unitSelector.Items.Add(unit);

        var layout = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.Controls.Add(number);
        layout.Controls.Add(unitSelector);
        Controls.Add(layout);

        unitSelector.SelectedItem = EngineeringUnits.BestFor(baseValue, units);
        SynchronizeDisplay();
        number.ValueChanged += NumberValueChanged;
        unitSelector.SelectedIndexChanged += (_, _) => SynchronizeDisplay();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ValueBaseUnit
    {
        get => baseValue;
        set
        {
            var clamped = Math.Clamp(value, minimumBaseValue, maximumBaseValue);
            if (Math.Abs(clamped - baseValue) <= Math.Max(1, Math.Abs(baseValue)) * 1e-15) return;
            baseValue = clamped;
            SynchronizeDisplay();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string SelectedUnitSymbol => SelectedUnit.Symbol;
    private EngineeringUnit SelectedUnit => (EngineeringUnit)unitSelector.SelectedItem!;

    private void NumberValueChanged(object? sender, EventArgs e)
    {
        if (synchronizing) return;
        baseValue = Math.Clamp(SelectedUnit.ToBase((double)number.Value), minimumBaseValue, maximumBaseValue);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeDisplay()
    {
        if (unitSelector.SelectedItem is not EngineeringUnit unit) return;
        synchronizing = true;
        try
        {
            var minimum = ToDecimal(minimumBaseValue / unit.BaseUnitFactor);
            var maximum = ToDecimal(maximumBaseValue / unit.BaseUnitFactor);
            number.Minimum = Math.Max(-DisplayMaximum, minimum);
            number.Maximum = Math.Min(DisplayMaximum, maximum);
            var displayValue = ToDecimal(unit.FromBase(baseValue));
            number.Value = Math.Clamp(displayValue, number.Minimum, number.Maximum);
        }
        finally
        {
            synchronizing = false;
        }
    }

    private static decimal ToDecimal(double value)
    {
        if (value >= (double)DisplayMaximum) return DisplayMaximum;
        if (value <= (double)-DisplayMaximum) return -DisplayMaximum;
        return (decimal)value;
    }
}
