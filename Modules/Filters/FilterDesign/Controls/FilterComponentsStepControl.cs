using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterComponentsStepControl : FilterStepBase
{
    public FilterComponentsStepControl(FilterDesignState state) : base(state)
    {
        var layout = FormLayout(); var rs = Combo(Enum.GetValues<StandardComponentSeries>(), DisplayName); var cs = Combo(Enum.GetValues<StandardComponentSeries>(), DisplayName); var ls = Combo(Enum.GetValues<StandardComponentSeries>(), DisplayName); rs.SelectedIndex = (int)state.ResistorSeries; cs.SelectedIndex = (int)state.CapacitorSeries; ls.SelectedIndex=(int)state.InductorSeries; var rt = Number(state.ResistorTolerancePercent, 0, 100); var ct = Number(state.CapacitorTolerancePercent, 0, 100); var lt=Number(state.InductorTolerancePercent,0,100); var optimize = new CheckBox { Text = "Optimize using standard component values", AutoSize = true, Checked = state.OptimizeStandardValues }; var target = Combo(Enum.GetValues<FilterOptimizationTarget>(), value => value == FilterOptimizationTarget.MinimizeFrequencyError ? "Minimize frequency error" : "Minimize Q error"); target.Width = 360; target.DropDownWidth = 400; target.SelectedIndex = (int)state.OptimizationTarget;
        AddRow(layout, "Resistor Series:", rs); AddRow(layout, "Capacitor Series:", cs); AddRow(layout, "Resistor Tolerance, %:", rt); AddRow(layout, "Capacitor Tolerance, %:", ct); AddRow(layout, "Standard Value Optimization:", optimize); AddRow(layout, "Optimization Target:", target);
        NumericUpDown? lmin=null,lmax=null,cmin=null,cmax=null;
        if(state.Mode==FilterMode.Passive&&state.Implementation==FilterImplementationType.Lc)
        {
            AddRow(layout,"Inductor Series:",ls);AddRow(layout,"Inductor Tolerance, %:",lt);
            lmin=Number(state.InductorMinimumH*1e6,.001,1e12);lmax=Number(state.InductorMaximumH*1e6,.001,1e12);cmin=Number(state.PassiveLcCapacitorMinimumF*1e12,.001,1e15);cmax=Number(state.PassiveLcCapacitorMaximumF*1e12,.001,1e15);
            AddRow(layout,"Inductor Minimum, µH:",lmin);AddRow(layout,"Inductor Maximum, µH:",lmax);AddRow(layout,"LC Capacitor Minimum, pF:",cmin);AddRow(layout,"LC Capacitor Maximum, pF:",cmax);
        }
        rs.SelectedIndexChanged += (_, _) => { state.ResistorSeries = ((EnumItem<StandardComponentSeries>)rs.SelectedItem!).Value; Changed(); }; cs.SelectedIndexChanged += (_, _) => { state.CapacitorSeries = ((EnumItem<StandardComponentSeries>)cs.SelectedItem!).Value; Changed(); }; rt.ValueChanged += (_, _) => { state.ResistorTolerancePercent = (double)rt.Value; Changed(); }; ct.ValueChanged += (_, _) => { state.CapacitorTolerancePercent = (double)ct.Value; Changed(); }; optimize.CheckedChanged += (_, _) => { state.OptimizeStandardValues = optimize.Checked; Changed(); }; target.SelectedIndexChanged += (_, _) => { state.OptimizationTarget = ((EnumItem<FilterOptimizationTarget>)target.SelectedItem!).Value; Changed(); };
        ls.SelectedIndexChanged+=(_,_)=>{state.InductorSeries=((EnumItem<StandardComponentSeries>)ls.SelectedItem!).Value;Changed();};lt.ValueChanged+=(_,_)=>{state.InductorTolerancePercent=(double)lt.Value;Changed();};
        if(lmin!=null){lmin.ValueChanged+=(_,_)=>{state.InductorMinimumH=(double)lmin.Value*1e-6;Changed();};lmax!.ValueChanged+=(_,_)=>{state.InductorMaximumH=(double)lmax.Value*1e-6;Changed();};cmin!.ValueChanged+=(_,_)=>{state.PassiveLcCapacitorMinimumF=(double)cmin.Value*1e-12;Changed();};cmax!.ValueChanged+=(_,_)=>{state.PassiveLcCapacitorMaximumF=(double)cmax.Value*1e-12;Changed();};}
        Controls.Add(CreateSection("Component Constraints", layout));
    }
}
