using System.Drawing;
using CircuitModuleDesigner.Models;
using CircuitModuleDesigner.Modules.Comparators.Controls;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Controls;
using CircuitModuleDesigner.Services;

namespace CircuitModuleDesigner.Forms;

/// <summary>
/// Application shell: category selector, module workspace, status bar and menu.
/// bar and a working File/Design/Tools/Help menu. The shell never references a
/// concrete wizard except when it instantiates the selected module.
/// </summary>
public sealed class MainForm : Form
{
    private const string Placeholder = "Select a circuit category...";
    private const string FiltersCategory = "Filters";
    private const string PrecisionCategory = "Precision Analog Computing";
    private const string ComparatorCategory = "Comparators & Threshold Control";
    private const string AttenuationMatchingCategory = "Signal Attenuation & Impedance Matching";

    private readonly AppSettingsStore settingsStore = new();
    private readonly DesignDocumentService documentService = new();
    private readonly ComboBox categoryCombo = new();
    private readonly TreeView navigationTree = new();
    private readonly Label navigationHint = new() { UseMnemonic = false };
    private readonly Label statusText = new()
    {
        Text = "Ready",
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = UiTheme.Caption,
        ForeColor = UiTheme.Text,
        AutoEllipsis = true,
        UseMnemonic = false
    };
    private readonly Label statusModule = new()
    {
        Text = string.Empty,
        AutoSize = true,
        Anchor = AnchorStyles.Right,
        TextAlign = ContentAlignment.MiddleRight,
        Font = UiTheme.Caption,
        ForeColor = UiTheme.Muted,
        Margin = new Padding(12, 0, 0, 0),
        UseMnemonic = false
    };

    private AppSettings settings;
    private Panel? designHost;
    private SplitContainer? mainSplit;
    private Panel? statusBar;
    private IDesignModuleHost? activeModule;
    private string? currentFile;
    private string? activeCategory;
    private bool suppressCategoryEvent;

    public MainForm()
    {
        settings = settingsStore.Load();
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Text = AppInfo.Name;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 820);
        MinimumSize = new Size(1100, 720);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = UiTheme.Body;
        BackColor = UiTheme.Window;
        Icon = SystemIcons.Application;

        BuildInterface();
        ApplySettings(settings);
        UpdateTitle();

