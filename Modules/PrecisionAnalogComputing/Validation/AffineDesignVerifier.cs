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
        var actualCoefficients = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in request.Inputs)
        {
            fixedValues[input.Name] = 1;
            var actual = dc.Solve(circuit, fixedValues) - baseline;
            fixedValues[input.Name] = 0;
            var tolerance = Math.Max(1e-6, Math.Abs(input.Coefficient) * request.MaximumFunctionErrorPercent / 100d);
            var error = actual - input.Coefficient;
            actualCoefficients[input.Name] = actual;
            metrics.Add(new($"Coefficient {input.Name}", input.Coefficient, actual, error, tolerance, Math.Abs(error) <= tolerance));
        }
        var offsetTolerance = Math.Max(1e-5, Math.Max(1, Math.Abs(request.OutputOffsetVoltage)) * request.MaximumFunctionErrorPercent / 100d);
        metrics.Add(new("Output offset", request.OutputOffsetVoltage, baseline, baseline - request.OutputOffsetVoltage, offsetTolerance, Math.Abs(baseline - request.OutputOffsetVoltage) <= offsetTolerance, "V"));

        var targetCornerOutputs = new List<double>();
        var selectedCornerOutputs = new List<double>();
        var selectedCircuitSaturated = false;
        var maximumStagePeak = 0d;
        {
            var cornerCount = 1 << request.Inputs.Count;
            for (var corner = 0; corner < cornerCount; corner++)
            {
                var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VCC"] = request.SupplyVccVoltage,
                    ["VEE"] = request.SupplyVeeVoltage
                };
                var target = request.OutputOffsetVoltage;
                for (var inputIndex = 0; inputIndex < request.Inputs.Count; inputIndex++)
                {
                    var input = request.Inputs[inputIndex];
                    var value = (corner & (1 << inputIndex)) == 0 ? input.MinimumVoltage : input.MaximumVoltage;
                    values[input.Name] = value;
                    target += input.Coefficient * value;
                }
                targetCornerOutputs.Add(target);
                selectedCornerOutputs.Add(baseline + request.Inputs.Sum(input => actualCoefficients[input.Name] * values[input.Name]));
                var nodes = dc.SolveNodes(circuit, values);
                foreach (var opAmp in circuit.Components.Where(component => component.Type == ComponentType.OpAmp))
                {
                    var outputNet = circuit.GetNetForPin(opAmp.GetPin("OUT"))?.Name ??
                                    throw new InvalidOperationException($"{opAmp.Reference}.OUT is not connected.");
                    maximumStagePeak = Math.Max(maximumStagePeak, Math.Abs(nodes[outputNet]));
                }
                var limits = OperatingEnvelopeEvaluator.OutputLimits(request);
                selectedCircuitSaturated |= dc.SolveWithSupplyLimits(circuit, values, limits.Low, limits.High).IsSupplyLimited;
            }
        }
        var outputLimits = OperatingEnvelopeEvaluator.OutputLimits(request);
        var envelope = OperatingEnvelopeEvaluator.Metric(targetCornerOutputs, selectedCornerOutputs,
            outputLimits.Low, outputLimits.High, selectedCircuitSaturated);
        metrics.Add(envelope);
        if (!request.OpAmp.UseIdealOpAmp)
        {
            var requiredGbw = OpAmpFeasibilityEvaluator.RequiredGbwForResistiveStages(circuit, request.MaximumExpectedFrequencyHz);
            var requiredSlew = OpAmpFeasibilityEvaluator.RequiredSlew(request.MaximumExpectedFrequencyHz, maximumStagePeak);
            OpAmpFeasibilityEvaluator.AddMetrics(metrics, request, requiredGbw, requiredSlew);
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
            var raw = dc.Solve(circuit, nominal);
            var limited = dc.SolveWithSupplyLimits(circuit, nominal, outputLimits.Low, outputLimits.High);
            points.Add(new(x, target, limited.OutputVoltage, limited.IsSupplyLimited, raw));
        }
        var warnings = new List<string>();
        if (!envelope.Pass) warnings.Add("The complete declared input range exceeds the configured output swing; clipped analysis remains available, but the operating envelope is unsupported.");
        if (request.OpAmp.UseIdealOpAmp) warnings.Add("Ideal op-amp verification; enter GBW and slew rate to enable practical affine-stage checks.");
        return (metrics, new("Linear DC sensitivity and selected-input sweep", points, metrics), warnings);
    }
}
