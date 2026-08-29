using System.Text;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;
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
        tabs.TabPages.Add(CreateReportPage(result));
        Controls.Add(tabs);
    }

    private static TabPage CreateReportPage(FilterDesignResult result)
    {
        var page = new TabPage("Design Report") { BackColor = SystemColors.Window };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        var html = new Button { Text = "Export HTML Report...", AutoSize = true, MinimumSize = new Size(145, 28) };
        var json = new Button { Text = "Export JSON Snapshot...", AutoSize = true, MinimumSize = new Size(155, 28) };
        var service = new FilterDesignReportService();
        var report = service.Build(result);
        html.Click += (_, _) => ExportReport(page, result, service, false);
        json.Click += (_, _) => ExportReport(page, result, service, true);
        buttons.Controls.Add(html);
        buttons.Controls.Add(json);
        var preview = new WebBrowser
        {
            Dock = DockStyle.Fill,
            AllowWebBrowserDrop = false,
            IsWebBrowserContextMenuEnabled = true,
            ScriptErrorsSuppressed = true,
            WebBrowserShortcutsEnabled = true,
            DocumentText = new HtmlFilterReportExporter().Export(report)
        };
        layout.Controls.Add(buttons, 0, 0);
        layout.Controls.Add(preview, 0, 1);
        page.Controls.Add(layout);
        return page;
    }

    private static void ExportReport(Control owner, FilterDesignResult result, FilterDesignReportService service, bool json)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = json ? "Filter Design Snapshot (*.json)|*.json" : "Filter Design Report (*.html)|*.html",
            DefaultExt = json ? "json" : "html",
            AddExtension = true,
            FileName = BuildReportFileName(result, json)
        };
        if (dialog.ShowDialog(owner.FindForm()) != DialogResult.OK) return;
        try
        {
            var report = service.Build(result);
            if (json) service.SaveSnapshot(report, dialog.FileName);
            else service.SaveHtml(report, dialog.FileName);
            MessageBox.Show(owner, $"Report exported successfully.\r\n{dialog.FileName}", "Filter Design Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(owner, exception.Message, "Filter Design Report", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string BuildReportFileName(FilterDesignResult result, bool json)
    {
        var request = result.DesignRequest;
        var extension = json ? "json" : "html";
        return request == null ? $"filter-design.{extension}" : $"{request.Mode}-{request.ResponseType}-{request.Approximation}-{request.Implementation}.{extension}";
    }

    private static TabPage CreateAnalysisPage(FilterDesignResult result)
    {
        var page = new TabPage("Analysis") { BackColor = SystemColors.Window };
        page.Controls.Add(new FilterAnalysisPlotControl
        {
            Dock = DockStyle.Fill,
            Analysis = result.AnalysisResult,
            Request = result.DesignRequest
        });
        return page;
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
        text.AppendLine(result.Message); text.AppendLine($"Required order: {result.RequiredOrder}"); text.AppendLine($"Prototype order: {result.PrototypeOrder}"); text.AppendLine($"Realized order: {result.RealizedOrder}"); text.AppendLine($"Meets requested order: {result.MeetsRequestedOrder}"); text.AppendLine($"Final circuit meets edge specifications: {result.MeetsSpecifications}");
        if (result.NormalizedSpecification != null)
        {
            if (result.NormalizedSpecification.CenterAngularFrequency > 0)
            {
                text.AppendLine($"Center frequency: {result.NormalizedSpecification.CenterAngularFrequency / (2 * Math.PI):0.###} Hz");
                var passbandBandwidth = result.NormalizedSpecification.SpecificationBandwidthAngularFrequency > 0
                    ? result.NormalizedSpecification.SpecificationBandwidthAngularFrequency
                    : result.NormalizedSpecification.BandwidthAngularFrequency;
                text.AppendLine($"Passband bandwidth: {passbandBandwidth / (2 * Math.PI):0.###} Hz");
                if (Math.Abs(passbandBandwidth - result.NormalizedSpecification.BandwidthAngularFrequency) > passbandBandwidth * 1e-9)
                    text.AppendLine($"Prototype transformation bandwidth: {result.NormalizedSpecification.BandwidthAngularFrequency / (2 * Math.PI):0.###} Hz");
            }
            else text.AppendLine($"Cutoff: {result.NormalizedSpecification.CutoffAngularFrequency / (2 * Math.PI):0.###} Hz");
        }
        if (result.SpecificationCompliance != null)
        {
            text.AppendLine(); text.AppendLine("Final circuit edge verification:");
            foreach (var edge in result.SpecificationCompliance.Edges)
                text.AppendLine($"  {edge.Label} = {edge.FrequencyHz:0.###} Hz: actual {edge.ActualMagnitudeDb:0.###} dB, {(edge.IsPassband ? "minimum" : "maximum")} {edge.LimitMagnitudeDb:0.###} dB, {(edge.MeetsLimit ? "PASS" : "FAIL")}");
        }
        if (result.GainDistribution != null)
        {
            var gain = result.GainDistribution;
            text.AppendLine(); text.AppendLine("Gain distribution / whole-circuit verification:");
            text.AppendLine($"  Requested overall A0: {gain.RequestedOverallGain:0.######} V/V");
            text.AppendLine($"  Raw cascade section gain product: {gain.RawCascadeGainMagnitude:0.######}");
            text.AppendLine($"  Interstage/output scale product: {gain.InterstageScaleProduct:0.######}");
            text.AppendLine($"  Actual passband peak: {gain.ActualPassbandPeakGain:0.######} V/V");
            text.AppendLine($"  Expected polarity: {gain.ExpectedOverallPolarity}; reference phase: {gain.ReferencePhaseDegrees:0.###} deg");
        }
        text.AppendLine(); text.AppendLine("Prototype poles:"); foreach (var pole in result.PrototypePoles) text.AppendLine($"  {pole.Real,12:0.######} {pole.Imaginary,12:+0.######;-0.######}j");
        if (result.PrototypeElementValues.Count > 0) { text.AppendLine(); text.AppendLine("Normalized ladder g-values:"); for (var i=0;i<result.PrototypeElementValues.Count;i++) text.AppendLine($"  g{i} = {result.PrototypeElementValues[i]:0.######}"); }
        text.AppendLine(); text.AppendLine("Sections:"); foreach (var section in result.Sections) { text.AppendLine($"  Section {section.Target.Index}: {section.Target.Order} {section.Target.ResponseType}, target f0={section.Target.PoleFrequencyHz:0.###} Hz, Q={section.Target.Q:0.######}, target topology gain={section.Target.SectionGain:0.######}"); text.AppendLine($"    actual f0={section.ActualPoleFrequencyHz:0.###} Hz, Q={section.ActualQ:0.######}, intrinsic gain={section.IntrinsicTopologyGain:0.######}, interstage scale={section.InterstageScale:0.######}, polarity={(section.PolaritySign < 0 ? "inverting" : "non-inverting")}, errors f={section.FrequencyError:P3}, Q={section.QError:P3}, gain={section.GainError:P3}"); foreach (var value in section.Components) text.AppendLine($"    {value.Role}: theoretical={Format(value.TheoreticalValue, value.ComponentType)}, selected={Format(value.SelectedValue, value.ComponentType)}, error={value.RelativeError:P3}"); }
        if(result.AdditionalNetworkComponents.Count>0){text.AppendLine();text.AppendLine("Network components:");foreach(var value in result.AdditionalNetworkComponents)text.AppendLine($"  {value.Role}: theoretical={Format(value.TheoreticalValue,value.ComponentType)}, selected={Format(value.SelectedValue,value.ComponentType)}, error={value.RelativeError:P3}");}
        if (result.CircuitGraph != null) { text.AppendLine(); text.AppendLine($"CircuitGraph: {result.CircuitGraph.Components.Count} components, {result.CircuitGraph.Nets.Count} nets"); foreach (var component in result.CircuitGraph.Components) { text.AppendLine($"  {component.Reference} {component.Type}"); foreach (var parameter in component.Parameters.Values) text.AppendLine($"    {parameter.Key} = {parameter.Value:G9}"); } }
        foreach (var warning in result.Warnings) text.AppendLine($"WARNING: {warning}"); return text.ToString();
    }

    private static string Format(double value, ComponentType type) => type switch { ComponentType.Resistor => $"{value:0.######} Ω", ComponentType.Capacitor => $"{value:E6} F", ComponentType.Inductor => $"{value:E6} H", _ => value.ToString("G6") };
}
