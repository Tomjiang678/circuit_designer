using System.Diagnostics;
using CircuitModuleDesigner.Models;
using CircuitModuleDesigner.Services;

namespace CircuitModuleDesigner.Forms;

/// <summary>Read-only viewer for a saved <c>.cmddesign</c> document.</summary>
public sealed class DesignDocumentViewer : UserControl
{
    private readonly DesignDocument document;
    private readonly string? fileName;

    public DesignDocumentViewer(DesignDocument document, string? fileName = null)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.fileName = fileName;
        Dock = DockStyle.Fill;
        BackColor = UiTheme.Window;
        BuildInterface();
    }

    public event EventHandler? CloseRequested;

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = Padding.Empty
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label
        {
            Text = $"Saved design — {document.Module}",
            AutoSize = true,
            Font = UiTheme.Subheading,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            UseMnemonic = false
        };
        var meta = new Label
        {
            Text = $"{document.Category}  ·  saved {document.SavedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
            AutoSize = true,
            Font = UiTheme.Caption,
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 0),
            UseMnemonic = false
        };
        var readOnly = new Label
        {
            Text = "READ-ONLY",
            AutoSize = true,
            Font = UiTheme.Caption,
            ForeColor = UiTheme.Accent,
            BackColor = UiTheme.AccentSoft,
            Padding = new Padding(8, 3, 8, 3),
            Margin = new Padding(0, 2, 0, 0)
        };
        headerLayout.Controls.Add(title, 0, 0);
        headerLayout.Controls.Add(readOnly, 1, 0);
        headerLayout.Controls.Add(meta, 0, 1);
        header.Controls.Add(headerLayout);
        root.Controls.Add(header, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = UiTheme.Body, Padding = new Point(14, 4) };
        tabs.TabPages.Add(Page("Summary", document.Summary, UiTheme.Body, true));
        tabs.TabPages.Add(Page("Snapshot (JSON)", DesignDocumentService.Pretty(document.SnapshotJson), UiTheme.Mono, true));
        if (!string.IsNullOrWhiteSpace(document.Netlist))
            tabs.TabPages.Add(Page("Netlist", document.Netlist, UiTheme.Mono, true));
        root.Controls.Add(tabs, 0, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0)
        };
        var close = new Button { Text = "Close viewer", AutoSize = true, MinimumSize = new Size(110, 30) };
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        footer.Controls.Add(close);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var openFolder = new Button { Text = "Show file location", AutoSize = true, MinimumSize = new Size(140, 30) };
            openFolder.Click += (_, _) => RevealFile();
            footer.Controls.Add(openFolder);
        }
        UiTheme.StyleButton(close);
        foreach (Button button in footer.Controls.OfType<Button>()) UiTheme.StyleButton(button);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);
    }

    private void RevealFile()
    {
        if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fileName}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // The viewer is read-only; failing to open Explorer must not break it.
        }
    }

    private static TabPage Page(string title, string text, Font font, bool wordWrap)
    {
        var page = new TabPage(title) { BackColor = UiTheme.Surface, Padding = new Padding(10) };
        var normalized = string.IsNullOrWhiteSpace(text)
            ? "No content was stored in this file."
            : text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        var box = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            Font = font,
            WordWrap = wordWrap,
            ScrollBars = wordWrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both,
            DetectUrls = false,
            Text = normalized
        };
        page.Controls.Add(box);
        return page;
    }
}
