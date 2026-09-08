using System.Numerics;
using CircuitModuleDesigner.Core.Analysis;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

internal sealed class DynamicOperationVerifier
{
    public (IReadOnlyList<VerificationMetric> Metrics, AnalogAnalysisResult Analysis, IReadOnlyList<string> Warnings) Verify(
        AnalogComputeDesignRequest request, CircuitGraph graph, IReadOnlyList<SelectedAnalogComponent> components)
    {
        if (request.Operation is not (AnalogComputeOperationType.PracticalIntegrator or AnalogComputeOperationType.PracticalDifferentiator))
            throw new ArgumentException("The AC verifier only accepts Integrator or Differentiator.", nameof(request));
        if (graph.Components.Any(component => component.Type == ComponentType.Diode))
            throw new InvalidOperationException("Linear dynamic AC verification cannot contain diode elements.");
        var start = Math.Max(.001, request.LowerCornerHz / 10);
        var stop = Math.Max(start * 10, request.UpperCornerHz * 10);
        var ac = new CircuitAcAnalysisService().Analyze(graph,
            new AcAnalysisOptions(start, stop, 40, 1e9,
                [request.LowerCornerHz, request.UpperCornerHz]));
        var values = components.ToDictionary(component => component.Reference, component => component.SelectedValue);
        var theory = components.ToDictionary(component => component.Reference, component => component.TheoreticalValue);
        var metrics = new List<VerificationMetric>();
        var points = new List<AnalogAnalysisPoint>(ac.Points.Count);
        double targetConstant;
        double actualConstant;
        double targetFirst;
        double actualFirst;
        double targetSecond;
        double actualSecond;
        Func<double, Complex> target;
        Func<double, Complex> continuous;
        if (request.Operation == AnalogComputeOperationType.PracticalIntegrator)
        {
            targetConstant = request.IntegrationConstantPerSecond;
            actualConstant = 1 / (values["R1"] * values["C1"]);
            targetFirst = request.LowerCornerHz;
            actualFirst = 1 / (2 * Math.PI * values["R2"] * values["C1"]);
            targetSecond = actualSecond = double.NaN;
            target = frequency => -request.IntegrationConstantPerSecond /
                new Complex(2 * Math.PI * request.LowerCornerHz, 2 * Math.PI * frequency);
            continuous = frequency => -theory["R2"] / theory["R1"] /
                (Complex.One + new Complex(0, 2 * Math.PI * frequency * theory["R2"] * theory["C1"]));
            AddMetric(metrics, "Integration constant Ki", targetConstant, actualConstant, request.MaximumFunctionErrorPercent, "1/s");
            AddMetric(metrics, "Leak corner", targetFirst, actualFirst, request.MaximumFunctionErrorPercent, "Hz");
        }
        else
        {
            targetConstant = request.DifferentiationConstantSecond;
            actualConstant = values["R2"] * values["C1"];
            targetFirst = request.LowerCornerHz;
            actualFirst = 1 / (2 * Math.PI * values["R1"] * values["C1"]);
            targetSecond = request.UpperCornerHz;
            actualSecond = 1 / (2 * Math.PI * values["R2"] * values["C2"]);
            target = frequency => TransferDifferentiator(frequency, request.DifferentiationConstantSecond,
                request.LowerCornerHz, request.UpperCornerHz);
            continuous = frequency => TransferDifferentiator(frequency, theory["R2"] * theory["C1"],
                1 / (2*Math.PI*theory["R1"]*theory["C1"]), 1 / (2*Math.PI*theory["R2"]*theory["C2"]));
            AddMetric(metrics, "Differentiation constant Kd", targetConstant, actualConstant, request.MaximumFunctionErrorPercent, "s");
            AddMetric(metrics, "Lower limiting corner", targetFirst, actualFirst, request.MaximumFunctionErrorPercent, "Hz");
            AddMetric(metrics, "Upper limiting corner", targetSecond, actualSecond, request.MaximumFunctionErrorPercent, "Hz");
        }
        var maxMagnitudeError = 0d;
        var maxPhaseError = 0d;
        foreach (var point in ac.Points)
        {
            var desired = target(point.FrequencyHz);
            var continuousValue = continuous(point.FrequencyHz);
            var desiredMagnitudeDb = 20 * Math.Log10(Math.Max(desired.Magnitude, 1e-15));
            var theoreticalMagnitudeDb = 20 * Math.Log10(Math.Max(continuousValue.Magnitude, 1e-15));
            maxMagnitudeError = Math.Max(maxMagnitudeError, Math.Abs(point.MagnitudeDb - desiredMagnitudeDb));
            var desiredPhase = Phase(desired);
            maxPhaseError = Math.Max(maxPhaseError, PhaseDifference(point.PhaseDegrees, desiredPhase));
            points.Add(new(point.FrequencyHz, desiredMagnitudeDb, point.MagnitudeDb, false,
                theoreticalMagnitudeDb, desiredPhase, point.PhaseDegrees));
        }
        var allowedDb = 20 * Math.Log10(1 + request.MaximumFunctionErrorPercent / 100);
        metrics.Add(new("Maximum AC magnitude error", 0, maxMagnitudeError, maxMagnitudeError,
            allowedDb + .015, maxMagnitudeError <= allowedDb + .015, "dB"));
        metrics.Add(new("Maximum AC phase error", 0, maxPhaseError, maxPhaseError,
            Math.Max(.5, request.MaximumFunctionErrorPercent * .75), maxPhaseError <= Math.Max(.5, request.MaximumFunctionErrorPercent * .75), "deg"));

        var maximumInput = Math.Max(Math.Abs(request.MinimumInputVoltage), Math.Abs(request.MaximumInputVoltage));
        var peakGain = ac.Points.Max(point => point.MagnitudeLinear);
        var peakOutput = maximumInput * peakGain;
        var swing = Math.Min(request.SupplyVccVoltage - request.OpAmp.OutputSwingHeadroomVoltage,
            -request.SupplyVeeVoltage - request.OpAmp.OutputSwingHeadroomVoltage);
        metrics.Add(new("Output swing peak", peakOutput, Math.Min(peakOutput, swing), Math.Max(0, peakOutput-swing),
            0, peakOutput <= swing, "V"));
        var warnings = new List<string>();
        if (!request.OpAmp.UseIdealOpAmp)
        {
            var noiseGain = request.Operation == AnalogComputeOperationType.PracticalDifferentiator
                ? 1 + values["R2"] / values["R1"] : 1 + values["R2"] / values["R1"];
            var requiredGbw = request.UpperCornerHz * noiseGain * 10;
            metrics.Add(new("Required GBW", requiredGbw, request.OpAmp.GainBandwidthHz,
                request.OpAmp.GainBandwidthHz-requiredGbw, 0, request.OpAmp.GainBandwidthHz >= requiredGbw, "Hz"));
            var requiredSlew = 2 * Math.PI * request.UpperCornerHz * Math.Min(peakOutput, swing);
            metrics.Add(new("Required slew rate", requiredSlew, request.OpAmp.SlewRateVoltPerSecond,
                request.OpAmp.SlewRateVoltPerSecond-requiredSlew, 0, request.OpAmp.SlewRateVoltPerSecond >= requiredSlew, "V/s"));
        }
        else warnings.Add("Ideal op-amp AC verification; enter GBW and slew rate to enable practical dynamic checks.");
        return (metrics, new("Bode magnitude and phase: Target / Theoretical / Selected", points, metrics), warnings);
    }

    private static Complex TransferDifferentiator(double frequency, double kd, double firstCorner, double secondCorner)
    {
        var s = new Complex(0, 2 * Math.PI * frequency);
        return -kd * s / ((Complex.One + s / (2*Math.PI*firstCorner)) *
                          (Complex.One + s / (2*Math.PI*secondCorner)));
    }

    private static void AddMetric(List<VerificationMetric> metrics, string name, double target, double actual,
        double percent, string unit)
    {
        var tolerance = Math.Max(Math.Abs(target) * percent / 100, 1e-15);
        metrics.Add(new(name, target, actual, actual-target, tolerance, Math.Abs(actual-target) <= tolerance, unit));
    }

    private static double Phase(Complex value) => Math.Atan2(value.Imaginary, value.Real) * 180 / Math.PI;
    private static double PhaseDifference(double first, double second)
    {
        var difference = Math.Abs(first-second) % 360;
        return difference > 180 ? 360-difference : difference;
    }
}
