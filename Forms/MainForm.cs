using System.Drawing;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

namespace CircuitModuleDesigner.Forms;

public sealed class MainForm : Form
{
    private readonly ComboBox categoryCombo = new();
    private readonly TreeView navigationTree = new();
    private Label? navigationHint;
    private Panel? designHost;
    private SplitContainer? mainSplit;
    private PrecisionAnalogWizardControl? precisionAnalogWizard;

    public MainForm()
    {
        Text = "Circuit Module Designer";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(1024, 700);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);
        BackColor = SystemColors.Control;
        Icon = SystemIcons.Application;
        BuildInterface();
        Shown += (_, _) =>
        {
            BeginInvoke(ConfigureSplitter);
            Application.Idle += PreloadPrecisionAnalogOnIdle;
        };
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var menu = CreateMenu(); var selection = CreateSelection(); var work = CreateWorkArea();
        root.Controls.Add(menu, 0, 0); root.Controls.Add(selection, 0, 1); root.Controls.Add(work, 0, 2); root.Controls.Add(CreateStatusBar(), 0, 3);
        MainMenuStrip = menu; Controls.Add(root);
    }

    private static MenuStrip CreateMenu()
    {
        var menu = new MenuStrip { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden };
        menu.Items.Add(CreateMenu("File", "New Design", "Open Design", "Save Design", "Save As", "Exit")); menu.Items.Add(CreateMenu("Design", "Calculate", "Reset Parameters")); menu.Items.Add(CreateMenu("Tools", "Settings")); menu.Items.Add(CreateMenu("Help", "User Guide", "About")); return menu;
    }

    private static ToolStripMenuItem CreateMenu(string title, params string[] items) { var m = new ToolStripMenuItem(title); foreach (var item in items) m.DropDownItems.Add(item); return m; }

    private GroupBox CreateSelection()
    {
        var box = new GroupBox { Text = "Circuit Selection", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 16, 10, 8), Margin = new Padding(8, 4, 8, 4) };
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
        row.Controls.Add(new Label { Text = "Circuit Category:", AutoSize = true, Margin = new Padding(0, 5, 10, 0) });
        categoryCombo.DropDownStyle = ComboBoxStyle.DropDownList; categoryCombo.Width = 560; categoryCombo.DropDownWidth = 560; categoryCombo.Items.AddRange(new object[] { "Select a circuit category...", "Precision Analog Computing", "Precision Signal Conditioning", "Filters", "Comparators & Threshold Control", "Oscillators & Function Generators", "Switching Power Supplies", "Control & Loop Compensation" }); categoryCombo.SelectedIndex = 0; categoryCombo.SelectedIndexChanged += CategoryChanged; row.Controls.Add(categoryCombo); box.Controls.Add(row); return box;
    }

    private Control CreateWorkArea()
    {
        mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            FixedPanel = FixedPanel.Panel1,
            Panel1MinSize = 0,
            Panel2MinSize = 0,
            Margin = new Padding(8, 0, 8, 4)
        };
        mainSplit.Panel1.Controls.Add(CreateNavigation());
        designHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty }; designHost.Controls.Add(CreateDefaultDesignArea()); mainSplit.Panel2.Controls.Add(designHost);
        return mainSplit;
    }

    private GroupBox CreateNavigation()
    {
        var box = new GroupBox { Text = "Design Navigation", Dock = DockStyle.Fill, Padding = new Padding(8, 14, 8, 8) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty }; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        navigationHint = new Label { Text = "Select a circuit category above to begin.", AutoSize = true, Margin = new Padding(2, 2, 2, 8) }; navigationTree.Dock = DockStyle.Fill; navigationTree.BorderStyle = BorderStyle.FixedSingle; navigationTree.HideSelection = false; layout.Controls.Add(navigationHint, 0, 0); layout.Controls.Add(navigationTree, 0, 1); box.Controls.Add(layout); return box;
    }

    private static Control CreateDefaultDesignArea()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6, 0, 0, 0) }; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var parameters = new GroupBox { Text = "Design Parameters", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10, 16, 10, 8) }; parameters.Controls.Add(new Label { AutoSize = true, Text = "No circuit module selected.\r\nSelect a circuit category and module to configure design parameters." }); layout.Controls.Add(parameters, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) }; tabs.TabPages.Add(MessageTab("Schematic", "No schematic available.")); tabs.TabPages.Add(MessageTab("Analysis", "No analysis available.")); tabs.TabPages.Add(MessageTab("Calculations", "No calculations available.")); var net = new TabPage("SPICE Netlist") { BackColor = SystemColors.Window }; net.Controls.Add(new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F), Text = "* No SPICE netlist generated.", BackColor = SystemColors.Window }); tabs.TabPages.Add(net); layout.Controls.Add(tabs, 0, 1); return layout;
    }

    private static TabPage MessageTab(string title, string text) { var page = new TabPage(title) { BackColor = SystemColors.Window }; page.Controls.Add(new Label { Dock = DockStyle.Fill, Text = text, TextAlign = ContentAlignment.MiddleCenter, BackColor = SystemColors.Window }); return page; }

    private void CategoryChanged(object? sender, EventArgs e)
    {
        var selection = categoryCombo.SelectedItem?.ToString();
        var isFilter = selection?.StartsWith("Filters", StringComparison.OrdinalIgnoreCase) == true;
        var isPrecisionAnalog = selection?.StartsWith("Precision Analog Computing", StringComparison.OrdinalIgnoreCase) == true;
        navigationHint?.Visible = !isFilter && !isPrecisionAnalog;
        if (designHost == null) return;
        RemoveCurrentDesignControl();
        if (isFilter) designHost.Controls.Add(new FilterWizardControl(navigationTree));
        else if (isPrecisionAnalog)
        {
            precisionAnalogWizard ??= new PrecisionAnalogWizardControl();
            precisionAnalogWizard.AttachNavigation(navigationTree);
            designHost.Controls.Add(precisionAnalogWizard);
        }
        else { navigationTree.Nodes.Clear(); designHost.Controls.Add(CreateDefaultDesignArea()); }
    }

    private void PreloadPrecisionAnalogOnIdle(object? sender, EventArgs e)
    {
        Application.Idle -= PreloadPrecisionAnalogOnIdle;
        if (IsDisposed || precisionAnalogWizard is not null) return;
        precisionAnalogWizard = new PrecisionAnalogWizardControl();
        precisionAnalogWizard.CreateControl();
    }

    private void RemoveCurrentDesignControl()
    {
        if (designHost == null) return;
        foreach (Control control in designHost.Controls.Cast<Control>().ToArray())
        {
            designHost.Controls.Remove(control);
            if (ReferenceEquals(control, precisionAnalogWizard))
                precisionAnalogWizard.DetachNavigation();
            else
                control.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.Idle -= PreloadPrecisionAnalogOnIdle;
            precisionAnalogWizard?.Dispose();
            precisionAnalogWizard = null;
        }
        base.Dispose(disposing);
    }

    private void ConfigureSplitter()
    {
        if (mainSplit == null)
            return;
        var available = mainSplit.ClientSize.Width - mainSplit.SplitterWidth;
        if (available <= 2)
            return;
        mainSplit.Panel1MinSize = 0;
        mainSplit.Panel2MinSize = 0;
        const int navigationMinimum = 420;
        const int designMinimum = 620;
        var maximum = available - designMinimum;
        if (maximum >= navigationMinimum)
        {
            // The original navigation pane was 340 px / 28%; Precision Analog needs
            // room for its longer operation names, so use exactly 1.5 times that width.
            var preferred = Math.Max(510, (int)(available * 0.42));
            mainSplit.SplitterDistance = Math.Clamp(preferred, navigationMinimum, maximum);
            mainSplit.Panel1MinSize = navigationMinimum;
            mainSplit.Panel2MinSize = designMinimum;
        }
        else
        {
            mainSplit.SplitterDistance = Math.Clamp((int)(available * 0.32), 1, available - 1);
        }
    }

    private static StatusStrip CreateStatusBar() { var status = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = true }; status.Items.Add(new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft }); return status; }
}
