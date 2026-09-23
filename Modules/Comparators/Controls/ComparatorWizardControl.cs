using CircuitModuleDesigner.Modules.Comparators.Analysis;
using CircuitModuleDesigner.Modules.Comparators.Engines;
using CircuitModuleDesigner.Modules.Comparators.Models;
using CircuitModuleDesigner.Modules.Comparators.Reporting;
using CircuitModuleDesigner.Modules.Comparators.Schematic;
using CircuitModuleDesigner.Modules.Comparators.Services;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Comparators.Controls;

public sealed class ComparatorWizardControl : UserControl
{
    private readonly ComboBox topology = Choice(Enum.GetValues<SchmittTopology>());
    private readonly ComboBox outputMode = Choice(Enum.GetValues<WindowOutputMode>());
    private readonly ComboBox series = Choice(new[] { StandardComponentSeries.E24, StandardComponentSeries.E48, StandardComponentSeries.E96 });
    private readonly NumericUpDown lower = Number(-100, 100, 1.5m, 4);
    private readonly NumericUpDown upper = Number(-100, 100, 3.5m, 4);
    private readonly NumericUpDown vcc = Number(-100, 100, 5, 4);
    private readonly NumericUpDown vee = Number(-100, 100, 0, 4);
    private readonly NumericUpDown voh = Number(-100, 100, 4.9m, 4);
    private readonly NumericUpDown vol = Number(-100, 100, .1m, 4);
    private readonly NumericUpDown lowerHysteresis = Number(0, 100, 0, 4);
    private readonly NumericUpDown upperHysteresis = Number(0, 100, 0, 4);
    private readonly NumericUpDown minimumResistance = Number(.001m, 1_000_000_000, 1000, 3);
    private readonly NumericUpDown maximumResistance = Number(.001m, 1_000_000_000, 1_000_000, 3);
    private readonly CheckBox optimize = new() { Text = "Optimize using standard component values", AutoSize = true, Checked = true };
    private readonly CheckBox advanced = new() { Text = "Advanced boundary hysteresis", AutoSize = true };
    private readonly Label topologyLabel = FieldLabel("Topology:");
    private readonly Label outputModeLabel = FieldLabel("Output Mode:");
    private readonly Label lowerHysteresisLabel = FieldLabel("Lower boundary H (V):");
    private readonly Label upperHysteresisLabel = FieldLabel("Upper boundary H (V):");
    private readonly Button design = new() { Text = "Design", AutoSize = true, Padding = new Padding(16, 3, 16, 3) };
    private readonly Label status = new()
    {
        AutoSize = true,
        MaximumSize = new Size(1100, 0),
        ForeColor = Color.Firebrick,
        Margin = new Padding(5, 4, 5, 2),
        Padding = new Padding(0, 2, 0, 2)
    };
    private readonly FilterSchematicControl schematic = new() { Dock = DockStyle.Fill };
    private readonly ComparatorTransferPlotControl analysis = new();
    private readonly RichTextBox calculations = TextView();
    private readonly WebBrowser report = new() { Dock = DockStyle.Fill, AllowWebBrowserDrop = false, ScriptErrorsSuppressed = true, IsWebBrowserContextMenuEnabled = true };
    private readonly RichTextBox spice = TextView();
    private readonly TabControl results = new() { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
    private TreeView? navigation;
    private TreeNode? navigationRoot;
    private ComparatorDesignerKind selectedKind;
    private ComparatorDesignResult? renderedResult;

    public ComparatorWizardControl()
    {
        Dock = DockStyle.Fill; AutoScaleMode = AutoScaleMode.Dpi; BackColor = SystemColors.Control; selectedKind = ComparatorDesignerKind.SchmittTrigger;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildParameters(), 0, 0); root.Controls.Add(BuildResults(), 0, 1); Controls.Add(root);
        topology.SelectedItem = SchmittTopology.Inverting; outputMode.SelectedItem = WindowOutputMode.InsideWindowHigh; series.SelectedItem = StandardComponentSeries.E96;
        design.Click += (_, _) => RunDesign();
        advanced.CheckedChanged += (_, _) => UpdateModeControls();
        optimize.CheckedChanged += (_, _) => series.Enabled = optimize.Checked;
        UpdateModeControls();
    }

