using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Analysis;

public sealed record RectifierWaveformPoint(
    double Periods,
    double InputVoltage,
    double TargetVoltage,
    double TheoreticalVoltage,
    double SelectedVoltage,
    bool IsSupplyLimited);

/// <summary>Builds a normalized three-period display from the independently verified Vin/Vout transfer.</summary>
public sealed class RectifierWaveformAnalysisService
{
    public const int PeriodCount = 3;

    public IReadOnlyList<RectifierWaveformPoint> Build(AnalogComputeDesignResult result, int samplesPerPeriod = 120)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Analysis is null || result.Analysis.Points.Count < 2)
            throw new InvalidOperationException("A verified rectifier transfer is required.");
        if (result.Request.Operation is not (AnalogComputeOperationType.PrecisionHalfWaveRectifier or AnalogComputeOperationType.PrecisionFullWaveRectifier))
            throw new ArgumentException("Three-period waveform analysis is only available for rectifiers.", nameof(result));
        if (samplesPerPeriod < 16) throw new ArgumentOutOfRangeException(nameof(samplesPerPeriod));

        var transfer=result.Analysis.Points.OrderBy(point=>point.Input).ToArray();
        var minimum=result.Request.MinimumInputVoltage;
        var maximum=result.Request.MaximumInputVoltage;
        var midpoint=(minimum+maximum)/2;
        var amplitude=(maximum-minimum)/2;
        var resultPoints=new List<RectifierWaveformPoint>(PeriodCount*samplesPerPeriod+1);
        for(var index=0;index<=PeriodCount*samplesPerPeriod;index++)
        {
            var periods=(double)index/samplesPerPeriod;
            var input=midpoint+amplitude*Math.Sin(2*Math.PI*periods);
            var transferPoint=Interpolate(transfer,input);
            resultPoints.Add(new(periods,input,transferPoint.Target,transferPoint.Theoretical??transferPoint.Target,
                transferPoint.Actual,transferPoint.IsSupplyLimited));
        }
        return resultPoints;
    }

    private static AnalogAnalysisPoint Interpolate(IReadOnlyList<AnalogAnalysisPoint> points,double input)
    {
        if(input<=points[0].Input)return points[0];
        if(input>=points[^1].Input)return points[^1];
        var low=0;var high=points.Count-1;
        while(high-low>1){var middle=(low+high)/2;if(points[middle].Input<input)low=middle;else high=middle;}
        var first=points[low];var second=points[high];var fraction=(input-first.Input)/(second.Input-first.Input);
        double Lerp(double a,double b)=>a+(b-a)*fraction;
        var theoretical=Lerp(first.Theoretical??first.Target,second.Theoretical??second.Target);
        return new(input,Lerp(first.Target,second.Target),Lerp(first.Actual,second.Actual),
            first.IsSupplyLimited||second.IsSupplyLimited,theoretical);
    }
}
