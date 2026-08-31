using System.Drawing;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Engines;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Analysis;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Reporting;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Schematic;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Services;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class PrecisionAnalogWizardControl : UserControl
{
    private readonly PrecisionAnalogDesignEngine engine = new();
    private AnalogComputeOperationType selectedOperation = AnalogComputeOperationType.GeneralLinearCombination;
    private readonly NumericUpDown inputCount = Number(1, 8, 1, 0);
    private readonly DataGridView inputs = new();
    private readonly NumericUpDown offset = Number(-1000, 1000, 0, 6);
    private readonly Label formula = new() { AutoSize = true, ForeColor = Color.Navy };
    private readonly Panel linearPanel = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly Panel mapperPanel = new() { Dock = DockStyle.Top, AutoSize = true, Visible = false };
    private readonly Label unsupportedParameters = new() { Dock = DockStyle.Top, AutoSize = true, Visible = false, ForeColor = Color.DarkGoldenrod };
    private readonly NumericUpDown vinLow = Number(-1000, 1000, .5m, 6);
    private readonly NumericUpDown vinHigh = Number(-1000, 1000, 4.5m, 6);
    private readonly NumericUpDown voutLow = Number(-1000, 1000, 0, 6);
    private readonly NumericUpDown voutHigh = Number(-1000, 1000, 3.3m, 6);
    private readonly Label mapperEquation = new() { AutoSize = true, ForeColor = Color.Navy };
    private readonly NumericUpDown vcc = Number(.001m, 1000, 15, 3);
    private readonly NumericUpDown vee = Number(-1000, -.001m, -15, 3);
    private readonly NumericUpDown errorPercent = Number(.001m, 100, 1, 3);
    private readonly ResistanceInput resistorMinimum = new(1_000);
    private readonly ResistanceInput resistorMaximum = new(1_000_000);
    private readonly ComboBox resistorSeries = new();
    private readonly CheckBox optimize = new() { Text = "Optimize Standard Values", AutoSize = true, Checked = true };
    private readonly CheckBox biasCompensation = new() { Text = "Bias Compensation", AutoSize = true, Checked = true };
    private readonly Button design = new() { Text = "Design Circuit", AutoSize = true, Padding = new Padding(12, 3, 12, 3) };
    private readonly Label status = new() { AutoSize = true, Margin = new Padding(12, 8, 0, 0) };
    private readonly TabControl results = new() { Dock = DockStyle.Fill };

    public PrecisionAnalogWizardControl(TreeView navigationTree)
    {
        Dock = DockStyle.Fill;
        BuildNavigation(navigationTree);
        BuildInterface();
        inputCount.ValueChanged += (_, _) => RebuildInputRows();
        inputs.CellValueChanged += (_, _) => UpdateFormula();
        offset.ValueChanged += (_, _) => UpdateFormula();
        foreach (var control in new Control[] { vinLow, vinHigh, voutLow, voutHigh }) control.TextChanged += (_, _) => UpdateMapperEquation();
        design.Click += DesignClicked;
        RebuildInputRows();
        OperationChanged();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6, 0, 0, 0) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var parameters = new GroupBox { Text = "Precision Analog Computing Parameters", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 18, 10, 8) };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        BuildLinearPanel(); BuildMapperPanel();
        content.Controls.Add(linearPanel); content.Controls.Add(mapperPanel); content.Controls.Add(unsupportedParameters);
        content.Controls.Add(BuildElectricalRow()); content.Controls.Add(BuildComponentRow());
        var action = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        action.Controls.Add(design); action.Controls.Add(status); content.Controls.Add(action);
        parameters.Controls.Add(content); root.Controls.Add(parameters, 0, 0);
        results.TabPages.Add(MessagePage("Schematic", "Design a verified circuit to display the schematic."));
        results.TabPages.Add(MessagePage("Analysis", "Operation-specific analysis will appear here."));
        results.TabPages.Add(MessagePage("Calculations", "Structured calculations will appear here."));
        results.TabPages.Add(MessagePage("Design Report", "A successful design report will appear here."));
        root.Controls.Add(results, 0, 1); Controls.Add(root);
    }

    private void BuildLinearPanel()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        row.Controls.Add(Label("Number of Inputs:", 7)); row.Controls.Add(inputCount); row.Controls.Add(Label("Output Offset b (V):", 7)); row.Controls.Add(offset);
        layout.Controls.Add(row);
        inputs.Height = 130; inputs.Width = 560; inputs.AllowUserToAddRows = false; inputs.AllowUserToDeleteRows = false; inputs.RowHeadersVisible = false;
        inputs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        inputs.Columns.Add("Name", "Input"); inputs.Columns.Add("Coefficient", "Multiplier");
        inputs.Columns[0].ReadOnly = true; layout.Controls.Add(inputs); layout.Controls.Add(formula); linearPanel.Controls.Add(layout);
    }

    private void BuildMapperPanel()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        AddField(row, "Vin Low (V):", vinLow); AddField(row, "Vin High (V):", vinHigh); AddField(row, "Vout Low (V):", voutLow); AddField(row, "Vout High (V):", voutHigh);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(row); layout.Controls.Add(mapperEquation); mapperPanel.Controls.Add(layout);
    }

    private Control BuildElectricalRow()
    {
        var group = new GroupBox { Text = "Electrical / Practical Constraints", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8, 15, 8, 5) };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        AddField(row, "VCC (V):", vcc); AddField(row, "VEE (V):", vee); AddField(row, "Allowed Function Error (%):", errorPercent);
        row.Controls.Add(new Label { Text = "GND = 0 V; references are synthesized from VCC/GND/VEE (no Vref input).", AutoSize = true, Margin = new Padding(14, 7, 0, 0), ForeColor = Color.DimGray });
        group.Controls.Add(row); return group;
    }

    private Control BuildComponentRow()
    {
        var group = new GroupBox { Text = "Component Constraints", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8, 15, 8, 5) };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        resistorSeries.DropDownStyle = ComboBoxStyle.DropDownList; resistorSeries.DataSource = Enum.GetValues<AnalogStandardSeries>(); resistorSeries.Width = 110; resistorSeries.SelectedItem = AnalogStandardSeries.E96;
        row.Controls.Add(Label("R Series:", 7)); row.Controls.Add(resistorSeries);
        AddField(row, "R Min:", resistorMinimum); AddField(row, "R Max:", resistorMaximum);
        optimize.Margin = new Padding(12, 6, 0, 0); biasCompensation.Margin = new Padding(12, 6, 0, 0); row.Controls.Add(optimize); row.Controls.Add(biasCompensation);
        group.Controls.Add(row); return group;
    }

    private async void DesignClicked(object? sender, EventArgs e)
    {
        AnalogComputeDesignRequest request;
        try { request = BuildRequest(); }
        catch (Exception exception) { status.Text = exception.Message; status.ForeColor = Color.Firebrick; return; }
        design.Enabled = false; status.Text = "Calculating and independently verifying selected values…"; status.ForeColor = SystemColors.ControlText;
        try
        {
            var result = await Task.Run(() => engine.Design(request));
            status.Text = result.Success ? "Verified design complete." : $"{result.Capability.ReasonCode}: {result.Capability.Message}";
            status.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
            ShowResult(result);
        }
        finally { UpdateDesignAvailability(); }
    }

    private AnalogComputeDesignRequest BuildRequest()
    {
        var terms = new List<AnalogInputTerm>();
        foreach (DataGridViewRow row in inputs.Rows)
            terms.Add(new(Convert.ToString(row.Cells[0].Value)!, Parse(row, 1)));
        return new()
        {
            Operation = selectedOperation, Inputs = terms, OutputOffsetVoltage = (double)offset.Value,
            InputLowVoltage = (double)vinLow.Value, InputHighVoltage = (double)vinHigh.Value,
            OutputLowVoltage = (double)voutLow.Value, OutputHighVoltage = (double)voutHigh.Value,
            SupplyVccVoltage = (double)vcc.Value, SupplyVeeVoltage = (double)vee.Value,
            MaximumFunctionErrorPercent = (double)errorPercent.Value,
            UseInputRangesForFeasibility = false,
            Components = new()
            {
                ResistorSeries = (AnalogStandardSeries)resistorSeries.SelectedItem!, OptimizeStandardValues = optimize.Checked,
                BiasCompensation = biasCompensation.Checked, ResistorMinimumOhm = resistorMinimum.ValueOhm,
                ResistorMaximumOhm = resistorMaximum.ValueOhm
            }
        };
    }

    private void ShowResult(AnalogComputeDesignResult result)
    {
        results.TabPages.Clear();
        if (!result.Success || result.CircuitGraph is null)
        {
            results.TabPages.Add(MessagePage("Design Status", $"{result.Capability.Level}\r\n{result.Capability.ReasonCode}\r\n{result.Capability.Message}\r\n{result.Capability.SuggestedAlternative}"));
            return;
        }
        var schematic = new PrecisionAnalogSchematicLayoutService().Layout(result.CircuitGraph);
        var schematicPage = new TabPage("Schematic"); schematicPage.Controls.Add(new FilterSchematicControl { Dock = DockStyle.Fill, Document = schematic, AvoidWireLabelCollisions = false }); results.TabPages.Add(schematicPage);
        results.TabPages.Add(CreateInteractiveAnalysisPage(result));
        var calculations = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window };
        calculations.Text = string.Join("\r\n", result.Calculations.Select(record => $"{record.Title}\r\n  {record.Expression}\r\n  => {record.Result}{(record.Note is null ? "" : "\r\n  " + record.Note)}")) + "\r\n\r\nACTUAL VERIFICATION\r\n" + string.Join("\r\n", result.VerificationMetrics.Select(metric => $"{metric.Name}: target {metric.Target:G9}, actual {metric.Actual:G9}, error {metric.Error:G6}, {(metric.Pass ? "PASS" : "FAIL")}"));
        var calculationsPage = new TabPage("Calculations"); calculationsPage.Controls.Add(calculations); results.TabPages.Add(calculationsPage);
        results.TabPages.Add(CreateSpicePage(result));
        results.TabPages.Add(CreateReportPage(result));
    }

    private static TabPage CreateInteractiveAnalysisPage(AnalogComputeDesignResult result)
    {
        var page = new TabPage("Analysis") { BackColor = SystemColors.Window };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 5) };
        var rangeMapper = result.Request.Operation == AnalogComputeOperationType.VoltageRangeMapper;
        if (rangeMapper)
        {
            var inputStart = Math.Min(result.Request.InputLowVoltage, result.Request.InputHighVoltage);
            var inputStop = Math.Max(result.Request.InputLowVoltage, result.Request.InputHighVoltage);
            toolbar.Controls.Add(new Label
            {
                Text = $"Input range: {inputStart:G8} V to {inputStop:G8} V    Output range: {result.Request.OutputLowVoltage:G8} V to {result.Request.OutputHighVoltage:G8} V",
                AutoSize = true,
                Margin = new Padding(3, 7, 0, 0)
            });
            root.Controls.Add(toolbar, 0, 0);
            var rangePlot = new AffineTransferPlotControl
            {
                Dock = DockStyle.Fill,
                SweepInput = "VIN",
                LowerLimit = result.Request.SupplyVeeVoltage + result.Request.OpAmp.OutputSwingHeadroomVoltage,
                UpperLimit = result.Request.SupplyVccVoltage - result.Request.OpAmp.OutputSwingHeadroomVoltage,
                PreferredMinimumY = Math.Min(result.Request.OutputLowVoltage, result.Request.OutputHighVoltage),
                PreferredMaximumY = Math.Max(result.Request.OutputLowVoltage, result.Request.OutputHighVoltage),
                Analysis = new AffineSweepAnalysisService().Analyze(result, "VIN", new Dictionary<string, double>(), inputStart, inputStop)
            };
            root.Controls.Add(rangePlot, 0, 1);
            page.Controls.Add(root);
            return page;
        }
        var sweep = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
        sweep.Items.AddRange(result.Request.Inputs.Select(input => (object)input.Name).ToArray()); sweep.SelectedIndex = 0;
        var start = Number(-1_000_000, 1_000_000, -1, 4); var stop = Number(-1_000_000, 1_000_000, 1, 4);
        var update = new Button { Text = "Update Plot", AutoSize = true, MinimumSize = new Size(95, 27) };
        toolbar.Controls.Add(Label("Sweep Input:", 7)); toolbar.Controls.Add(sweep); toolbar.Controls.Add(Label("From (V):", 7)); toolbar.Controls.Add(start); toolbar.Controls.Add(Label("To (V):", 7)); toolbar.Controls.Add(stop); toolbar.Controls.Add(update);
        root.Controls.Add(toolbar, 0, 0);
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 6 };
        split.SizeChanged += (_, _) =>
        {
            var available = split.ClientSize.Width - split.SplitterWidth;
            if (available > 480) split.SplitterDistance = Math.Clamp(250, 190, available - 290);
        };
        var fixedGroup = new GroupBox { Text = "Fixed Inputs", Dock = DockStyle.Fill, Padding = new Padding(7, 18, 7, 7) };
        var fixedGrid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        fixedGrid.Columns.Add("Input", "Input"); fixedGrid.Columns.Add("Voltage", "Voltage (V)"); fixedGrid.Columns[0].ReadOnly = true;
        foreach (var input in result.Request.Inputs) fixedGrid.Rows.Add(input.Name, 0d);
        fixedGroup.Controls.Add(fixedGrid); split.Panel1.Controls.Add(fixedGroup);
        var plot = new AffineTransferPlotControl
        {
            Dock = DockStyle.Fill,
            LowerLimit = result.Request.SupplyVeeVoltage + result.Request.OpAmp.OutputSwingHeadroomVoltage,
            UpperLimit = result.Request.SupplyVccVoltage - result.Request.OpAmp.OutputSwingHeadroomVoltage
        };
        split.Panel2.Controls.Add(plot); root.Controls.Add(split, 0, 1);

        void RefreshRows()
        {
            foreach (DataGridViewRow row in fixedGrid.Rows)
            {
                var swept = string.Equals(Convert.ToString(row.Cells[0].Value), Convert.ToString(sweep.SelectedItem), StringComparison.OrdinalIgnoreCase);
                row.Cells[1].ReadOnly = swept;
                row.DefaultCellStyle.BackColor = swept ? SystemColors.Control : SystemColors.Window;
                row.Cells[1].ToolTipText = swept ? "This input is swept on the horizontal axis." : "This input is held at the entered voltage.";
            }
        }
        void Analyze()
        {
            try
            {
                var fixedValues = fixedGrid.Rows.Cast<DataGridViewRow>().ToDictionary(row => Convert.ToString(row.Cells[0].Value)!, row => double.TryParse(Convert.ToString(row.Cells[1].Value), out var value) ? value : throw new FormatException($"{row.Cells[0].Value} fixed voltage is invalid."), StringComparer.OrdinalIgnoreCase);
                var selected = Convert.ToString(sweep.SelectedItem)!;
                var analysis = new AffineSweepAnalysisService().Analyze(result, selected, fixedValues, (double)start.Value, (double)stop.Value);
                plot.SweepInput = selected; plot.Analysis = analysis;
            }
            catch (Exception exception) { MessageBox.Show(page, exception.Message, "Analysis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        sweep.SelectedIndexChanged += (_, _) => { RefreshRows(); Analyze(); };
        update.Click += (_, _) => Analyze();
        fixedGrid.CellEndEdit += (_, _) => Analyze();
        RefreshRows(); Analyze(); page.Controls.Add(root); return page;
    }

    private static TabPage CreateSpicePage(AnalogComputeDesignResult result)
    {
        var page = new TabPage("SPICE Netlist") { BackColor = SystemColors.Window };
        var service = new PrecisionAnalogSpiceExportService(); var text = service.Export(result);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(5, 4, 5, 4) };
        var save = new Button { Text = "Save CIR...", AutoSize = true, MinimumSize = new Size(95, 27) };
        var copy = new Button { Text = "Copy Netlist", AutoSize = true, MinimumSize = new Size(105, 27) };
        toolbar.Controls.Add(save); toolbar.Controls.Add(copy); toolbar.Controls.Add(new Label { Text = "Multisim: File > Open > select the exported .cir file.", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(12, 7, 0, 0) });
        var preview = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window, Text = text };
        save.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "SPICE Circuit (*.cir)|*.cir|All files (*.*)|*.*", DefaultExt = "cir", AddExtension = true, FileName = $"precision-{result.Request.Operation}.cir" };
            if (dialog.ShowDialog(page.FindForm()) != DialogResult.OK) return;
            try { service.Save(result, dialog.FileName); }
            catch (Exception exception) { MessageBox.Show(page, exception.Message, "CIR Export", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        copy.Click += (_, _) => { if (preview.TextLength > 0) Clipboard.SetText(preview.Text); };
        layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(preview, 0, 1); page.Controls.Add(layout); return page;
    }

    private static TabPage CreateReportPage(AnalogComputeDesignResult result)
    {
        var page = new TabPage("Design Report") { BackColor = SystemColors.Window };
        var service = new PrecisionAnalogDesignReportService();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var html = new Button { Text = "Export HTML Report...", AutoSize = true, MinimumSize = new Size(145, 28) };
        var json = new Button { Text = "Export JSON Snapshot...", AutoSize = true, MinimumSize = new Size(155, 28) };
        buttons.Controls.Add(html); buttons.Controls.Add(json);
        var preview = new WebBrowser
        {
            Dock = DockStyle.Fill, AllowWebBrowserDrop = false, IsWebBrowserContextMenuEnabled = true,
            ScriptErrorsSuppressed = true, WebBrowserShortcutsEnabled = true, DocumentText = service.ExportHtml(result)
        };
        html.Click += (_, _) => ExportReport(page, result, service, false);
        json.Click += (_, _) => ExportReport(page, result, service, true);
        layout.Controls.Add(buttons, 0, 0); layout.Controls.Add(preview, 0, 1); page.Controls.Add(layout); return page;
    }

    private static void ExportReport(Control owner, AnalogComputeDesignResult result, PrecisionAnalogDesignReportService service, bool json)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = json ? "Precision Analog Snapshot (*.json)|*.json" : "Precision Analog Report (*.html)|*.html",
            DefaultExt = json ? "json" : "html", AddExtension = true,
            FileName = $"precision-{result.Request.Operation}.{(json ? "json" : "html")}" 
        };
        if (dialog.ShowDialog(owner.FindForm()) != DialogResult.OK) return;
        try
        {
            if (json) service.SaveJson(result, dialog.FileName); else service.SaveHtml(result, dialog.FileName);
            MessageBox.Show(owner, $"Report exported successfully.\r\n{dialog.FileName}", "Design Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { MessageBox.Show(owner, exception.Message, "Design Report", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OperationChanged()
    {
        linearPanel.Visible = selectedOperation == AnalogComputeOperationType.GeneralLinearCombination;
        mapperPanel.Visible = selectedOperation == AnalogComputeOperationType.VoltageRangeMapper;
        unsupportedParameters.Visible = !linearPanel.Visible && !mapperPanel.Visible;
        unsupportedParameters.Text = unsupportedParameters.Visible ? "This operation is part of the v1 sequence but remains disabled until its topology-specific independent verifier passes." : "";
        UpdateMapperEquation(); UpdateDesignAvailability();
    }

    private void UpdateDesignAvailability()
    {
        design.Enabled = selectedOperation is AnalogComputeOperationType.GeneralLinearCombination or AnalogComputeOperationType.VoltageRangeMapper;
    }

    private void RebuildInputRows()
    {
        var old = inputs.Rows.Cast<DataGridViewRow>().Select(row => row.Cells.Cast<DataGridViewCell>().Select(cell => cell.Value ?? "").ToArray()).ToArray();
        inputs.Rows.Clear();
        for (var index = 0; index < (int)inputCount.Value; index++)
        {
            var multiplier = index < old.Length && old[index].Length > 1 ? old[index][1] : index == 0 ? 1d : 0d;
            inputs.Rows.Add($"U{index + 1}", multiplier);
        }
        UpdateFormula();
    }

    private void UpdateFormula()
    {
        var terms = inputs.Rows.Cast<DataGridViewRow>().Select(row => $"({row.Cells[1].Value ?? 0})*{row.Cells[0].Value}");
        formula.Text = $"Uout = {offset.Value:G} V + " + string.Join(" + ", terms);
    }

    private void UpdateMapperEquation()
    {
        var denominator = vinHigh.Value - vinLow.Value;
        if (denominator == 0) { mapperEquation.Text = "Input endpoints must be different."; return; }
        var scale = (voutHigh.Value - voutLow.Value) / denominator;
        var calculatedOffset = voutLow.Value - scale * vinLow.Value;
        mapperEquation.Text = $"Calculated Scale a = {scale:G8}; Calculated Offset b = {calculatedOffset:G8} V";
    }

    private void BuildNavigation(TreeView tree)
    {
        tree.BeginUpdate(); tree.Nodes.Clear();
        var root = tree.Nodes.Add("Precision Analog Computing");
        foreach (var item in Enum.GetValues<AnalogComputeOperationType>()) root.Nodes.Add(PrecisionAnalogDesignEngine.DisplayName(item));
        root.Expand(); tree.EndUpdate();
        tree.AfterSelect += (_, args) =>
        {
            if (args.Node?.Parent == root && args.Node.Index >= 0 && args.Node.Index < Enum.GetValues<AnalogComputeOperationType>().Length)
            {
                selectedOperation = Enum.GetValues<AnalogComputeOperationType>()[args.Node.Index];
                OperationChanged();
            }
        };
    }

    private static double Parse(DataGridViewRow row, int column) => double.TryParse(Convert.ToString(row.Cells[column].Value), out var value) ? value : throw new FormatException($"Row {row.Index + 1}, column {row.Cells[column].OwningColumn?.HeaderText ?? column.ToString()} is not a valid number.");
    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, int decimals) => new() { Minimum = minimum, Maximum = maximum, Value = value, DecimalPlaces = decimals, Width = 125, ThousandsSeparator = true };
    private static Label Label(string text, int top) => new() { Text = text, AutoSize = true, Margin = new Padding(5, top, 4, 0) };
    private static void AddField(Control row, string title, Control field) { row.Controls.Add(Label(title, 7)); row.Controls.Add(field); }
    private static TabPage MessagePage(string title, string message) { var page = new TabPage(title); page.Controls.Add(new Label { Dock = DockStyle.Fill, Text = message, TextAlign = ContentAlignment.MiddleCenter }); return page; }

    private sealed class ResistanceInput : UserControl
    {
        private readonly NumericUpDown number = new() { DecimalPlaces = 6, Minimum = .000001m, Maximum = 1_000_000_000m, Width = 135, ThousandsSeparator = true };
        private readonly ComboBox unit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 82 };
        public double ValueOhm => (double)number.Value * ((ResistanceUnit)unit.SelectedItem!).Factor;

        public ResistanceInput(double valueOhm)
        {
            AutoSize = true; Margin = Padding.Empty;
            ResistanceUnit[] units = [new("\u03A9", 1), new("k\u03A9", 1e3), new("M\u03A9", 1e6)];
            unit.Items.AddRange(units.Cast<object>().ToArray());
            var selected = valueOhm >= 1e6 ? units[2] : valueOhm >= 1e3 ? units[1] : units[0];
            unit.SelectedItem = selected; number.Value = (decimal)(valueOhm / selected.Factor);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            row.Controls.Add(number); row.Controls.Add(unit); Controls.Add(row);
        }

        private sealed record ResistanceUnit(string Text, double Factor) { public override string ToString() => Text; }
    }
}