    public void AttachNavigation(TreeView tree)
    {
        if (ReferenceEquals(navigation, tree)) { BuildNavigation(); return; }
        DetachNavigation(); navigation = tree; navigation.AfterSelect += NavigationAfterSelect; BuildNavigation();
    }
    public void DetachNavigation() { if (navigation is not null) navigation.AfterSelect -= NavigationAfterSelect; navigation = null; navigationRoot = null; }

    public ComparatorDesignResult RunDesign()
    {
        try
        {
            var request = new ComparatorDesignRequest
            {
                Kind = selectedKind, Topology = (SchmittTopology)topology.SelectedItem!, OutputMode = (WindowOutputMode)outputMode.SelectedItem!,
                LowerThresholdVoltage = (double)lower.Value, UpperThresholdVoltage = (double)upper.Value,
                SupplyHighVoltage = (double)vcc.Value, SupplyLowVoltage = (double)vee.Value, OutputHighVoltage = (double)voh.Value, OutputLowVoltage = (double)vol.Value,
                LowerBoundaryHysteresisVoltage = selectedKind == ComparatorDesignerKind.WindowComparator && advanced.Checked ? (double)lowerHysteresis.Value : 0,
                UpperBoundaryHysteresisVoltage = selectedKind == ComparatorDesignerKind.WindowComparator && advanced.Checked ? (double)upperHysteresis.Value : 0,
                OptimizeStandardValues = optimize.Checked, ResistorSeries = (StandardComponentSeries)series.SelectedItem!,
                MinimumResistanceOhm = (double)minimumResistance.Value, MaximumResistanceOhm = (double)maximumResistance.Value
            };
            renderedResult = new ComparatorDesignEngine().Design(request); Render(renderedResult);
            switch (renderedResult.Status)
            {
                case ComparatorDesignStatus.Pass:
                    status.Text = string.Empty; status.ForeColor = Color.Firebrick; break;
                case ComparatorDesignStatus.Warning:
                    status.ForeColor = Color.DarkOrange;
                    status.Text = "Warning: " + (renderedResult.Warnings.Count > 0
                        ? string.Join(" ", renderedResult.Warnings)
                        : renderedResult.Message);
                    break;
                default:
                    status.ForeColor = Color.Firebrick; status.Text = "Failed: " + renderedResult.Message; break;
            }
            return renderedResult;
        }
        catch (Exception exception)
        {
            renderedResult = new ComparatorDesignResult { Request = new ComparatorDesignRequest(), Status = ComparatorDesignStatus.Failed, Message = exception.Message };
            Render(renderedResult); status.ForeColor = Color.Firebrick; status.Text = "Failed: " + exception.Message; return renderedResult;
        }
    }

