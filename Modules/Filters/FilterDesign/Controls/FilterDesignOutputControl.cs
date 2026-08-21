using System.Text;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Services;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterDesignOutputControl : UserControl
{
    public FilterDesignOutputControl(FilterDesignResult result)
    {
        Dock = DockStyle.Fill;
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
        var schematicPage = new TabPage("Schematic") { BackColor = SystemColors.Window };
        schematicPage.Controls.Add(new FilterSchematicControl { Dock = DockStyle.Fill, Document = result.SchematicDocument });
        tabs.TabPages.Add(schematicPage);
        tabs.TabPages.Add(CreateAnalysisPage(result));
        var calculations = new TabPage("Calculations") { BackColor = SystemColors.Window };
        calculations.Controls.Add(new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window, Text = BuildCalculations(result) });
        tabs.TabPages.Add(calculations);
        tabs.TabPages.Add(CreateSpicePage(result));
        Controls.Add(tabs);
    }

    private static TabPage CreateAnalysisPage(FilterDesignResult result)
    {
        var page = new TabPage("Analysis") { BackColor = SystemColors.Window };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new FilterAnalysisPlotControl { Dock = DockStyle.Fill, Analysis = result.AnalysisResult, Request = result.DesignRequest }, 0, 0);
        var status = result.AnalysisResult == null
            ? "AC analysis unavailable."
            : $"Sweep: {result.AnalysisResult.StartFrequencyHz:G5} Hz to {result.AnalysisResult.StopFrequencyHz:G5} Hz, {result.AnalysisResult.Points.Count} points. Reference: {result.AnalysisResult.MagnitudeReferenceDescription}. Move the mouse over the plot; click to pin/unpin the cursor.  " + string.Join(" ", result.AnalysisResult.Warnings);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8, 5, 8, 5), ForeColor = Color.DarkGoldenrod, Text = status }, 0, 1);
        page.Controls.Add(layout); return page;
    }

    private static TabPage CreateSpicePage(FilterDesignResult result)
    {
        var page = new TabPage("SPICE Netlist") { BackColor = SystemColors.Window };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(5, 4, 5, 4) };
        var save = new Button { Text = "Save CIR...", AutoSize = true, MinimumSize = new Size(95, 26) };
        var copy = new Button { Text = "Copy Netlist", AutoSize = true, MinimumSize = new Size(100, 26) };
        var note = new Label { AutoSize = true, Margin = new Padding(12, 6, 0, 0), ForeColor = Color.DimGray, Text = "Multisim: File > Open > select the exported .cir file. AC directives may be ignored during schematic import." };
        var netlist = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window, Text = result.SpiceNetlist };
        save.Click += (_, _) =>
        {
            using var dialog = new SaveFileDialog { Filter = "SPICE Circuit (*.cir)|*.cir|All files (*.*)|*.*", DefaultExt = "cir", AddExtension = true, FileName = BuildCirFileName(result) };
            if (dialog.ShowDialog(page.FindForm()) != DialogResult.OK) return;
            try { new FilterSpiceExportService().Save(result, dialog.FileName); }
            catch (Exception exception) { MessageBox.Show(page, exception.Message, "CIR Export", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        copy.Click += (_, _) => { if (netlist.TextLength > 0) Clipboard.SetText(netlist.Text); };
        toolbar.Controls.Add(save); toolbar.Controls.Add(copy); toolbar.Controls.Add(note);
        layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(netlist, 0, 1); page.Controls.Add(layout); return page;
    }

    private static string BuildCirFileName(FilterDesignResult result)
    {
        var request = result.DesignRequest;
        return request == null ? "filter-design.cir" : $"{request.Mode}-{request.ResponseType}-{request.Implementation}.cir";
    }

    private static TabPage CreateMessagePage(string title, string text)
    {
        var page = new TabPage(title) { BackColor = SystemColors.Window };
        page.Controls.Add(new Label { Dock = DockStyle.Fill, Text = text, TextAlign = ContentAlignment.MiddleCenter });
        return page;
    }

    private static string BuildCalculations(FilterDesignResult result)
    {
        var text = new StringBuilder();
        text.AppendLine(result.Message); text.AppendLine($"Required order: {result.RequiredOrder}"); text.AppendLine($"Prototype order: {result.PrototypeOrder}"); text.AppendLine($"Realized order: {result.RealizedOrder}"); text.AppendLine($"Meets requested order: {result.MeetsRequestedOrder}");
        if (result.NormalizedSpecification != null)
        {
            if (result.NormalizedSpecification.CenterAngularFrequency > 0)
            {
                text.AppendLine($"Center frequency: {result.NormalizedSpecification.CenterAngularFrequency / (2 * Math.PI):0.###} Hz");
                text.AppendLine($"Passband bandwidth: {result.NormalizedSpecification.BandwidthAngularFrequency / (2 * Math.PI):0.###} Hz");
            }
            else text.AppendLine($"Cutoff: {result.NormalizedSpecification.CutoffAngularFrequency / (2 * Math.PI):0.###} Hz");
        }
        text.AppendLine(); text.AppendLine("Prototype poles:"); foreach (var pole in result.PrototypePoles) text.AppendLine($"  {pole.Real,12:0.######} {pole.Imaginary,12:+0.######;-0.######}j");
        if (result.PrototypeElementValues.Count > 0) { text.AppendLine(); text.AppendLine("Normalized ladder g-values:"); for (var i=0;i<result.PrototypeElementValues.Count;i++) text.AppendLine($"  g{i} = {result.PrototypeElementValues[i]:0.######}"); }
        text.AppendLine(); text.AppendLine("Sections:"); foreach (var section in result.Sections) { text.AppendLine($"  Section {section.Target.Index}: {section.Target.Order} {section.Target.ResponseType}, target f0={section.Target.PoleFrequencyHz:0.###} Hz, Q={section.Target.Q:0.######}, gain={section.Target.SectionGain:0.######}"); text.AppendLine($"    actual f0={section.ActualPoleFrequencyHz:0.###} Hz, Q={section.ActualQ:0.######}, gain={section.ActualGain:0.######}, errors f={section.FrequencyError:P3}, Q={section.QError:P3}, gain={section.GainError:P3}"); foreach (var value in section.Components) text.AppendLine($"    {value.Role}: theoretical={Format(value.TheoreticalValue, value.ComponentType)}, selected={Format(value.SelectedValue, value.ComponentType)}, error={value.RelativeError:P3}"); }
        if(result.AdditionalNetworkComponents.Count>0){text.AppendLine();text.AppendLine("Network components:");foreach(var value in result.AdditionalNetworkComponents)text.AppendLine($"  {value.Role}: theoretical={Format(value.TheoreticalValue,value.ComponentType)}, selected={Format(value.SelectedValue,value.ComponentType)}, error={value.RelativeError:P3}");}
        if (result.CircuitGraph != null) { text.AppendLine(); text.AppendLine($"CircuitGraph: {result.CircuitGraph.Components.Count} components, {result.CircuitGraph.Nets.Count} nets"); foreach (var component in result.CircuitGraph.Components) { text.AppendLine($"  {component.Reference} {component.Type}"); foreach (var parameter in component.Parameters.Values) text.AppendLine($"    {parameter.Key} = {parameter.Value:G9}"); } }
        foreach (var warning in result.Warnings) text.AppendLine($"WARNING: {warning}"); return text.ToString();
    }

    private static string Format(double value, ComponentType type) => type switch { ComponentType.Resistor => $"{value:0.######} Ω", ComponentType.Capacitor => $"{value:E6} F", ComponentType.Inductor => $"{value:E6} H", _ => value.ToString("G6") };
}
