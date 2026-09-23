using CircuitModuleDesigner.Modules.Comparators.Engines;
using CircuitModuleDesigner.Modules.Comparators.Services;

namespace CircuitModuleDesigner.Modules.Comparators.Controls;

public sealed class ComparatorManualTestRowDialog : Form
{
    private readonly RichTextBox input = Editor();
    private readonly RichTextBox output = Editor(true);
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 8, 8, 0) };
    private readonly Button run = new() { Text = "Run 150 Rows", AutoSize = true, MinimumSize = new Size(115, 28) };
    private readonly Button copy = new() { Text = "Copy Results", AutoSize = true, MinimumSize = new Size(115, 28), Enabled = false };
    private readonly Button copyWithHeader = new() { Text = "Copy Header + Results", AutoSize = true, MinimumSize = new Size(155, 28), Enabled = false };

    public ComparatorManualTestRowDialog()
    {
        Text = "Comparators - 150 Test Points";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 560);
        MinimumSize = new Size(840, 440);
        Font = new Font("Segoe UI", 9F);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(6, 4, 6, 4) };
        var paste = new Button { Text = "Paste Test Points", AutoSize = true, MinimumSize = new Size(130, 28) };
        var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(80, 28) };
        paste.Click += (_, _) => { if (Clipboard.ContainsText()) input.Text = Clipboard.GetText().Trim(); };
        run.Click += async (_, _) => await RunAsync();
        copy.Click += (_, _) => Copy(false);
        copyWithHeader.Click += (_, _) => Copy(true);
        close.Click += (_, _) => Close();
        actions.Controls.Add(paste); actions.Controls.Add(run); actions.Controls.Add(copy); actions.Controls.Add(copyWithHeader); actions.Controls.Add(close); actions.Controls.Add(status);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "Input: paste up to 150 tab-separated comparator rows (header optional).", AutoSize = true }, 0, 0);
        root.Controls.Add(input, 0, 1);
        root.Controls.Add(new Label { Text = "Output: one completed 38-field row per input row, preserving input order.", AutoSize = true, Margin = new Padding(0, 8, 0, 2) }, 0, 2);
        root.Controls.Add(output, 0, 3); root.Controls.Add(actions, 0, 4); Controls.Add(root);
    }

    private async Task RunAsync()
    {
        run.Enabled = false; copy.Enabled = copyWithHeader.Enabled = false; UseWaitCursor = true;
        status.ForeColor = Color.DimGray; status.Text = "Parsing rows...";
        try
        {
            var service = new ComparatorManualTestRowService();
            var rows = service.ParseMany(input.Text);
            var progress = new Progress<int>(count => status.Text = $"Designing {count}/{rows.Count}...");
            var completed = await Task.Run(() =>
            {
                var engine = new ComparatorDesignEngine();
                var batch = new List<(string Row, bool Success)>();
                for (var index = 0; index < rows.Count; index++)
                {
                    var result = engine.Design(rows[index].Request);
                    batch.Add((service.Complete(rows[index], result), result.Success));
                    ((IProgress<int>)progress).Report(index + 1);
                }
                return batch;
            });
            output.Lines = completed.Select(item => item.Row).ToArray();
            copy.Enabled = copyWithHeader.Enabled = true;
            var succeeded = completed.Count(item => item.Success);
            status.ForeColor = succeeded == completed.Count ? Color.DarkGreen : Color.DarkOrange;
            status.Text = $"Completed {completed.Count}: {succeeded} succeeded, {completed.Count - succeeded} failed. Copy the results for analysis.";
        }
        catch (Exception exception)
        {
            status.ForeColor = Color.Firebrick; status.Text = exception.Message;
        }
        finally { run.Enabled = true; UseWaitCursor = false; }
    }

    private void Copy(bool includeHeader)
    {
        if (output.TextLength == 0) return;
        var text = includeHeader
            ? ComparatorManualTestRowService.Header + Environment.NewLine + output.Text.Trim()
            : output.Text.Trim();
        Clipboard.SetText(text);
    }

    private static RichTextBox Editor(bool readOnly = false) => new()
    {
        Dock = DockStyle.Fill, ReadOnly = readOnly, WordWrap = false, DetectUrls = false,
        Font = new Font("Consolas", 9F), BackColor = SystemColors.Window
    };
}
