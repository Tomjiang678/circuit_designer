using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls.SpecificationPreview;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterSpecificationsStepControl : FilterStepBase
{
    private readonly FilterDesignState state;
    private readonly TableLayoutPanel inputLayout;
    private readonly FilterSpecPlotControl plot;
    private readonly SplitContainer split;
    private readonly NumericUpDown rp;
    private readonly NumericUpDown attenuation;
    private readonly NumericUpDown gain;
    private readonly EngineeringValueInput fp;
    private readonly EngineeringValueInput fs;
    private readonly EngineeringValueInput fs1;
    private readonly EngineeringValueInput fp1;
    private readonly EngineeringValueInput fp2;
    private readonly EngineeringValueInput fs2;
    private bool configuringSplit;

    public FilterSpecificationsStepControl(FilterDesignState state) : base(state)
    {
        this.state = state;
        AutoScroll = false;
        inputLayout = FormLayout();

        rp = Number(state.PassbandRippleDb, 0, 100);
        attenuation = Number(state.StopbandAttenuationDb, 0, 200);
        gain = Number(state.PassbandGain, 0.001, 1000);
        fp = Frequency(state.PassbandEdgeHz);
        fs = Frequency(state.StopbandEdgeHz);
        fs1 = Frequency(state.StopbandLowerHz);
        fp1 = Frequency(state.PassbandLowerHz);
        fp2 = Frequency(state.PassbandUpperHz);
        fs2 = Frequency(state.StopbandUpperHz);

        AddRow(inputLayout, "Allowable Passband Ripple (Rp), dB:", rp);
        AddRow(inputLayout, "Stopband Attenuation (As/Asb), dB:", attenuation);
        if (state.Mode == FilterMode.Active)
            AddRow(inputLayout, "Passband Gain (A0), V/V:", gain);
        BuildFrequencyRows();

        foreach (var number in new[] { rp, attenuation, gain })
            number.ValueChanged += (_, _) => UpdateStateAndPreview();
        foreach (var frequency in new[] { fp, fs, fs1, fp1, fp2, fs2 })
            frequency.ValueChanged += (_, _) => UpdateStateAndPreview();

        var inputGroup = new GroupBox
        {
            Text = "Filter Specifications",
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 18, 8, 8)
        };
        inputGroup.Controls.Add(inputLayout);

        plot = new FilterSpecPlotControl { Dock = DockStyle.Fill };
        plot.Model = FilterSpecPlotBuilder.Build(state);
        var previewGroup = new GroupBox
        {
            Text = "Specification Preview",
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 18, 6, 6)
        };
        previewGroup.Controls.Add(plot);

        split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            Panel1MinSize = 0,
            Panel2MinSize = 0
        };
        split.Panel1.AutoScroll = true;
        split.Panel1.Controls.Add(inputGroup);
        split.Panel2.Controls.Add(previewGroup);
        split.Resize += (_, _) => ConfigureSplit();
        Controls.Add(split);
        HandleCreated += (_, _) => BeginInvoke(ConfigureSplit);
    }

    private void BuildFrequencyRows()
    {
        if (state.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass)
        {
            AddRow(inputLayout, "Passband Edge Frequency (fp):", fp);
            AddRow(inputLayout, "Stopband Edge Frequency (fs):", fs);
            return;
        }

        if (state.ResponseType == FilterResponseType.BandPass)
        {
            AddRow(inputLayout, "Lower Stopband Edge (fs1):", fs1);
            AddRow(inputLayout, "Lower Passband Edge (fp1):", fp1);
            AddRow(inputLayout, "Upper Passband Edge (fp2):", fp2);
            AddRow(inputLayout, "Upper Stopband Edge (fs2):", fs2);
        }
        else
        {
            AddRow(inputLayout, "Lower Passband Edge (fp1):", fp1);
            AddRow(inputLayout, "Lower Stopband Edge (fs1):", fs1);
            AddRow(inputLayout, "Upper Stopband Edge (fs2):", fs2);
            AddRow(inputLayout, "Upper Passband Edge (fp2):", fp2);
        }
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
            var useSideBySide = split.ClientSize.Width >= 860;
            split.Orientation = useSideBySide ? Orientation.Vertical : Orientation.Horizontal;

            var available = (useSideBySide ? split.ClientSize.Width : split.ClientSize.Height) - split.SplitterWidth;
            if (available <= 2)
                return;

            var preferred = useSideBySide ? 470 : 235;
            var minimumFirst = useSideBySide ? 400 : 170;
            var minimumSecond = useSideBySide ? 370 : 220;
            var maximum = Math.Max(1, available - minimumSecond);
            split.SplitterDistance = Math.Clamp(preferred, Math.Min(minimumFirst, maximum), maximum);
        }
        finally
        {
            configuringSplit = false;
        }
    }

    private void UpdateStateAndPreview()
    {
        state.PassbandRippleDb = (double)rp.Value;
        state.StopbandAttenuationDb = (double)attenuation.Value;
        state.PassbandGain = (double)gain.Value;
        state.PassbandEdgeHz = fp.ValueBaseUnit;
        state.StopbandEdgeHz = fs.ValueBaseUnit;
        state.StopbandLowerHz = fs1.ValueBaseUnit;
        state.PassbandLowerHz = fp1.ValueBaseUnit;
        state.PassbandUpperHz = fp2.ValueBaseUnit;
        state.StopbandUpperHz = fs2.ValueBaseUnit;
        plot.Model = FilterSpecPlotBuilder.Build(state);
        Changed();
    }

    private static EngineeringValueInput Frequency(double valueHz) =>
        new(valueHz, EngineeringUnits.Frequency, 0.001, 1e12);
}
