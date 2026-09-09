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

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class PrecisionAnalogWizardControl : UserControl
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
    private readonly Panel operationPanel = new() { Dock = DockStyle.Top, AutoSize = false, Height = 142, Visible = false };
    private readonly GroupBox operationGroup = new() { AutoSize = false, Dock = DockStyle.Fill, Padding = new Padding(8, 15, 8, 5) };
    private readonly FlowLayoutPanel operationFields = new() { Dock = DockStyle.Fill, AutoSize = false, WrapContents = true, AutoScroll = true };
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
    private readonly NumericUpDown logScale = Number(.000001m, 1000, .1m, 6);
    private readonly NumericUpDown temperature = Number(-200, 500, 25, 2);
    private readonly NumericUpDown saturationCurrent = Number(.000000000000000001m, 1, .000000000001m, 15);
    private readonly NumericUpDown ideality = Number(.1m, 10, 1, 4);
    private readonly NumericUpDown ki = Number(.000001m, 100_000_000, 1000, 6);
    private readonly NumericUpDown kd = Number(.000000001m, 1000, .001m, 9);
    private readonly NumericUpDown lowerCorner = Number(.001m, 100_000_000, 10, 3);
    private readonly NumericUpDown upperCorner = Number(.001m, 100_000_000, 10000, 3);
    private readonly NumericUpDown lowerLimit = Number(-1000, 1000, -5, 6);
    private readonly NumericUpDown upperLimit = Number(-1000, 1000, 5, 6);
    private readonly NumericUpDown maximumJunctionCurrent = Number(.000000001m, 10, .01m, 9);
    private readonly Label mapperEquation = new() { AutoSize = true, ForeColor = Color.Navy };
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

    public PrecisionAnalogWizardControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Dock = DockStyle.Fill;
        BuildInterface();
        inputCount.ValueChanged += (_, _) => { if (!updatingUi) RebuildInputRows(); };
        inputs.CellValueChanged += (_, _) => { if (!updatingUi) UpdateFormula(); };
        offset.ValueChanged += (_, _) => { if (!updatingUi) UpdateFormula(); };
        foreach (var control in new Control[] { vinLow, vinHigh, voutLow, voutHigh }) control.TextChanged += (_, _) => { if (!updatingUi) UpdateMapperEquation(); };
        design.Click += DesignClicked;
        RebuildInputRows();
        OperationChanged();
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

    private void BuildInterface()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6, 0, 0, 0) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var parameters = new GroupBox { Text = "Precision Analog Computing Parameters", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 18, 10, 8) };
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        BuildLinearPanel(); BuildMapperPanel();
        InitializeOperationFields(); operationGroup.Controls.Add(operationFields); operationPanel.Controls.Add(operationGroup);
        content.Controls.Add(linearPanel); content.Controls.Add(mapperPanel); content.Controls.Add(operationPanel);
        content.Controls.Add(BuildElectricalRow()); content.Controls.Add(BuildComponentRow());
        var action = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
        action.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        action.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        action.Controls.Add(design, 0, 0); action.Controls.Add(status, 1, 0); content.Controls.Add(action);
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
        var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(row); layout.Controls.Add(mapperEquation); mapperPanel.Controls.Add(layout);
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
                break;
            case AnalogComputeOperationType.PrecisionFullWaveRectifier:
                ShowOperationField(outputPolarity, "Output:");
                ShowOperationField(gainK, "Gain K:");
                ShowInputRange();
                break;
            case AnalogComputeOperationType.LogarithmicAmplifier:
                if (inputMinimum.Value <= 0) inputMinimum.Value = .01m;
                ShowInputRange();
                ShowOperationField(logScale, "S (V/neper):");
                ShowJunctionFields();
                break;
            case AnalogComputeOperationType.ExponentialAmplifier:
                ShowInputRange();
                ShowOperationField(logScale, "S (V):");
                ShowJunctionFields();
                break;
            case AnalogComputeOperationType.PracticalIntegrator:
                ShowOperationField(ki, "Ki (1/s):");
                ShowOperationField(lowerCorner, "Leak fL (Hz):");
                ShowOperationField(upperCorner, "Verify to (Hz):");
                ShowInputRange();
                break;
            case AnalogComputeOperationType.PracticalDifferentiator:
                ShowOperationField(kd, "Kd (s):");
                ShowOperationField(lowerCorner, "Lower Corner (Hz):");
                ShowOperationField(upperCorner, "Upper Corner (Hz):");
                ShowInputRange();
                break;
            case AnalogComputeOperationType.PrecisionLimiter:
                ShowOperationField(gainK, "Gain K:");
                ShowOperationField(lowerLimit, "VLOW (V):");
                ShowOperationField(upperLimit, "VHIGH (V):");
                ShowInputRange();
                ShowOperationField(maximumFrequency, "Max f (Hz):");
                break;
        }
        operationFields.ResumeLayout(false);
        operationFields.PerformLayout();
        operationPanel.ResumeLayout(false);
        operationPanel.PerformLayout();
    }

    private void InitializeOperationFields()
    {
        foreach (var item in new (Control Control, string Label)[]
        {
            (halfPolarity,"Polarity:"),(outputPolarity,"Output:"),(gainK,"Gain K:"),(inputMinimum,"Vin Min (V):"),(inputMaximum,"Vin Max (V):"),
            (maximumFrequency,"Max f (Hz):"),(logScale,"S:"),(saturationCurrent,"Is (A):"),(ideality,"n:"),(temperature,"Temp (C):"),
            (maximumJunctionCurrent,"Max Ij (A):"),(ki,"Ki (1/s):"),(kd,"Kd (s):"),(lowerCorner,"Lower Corner (Hz):"),
            (upperCorner,"Upper Corner (Hz):"),(lowerLimit,"VLOW (V):"),(upperLimit,"VHIGH (V):")
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

    private void ShowJunctionFields()
    {
        ShowOperationField(saturationCurrent, "Is (A):");
        ShowOperationField(ideality, "n:");
        ShowOperationField(temperature, "Temp (C):");
        ShowOperationField(maximumJunctionCurrent, "Max Ij (A):");
    }

    private Control BuildElectricalRow()
    {
        var group = new GroupBox { Text = "Electrical / Practical Constraints", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8, 15, 8, 5) };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        AddField(row, "VCC (V):", vcc); AddField(row, "VEE (V):", vee); AddField(row, "Allowed Error (%):", errorPercent);
        idealOpAmp.Margin = new Padding(12, 6, 0, 0); row.Controls.Add(idealOpAmp);
        AddField(row, "GBW (Hz):", gbw); AddField(row, "Slew (V/s):", slewRate);
        row.Controls.Add(new Label { Text = "GND = 0 V; references are synthesized from VCC/GND/VEE (no Vref input).", AutoSize = true, Margin = new Padding(14, 7, 0, 0), ForeColor = Color.DimGray });
        group.Controls.Add(row); return group;
    }

    private Control BuildComponentRow()
    {
        var group = new GroupBox { Text = "Component Constraints", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8, 15, 8, 5) };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        resistorSeries.DropDownStyle = ComboBoxStyle.DropDownList; resistorSeries.DataSource = Enum.GetValues<AnalogStandardSeries>(); resistorSeries.Width = 110; resistorSeries.SelectedItem = AnalogStandardSeries.E96;
        capacitorSeries.DropDownStyle = ComboBoxStyle.DropDownList; capacitorSeries.DataSource = Enum.GetValues<AnalogStandardSeries>(); capacitorSeries.Width = 110; capacitorSeries.SelectedItem = AnalogStandardSeries.E96;
        row.Controls.Add(Label("R Series:", 7)); row.Controls.Add(resistorSeries);
        AddField(row, "R Min:", resistorMinimum); AddField(row, "R Max:", resistorMaximum);
        row.Controls.Add(Label("C Series:", 7)); row.Controls.Add(capacitorSeries);
        AddField(row, "C Min:", capacitorMinimum); AddField(row, "C Max:", capacitorMaximum);
        optimize.Margin = new Padding(12, 6, 0, 0); biasCompensation.Margin = new Padding(12, 6, 0, 0); row.Controls.Add(optimize); row.Controls.Add(biasCompensation);
        group.Controls.Add(row); return group;
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
            status.Text = result.Success ? "Verified design complete." : $"{result.Capability.ReasonCode}: {result.Capability.Message}";
            status.ForeColor = result.Success ? Color.DarkGreen : Color.Firebrick;
            var renderWatch = Stopwatch.StartNew();
            ShowResult(result);
            renderWatch.Stop(); totalWatch.Stop();
            UiPerformanceTrace.Design("Precision Analog Computing", buildWatch.Elapsed, coreWatch.Elapsed, renderWatch.Elapsed, totalWatch.Elapsed);
        }
        catch (Exception exception) { status.Text = exception.Message; status.ForeColor = Color.Firebrick; }
        finally { designing = false; UpdateDesignAvailability(); }
    }

    private AnalogComputeDesignRequest BuildRequest()
    {
        var terms = new List<AnalogInputTerm>();
        foreach (DataGridViewRow row in inputs.Rows)
            terms.Add(new(Convert.ToString(row.Cells[0].Value)!, Parse(row, 1)));
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
            UseInputRangesForFeasibility = false,
            GainK = (double)gainK.Value,
            HalfWavePolarity = (HalfWavePolarity)halfPolarity.SelectedItem!,
            OutputPolarity = (RectifierOutputPolarity)outputPolarity.SelectedItem!,
            MinimumInputVoltage = (double)inputMinimum.Value,
            MaximumInputVoltage = (double)inputMaximum.Value,
            MaximumExpectedFrequencyHz = (double)maximumFrequency.Value,
            ReferenceVoltage = Math.Min(1d, (double)vcc.Value / 2d),
            LogScaleVoltage = (double)logScale.Value,
            IntegrationConstantPerSecond = (double)ki.Value,
            DifferentiationConstantSecond = (double)kd.Value,
            LowerCornerHz = (double)lowerCorner.Value,
            UpperCornerHz = (double)upperCorner.Value,
            LowerOutputLimitVoltage = (double)lowerLimit.Value,
            UpperOutputLimitVoltage = (double)upperLimit.Value,
            MaximumJunctionCurrentAmpere = (double)maximumJunctionCurrent.Value,
            Junction = new()
            {
                Behavior = DiodeBehaviorModel.ShockleyJunction,
                SaturationCurrentAmpere = (double)saturationCurrent.Value,
                EmissionCoefficient = (double)ideality.Value,
                TemperatureCelsius = (double)temperature.Value
            },
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
        renderedResult = result;
        results.SuspendLayout(); schematicResultPage.SuspendLayout(); analysisResultPage.SuspendLayout();
        try
        {
            if (!result.Success || result.CircuitGraph is null)
            {
                schematicView.Document = null;
                UiControlHost.ReplaceSingleChild(analysisHost, CreateMessageControl($"{result.Capability.Level}\r\n{result.Capability.ReasonCode}\r\n{result.Capability.Message}\r\n{result.Capability.SuggestedAlternative}"));
                calculationsView.Text = $"{result.Capability.Level}\r\n{result.Capability.ReasonCode}\r\n{result.Capability.Message}\r\n{result.Capability.SuggestedAlternative}";
                spiceView.Text = "* No netlist generated.";
            }
            else
            {
                var stageWatch = Stopwatch.StartNew();
                schematicView.Document = new PrecisionAnalogSchematicLayoutService().Layout(result.CircuitGraph);
                stageWatch.Stop(); UiPerformanceTrace.Stage("Precision Analog", "RenderSchematic", stageWatch.Elapsed);
                stageWatch.Restart();
                UiControlHost.ReplaceSingleChild(analysisHost, CreateInteractiveAnalysisControl(result));
                stageWatch.Stop(); UiPerformanceTrace.Stage("Precision Analog", "RenderAnalysis", stageWatch.Elapsed);
                stageWatch.Restart();
                calculationsView.Text = string.Join("\r\n", result.Calculations.Select(record => $"{record.Title}\r\n  {record.Expression}\r\n  => {record.Result}{(record.Note is null ? "" : "\r\n  " + record.Note)}")) + "\r\n\r\nACTUAL VERIFICATION\r\n" + string.Join("\r\n", result.VerificationMetrics.Select(metric => $"{metric.Name}: target {metric.Target:G9}, actual {metric.Actual:G9}, error {metric.Error:G6}, {(metric.Pass ? "PASS" : "FAIL")}"));
                stageWatch.Stop(); UiPerformanceTrace.Stage("Precision Analog", "RenderCalculations", stageWatch.Elapsed);
                stageWatch.Restart();
                spiceView.Text = new PrecisionAnalogSpiceExportService().Export(result);
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

    private static Control CreateInteractiveAnalysisControl(AnalogComputeDesignResult result)
    {
        if (result.Request.Operation is AnalogComputeOperationType.PracticalIntegrator or AnalogComputeOperationType.PracticalDifferentiator)
            return new AnalogBodePlotControl { Dock = DockStyle.Fill, Analysis = result.Analysis };
        if (result.Request.Operation is AnalogComputeOperationType.PrecisionHalfWaveRectifier or AnalogComputeOperationType.PrecisionFullWaveRectifier)
            return new RectifierWaveformPlotControl { Dock = DockStyle.Fill, Result = result };
        if (result.Request.Operation is not (AnalogComputeOperationType.GeneralLinearCombination or AnalogComputeOperationType.VoltageRangeMapper))
            return new AffineTransferPlotControl
            {
                Dock = DockStyle.Fill,
                Analysis = result.Analysis,
                SweepInput = "Vin",
                LowerLimit = result.Request.SupplyVeeVoltage + result.Request.OpAmp.OutputSwingHeadroomVoltage,
                UpperLimit = result.Request.SupplyVccVoltage - result.Request.OpAmp.OutputSwingHeadroomVoltage
            };
        return result.Request.Operation == AnalogComputeOperationType.VoltageRangeMapper
            ? CreateRangeMapperAnalysisControl(result)
            : CreateLinearCombinationAnalysisControl(result);
    }

    private static Control CreateRangeMapperAnalysisControl(AnalogComputeDesignResult result)
    {
        var (root,toolbar)=CreateAnalysisLayout();
        var inputStart = Math.Min(result.Request.InputLowVoltage, result.Request.InputHighVoltage);
        var inputStop = Math.Max(result.Request.InputLowVoltage, result.Request.InputHighVoltage);
        toolbar.Controls.Add(new Label
        {
            Text = $"Input range: {inputStart:G8} V to {inputStop:G8} V    Output range: {result.Request.OutputLowVoltage:G8} V to {result.Request.OutputHighVoltage:G8} V",
            AutoSize = true,
            Margin = new Padding(3, 7, 0, 0)
        });
        root.Controls.Add(new AffineTransferPlotControl
        {
            Dock = DockStyle.Fill,
            SweepInput = "VIN",
            LowerLimit = result.Request.SupplyVeeVoltage + result.Request.OpAmp.OutputSwingHeadroomVoltage,
            UpperLimit = result.Request.SupplyVccVoltage - result.Request.OpAmp.OutputSwingHeadroomVoltage,
            PreferredMinimumY = Math.Min(result.Request.OutputLowVoltage, result.Request.OutputHighVoltage),
            PreferredMaximumY = Math.Max(result.Request.OutputLowVoltage, result.Request.OutputHighVoltage),
            Analysis = new AffineSweepAnalysisService().Analyze(result, "VIN", new Dictionary<string, double>(), inputStart, inputStop)
        }, 0, 1);
        return root;
    }

    private static Control CreateLinearCombinationAnalysisControl(AnalogComputeDesignResult result)
    {
        var (root,toolbar)=CreateAnalysisLayout();
        var sweep = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 105 };
        sweep.Items.AddRange(result.Request.Inputs.Select(input => (object)input.Name).ToArray()); sweep.SelectedIndex = 0;
        var start = Number(-1_000_000, 1_000_000, -1, 4); var stop = Number(-1_000_000, 1_000_000, 1, 4);
        var update = new Button { Text = "Update Plot", AutoSize = true, MinimumSize = new Size(95, 27) };
        toolbar.Controls.Add(Label("Sweep Input:", 7)); toolbar.Controls.Add(sweep); toolbar.Controls.Add(Label("From (V):", 7)); toolbar.Controls.Add(start); toolbar.Controls.Add(Label("To (V):", 7)); toolbar.Controls.Add(stop); toolbar.Controls.Add(update);
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
            catch (Exception exception) { MessageBox.Show(root, exception.Message, "Analysis", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        sweep.SelectedIndexChanged += (_, _) => { RefreshRows(); Analyze(); };
        update.Click += (_, _) => Analyze();
        fixedGrid.CellEndEdit += (_, _) => Analyze();
        RefreshRows(); Analyze(); return root;
    }

    private static (TableLayoutPanel Root,FlowLayoutPanel Toolbar) CreateAnalysisLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 5) };
        root.Controls.Add(toolbar,0,0);
        return (root,toolbar);
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
            reportBrowser.DocumentText = new PrecisionAnalogDesignReportService().ExportHtml(result);
            watch.Stop();
            UiPerformanceTrace.Stage("Precision Analog", "RenderReport", watch.Elapsed);
            reportNeedsRefresh = false;
        }
        finally { reportPreviewHost.ResumeLayout(false); }
    }

    private void ExportCurrentCir()
    {
        if (renderedResult is not { Success: true } result) return;
        using var dialog = new SaveFileDialog { Filter = "SPICE Circuit (*.cir)|*.cir|SPICE Netlist (*.net)|*.net|All files (*.*)|*.*", DefaultExt = "cir", AddExtension = true, FileName = $"precision-{result.Request.Operation}.cir" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { new PrecisionAnalogSpiceExportService().Save(result, dialog.FileName); }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "CIR Export", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExportCurrentReport(bool json)
    {
        if (renderedResult is not { Success: true } result) return;
        ExportReport(this, result, new PrecisionAnalogDesignReportService(), json);
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
            if (selectedOperation == AnalogComputeOperationType.VoltageRangeMapper) UpdateMapperEquation();
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
        gainK.Value, inputMinimum.Value, inputMaximum.Value, maximumFrequency.Value, logScale.Value, temperature.Value,
        saturationCurrent.Value, ideality.Value, ki.Value, kd.Value, lowerCorner.Value, upperCorner.Value,
        lowerLimit.Value, upperLimit.Value, maximumJunctionCurrent.Value, halfPolarity.SelectedIndex, outputPolarity.SelectedIndex);

    private void RestoreOperationState(AnalogComputeOperationType operation)
    {
        if (!operationStates.TryGetValue(operation, out var state))
            state = operation switch
            {
                AnalogComputeOperationType.LogarithmicAmplifier => OperationUiState.Default with { InputMinimum = .01m, InputMaximum = 10m },
                AnalogComputeOperationType.ExponentialAmplifier => OperationUiState.Default with { InputMinimum = -.2m, InputMaximum = .2m },
                AnalogComputeOperationType.PracticalIntegrator => OperationUiState.Default with { InputMinimum = -.05m, InputMaximum = .05m },
                AnalogComputeOperationType.PracticalDifferentiator => OperationUiState.Default with { InputMinimum = -.1m, InputMaximum = .1m },
                _ => OperationUiState.Default
            };
        gainK.Value = state.GainK; inputMinimum.Value = state.InputMinimum; inputMaximum.Value = state.InputMaximum;
        maximumFrequency.Value = state.MaximumFrequency; logScale.Value = state.LogScale; temperature.Value = state.Temperature;
        saturationCurrent.Value = state.SaturationCurrent; ideality.Value = state.Ideality; ki.Value = state.Ki; kd.Value = state.Kd;
        lowerCorner.Value = state.LowerCorner; upperCorner.Value = state.UpperCorner; lowerLimit.Value = state.LowerLimit;
        upperLimit.Value = state.UpperLimit; maximumJunctionCurrent.Value = state.MaximumJunctionCurrent;
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
        decimal LogScale, decimal Temperature, decimal SaturationCurrent, decimal Ideality, decimal Ki, decimal Kd,
        decimal LowerCorner, decimal UpperCorner, decimal LowerLimit, decimal UpperLimit, decimal MaximumJunctionCurrent,
        int HalfPolarityIndex, int OutputPolarityIndex)
    {
        public static OperationUiState Default => new(1, -5, 5, 1000, .1m, 25, .000000000001m, 1, 1000, .001m, 10, 10000, -5, 5, .01m, 0, 0);
    }
    private sealed record OperationFieldUi(Label Label, Control Container);
    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, int decimals) => new CompactNumericUpDown
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        DecimalPlaces = decimals,
        Increment = 1m,
        Width = 125,
        ThousandsSeparator = true
    };
    private static ComboBox Choice<T>(IEnumerable<T> values) where T : struct, Enum
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
        box.Items.AddRange(values.Cast<object>().ToArray()); box.SelectedIndex = 0; return box;
    }
    private static Label Label(string text, int top) => new() { Text = text, AutoSize = true, Margin = new Padding(5, top, 4, 0) };
    private static void AddField(Control row, string title, Control field) { row.Controls.Add(Label(title, 7)); row.Controls.Add(field); }
    private sealed class ResistanceInput : UserControl
    {
        private readonly NumericUpDown number = new CompactNumericUpDown { DecimalPlaces = 6, Minimum = .000001m, Maximum = 1_000_000_000m, Increment = 1m, Width = 135, ThousandsSeparator = true };
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
        private readonly NumericUpDown number = new CompactNumericUpDown { DecimalPlaces = 6, Minimum = .000001m, Maximum = 1_000_000_000m, Increment = 1m, Width = 120, ThousandsSeparator = true };
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
