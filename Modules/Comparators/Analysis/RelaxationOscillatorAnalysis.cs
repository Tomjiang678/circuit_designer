using CircuitModuleDesigner.Modules.Comparators.Models;

namespace CircuitModuleDesigner.Modules.Comparators.Analysis;

public sealed record RelaxationOscillatorPoint(double TimeSeconds,double OutputVoltage,double CapacitorVoltage);

public sealed record RelaxationOscillatorAnalysis(
    double FrequencyHz,
    double LowerThresholdVoltage,
    double UpperThresholdVoltage,
    IReadOnlyList<RelaxationOscillatorPoint> Points);

public sealed class RelaxationOscillatorAnalysisService
{
    public RelaxationOscillatorAnalysis Analyze(ComparatorDesignResult result,int visibleCycles=4,int samplesPerCycle=240)
    {
        if(result.Oscillator is null||result.ActualThresholds is null)
            throw new ArgumentException("A successful relaxation-oscillator design is required.",nameof(result));
        var request=result.Request;
        var timingResistance=result.Resistors.Single(item=>item.Reference=="RT").SelectedOhm;
        var timingCapacitance=result.Capacitors.Single(item=>item.Reference=="CT").SelectedFarad;
        var tau=timingResistance*timingCapacitance;
        var thresholds=result.ActualThresholds;
        var highTime=result.Oscillator.HighTimeSeconds;
        var lowTime=result.Oscillator.LowTimeSeconds;
        var period=highTime+lowTime;
        var count=visibleCycles*samplesPerCycle;
        var points=new List<RelaxationOscillatorPoint>(count+1);
        for(var index=0;index<=count;index++)
        {
            var time=index*visibleCycles*period/count;
            var phase=time%period;
            if(phase<highTime)
            {
                var capacitor=request.OutputHighVoltage-(request.OutputHighVoltage-thresholds.Lower)*Math.Exp(-phase/tau);
                points.Add(new(time,request.OutputHighVoltage,capacitor));
            }
            else
            {
                var elapsed=phase-highTime;
                var capacitor=request.OutputLowVoltage+(thresholds.Upper-request.OutputLowVoltage)*Math.Exp(-elapsed/tau);
                points.Add(new(time,request.OutputLowVoltage,capacitor));
            }
        }
        return new(result.Oscillator.ActualFrequencyHz,thresholds.Lower,thresholds.Upper,points);
    }
}
