using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterApproximationStepControl : FilterStepBase
{
    private static readonly IReadOnlyDictionary<FilterApproximation, (string Description, string Status)> Information =
        new Dictionary<FilterApproximation, (string, string)>
        {
            [FilterApproximation.Butterworth] = ("Maximally flat passband magnitude with monotonic roll-off.", "Active Sallen-Key LP/HP and MFB LP/HP/BP; passive RC first-order LP/HP; passive LC LP/HP/BP/BS. Active Band-Stop is unavailable."),
            [FilterApproximation.ChebyshevTypeI] = ("Equiripple passband with a steeper transition than Butterworth for the same order.", "Active Sallen-Key LP/HP and MFB LP/HP/BP; passive RC first-order LP/HP; passive LC LP/HP/BP/BS. Rp is required; Active Band-Stop is unavailable."),
            [FilterApproximation.Bessel] = ("Reverse-Bessel polynomial with maximally flat group delay; exact -3 dB normalization.", "Active Sallen-Key LP/HP and MFB LP/HP/BP, orders 1-10. Passive LC is unavailable without verified ladder g-values."),
            [FilterApproximation.GaussianTo6Db] = ("AN-649 Gaussian-to-6 dB transitional all-pole response.", "Published orders 3-10; Active Sallen-Key LP/HP and MFB LP/HP/BP."),
            [FilterApproximation.GaussianTo12Db] = ("AN-649 Gaussian-to-12 dB transitional all-pole response.", "Published orders 3-10; Active Sallen-Key LP/HP and MFB LP/HP/BP."),
            [FilterApproximation.LinearPhase005Degree] = ("AN-649 equiripple linear-phase approximation with 0.05 degree phase error.", "Published orders 2-10; Active Sallen-Key LP/HP and MFB LP/HP/BP."),
            [FilterApproximation.LinearPhase05Degree] = ("AN-649 equiripple linear-phase approximation with 0.5 degree phase error.", "Published orders 2-10; Active Sallen-Key LP/HP and MFB LP/HP/BP.")
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
