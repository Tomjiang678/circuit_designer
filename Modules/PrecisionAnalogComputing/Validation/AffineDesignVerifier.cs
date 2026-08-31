using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

internal sealed class AffineDesignVerifier
{
    private readonly LinearCircuitDcVerifier dc = new();

    public (IReadOnlyList<VerificationMetric> Metrics, AnalogAnalysisResult Analysis, IReadOnlyList<string> Warnings) Verify(
        AnalogComputeDesignRequest request,
        CircuitGraph circuit)
    {
        var fixedValues = request.Inputs.ToDictionary(input => input.Name, _ => 0d, StringComparer.OrdinalIgnoreCase);
        fixedValues["VCC"] = request.SupplyVccVoltage;
        fixedValues["VEE"] = request.SupplyVeeVoltage;
        var baseline = dc.Solve(circuit, fixedValues);
        var metrics = new List<VerificationMetric>();
        foreach (var input in request.Inputs)
        {
            fixedValues[input.Name] = 1;
            var actual = dc.Solve(circuit, fixedValues) - baseline;
            fixedValues[input.Name] = 0;
            var tolerance = Math.Max(1e-6, Math.Abs(input.Coefficient) * request.MaximumFunctionErrorPercent / 100d);
            var error = actual - input.Coefficient;
            metrics.Add(new($"Coefficient {input.Name}", input.Coefficient, actual, error, tolerance, Math.Abs(error) <= tolerance));
        }
        var offsetTolerance = Math.Max(1e-5, Math.Max(1, Math.Abs(request.OutputOffsetVoltage)) * request.MaximumFunctionErrorPercent / 100d);
        metrics.Add(new("Output offset", request.OutputOffsetVoltage, baseline, baseline - request.OutputOffsetVoltage, offsetTolerance, Math.Abs(baseline - request.OutputOffsetVoltage) <= offsetTolerance, "V"));

        var swingPass = true;
        if (request.UseInputRangesForFeasibility)
        {
            var expectedMin = request.OutputOffsetVoltage;
            var expectedMax = request.OutputOffsetVoltage;
            foreach (var input in request.Inputs)
            {
                var first = input.Coefficient * input.MinimumVoltage;
                var second = input.Coefficient * input.MaximumVoltage;
                expectedMin += Math.Min(first, second);
                expectedMax += Math.Max(first, second);
            }
            var lowLimit = request.SupplyVeeVoltage + request.OpAmp.OutputSwingHeadroomVoltage;
            var highLimit = request.SupplyVccVoltage - request.OpAmp.OutputSwingHeadroomVoltage;
            swingPass = expectedMin >= lowLimit && expectedMax <= highLimit;
            metrics.Add(new("Output minimum swing", expectedMin, Math.Max(expectedMin, lowLimit), Math.Max(0, lowLimit - expectedMin), 0, expectedMin >= lowLimit, "V"));
            metrics.Add(new("Output maximum swing", expectedMax, Math.Min(expectedMax, highLimit), Math.Min(0, highLimit - expectedMax), 0, expectedMax <= highLimit, "V"));
        }

        var selected = request.Inputs.FirstOrDefault(input => input.Coefficient != 0) ?? request.Inputs[0];
        var points = new List<AnalogAnalysisPoint>();
        var nominal = request.Inputs.ToDictionary(input => input.Name, input => input.NominalVoltage, StringComparer.OrdinalIgnoreCase);
        nominal["VCC"] = request.SupplyVccVoltage; nominal["VEE"] = request.SupplyVeeVoltage;
        for (var index = 0; index <= 50; index++)
        {
            var x = selected.MinimumVoltage + (selected.MaximumVoltage - selected.MinimumVoltage) * index / 50d;
            nominal[selected.Name] = x;
            var target = request.OutputOffsetVoltage + request.Inputs.Sum(input => input.Coefficient * nominal[input.Name]);
            points.Add(new(x, target, dc.Solve(circuit, nominal)));
        }
        var warnings = new List<string>();
        if (!swingPass) warnings.Add("The requested input range drives the calculated output beyond the configured VCC/VEE output range.");
        return (metrics, new("Linear DC sensitivity and selected-input sweep", points, metrics), warnings);
    }
}
