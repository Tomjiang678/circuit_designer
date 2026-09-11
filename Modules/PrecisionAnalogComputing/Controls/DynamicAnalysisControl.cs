using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class DynamicAnalysisControl:UserControl
{
    private readonly AnalogBodePlotControl magnitude=new(){Dock=DockStyle.Fill,PlotMode=AnalogBodePlotMode.Magnitude};
    private readonly AnalogBodePlotControl phase=new(){Dock=DockStyle.Fill,PlotMode=AnalogBodePlotMode.Phase};
    private readonly DynamicWaveformPlotControl waveform=new(){Dock=DockStyle.Fill};

    public DynamicAnalysisControl()
    {
        Dock=DockStyle.Fill;DoubleBuffered=true;
        var tabs=new TabControl{Dock=DockStyle.Fill};
        var magnitudePage=new TabPage("Magnitude Response"){BackColor=SystemColors.Window};magnitudePage.Controls.Add(magnitude);
        var phasePage=new TabPage("Phase Response"){BackColor=SystemColors.Window};phasePage.Controls.Add(phase);
        var timePage=new TabPage("Time Domain"){BackColor=SystemColors.Window};timePage.Controls.Add(waveform);
        tabs.TabPages.Add(magnitudePage);tabs.TabPages.Add(phasePage);tabs.TabPages.Add(timePage);Controls.Add(tabs);
    }

    public void UpdateResult(AnalogComputeDesignResult result)
    {
        var parameters=DynamicOperationParameters.Resolve(result.Request);
        IReadOnlyList<AnalysisFrequencyMarker> markers=result.Request.Operation==AnalogComputeOperationType.PracticalIntegrator
            ? [new(parameters.FirstCornerHz,"Leak fL")]
            : [new(parameters.UsefulUpperFrequencyHz,"Useful fMax"),new(parameters.FirstCornerHz,"Pole f1"),new(parameters.SecondCornerHz,"Pole f2")];
        magnitude.Markers=markers;phase.Markers=markers;magnitude.Analysis=result.Analysis;phase.Analysis=result.Analysis;
        waveform.Analysis=result.Analysis?.TimeDomain;
    }
}
