using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterWizardControl : UserControl
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

    public FilterDesignState State => state;

    public FilterWizardControl(TreeView navigation)
    {
        this.navigation = navigation;
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

    private void ShowStep(int requestedIndex)
    {
        var maximumIndex = lastResult == null ? titles.Length - 2 : titles.Length - 1;
        currentIndex = Math.Clamp(requestedIndex, 0, maximumIndex);
        stepTitle.Text = $"Filter Design Wizard - {titles[currentIndex]}";

        pageHost.SuspendLayout();
        pageHost.Controls.Clear();
        if (currentIndex == titles.Length - 1)
        {
            pageHost.Controls.Add(new FilterDesignOutputControl(lastResult!));
        }
        else
        {
            var step = CreateStep(currentIndex);
            step.StateChanged += (_, _) =>
            {
                stateRevision++;
                if (lastResult != null) resultIsStale = true;
                if (currentIndex == 0) ShowStep(0); else UpdateNavigationState();
            };
            if (step is FilterSummaryControl summary)
                summary.DesignRequested += (_, _) => ExecuteDesign();
            pageHost.Controls.Add(step);
        }
        pageHost.ResumeLayout(true);

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

    private async void ExecuteDesign()
    {
        if (designInProgress) return;
        designInProgress = true;
        var revision = stateRevision;
        var request = FilterDesignRequest.FromState(state);
        UseWaitCursor = true;
        nextButton.Enabled = false;
        try
        {
            var result = await Task.Run(() => new FilterDesignEngine().Design(request));
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
            ShowStep(titles.Length - 1);
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
