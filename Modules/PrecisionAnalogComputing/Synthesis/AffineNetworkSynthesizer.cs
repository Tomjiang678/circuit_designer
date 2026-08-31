using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

internal sealed record AffineSynthesisResult(
    CircuitGraph Circuit,
    IReadOnlyList<SelectedAnalogComponent> Components,
    string Topology,
    IReadOnlyList<CalculationRecord> Calculations);

internal sealed class AffineNetworkSynthesizer
{
    private sealed record Ratio(string Key, string Role, double Target);
    private sealed record StageValues(double TheoreticalFeedback, double Feedback, IReadOnlyDictionary<string, double> Inputs, IReadOnlyDictionary<string, double> TheoreticalInputs);

    public AffineSynthesisResult Synthesize(AnalogComputeDesignRequest request)
    {
        Validate(request);
        var positive = request.Inputs.Where(input => input.Coefficient > 0).ToArray();
        var negative = request.Inputs.Where(input => input.Coefficient < 0).ToArray();
        var positiveOffset = Math.Max(0, request.OutputOffsetVoltage);
        var negativeOffset = Math.Max(0, -request.OutputOffsetVoltage);
        var needsFirstStage = positive.Length > 0 || positiveOffset > 0;
        var components = new List<SelectedAnalogComponent>();
        var calculations = new List<CalculationRecord>
        {
            new("Coefficient partition", "ai > 0 → first inverting summer; ai < 0 → output inverting summer", $"{positive.Length} positive, {negative.Length} negative"),
            new("Offset source", "Reference injection uses the entered VCC rail; no independent VREF value is accepted", $"VCC = {request.SupplyVccVoltage:G6} V", "VEE and GND remain explicit circuit nets.")
        };

        StageValues? first = null;
        if (needsFirstStage)
        {
            var ratios = positive.Select(input => new Ratio(input.Name, $"Positive coefficient {input.Name}", input.Coefficient)).ToList();
            if (positiveOffset > 0) ratios.Add(new Ratio("VCC_OFFSET_1", "Positive output offset injection", positiveOffset / request.SupplyVccVoltage));
            first = SelectStage(ratios, request.Components);
        }

        var secondRatios = negative.Select(input => new Ratio(input.Name, $"Negative coefficient {input.Name}", -input.Coefficient)).ToList();
        if (needsFirstStage) secondRatios.Add(new Ratio("STAGE1", "First-stage unity recovery", 1));
        if (negativeOffset > 0) secondRatios.Add(new Ratio("VCC_OFFSET_2", "Negative output offset injection", negativeOffset / request.SupplyVccVoltage));
        var second = SelectStage(secondRatios, request.Components);

        var builder = new CircuitBuilder();
        builder.CreateNet("GND"); builder.CreateNet("VCC"); builder.CreateNet("VEE"); builder.CreateNet("VOUT");
        foreach (var input in request.Inputs) builder.CreateNet(input.Name);
        var resistorNumber = 0;
        var opAmpNumber = 0;

        void AddResistor(string net1, string net2, double theoretical, double selected, string role)
        {
            var reference = $"R{++resistorNumber}";
            var resistor = builder.Add(ComponentFactory.Resistor(reference, selected));
            builder.Connect(net1, resistor.GetPin("1"));
            builder.Connect(net2, resistor.GetPin("2"));
            components.Add(new(reference, role, "Resistance", theoretical, selected, "ohm", request.Components.OptimizeStandardValues ? request.Components.ResistorSeries : null));
        }

        void AddOpAmp(string sumNet, string outputNet, StageValues values, IReadOnlyList<(string Net, string Key, string Role)> inputs)
        {
            var opAmp = builder.Add(ComponentFactory.OpAmp($"U{++opAmpNumber}"));
            builder.Connect(sumNet, opAmp.GetPin("IN-"));
            builder.Connect(outputNet, opAmp.GetPin("OUT"));
            builder.Connect("VCC", opAmp.GetPin("V+"));
            builder.Connect("VEE", opAmp.GetPin("V-"));
            AddResistor(outputNet, sumNet, values.TheoreticalFeedback, values.Feedback, $"{opAmp.Reference} feedback");
            var parallel = 1d / values.Feedback;
            foreach (var input in inputs)
            {
                var selected = values.Inputs[input.Key];
                var theoretical = values.TheoreticalInputs[input.Key];
                AddResistor(input.Net, sumNet, theoretical, selected, input.Role);
                parallel += 1d / selected;
            }
            if (request.Components.BiasCompensation)
            {
                var theoretical = 1d / parallel;
                var selected = request.Components.OptimizeStandardValues
                    ? AnalogPreferredValueSeries.Nearest(theoretical, request.Components.ResistorSeries, request.Components.ResistorMinimumOhm, request.Components.ResistorMaximumOhm)
                    : Math.Clamp(theoretical, request.Components.ResistorMinimumOhm, request.Components.ResistorMaximumOhm);
                AddResistor("GND", $"{sumNet}_PLUS", theoretical, selected, $"{opAmp.Reference} input-bias compensation");
                builder.Connect($"{sumNet}_PLUS", opAmp.GetPin("IN+"));
            }
            else builder.Connect("GND", opAmp.GetPin("IN+"));
        }

        if (first is not null)
        {
            builder.CreateNet("SUM_1"); builder.CreateNet("STAGE_1_OUT");
            var inputs = positive.Select(input => (input.Name, input.Name, $"{input.Name} positive weight" )).ToList();
            if (positiveOffset > 0) inputs.Add(("VCC", "VCC_OFFSET_1", "VCC-derived positive offset"));
            AddOpAmp("SUM_1", "STAGE_1_OUT", first, inputs);
        }

        builder.CreateNet("SUM_OUT");
        var outputInputs = negative.Select(input => (input.Name, input.Name, $"{input.Name} negative weight")).ToList();
        if (needsFirstStage) outputInputs.Add(("STAGE_1_OUT", "STAGE1", "First-stage polarity recovery"));
        if (negativeOffset > 0) outputInputs.Add(("VCC", "VCC_OFFSET_2", "VCC-derived negative offset"));
        AddOpAmp("SUM_OUT", "VOUT", second, outputInputs);

        foreach (var input in request.Inputs.Where(input => input.Coefficient == 0))
            calculations.Add(new("Zero coefficient", $"{input.Name} coefficient = 0", "Input retained as a named port and omitted from resistor synthesis"));
        calculations.Add(new("Affine topology", "Vout = b + Σ(ai·Ui)", needsFirstStage ? "Two-stage sign-partitioned inverting summers" : "Single inverting summer"));
        return new(builder.Build(), components, needsFirstStage ? "Two-stage sign-partitioned affine summer" : "Single-stage inverting affine summer", calculations);
    }

