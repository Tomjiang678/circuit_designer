using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterImplementationStepControl : FilterStepBase
{
    private readonly SplitContainer split;
    private bool configuringSplit;

    public FilterImplementationStepControl(FilterDesignState state) : base(state)
    {
        AutoScroll = false;
        var form = FormLayout();
        var selectedCombination = new Label { AutoSize = true, MaximumSize = new Size(520, 0) };
        var preview = new FilterSchematicControl { Dock = DockStyle.Fill, ShowComponentValues = false };
        var previewGroup = new GroupBox { Dock = DockStyle.Fill, Padding = new Padding(6, 18, 6, 6) };
        previewGroup.Controls.Add(preview);

        void RefreshPreview()
        {
            var capability=FilterSupportMatrix.Evaluate(FilterDesignRequest.FromState(state));
            if(!capability.IsSupported){previewGroup.Text="No Applicable Topology";preview.Document=null;return;}
            previewGroup.Text = $"Selected Topology - {DisplayName(state.Implementation)}";
            preview.Document = new FilterTopologyReferenceBuilder().Build(state.Mode, state.Implementation);
        }

        void RefreshAvailability()
        {
            var current = new FilterDesignEngine().GetAvailability(FilterDesignRequest.FromState(state));
            selectedCombination.Text = current.CanDesign ? "Supported" : current.Message;
            selectedCombination.ForeColor = current.CanDesign ? Color.DarkGreen : Color.Firebrick;
        }

        if (state.Mode == FilterMode.Active)
        {
            state.SignalArchitecture = FilterSignalArchitecture.SingleEnded;
            var supported=FilterSupportMatrix.GetSupportedImplementations(FilterDesignRequest.FromState(state));
            var fallback=state.ResponseType==FilterResponseType.BandStop
                ? new[]{FilterImplementationType.BainterNotch}
                : new[]{FilterImplementationType.SallenKey,FilterImplementationType.MultipleFeedback};
            var availableTopologies=supported.Count>0?supported.ToArray():fallback;
            if (!availableTopologies.Contains(state.Implementation)) state.Implementation = availableTopologies[0];
            var topology = Combo(availableTopologies, DisplayName);
            topology.Enabled = supported.Count>0;
            topology.SelectedIndex = Array.IndexOf(availableTopologies, state.Implementation);
            topology.SelectedIndexChanged += (_, _) =>
            {
                state.Implementation = ((EnumItem<FilterImplementationType>)topology.SelectedItem!).Value;
                RefreshAvailability(); RefreshPreview(); Changed();
            };
            AddRow(form, "Topology:", topology);
            AddRow(form, "Current Support:", new Label
            {
                Text = state.ResponseType switch
                {
                    FilterResponseType.LowPass or FilterResponseType.HighPass => "Low-Pass and High-Pass support both Sallen-Key and verified inverting Multiple Feedback (MFB).",
                    FilterResponseType.BandPass => "Band-Pass supports MFB and the Q-coupled Sallen-Key realization with section amplitude normalization.",
                    _ => "Band-Stop uses a direct Bainter notch biquad. It is not synthesized by duplicating Low-Pass and High-Pass branches."
                },
                AutoSize = true,
                MaximumSize = new Size(520, 0)
            });
        }
        else
        {
            var supported=FilterSupportMatrix.GetSupportedImplementations(FilterDesignRequest.FromState(state));
            var implementations=supported.Count>0?supported.ToArray():new[]{FilterImplementationType.Rc,FilterImplementationType.Lc};
            if (!implementations.Contains(state.Implementation)) state.Implementation=implementations[0];
            var implementation = Combo(implementations, DisplayName);
            implementation.SelectedIndex = Array.IndexOf(implementations,state.Implementation);
            implementation.Enabled=supported.Count>0;
            implementation.SelectedIndexChanged += (_, _) =>
            {
                state.Implementation = ((EnumItem<FilterImplementationType>)implementation.SelectedItem!).Value;
                RefreshAvailability(); RefreshPreview();
                Changed();
            };
            AddRow(form, "Passive Implementation:", implementation);
            AddRow(form, "Current Support:", new Label
            {
                Text = "RC: physically valid first-order LP/HP only. LC: Butterworth/Chebyshev-I LP/HP/BP/BS with normalized termination scaling.",
                AutoSize = true,
                MaximumSize = new Size(520, 0)
            });
        }

        RefreshAvailability();
        RefreshPreview();
        AddRow(form, "Selected Combination:", selectedCombination);
        var inputGroup = CreateSection(state.Mode == FilterMode.Active ? "Active Implementation" : "Passive Implementation", form);

        split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            Panel1MinSize = 0,
            Panel2MinSize = 0
        };
        split.Panel1.AutoScroll = true;
        split.Panel1.Padding = new Padding(0, 0, 4, 0);
        split.Panel1.Controls.Add(inputGroup);
        split.Panel2.Controls.Add(previewGroup);
        split.Resize += (_, _) => ConfigureSplit();
        Controls.Add(split);
        HandleCreated += (_, _) => BeginInvoke(ConfigureSplit);
    }

    private void ConfigureSplit()
    {
        if (configuringSplit || split.ClientSize.Width <= 2 || split.ClientSize.Height <= 2) return;
        configuringSplit = true;
        try
        {
            split.Panel1MinSize = 0; split.Panel2MinSize = 0;
            var sideBySide = split.ClientSize.Width >= 900;
            split.Orientation = sideBySide ? Orientation.Vertical : Orientation.Horizontal;
            var available = (sideBySide ? split.ClientSize.Width : split.ClientSize.Height) - split.SplitterWidth;
            if (available <= 2) return;
            var preferred = sideBySide ? Math.Max(410, (int)(available * .43)) : Math.Max(230, (int)(available * .43));
            var secondMinimum = sideBySide ? 430 : 260;
            split.SplitterDistance = Math.Clamp(preferred, 1, Math.Max(1, available - Math.Min(secondMinimum, available - 1)));
        }
        finally { configuringSplit = false; }
    }
}
