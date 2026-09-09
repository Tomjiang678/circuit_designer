using System.Diagnostics;
using System.Text;
using CircuitModuleDesigner.Controls;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Services;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterDesignOutputControl : UserControl
{
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill, Padding = new Point(12, 4) };
    private readonly TabPage schematicPage = new("Schematic") { BackColor = SystemColors.Window };
    private readonly TabPage analysisPage = new("Analysis") { BackColor = SystemColors.Window };
    private readonly TabPage calculationsPage = new("Calculations") { BackColor = SystemColors.Window };
    private readonly TabPage spicePage = new("SPICE Netlist") { BackColor = SystemColors.Window };
    private readonly TabPage reportPage = new("Design Report") { BackColor = SystemColors.Window };
    private readonly FilterSchematicControl schematic = new() { Dock = DockStyle.Fill };
    private readonly Panel analysisHost = new() { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private readonly RichTextBox calculations = new() { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window };
    private readonly RichTextBox netlist = new() { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font("Consolas", 9F), BackColor = SystemColors.Window };
    private readonly Panel reportHost = new() { Dock = DockStyle.Fill, BackColor = SystemColors.Window };
    private FilterDesignResult currentResult;
    private WebBrowser? reportBrowser;
    private bool reportDirty = true;

    public FilterDesignOutputControl(FilterDesignResult result)
    {
        currentResult = result;
        Dock = DockStyle.Fill;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        schematicPage.Controls.Add(schematic); analysisPage.Controls.Add(analysisHost); calculationsPage.Controls.Add(calculations);
        BuildSpicePage(); BuildReportPage();
        tabs.TabPages.AddRange([schematicPage, analysisPage, calculationsPage, spicePage, reportPage]);
        tabs.SelectedIndexChanged += (_, _) => EnsureReportPreview();
        Controls.Add(tabs);
        UpdateResult(result);
    }

    public void UpdateResult(FilterDesignResult result)
    {
        currentResult = result;
        SuspendLayout(); tabs.SuspendLayout(); analysisHost.SuspendLayout();
        try
        {
            var stageWatch = Stopwatch.StartNew();
            schematic.Document = result.SchematicDocument;
            stageWatch.Stop(); UiPerformanceTrace.Stage("Filter", "RenderSchematic", stageWatch.Elapsed); stageWatch.Restart();
            UiControlHost.ReplaceSingleChild(analysisHost, CreateAnalysisControl(result));
            stageWatch.Stop(); UiPerformanceTrace.Stage("Filter", "RenderAnalysis", stageWatch.Elapsed); stageWatch.Restart();
            calculations.Text = BuildCalculations(result);
            stageWatch.Stop(); UiPerformanceTrace.Stage("Filter", "RenderCalculations", stageWatch.Elapsed); stageWatch.Restart();
            netlist.Text = result.SpiceNetlist;
            stageWatch.Stop(); UiPerformanceTrace.Stage("Filter", "RenderNetlist", stageWatch.Elapsed);
            reportDirty = true;
        }
        finally { analysisHost.ResumeLayout(false); tabs.ResumeLayout(false); ResumeLayout(false); PerformLayout(); }
        EnsureReportPreview();
    }

    private void BuildSpicePage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty }; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(5, 4, 5, 4) };
        var save = new Button { Text = "Save CIR...", AutoSize = true, MinimumSize = new Size(95, 26) }; var copy = new Button { Text = "Copy Netlist", AutoSize = true, MinimumSize = new Size(100, 26) };
        save.Click += (_, _) => SaveCurrentCir(); copy.Click += (_, _) => { if (netlist.TextLength > 0) Clipboard.SetText(netlist.Text); }; toolbar.Controls.Add(save); toolbar.Controls.Add(copy);
        toolbar.Controls.Add(new Label { AutoSize = true, Margin = new Padding(12, 6, 0, 0), ForeColor = Color.DimGray, Text = "Generic SPICE text netlist; usable as LTspice/Multisim .cir or .net." });
        layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(netlist, 0, 1); spicePage.Controls.Add(layout);
    }

    private void BuildReportPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) }; layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false }; var html = new Button { Text = "Export HTML Report...", AutoSize = true, MinimumSize = new Size(145, 28) }; var json = new Button { Text = "Export JSON Snapshot...", AutoSize = true, MinimumSize = new Size(155, 28) };
        html.Click += (_, _) => ExportReport(this, currentResult, new FilterDesignReportService(), false); json.Click += (_, _) => ExportReport(this, currentResult, new FilterDesignReportService(), true); buttons.Controls.Add(html); buttons.Controls.Add(json);
        layout.Controls.Add(buttons, 0, 0); layout.Controls.Add(reportHost, 0, 1); reportPage.Controls.Add(layout);
    }

    private void SaveCurrentCir()
    {
        using var dialog = new SaveFileDialog { Filter = "SPICE Circuit (*.cir)|*.cir|SPICE Netlist (*.net)|*.net|All files (*.*)|*.*", DefaultExt = "cir", AddExtension = true, FileName = BuildCirFileName(currentResult) };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { new FilterSpiceExportService().Save(currentResult, dialog.FileName); } catch (Exception exception) { MessageBox.Show(this, exception.Message, "CIR Export", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void EnsureReportPreview()
    {
        if (tabs.SelectedTab != reportPage || !reportDirty) return;
        reportHost.SuspendLayout();
        try
        {
            reportBrowser ??= new WebBrowser { Dock = DockStyle.Fill, AllowWebBrowserDrop = false, IsWebBrowserContextMenuEnabled = true, ScriptErrorsSuppressed = true, WebBrowserShortcutsEnabled = true };
            if (reportBrowser.Parent is null) reportHost.Controls.Add(reportBrowser);
            var watch = Stopwatch.StartNew(); var report = new FilterDesignReportService().Build(currentResult); reportBrowser.DocumentText = new HtmlFilterReportExporter().Export(report); watch.Stop();
            UiPerformanceTrace.Stage("Filter", "RenderReport", watch.Elapsed); reportDirty = false;
        }
        finally { reportHost.ResumeLayout(false); }
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

    private static Control CreateAnalysisControl(FilterDesignResult result)
    {
        var plot = new FilterAnalysisPlotControl
        {
            Dock = DockStyle.Fill,
            Analysis = result.AnalysisResult,
            Request = result.DesignRequest
        };
        if (result.PassiveAnalysis == null)
            return plot;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new Padding(7, 5, 7, 4) };
        toolbar.Controls.Add(new Label { Text = "Response:", AutoSize = true, Margin = new Padding(0, 6, 5, 0) });
        var selector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360, DropDownWidth = 390 };
        var responses = new List<(string Label, PassiveResponseReference Reference, Core.Analysis.AcAnalysisResult Analysis)>
        {
            ("Absolute V (VOUT/VSRC)", PassiveResponseReference.AbsoluteVoltageTransfer, result.PassiveAnalysis.AbsoluteVoltageTransfer),
            ("Filter Node (VOUT/VIN)", PassiveResponseReference.FilterNodeVoltageTransfer, result.PassiveAnalysis.FilterNodeVoltageTransfer),
            ("Insertion (Through = 0 dB)", PassiveResponseReference.InsertionLossRelativeToThrough, result.PassiveAnalysis.InsertionLossRelativeToThrough)
        };
        if (result.PassiveAnalysis.TransducerGain is { } transducer)
            responses.Insert(2, ("Transducer Gain", PassiveResponseReference.TransducerGain, transducer));
        selector.Items.AddRange(responses.Select(value => value.Label).Cast<object>().ToArray());
        selector.SelectedIndex = Math.Max(0, responses.FindIndex(value => value.Reference == result.PassiveAnalysis.PrimaryReference));
        selector.SelectedIndexChanged += (_, _) =>
        {
            if (selector.SelectedIndex >= 0) plot.Analysis = responses[selector.SelectedIndex].Analysis;
        };
        toolbar.Controls.Add(selector);
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(plot, 0, 1);
        return layout;
    }

    private static string BuildCirFileName(FilterDesignResult result)
    {
        var request = result.DesignRequest;
        return request == null ? "filter-design.cir" : $"{request.Mode}-{request.ResponseType}-{request.Implementation}.cir";
    }

    private static string BuildCalculations(FilterDesignResult result)
    {
        var text = new StringBuilder();
        text.AppendLine(result.Message); text.AppendLine($"Required order: {result.RequiredOrder}"); text.AppendLine($"Prototype order: {result.PrototypeOrder}"); text.AppendLine($"Realized order: {result.RealizedOrder}"); text.AppendLine($"Meets requested order: {result.MeetsRequestedOrder}"); text.AppendLine($"Final circuit meets edge specifications: {result.MeetsSpecifications}");
        if (result.StageDiagnostics is { } stages)
            text.AppendLine($"Verification stages: theoretical circuit valid={TriState(stages.TheoreticalCircuitValid)}, theoretical meets specification={TriState(stages.TheoreticalMeetsSpecifications)}, selected circuit valid={stages.SelectedCircuitValid}, selected meets specification={stages.SelectedMeetsSpecifications}, failure stage={stages.FailureStage}");
        if (result.PassiveAnalysis is { } passive)
        {
            text.AppendLine(); text.AppendLine("Source / Load / Response Reference:");
            text.AppendLine("  Boundary: VSRC -> Rs -> VIN -> filter network -> VOUT -> RL -> GND");
            text.AppendLine("  VSRC: ideal Thevenin open-circuit voltage");
            text.AppendLine($"  Rs = {passive.Boundary.SourceResistanceOhm:G9} Ω; RL = {passive.Boundary.LoadResistanceOhm:G9} Ω");
            text.AppendLine($"  Direct-through VOUT/VSRC = {passive.Boundary.DirectThroughVoltageRatio:G9} ({20 * Math.Log10(passive.Boundary.DirectThroughVoltageRatio):0.###} dB absolute; 0 dB normalized insertion response)");
            text.AppendLine($"  Primary response: {passive.PrimaryReference}");
            text.AppendLine(passive.TransducerGain == null
                ? "  Transducer gain: undefined for Rs = 0"
                : "  Transducer gain: PL/Pavs = 4·Rs/RL·|VOUT/VSRC|²");
        }
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
        if (result.DesignRequest?.Mode == Enums.FilterMode.Active && result.GainDistribution != null)
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
        if (result.PrototypeElementValues.Count > 0) { text.AppendLine(); text.AppendLine("Normalized ladder g-values:"); for (var i = 0; i < result.PrototypeElementValues.Count; i++) text.AppendLine($"  g{i} = {result.PrototypeElementValues[i]:0.######}"); }
        text.AppendLine(); text.AppendLine("Sections:"); foreach (var section in result.Sections) { text.AppendLine($"  Section {section.Target.Index}: {section.Target.Order} {section.Target.ResponseType}, target f0={section.Target.PoleFrequencyHz:0.###} Hz, Q={section.Target.Q:0.######}, target topology gain={section.Target.SectionGain:0.######}"); text.AppendLine($"    actual f0={section.ActualPoleFrequencyHz:0.###} Hz, Q={section.ActualQ:0.######}, intrinsic gain={section.IntrinsicTopologyGain:0.######}, interstage scale={section.InterstageScale:0.######}, polarity={(section.PolaritySign < 0 ? "inverting" : "non-inverting")}, errors f={section.FrequencyError:P3}, Q={section.QError:P3}, gain={section.GainError:P3}"); foreach (var value in section.Components) text.AppendLine($"    {value.Role}: theoretical={Format(value.TheoreticalValue, value.ComponentType)}, selected={Format(value.SelectedValue, value.ComponentType)}, error={value.RelativeError:P3}"); }
        if (result.AdditionalNetworkComponents.Count > 0) { text.AppendLine(); text.AppendLine("Network components:"); foreach (var value in result.AdditionalNetworkComponents) text.AppendLine($"  {value.Role}: theoretical={Format(value.TheoreticalValue, value.ComponentType)}, selected={Format(value.SelectedValue, value.ComponentType)}, error={value.RelativeError:P3}"); }
        if (result.CircuitGraph != null) { text.AppendLine(); text.AppendLine($"CircuitGraph: {result.CircuitGraph.Components.Count} components, {result.CircuitGraph.Nets.Count} nets"); foreach (var component in result.CircuitGraph.Components) { text.AppendLine($"  {component.Reference} {component.Type}"); foreach (var parameter in component.Parameters.Values) text.AppendLine($"    {parameter.Key} = {parameter.Value:G9}"); } }
        foreach (var warning in result.Warnings) text.AppendLine($"WARNING: {warning}"); return text.ToString();
    }

    private static string TriState(bool? value) => value switch { true => "True", false => "False", null => "NotEvaluated" };

    private static string Format(double value, ComponentType type) => type switch { ComponentType.Resistor => $"{value:0.######} Ω", ComponentType.Capacitor => $"{value:E6} F", ComponentType.Inductor => $"{value:E6} H", _ => value.ToString("G6") };
}
