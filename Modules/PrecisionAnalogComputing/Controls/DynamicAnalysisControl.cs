using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Analysis;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;

public sealed class DynamicAnalysisControl:UserControl
{
    private readonly AnalogBodePlotControl magnitude=new(){Dock=DockStyle.Fill,PlotMode=AnalogBodePlotMode.Magnitude};
    private readonly AnalogBodePlotControl phase=new(){Dock=DockStyle.Fill,PlotMode=AnalogBodePlotMode.Phase};
    private readonly DynamicWaveformPlotControl waveform=new(){Dock=DockStyle.Fill};
    private readonly TrackBar frequencySlider=new()
    {
        Dock=DockStyle.Top,Minimum=0,Maximum=1000,TickStyle=TickStyle.None,
        SmallChange=2,LargeChange=50,Height=32,AccessibleName="Time-domain frequency"
    };
    private readonly Label frequencyReadout=new()
    {
        Dock=DockStyle.Bottom,Height=22,TextAlign=ContentAlignment.MiddleCenter
    };
    private readonly DynamicTimeDomainAnalysisService timeDomain=new();
    private AnalogComputeDesignResult? currentResult;
    private double frequencyStartHz;
    private double frequencyStopHz;
    private bool updatingSlider;

    public DynamicAnalysisControl()
    {
        Dock=DockStyle.Fill;DoubleBuffered=true;
        var tabs=new TabControl{Dock=DockStyle.Fill};
        var magnitudePage=new TabPage("Magnitude Response"){BackColor=SystemColors.Window};magnitudePage.Controls.Add(magnitude);
        var phasePage=new TabPage("Phase Response"){BackColor=SystemColors.Window};phasePage.Controls.Add(phase);
        var timePage=new TabPage("Time Domain"){BackColor=SystemColors.Window};
        var frequencyPanel=new Panel{Dock=DockStyle.Bottom,Height=54};
        frequencyPanel.Controls.Add(frequencySlider);frequencyPanel.Controls.Add(frequencyReadout);
        timePage.Controls.Add(waveform);timePage.Controls.Add(frequencyPanel);
        frequencySlider.ValueChanged+=(_,_)=>UpdateWaveformFrequency();
        tabs.TabPages.Add(magnitudePage);tabs.TabPages.Add(phasePage);tabs.TabPages.Add(timePage);Controls.Add(tabs);
    }

    public void UpdateResult(AnalogComputeDesignResult result)
    {
        var parameters=DynamicOperationParameters.Resolve(result.Request);
        IReadOnlyList<AnalysisFrequencyMarker> markers=result.Request.Operation==AnalogComputeOperationType.PracticalIntegrator
            ? [new(parameters.FirstCornerHz,"Leak fL")]
            : [new(parameters.UsefulUpperFrequencyHz,"Useful fMax"),new(parameters.FirstCornerHz,"Pole f1"),new(parameters.SecondCornerHz,"Pole f2")];
        magnitude.Markers=markers;phase.Markers=markers;magnitude.Analysis=result.Analysis;phase.Analysis=result.Analysis;
        currentResult=result;
        frequencyStartHz=parameters.SweepStartFrequencyHz;
        frequencyStopHz=parameters.SweepStopFrequencyHz;
        updatingSlider=true;
        frequencySlider.Value=SliderValue(result.Analysis?.TimeDomain?.DemoFrequencyHz??frequencyStartHz);
        updatingSlider=false;
        UpdateWaveformFrequency();
    }

    private int SliderValue(double frequencyHz)
    {
        var span=Math.Log10(frequencyStopHz/frequencyStartHz);
        if(!(span>0)||!double.IsFinite(span))return frequencySlider.Minimum;
        var normalized=Math.Log10(Math.Clamp(frequencyHz,frequencyStartHz,frequencyStopHz)/frequencyStartHz)/span;
        return (int)Math.Round(frequencySlider.Minimum+normalized*(frequencySlider.Maximum-frequencySlider.Minimum));
    }

    private double SliderFrequency()
    {
        var normalized=(frequencySlider.Value-frequencySlider.Minimum)/(double)(frequencySlider.Maximum-frequencySlider.Minimum);
        return frequencyStartHz*Math.Pow(frequencyStopHz/frequencyStartHz,normalized);
    }

    private void UpdateWaveformFrequency()
    {
        if(updatingSlider||currentResult is null||!(frequencyStartHz>0)||!(frequencyStopHz>frequencyStartHz))return;
        var frequency=SliderFrequency();
        frequencyReadout.Text=$"f = {frequency:G6} Hz";
        waveform.Analysis=timeDomain.AnalyzeAtFrequency(currentResult.Request,currentResult.Components,frequency);
    }
}
