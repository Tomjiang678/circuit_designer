using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Engines;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Services;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class PrecisionAnalogManualTestRowDialog : Form
{
    private readonly RichTextBox input = Editor();
    private readonly RichTextBox output = Editor(true);
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 8, 8, 0) };
    private readonly Button run = new() { Text = "Run 500 Rows", AutoSize = true, MinimumSize = new Size(115, 28) };
    private readonly Button copy = new() { Text = "Copy Results", AutoSize = true, MinimumSize = new Size(115, 28), Enabled = false };
    private readonly Button copyWithHeader = new() { Text = "Copy Header + Results", AutoSize = true, MinimumSize = new Size(150, 28), Enabled = false };

    public PrecisionAnalogManualTestRowDialog()
    {
        Text = "Precision Analog - 500 Test Points";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 620);
        MinimumSize = new Size(840, 460);
        Font = new Font("Segoe UI", 9F);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(6, 4, 6, 4) };
        var paste = new Button { Text = "Paste Test Points", AutoSize = true, MinimumSize = new Size(130, 28) };
        var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(80, 28) };
        paste.Click += (_, _) => { if (Clipboard.ContainsText()) input.Text = Clipboard.GetText().Trim(); };
        run.Click += async (_, _) => await RunAsync();
        copy.Click += (_, _) => CopyResults(false);
        copyWithHeader.Click += (_, _) => CopyResults(true);
        close.Click += (_, _) => Close();
        actions.Controls.Add(paste); actions.Controls.Add(run); actions.Controls.Add(copy); actions.Controls.Add(copyWithHeader); actions.Controls.Add(close); actions.Controls.Add(status);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 57)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(new Label { Text = "Input: paste up to 500 tab-separated TXT rows (the header is optional).", AutoSize = true }, 0, 0);
        root.Controls.Add(input, 0, 1);
        root.Controls.Add(new Label { Text = "Output: one completed 57-field row per input row, preserving input order.", AutoSize = true, Margin = new Padding(0, 8, 0, 2) }, 0, 2);
        root.Controls.Add(output, 0, 3);
        root.Controls.Add(actions, 0, 4);
        Controls.Add(root);
    }

    private async Task RunAsync()
    {
        run.Enabled = false; copy.Enabled = false; copyWithHeader.Enabled = false; UseWaitCursor = true;
        status.ForeColor = Color.DimGray; status.Text = "Parsing rows...";
        try
        {
            var service = new PrecisionAnalogManualTestRowService();
            var rows = service.ParseMany(input.Text);
            var progress = new Progress<int>(count => status.Text = $"Designing {count}/{rows.Count}...");
            var completed = await Task.Run(() =>
            {
                var engine = new PrecisionAnalogDesignEngine();
                var resultRows = new List<(string Text, bool Success)>(rows.Count);
                for (var index = 0; index < rows.Count; index++)
                {
                    var result = engine.Design(rows[index].Request);
                    resultRows.Add((service.Complete(rows[index], result), result.Success));
                    ((IProgress<int>)progress).Report(index + 1);
                }
                return resultRows;
            });
            output.Lines = completed.Select(item => item.Text).ToArray();
            copy.Enabled = true; copyWithHeader.Enabled = true;
            var passed = completed.Count(item => item.Success);
            status.ForeColor = passed == completed.Count ? Color.DarkGreen : Color.DarkOrange;
            status.Text = $"Completed {completed.Count}: {passed} succeeded, {completed.Count - passed} failed/unsupported.";
        }
        catch (Exception exception)
        {
            status.ForeColor = Color.Firebrick; status.Text = exception.Message;
        }
        finally { run.Enabled = true; UseWaitCursor = false; }
    }

    private void CopyResults(bool includeHeader)
    {
        if (output.TextLength == 0) return;
        Clipboard.SetText(includeHeader
            ? PrecisionAnalogManualTestRowService.Header + Environment.NewLine + output.Text.Trim()
            : output.Text.Trim());
    }

    private static RichTextBox Editor(bool readOnly = false) => new()
    {
        Dock = DockStyle.Fill, ReadOnly = readOnly, WordWrap = false, DetectUrls = false,
        Font = new Font("Consolas", 9F), BackColor = SystemColors.Window
    };
}
