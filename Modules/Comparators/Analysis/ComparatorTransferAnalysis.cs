using CircuitModuleDesigner.Modules.Comparators.Models;

namespace CircuitModuleDesigner.Modules.Comparators.Analysis;

public sealed record ComparatorTransferPoint(double InputVoltage, double RisingOutputVoltage, double FallingOutputVoltage);
public sealed record ComparatorTransferAnalysis(IReadOnlyList<ComparatorTransferPoint> Points, IReadOnlyList<double> Markers, IReadOnlyList<(double Start, double End, string Label)> Regions);

public sealed class ComparatorTransferAnalysisService
{
    public ComparatorTransferAnalysis Analyze(ComparatorDesignResult result, int pointCount = 401)
    {
        if (!result.Success || result.ActualThresholds is null) throw new ArgumentException("A successful design is required.", nameof(result));
        var r = result.Request; var padding = Math.Max(.25, (r.UpperThresholdVoltage - r.LowerThresholdVoltage) * .35);
        if(r.Kind==ComparatorDesignerKind.RelaxationOscillator)
            throw new ArgumentException("Use RelaxationOscillatorAnalysisService for oscillator time-domain analysis.",nameof(result));
        var start = Math.Max(r.SupplyLowVoltage, r.LowerThresholdVoltage - padding); var stop = Math.Min(r.SupplyHighVoltage, r.UpperThresholdVoltage + padding);
        var points = new List<ComparatorTransferPoint>(pointCount);
        if (r.Kind == ComparatorDesignerKind.SchmittTrigger)
        {
            var t = result.ActualThresholds;
            for (var i = 0; i < pointCount; i++) { var x = start + (stop - start) * i / (pointCount - 1d); var rising = x < t.Upper ? r.OutputHighVoltage : r.OutputLowVoltage; var falling = x <= t.Lower ? r.OutputHighVoltage : r.OutputLowVoltage; if (r.Topology == SchmittTopology.NonInverting) { rising = x < t.Upper ? r.OutputLowVoltage : r.OutputHighVoltage; falling = x <= t.Lower ? r.OutputLowVoltage : r.OutputHighVoltage; } points.Add(new(x, rising, falling)); }
            return new(points, [t.Lower, t.Upper], [(t.Lower, t.Upper, "Hysteresis")]);
        }
        var w = result.WindowThresholds!;
        for (var i = 0; i < pointCount; i++)
        {
            var x = start + (stop - start) * i / (pointCount - 1d);
            var risingInside = x >= w.LowerRising && x <= w.UpperRising; var fallingInside = x >= w.LowerFalling && x <= w.UpperFalling;
            if (r.OutputMode == WindowOutputMode.OutsideWindowHigh) { risingInside = !risingInside; fallingInside = !fallingInside; }
            points.Add(new(x, risingInside ? r.OutputHighVoltage : r.OutputLowVoltage, fallingInside ? r.OutputHighVoltage : r.OutputLowVoltage));
        }
        return new(points, [w.LowerFalling, w.LowerRising, w.UpperFalling, w.UpperRising],
            [(start, result.ActualThresholds.Lower, "Below window"), (result.ActualThresholds.Lower, result.ActualThresholds.Upper, "Inside window"), (result.ActualThresholds.Upper, stop, "Above window")]);
    }
}
