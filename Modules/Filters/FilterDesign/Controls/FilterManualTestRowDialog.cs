using CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Services;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterManualTestRowDialog : Form
{
    private readonly RichTextBox input = Editor();
    private readonly RichTextBox output = Editor(true);
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 8, 8, 0) };
    private readonly Button run = new() { Text = "Run Rows", AutoSize = true, MinimumSize = new Size(100, 28) };
    private readonly Button copy = new() { Text = "Copy Results", AutoSize = true, MinimumSize = new Size(125, 28), Enabled = false };

    public FilterManualTestRowDialog()
    {
        Text = "Filter Manual Test Row";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1060, 500);
        MinimumSize = new Size(820, 420);
        Font = new Font("Segoe UI", 9F);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(6, 4, 6, 4) };
        var paste = new Button { Text = "Paste Excel Row", AutoSize = true, MinimumSize = new Size(125, 28) };
        var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(85, 28) };
        paste.Click += (_, _) => { if (Clipboard.ContainsText()) input.Text = Clipboard.GetText().Trim(); };
        run.Click += async (_, _) => await RunAsync();
        copy.Click += (_, _) => { if (output.TextLength > 0) Clipboard.SetText(output.Text.Trim()); };
        close.Click += (_, _) => Close();
        actions.Controls.Add(paste); actions.Controls.Add(run); actions.Controls.Add(copy); actions.Controls.Add(close); actions.Controls.Add(status);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "Input: paste 1-300 rows. Each row may use the 35-field TXT format, complete 57-field format, or legacy 51-field format.", AutoSize = true }, 0, 0);
        root.Controls.Add(input, 0, 1);
        root.Controls.Add(new Label { Text = "Output: one 57-field result row per input row, in the same order.", AutoSize = true, Margin = new Padding(0, 8, 0, 2) }, 0, 2);
        root.Controls.Add(output, 0, 3);
        root.Controls.Add(actions, 0, 4);
        Controls.Add(root);
    }

    private async Task RunAsync()
    {
        run.Enabled = false; copy.Enabled = false; UseWaitCursor = true; status.ForeColor = Color.DimGray; status.Text = "Preparing rows...";
        try
        {
            var service = new FilterManualTestRowService();
            var parsedRows = service.ParseMany(input.Text);
            var progress = new Progress<int>(completed => status.Text = $"Designing {completed}/{parsedRows.Count}...");
            var batch = await Task.Run(() =>
            {
                var engine = new FilterDesignEngine();
                var completed = new List<(string Row, bool Supported)>();
                for (var index = 0; index < parsedRows.Count; index++)
                {
                    var parsed = parsedRows[index];
                    var result = engine.Design(parsed.Request);
                    completed.Add((service.Complete(parsed, result), result.IsFullySupported));
                    ((IProgress<int>)progress).Report(index + 1);
                }
                return completed;
            });
            output.Lines = batch.Select(item => item.Row).ToArray();
            copy.Enabled = true;
            var supported = batch.Count(item => item.Supported);
            status.ForeColor = supported == batch.Count ? Color.DarkGreen : Color.DarkOrange;
            status.Text = $"Completed {batch.Count} rows: {supported} fully supported, {batch.Count - supported} conditional/unsupported. Copy all results.";
        }
        catch (Exception exception)
        {
            status.ForeColor = Color.Firebrick; status.Text = exception.Message;
        }
        finally { run.Enabled = true; UseWaitCursor = false; }
    }

    private static RichTextBox Editor(bool readOnly = false) => new()
    {
        Dock = DockStyle.Fill, ReadOnly = readOnly, WordWrap = false, DetectUrls = false,
        Font = new Font("Consolas", 9F), BackColor = SystemColors.Window
    };
}
