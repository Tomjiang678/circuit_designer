using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls.SpecificationPreview;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterSummaryControl : FilterStepBase
{
    private readonly SplitContainer split;
    private bool configuringSplit;
    public event EventHandler? DesignRequested;

    public FilterSummaryControl(FilterDesignState state) : base(state)
    {
        AutoScroll = false;
        var summary = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SystemColors.Window,
            Font = new Font(Font, FontStyle.Regular),
            Text = BuildText()
        };
        var plot = new FilterSpecPlotControl
        {
            Dock = DockStyle.Fill,
            Model = FilterSpecPlotBuilder.Build(state)
        };

        split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            Panel1MinSize = 0,
            Panel2MinSize = 0
        };
        split.Panel1.Controls.Add(summary);
        split.Panel2.Controls.Add(plot);
        split.Resize += (_, _) => ConfigureSplit();

        var request = FilterDesignRequest.FromState(state);
        var availability = new FilterDesignEngine().GetAvailability(request);
        var designButton = new Button
        {
            Text = "Design Filter",
            Dock = DockStyle.Fill,
            AutoSize = true,
            MinimumSize = new Size(120, 28),
            Enabled = availability.CanDesign
        };
        designButton.Click += (_, _) => DesignRequested?.Invoke(this, EventArgs.Empty);
        var unavailableReason = new Label
        {
            Text = availability.Message,
            AutoSize = true,
            ForeColor = availability.CanDesign ? Color.DarkGreen : Color.Firebrick,
            Margin = new Padding(6, 7, 10, 0)
        };
        var commandBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(6),
            Margin = Padding.Empty
        };
        commandBar.Controls.Add(designButton);
        commandBar.Controls.Add(unavailableReason);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(split, 0, 0);
        root.Controls.Add(commandBar, 0, 1);
        Controls.Add(root);
        HandleCreated += (_, _) => BeginInvoke(ConfigureSplit);
    }

    private void ConfigureSplit()
    {
        if (configuringSplit || split.ClientSize.Width <= 2 || split.ClientSize.Height <= 2)
            return;
        configuringSplit = true;
        try
        {
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            var sideBySide = split.ClientSize.Width >= 860;
            split.Orientation = sideBySide ? Orientation.Vertical : Orientation.Horizontal;
            var available = (sideBySide ? split.ClientSize.Width : split.ClientSize.Height) - split.SplitterWidth;
            if (available <= 2)
                return;
            split.SplitterDistance = Math.Clamp(
                sideBySide ? Math.Max(430, (int)(available * 0.48)) : 240,
                1,
                Math.Max(1, available - 1));
        }
        finally
        {
            configuringSplit = false;
        }
    }

    private string BuildText() =>
        $"Filter Design Summary\r\n\r\n" +
        $"Mode: {DisplayName(State.Mode)}\r\n" +
        $"Response: {DisplayName(State.ResponseType)}\r\n" +
        $"Approximation: {DisplayName(State.Approximation)}\r\n" +
        $"Implementation: {DisplayName(State.Implementation)}\r\n" +
        $"Order: {State.OrderMode}" +
        $"{(State.OrderMode == FilterOrderMode.SpecifyOrder ? $" ({State.SpecifiedOrder})" : string.Empty)}\r\n\r\n" +
        $"Rp: {State.PassbandRippleDb:0.##} dB\r\n" +
        $"As/Asb: {State.StopbandAttenuationDb:0.##} dB\r\n" +
        $"A0: {State.PassbandGain:0.###} V/V\r\n" +
        FrequencySummary() + "\r\n" + ScalingSummary() +
        $"Resistor series: {State.ResistorSeries} +/-{State.ResistorTolerancePercent:0.##}%\r\n" +
        $"Capacitor series: {State.CapacitorSeries} +/-{State.CapacitorTolerancePercent:0.##}%\r\n" +
        (State.Mode==FilterMode.Passive&&State.Implementation==FilterImplementationType.Lc?$"Inductor series: {State.InductorSeries} +/-{State.InductorTolerancePercent:0.##}%\r\n":string.Empty)+
        $"Optimize standard values: {State.OptimizeStandardValues}";

    private string ScalingSummary()=>State.Mode switch
    {
        FilterMode.Active=>$"R range: {State.PreferredResistorMinOhm:G6} to {State.PreferredResistorMaxOhm:G6} ohm\r\nC range: {State.PreferredCapacitorMinF:G6} to {State.PreferredCapacitorMaxF:G6} F\r\n",
        _ when State.Implementation==FilterImplementationType.Lc=>$"Rs: {State.SourceResistanceOhm:G6} ohm\r\nRL: {State.LoadResistanceOhm:G6} ohm\r\nL range: {State.InductorMinimumH:G6} to {State.InductorMaximumH:G6} H\r\nLC C range: {State.PassiveLcCapacitorMinimumF:G6} to {State.PassiveLcCapacitorMaximumF:G6} F\r\n",
        _=>$"Preferred resistance: {State.PreferredResistanceOhm:G6} ohm\r\n"
    };

    private string FrequencySummary() => State.ResponseType switch
    {
        FilterResponseType.LowPass or FilterResponseType.HighPass => $"fp: {State.PassbandEdgeHz:0.###} Hz\r\nfs: {State.StopbandEdgeHz:0.###} Hz\r\n",
        FilterResponseType.BandPass => $"fs1: {State.StopbandLowerHz:0.###} Hz\r\nfp1: {State.PassbandLowerHz:0.###} Hz\r\nfp2: {State.PassbandUpperHz:0.###} Hz\r\nfs2: {State.StopbandUpperHz:0.###} Hz\r\n",
        _ => $"fp1: {State.PassbandLowerHz:0.###} Hz\r\nfs1: {State.StopbandLowerHz:0.###} Hz\r\nfs2: {State.StopbandUpperHz:0.###} Hz\r\nfp2: {State.PassbandUpperHz:0.###} Hz\r\n"
    };
}
