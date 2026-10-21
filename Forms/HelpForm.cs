namespace CircuitModuleDesigner.Forms;

/// <summary>Plain read-only text window used by Help &gt; User Guide and Help &gt; About.</summary>
public sealed class HelpForm : Form
{
    public HelpForm(string title, string body, Size? size = null)
    {
        var desiredSize = size ?? new Size(760, 620);
        Text = title;
        Font = UiTheme.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiTheme.Window;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = desiredSize;
        MinimumSize = new Size(560, 420);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            Font = UiTheme.Body,
            DetectUrls = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Text = body.Replace("\r\n", "\n").Replace("\n", Environment.NewLine)
        };
        root.Controls.Add(text, 0, 0);

        var close = new Button
        {
            Text = "Close",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(90, 30),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 10, 0, 0)
        };
        UiTheme.StyleButton(close);
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        footer.Controls.Add(close);
        root.Controls.Add(footer, 0, 1);

        AcceptButton = close;
        CancelButton = close;
        Controls.Add(root);
        ClientSize = desiredSize;
    }
}
