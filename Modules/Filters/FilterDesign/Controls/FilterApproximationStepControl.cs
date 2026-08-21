using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterApproximationStepControl : FilterStepBase
{
    private static readonly IReadOnlyDictionary<FilterApproximation, (string Description, string Status)> Information =
        new Dictionary<FilterApproximation, (string, string)>
        {
            [FilterApproximation.Butterworth] = ("Maximally flat passband magnitude with monotonic roll-off.", "Active Sallen-Key/MFB LP/HP/BP/BS; passive RC first-order LP/HP; passive LC LP/HP/BP/BS."),
            [FilterApproximation.ChebyshevTypeI] = ("Equiripple passband with a steeper transition than Butterworth for the same order.", "Active Sallen-Key/MFB LP/HP/BP/BS; passive RC first-order LP/HP; passive LC LP/HP/BP/BS. Rp is required."),
            [FilterApproximation.Bessel] = ("Maximally flat group delay and good waveform preservation.", "Active Sallen-Key/MFB LP/HP/BP/BS and physically valid first-order passive RC LP/HP."),
            [FilterApproximation.GaussianTo6Db] = ("Transitional Gaussian response with the transition breakpoint at 6 dB.", "ADI/FilterPro tabulated poles (orders 3-10); active Sallen-Key/MFB LP/HP/BP/BS."),
            [FilterApproximation.GaussianTo12Db] = ("Transitional Gaussian response emphasizing transient fidelity through 12 dB.", "ADI/FilterPro tabulated poles (orders 3-10); active Sallen-Key/MFB LP/HP/BP/BS."),
            [FilterApproximation.LinearPhase005Degree] = ("Equiripple linear-phase approximation with 0.05° phase error.", "ADI tabulated poles (orders 2-10) are implemented for supported active topologies."),
            [FilterApproximation.LinearPhase05Degree] = ("Equiripple linear-phase approximation with 0.5° phase error.", "ADI tabulated poles (orders 2-10) are implemented for supported active topologies.")
        };

    public FilterApproximationStepControl(FilterDesignState state) : base(state)
    {
        var layout = FormLayout();
        var approximation = Combo(Enum.GetValues<FilterApproximation>(), DisplayName);
        approximation.SelectedIndex = (int)state.Approximation;
        var description = new Label { AutoSize = true, MaximumSize = new Size(650, 0) };
        var status = new Label { AutoSize = true, MaximumSize = new Size(650, 0) };

        void RefreshInformation()
        {
            var information = Information[state.Approximation];
            description.Text = information.Description;
            status.Text = information.Status;
            status.ForeColor = Color.DarkGreen;
        }

        approximation.SelectedIndexChanged += (_, _) =>
        {
            state.Approximation = ((EnumItem<FilterApproximation>)approximation.SelectedItem!).Value;
            RefreshInformation(); Changed();
        };
        AddRow(layout, "Approximation:", approximation);
        AddRow(layout, "Description:", description);
        AddRow(layout, "Design Status:", status);

        var order = Combo(Enum.GetValues<FilterOrderMode>(), DisplayName);
        order.SelectedIndex = (int)state.OrderMode;
        var orderNumber = Number(state.SpecifiedOrder, 1, 30, 0);
        order.SelectedIndexChanged += (_, _) =>
        {
            state.OrderMode = ((EnumItem<FilterOrderMode>)order.SelectedItem!).Value;
            orderNumber.Visible = state.OrderMode == FilterOrderMode.SpecifyOrder;
            Changed();
        };
        orderNumber.Visible = state.OrderMode == FilterOrderMode.SpecifyOrder;
        orderNumber.ValueChanged += (_, _) => { state.SpecifiedOrder = (int)orderNumber.Value; Changed(); };
        AddRow(layout, "Order Mode:", order);
        AddRow(layout, "Specified Order:", orderNumber);
        RefreshInformation();
        Controls.Add(CreateSection("Approximation / Frequency Response", layout));
    }
}
