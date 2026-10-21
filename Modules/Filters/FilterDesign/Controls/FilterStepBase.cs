using System.Drawing;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public abstract class FilterStepBase : UserControl
{
    protected readonly FilterDesignState State;
    public event EventHandler? StateChanged;
    protected FilterStepBase(FilterDesignState state)
    {
        State = state;
        Dock = DockStyle.Fill;
        BackColor = SystemColors.Control;
        Padding = new Padding(8);
        AutoScroll = true;
    }

    protected void Changed() { State.NotifyChanged(); StateChanged?.Invoke(this, EventArgs.Empty); }

    protected static TableLayoutPanel FormLayout(int columns = 2)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = columns,
            RowCount = 0,
            Padding = new Padding(4),
            Margin = Padding.Empty,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var i = 1; i < columns; i++)
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / (columns - 1)));
        return layout;
    }

    protected static void AddRow(TableLayoutPanel layout, string label, Control control)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(2, 6, 10, 4)
        }, 0, row);
        control.Anchor = AnchorStyles.Left;
        control.Margin = new Padding(2, 3, 2, 3);
        layout.Controls.Add(control, 1, row);
    }

    protected static ComboBox Combo<T>(IEnumerable<T> values, Func<T, string> display)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 340,
            DropDownWidth = 400
        };
        foreach (var value in values)
            combo.Items.Add(new EnumItem<T>(value, display(value)));
        combo.SelectedIndex = 0;
        return combo;
    }

    protected static NumericUpDown Number(double value, double min = 0, double max = 1_000_000_000, int decimals = 3)
    {
        var number = new NumericUpDown
        {
            Minimum = (decimal)min,
            Maximum = (decimal)max,
            DecimalPlaces = decimals,
            Increment = decimals == 0 ? 1M : 0.1M,
            Width = 150,
            ThousandsSeparator = true
        };
        number.Value = (decimal)Math.Clamp(value, min, max);
        return number;
    }

    protected static GroupBox CreateSection(string title, Control content)
    {
        var section = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 18, 8, 8),
            Margin = Padding.Empty
        };
        section.Controls.Add(content);
        return section;
    }
    protected static string DisplayName<T>(T value) => value?.ToString() switch
    {
        "LowPass" => "Low-Pass",
        "HighPass" => "High-Pass",
        "BandPass" => "Band-Pass",
        "BandStop" => "Band-Stop",
        "ChebyshevTypeI" => "Chebyshev Type I",
        "GaussianTo6Db" => "Transitional Gaussian to 6 dB",
        "GaussianTo12Db" => "Transitional Gaussian to 12 dB",
        "LinearPhase005Degree" => "Linear Phase 0.05°",
        "LinearPhase05Degree" => "Linear Phase 0.5°",
        "SallenKey" => "Sallen-Key",
        "MultipleFeedback" => "Multiple Feedback (MFB)",
        "BainterNotch" => "Bainter Notch",
        "Rc" => "RC",
        "Lc" => "LC",
        "SingleEnded" => "Single-Ended",
        "FullyDifferential" => "Fully Differential",
        "CalculateAutomatically" => "Calculate Automatically",
        "SpecifyOrder" => "Specify Order",
        var text => text ?? string.Empty
    };
    protected sealed record EnumItem<T>(T Value, string Label) { public override string ToString() => Label; }
}