    private static StageValues SelectStage(IReadOnlyList<Ratio> ratios, ComponentSelectionConstraints constraints)
    {
        if (ratios.Count == 0)
            throw new InvalidOperationException("An affine stage must have at least one signal or reference input.");
        var lower = constraints.ResistorMinimumOhm;
        var upper = constraints.ResistorMaximumOhm;
        foreach (var ratio in ratios)
        {
            lower = Math.Max(lower, constraints.ResistorMinimumOhm * ratio.Target);
            upper = Math.Min(upper, constraints.ResistorMaximumOhm * ratio.Target);
        }
        if (lower > upper)
            throw new InvalidOperationException("No common feedback resistance can realize all requested ratios inside the configured resistor range.");
        // Select the center of the feasible logarithmic range. Users only need to constrain R min/max;
        // an arbitrary preferred-resistance field is not part of the operation specification.
        var theoreticalFeedback = Math.Sqrt(lower * upper);
        var theoreticalInputs = ratios.ToDictionary(ratio => ratio.Key, ratio => theoreticalFeedback / ratio.Target, StringComparer.OrdinalIgnoreCase);
        if (!constraints.OptimizeStandardValues)
            return new(theoreticalFeedback, theoreticalFeedback, theoreticalInputs, theoreticalInputs);

        // A common feedback resistor couples every coefficient in the stage. Searching the
        // complete bounded E-series is still small (even E96) and avoids false E12 failures.
        var candidates = AnalogPreferredValueSeries.All(constraints.ResistorSeries, lower, upper);
        if (candidates.Count == 0) throw new InvalidOperationException("The selected E-series has no feedback resistor inside the feasible range.");
        var inputCandidates = AnalogPreferredValueSeries.All(constraints.ResistorSeries, constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm);
        StageValues? best = null;
        var bestScore = double.PositiveInfinity;
        foreach (var feedback in candidates)
        {
            var selected = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var score = 0d;
            foreach (var ratio in ratios)
            {
                var targetInput = feedback / ratio.Target;
                var input = AnalogPreferredValueSeries.Nearest(targetInput, inputCandidates);
                selected.Add(ratio.Key, input);
                var relative = (feedback / input - ratio.Target) / Math.Max(ratio.Target, 1e-12);
                score += relative * relative;
            }
            var spread = selected.Values.Append(feedback).Max() / selected.Values.Append(feedback).Min();
            score += 1e-7 * Math.Log(spread + 1);
            if (score >= bestScore) continue;
            bestScore = score;
            best = new(theoreticalFeedback, feedback, selected, theoreticalInputs);
        }
        return best!;
    }

    private static void Validate(AnalogComputeDesignRequest request)
    {
        if (request.Inputs.Count is < 1 or > 8) throw new ArgumentException("General Linear Combination supports 1 to 8 inputs.");
        if (request.SupplyVccVoltage <= 0 || request.SupplyVeeVoltage >= 0 || request.SupplyVccVoltage <= request.SupplyVeeVoltage)
            throw new ArgumentException("VCC must be positive and VEE must be negative.");
        if (request.Inputs.All(input => input.Coefficient == 0) && request.OutputOffsetVoltage == 0)
            throw new ArgumentException("At least one coefficient or the output offset must be non-zero.");
        if (request.Inputs.Any(input => !double.IsFinite(input.Coefficient) || input.MinimumVoltage > input.MaximumVoltage || input.NominalVoltage < input.MinimumVoltage || input.NominalVoltage > input.MaximumVoltage))
            throw new ArgumentException("Input coefficients and voltage ranges must be finite and consistent.");
        var c = request.Components;
        if (!(c.ResistorMinimumOhm > 0) || c.ResistorMaximumOhm < c.ResistorMinimumOhm)
            throw new ArgumentException("Resistor constraints are invalid.");
    }
}
