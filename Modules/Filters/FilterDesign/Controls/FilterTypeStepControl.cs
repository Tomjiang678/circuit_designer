using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterTypeStepControl : FilterStepBase
{
    public FilterTypeStepControl(FilterDesignState state) : base(state)
    {
        var layout = FormLayout();
        var mode = Combo(Enum.GetValues<FilterMode>(), value => value == FilterMode.Active ? "Active Filter" : "Passive Filter");
        var response = Combo(Enum.GetValues<FilterResponseType>(), DisplayName);
        mode.SelectedIndex = (int)state.Mode; response.SelectedIndex = (int)state.ResponseType;
        mode.SelectedIndexChanged += (_, _) =>
        {
            state.Mode = ((EnumItem<FilterMode>)mode.SelectedItem!).Value;
            state.SelectResponseType(state.ResponseType);
            Changed();
        };
        response.SelectedIndexChanged += (_, _) =>
        {
            state.SelectResponseType(((EnumItem<FilterResponseType>)response.SelectedItem!).Value);
            Changed();
        };
        AddRow(layout, "Filter Mode:", mode); AddRow(layout, "Response Type:", response);
        Controls.Add(CreateSection("Filter Type", layout));
    }

}
