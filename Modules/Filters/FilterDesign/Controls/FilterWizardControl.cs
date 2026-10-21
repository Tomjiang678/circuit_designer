using System.Diagnostics;
using System.Text;
using CircuitModuleDesigner.Controls;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;
using CircuitModuleDesigner.Services;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterWizardControl : UserControl, IDesignModuleHost
{
    private readonly TreeView navigation;
    private readonly FilterDesignState state = new();
    private readonly Panel pageHost = new();
    private readonly Label stepTitle = new();
    private readonly Button backButton = new();
    private readonly Button nextButton = new();
    private FilterDesignResult? lastResult;
    private bool resultIsStale;
    private readonly string[] titles =
    {
        "1. Filter Type",
        "2. Specifications",
        "3. Approximation",
        "4. Implementation",
        "5. Impedance / Scaling",
        "6. Components",
        "7. Summary",
        "8. Design Result"
    };
    private int currentIndex;
    private bool updatingNavigation;
    private bool designInProgress;
    private int stateRevision;
    private readonly Dictionary<int, FilterStepBase> stepCache = [];
    private FilterDesignOutputControl? outputControl;

    public FilterDesignState State => state;

    public string ModuleTitle => "Filters";
    public bool HasDesign => lastResult is { IsImplemented: true };

    public string BuildDesignJson()
    {
        if (lastResult is not { IsImplemented: true } result) return string.Empty;
        var report = new FilterDesignReportService().Build(result);
        return new JsonFilterDesignSnapshotExporter().Export(report.Snapshot);
    }

    public string BuildDesignSummary()
    {
        if (lastResult is not { IsImplemented: true } result) return string.Empty;
        var summary = new StringBuilder()
            .AppendLine($"Filter design: {result.Status}")
            .AppendLine($"Message: {result.Message}")
            .AppendLine($"Order: required {result.RequiredOrder}, prototype {result.PrototypeOrder}, realized {result.RealizedOrder}")
            .AppendLine($"Sections: {result.Sections.Count}")
            .AppendLine($"Approximation: {result.ApproximationDescription}");
        if (result.Warnings.Count > 0)
        {
            summary.AppendLine("Warnings:");
            foreach (var warning in result.Warnings) summary.AppendLine("  - " + warning);
        }
        return summary.ToString();
    }

    public string? BuildNetlist() =>
        lastResult is { IsImplemented: true } result && !string.IsNullOrWhiteSpace(result.SpiceNetlist)
            ? result.SpiceNetlist
            : null;

    public void Calculate() => ExecuteDesign();

    public void DetachNavigation() => navigation.AfterSelect -= NavigationAfterSelect;

    public FilterWizardControl(TreeView navigation)
    {
        this.navigation = navigation;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Dock = DockStyle.Fill;
        BackColor = SystemColors.Control;
        BuildLayout();
        PopulateNavigation();
        ShowStep(0);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        stepTitle.AutoSize = true;
        stepTitle.Dock = DockStyle.Fill;
        stepTitle.Font = new Font(Font, FontStyle.Bold);
        stepTitle.Padding = new Padding(8, 7, 8, 7);
        stepTitle.BackColor = SystemColors.Control;

        pageHost.Dock = DockStyle.Fill;
        pageHost.Margin = Padding.Empty;
        pageHost.Padding = new Padding(0, 0, 0, 4);

        var commandBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(6),
            Margin = Padding.Empty
        };
        nextButton.Text = "Next >";
        nextButton.AutoSize = true;
        nextButton.MinimumSize = new Size(90, 28);
        nextButton.Click += (_, _) => ShowStep(currentIndex + 1);
        backButton.Text = "< Back";
        backButton.AutoSize = true;
        backButton.MinimumSize = new Size(90, 28);
        backButton.Click += (_, _) => ShowStep(Math.Max(0, currentIndex - 1));
        commandBar.Controls.Add(nextButton);
        commandBar.Controls.Add(backButton);

        root.Controls.Add(stepTitle, 0, 0);
        root.Controls.Add(pageHost, 0, 1);
        root.Controls.Add(commandBar, 0, 2);
        Controls.Add(root);
    }

    private void PopulateNavigation()
    {
        updatingNavigation = true;
        navigation.BeginUpdate();
        navigation.Nodes.Clear();
        foreach (var title in titles)
            navigation.Nodes.Add(new TreeNode(title));
        navigation.SelectedNode = navigation.Nodes[0];
        navigation.EndUpdate();
        updatingNavigation = false;
        navigation.AfterSelect += NavigationAfterSelect;
    }

    private void NavigationAfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (updatingNavigation || e.Node == null)
            return;
        if (e.Node.Index < titles.Length - 1 || lastResult != null)
            ShowStep(e.Node.Index);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            DetachNavigation();
        base.Dispose(disposing);
    }

    private void ShowStep(int requestedIndex)
    {
        var maximumIndex = lastResult == null ? titles.Length - 2 : titles.Length - 1;
        currentIndex = Math.Clamp(requestedIndex, 0, maximumIndex);
        stepTitle.Text = $"Filter Design Wizard - {titles[currentIndex]}";

        pageHost.SuspendLayout();
        foreach (Control control in pageHost.Controls) control.Visible = false;
        if (currentIndex == titles.Length - 1)
        {
            outputControl ??= new FilterDesignOutputControl(lastResult!);
            if (outputControl.Parent is null) pageHost.Controls.Add(outputControl);
            outputControl.Visible = true;
        }
        else
        {
            var step = GetOrCreateStep(currentIndex);
            step.Visible = true;
        }
        pageHost.ResumeLayout(false);
        pageHost.PerformLayout();

        backButton.Enabled = currentIndex > 0;
        nextButton.Enabled = currentIndex < titles.Length - 2 || currentIndex == titles.Length - 2 && lastResult != null;
        nextButton.Text = currentIndex == titles.Length - 2 && lastResult != null ? "View Result >" : "Next >";
        UpdateNavigationState();
    }

    private FilterStepBase CreateStep(int stepIndex) => stepIndex switch
    {
        0 => new FilterTypeStepControl(state),
        1 => new FilterSpecificationsStepControl(state),
        2 => new FilterApproximationStepControl(state),
        3 => new FilterImplementationStepControl(state),
        4 => new FilterScalingStepControl(state),
        5 => new FilterComponentsStepControl(state),
        _ => new FilterSummaryControl(state)
    };

    private FilterStepBase GetOrCreateStep(int stepIndex)
    {
        if (stepCache.TryGetValue(stepIndex, out var existing)) return existing;
        var step = CreateStep(stepIndex);
        step.StateChanged += (_, _) =>
        {
            stateRevision++;
            if (lastResult is not null) resultIsStale = true;
            InvalidateStepsAfter(currentIndex);
            if (currentIndex == 0) ShowStep(0); else UpdateNavigationState();
        };
        if (step is FilterSummaryControl summary) summary.DesignRequested += (_, _) => ExecuteDesign();
        stepCache.Add(stepIndex, step); pageHost.Controls.Add(step); return step;
    }

    private void InvalidateStepsAfter(int stepIndex)
    {
        foreach (var key in stepCache.Keys.Where(key => key > stepIndex).ToArray())
        {
            var obsolete = stepCache[key];
            stepCache.Remove(key); pageHost.Controls.Remove(obsolete); obsolete.Dispose();
        }
    }

    private async void ExecuteDesign()
    {
        if (designInProgress) return;
        var totalWatch = Stopwatch.StartNew();
        designInProgress = true;
        var revision = stateRevision;
        var buildWatch = Stopwatch.StartNew();
        var request = FilterDesignRequest.FromState(state);
        buildWatch.Stop();
        UseWaitCursor = true;
        nextButton.Enabled = false;
        try
        {
            var coreWatch = Stopwatch.StartNew();
            var result = await Task.Run(() => new FilterDesignEngine().Design(request));
            coreWatch.Stop();
            if (IsDisposed) return;
            if (revision != stateRevision)
            {
                MessageBox.Show(this, "Filter settings changed while the design was running. The stale result was discarded; run Design Filter again.",
                    "Filter Design", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!result.IsImplemented)
            {
                var capability = result.Capability;
                var details = $"Status: {result.Status}\r\n" +
                              $"CapabilityLevel: {capability?.Level.ToString() ?? "Not recorded"}\r\n" +
                              $"CapabilityReasonCode: {capability?.ReasonCode.ToString() ?? "Not recorded"}\r\n\r\n" +
                              result.Message;
                if (!string.IsNullOrWhiteSpace(capability?.SuggestedAlternative))
                    details += $"\r\n\r\nSuggested alternative: {capability.SuggestedAlternative}";
                MessageBox.Show(this, details, "Filter Design", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            lastResult = result;
            resultIsStale = false;
            var renderWatch = Stopwatch.StartNew();
            outputControl?.UpdateResult(result);
            ShowStep(titles.Length - 1);
            renderWatch.Stop(); totalWatch.Stop();
            UiPerformanceTrace.Design("Filter Design", buildWatch.Elapsed, coreWatch.Elapsed, renderWatch.Elapsed, totalWatch.Elapsed);
        }
        catch (Exception exception)
        {
            if (!IsDisposed)
                MessageBox.Show(this, exception.Message, "Filter Design", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                designInProgress = false;
                UseWaitCursor = false;
                nextButton.Enabled = currentIndex < titles.Length - 2 || currentIndex == titles.Length - 2 && lastResult != null;
            }
        }
    }

    private void UpdateNavigationState()
    {
        updatingNavigation = true;
        for (var i = 0; i < navigation.Nodes.Count; i++)
        {
            navigation.Nodes[i].Text = i == titles.Length - 1 && resultIsStale
                ? titles[i] + " (settings changed)"
                : i < currentIndex ? "\u2713 " + titles[i] : titles[i];
            navigation.Nodes[i].ForeColor = i == titles.Length - 1 && lastResult == null ? SystemColors.GrayText : SystemColors.WindowText;
        }
        if (navigation.Nodes.Count > currentIndex)
            navigation.SelectedNode = navigation.Nodes[currentIndex];
        updatingNavigation = false;
    }
}