    private Control BuildParameters()
    {
        var box = new GroupBox { Text = "Comparator Design Parameters", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 17, 10, 8), Margin = new Padding(6, 3, 6, 3) };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6, RowCount = 4, Margin = Padding.Empty };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        Add(table, 0, 0, FieldLabel("Lower Threshold (V):"), lower); Add(table, 2, 0, FieldLabel("Upper Threshold (V):"), upper); Add(table, 4, 0, FieldLabel("E-Series:"), series);
        Add(table, 0, 1, FieldLabel("VCC (V):"), vcc); Add(table, 2, 1, FieldLabel("VEE / GND (V):"), vee); Add(table, 4, 1, topologyLabel, topology);
        Add(table, 0, 2, FieldLabel("Output High (V):"), voh); Add(table, 2, 2, FieldLabel("Output Low (V):"), vol); Add(table, 4, 2, outputModeLabel, outputMode);
        Add(table, 0, 3, FieldLabel("Minimum R (Ω):"), minimumResistance); Add(table, 2, 3, FieldLabel("Maximum R (Ω):"), maximumResistance);
        Add(table, 4, 3, FieldLabel("Standard Value Optimization:"), optimize);
        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 5, 0, 0) };
        actionRow.Controls.Add(advanced); actionRow.Controls.Add(lowerHysteresisLabel); actionRow.Controls.Add(lowerHysteresis); actionRow.Controls.Add(upperHysteresisLabel); actionRow.Controls.Add(upperHysteresis); actionRow.Controls.Add(design);
        content.Controls.Add(table, 0, 0); content.Controls.Add(actionRow, 0, 1); content.Controls.Add(status, 0, 2);
        box.Controls.Add(content); return box;
    }

    private Control BuildResults()
    {
        results.TabPages.Add(Page("Schematic", schematic)); results.TabPages.Add(Page("Analysis", analysis));
        results.TabPages.Add(Page("Calculations", calculations)); results.TabPages.Add(Page("Report", report)); results.TabPages.Add(Page("SPICE Netlist", spice)); return results;
    }

    private void Render(ComparatorDesignResult result)
    {
        results.SuspendLayout();
        try
        {
            if (!result.Success || result.CircuitGraph is null)
            {
                schematic.Document = null; analysis.Analysis = null;
                calculations.Text = result.Message; report.DocumentText = new ComparatorDesignReportBuilder().BuildHtml(result); spice.Text = "* No netlist generated: " + result.Message; return;
            }
            schematic.Document = new ComparatorSchematicLayoutService().Layout(result.CircuitGraph); analysis.Analysis = new ComparatorTransferAnalysisService().Analyze(result);
            var builder = new ComparatorDesignReportBuilder(); calculations.Text = builder.BuildText(result); report.DocumentText = builder.BuildHtml(result);
            try { spice.Text = new ComparatorSpiceExportService().Export(result); } catch (Exception exception) { spice.Text = "* SPICE export unavailable: " + exception.Message; }
        }
        finally { results.ResumeLayout(false); }
    }

    private void UpdateModeControls()
    {
        var schmitt = selectedKind == ComparatorDesignerKind.SchmittTrigger;
        topologyLabel.Visible = topology.Visible = schmitt; outputModeLabel.Visible = outputMode.Visible = !schmitt; advanced.Visible = !schmitt;
        lowerHysteresisLabel.Visible = lowerHysteresis.Visible = !schmitt && advanced.Checked; upperHysteresisLabel.Visible = upperHysteresis.Visible = !schmitt && advanced.Checked;
        series.Enabled = optimize.Checked;
    }
    private void BuildNavigation()
    {
        if (navigation is null) return; navigation.BeginUpdate(); navigation.Nodes.Clear(); navigationRoot = navigation.Nodes.Add("Comparators & Threshold Control");
        navigationRoot.Nodes.Add("Schmitt Trigger"); navigationRoot.Nodes.Add("Window Comparator"); navigationRoot.Expand(); navigation.EndUpdate();
    }
    private void NavigationAfterSelect(object? sender, TreeViewEventArgs e)
    {
        var node = e.Node;
        if (node is null || node.Parent != navigationRoot || node.Index is < 0 or > 1) return;
        selectedKind = node.Index == 0 ? ComparatorDesignerKind.SchmittTrigger : ComparatorDesignerKind.WindowComparator; UpdateModeControls(); status.Text = string.Empty;
    }
    protected override void Dispose(bool disposing) { if (disposing) DetachNavigation(); base.Dispose(disposing); }

    private static TabPage Page(string title, Control child) { var page = new TabPage(title) { BackColor = SystemColors.Window }; page.Controls.Add(child); return page; }
    private static RichTextBox TextView() => new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window, WordWrap = false };
    private static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(5, 7, 5, 0) };
    private static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals) => new() { Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals, Width = 130, ThousandsSeparator = true };
    private static ComboBox Choice<T>(IEnumerable<T> values) { var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 }; box.Items.AddRange(values.Cast<object>().ToArray()); box.SelectedIndex = 0; return box; }
    private static void Add(TableLayoutPanel table, int column, int row, Control label, Control input) { table.Controls.Add(label, column, row); table.Controls.Add(input, column + 1, row); }
}
