using CircuitModuleDesigner.Models;

namespace CircuitModuleDesigner.Forms;

/// <summary>Small preferences dialog. Returns a modified copy in <see cref="Result"/>.</summary>
public sealed class SettingsForm : Form
{
    private readonly CheckBox confirmOnExit = Option("Confirm before exiting with a generated design");
    private readonly CheckBox showNavigation = Option("Show the navigation pane");
    private readonly CheckBox rememberBounds = Option("Remember window size and position");
    private readonly CheckBox reopenLast = Option("Reopen the last category at startup");

    public SettingsForm(AppSettings current, string settingsFileName)
    {
        ArgumentNullException.ThrowIfNull(current);
        Result = current.Clone();
        Text = "Settings";
        Font = UiTheme.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Window;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ShowInTaskbar = false;
        MinimumSize = new Size(560, 420);
        ClientSize = new Size(640, 460);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
            BackColor = UiTheme.Window
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "Application preferences",
            AutoSize = true,
            Font = UiTheme.Subheading,
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 0, 0, 10)
        }, 0, 0);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = UiTheme.Surface,
            Padding = new Padding(14, 10, 14, 10)
        };
        foreach (var option in new[] { confirmOnExit, showNavigation, rememberBounds, reopenLast })
        {
            option.Margin = new Padding(0, 4, 0, 4);
            options.Controls.Add(option);
        }
        confirmOnExit.Checked = Result.ConfirmOnExit;
        showNavigation.Checked = Result.ShowNavigationPane;
        rememberBounds.Checked = Result.RememberWindowBounds;
        reopenLast.Checked = Result.ReopenLastCategory;
        root.Controls.Add(options, 0, 1);

        root.Controls.Add(new Label
        {
            Text = "Stored in " + settingsFileName,
            AutoSize = true,
            Font = UiTheme.Caption,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 8, 0, 8)
        }, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(90, 30) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 30) };
        UiTheme.StyleButton(ok);
        UiTheme.StyleButton(cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons, 0, 3);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(root);
        ClientSize = new Size(640, 460);
    }

    public AppSettings Result { get; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            Result.ConfirmOnExit = confirmOnExit.Checked;
            Result.ShowNavigationPane = showNavigation.Checked;
            Result.RememberWindowBounds = rememberBounds.Checked;
            Result.ReopenLastCategory = reopenLast.Checked;
        }
        base.OnFormClosing(e);
    }

    private static CheckBox Option(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = UiTheme.Text,
        BackColor = Color.Transparent
    };
}
