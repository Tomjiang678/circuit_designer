using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Analysis;

public sealed class AffineSweepAnalysisService
{
    private readonly LinearCircuitDcVerifier verifier = new();

    public AnalogAnalysisResult Analyze(AnalogComputeDesignResult result, string sweepInput, IReadOnlyDictionary<string, double> fixedInputs, double start, double stop, int samples = 101)
    {
        if (!result.Success || result.CircuitGraph is null) throw new InvalidOperationException("A successful CircuitGraph design is required.");
        if (!result.Request.Inputs.Any(input => input.Name.Equals(sweepInput, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("The selected sweep input does not exist.", nameof(sweepInput));
        if (!double.IsFinite(start) || !double.IsFinite(stop) || stop <= start) throw new ArgumentException("Sweep Stop must be greater than Sweep Start.");
        samples = Math.Clamp(samples, 11, 1001);
        var values = result.Request.Inputs.ToDictionary(input => input.Name, input => fixedInputs.GetValueOrDefault(input.Name), StringComparer.OrdinalIgnoreCase);
        values["VCC"] = result.Request.SupplyVccVoltage; values["VEE"] = result.Request.SupplyVeeVoltage;
        var points = new List<AnalogAnalysisPoint>(samples);
        for (var index = 0; index < samples; index++)
        {
            var inputValue = start + (stop - start) * index / (samples - 1d);
            values[sweepInput] = inputValue;
            var target = result.Request.OutputOffsetVoltage + result.Request.Inputs.Sum(input => input.Coefficient * values[input.Name]);
            var operatingPoint = verifier.SolveWithSupplyLimits(
                result.CircuitGraph,
                values,
                result.Request.SupplyVeeVoltage + result.Request.OpAmp.OutputSwingHeadroomVoltage,
                result.Request.SupplyVccVoltage - result.Request.OpAmp.OutputSwingHeadroomVoltage);
            points.Add(new(inputValue, target, operatingPoint.OutputVoltage, operatingPoint.IsSupplyLimited));
        }
        var maximumError = points.Max(point => Math.Abs(point.Actual - point.Target));
        return new("Rail-limited CircuitGraph DC sweep", points,
            [
                new VerificationMetric("Sweep maximum absolute error", 0, maximumError, maximumError, double.PositiveInfinity, true, "V"),
                new VerificationMetric("Supply-limited sample count", 0, points.Count(point => point.IsSupplyLimited), points.Count(point => point.IsSupplyLimited), 0, points.All(point => !point.IsSupplyLimited))
            ]);
    }
}
