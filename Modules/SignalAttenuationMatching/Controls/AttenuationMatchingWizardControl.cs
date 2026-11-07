using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using CircuitModuleDesigner.Controls;
using CircuitModuleDesigner.Core.Circuit.Spice;
using CircuitModuleDesigner.Forms;
using CircuitModuleDesigner.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using AttenuationDesignMode = CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums.DesignMode;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Engines;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Reporting;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Schematic;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Services;
using CircuitModuleDesigner.Services;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Controls;

public sealed class AttenuationMatchingWizardControl : UserControl, IDesignModuleHost
{
    private readonly TreeView navigation;
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill };
    private readonly Label stepTitle = new() { AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false, Font = new Font("Segoe UI", 9F, FontStyle.Bold), Padding = new Padding(8, 7, 8, 7) };
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button next = new() { Text = "Next >", AutoSize = true, MinimumSize = new Size(95, 28) };
    private readonly Button back = new() { Text = "< Back", AutoSize = true, MinimumSize = new Size(95, 28) };
    private readonly string[] titles =
    {
        "Select Design Mode", "Source & Load Configuration", "Performance Requirements", "Network Topology",
        "Component Selection & Optimization", "Candidate & Verification"
    };
    private readonly List<Panel> pages = [];
    private int currentIndex;
    private bool updatingNavigation;
    private bool initializing = true;
    private bool designing;
    private bool stale;
    private bool configuringMatchingPlanGrid;
    private AttenuationMatchingDesignResult? result;
    private int selectedIndex;
    private CancellationTokenSource? designCancellation;

    private readonly RadioButton attenuationMode = new() { Text = "Attenuation", AutoSize = true, Font = new Font("Segoe UI Semibold", 10F) };
    private readonly RadioButton matchingMode = new() { Text = "Single-frequency Matching", AutoSize = true, Font = new Font("Segoe UI Semibold", 10F) };
    private readonly RadioButton multiFrequencyMode = new() { Text = "Multi-frequency Matching", AutoSize = true, Font = new Font("Segoe UI Semibold", 10F) };
    private readonly RadioButton broadbandMode = new() { Text = "Broadband Matching", AutoSize = true, Font = new Font("Segoe UI Semibold", 10F) };
    private readonly Label modeDescription = new() { AutoSize = true, MaximumSize = new Size(1000, 0), ForeColor = Color.FromArgb(75, 85, 99), Padding = new Padding(4, 6, 4, 4) };
    private readonly List<(AttenuationDesignMode Mode, Panel Card, RadioButton Radio)> modeCards = [];
    private AttenuationDesignMode selectedMode = AttenuationDesignMode.Attenuation;
    private MatchingDesignKind selectedMatchingKind = MatchingDesignKind.SingleFrequency;

    private readonly NumericUpDown sourceR = Number(.000001m, 1_000_000_000, 50, 3);
    private readonly NumericUpDown sourceX = Number(-1_000_000_000, 1_000_000_000, 0, 3);
    private readonly NumericUpDown loadR = Number(.000001m, 1_000_000_000, 75, 3);
    private readonly NumericUpDown loadX = Number(-1_000_000_000, 1_000_000_000, 0, 3);
    private readonly NumericUpDown signalValue = Number(-1000, 1000, 1, 3);
    private readonly ComboBox signalUnit = Choice(Enum.GetValues<SignalLevelUnit>());
    private GroupBox? frequencyGroup;
    private GroupBox? referenceGroup;
    private GroupBox? capacitorGroup;
    private GroupBox? inductorGroup;
    private GroupBox? resistorGroup;
    private Label? sourceImportNote;
    private Label? loadImportNote;
    private IReadOnlyList<PortImpedancePoint>? sourceVnaPoints;
    private IReadOnlyList<PortImpedancePoint>? loadVnaPoints;
    private readonly EngineeringNumberInput designFrequency = new(10e6, EngineeringUnits.Frequency, 1, 1e12);
    private readonly EngineeringNumberInput startFrequency = new(1e6, EngineeringUnits.Frequency, 1, 1e12);
    private readonly EngineeringNumberInput stopFrequency = new(100e6, EngineeringUnits.Frequency, 1, 1e12);
    private readonly NumericUpDown sweepPoints = Number(21, 4001, 121, 0);
    private readonly ComboBox sweepType = Choice(new[] { "Logarithmic", "Linear" });

    private readonly NumericUpDown attenuationTarget = Number(0, 200, 6, 3);
    private readonly NumericUpDown attenuationError = Number(.001m, 20, .5m, 3);
    private readonly NumericUpDown attenuationInputRl = Number(.1m, 200, 20, 2);
    private readonly NumericUpDown attenuationOutputRl = Number(.1m, 200, 20, 2);
    private readonly NumericUpDown maxInputPower = Number(.000001m, 100000, 1, 4);
    private readonly NumericUpDown matchingInputRl = Number(.1m, 200, 20, 2);
    private readonly NumericUpDown matchingOutputRl = Number(.1m, 200, 20, 2);
    private readonly NumericUpDown maximumInsertionLoss = Number(0, 100, 1, 3);
    private readonly NumericUpDown targetBandwidth = Number(0, 1e12m, 0, 3);
    private readonly ComboBox objective = Choice(Enum.GetValues<OptimizationObjective>());
    private readonly GroupBox attenuationGroup = new() { Text = "Attenuation Requirements", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8, 22, 8, 6) };
    private readonly GroupBox matchingGroup = new() { Text = "Matching Requirements", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8, 22, 8, 6) };
    private readonly DataGridView matchingPlanGrid = new()
    {
        Dock = DockStyle.Fill, BackgroundColor = SystemColors.Window,
        RowHeadersVisible = false, AllowUserToAddRows = true, AllowUserToDeleteRows = true,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect
    };
    private readonly TableLayoutPanel matchingPlanPanel = new()
    {
        Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 2,
        Margin = new Padding(0, 10, 0, 0), Padding = new Padding(4, 8, 4, 4), Visible = false
    };
    private readonly Label matchingPlanHint = new()
    {
        AutoSize = true, Dock = DockStyle.Fill, ForeColor = Color.DimGray,
        Padding = new Padding(0, 0, 0, 6)
    };

    private readonly RadioButton automaticTopology = new() { Text = "Automatic topology search", AutoSize = true, Checked = true };
    private readonly RadioButton manualTopology = new() { Text = "Manual topology restriction", AutoSize = true };
    private readonly ComboBox topologyFamily = Choice(Enum.GetValues<TopologyFamily>());
    private readonly ComboBox matchingResponse = Choice(Enum.GetValues<MatchingResponse>());
    private readonly NumericUpDown topologyQ = Number(.01m, 1000, 1.732m, 3);
    private readonly CheckBox allowMixed = new() { Text = "Allow mixed reactance", AutoSize = true };
    private readonly Label topologyHint = new() { AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = Color.DimGray };
    private readonly AttenuationMatchingTopologyPreviewControl topologyPreview = new() { Dock = DockStyle.Fill };

    private readonly ComboBox valueMode = Choice(Enum.GetValues<ComponentValueMode>());
    private readonly EngineeringNumberInput minimumResistance = new(1, EngineeringUnits.Resistance, .000001, 1e12);
    private readonly EngineeringNumberInput maximumResistance = new(1e6, EngineeringUnits.Resistance, .000001, 1e12);
    private readonly NumericUpDown maximumResistorPower = Number(.000001m, 100000, .25m, 4);
    private readonly EngineeringNumberInput minimumCapacitance = new(1e-12, EngineeringUnits.Capacitance, 1e-15, 1);
    private readonly EngineeringNumberInput maximumCapacitance = new(1e-5, EngineeringUnits.Capacitance, 1e-15, 1);
    private readonly NumericUpDown capacitorEsr = Number(0, 100000, 0, 6);
    private readonly OptionalNumberInput capacitorMaxVoltage = new();
    private readonly OptionalNumberInput capacitorMaxRipple = new();
    private readonly OptionalNumberInput capacitorSrfMhz = new();
    private readonly EngineeringNumberInput minimumInductance = new(1e-9, EngineeringUnits.Inductance, 1e-12, 1);
    private readonly EngineeringNumberInput maximumInductance = new(1e-2, EngineeringUnits.Inductance, 1e-12, 1);
    private readonly NumericUpDown inductorQ = Number(0, 100000, 0, 3);
    private readonly NumericUpDown inductorSeriesResistance = Number(0, 100000, 0, 6);
    private readonly OptionalNumberInput inductorMaxRms = new();
    private readonly OptionalNumberInput inductorSaturation = new();
    private readonly OptionalNumberInput inductorSrfMhz = new();
    private readonly NumericUpDown maximumCombinations = Number(100, 200000, 20000, 0);
    private readonly NumericUpDown maximumCandidates = Number(1, 100, 12, 0);

    private readonly DataGridView candidateGrid = Grid();
    private readonly Button calculate = new() { Text = "Calculate", AutoSize = true, MinimumSize = new Size(110, 30), Font = new Font("Segoe UI Semibold", 9F) };
    private readonly Label candidateStatus = new() { AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
    private readonly FilterSchematicControl schematicView = new() { Dock = DockStyle.Fill };
    private readonly AttenuationMatchingResponsePlotControl responsePlot = new() { Dock = DockStyle.Fill };
    private readonly AttenuationMatchingSmithChartControl smithChart = new() { Dock = DockStyle.Fill };
    private readonly FilterSchematicControl outputSchematic = new() { Dock = DockStyle.Fill };
    private readonly AttenuationMatchingResponsePlotControl outputResponse = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox outputSpice = TextView();

    private readonly DataGridView componentGrid = Grid();
    private readonly RichTextBox calculationView = TextView();
    private readonly TabControl previewTabs = new() { Dock = DockStyle.Fill, Padding = new Point(10, 4) };
    private TabPage? smithPage;
    private TabPage? analysisPage;
    private TabPage? componentsPage;
    private TabPage? netlistPage;
    private TabPage? reportPage;
    private readonly RichTextBox netlistView = TextView();
    private readonly WebBrowser reportView = new() { Dock = DockStyle.Fill, AllowWebBrowserDrop = false, ScriptErrorsSuppressed = true };
    private readonly RichTextBox outputSummary = TextView();
    private readonly DataGridView performanceGrid = Grid();
    private readonly RichTextBox outputDiagnostics = TextView();
    private readonly Label outputStatus = new() { AutoSize = true, Dock = DockStyle.Fill, AutoEllipsis = true, Font = new Font("Segoe UI Semibold", 10F) };
    private readonly List<(Button Button, SpiceNetlistDialect Dialect)> spiceExportButtons = [];
    private bool refreshingCandidates;
    private string? previewCandidateId;
    private string? renderedReportCandidateId;
    private AttenuationMatchingDesignResult? displayedCandidateResult;

    public string ModuleTitle => "Signal Attenuation & Impedance Matching";
    public bool HasDesign => result is { Success: true } && SelectedCandidate is { Status: not DesignStatus.Failed };
    public bool IsStale => stale;
    public void Calculate() => _ = RunDesignAsync();
    public void DetachNavigation() => navigation.AfterSelect -= NavigationAfterSelect;

    public AttenuationMatchingWizardControl(TreeView navigation)
    {
        this.navigation = navigation;
        Dock = DockStyle.Fill;
        BackColor = SystemColors.Control;
        DoubleBuffered = true;
        BuildInterface();
        PopulateNavigation();
        initializing = false;
        UpdateModeVisibility();
        UpdateTopologyFamilies();
        for (var index = 0; index < pages.Count - 1; index++) HookInvalidation(pages[index]);
        ShowStep(0);
    }

    public string BuildDesignJson() => !stale && result is { Success: true } ? new AttenuationMatchingDesignSnapshotService().Build(ResultWithSelection()) : string.Empty;

    public string BuildDesignSummary()
    {
        if (stale || result is null) return string.Empty;
        var selected = SelectedCandidate;
        var summary = new StringBuilder()
            .AppendLine($"Signal attenuation / impedance matching design: {result.Status}")
            .AppendLine($"Mode: {result.Request.Mode}, design frequency: {result.Request.DesignFrequencyHz:G6} Hz")
            .AppendLine($"Frequency strategy: {(result.Request.Mode == AttenuationDesignMode.ImpedanceMatching ? result.Request.MatchingPlan.Kind : null)}")
            .AppendLine($"Selected topology: {selected?.TopologyName ?? "none"}")
            .AppendLine($"Message: {result.Message}");
        if (selected is not null)
            summary.AppendLine($"Attenuation: {selected.Metrics.AttenuationDb:G6} dB; input RL: {selected.Metrics.InputReturnLossDb:G6} dB; output RL: {selected.Metrics.OutputReturnLossDb:G6} dB")
                .AppendLine($"Components: {selected.Components.Count}; standard values: {(selected.IsStandardValueDesign ? "yes" : "continuous")}");
        if (selected?.Diagnostics.Count > 0)
        {
            summary.AppendLine("Warnings/errors:");
            foreach (var diagnostic in selected.Diagnostics) summary.AppendLine($"  [{diagnostic.Severity}] {diagnostic.Code}: {diagnostic.Description}");
        }
        return summary.ToString();
    }

    public string? BuildNetlist()
    {
        if (stale || SelectedCandidate is not { Status: not DesignStatus.Failed } selected) return null;
        try
        {
            var service = new AttenuationMatchingSpiceExportService();
            return service.ExportDocumentNetlist(selected, result!.Request);
        }
        catch { return null; }
    }

    private TopologyCandidate? SelectedCandidate => result is { Candidates.Count: > 0 } && selectedIndex >= 0 && selectedIndex < result.Candidates.Count
        ? result.Candidates[selectedIndex] : null;

    private AttenuationMatchingDesignResult ResultWithSelection() =>
        result is null ? new AttenuationMatchingDesignResult { Request = DefaultRequest(), Status = DesignStatus.Failed }
            : result with { SelectedIndex = selectedIndex };

    private void BuildInterface()
    {
        objective.Width = 360; objective.DropDownWidth = 380;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var command = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(6) };
        next.Click += (_, _) => Advance();
        back.Click += (_, _) => ShowStep(Math.Max(0, currentIndex - 1));
        command.Controls.Add(next); command.Controls.Add(back);

        pages.Add(BuildModePage());
        pages.Add(BuildPortPage());
        pages.Add(BuildSpecificationPage());
        pages.Add(BuildTopologyPage());
        pages.Add(BuildConstraintPage());
        pages.Add(BuildCandidatePage());
        foreach (var page in pages) { page.Visible = false; pageHost.Controls.Add(page); }
        root.Controls.Add(stepTitle, 0, 0); root.Controls.Add(pageHost, 0, 1); root.Controls.Add(command, 0, 2);
        Controls.Add(root);
    }

    private void PopulateNavigation()
    {
        updatingNavigation = true;
        navigation.BeginUpdate();
        navigation.Nodes.Clear();
        foreach (var title in titles) navigation.Nodes.Add(new TreeNode(title));
        navigation.SelectedNode = navigation.Nodes[0];
        navigation.EndUpdate();
        updatingNavigation = false;
        navigation.AfterSelect += NavigationAfterSelect;
    }

    private void NavigationAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (updatingNavigation || e.Node is null) return;
        ShowStep(e.Node.Index);
    }

    private void ShowStep(int index)
    {
        currentIndex = Math.Clamp(index, 0, pages.Count - 1);
        stepTitle.Text = $"Signal Attenuation & Impedance Matching - {currentIndex + 1}. {titles[currentIndex]}";
        foreach (var page in pages) page.Visible = false;
        pages[currentIndex].Visible = true;
        updatingNavigation = true;
        if (navigation.Nodes.Count > currentIndex) navigation.SelectedNode = navigation.Nodes[currentIndex];
        updatingNavigation = false;
        back.Enabled = currentIndex > 0;
        next.Enabled = currentIndex < pages.Count - 1 && (currentIndex != 5 || result is { Success: true });
        next.Text = currentIndex == pages.Count - 2 ? "View Result >" : "Next >";
        next.Visible = currentIndex < pages.Count - 1;
        if (currentIndex == 5) RefreshCandidates();
        pageHost.PerformLayout();
    }
    private Panel BuildModePage()
    {
        var page = Page();
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(18, 14, 18, 14) };
        attenuationMode.Font = Font; matchingMode.Font = Font; multiFrequencyMode.Font = Font; broadbandMode.Font = Font;
        attenuationMode.AutoSize = true; matchingMode.AutoSize = true; multiFrequencyMode.AutoSize = true; broadbandMode.AutoSize = true;
        attenuationMode.Margin = new Padding(2); matchingMode.Margin = new Padding(2); multiFrequencyMode.Margin = new Padding(2); broadbandMode.Margin = new Padding(2);
        attenuationMode.CheckedChanged += (_, _) => { if (attenuationMode.Checked) SelectMode(AttenuationDesignMode.Attenuation); };
        matchingMode.CheckedChanged += (_, _) => { if (matchingMode.Checked) SelectMode(AttenuationDesignMode.ImpedanceMatching, MatchingDesignKind.SingleFrequency); };
        multiFrequencyMode.CheckedChanged += (_, _) => { if (multiFrequencyMode.Checked) SelectMode(AttenuationDesignMode.ImpedanceMatching, MatchingDesignKind.MultiFrequency); };
        broadbandMode.CheckedChanged += (_, _) => { if (broadbandMode.Checked) SelectMode(AttenuationDesignMode.ImpedanceMatching, MatchingDesignKind.Broadband); };
        layout.Controls.Add(attenuationMode);
        layout.Controls.Add(matchingMode);
        layout.Controls.Add(multiFrequencyMode);
        layout.Controls.Add(broadbandMode);
        page.Controls.Add(layout);
        SelectMode(AttenuationDesignMode.Attenuation);
        return page;
    }

    private void SelectMode(AttenuationDesignMode mode, MatchingDesignKind matchingKind = MatchingDesignKind.SingleFrequency)
    {
        selectedMode = mode;
        selectedMatchingKind = matchingKind;
        attenuationMode.Checked = mode == AttenuationDesignMode.Attenuation;
        matchingMode.Checked = mode == AttenuationDesignMode.ImpedanceMatching && matchingKind == MatchingDesignKind.SingleFrequency;
        multiFrequencyMode.Checked = mode == AttenuationDesignMode.ImpedanceMatching && matchingKind == MatchingDesignKind.MultiFrequency;
        broadbandMode.Checked = mode == AttenuationDesignMode.ImpedanceMatching && matchingKind == MatchingDesignKind.Broadband;
        if (mode == AttenuationDesignMode.Attenuation)
        {
            sourceX.Value = 0; loadX.Value = 0;
        }
        UpdateModeVisibility();
        ConfigureMatchingPlanGrid();
        UpdateTopologyFamilies();
        MarkStale();
    }
    private Panel BuildPortPage()
    {
        var page = Page();
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var source = Group("Source Port");
        var target = Group("Load Port");
        FillPortGroup(source, sourceR, sourceX, includeSignal: true, isSource: true);
        FillPortGroup(target, loadR, loadX, includeSignal: false, isSource: false);
        layout.Controls.Add(source, 0, 0); layout.Controls.Add(target, 1, 0);

        var reference = Group("Impedance Reference");
        referenceGroup = reference;
        var referenceGrid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        referenceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        referenceGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        AddPair(referenceGrid, 0, 0, "Design frequency (X reference):", designFrequency);
        reference.Controls.Add(referenceGrid);
        layout.Controls.Add(reference, 0, 1); layout.SetColumnSpan(reference, 2);

        var frequency = Group("Analysis Sweep");
        frequencyGroup = frequency;
        var grid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 4 };
        for (var index = 0; index < 4; index++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        InputColumns(grid, 280, 1, 3);
        AddPair(grid, 0, 0, "Start frequency:", startFrequency);
        AddPair(grid, 2, 0, "Stop frequency:", stopFrequency);
        AddPair(grid, 0, 1, "Sweep points:", sweepPoints);
        AddPair(grid, 2, 1, "Sweep type:", sweepType);
        matchingPlanPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        matchingPlanPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        matchingPlanPanel.Controls.Add(matchingPlanHint, 0, 0);
        matchingPlanPanel.Controls.Add(matchingPlanGrid, 0, 1);
        matchingPlanGrid.CellValueChanged += (_, eventArgs) => MatchingPlanChanged(eventArgs);
        matchingPlanGrid.RowsRemoved += (_, _) => MatchingPlanChanged();
        matchingPlanGrid.UserAddedRow += (_, _) => MatchingPlanChanged();
        var frequencyLayout = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2 };
        frequencyLayout.Controls.Add(grid, 0, 0);
        frequencyLayout.Controls.Add(matchingPlanPanel, 0, 1);
        frequency.Controls.Add(frequencyLayout);
        layout.Controls.Add(frequency, 0, 2); layout.SetColumnSpan(frequency, 2);
        page.Controls.Add(layout);
        return page;
    }

    private void FillPortGroup(GroupBox group, NumericUpDown resistance, NumericUpDown reactance, bool includeSignal, bool isSource)
    {
        var grid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        AddPair(grid, 0, 0, "Resistance R (Ω):", resistance);
        AddPair(grid, 0, 1, "Reactance (Ω):", reactance);
        if (includeSignal)
        {
            var signal = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            signal.Controls.Add(signalValue); signal.Controls.Add(signalUnit);
            AddPair(grid, 0, 2, "Signal level:", signal);
        }
        reactance.ValueChanged += (_, _) => UpdatePortControls();
        var import = new Button { Text = "Import VNA...", AutoSize = true, MinimumSize = new Size(120, 28), Margin = new Padding(3, 8, 3, 3) };
        import.Click += (_, _) => ImportTouchstone(isSource);
        grid.Controls.Add(import, 1, includeSignal ? 3 : 2);
        var importNote = new Label
        {
            AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(330, 0),
            Margin = new Padding(3, 0, 3, 3), Visible = false
        };
        if (isSource) sourceImportNote = importNote; else loadImportNote = importNote;
        void ClearImportNote()
        {
            importNote.Text = string.Empty; importNote.Visible = false;
            if (isSource) sourceVnaPoints = null; else loadVnaPoints = null;
        }
        resistance.ValueChanged += (_, _) => ClearImportNote();
        reactance.ValueChanged += (_, _) => ClearImportNote();
        grid.Controls.Add(importNote, 1, includeSignal ? 4 : 3);
        group.Controls.Add(grid);
    }

    private void ImportTouchstone(bool source)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Touchstone VNA (*.s1p;*.s2p)|*.s1p;*.s2p|All files (*.*)|*.*",
            Title = source ? "Import source VNA data" : "Import load VNA data",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var data = TouchstoneImporter.Parse(File.ReadAllText(dialog.FileName), dialog.FileName);
            var portIndex = 0;
            if (data.PortCount == 2)
            {
                var selectedPort = ChooseTouchstonePort(dialog.FileName, source);
                if (selectedPort is null) return;
                portIndex = selectedPort.Value;
            }
            var imported = TouchstoneImporter.Import(data, dialog.FileName, portIndex, SelectedReferenceFrequency());
            if (imported.Impedance.Real > (double)sourceR.Maximum || imported.Impedance.Real < (double)sourceR.Minimum)
                throw new InvalidDataException($"Imported resistance {imported.Impedance.Real:G6} Ω is outside the supported range.");
            if (source)
            {
                sourceR.Value = (decimal)imported.Impedance.Real;
                sourceX.Value = (decimal)imported.Impedance.Imaginary;
                sourceVnaPoints = imported.Points;
            }
            else
            {
                loadR.Value = (decimal)imported.Impedance.Real;
                loadX.Value = (decimal)imported.Impedance.Imaginary;
                loadVnaPoints = imported.Points;
            }
            var tableMinimum = imported.Points[0].FrequencyHz;
            var tableMaximum = imported.Points[^1].FrequencyHz;
            if (startFrequency.ValueBaseUnit < tableMinimum || stopFrequency.ValueBaseUnit > tableMaximum ||
                startFrequency.ValueBaseUnit >= stopFrequency.ValueBaseUnit)
            {
                startFrequency.ValueBaseUnit = tableMinimum;
                stopFrequency.ValueBaseUnit = tableMaximum;
            }
            UpdateAutomaticSweepRange();
            UpdatePortControls();
            var note = source ? sourceImportNote : loadImportNote;
            if (note is not null)
            {
                note.Text = $"{Path.GetFileName(dialog.FileName)}  -  full VNA curve ({imported.Points.Count} points, {imported.Points[0].FrequencyHz:G6}-{imported.Points[^1].FrequencyHz:G6} Hz)";
                note.Visible = true;
            }            MarkStale();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "VNA Import", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private int? ChooseTouchstonePort(string fileName, bool source)
    {
        using var form = new Form
        {
            Text = "VNA Port",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(360, 150),
            ShowInTaskbar = false
        };
        var label = new Label { Text = Path.GetFileName(fileName), AutoSize = true, Location = new Point(14, 14), MaximumSize = new Size(330, 0) };
        var port = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(14, 48), Width = 330 };
        port.Items.Add("S11 / Port 1");
        port.Items.Add("S22 / Port 2");
        port.SelectedIndex = source ? 1 : 0;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(176, 98), Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(264, 98), Width = 80 };
        form.Controls.Add(label); form.Controls.Add(port); form.Controls.Add(ok); form.Controls.Add(cancel);
        form.AcceptButton = ok; form.CancelButton = cancel;
        return form.ShowDialog(FindForm()) == DialogResult.OK ? port.SelectedIndex : null;
    }
    private Panel BuildSpecificationPage()
    {
        var page = Page();
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
        var attenuationGrid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 4 };
        for (var index = 0; index < 4; index++) attenuationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        InputColumns(attenuationGrid, 260, 1, 3);
        AddPair(attenuationGrid, 0, 0, "Target attenuation (dB):", attenuationTarget);
        AddPair(attenuationGrid, 2, 0, "Allowed error (dB):", attenuationError);
        AddPair(attenuationGrid, 0, 1, "Minimum input RL (dB):", attenuationInputRl);
        AddPair(attenuationGrid, 2, 1, "Minimum output RL (dB):", attenuationOutputRl);
        AddPair(attenuationGrid, 0, 2, "Maximum input power (W):", maxInputPower);
        attenuationGroup.Controls.Add(attenuationGrid);

        var matchingGrid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 4 };
        for (var index = 0; index < 4; index++) matchingGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        InputColumns(matchingGrid, 260, 1, 3);
        AddPair(matchingGrid, 0, 0, "Target input RL (dB):", matchingInputRl);
        AddPair(matchingGrid, 2, 0, "Target output RL (dB):", matchingOutputRl);
        AddPair(matchingGrid, 0, 1, "Maximum insertion loss (dB):", maximumInsertionLoss);
        AddPair(matchingGrid, 2, 1, "Target bandwidth (Hz):", targetBandwidth);
        matchingGroup.Controls.Add(matchingGrid);

        var objectiveGroup = Group("Optimization objective");
        objectiveGroup.Padding = new Padding(8, 30, 8, 8);
        var objectiveRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Padding = new Padding(3, 4, 3, 3) };
        objectiveRow.Controls.Add(objective);
        objectiveGroup.Controls.Add(objectiveRow);
        layout.Controls.Add(attenuationGroup); layout.Controls.Add(matchingGroup); layout.Controls.Add(objectiveGroup);
        page.Controls.Add(layout);
        return page;
    }

    private Panel BuildTopologyPage()
    {
        var page = Page();
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        var left = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        var modeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(650, 0) };
        modeRow.Controls.Add(automaticTopology); modeRow.Controls.Add(manualTopology);
        left.Controls.Add(modeRow);
        var grid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddPair(grid, 0, 0, "Topology family:", topologyFamily);
        AddPair(grid, 0, 1, "Response:", matchingResponse);
        AddPair(grid, 0, 2, "Design Q (Pi/T):", topologyQ);
        grid.Controls.Add(allowMixed, 1, 3);
        left.Controls.Add(grid);
        layout.Controls.Add(left, 0, 0); layout.Controls.Add(topologyPreview, 1, 0);
        automaticTopology.CheckedChanged += (_, _) => { if (automaticTopology.Checked) { manualTopology.Checked = false; UpdateTopologyControls(); } };
        manualTopology.CheckedChanged += (_, _) => { if (manualTopology.Checked) { automaticTopology.Checked = false; UpdateTopologyControls(); } };
        topologyFamily.SelectedIndexChanged += (_, _) => { topologyPreview.Family = SelectedFamily(); UpdateTopologyControls(); };
        matchingResponse.SelectedIndexChanged += (_, _) => { topologyPreview.Response = SelectedResponse(); };
        topologyPreview.Family = TopologyFamily.TPads;
        page.Controls.Add(layout);
        return page;
    }

    private Panel BuildConstraintPage()
    {
        var page = Page();
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(12) };
        var valueGroup = Group("Component value mode");
        var valueRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
        valueMode.SelectedIndexChanged += (_, _) => maximumCombinations.Enabled = valueMode.SelectedIndex != (int)ComponentValueMode.Continuous;
        valueRow.Controls.Add(valueMode);
        valueGroup.Controls.Add(valueRow);

        var resistor = Group("Resistor constraints");
        resistorGroup = resistor;
        var rGrid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 4 };
        for (var index = 0; index < 4; index++) rGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        InputColumns(rGrid, 280, 1, 3);
        AddPair(rGrid, 0, 0, "Minimum R:", minimumResistance);
        AddPair(rGrid, 2, 0, "Maximum R:", maximumResistance);
        AddPair(rGrid, 0, 1, "Max dissipation (W):", maximumResistorPower);
        resistor.Controls.Add(rGrid);

        var capacitor = Group("Capacitor constraints");
        capacitorGroup = capacitor;
        var cGrid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 4 };
        for (var index = 0; index < 4; index++) cGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        InputColumns(cGrid, 280, 1, 3);
        AddPair(cGrid, 0, 0, "Minimum C:", minimumCapacitance);
        AddPair(cGrid, 2, 0, "Maximum C:", maximumCapacitance);
        AddPair(cGrid, 0, 1, "ESR (Ω):", capacitorEsr);
        AddPair(cGrid, 2, 1, "Max voltage (V):", capacitorMaxVoltage);
        AddPair(cGrid, 0, 2, "Max ripple (A):", capacitorMaxRipple);
        AddPair(cGrid, 2, 2, "SRF (MHz):", capacitorSrfMhz);
        capacitor.Controls.Add(cGrid);

        var inductor = Group("Inductor constraints");
        inductorGroup = inductor;
        var lGrid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 4 };
        for (var index = 0; index < 4; index++) lGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        InputColumns(lGrid, 280, 1, 3);
        AddPair(lGrid, 0, 0, "Minimum L:", minimumInductance);
        AddPair(lGrid, 2, 0, "Maximum L:", maximumInductance);
        AddPair(lGrid, 0, 1, "Inductor Q:", inductorQ);
        AddPair(lGrid, 2, 1, "Series R (Ω):", inductorSeriesResistance);
        AddPair(lGrid, 0, 2, "Max RMS (A):", inductorMaxRms);
        AddPair(lGrid, 2, 2, "Saturation (A):", inductorSaturation);
        AddPair(lGrid, 0, 3, "SRF (MHz):", inductorSrfMhz);
        inductor.Controls.Add(lGrid);

        var budget = Group("Search budget");
        var budgetRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
        budgetRow.Controls.Add(new Label { Text = "Maximum combinations:", AutoSize = true, Margin = new Padding(4, 6, 4, 0) }); budgetRow.Controls.Add(maximumCombinations);
        budgetRow.Controls.Add(new Label { Text = "Maximum candidates:", AutoSize = true, Margin = new Padding(16, 6, 4, 0) }); budgetRow.Controls.Add(maximumCandidates);
        budget.Controls.Add(budgetRow);
        layout.Controls.Add(valueGroup); layout.Controls.Add(resistor); layout.Controls.Add(capacitor); layout.Controls.Add(inductor); layout.Controls.Add(budget);
        page.Controls.Add(layout);
        return page;
    }

    private Panel BuildCandidatePage()
    {
        var page = Page();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var command = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        command.Controls.Add(calculate);
        calculate.Click += (_, _) => _ = RunDesignAsync();
        candidateGrid.Dock = DockStyle.Fill;
        candidateGrid.Columns.Add("rank", "Rank"); candidateGrid.Columns.Add("topology", "Topology"); candidateGrid.Columns.Add("status", "Status");
        candidateGrid.Columns.Add("components", "Components"); candidateGrid.Columns.Add("attenuation", "Max IL / Atten dB"); candidateGrid.Columns.Add("rlin", "Worst RL In dB");
        candidateGrid.Columns.Add("rlout", "Worst RL Out dB"); candidateGrid.Columns.Add("bandpass", "Targets");
        candidateGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        candidateGrid.Columns[0].FillWeight = 8; candidateGrid.Columns[1].FillWeight = 30; candidateGrid.Columns[2].FillWeight = 14;
        candidateGrid.Columns[3].FillWeight = 11; candidateGrid.Columns[4].FillWeight = 15; candidateGrid.Columns[5].FillWeight = 15;
        candidateGrid.Columns[6].FillWeight = 15; candidateGrid.Columns[7].FillWeight = 12;
        candidateGrid.SelectionChanged += (_, _) => SelectCandidate();
        layout.Controls.Add(command, 0, 0); layout.Controls.Add(candidateGrid, 0, 1); layout.Controls.Add(previewTabs, 0, 2);

        previewTabs.TabPages.Add(PreviewPage("Schematic", schematicView));
        analysisPage = PreviewPage("Analysis", responsePlot);
        previewTabs.TabPages.Add(analysisPage);
        smithPage = PreviewPage("Smith Chart", smithChart);
        previewTabs.TabPages.Add(smithPage);
        componentGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        componentGrid.Columns.Add("ref", "Reference"); componentGrid.Columns.Add("type", "Type"); componentGrid.Columns.Add("ideal", "Ideal");
        componentGrid.Columns.Add("selected", "Selected"); componentGrid.Columns.Add("unit", "Unit"); componentGrid.Columns.Add("error", "Error %"); componentGrid.Columns.Add("rating", "Rating");
        componentsPage = PreviewPage("Components", componentGrid);
        previewTabs.TabPages.Add(componentsPage);
        netlistPage = PreviewPage("SPICE Netlist", netlistView);
        previewTabs.TabPages.Add(netlistPage);
        reportPage = PreviewPage("Design Report", reportView);
        previewTabs.TabPages.Add(reportPage);
        previewTabs.SelectedIndexChanged += (_, _) => RenderSelectedReportIfNeeded();

        outputStatus.AutoSize = true;
        outputStatus.Dock = DockStyle.Top;
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.Controls.Add(outputStatus, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        AddSpiceButton("LTspice Netlist...", SpiceNetlistDialect.Ltspice);
        AddSpiceButton("Multisim Netlist...", SpiceNetlistDialect.Multisim);
        AddSpiceButton("Generic SPICE...", SpiceNetlistDialect.GenericSpice);
        actions.Controls.Add(ActionButton("HTML Report...", () => ExportReport(html: true)));
        actions.Controls.Add(ActionButton("JSON Snapshot...", () => ExportReport(html: false)));
        actions.Controls.Add(ActionButton("Save Design...", SaveDesign));
        footer.Controls.Add(actions, 0, 1);
        layout.Controls.Add(footer, 0, 3);
        page.Controls.Add(layout);
        return page;

        void AddSpiceButton(string text, SpiceNetlistDialect dialect)
        {
            var button = ActionButton(text, () => ExportNetlist(dialect));
            spiceExportButtons.Add((button, dialect));
            actions.Controls.Add(button);
        }
    }

    private Panel BuildOutputPage()
    {
        var page = Page();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outputStatus.AutoSize = true;
        layout.Controls.Add(outputStatus, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(10, 4) };
        tabs.TabPages.Add(PreviewPage("Schematic", outputSchematic));
        tabs.TabPages.Add(PreviewPage("Analysis", outputResponse));
        tabs.TabPages.Add(PreviewPage("SPICE", outputSpice));
        layout.Controls.Add(tabs, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        actions.Controls.Add(ActionButton("LTspice Netlist...", () => ExportNetlist(SpiceNetlistDialect.Ltspice)));
        actions.Controls.Add(ActionButton("Multisim Netlist...", () => ExportNetlist(SpiceNetlistDialect.Multisim)));
        actions.Controls.Add(ActionButton("Generic SPICE...", () => ExportNetlist(SpiceNetlistDialect.GenericSpice)));
        actions.Controls.Add(ActionButton("HTML Report...", () => ExportReport(html: true)));
        actions.Controls.Add(ActionButton("JSON Snapshot...", () => ExportReport(html: false)));
        actions.Controls.Add(ActionButton("Save Design...", SaveDesign));
        layout.Controls.Add(actions, 0, 2);
        page.Controls.Add(layout);
        return page;
    }
    private static TabPage PreviewPage(string title, Control child)
    {
        var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(4) };
        page.Controls.Add(child); return page;
    }

    private void Advance()
    {
        if (currentIndex == 5 && result is not { Success: true })
        {
            _ = RunDesignAsync();
            return;
        }
        if (currentIndex < pages.Count - 1) ShowStep(currentIndex + 1);
    }

    private async Task RunDesignAsync()
    {
        if (designing) return;
        AttenuationMatchingDesignRequest request;
        try { request = BuildRequest(); }
        catch (Exception exception) { candidateStatus.Text = exception.Message; return; }
        var diagnostics = Validation.DesignRequestValidator.Validate(request);
        var errors = diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length > 0) { MessageBox.Show(this, errors[0].Description, ModuleTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        designing = true;
        calculate.Enabled = false;
        next.Enabled = false;
        candidateStatus.Text = "Designing, selecting topologies and verifying the final circuit…";
        status.Text = "Designing " + ModuleTitle + "…";
        designCancellation = new CancellationTokenSource();
        try
        {
            var computed = await Task.Run(() => new AttenuationMatchingDesignEngine().Design(request), designCancellation.Token);
            if (IsDisposed) return;
            result = computed;
            selectedIndex = 0;
            stale = false;
            status.Text = computed.Message;
            candidateStatus.Text = $"{computed.Candidates.Count} candidate(s); status {computed.Status}.";
            try { RefreshCandidates(); } catch (Exception previewException) { MessageBox.Show(this, previewException.Message, ModuleTitle, MessageBoxButtons.OK, MessageBoxIcon.Error); }
            if (!computed.Success)
            {
                outputStatus.Text = computed.Message;
                var info = computed.Diagnostics.Any(item => item.Code == "DIRECT_CONNECTION_OPTIMAL");
                MessageBox.Show(this, computed.Message, ModuleTitle, MessageBoxButtons.OK, info ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            next.Enabled = true;
        }
        catch (OperationCanceledException) { candidateStatus.Text = "Calculation cancelled."; }
        catch (Exception exception) { result = null; MessageBox.Show(this, exception.Message, ModuleTitle, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally
        {
            designing = false;
            calculate.Enabled = true;
            next.Enabled = currentIndex == 5 ? result is { Success: true } : currentIndex < pages.Count - 1;
            designCancellation?.Dispose();
            designCancellation = null;
        }
    }

    private AttenuationMatchingDesignRequest BuildRequest()
    {
        var mode = SelectedMode();
        var bands = ReadMatchingBands();
        var distributed = mode == AttenuationDesignMode.ImpedanceMatching && selectedMatchingKind != MatchingDesignKind.SingleFrequency;
        if (distributed && bands.Count == 0) throw new InvalidOperationException("Add at least one frequency target row.");
        var referenceFrequency = SelectedReferenceFrequency();
        var analysisStart = startFrequency.ValueBaseUnit;
        var analysisStop = stopFrequency.ValueBaseUnit;
        static PortImpedanceType PortType(double reactance) =>
            reactance > 1e-12 ? PortImpedanceType.SeriesRL : reactance < -1e-12 ? PortImpedanceType.SeriesRC : PortImpedanceType.PureResistance;
        var source = new PortImpedanceModel("Source", (double)sourceR.Value, (double)sourceX.Value,
            sourceVnaPoints is not null ? PortImpedanceType.Vna : PortType((double)sourceX.Value), referenceFrequency, sourceVnaPoints);
        var load = new PortImpedanceModel("Load", (double)loadR.Value, (double)loadX.Value,
            loadVnaPoints is not null ? PortImpedanceType.Vna : PortType((double)loadX.Value), referenceFrequency, loadVnaPoints);
        var components = new ComponentConstraints
        {
            ValueMode = (ComponentValueMode)valueMode.SelectedItem!,
            MinimumResistanceOhm = minimumResistance.ValueBaseUnit,
            MaximumResistanceOhm = maximumResistance.ValueBaseUnit,
            MaximumResistorDissipationWatt = (double)maximumResistorPower.Value,
            MinimumCapacitanceFarad = minimumCapacitance.ValueBaseUnit,
            MaximumCapacitanceFarad = maximumCapacitance.ValueBaseUnit,
            CapacitorEsrOhm = (double)capacitorEsr.Value,
            CapacitorMaximumVoltageVolt = capacitorMaxVoltage.NullableValue,
            CapacitorMaximumRippleCurrentAmpere = capacitorMaxRipple.NullableValue,
            CapacitorSelfResonantFrequencyHz = capacitorSrfMhz.NullableValue is { } capacitorSrf ? capacitorSrf * 1e6 : null,
            MinimumInductanceHenry = minimumInductance.ValueBaseUnit,
            MaximumInductanceHenry = maximumInductance.ValueBaseUnit,
            InductorQ = (double)inductorQ.Value,
            InductorSeriesResistanceOhm = (double)inductorSeriesResistance.Value,
            InductorMaximumRmsCurrentAmpere = inductorMaxRms.NullableValue,
            InductorSaturationCurrentAmpere = inductorSaturation.NullableValue,
            InductorSelfResonantFrequencyHz = inductorSrfMhz.NullableValue is { } inductorSrf ? inductorSrf * 1e6 : null,
            MaximumSearchCombinations = (int)maximumCombinations.Value,
            MaximumCandidateCount = (int)maximumCandidates.Value
        };
        var attenuation = new AttenuationSpecification(
            (double)attenuationTarget.Value, (double)attenuationError.Value,
            (double)attenuationInputRl.Value, (double)attenuationOutputRl.Value,
            (double)maxInputPower.Value);
        var matching = new MatchingSpecification(
            (double)matchingInputRl.Value, (double)matchingOutputRl.Value,
            (double)maximumInsertionLoss.Value, (double)targetBandwidth.Value);
        var topology = new TopologySelection(
            manualTopology.Checked ? TopologySelectionMode.Manual : TopologySelectionMode.Automatic,
            manualTopology.Checked ? SelectedFamily() : null,
            SelectedResponse(), (manualTopology.Checked && (SelectedFamily() is TopologyFamily.PiMatching or TopologyFamily.TMatching)) ? (double)topologyQ.Value : null, allowMixed.Checked);
        return new AttenuationMatchingDesignRequest
        {
            Mode = mode,
            Source = source,
            Load = load,
            DesignFrequencyHz = referenceFrequency,
            StartFrequencyHz = analysisStart,
            StopFrequencyHz = analysisStop,
            SweepPoints = (int)sweepPoints.Value,
            LogarithmicSweep = sweepType.SelectedIndex == 0,
            Signal = new SourceSignal((double)signalValue.Value, (SignalLevelUnit)signalUnit.SelectedItem!),
            Attenuation = attenuation,
            Matching = matching,
            MatchingPlan = new MatchingPlan { Kind = selectedMatchingKind, Bands = bands, MinimumPointsPerBand = 41 },
            Components = components,
            Topology = topology,
            Objective = (OptimizationObjective)objective.SelectedItem!
        };
    }

    private IReadOnlyList<MatchingBand> ReadMatchingBands()
    {
        if (selectedMatchingKind == MatchingDesignKind.SingleFrequency) return [];
        var bands = new List<MatchingBand>();
        foreach (DataGridViewRow row in matchingPlanGrid.Rows)
        {
            if (row.IsNewRow || row.Cells.Cast<DataGridViewCell>().All(cell => string.IsNullOrWhiteSpace(Convert.ToString(cell.Value)))) continue;
            double Value(string name, bool optional = false)
            {
                var text = Convert.ToString(row.Cells[name].Value)?.Trim();
                if (optional && string.IsNullOrEmpty(text)) return double.NaN;
                if (!double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value) &&
                    !double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    throw new InvalidOperationException($"Frequency plan row {row.Index + 1} contains an invalid value in {row.Cells[name].OwningColumn?.HeaderText ?? name}.");
                return value;
            }
            var start = Value(selectedMatchingKind == MatchingDesignKind.MultiFrequency ? "frequency" : "start") * 1e6;
            var stop = selectedMatchingKind == MatchingDesignKind.MultiFrequency ? start : Value("stop") * 1e6;
            var output = Value("rlout", optional: true);
            bands.Add(new MatchingBand(start, stop, Value("rlin"), double.IsNaN(output) ? null : output, Value("il"), Value("weight")));
        }
        return bands;
    }

    private double SelectedReferenceFrequency() => designFrequency.ValueBaseUnit;

    private void UpdateAutomaticSweepRange(IReadOnlyList<MatchingBand>? parsedBands = null)
    {
        if (selectedMode != AttenuationDesignMode.ImpedanceMatching || selectedMatchingKind == MatchingDesignKind.SingleFrequency) return;
        try
        {
            var bands = parsedBands ?? ReadMatchingBands();
            if (bands.Count == 0) return;
            var targetMinimum = bands.Min(band => band.StartFrequencyHz);
            var targetMaximum = bands.Max(band => band.StopFrequencyHz);
            double automaticStart;
            double automaticStop;
            if (targetMaximum <= targetMinimum * (1 + 1e-12))
            {
                automaticStart = targetMinimum * .8;
                automaticStop = targetMaximum * 1.25;
            }
            else
            {
                var marginRatio = Math.Exp(Math.Max(.06, Math.Log(targetMaximum / targetMinimum) * .08));
                automaticStart = targetMinimum / marginRatio;
                automaticStop = targetMaximum * marginRatio;
            }

            double? measuredMinimum = null;
            double? measuredMaximum = null;
            var vnaTables = new[] { sourceVnaPoints, loadVnaPoints }.Where(table => table is { Count: >= 2 }).ToArray();
            if (vnaTables.Length > 0)
            {
                measuredMinimum = vnaTables.Max(table => table![0].FrequencyHz);
                measuredMaximum = vnaTables.Min(table => table![^1].FrequencyHz);
                if (measuredMinimum <= targetMinimum && measuredMaximum >= targetMaximum)
                {
                    automaticStart = Math.Max(automaticStart, measuredMinimum.Value);
                    automaticStop = Math.Min(automaticStop, measuredMaximum.Value);
                }
            }

            const double preferredStepHz = 10e6;
            const double fallbackStepHz = 1e6;
            var roundedStart = Math.Round(automaticStart / preferredStepHz, MidpointRounding.AwayFromZero) * preferredStepHz;
            var roundedStop = Math.Round(automaticStop / preferredStepHz, MidpointRounding.AwayFromZero) * preferredStepHz;
            if (roundedStart <= 0 || roundedStart > targetMinimum)
                roundedStart = Math.Floor(targetMinimum / fallbackStepHz) * fallbackStepHz;
            if (roundedStart <= 0)
                roundedStart = Math.Max(1, Math.Min(automaticStart, targetMinimum));
            if (roundedStop < targetMaximum)
                roundedStop = Math.Ceiling(targetMaximum / preferredStepHz) * preferredStepHz;
            if (roundedStop <= roundedStart)
                roundedStop = Math.Ceiling(automaticStop / preferredStepHz) * preferredStepHz;
            if (roundedStop <= roundedStart)
                roundedStop = roundedStart + preferredStepHz;

            if (measuredMinimum is not null && measuredMaximum is not null &&
                measuredMinimum <= targetMinimum && measuredMaximum >= targetMaximum)
            {
                roundedStart = Math.Max(roundedStart, measuredMinimum.Value);
                roundedStop = Math.Min(roundedStop, measuredMaximum.Value);
            }
            startFrequency.ValueBaseUnit = Math.Min(roundedStart, targetMinimum);
            stopFrequency.ValueBaseUnit = Math.Max(roundedStop, targetMaximum);
        }
        catch (InvalidOperationException)
        {
            // A cell may be temporarily incomplete while the user is editing.
        }
    }

    private void RefreshCandidates()
    {
        if (result is not null && ReferenceEquals(displayedCandidateResult, result) && candidateGrid.Rows.Count == result.Candidates.Count)
            return;
        refreshingCandidates = true;
        previewCandidateId = null;
        renderedReportCandidateId = null;
        try
        {
            candidateGrid.Rows.Clear();
            if (result is null) { outputStatus.Text = "No design."; netlistView.Text = string.Empty; reportView.DocumentText = string.Empty; return; }
            foreach (var candidate in result.Candidates)
            {
                var bandMetrics = candidate.Sweep.BandMetrics;
                var attenuationValue = bandMetrics.Count > 0 ? bandMetrics.Max(item => item.MaximumInsertionLossDb) : candidate.Metrics.AttenuationDb;
                var inputValue = bandMetrics.Count > 0 ? bandMetrics.Min(item => item.WorstInputReturnLossDb) : candidate.Metrics.InputReturnLossDb;
                var outputValue = bandMetrics.Count > 0 ? bandMetrics.Min(item => item.WorstOutputReturnLossDb) : candidate.Metrics.OutputReturnLossDb;
                var attenuation = Math.Abs(attenuationValue) < 1e-6 ? "0" : attenuationValue.ToString("0.###");
                var inputReturnLoss = double.IsFinite(inputValue) ? inputValue.ToString("0.#") : "N/A";
                var outputReturnLoss = double.IsFinite(outputValue) ? outputValue.ToString("0.#") : "N/A";
                var planStatus = bandMetrics.Count == 0 ? "—" : bandMetrics.All(item => item.Passed) ? "Pass" : "Fail";
                var index = candidateGrid.Rows.Add(candidate.Rank, candidate.TopologyName, candidate.Status.ToString(), candidate.Components.Count,
                    attenuation, inputReturnLoss, outputReturnLoss, planStatus);
                candidateGrid.Rows[index].Tag = candidate;
            }
            var firstSelectableIndex = result.Candidates.ToList().FindIndex(candidate => candidate.Status != DesignStatus.Failed);
            selectedIndex = firstSelectableIndex;
            if (firstSelectableIndex >= 0 && candidateGrid.Rows.Count > firstSelectableIndex)
                candidateGrid.Rows[firstSelectableIndex].Selected = true;
            else
                candidateGrid.ClearSelection();
        }
        finally
        {
            refreshingCandidates = false;
            displayedCandidateResult = result;
        }
        var selectableIndex = selectedIndex;
        if (candidateGrid.Rows.Count == 0)
        {
            outputStatus.Text = result.Message;
            netlistView.Text = string.Empty;
            reportView.DocumentText = string.Empty;
        }
        if (selectableIndex < 0)
        {
            outputStatus.Text = result.Message;
            schematicView.Document = null;
            responsePlot.Sweep = null;
            smithChart.Sweep = null;
            componentGrid.Rows.Clear();
            netlistView.Text = string.Empty;
            reportView.DocumentText = string.Empty;
            return;
        }
        SelectCandidate();
    }

    private void SelectCandidate()
    {
        if (refreshingCandidates) return;
        if (candidateGrid.SelectedRows.Count == 0 || candidateGrid.SelectedRows[0].Tag is not TopologyCandidate candidate) return;
        selectedIndex = candidateGrid.SelectedRows[0].Index;
        if (previewCandidateId == candidate.Id)
        {
            RenderSelectedReportIfNeeded();
            return;
        }
        try
        {
            schematicView.Document = new AttenuationMatchingLayoutService().Layout(candidate);
            schematicView.Zoom = 1.0;
            responsePlot.Sweep = candidate.Sweep;
            responsePlot.DesignFrequencyHz = result?.Request.DesignFrequencyHz ?? 0;
            responsePlot.Request = result?.Request;
            smithChart.Sweep = candidate.Sweep;
            smithChart.ReferenceImpedanceOhm = candidate.Metrics.SourceImpedance.Real > 0 ? candidate.Metrics.SourceImpedance.Real : 50;
            smithChart.DesignFrequencyHz = result?.Request.DesignFrequencyHz ?? 0;
            smithChart.Request = result?.Request;
            UpdatePreviewTabs(candidate);
            componentGrid.Rows.Clear();
            foreach (var component in candidate.Components)
                componentGrid.Rows.Add(component.Reference, component.TypeName, component.IdealValue.ToString("G6"), component.SelectedValue.ToString("G6"), component.Unit, component.ErrorPercent.ToString("G5"), component.Rating);
            netlistView.Text = new AttenuationMatchingSpiceExportService().ExportDesign(candidate, result!.Request);
            previewCandidateId = candidate.Id;
            renderedReportCandidateId = null;
            reportView.DocumentText = string.Empty;
            UpdateSpiceExportButtons();
            RenderSelectedReportIfNeeded();
            outputStatus.Text = candidate.Status + " - " + candidate.TopologyName;
        }
        catch (Exception exception)
        {
            netlistView.Text = string.Empty;
            reportView.DocumentText = string.Empty;
            outputStatus.Text = exception.Message;
        }
    }

    private void RenderSelectedReportIfNeeded()
    {
        if (reportPage is null || previewTabs.SelectedTab != reportPage || SelectedCandidate is not { } candidate || result is null) return;
        if (renderedReportCandidateId == candidate.Id) return;
        reportView.DocumentText = new AttenuationMatchingDesignReportService().ExportHtml(ResultWithSelection());
        renderedReportCandidateId = candidate.Id;
    }

    private void UpdateSpiceExportButtons()
    {
        if (result is null) return;
        var testbench = AttenuationMatchingSpiceExportService.SupportsTestbench(result.Request);
        foreach (var (button, dialect) in spiceExportButtons)
        {
            var name = dialect switch
            {
                SpiceNetlistDialect.Ltspice => "LTspice",
                SpiceNetlistDialect.Multisim => "Multisim",
                _ => "Generic SPICE"
            };
            button.Text = testbench ? $"{name} Netlist..." : $"{name} Design Netlist...";
        }
    }
    private static bool IsAttenuationOnly(TopologyCandidate candidate) =>
        candidate.Family is TopologyFamily.DirectConnection or TopologyFamily.TPads or TopologyFamily.PiPads or TopologyFamily.LPads or TopologyFamily.CascadedPads;

    private static bool HasMeaningfulSmith(TopologyCandidate candidate)
    {
        var points = candidate.Sweep.Points;
        if (points.Count < 2) return false;
        var magnitudes = points.Select(point => Math.Sqrt(point.InputReflectionCoefficient.Real * point.InputReflectionCoefficient.Real +
                                                             point.InputReflectionCoefficient.Imaginary * point.InputReflectionCoefficient.Imaginary)).ToArray();
        return magnitudes.Max() > .05 && magnitudes.Max() - magnitudes.Min() > .01;
    }

    private void SetPreviewTab(TabPage? page, bool visible, int index)
    {
        if (page is null) return;
        var present = previewTabs.TabPages.Contains(page);
        if (visible && !present)
        {
            previewTabs.TabPages.Insert(Math.Min(index, previewTabs.TabPages.Count), page);
        }
        else if (!visible && present)
        {
            var selected = previewTabs.SelectedTab == page;
            previewTabs.TabPages.Remove(page);
            if (selected) previewTabs.SelectedIndex = 0;
        }
    }

    private void UpdatePreviewTabs(TopologyCandidate candidate)
    {
        var attenuationOnly = IsAttenuationOnly(candidate);
        SetPreviewTab(analysisPage, !attenuationOnly, 1);
        SetPreviewTab(smithPage, !attenuationOnly && HasMeaningfulSmith(candidate), 2);
    }
    private void RefreshOutput()
    {
        var selected = SelectedCandidate;
        outputStatus.ForeColor = SystemColors.ControlText;
        outputStatus.Text = result is null ? "No design." : $"{result.Status} — {selected?.TopologyName ?? "no candidate"}";
        if (selected is null)
        {
            outputSchematic.Document = null;
            return;
        }
        outputSchematic.Document = new AttenuationMatchingLayoutService().Layout(selected);
        outputSchematic.Zoom = 1.0;
        outputResponse.Sweep = selected.Sweep;
        outputResponse.DesignFrequencyHz = result?.Request.DesignFrequencyHz ?? 0;
        outputResponse.Request = result?.Request;
        outputSpice.Text = new AttenuationMatchingSpiceExportService().ExportDesign(selected, result!.Request);
    }
    private void ExportNetlist(SpiceNetlistDialect dialect)
    {
        if (!EnsureExportable()) return;
        using var dialog = new SaveFileDialog
        {
            Filter = dialect switch
            {
                SpiceNetlistDialect.Ltspice => "LTspice netlist (*.cir)|*.cir|All files (*.*)|*.*",
                SpiceNetlistDialect.Multisim => "Multisim/Berkeley netlist (*.net)|*.net|All files (*.*)|*.*",
                _ => "SPICE netlist (*.cir)|*.cir|All files (*.*)|*.*"
            },
            DefaultExt = dialect == SpiceNetlistDialect.Multisim ? "net" : "cir",
            AddExtension = true,
            FileName = $"{(result!.Request.Mode == AttenuationDesignMode.Attenuation ? "attenuator" : "matching")}-{dialect}.cir"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var service = new AttenuationMatchingSpiceExportService();
            var testbench = AttenuationMatchingSpiceExportService.SupportsTestbench(result!.Request);
            if (testbench)
                service.SaveTestbench(SelectedCandidate!, result.Request, dialog.FileName, dialect);
            else
                service.SaveDesign(SelectedCandidate!, result.Request, dialog.FileName, dialect);
            MessageBox.Show(this, (testbench ? "Testbench" : "Passive design netlist") + " exported successfully.\r\n" + dialog.FileName,
                "SPICE Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "SPICE Export", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ExportReport(bool html)
    {
        if (!EnsureExportable()) return;
        using var dialog = new SaveFileDialog
        {
            Filter = html ? "HTML Report (*.html)|*.html|All files (*.*)|*.*" : "JSON Snapshot (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = html ? "html" : "json", AddExtension = true,
            FileName = $"signal-{result!.Request.Mode}-report.{(html ? "html" : "json")}"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var service = new AttenuationMatchingDesignReportService();
            if (html) service.SaveHtml(ResultWithSelection(), dialog.FileName); else service.SaveJson(ResultWithSelection(), dialog.FileName);
            MessageBox.Show(this, "Report exported successfully.\r\n" + dialog.FileName, "Design Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Design Report", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveDesign()
    {
        if (!EnsureExportable()) return;
        using var dialog = new SaveFileDialog
        {
            Filter = DesignDocumentService.FileFilter, DefaultExt = DesignDocumentService.Extension, AddExtension = true,
            FileName = $"signal-matching-{DateTime.Now:yyyyMMdd-HHmm}{DesignDocumentService.Extension}"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var document = DesignDocumentService.Create(this, ModuleTitle, DateTimeOffset.UtcNow);
            new DesignDocumentService().Save(document, dialog.FileName);
            MessageBox.Show(this, "Design saved successfully.\r\n" + dialog.FileName, "Save Design", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception) { MessageBox.Show(this, exception.Message, "Save Design", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private bool EnsureExportable()
    {
        if (stale) { MessageBox.Show(this, "Parameters changed; calculate again before exporting.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information); return false; }
        if (SelectedCandidate is { Status: not DesignStatus.Failed } && result is { Success: true }) return true;
        MessageBox.Show(this, "Calculate and verify a successful candidate before exporting.", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void MarkStale()
    {
        if (initializing || result is null) return;
        stale = true;
        status.Text = "Parameters changed — calculate again.";
        status.ForeColor = SystemColors.ControlText;
        next.Enabled = currentIndex < pages.Count - 1 && currentIndex != 5;
        outputStatus.Text = string.Empty;
        outputStatus.ForeColor = SystemColors.ControlText;
    }

    private void HookInvalidation(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            switch (child)
            {
                case NumericUpDown number: number.ValueChanged += (_, _) => MarkStale(); break;
                case ComboBox combo: combo.SelectedIndexChanged += (_, _) => MarkStale(); break;
                case CheckBox check: check.CheckedChanged += (_, _) => MarkStale(); break;
                case TextBox text: text.TextChanged += (_, _) => MarkStale(); break;
                case EngineeringNumberInput engineering: engineering.ValueChanged += (_, _) => MarkStale(); break;
            }
            HookInvalidation(child);
        }
    }

    private void UpdateModeVisibility()
    {
        var mode = SelectedMode();
        attenuationGroup.Visible = mode is AttenuationDesignMode.Attenuation;
        matchingGroup.Visible = mode is AttenuationDesignMode.ImpedanceMatching && selectedMatchingKind == MatchingDesignKind.SingleFrequency;
        if (frequencyGroup is not null) frequencyGroup.Visible = mode is AttenuationDesignMode.ImpedanceMatching;
        if (resistorGroup is not null) resistorGroup.Visible = mode is AttenuationDesignMode.Attenuation;
        if (capacitorGroup is not null) capacitorGroup.Visible = mode is AttenuationDesignMode.ImpedanceMatching;
        if (inductorGroup is not null) inductorGroup.Visible = mode is AttenuationDesignMode.ImpedanceMatching;
        var distributed = mode == AttenuationDesignMode.ImpedanceMatching && selectedMatchingKind != MatchingDesignKind.SingleFrequency;
        matchingPlanPanel.Visible = distributed;
        if (referenceGroup is not null) referenceGroup.Visible = mode is AttenuationDesignMode.ImpedanceMatching;
        targetBandwidth.Enabled = !distributed;
        UpdatePortControls();
        UpdateTopologyFamilies();
    }

    private void ConfigureMatchingPlanGrid()
    {
        if (matchingPlanGrid.IsDisposed) return;
        var existingKind = matchingPlanGrid.Tag is MatchingDesignKind kind ? kind : (MatchingDesignKind?)null;
        // Do not query Control.Visible here: mode selection happens while the
        // Source & Load page is hidden, and WinForms then reports child controls
        // as not visible even when their local Visible flag is true.
        var shouldShow = selectedMode == AttenuationDesignMode.ImpedanceMatching && selectedMatchingKind != MatchingDesignKind.SingleFrequency;
        matchingPlanPanel.Visible = shouldShow;
        if (!shouldShow || existingKind == selectedMatchingKind) return;
        configuringMatchingPlanGrid = true;
        try
        {
            matchingPlanGrid.Columns.Clear();
            matchingPlanGrid.Rows.Clear();
            if (selectedMatchingKind == MatchingDesignKind.MultiFrequency)
            {
                matchingPlanGrid.Columns.Add("frequency", "Frequency (MHz)");
                matchingPlanHint.Text = "Multi-frequency targets — enter one frequency per row; use the blank last row to add another point.";
            }
            else
            {
                matchingPlanGrid.Columns.Add("start", "Start (MHz)");
                matchingPlanGrid.Columns.Add("stop", "Stop (MHz)");
                matchingPlanHint.Text = "Broadband targets — enter the start and stop frequency of each required band.";
            }
            matchingPlanGrid.Columns.Add("rlin", "Min RL In (dB)");
            matchingPlanGrid.Columns.Add("rlout", "Min RL Out (dB)");
            matchingPlanGrid.Columns.Add("il", "Max IL (dB)");
            matchingPlanGrid.Columns.Add("weight", "Weight");
            if (selectedMatchingKind == MatchingDesignKind.MultiFrequency)
            {
                matchingPlanGrid.Rows.Add(10d, (double)matchingInputRl.Value, (double)matchingOutputRl.Value, (double)maximumInsertionLoss.Value, 1d);
                matchingPlanGrid.Rows.Add(20d, (double)matchingInputRl.Value, (double)matchingOutputRl.Value, (double)maximumInsertionLoss.Value, 1d);
            }
            else
                matchingPlanGrid.Rows.Add(10d, 20d, (double)matchingInputRl.Value, (double)matchingOutputRl.Value, (double)maximumInsertionLoss.Value, 1d);
            matchingPlanGrid.Tag = selectedMatchingKind;
        }
        finally
        {
            configuringMatchingPlanGrid = false;
        }
        UpdateAutomaticSweepRange();
    }

    private void MatchingPlanChanged(DataGridViewCellEventArgs? eventArgs = null)
    {
        if (configuringMatchingPlanGrid) return;
        var frequencyChanged = eventArgs is null;
        if (eventArgs is { ColumnIndex: >= 0 } && eventArgs.ColumnIndex < matchingPlanGrid.Columns.Count)
        {
            var columnName = matchingPlanGrid.Columns[eventArgs.ColumnIndex].Name;
            frequencyChanged = columnName is "frequency" or "start" or "stop";
        }
        if (frequencyChanged) UpdateAutomaticSweepRange();
        MarkStale();
    }

    private void UpdateTopologyFamilies()
    {
        if (initializing) return;
        var previous = SelectedFamily();
        var families = SelectedMode() switch
        {
            AttenuationDesignMode.Attenuation => new[] { TopologyFamily.TPads, TopologyFamily.PiPads, TopologyFamily.LPads, TopologyFamily.CascadedPads },
            AttenuationDesignMode.ImpedanceMatching when selectedMatchingKind != MatchingDesignKind.SingleFrequency => new[] { TopologyFamily.PiMatching, TopologyFamily.TMatching },
            _ => new[] { TopologyFamily.LMatching, TopologyFamily.PiMatching, TopologyFamily.TMatching }
        };
        topologyFamily.Items.Clear();
        foreach (var family in families) topologyFamily.Items.Add(family);
        if (previous is { } selected && families.Contains(selected)) topologyFamily.SelectedItem = selected; else topologyFamily.SelectedIndex = 0;
        topologyPreview.Family = SelectedFamily();
        UpdateTopologyControls();
    }

    private void UpdatePortControls()
    {
        var complexPort = Math.Abs((double)sourceX.Value) > 1e-12 || Math.Abs((double)loadX.Value) > 1e-12;
        if (SelectedMode() == AttenuationDesignMode.Attenuation && complexPort)
        {
            SelectMode(AttenuationDesignMode.ImpedanceMatching);
            return;
        }
        sourceR.Enabled = true;
        loadR.Enabled = true;
        sourceX.Enabled = SelectedMode() == AttenuationDesignMode.ImpedanceMatching || Math.Abs((double)sourceX.Value) <= 1e-12;
        loadX.Enabled = SelectedMode() == AttenuationDesignMode.ImpedanceMatching || Math.Abs((double)loadX.Value) <= 1e-12;
        maximumCombinations.Enabled = valueMode.SelectedIndex != (int)ComponentValueMode.Continuous;
    }
    private void UpdateTopologyControls()
    {
        var manual = manualTopology.Checked;
        topologyFamily.Enabled = manual;
        matchingResponse.Enabled = SelectedFamily() is TopologyFamily.PiMatching or TopologyFamily.TMatching;
        topologyQ.Enabled = manual && (SelectedFamily() is TopologyFamily.PiMatching or TopologyFamily.TMatching);
        allowMixed.Enabled = SelectedFamily() is TopologyFamily.PiMatching or TopologyFamily.TMatching;
        topologyHint.Text = manual ? ManualTopologyHint() : "Automatic mode enumerates every physically valid high-side L form and every Pi/T response, then filters hard constraints before ranking.";
        topologyPreview.Family = SelectedFamily();
        topologyPreview.Response = SelectedResponse();
    }

    private string ManualTopologyHint() => SelectedFamily() switch
    {
        TopologyFamily.LMatching => "A two-element L match needs the shunt element on the higher-resistance side. The opposite position is reported infeasible rather than silently swapped.",
        TopologyFamily.PiMatching => "Pi: shunt elements at both ports and a series element between them. Design Q chooses the virtual resistance; loaded Q is measured from the final sweep.",
        TopologyFamily.TMatching => "T: series elements at both ports and a shunt element at the center. Design Q chooses the virtual resistance; loaded Q is measured from the final sweep.",
        TopologyFamily.TPads or TopologyFamily.PiPads => "A resistive pad is selected only if the requested attenuation is at or above the minimum realizable loss for the port ratio.",
        TopologyFamily.LPads => "An L-pad has only two resistors; its loss is fixed by the impedance ratio. It cannot be freely assigned an arbitrary attenuation.",
        TopologyFamily.CascadedPads => "Multi-stage matched sections reduce resistor stress at high attenuation; the complete cascade is re-analyzed.",
        _ => string.Empty
    };

    private TopologyFamily SelectedFamily() => topologyFamily.SelectedItem is TopologyFamily family ? family : TopologyFamily.TPads;
    private MatchingResponse SelectedResponse() => matchingResponse.SelectedItem is MatchingResponse response ? response : MatchingResponse.LowPass;
    private AttenuationDesignMode SelectedMode() => selectedMode;

    private static Panel Page() => new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = SystemColors.Control };
    private static GroupBox Group(string title) => new() { Text = title, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(8, 22, 8, 6), Margin = new Padding(4, 3, 4, 5) };
    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, int decimals) => new()
    {
        Minimum = minimum, Maximum = maximum, Value = value, DecimalPlaces = decimals,
        Width = UiFieldMetrics.NumberWidth(minimum, maximum, decimals, 150, 240), ThousandsSeparator = true, AutoSize = false, Margin = new Padding(3)
    };
    private static ComboBox Choice<T>(IEnumerable<T> values)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, AutoSize = false, Margin = new Padding(3) };
        foreach (var value in values) box.Items.Add(value!);
        box.SelectedIndex = 0;
        box.FormattingEnabled = true;
        if (typeof(T).IsEnum) box.Format += (_, e) => e.Value = Spaced(e.Value?.ToString());
        UiFieldMetrics.FitCombo(box, 150, 260);
        return box;
    }
    private static void InputColumns(TableLayoutPanel table, int width, params int[] columns)
    {
        foreach (var column in columns)
        {
            if (column < 0 || column >= table.ColumnStyles.Count) continue;
            table.ColumnStyles[column] = new ColumnStyle(SizeType.Absolute, width);
        }
    }
    private static Label AddPair(TableLayoutPanel table, int column, int row, string label, Control control)
    {
        var caption = new Label { Text = label, AutoSize = true, Margin = new Padding(4, 7, 4, 0) };
        table.Controls.Add(caption, column, row);

        table.Controls.Add(control, column + 1, row);
        return caption;
    }
    private static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
            BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            EnableHeadersVisualStyles = false
        };
        grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
        grid.DefaultCellStyle.SelectionBackColor = SystemColors.ControlLight;
        grid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        return grid;
    }
    private static RichTextBox TextView() => new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F), BackColor = Color.White, WordWrap = false };
    private static Button ActionButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(120, 29), Margin = new Padding(4) };
        button.Click += (_, _) => action(); return button;
    }
    private static string Spaced(string? value) => string.IsNullOrEmpty(value) ? string.Empty : System.Text.RegularExpressions.Regex.Replace(value, "(?<!^)([A-Z])", " $1");
    private static string FormatComplex(System.Numerics.Complex value) =>
        $"{value.Real:G6} {(value.Imaginary >= 0 ? "+" : "−")} j{Math.Abs(value.Imaginary):G6} Ω";

    private AttenuationMatchingDesignRequest DefaultRequest() => new();
}

internal static class AttenuationMatchingCollectionExtensions
{
    public static int IndexOf<T>(this IReadOnlyList<T> items, T value)
    {
        for (var index = 0; index < items.Count; index++) if (EqualityComparer<T>.Default.Equals(items[index], value)) return index;
        return 0;
    }
}
