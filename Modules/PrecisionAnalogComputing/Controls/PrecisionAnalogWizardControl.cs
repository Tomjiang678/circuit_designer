using System.Diagnostics;
using System.Drawing;
using CircuitModuleDesigner.Controls;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Engines;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Analysis;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Reporting;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Schematic;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Services;
using CircuitModuleDesigner.Services;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class PrecisionAnalogWizardControl : UserControl, IDesignModuleHost
{
    private readonly PrecisionAnalogDesignEngine engine = new();
    private TreeView? navigationTree;
    private TreeNode? navigationRoot;
    private AnalogComputeOperationType selectedOperation = AnalogComputeOperationType.GeneralLinearCombination;
    private readonly NumericUpDown inputCount = Number(1, 8, 1, 0);
    private readonly DataGridView inputs = new();
    private readonly NumericUpDown offset = Number(-1000, 1000, 0, 6);
    private readonly Label formula = new() { AutoSize = true, ForeColor = Color.Navy };
    private readonly Panel linearPanel = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly Panel mapperPanel = new() { Dock = DockStyle.Top, AutoSize = true, Visible = false };
    private readonly Panel operationPanel = new() { Dock = DockStyle.Top, AutoSize = false, Height = 162, Visible = false };
    private readonly GroupBox operationGroup = new() { AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(8, 15, 8, 5) };
    private readonly FlowLayoutPanel operationFields = new() { Dock = DockStyle.Fill, AutoSize = false, WrapContents = true, AutoScroll = true };
    private readonly Label operationFormula = new() { Dock = DockStyle.Bottom, Height = 34, ForeColor = Color.Navy, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0) };
    private AnalogComputeOperationType? displayedOperation;
    private readonly Dictionary<AnalogComputeOperationType, OperationUiState> operationStates = [];
    private readonly Dictionary<Control, OperationFieldUi> operationFieldUi = [];
    private readonly NumericUpDown vinLow = Number(-1000, 1000, .5m, 6);
    private readonly NumericUpDown vinHigh = Number(-1000, 1000, 4.5m, 6);
    private readonly NumericUpDown voutLow = Number(-1000, 1000, 0, 6);
    private readonly NumericUpDown voutHigh = Number(-1000, 1000, 3.3m, 6);
    private readonly NumericUpDown gainK = Number(.000001m, 1000, 1, 6);
    private readonly ComboBox halfPolarity = Choice(Enum.GetValues<HalfWavePolarity>());
    private readonly ComboBox outputPolarity = Choice(Enum.GetValues<RectifierOutputPolarity>());
    private readonly NumericUpDown inputMinimum = Number(-1000, 1000, -5, 6);
    private readonly NumericUpDown inputMaximum = Number(-1000, 1000, 5, 6);
    private readonly NumericUpDown maximumFrequency = Number(.001m, 100_000_000, 1000, 3);
    private readonly NumericUpDown referenceVoltage = Number(.000001m, 1000, 1, 6);
    private readonly NumericUpDown logScale = Number(.000001m, 1000, .1m, 6);
    private readonly NumericUpDown ki = Number(.000001m, 100_000_000, 1000, 6);
    private readonly NumericUpDown kd = Number(.000000001m, 1000, .001m, 9);
    private readonly NumericUpDown lowerCorner = Number(.001m, 100_000_000, 10, 3);
    private readonly NumericUpDown upperCorner = Number(.001m, 100_000_000, 10000, 3);
    private readonly NumericUpDown vcc = Number(.001m, 1000, 15, 3);
    private readonly NumericUpDown vee = Number(-1000, -.001m, -15, 3);
    private readonly NumericUpDown errorPercent = Number(.001m, 100, 1, 3);
    private readonly ResistanceInput resistorMinimum = new(1_000);
    private readonly ResistanceInput resistorMaximum = new(1_000_000);
    private readonly CapacitanceInput capacitorMinimum = new(10e-12);
    private readonly CapacitanceInput capacitorMaximum = new(10e-6);
    private readonly ComboBox resistorSeries = new();
    private readonly ComboBox capacitorSeries = new();
    private readonly CheckBox optimize = new() { Text = "Optimize Standard Values", AutoSize = true, Checked = true };
    private readonly CheckBox biasCompensation = new() { Text = "Bias Compensation", AutoSize = true, Checked = true };
    private readonly CheckBox idealOpAmp = new() { Text = "Ideal OpAmp", AutoSize = true, Checked = true };
    private readonly NumericUpDown gbw = Number(1, 1_000_000_000, 1_000_000, 0);
    private readonly NumericUpDown slewRate = Number(1, 1_000_000_000_000, 1_000_000, 0);
    private readonly Button design = new() { Text = "Design Circuit", AutoSize = true, Padding = new Padding(12, 3, 12, 3) };
    private readonly Label status = new() { AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(12, 2, 0, 0) };
    private readonly Button advancedToggle = new() { Text = "Advanced ▸", AutoSize = true, FlatStyle = FlatStyle.System };
    private readonly TableLayoutPanel advancedPanel = new() { Dock = DockStyle.Top, AutoSize = true, AutoScroll = true, MaximumSize = new Size(0, 330), ColumnCount = 1, Visible = false, Margin = Padding.Empty };
    private readonly TabControl results = new() { Dock = DockStyle.Fill };
    private readonly TabPage schematicResultPage = new("Schematic") { BackColor = SystemColors.Window };
    private readonly TabPage analysisResultPage = new("Analysis") { BackColor = SystemColors.Window };
    private readonly TabPage calculationsResultPage = new("Calculations") { BackColor = SystemColors.Window };
    private readonly TabPage spiceResultPage = new("SPICE Netlist") { BackColor = SystemColors.Window };
    private readonly TabPage reportResultPage = new("Design Report") { BackColor = SystemColors.Window };
    private readonly FilterSchematicControl schematicView = new() { Dock = DockStyle.Fill, AvoidWireLabelCollisions = false };
    private readonly Panel analysisHost = new() { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private readonly RichTextBox calculationsView = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window };
    private readonly RichTextBox spiceView = new() { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window };
    private readonly Panel reportPreviewHost = new() { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private AnalogComputeDesignResult? renderedResult;
    private WebBrowser? reportBrowser;
    private bool reportNeedsRefresh;
    private bool updatingUi;
    private bool designing;
    private bool resultIsStale;

    public PrecisionAnalogWizardControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Dock = DockStyle.Fill;
        BuildInterface();
        inputCount.ValueChanged += (_, _) => { if (!updatingUi) RebuildInputRows(); };
        inputs.CellValueChanged += (_, _) => { if (!updatingUi) UpdateFormula(); };
        offset.ValueChanged += (_, _) => { if (!updatingUi) UpdateFormula(); };
        logScale.ValueChanged += (_, _) => { if (!updatingUi) UpdateOperationFormula(); };
        referenceVoltage.ValueChanged += (_, _) => { if (!updatingUi) UpdateOperationFormula(); };
        idealOpAmp.CheckedChanged += (_, _) =>
        {
            gbw.Enabled = slewRate.Enabled = !idealOpAmp.Checked;
            if (selectedOperation is AnalogComputeOperationType.PrecisionHalfWaveRectifier or AnalogComputeOperationType.PrecisionFullWaveRectifier)
                BuildOperationPanel();
        };
        gbw.Enabled = slewRate.Enabled = false;
        design.Click += DesignClicked;
        advancedToggle.Click += (_, _) =>
        {
            advancedPanel.Visible = !advancedPanel.Visible;
            advancedToggle.Text = advancedPanel.Visible ? "Advanced ▾" : "Advanced ▸";
        };
        RebuildInputRows();
        OperationChanged();
        HookParameters(this);
    }

    public PrecisionAnalogWizardControl(TreeView navigationTree) : this() => AttachNavigation(navigationTree);

    public void AttachNavigation(TreeView tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (ReferenceEquals(navigationTree, tree))
        {
            BuildNavigation(tree);
            return;
        }
        DetachNavigation();
        navigationTree = tree;
        navigationTree.AfterSelect += NavigationAfterSelect;
        BuildNavigation(tree);
    }

    public void DetachNavigation()
    {
        if (navigationTree is not null)
            navigationTree.AfterSelect -= NavigationAfterSelect;
        navigationTree = null;
        navigationRoot = null;
    }

    public string ModuleTitle => "Precision Analog Computing";
    public bool HasDesign => renderedResult is { Success: true };
    public bool IsStale => resultIsStale;

    public string BuildDesignJson() =>
        !resultIsStale && renderedResult is { Success: true } result
            ? new PrecisionAnalogDesignReportService().ExportJson(result)
            : string.Empty;

    public string BuildDesignSummary() =>
        !resultIsStale && renderedResult is { Success: true } result
            ? new PrecisionAnalogDesignReportBuilder().BuildText(result)
            : string.Empty;

    public string? BuildNetlist()
    {
        if (resultIsStale || renderedResult is not { Success: true } result) return null;
        try
        {
            return new PrecisionAnalogSpiceExportService().Export(result);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Calculate() => DesignClicked(null, EventArgs.Empty);

    private void BuildInterface()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6, 0, 0, 0) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var parameters = new GroupBox { Text = "Precision Analog Computing Parameters", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 18, 10, 8) };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        BuildLinearPanel(); BuildMapperPanel();
        InitializeOperationFields(); operationGroup.Controls.Add(operationFields); operationGroup.Controls.Add(operationFormula); operationPanel.Controls.Add(operationGroup);
        content.Controls.Add(linearPanel); content.Controls.Add(mapperPanel); content.Controls.Add(operationPanel);
        advancedPanel.Controls.Add(BuildElectricalRow(),0,0);advancedPanel.Controls.Add(BuildComponentRow(),0,1);
        advancedPanel.RowStyles.Clear();
        advancedPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        advancedPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var action = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
        action.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        action.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        action.Controls.Add(design, 0, 0); action.Controls.Add(status, 1, 0);
        content.Controls.Add(action); content.Controls.Add(advancedToggle); content.Controls.Add(advancedPanel);
        parameters.Controls.Add(content); root.Controls.Add(parameters, 0, 0);
        InitializeResultPages();
        root.Controls.Add(results, 0, 1); Controls.Add(root);
    }

    private void InitializeResultPages()
    {
        schematicResultPage.Controls.Add(schematicView);
        analysisResultPage.Controls.Add(analysisHost);
        calculationsResultPage.Controls.Add(calculationsView);

        var spiceLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        spiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); spiceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var spiceButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(5, 4, 5, 4) };
        var saveCir = new Button { Text = "Save CIR...", AutoSize = true, MinimumSize = new Size(95, 27) }; var copyCir = new Button { Text = "Copy Netlist", AutoSize = true, MinimumSize = new Size(105, 27) };
        saveCir.Click += (_, _) => ExportCurrentCir(); copyCir.Click += (_, _) => { if (spiceView.TextLength > 0) Clipboard.SetText(spiceView.Text); };
        spiceButtons.Controls.Add(saveCir); spiceButtons.Controls.Add(copyCir); spiceLayout.Controls.Add(spiceButtons, 0, 0); spiceLayout.Controls.Add(spiceView, 0, 1); spiceResultPage.Controls.Add(spiceLayout);

        var reportLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = new Padding(8) };
        reportLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); reportLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var reportButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var html = new Button { Text = "Export HTML Report...", AutoSize = true, MinimumSize = new Size(145, 28) }; var json = new Button { Text = "Export JSON Snapshot...", AutoSize = true, MinimumSize = new Size(155, 28) };
        html.Click += (_, _) => ExportCurrentReport(false); json.Click += (_, _) => ExportCurrentReport(true); reportButtons.Controls.Add(html); reportButtons.Controls.Add(json);
        reportLayout.Controls.Add(reportButtons, 0, 0); reportLayout.Controls.Add(reportPreviewHost, 0, 1); reportResultPage.Controls.Add(reportLayout);

        results.TabPages.Add(schematicResultPage); results.TabPages.Add(analysisResultPage); results.TabPages.Add(calculationsResultPage); results.TabPages.Add(spiceResultPage); results.TabPages.Add(reportResultPage);
        results.SelectedIndexChanged += (_, _) => EnsureReportPreview();
        SetHostMessage(analysisHost, "Design a verified circuit to display its analysis.");
        calculationsView.Text = "Design a verified circuit to display its calculations."; spiceView.Text = "* No netlist generated.";
        SetHostMessage(reportPreviewHost, "Design a verified circuit to display its report.");
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
        mapperPanel.Controls.Add(row);
    }

    private void BuildOperationPanel()
    {
        if (!operationPanel.Visible) return;
        operationPanel.SuspendLayout();
        operationFields.SuspendLayout();
        foreach (var field in operationFieldUi.Values) field.Container.Visible = false;
        operationGroup.Text = PrecisionAnalogDesignEngine.DisplayName(selectedOperation) + " Specification";
        switch (selectedOperation)
        {
            case AnalogComputeOperationType.PrecisionHalfWaveRectifier:
                ShowOperationField(halfPolarity, "Polarity:");
                ShowOperationField(gainK, "Gain K:");
                ShowInputRange();
                if(!idealOpAmp.Checked)ShowOperationField(maximumFrequency,"Max f (Hz):");
                break;
            case AnalogComputeOperationType.PrecisionFullWaveRectifier:
                ShowOperationField(outputPolarity, "Output:");
                ShowOperationField(gainK, "Gain K:");
                ShowInputRange();
                if(!idealOpAmp.Checked)ShowOperationField(maximumFrequency,"Max f (Hz):");
                break;
            case AnalogComputeOperationType.LogarithmicAmplifier:
                if (inputMinimum.Value <= 0) inputMinimum.Value = .01m;
                ShowInputRange();
                ShowOperationField(referenceVoltage, "Vref (V):");
                ShowOperationField(logScale, "Log Scale S (V):");
                break;
            case AnalogComputeOperationType.ExponentialAmplifier:
                ShowInputRange();
                ShowOperationField(referenceVoltage, "Vref (V):");
                ShowOperationField(logScale, "Exponent Scale S (V):");
                break;
            case AnalogComputeOperationType.PracticalIntegrator:
                ShowOperationField(ki, "Ki (1/s):");
                ShowOperationField(lowerCorner, "Leak fL (Hz):");
                break;
            case AnalogComputeOperationType.PracticalDifferentiator:
                ShowOperationField(kd, "Kd (s):");
                ShowOperationField(upperCorner, "Useful fMax (Hz):");
                break;
        }
        UpdateOperationFormula();
        operationFields.ResumeLayout(false);
        operationFields.PerformLayout();
        operationPanel.ResumeLayout(false);
        operationPanel.PerformLayout();
    }

    private void UpdateOperationFormula()
    {
        operationFormula.Text = selectedOperation switch
        {
            AnalogComputeOperationType.LogarithmicAmplifier =>
                $"Vout = -S × ln(Vin / Vref),   S = {logScale.Value:G} V per e-fold,   Vref = {referenceVoltage.Value:G} V",
            AnalogComputeOperationType.ExponentialAmplifier =>
                $"Vout = Vref × exp(Vin / S),   S = {logScale.Value:G} V per e-fold,   Vref = {referenceVoltage.Value:G} V",
            _ => string.Empty
        };
        operationFormula.Visible = !string.IsNullOrEmpty(operationFormula.Text);
    }

    private void InitializeOperationFields()
    {
        foreach (var item in new (Control Control, string Label)[]
        {
            (halfPolarity,"Polarity:"),(outputPolarity,"Output:"),(gainK,"Gain K:"),(inputMinimum,"Vin Min (V):"),(inputMaximum,"Vin Max (V):"),
            (maximumFrequency,"Max f (Hz):"),(referenceVoltage,"Vref (V):"),(logScale,"S:"),(ki,"Ki (1/s):"),(kd,"Kd (s):"),(lowerCorner,"Lower Corner (Hz):"),
            (upperCorner,"Upper Corner (Hz):")
        })
        {
            var label = Label(item.Label, 7);
            var pair = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = Padding.Empty
            };
            pair.Controls.Add(label); pair.Controls.Add(item.Control);
            operationFieldUi.Add(item.Control, new(label, pair));
            operationFields.Controls.Add(pair);
        }
    }

    private void ShowOperationField(Control control, string title)
    {
        var field = operationFieldUi[control]; field.Label.Text = title; field.Container.Visible = true;
    }

    private void ShowInputRange()
    {
        ShowOperationField(inputMinimum, "Vin Min (V):");
        ShowOperationField(inputMaximum, "Vin Max (V):");
    }

    private Control BuildElectricalRow()
    {
        var group = new GroupBox { Text = "Electrical / Practical Constraints", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(8, 15, 8, 5) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 8, RowCount = 3, Margin = Padding.Empty };
        for (var column = 0; column < 8; column++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var rowIndex = 0; rowIndex < 3; rowIndex++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        void AddPair(int column, int rowIndex, string title, Control field)
        {
            grid.Controls.Add(Label(title, 7), column, rowIndex);
            grid.Controls.Add(field, column + 1, rowIndex);
        }
        AddPair(0, 0, "VCC (V):", vcc);
        AddPair(2, 0, "VEE (V):", vee);
        AddPair(4, 0, "Allowed Error (%):", errorPercent);
        idealOpAmp.Margin = new Padding(12, 6, 0, 0);
        grid.Controls.Add(idealOpAmp, 6, 0);
        grid.SetColumnSpan(idealOpAmp, 2);
        AddPair(0, 1, "GBW (Hz):", gbw);
        AddPair(2, 1, "Slew (V/s):", slewRate);
        var note = new Label
        {
            Text = "GND = 0 V; requested reference voltages are synthesized from the supply rails.",
            AutoSize = true,
            MaximumSize = new Size(1050, 0),
            Margin = new Padding(14, 7, 0, 0),
            ForeColor = Color.DimGray
        };
        grid.Controls.Add(note, 0, 2);
        grid.SetColumnSpan(note, 8);
        group.Controls.Add(grid);
        return group;
    }

    private Control BuildComponentRow()
    {
        var group = new GroupBox { Text = "Component Constraints", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(8, 15, 8, 5) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 6, RowCount = 3, Margin = Padding.Empty };
        for (var column = 0; column < 6; column++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var rowIndex = 0; rowIndex < 3; rowIndex++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        void AddPair(int column, int rowIndex, string title, Control field)
        {
            grid.Controls.Add(Label(title, 7), column, rowIndex);
            grid.Controls.Add(field, column + 1, rowIndex);
        }
        // These fixed choices need no CurrencyManager: binding before parenting resets selection.
        resistorSeries.DropDownStyle = ComboBoxStyle.DropDownList; resistorSeries.Items.AddRange(Enum.GetValues<AnalogStandardSeries>().Cast<object>().ToArray()); resistorSeries.Width = 110; resistorSeries.SelectedItem = AnalogStandardSeries.E96;
        capacitorSeries.DropDownStyle = ComboBoxStyle.DropDownList; capacitorSeries.Items.AddRange(Enum.GetValues<AnalogStandardSeries>().Cast<object>().ToArray()); capacitorSeries.Width = 110; capacitorSeries.SelectedItem = AnalogStandardSeries.E96;
        AddPair(0, 0, "R Series:", resistorSeries);
        AddPair(2, 0, "R Min:", resistorMinimum);
        AddPair(4, 0, "R Max:", resistorMaximum);
        AddPair(0, 1, "C Series:", capacitorSeries);
        AddPair(2, 1, "C Min:", capacitorMinimum);
        AddPair(4, 1, "C Max:", capacitorMaximum);
        optimize.Margin = new Padding(12, 6, 0, 0); biasCompensation.Margin = new Padding(12, 6, 0, 0);
        grid.Controls.Add(optimize, 0, 2); grid.SetColumnSpan(optimize, 2);
        grid.Controls.Add(biasCompensation, 2, 2); grid.SetColumnSpan(biasCompensation, 2);
        group.Controls.Add(grid);
        return group;
    }

    private async void DesignClicked(object? sender, EventArgs e)
    {
        if (designing) return;
        var totalWatch = Stopwatch.StartNew();
        var buildWatch = Stopwatch.StartNew();
        AnalogComputeDesignRequest request;
        try { request = BuildRequest(); }
        catch (Exception exception) { status.Text = exception.Message; status.ForeColor = Color.Firebrick; return; }
        buildWatch.Stop();
        designing = true;
        design.Enabled = false; status.Text = "Calculating and independently verifying selected values…"; status.ForeColor = SystemColors.ControlText;
        try
        {
            var coreWatch = Stopwatch.StartNew();
            var result = await Task.Run(() => engine.Design(request));
            coreWatch.Stop();
            if (IsDisposed || Disposing) return;
            if (selectedOperation != request.Operation)
            {
                status.Text = "Design result discarded because the selected operation changed.";
                status.ForeColor = Color.DarkGoldenrod;
                return;
            }
            status.Text = result.Success ? "Verified design complete." : $"{result.Capability.ReasonCode}: {result.Capability.Message}";
            status.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
            var renderWatch = Stopwatch.StartNew();
            ShowResult(result);
            renderWatch.Stop(); totalWatch.Stop();
            UiPerformanceTrace.Design("Precision Analog Computing", buildWatch.Elapsed, coreWatch.Elapsed, renderWatch.Elapsed, totalWatch.Elapsed);
        }
        catch (Exception exception)
        {
            if (!IsDisposed && !Disposing) { status.Text = exception.Message; status.ForeColor = Color.Firebrick; }
        }
        finally
        {
            designing = false;
            if (!IsDisposed && !Disposing) UpdateDesignAvailability();
        }
    }

    private AnalogComputeDesignRequest BuildRequest()
    {
        var terms = new List<AnalogInputTerm>();
        foreach (DataGridViewRow row in inputs.Rows)
            terms.Add(new(Convert.ToString(row.Cells[0].Value)!, Parse(row, 1)));
        var leakCorner = (double)lowerCorner.Value;
        var usefulDifferentiationMaximum = (double)upperCorner.Value;
        return new()
        {
            Operation = selectedOperation,
            Inputs = terms,
            OutputOffsetVoltage = (double)offset.Value,
            InputLowVoltage = (double)vinLow.Value,
            InputHighVoltage = (double)vinHigh.Value,
            OutputLowVoltage = (double)voutLow.Value,
            OutputHighVoltage = (double)voutHigh.Value,
            SupplyVccVoltage = (double)vcc.Value,
            SupplyVeeVoltage = (double)vee.Value,
            MaximumFunctionErrorPercent = (double)errorPercent.Value,
            GainK = (double)gainK.Value,
            HalfWavePolarity = (HalfWavePolarity)halfPolarity.SelectedItem!,
            OutputPolarity = (RectifierOutputPolarity)outputPolarity.SelectedItem!,
            MinimumInputVoltage = (double)inputMinimum.Value,
            MaximumInputVoltage = (double)inputMaximum.Value,
            MaximumExpectedFrequencyHz = (double)maximumFrequency.Value,
            ReferenceVoltage = (double)referenceVoltage.Value,
            LogScaleVoltage = (double)logScale.Value,
            IntegrationConstantPerSecond = (double)ki.Value,
            DifferentiationConstantSecond = (double)kd.Value,
            DifferentiationUsefulUpperFrequencyHz = selectedOperation == AnalogComputeOperationType.PracticalDifferentiator
                ? usefulDifferentiationMaximum : 0,
            LowerCornerHz = leakCorner,
            UpperCornerHz = selectedOperation == AnalogComputeOperationType.PracticalIntegrator
                ? Math.Max(leakCorner * 1_000, leakCorner * 10)
                : usefulDifferentiationMaximum,
            MaximumJunctionCurrentAmpere = .01,
            Junction = new JunctionModelParameters(),
            OpAmp = new()
            {
                UseIdealOpAmp = idealOpAmp.Checked,
                GainBandwidthHz = idealOpAmp.Checked ? double.PositiveInfinity : (double)gbw.Value,
                SlewRateVoltPerSecond = idealOpAmp.Checked ? double.PositiveInfinity : (double)slewRate.Value
            },
            Components = new()
            {
                ResistorSeries = (AnalogStandardSeries)resistorSeries.SelectedItem!,
                OptimizeStandardValues = optimize.Checked,
                BiasCompensation = biasCompensation.Checked,
                ResistorMinimumOhm = resistorMinimum.ValueOhm,
                ResistorMaximumOhm = resistorMaximum.ValueOhm,
                CapacitorSeries = (AnalogStandardSeries)capacitorSeries.SelectedItem!,
                CapacitorMinimumFarad = capacitorMinimum.ValueFarad,
                CapacitorMaximumFarad = capacitorMaximum.ValueFarad
            }
        };
    }

    private void ShowResult(AnalogComputeDesignResult result)
    {
        resultIsStale = false;
        renderedResult = result;
        results.SuspendLayout(); schematicResultPage.SuspendLayout(); analysisResultPage.SuspendLayout();
        try
        {
            if (!result.Success || result.CircuitGraph is null)
            {
                schematicView.Document = null;
                UiControlHost.ReplaceSingleChild(analysisHost, CreateMessageControl($"{result.FailureStage}\r\n{result.Capability.ReasonCode}\r\n{result.Capability.Message}\r\n{result.Capability.SuggestedAlternative}"));
                calculationsView.Text = $"Design failed during {result.FailureStage}.\r\n{result.Capability.ReasonCode}\r\n{result.Capability.Message}\r\n{result.Capability.SuggestedAlternative}";
                spiceView.Text = "* No netlist generated.";
            }
            else
            {
                var stageWatch = Stopwatch.StartNew();
                schematicView.Document = new PrecisionAnalogSchematicLayoutService().Layout(result.CircuitGraph);
                stageWatch.Stop(); UiPerformanceTrace.Stage("Precision Analog", "RenderSchematic", stageWatch.Elapsed);
                stageWatch.Restart();
                UpdateAnalysisControl(result);
                stageWatch.Stop(); UiPerformanceTrace.Stage("Precision Analog", "RenderAnalysis", stageWatch.Elapsed);
                stageWatch.Restart();
                calculationsView.Text = new PrecisionAnalogDesignReportBuilder().BuildText(result);
                stageWatch.Stop(); UiPerformanceTrace.Stage("Precision Analog", "RenderCalculations", stageWatch.Elapsed);
                stageWatch.Restart();
                spiceView.Text = BuildSpicePreview(result);
                stageWatch.Stop(); UiPerformanceTrace.Stage("Precision Analog", "RenderNetlist", stageWatch.Elapsed);
            }
            reportNeedsRefresh = true;
        }
        finally
        {
            analysisResultPage.ResumeLayout(false); schematicResultPage.ResumeLayout(false); results.ResumeLayout(false); results.PerformLayout();
        }
        EnsureReportPreview();
    }

    private void UpdateAnalysisControl(AnalogComputeDesignResult result)
    {
        if (result.Request.Operation is AnalogComputeOperationType.PracticalIntegrator or AnalogComputeOperationType.PracticalDifferentiator)
        {
            if (analysisHost.Controls.Count == 1 && analysisHost.Controls[0] is DynamicAnalysisControl existingDynamic)
            {
                existingDynamic.UpdateResult(result);
                return;
            }
            var dynamicAnalysis=new DynamicAnalysisControl();dynamicAnalysis.UpdateResult(result);
            UiControlHost.ReplaceSingleChild(analysisHost,dynamicAnalysis);
            return;
        }
        if (result.Request.Operation is AnalogComputeOperationType.LogarithmicAmplifier or
            AnalogComputeOperationType.ExponentialAmplifier or
            AnalogComputeOperationType.PrecisionHalfWaveRectifier or
            AnalogComputeOperationType.PrecisionFullWaveRectifier)
        {
            if (analysisHost.Controls.Count == 1 && analysisHost.Controls[0] is AffineTransferPlotControl existing)
            {
                ConfigureNonlinearTransferPlot(existing, result);
                return;
            }
            var plot = new AffineTransferPlotControl { Dock = DockStyle.Fill };
            ConfigureNonlinearTransferPlot(plot, result);
            UiControlHost.ReplaceSingleChild(analysisHost, plot);
            return;
        }
        if(result.Request.Operation==AnalogComputeOperationType.VoltageRangeMapper)
        {
            if(analysisHost.Controls.Count==1&&analysisHost.Controls[0] is AffineTransferPlotControl existingMapper)
            {ConfigureRangeMapperPlot(existingMapper,result);return;}
            var mapper=new AffineTransferPlotControl{Dock=DockStyle.Fill};ConfigureRangeMapperPlot(mapper,result);
            UiControlHost.ReplaceSingleChild(analysisHost,mapper);return;
        }
        if(analysisHost.Controls.Count==1&&analysisHost.Controls[0] is LinearCombinationAnalysisControl existingLinear)
        {existingLinear.UpdateResult(result);return;}
        var linear=new LinearCombinationAnalysisControl();linear.UpdateResult(result);UiControlHost.ReplaceSingleChild(analysisHost,linear);
    }

    private static void ConfigureNonlinearTransferPlot(AffineTransferPlotControl plot, AnalogComputeDesignResult result)
    {
        plot.SuspendLayout();
        try
        {
            plot.SweepInput = "Vin";
            plot.LowerLimit = result.Request.SupplyVeeVoltage + result.Request.OpAmp.OutputSwingHeadroomVoltage;
            plot.UpperLimit = result.Request.SupplyVccVoltage - result.Request.OpAmp.OutputSwingHeadroomVoltage;
            // Vin-Vout means a linear voltage axis. A logarithmic X axis would make ln(Vin) look like a straight line.
            plot.LogarithmicXAxis = false;
            if (result.Analysis is { Points.Count: > 0 } analysis)
            {
                var values = analysis.Points.SelectMany(point => new[] { point.Theoretical ?? point.Target, point.Actual }).ToArray();
                var minimum = values.Min(); var maximum = values.Max(); var padding = Math.Max(.02, (maximum - minimum) * .08);
                plot.PreferredMinimumY = minimum - padding; plot.PreferredMaximumY = maximum + padding;
            }
            plot.Analysis = result.Analysis;
        }
        finally { plot.ResumeLayout(false); }
    }

    private static void ConfigureRangeMapperPlot(AffineTransferPlotControl plot,AnalogComputeDesignResult result)
    {
        var inputStart=Math.Min(result.Request.InputLowVoltage,result.Request.InputHighVoltage);
        var inputStop=Math.Max(result.Request.InputLowVoltage,result.Request.InputHighVoltage);
        plot.SweepInput="VIN";plot.LowerLimit=result.Request.SupplyVeeVoltage+result.Request.OpAmp.OutputSwingHeadroomVoltage;
        plot.UpperLimit=result.Request.SupplyVccVoltage-result.Request.OpAmp.OutputSwingHeadroomVoltage;
        plot.PreferredMinimumY=Math.Min(result.Request.OutputLowVoltage,result.Request.OutputHighVoltage);
        plot.PreferredMaximumY=Math.Max(result.Request.OutputLowVoltage,result.Request.OutputHighVoltage);
        plot.Analysis=new AffineSweepAnalysisService().Analyze(result,"VIN",new Dictionary<string,double>(),inputStart,inputStop);
    }

    private static void ExportReport(Control owner, AnalogComputeDesignResult result, PrecisionAnalogDesignReportService service, bool json)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = json ? "Precision Analog Snapshot (*.json)|*.json" : "Precision Analog Report (*.html)|*.html",
            DefaultExt = json ? "json" : "html",
            AddExtension = true,
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

    private static Control CreateMessageControl(string message) => new Label
    {
        Dock = DockStyle.Fill,
        Text = message,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoSize = false,
        AutoEllipsis = true,
        Padding = new Padding(16)
    };

    private static void SetHostMessage(Panel host, string message)
    {
        UiControlHost.ReplaceSingleChild(host, CreateMessageControl(message));
    }

    private void EnsureReportPreview()
    {
        if (results.SelectedTab != reportResultPage || !reportNeedsRefresh) return;
        if (renderedResult is not { Success: true, CircuitGraph: not null } result)
        {
            if (reportBrowser is not null) { reportPreviewHost.Controls.Remove(reportBrowser); reportBrowser.Dispose(); reportBrowser = null; }
            SetHostMessage(reportPreviewHost, "A successful verified design is required before a report can be displayed.");
            reportNeedsRefresh = false;
            return;
        }
        reportPreviewHost.SuspendLayout();
        try
        {
            if (reportBrowser is null)
            {
                reportBrowser = new WebBrowser
                {
                    Dock = DockStyle.Fill,
                    AllowWebBrowserDrop = false,
                    IsWebBrowserContextMenuEnabled = true,
                    ScriptErrorsSuppressed = true,
                    WebBrowserShortcutsEnabled = true
                };
                UiControlHost.ReplaceSingleChild(reportPreviewHost, reportBrowser);
            }
            var watch = Stopwatch.StartNew();
            try
            {
                reportBrowser.DocumentText = new PrecisionAnalogDesignReportService().ExportHtml(result);
            }
            catch (Exception exception)
            {
                reportBrowser.DocumentText =
                    "<html><body style=\"font:13px 'Segoe UI',Arial,sans-serif;color:#7a1f1f;margin:18px\">" +
                    "<h3>Design report unavailable</h3><p>" + System.Net.WebUtility.HtmlEncode(exception.Message) + "</p></body></html>";
            }
            watch.Stop();
            UiPerformanceTrace.Stage("Precision Analog", "RenderReport", watch.Elapsed);
            reportNeedsRefresh = false;
        }
        finally { reportPreviewHost.ResumeLayout(false); }
    }

    private void ExportCurrentCir()
    {
        if (!EnsureExportableDesign()) return;
        var result = renderedResult!;
        using var dialog = new SaveFileDialog { Filter = "SPICE Circuit (*.cir)|*.cir|SPICE Netlist (*.net)|*.net|All files (*.*)|*.*", DefaultExt = "cir", AddExtension = true, FileName = $"precision-{result.Request.Operation}.cir" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { new PrecisionAnalogSpiceExportService().Save(result, dialog.FileName); }
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

    private static string BuildSpicePreview(AnalogComputeDesignResult result)
    {
        try { return new PrecisionAnalogSpiceExportService().Export(result); }
        catch(Exception exception) { return "* SPICE export unavailable: " + exception.Message; }
    }

    private void ExportCurrentReport(bool json)
    {
        if (!EnsureExportableDesign()) return;
        ExportReport(this, renderedResult!, new PrecisionAnalogDesignReportService(), json);
    }

    private void OperationChanged()
    {
        if (displayedOperation == selectedOperation) return;
        if (displayedOperation is AnalogComputeOperationType previous &&
           previous is not (AnalogComputeOperationType.GeneralLinearCombination or AnalogComputeOperationType.VoltageRangeMapper))
            operationStates[previous] = CaptureOperationState();
        SuspendLayout();
        updatingUi = true;
        try
        {
            if (selectedOperation is not (AnalogComputeOperationType.GeneralLinearCombination or AnalogComputeOperationType.VoltageRangeMapper))
                RestoreOperationState(selectedOperation);
            linearPanel.Visible = selectedOperation == AnalogComputeOperationType.GeneralLinearCombination;
            mapperPanel.Visible = selectedOperation == AnalogComputeOperationType.VoltageRangeMapper;
            operationPanel.Visible = !linearPanel.Visible && !mapperPanel.Visible;
            BuildOperationPanel();
            UpdateDesignAvailability();
            displayedOperation = selectedOperation;
            status.Text = string.Empty;
        }
        finally
        {
            updatingUi = false;
            ResumeLayout(false);
            PerformLayout();
        }
    }

    private OperationUiState CaptureOperationState() => new(
        gainK.Value, inputMinimum.Value, inputMaximum.Value, maximumFrequency.Value, referenceVoltage.Value, logScale.Value,
        ki.Value, kd.Value, lowerCorner.Value, upperCorner.Value,
        halfPolarity.SelectedIndex, outputPolarity.SelectedIndex);

    private void RestoreOperationState(AnalogComputeOperationType operation)
    {
        if (!operationStates.TryGetValue(operation, out var state))
            state = operation switch
            {
                AnalogComputeOperationType.LogarithmicAmplifier => OperationUiState.Default with { InputMinimum = .01m, InputMaximum = 10m },
                AnalogComputeOperationType.ExponentialAmplifier => OperationUiState.Default with { InputMinimum = -.2m, InputMaximum = .2m },
                AnalogComputeOperationType.PracticalIntegrator => OperationUiState.Default with { InputMinimum = -.05m, InputMaximum = .05m },
                AnalogComputeOperationType.PracticalDifferentiator => OperationUiState.Default with { InputMinimum = -.1m, InputMaximum = .1m, UpperCorner = 1_000m },
                _ => OperationUiState.Default
            };
        gainK.Value = state.GainK; inputMinimum.Value = state.InputMinimum; inputMaximum.Value = state.InputMaximum;
        maximumFrequency.Value = state.MaximumFrequency; referenceVoltage.Value = state.ReferenceVoltage; logScale.Value = state.LogScale; ki.Value = state.Ki; kd.Value = state.Kd;
        lowerCorner.Value = state.LowerCorner; upperCorner.Value = state.UpperCorner;
        halfPolarity.SelectedIndex = state.HalfPolarityIndex; outputPolarity.SelectedIndex = state.OutputPolarityIndex;
    }

    private void UpdateDesignAvailability()
    {
        design.Enabled = !designing;
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

    private void BuildNavigation(TreeView tree)
    {
        tree.BeginUpdate(); tree.Nodes.Clear();
        navigationRoot = tree.Nodes.Add("Precision Analog Computing");
        foreach (var item in Enum.GetValues<AnalogComputeOperationType>()) navigationRoot.Nodes.Add(PrecisionAnalogDesignEngine.DisplayName(item));
        navigationRoot.Expand(); tree.EndUpdate();
    }

    private void NavigationAfterSelect(object? sender, TreeViewEventArgs args)
    {
        var operations = Enum.GetValues<AnalogComputeOperationType>();
        var node = args.Node;
        if (node is not null && navigationRoot is not null && ReferenceEquals(node.Parent, navigationRoot) &&
            node.Index >= 0 && node.Index < operations.Length)
        {
            selectedOperation = operations[node.Index];
            OperationChanged();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DetachNavigation();
        base.Dispose(disposing);
    }

    private static double Parse(DataGridViewRow row, int column) => double.TryParse(Convert.ToString(row.Cells[column].Value), out var value) ? value : throw new FormatException($"Row {row.Index + 1}, column {row.Cells[column].OwningColumn?.HeaderText ?? column.ToString()} is not a valid number.");
    private sealed record OperationUiState(decimal GainK, decimal InputMinimum, decimal InputMaximum, decimal MaximumFrequency,
        decimal ReferenceVoltage, decimal LogScale, decimal Ki, decimal Kd,
        decimal LowerCorner, decimal UpperCorner,
        int HalfPolarityIndex, int OutputPolarityIndex)
    {
        public static OperationUiState Default => new(1, -5, 5, 1000, 1, .1m, 1000, .001m, 10, 10000, 0, 0);
    }
    private sealed record OperationFieldUi(Label Label, Control Container);
    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, int decimals) => new CompactNumericUpDown
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        DecimalPlaces = decimals,
        Increment = 1m,
        Width = 150,
        ThousandsSeparator = true
    };
    private static ComboBox Choice<T>(IEnumerable<T> values) where T : struct, Enum
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        box.Items.AddRange(values.Cast<object>().ToArray()); box.SelectedIndex = 0;
        UiFieldMetrics.FitCombo(box, 130, 240);
        return box;
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
                case DataGridView grid: grid.CellValueChanged += MarkStale; break;
            }
            HookParameters(child);
        }
    }

    private void MarkStale(object? sender, EventArgs e)
    {
        if (renderedResult is not null) resultIsStale = true;
    }

    private static Label Label(string text, int top) => new() { Text = text, AutoSize = true, Margin = new Padding(5, top, 4, 0) };
    private static void AddField(Control row, string title, Control field) { row.Controls.Add(Label(title, 7)); row.Controls.Add(field); }
    private sealed class ResistanceInput : UserControl
    {
        private readonly NumericUpDown number = new CompactNumericUpDown { DecimalPlaces = 6, Minimum = .000001m, Maximum = 1_000_000_000m, Increment = 1m, Width = 150, ThousandsSeparator = true };
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

    private sealed class CapacitanceInput : UserControl
    {
        private readonly NumericUpDown number = new CompactNumericUpDown { DecimalPlaces = 6, Minimum = .000001m, Maximum = 1_000_000_000m, Increment = 1m, Width = 140, ThousandsSeparator = true };
        private readonly ComboBox unit = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        public double ValueFarad => (double)number.Value * ((CapacitanceUnit)unit.SelectedItem!).Factor;
        public CapacitanceInput(double valueFarad)
        {
            AutoSize = true; Margin = Padding.Empty;
            CapacitanceUnit[] units = [new("pF", 1e-12), new("nF", 1e-9), new("\u00B5F", 1e-6)];
            unit.Items.AddRange(units.Cast<object>().ToArray());
            var selected = valueFarad >= 1e-6 ? units[2] : valueFarad >= 1e-9 ? units[1] : units[0];
            unit.SelectedItem = selected; number.Value = (decimal)(valueFarad / selected.Factor);
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            row.Controls.Add(number); row.Controls.Add(unit); Controls.Add(row);
        }
        private sealed record CapacitanceUnit(string Text, double Factor) { public override string ToString() => Text; }
    }
}