        Shown += (_, _) =>
        {
            BeginInvoke(ConfigureSplitter);
            if (settings.ReopenLastCategory) ReopenLastCategory();
        };
        FormClosing += MainFormClosing;
    }

    // ---------------------------------------------------------------- layout

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // menu
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // category card
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // workspace
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));  // status

        var menu = CreateMenu();
        root.Controls.Add(menu, 0, 0);
        root.Controls.Add(CreateSelectionCard(), 0, 1);
        root.Controls.Add(CreateWorkArea(), 0, 2);
        root.Controls.Add(CreateStatusBar(), 0, 3);

        MainMenuStrip = menu;
        Controls.Add(root);
    }

    private MenuStrip CreateMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            Renderer = new UiTheme.MenuRenderer(),
            BackColor = UiTheme.Surface,
            Font = UiTheme.Body,
            Padding = new Padding(8, 2, 8, 2)
        };

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(Item("&New Design", Keys.Control | Keys.N, NewDesign));
        file.DropDownItems.Add(Item("&Open Design...", Keys.Control | Keys.O, OpenDesign));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Save Design", Keys.Control | Keys.S, SaveDesign));
        file.DropDownItems.Add(Item("Save &As...", Keys.Control | Keys.Shift | Keys.S, SaveDesignAs));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("E&xit", Keys.None, Close));

        var design = new ToolStripMenuItem("&Design");
        design.DropDownItems.Add(Item("&Calculate", Keys.F5, Calculate));
        design.DropDownItems.Add(Item("&Reset Parameters", Keys.Control | Keys.R, ResetParameters));

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.Add(Item("&Settings...", Keys.None, OpenSettings));

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(Item("&User Guide", Keys.F1, ShowUserGuide));
        help.DropDownItems.Add(Item("&About", Keys.None, ShowAbout));

        menu.Items.AddRange(new ToolStripItem[] { file, design, tools, help });
        return menu;
    }

    private static ToolStripMenuItem Item(string text, Keys shortcut, Action action)
    {
        var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut, ShowShortcutKeys = shortcut != Keys.None };
        item.Click += (_, _) => action();
        return item;
    }

    private Control CreateSelectionCard()
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 1,
            BackColor = UiTheme.Surface,
            Padding = new Padding(16, 10, 16, 10),
            Margin = new Padding(10, 8, 10, 0)
        };
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawLine(pen, 0, card.Height - 1, card.Width, card.Height - 1);
        };

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));

        row.Controls.Add(new Label
        {
            Text = "CIRCUIT CATEGORY",
            AutoSize = true,
            Font = UiTheme.Caption,
            ForeColor = UiTheme.Muted,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 12, 0)
        }, 0, 0);

        categoryCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        categoryCombo.Font = UiTheme.Body;
        categoryCombo.Dock = DockStyle.Fill;
        categoryCombo.Margin = new Padding(0, 0, 8, 0);
        categoryCombo.Items.AddRange(new object[] { Placeholder, PrecisionCategory, FiltersCategory, ComparatorCategory, AttenuationMatchingCategory });
        categoryCombo.SelectedIndex = 0;
        categoryCombo.SelectedIndexChanged += CategoryChanged;
        row.Controls.Add(categoryCombo, 1, 0);

        var open = new Button
        {
            Text = "Open design...",
            AutoSize = true,
            MinimumSize = new Size(120, 30),
            Margin = new Padding(10, 0, 0, 0)
        };
        open.Click += (_, _) => OpenDesign();
        UiTheme.StyleButton(open);
        row.Controls.Add(open, 2, 0);

        row.Controls.Add(new Label
        {
            Text = "Pick a category to start a new design, or open a saved .cmddesign file.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = UiTheme.Caption,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(12, 0, 0, 0)
        }, 3, 0);

        card.Controls.Add(row, 0, 0);
        return card;
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
            BackColor = UiTheme.Window,
            Margin = new Padding(10, 8, 10, 8)
        };
        mainSplit.Panel1.Controls.Add(CreateNavigationCard());
        designHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(2)
        };
        designHost.Controls.Add(CreateEmptyState());
        mainSplit.Panel2.Controls.Add(designHost);
        return mainSplit;
    }

    private Control CreateNavigationCard()
    {
        var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 10, 12, 10) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "DESIGN NAVIGATION",
            AutoSize = true,
            Font = UiTheme.Caption,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 0, 0, 4)
        }, 0, 0);

        navigationHint.Text = "Select a circuit category above to begin.";
        navigationHint.AutoSize = true;
        navigationHint.Font = UiTheme.Body;
        navigationHint.ForeColor = UiTheme.Text;
        navigationHint.Margin = new Padding(0, 0, 0, 8);
        card.Resize += (_, _) =>
            navigationHint.MaximumSize = new Size(Math.Max(160, card.ClientSize.Width - 24), 0);
        layout.Controls.Add(navigationHint, 0, 1);

        navigationTree.Dock = DockStyle.Fill;
        navigationTree.BorderStyle = BorderStyle.None;
        navigationTree.BackColor = UiTheme.Surface;
        navigationTree.Font = UiTheme.Body;
        navigationTree.ItemHeight = 26;
        navigationTree.FullRowSelect = true;
        navigationTree.HideSelection = false;
        navigationTree.ShowLines = false;
        navigationTree.ShowPlusMinus = false;
        navigationTree.ShowRootLines = true;
        layout.Controls.Add(navigationTree, 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private Control CreateStatusBar()
    {
        statusBar = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Surface,
            Padding = new Padding(12, 0, 12, 0)
        };
        statusBar.Paint += (_, e) =>
        {
            using var pen = new Pen(UiTheme.Border);
            e.Graphics.DrawLine(pen, 0, 0, statusBar.Width, 0);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(statusText, 0, 0);
        layout.Controls.Add(statusModule, 1, 0);
        statusBar.Controls.Add(layout);
        return statusBar;
    }

    private Control CreateEmptyState()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UiTheme.Surface,
            Padding = new Padding(24)
        };
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var block = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Anchor = AnchorStyles.None,
            BackColor = Color.Transparent
        };
        block.Controls.Add(new Label
        {
            Text = "No design open",
            AutoSize = true,
            Font = UiTheme.Title,
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 0, 0, 6)
        });
        block.Controls.Add(new Label
        {
            Text = "Choose a circuit category above to start a new design.\r\nThe modules walk you through parameters, synthesis and verification.",
            AutoSize = true,
            Font = UiTheme.Body,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 0, 0, 18)
        });
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var newDesign = new Button { Text = "New design", AutoSize = true, MinimumSize = new Size(110, 32) };
        var openDesign = new Button { Text = "Open design...", AutoSize = true, MinimumSize = new Size(120, 32) };
        newDesign.Click += (_, _) => NewDesign();
        openDesign.Click += (_, _) => OpenDesign();
        UiTheme.StylePrimaryButton(newDesign);
        UiTheme.StyleButton(openDesign);
        actions.Controls.Add(newDesign);
        actions.Controls.Add(openDesign);
        block.Controls.Add(actions);
        host.Controls.Add(block, 0, 1);
        return host;
    }

    // ------------------------------------------------------------- module host

    private void CategoryChanged(object? sender, EventArgs e)
    {
        if (suppressCategoryEvent) return;
        var selection = categoryCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selection) || selection == Placeholder)
        {
            if (activeModule?.HasDesign == true && !ConfirmDiscard("Clearing the current module"))
            {
                RestoreCategorySelection();
                return;
            }
            ClearWorkspace(false);
            return;
        }
        SwitchModule(selection);
    }

    private void SwitchModule(string selection, bool force = false)
    {
        if (!force && activeModule?.HasDesign == true && !ConfirmDiscard($"Switching to {selection}"))
        {
            RestoreCategorySelection();
            return;
        }

        activeCategory = selection;
        currentFile = null;
        settings.LastCategory = selection;
        DisposeWorkspace();

        var (control, module) = CreateModule(selection);
        activeModule = module;
        navigationHint.Visible = false;
        designHost!.Controls.Add(control);
        UiTheme.ApplyModule(control);

        statusText.Text = $"{selection} ready.";
        UpdateStatus();
        UpdateTitle();
    }

    private (Control Control, IDesignModuleHost Module) CreateModule(string selection)
    {
        if (string.Equals(selection, FiltersCategory, StringComparison.OrdinalIgnoreCase))
        {
            var wizard = new FilterWizardControl(navigationTree);
            return (wizard, wizard);
        }
        if (string.Equals(selection, PrecisionCategory, StringComparison.OrdinalIgnoreCase))
        {
            var wizard = new PrecisionAnalogWizardControl();
            wizard.AttachNavigation(navigationTree);
            return (wizard, wizard);
        }
        if (string.Equals(selection, ComparatorCategory, StringComparison.OrdinalIgnoreCase))
        {
            var comparator = new ComparatorWizardControl();
            comparator.AttachNavigation(navigationTree);
            return (comparator, comparator);
        }
        var matching = new AttenuationMatchingWizardControl(navigationTree);
        return (matching, matching);
    }

    private void DisposeWorkspace()
    {
        activeModule?.DetachNavigation();
        activeModule = null;
        if (designHost is null) return;
        foreach (Control control in designHost.Controls.Cast<Control>().ToArray())
        {
            designHost.Controls.Remove(control);
            control.Dispose();
        }
    }

    // ---------------------------------------------------------------- actions

    private void NewDesign()
    {
        if (activeModule?.HasDesign == true && !ConfirmDiscard("Starting a new design")) return;
        ClearWorkspace(true);
        statusText.Text = "New design started. Choose a circuit category.";
    }

    private void ClearWorkspace(bool resetCategory)
    {
        DisposeWorkspace();
        activeCategory = null;
        currentFile = null;
        settings.LastCategory = string.Empty;
        navigationTree.Nodes.Clear();
        navigationHint.Visible = true;
        navigationHint.Text = "Select a circuit category above to begin.";
        designHost!.Controls.Add(CreateEmptyState());
        if (resetCategory)
        {
            suppressCategoryEvent = true;
            categoryCombo.SelectedIndex = 0;
            suppressCategoryEvent = false;
        }
        UpdateStatus();
        UpdateTitle();
    }

    private void Calculate()
    {
        if (activeModule is null)
        {
            ShowInfo("Select a circuit category before calculating a design.");
            return;
        }
        try
        {
            statusText.Text = $"Calculating {activeModule.ModuleTitle}...";
            activeModule.Calculate();
        }
        catch (Exception exception)
        {
            statusText.Text = "Calculation failed.";
            ShowError("The design calculation failed:\r\n\r\n" + exception.Message);
        }
    }

    private void ResetParameters()
    {
        if (activeCategory is null)
        {
            ShowInfo("Select a circuit category before resetting its parameters.");
            return;
        }
        if (activeModule?.HasDesign == true && !ConfirmDiscard("Resetting parameters")) return;
        var category = activeCategory;
        SwitchModule(category, force: true);
        statusText.Text = $"{category} parameters reset to defaults.";
    }

    private void SaveDesign()
    {
        if (!EnsureDesignForSave()) return;
        var fileName = currentFile ?? PromptSaveFileName();
        if (fileName is null) return;
        SaveTo(fileName);
    }

    private void SaveDesignAs()
    {
        if (!EnsureDesignForSave()) return;
        var fileName = PromptSaveFileName();
        if (fileName is null) return;
        SaveTo(fileName);
    }

    private bool EnsureDesignForSave()
    {
        if (activeModule is { HasDesign: true }) return true;
        ShowInfo("There is no generated design to save.\r\n\r\nRun the module's design step first (Design > Calculate or the module's own button).");
        return false;
    }

    private string? PromptSaveFileName()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = DesignDocumentService.FileFilter,
            DefaultExt = DesignDocumentService.Extension,
            AddExtension = true,
            FileName = $"{(string.IsNullOrWhiteSpace(activeCategory) ? "design" : activeCategory.Replace(" ", "-").Replace("&", "and"))}-{DateTime.Now:yyyyMMdd-HHmm}{DesignDocumentService.Extension}",
            Title = "Save design"
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void SaveTo(string fileName)
    {
        try
        {
            var document = DesignDocumentService.Create(activeModule!, activeCategory ?? string.Empty, DateTimeOffset.UtcNow);
            documentService.Save(document, fileName);
            currentFile = fileName;
            statusText.Text = $"Saved {Path.GetFileName(fileName)}";
            UpdateTitle();
        }
        catch (Exception exception)
        {
            ShowError("The design could not be saved:\r\n\r\n" + exception.Message);
        }
    }

    private void OpenDesign()
    {
        if (activeModule?.HasDesign == true && !ConfirmDiscard("Opening a saved design")) return;
        using var dialog = new OpenFileDialog
        {
            Filter = DesignDocumentService.FileFilter,
            DefaultExt = DesignDocumentService.Extension,
            CheckFileExists = true,
            Title = "Open design"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var document = documentService.Load(dialog.FileName);
            ShowDocument(document, dialog.FileName);
        }
        catch (Exception exception)
        {
            ShowError("The design could not be opened:\r\n\r\n" + exception.Message);
        }
    }

    private void ShowDocument(DesignDocument document, string fileName)
    {
        DisposeWorkspace();
        activeModule = null;
        activeCategory = null;
        currentFile = fileName;
        settings.LastCategory = string.Empty;
        suppressCategoryEvent = true;
        categoryCombo.SelectedIndex = 0;
        suppressCategoryEvent = false;

        navigationTree.Nodes.Clear();
        navigationTree.Nodes.Add(new TreeNode("Saved design (read-only)"));
        navigationHint.Visible = true;
        navigationHint.Text = "This file is a saved design archive. Pick a category above to start a new design.";
        var viewer = new DesignDocumentViewer(document, fileName);
        viewer.CloseRequested += (_, _) => ClearWorkspace(true);
        designHost!.Controls.Add(viewer);

        statusText.Text = $"Opened {Path.GetFileName(fileName)}";
        UpdateStatus();
        UpdateTitle();
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(settings, settingsStore.FileName);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        settings = dialog.Result;
        ApplySettings(settings);
        try
        {
            settingsStore.Save(settings);
            statusText.Text = "Settings saved.";
        }
        catch (Exception exception)
        {
            ShowError("Settings could not be persisted:\r\n\r\n" + exception.Message);
        }
    }

    private void ShowUserGuide() => new HelpForm("User Guide", UserGuideText()).ShowDialog(this);

    private void ShowAbout() => new HelpForm("About", AboutText(), new Size(560, 430)).ShowDialog(this);

    // -------------------------------------------------------------- preferences

    private void ApplySettings(AppSettings value)
    {
        if (mainSplit is not null) mainSplit.Panel1Collapsed = !value.ShowNavigationPane;
        if (statusBar is not null)
        {
            statusBar.Visible = value.ShowStatusBar;
            if (statusBar.Parent is TableLayoutPanel table && table.RowStyles.Count > 3)
                table.RowStyles[3].Height = value.ShowStatusBar ? 26 : 0;
        }
        if (value.RememberWindowBounds && value.WindowWidth > 320 && value.WindowHeight > 240)
        {
            var bounds = new Rectangle(value.WindowX, value.WindowY, value.WindowWidth, value.WindowHeight);
            if (value.WindowX != int.MinValue && Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds)))
            {
                StartPosition = FormStartPosition.Manual;
                Bounds = bounds;
            }
            else
            {
                Size = new Size(value.WindowWidth, value.WindowHeight);
            }
        }
    }

    private void ReopenLastCategory()
    {
        if (string.IsNullOrWhiteSpace(settings.LastCategory)) return;
        var index = categoryCombo.Items.IndexOf(settings.LastCategory);
        if (index > 0) categoryCombo.SelectedIndex = index;
    }

    private void MainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (settings.RememberWindowBounds)
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.WindowX = Location.X;
            settings.WindowY = Location.Y;
        }
        try
        {
            settingsStore.Save(settings);
        }
        catch (Exception)
        {
            // Closing must never be blocked by a settings write failure.
        }
        if (!settings.ConfirmOnExit || activeModule?.HasDesign != true) return;
        var answer = MessageBox.Show(this,
            "A generated design is still open. Exit without saving it?",
            AppInfo.Name,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) e.Cancel = true;
    }

    // ------------------------------------------------------------------- helpers

    private void UpdateStatus() =>
        statusModule.Text = activeModule?.ModuleTitle ??
            (currentFile is not null ? "Saved design" : "No module selected");

    private void UpdateTitle()
    {
        var parts = new List<string> { AppInfo.Name };
        if (activeModule is not null) parts.Add(activeModule.ModuleTitle);
        else if (currentFile is not null) parts.Add("Saved design");
        if (!string.IsNullOrWhiteSpace(currentFile)) parts.Add(Path.GetFileName(currentFile));
        Text = string.Join("  —  ", parts);
    }

    private void RestoreCategorySelection()
    {
        suppressCategoryEvent = true;
        if (activeCategory is null) categoryCombo.SelectedIndex = 0;
        else categoryCombo.SelectedItem = activeCategory;
        suppressCategoryEvent = false;
    }

    private bool ConfirmDiscard(string action) =>
        MessageBox.Show(this,
            $"{action} will discard the current generated design. Continue?",
            AppInfo.Name,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private void ShowInfo(string message) =>
        MessageBox.Show(this, message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);

    private void ShowError(string message) =>
        MessageBox.Show(this, message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);

    private void ConfigureSplitter()
    {
        if (mainSplit is null) return;
        var available = mainSplit.ClientSize.Width - mainSplit.SplitterWidth;
        if (available <= 2) return;
        mainSplit.Panel1MinSize = 0;
        mainSplit.Panel2MinSize = 0;
        const int navigationMinimum = 360;
        const int designMinimum = 640;
        var maximum = available - designMinimum;
        if (maximum >= navigationMinimum)
        {
            mainSplit.SplitterDistance = Math.Clamp((int)(available * 0.34), navigationMinimum, maximum);
            mainSplit.Panel1MinSize = navigationMinimum;
            mainSplit.Panel2MinSize = designMinimum;
        }
        else
        {
            mainSplit.SplitterDistance = Math.Clamp((int)(available * 0.32), 1, available - 1);
        }
    }

    private static string UserGuideText() => $"""
{AppInfo.Name} — User Guide

QUICK START
1. Choose a circuit category in the selector at the top of the window.
2. Work through the wizard steps in the module workspace. Each step validates its
   inputs and the Design Result page shows the generated CircuitGraph.
3. Press F5 (or Design > Calculate) to run the module's calculation again.
4. Press Ctrl+S to save the generated design as a .cmddesign archive.

MODULES
• Filters — active and passive filter synthesis with Butterworth, Chebyshev,
  Bessel, tabulated prototypes and Sallen-Key / multiple-feedback / Bainter /
  passive RC or LC implementations. The module verifies the generated circuit.
• Precision Analog Computing — affine summers, range mappers, piecewise and
  nonlinear operation synthesis with DC, AC and time-domain analysis.
• Comparators & Threshold Control — Schmitt triggers, window comparators and
  relaxation oscillators with threshold and hysteresis verification.
• Signal Attenuation & Impedance Matching — resistive T/Pi/L/multi-stage pads,
  LC L/Pi/T matching, complex ports, standard values, two-port verification and
  S-parameter/frequency-response reporting.

MENU ACTIONS
File > New Design (Ctrl+N)      Discard the workspace and start over.
File > Open Design (Ctrl+O)     Open a saved .cmddesign archive read-only.
File > Save Design (Ctrl+S)     Save the generated design of the active module.
File > Save As (Ctrl+Shift+S)   Save under a new file name.
Design > Calculate (F5)         Run the active module's design action.
Design > Reset Parameters       Recreate the active module with defaults.
Tools > Settings                Preferences (navigation, status bar, window,
                               confirmation and startup behaviour).
Help > User Guide (F1)          This page.
Help > About                    Version and runtime information.

SAVED FILES
A .cmddesign file stores the module name, the JSON design snapshot, a readable
summary and the SPICE netlist (when available). Open Design shows the archive in
a read-only viewer; it never overwrites unsaved work silently.

TIPS
• The status bar shows the active module and the last action.
• Window size, navigation pane and status bar visibility are remembered.
• Every generated design is verified from its circuit graph before export.
""";

    private string AboutText() => $"""
{AppInfo.Name}

Modules
  • {FiltersCategory}
  • {PrecisionCategory}
  • {ComparatorCategory}
  • {AttenuationMatchingCategory}

Runtime
  .NET {Environment.Version}
  Windows Forms · {Environment.OSVersion}

Settings file
  {settingsStore.FileName}

Design files
  .cmddesign archives keep a module snapshot, a summary and an optional netlist.
""";
}
