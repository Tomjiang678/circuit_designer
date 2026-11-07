using CircuitModuleDesigner.Controls;
using CircuitModuleDesigner.Modules.Comparators.Analysis;
using CircuitModuleDesigner.Modules.Comparators.Engines;
using CircuitModuleDesigner.Modules.Comparators.Models;
using CircuitModuleDesigner.Modules.Comparators.Reporting;
using CircuitModuleDesigner.Modules.Comparators.Schematic;
using CircuitModuleDesigner.Modules.Comparators.Services;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Services;

namespace CircuitModuleDesigner.Modules.Comparators.Controls;

public sealed class ComparatorWizardControl : UserControl, IDesignModuleHost
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
    private readonly NumericUpDown frequency = Number(.001m, 1_000_000_000, 1000, 3);
    private readonly NumericUpDown dutyCycle = Number(20, 80, 50, 2);
    private readonly NumericUpDown minimumResistance = Number(.001m, 1_000_000_000, 1000, 3);
    private readonly NumericUpDown maximumResistance = Number(.001m, 1_000_000_000, 1_000_000, 3);
    private readonly CheckBox optimize = new() { Text = "Enabled", AutoSize = true, Checked = true };
    private readonly CheckBox advanced = new() { Text = "Advanced boundary hysteresis", AutoSize = true };
    private readonly Label topologyLabel = FieldLabel("Topology:");
    private readonly Label outputModeLabel = FieldLabel("Output Mode:");
    private readonly Label lowerHysteresisLabel = FieldLabel("Lower boundary H (V):");
    private readonly Label upperHysteresisLabel = FieldLabel("Upper boundary H (V):");
    private readonly Label lowerLabel = FieldLabel("Lower Threshold (V):");
    private readonly Label upperLabel = FieldLabel("Upper Threshold (V):");
    private readonly Label seriesLabel = FieldLabel("E-Series:");
    private readonly Label minimumResistanceLabel = FieldLabel("Minimum R (Ω):");
    private readonly Label maximumResistanceLabel = FieldLabel("Maximum R (Ω):");
    private readonly Label optimizationLabel = FieldLabel("Standard Value Optimization:");
    private readonly Label frequencyLabel = FieldLabel("Frequency (Hz):");
    private readonly Label dutyCycleLabel = FieldLabel("Duty Cycle (%):");
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
    private readonly RelaxationOscillatorPlotControl oscillatorAnalysis = new(){Visible=false};
    private readonly Panel analysisHost = new(){Dock=DockStyle.Fill};
    private readonly RichTextBox calculations = TextView();
    private readonly WebBrowser report = new() { Dock = DockStyle.Fill, AllowWebBrowserDrop = false, ScriptErrorsSuppressed = true, IsWebBrowserContextMenuEnabled = true };
    private readonly RichTextBox spice = TextView();
    private readonly TabControl results = new() { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
    private TreeView? navigation;
    private TreeNode? navigationRoot;
    private ComparatorDesignerKind selectedKind;
    private ComparatorDesignResult? renderedResult;
    private bool resultIsStale;

    public ComparatorWizardControl()
    {
        Dock = DockStyle.Fill; AutoScaleMode = AutoScaleMode.Dpi; BackColor = SystemColors.Control; selectedKind = ComparatorDesignerKind.SchmittTrigger;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var parameters = BuildParameters();
        root.Controls.Add(parameters, 0, 0); root.Controls.Add(BuildResults(), 0, 1); Controls.Add(root);
        topology.SelectedItem = SchmittTopology.Inverting; outputMode.SelectedItem = WindowOutputMode.InsideWindowHigh; series.SelectedItem = StandardComponentSeries.E96;
        design.Click += (_, _) => RunDesign();
        advanced.CheckedChanged += (_, _) => UpdateModeControls();
        optimize.CheckedChanged += (_, _) => series.Enabled = optimize.Checked;
        UpdateModeControls();
        HookParameters(parameters);
    }

    public void AttachNavigation(TreeView tree)
    {
        if (ReferenceEquals(navigation, tree)) { BuildNavigation(); return; }
        DetachNavigation(); navigation = tree; navigation.AfterSelect += NavigationAfterSelect; BuildNavigation();
    }
    public void DetachNavigation() { if (navigation is not null) navigation.AfterSelect -= NavigationAfterSelect; navigation = null; navigationRoot = null; }

    public string ModuleTitle => "Comparators & Threshold Control";
    public bool HasDesign => renderedResult is { Success: true };
    public bool IsStale => resultIsStale;

    public string BuildDesignJson() =>
        !resultIsStale && renderedResult is { Success: true } result
            ? new ComparatorDesignReportService().ExportJson(result)
            : string.Empty;

    public string BuildDesignSummary() =>
        !resultIsStale && renderedResult is { Success: true } result
            ? new ComparatorDesignReportBuilder().BuildText(result)
            : string.Empty;

    public string? BuildNetlist()
    {
        if (resultIsStale || renderedResult is not { Success: true } result) return null;
        try
        {
            return new ComparatorSpiceExportService().Export(result);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Calculate() => RunDesign();

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
                MinimumResistanceOhm = (double)minimumResistance.Value, MaximumResistanceOhm = (double)maximumResistance.Value,
                TargetFrequencyHz=(double)frequency.Value,DutyCyclePercent=(double)dutyCycle.Value
            };
            if(selectedKind==ComparatorDesignerKind.RelaxationOscillator)
                request=request with{OptimizeStandardValues=true,ResistorSeries=StandardComponentSeries.E96,
                    MinimumResistanceOhm=1_000,MaximumResistanceOhm=1_000_000};
            renderedResult = new ComparatorDesignEngine().Design(request);
            resultIsStale = false;
            Render(renderedResult);
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
            renderedResult = new ComparatorDesignResult { Request = new ComparatorDesignRequest{Kind=selectedKind}, Status = ComparatorDesignStatus.Failed, Message = exception.Message };
            Render(renderedResult); status.ForeColor = Color.Firebrick; status.Text = "Failed: " + exception.Message; return renderedResult;
        }
    }

    private Control BuildParameters()
    {
        var box = new GroupBox { Text = "Comparator Design Parameters", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 17, 10, 8), Margin = new Padding(6, 3, 6, 3) };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 6, Margin = Padding.Empty };
        for (var column = 0; column < 4; column++) table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Add(table, 0, 0, lowerLabel, lower); Add(table, 2, 0, upperLabel, upper);
        Add(table, 0, 0, frequencyLabel, frequency); Add(table, 2, 0, dutyCycleLabel, dutyCycle);
        Add(table, 0, 1, FieldLabel("VCC (V):"), vcc); Add(table, 2, 1, FieldLabel("VEE / GND (V):"), vee);
        Add(table, 0, 2, FieldLabel("Output High (V):"), voh); Add(table, 2, 2, FieldLabel("Output Low (V):"), vol);
        Add(table, 0, 3, minimumResistanceLabel, minimumResistance); Add(table, 2, 3, maximumResistanceLabel, maximumResistance);
        Add(table, 0, 4, seriesLabel, series); Add(table, 2, 4, topologyLabel, topology);
        Add(table, 0, 5, outputModeLabel, outputMode); Add(table, 2, 5, optimizationLabel, optimize);
        var actionRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 5, 0, 0) };
        actionRow.Controls.Add(advanced); actionRow.Controls.Add(design);
        var hysteresisRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 0) };
        hysteresisRow.Controls.Add(lowerHysteresisLabel); hysteresisRow.Controls.Add(lowerHysteresis);
        hysteresisRow.Controls.Add(upperHysteresisLabel); hysteresisRow.Controls.Add(upperHysteresis);
        content.Controls.Add(table, 0, 0); content.Controls.Add(actionRow, 0, 1); content.Controls.Add(hysteresisRow, 0, 2); content.Controls.Add(status, 0, 3);
        box.Controls.Add(content); return box;
    }

    private Control BuildResults()
    {
        analysisHost.Controls.Add(analysis);analysisHost.Controls.Add(oscillatorAnalysis);
        results.TabPages.Add(Page("Schematic", schematic)); results.TabPages.Add(Page("Analysis", analysisHost));
        results.TabPages.Add(Page("Calculations", calculations)); results.TabPages.Add(Page("Report", BuildReportExportView())); results.TabPages.Add(Page("SPICE Netlist", BuildSpiceExportView())); return results;
    }

    private Control BuildSpiceExportView()
    {
        var layout = ExportLayout();
        var buttons = ExportButtons();
        var save = new Button { Text = "Save CIR...", AutoSize = true, MinimumSize = new Size(95, 27) };
        var copy = new Button { Text = "Copy Netlist", AutoSize = true, MinimumSize = new Size(105, 27) };
        save.Click += (_, _) => ExportCurrentCir();
        copy.Click += (_, _) => { if (spice.TextLength > 0) Clipboard.SetText(spice.Text); };
        buttons.Controls.Add(save); buttons.Controls.Add(copy); layout.Controls.Add(buttons, 0, 0); layout.Controls.Add(spice, 0, 1);
        return layout;
    }

    private Control BuildReportExportView()
    {
        var layout = ExportLayout();
        var buttons = ExportButtons();
        var html = new Button { Text = "Export HTML Report...", AutoSize = true, MinimumSize = new Size(145, 28) };
        var json = new Button { Text = "Export JSON Snapshot...", AutoSize = true, MinimumSize = new Size(155, 28) };
        html.Click += (_, _) => ExportCurrentReport(false); json.Click += (_, _) => ExportCurrentReport(true);
        buttons.Controls.Add(html); buttons.Controls.Add(json); layout.Controls.Add(buttons, 0, 0); layout.Controls.Add(report, 0, 1);
        return layout;
    }

    private static TableLayoutPanel ExportLayout()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return layout;
    }

    private static FlowLayoutPanel ExportButtons() => new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(5, 4, 5, 4) };

    private void ExportCurrentCir()
    {
        if (!EnsureExportableDesign()) return;
        var result = renderedResult!;
        using var dialog = new SaveFileDialog
        {
            Filter = "SPICE Circuit (*.cir)|*.cir|SPICE Netlist (*.net)|*.net|All files (*.*)|*.*",
            DefaultExt = "cir", AddExtension = true, FileName = $"comparator-{result.Request.Kind}.cir"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { new ComparatorSpiceExportService().Save(result, dialog.FileName); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "CIR Export", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private bool EnsureExportableDesign()
    {
        if (resultIsStale)
        {
            MessageBox.Show(this, "Parameters changed; run the design again before exporting.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        if (renderedResult is { Success: true }) return true;
        MessageBox.Show(this, "Generate and verify a design before exporting its netlist or report.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void ExportCurrentReport(bool json)
    {
        if (!EnsureExportableDesign()) return;
        var result = renderedResult!;
        using var dialog = new SaveFileDialog
        {
            Filter = json ? "JSON Snapshot (*.json)|*.json|All files (*.*)|*.*" : "HTML Report (*.html)|*.html|All files (*.*)|*.*",
            DefaultExt = json ? "json" : "html", AddExtension = true,
            FileName = $"comparator-{result.Request.Kind}-report.{(json ? "json" : "html")}"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var service = new ComparatorDesignReportService();
            if (json) service.SaveJson(result, dialog.FileName); else service.SaveHtml(result, dialog.FileName);
            MessageBox.Show(this, $"Report exported successfully.\r\n{dialog.FileName}", "Design Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Design Report", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Render(ComparatorDesignResult result)
    {
        results.SuspendLayout();
        try
        {
            if (!result.Success || result.CircuitGraph is null)
            {
                schematic.Document = null; analysis.Analysis = null;oscillatorAnalysis.Analysis=null;
                SetAnalysisMode(result.Request.Kind==ComparatorDesignerKind.RelaxationOscillator);
                calculations.Text = result.Message; report.DocumentText = new ComparatorDesignReportBuilder().BuildHtml(result); spice.Text = "* No netlist generated: " + result.Message; return;
            }
            schematic.Document = new ComparatorSchematicLayoutService().Layout(result.CircuitGraph);
            var oscillator=result.Request.Kind==ComparatorDesignerKind.RelaxationOscillator;
            SetAnalysisMode(oscillator);
            if(oscillator)
            {
                analysis.Analysis=null;
                oscillatorAnalysis.Analysis=new RelaxationOscillatorAnalysisService().Analyze(result);
            }
            else
            {
                oscillatorAnalysis.Analysis=null;
                analysis.Analysis=new ComparatorTransferAnalysisService().Analyze(result);
            }
            var builder = new ComparatorDesignReportBuilder(); calculations.Text = builder.BuildText(result); report.DocumentText = builder.BuildHtml(result);
            try { spice.Text = new ComparatorSpiceExportService().Export(result); } catch (Exception exception) { spice.Text = "* SPICE export unavailable: " + exception.Message; }
        }
        finally { results.ResumeLayout(false); }
    }

    private void UpdateModeControls()
    {
        var schmitt = selectedKind == ComparatorDesignerKind.SchmittTrigger;
        var window=selectedKind==ComparatorDesignerKind.WindowComparator;
        var oscillator=selectedKind==ComparatorDesignerKind.RelaxationOscillator;
        lowerLabel.Visible=lower.Visible=upperLabel.Visible=upper.Visible=!oscillator;
        frequencyLabel.Visible=frequency.Visible=dutyCycleLabel.Visible=dutyCycle.Visible=oscillator;
        seriesLabel.Visible=series.Visible=minimumResistanceLabel.Visible=minimumResistance.Visible=
            maximumResistanceLabel.Visible=maximumResistance.Visible=optimizationLabel.Visible=optimize.Visible=!oscillator;
        topologyLabel.Visible = topology.Visible = schmitt; outputModeLabel.Visible = outputMode.Visible = window; advanced.Visible = window;
        lowerHysteresisLabel.Visible = lowerHysteresis.Visible = window && advanced.Checked; upperHysteresisLabel.Visible = upperHysteresis.Visible = window && advanced.Checked;
        series.Enabled = optimize.Checked;
    }
    private void BuildNavigation()
    {
        if (navigation is null) return; navigation.BeginUpdate(); navigation.Nodes.Clear(); navigationRoot = navigation.Nodes.Add("Comparators & Threshold Control");
        navigationRoot.Nodes.Add("Schmitt Trigger"); navigationRoot.Nodes.Add("Window Comparator");navigationRoot.Nodes.Add("Relaxation Oscillator"); navigationRoot.Expand(); navigation.EndUpdate();
    }
    private void NavigationAfterSelect(object? sender, TreeViewEventArgs e)
    {
        var node = e.Node;
        if (node is null || node.Parent != navigationRoot || node.Index is < 0 or > 2) return;
        selectedKind=node.Index switch{0=>ComparatorDesignerKind.SchmittTrigger,1=>ComparatorDesignerKind.WindowComparator,_=>ComparatorDesignerKind.RelaxationOscillator};
        UpdateModeControls(); status.Text = string.Empty;
        if (renderedResult is not null) resultIsStale = true;
    }
    private void SetAnalysisMode(bool oscillator)
    {
        analysis.Visible=!oscillator;oscillatorAnalysis.Visible=oscillator;
        if(oscillator)oscillatorAnalysis.BringToFront();else analysis.BringToFront();
    }
    private void HookParameters(Control root)
    {
        foreach (Control child in root.Controls)
        {
            switch (child)
            {
                case NumericUpDown number: number.ValueChanged += MarkStale; break;
                case ComboBox combo: combo.SelectedIndexChanged += MarkStale; break;
                case CheckBox check: check.CheckedChanged += MarkStale; break;
                case TextBox text: text.TextChanged += MarkStale; break;
            }
            HookParameters(child);
        }
    }

    private void MarkStale(object? sender, EventArgs e)
    {
        if (renderedResult is not null) resultIsStale = true;
    }

    protected override void Dispose(bool disposing) { if (disposing) DetachNavigation(); base.Dispose(disposing); }

    private static TabPage Page(string title, Control child) { var page = new TabPage(title) { BackColor = SystemColors.Window }; page.Controls.Add(child); return page; }
    private static RichTextBox TextView() => new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window, WordWrap = false };
    private static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(5, 7, 5, 0) };
    private static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals) => new()
    {
        Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals,
        Width = UiFieldMetrics.NumberWidth(min, max, decimals, 130, 220), ThousandsSeparator = true
    };
    private static ComboBox Choice<T>(IEnumerable<T> values)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        box.Items.AddRange(values.Cast<object>().ToArray()); box.SelectedIndex = 0;
        UiFieldMetrics.FitCombo(box, 220, 280);
        return box;
    }
    private static void Add(TableLayoutPanel table, int column, int row, Control label, Control input) { table.Controls.Add(label, column, row); table.Controls.Add(input, column + 1, row); }
}
