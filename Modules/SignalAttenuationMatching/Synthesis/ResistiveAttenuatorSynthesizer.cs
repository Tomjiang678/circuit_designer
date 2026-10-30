using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Synthesis;

public sealed record SynthesisAttempt(
    NetworkTopologySpec? Topology,
    string Name,
    string Description,
    bool Feasible,
    string Reason,
    MatchingResponse Response = MatchingResponse.LowPass)
{
    public static SynthesisAttempt Failed(string name, string description, string reason, MatchingResponse response = MatchingResponse.LowPass) =>
        new(null, name, description, false, reason, response);
}

internal static class PadMath
{
    public sealed record Abcd(double A, double B, double C, double D);

    public static Abcd T(double seriesInput, double seriesOutput, double shunt) => new(
        1 + seriesInput / shunt,
        seriesInput + seriesOutput + seriesInput * seriesOutput / shunt,
        1 / shunt,
        1 + seriesOutput / shunt);

    public static Abcd Pi(double shuntInput, double series, double shuntOutput) => new(
        1 + series / shuntOutput,
        series,
        1 / shuntInput + 1 / shuntOutput + series / (shuntInput * shuntOutput),
        1 + series / shuntInput);

    public static (double Zin, double Zout, double Gain) Metrics(Abcd abcd, double sourceResistance, double loadResistance)
    {
        var zin = (abcd.A * loadResistance + abcd.B) / (abcd.C * loadResistance + abcd.D);
        var zout = (abcd.D * sourceResistance + abcd.B) / (abcd.C * sourceResistance + abcd.A);
        var denominator = abcd.A * loadResistance + abcd.B + abcd.C * sourceResistance * loadResistance + abcd.D * sourceResistance;
        var gain = 4 * sourceResistance * loadResistance / (denominator * denominator);
        return (zin, zout, gain);
    }

    public static double[] Residuals(Abcd abcd, double sourceResistance, double loadResistance, double targetGain)
    {
        var metrics = Metrics(abcd, sourceResistance, loadResistance);
        var scale = Math.Max(1, Math.Max(sourceResistance, loadResistance));
        return
        [
            (metrics.Zin - sourceResistance) / scale,
            (metrics.Zout - loadResistance) / scale,
            metrics.Gain - targetGain
        ];
    }
}

/// <summary>
/// Closed-form matched T/Pi pads plus a bounded positive-value Newton solve for
/// unequal ports. The final performance is always re-derived by the graph analyzer.
/// </summary>
public sealed class ResistiveAttenuatorSynthesizer
{
    public SynthesisAttempt Synthesize(AttenuationMatchingDesignRequest request, TopologyFamily family)
    {
        ArgumentNullException.ThrowIfNull(request);
        var attenuation = request.Attenuation.TargetAttenuationDb;
        if (attenuation < -1e-9) return SynthesisAttempt.Failed(Name(family), Description(family), "Target attenuation must not be negative.");
        if (request.Source.IsComplex || request.Load.IsComplex)
            return SynthesisAttempt.Failed(Name(family), Description(family),
                "A purely resistive pad cannot cancel a reactive port. Use the Impedance Matching mode for R+jX ports.");
        if (attenuation <= 1e-9)
        {
            if (Math.Abs(request.Source.ResistanceOhm - request.Load.ResistanceOhm) > Math.Max(request.Source.ResistanceOhm, request.Load.ResistanceOhm) * 1e-9)
                return SynthesisAttempt.Failed(Name(family), Description(family),
                    "A 0 dB matched bypass is only possible with equal port resistances; a lossless direct link cannot match unequal source and load.");
            return new SynthesisAttempt(
                new NetworkTopologySpec("Direct connection", TopologyFamily.DirectConnection, MatchingResponse.LowPass,
                    [], "Ideal through connection with no inserted components."),
                "Direct connection", "0 dB direct connection", true, "None");        }

        return family switch
        {
            TopologyFamily.TPads => SynthesizeT(request),
            TopologyFamily.PiPads => SynthesizePi(request),
            TopologyFamily.LPads => SynthesizeL(request),
            TopologyFamily.CascadedPads => SynthesizeCascaded(request),
            _ => SynthesisAttempt.Failed(Name(family), Description(family), "The requested family is not a resistive attenuation topology.")
        };
    }

    private static SynthesisAttempt SynthesizeT(AttenuationMatchingDesignRequest request)
    {
        var source = request.Source.ResistanceOhm;
        var load = request.Load.ResistanceOhm;
        var target = request.Attenuation.TargetAttenuationDb;
        if (Math.Abs(source - load) < source * 1e-9)
        {
            var k = Math.Pow(10, target / 20);
            var series = source * (k - 1) / (k + 1);
            var shunt = 2 * source * k / (k * k - 1);
            if (!(series > 0) || !(shunt > 0) || !double.IsFinite(series) || !double.IsFinite(shunt))
                return SynthesisAttempt.Failed("T-Pad", "Matched resistive T attenuator", "The requested attenuation does not yield finite positive resistors.");
            return new SynthesisAttempt(
                new NetworkTopologySpec("Matched T-Pad", TopologyFamily.TPads, MatchingResponse.LowPass,
                    [new("R1", Core.Circuit.ComponentType.Resistor, ElementPlacement.Series, series),
                     new("R2", Core.Circuit.ComponentType.Resistor, ElementPlacement.Shunt, shunt),
                     new("R3", Core.Circuit.ComponentType.Resistor, ElementPlacement.Series, series)],
                    $"Matched T-pad: R1=R3={series:G6} ohm, R2={shunt:G6} ohm; both ports {source:G6} ohm."),
                "Matched T-Pad", "Symmetric T attenuator", true, "Closed-form matched T solution");
        }
        return SolveNumeric(request, TopologyFamily.TPads, "Unequal-Impedance T-Pad",
            "T-pad with independent source/load terminations", (a, b, c) => PadMath.T(a, b, c));
    }

    private static SynthesisAttempt SynthesizePi(AttenuationMatchingDesignRequest request)
    {
        var source = request.Source.ResistanceOhm;
        var load = request.Load.ResistanceOhm;
        var target = request.Attenuation.TargetAttenuationDb;
        if (Math.Abs(source - load) < source * 1e-9)
        {
            var k = Math.Pow(10, target / 20);
            var shunt = source * (k + 1) / (k - 1);
            var series = source * (k * k - 1) / (2 * k);
            if (!(series > 0) || !(shunt > 0) || !double.IsFinite(series) || !double.IsFinite(shunt))
                return SynthesisAttempt.Failed("Pi-Pad", "Matched resistive Pi attenuator", "The requested attenuation does not yield finite positive resistors.");
            return new SynthesisAttempt(
                new NetworkTopologySpec("Matched Pi-Pad", TopologyFamily.PiPads, MatchingResponse.LowPass,
                    [new("R1", Core.Circuit.ComponentType.Resistor, ElementPlacement.Shunt, shunt),
                     new("R2", Core.Circuit.ComponentType.Resistor, ElementPlacement.Series, series),
                     new("R3", Core.Circuit.ComponentType.Resistor, ElementPlacement.Shunt, shunt)],
                    $"Matched Pi-pad: R1=R3={shunt:G6} ohm, R2={series:G6} ohm; both ports {source:G6} ohm."),
                "Matched Pi-Pad", "Symmetric Pi attenuator", true, "Closed-form matched Pi solution");
        }
        return SolveNumeric(request, TopologyFamily.PiPads, "Unequal-Impedance Pi-Pad",
            "Pi-pad with independent source/load terminations", (a, b, c) => PadMath.Pi(a, b, c));
    }

    private static SynthesisAttempt SolveNumeric(
        AttenuationMatchingDesignRequest request,
        TopologyFamily family,
        string name,
        string description,
        Func<double, double, double, PadMath.Abcd> makeAbcd)
    {
        var source = request.Source.ResistanceOhm;
        var load = request.Load.ResistanceOhm;
        var targetGain = Math.Pow(10, -request.Attenuation.TargetAttenuationDb / 10);
        var initialReference = Math.Sqrt(source * load);
        var k = Math.Pow(10, request.Attenuation.TargetAttenuationDb / 20);
        var seeds = new List<(double A, double B, double C)>();
        if (family == TopologyFamily.TPads)
        {
            var series = initialReference * (k - 1) / (k + 1);
            var shunt = 2 * initialReference * k / (k * k - 1);
            seeds.Add((series, series, shunt));
            seeds.Add((Math.Max(1e-3, series) * source / initialReference, Math.Max(1e-3, series) * load / initialReference, Math.Max(1e-3, shunt)));
            seeds.Add((Math.Max(1e-3, source * .01), Math.Max(1e-3, load * .01), Math.Max(1e-3, initialReference)));
        }
        else
        {
            var shunt = initialReference * (k + 1) / (k - 1);
            var series = initialReference * (k * k - 1) / (2 * k);
            seeds.Add((shunt, series, shunt));
            seeds.Add((Math.Max(1e-3, shunt) * source / initialReference, Math.Max(1e-3, series), Math.Max(1e-3, shunt) * load / initialReference));
            seeds.Add((Math.Max(1e-3, source), Math.Max(1e-3, load * .01), Math.Max(1e-3, load)));
        }
        // Multi-start fallback: the unequal-port equations are nonlinear and a
        // single symmetric seed can converge to a non-physical branch. A small
        // deterministic grid is cheap and covers both port scales.
        foreach (var firstScale in new[] { .05, .2, 1, 5, 20 })
        foreach (var secondScale in new[] { .05, .2, 1, 5, 20 })
            seeds.Add((Math.Max(1e-6, source * firstScale), Math.Max(1e-6, load * secondScale), Math.Max(1e-6, initialReference)));

        foreach (var seed in seeds)
        {
            if (seed.A <= 0 || seed.B <= 0 || seed.C <= 0 || !double.IsFinite(seed.A) || !double.IsFinite(seed.B) || !double.IsFinite(seed.C)) continue;
            var solved = TryNewton(seed, source, load, targetGain, makeAbcd);
            if (solved is null) continue;
            var values = solved.Value;
            var abcd = makeAbcd(values.A, values.B, values.C);
            var metrics = PadMath.Metrics(abcd, source, load);
            if (Math.Abs(metrics.Zin / source - 1) > 1e-5 || Math.Abs(metrics.Zout / load - 1) > 1e-5 || Math.Abs(metrics.Gain / targetGain - 1) > 1e-4)
                continue;
            var elements = family == TopologyFamily.TPads
                ? new[]
                {
                    new NetworkElementSpec("R1", Core.Circuit.ComponentType.Resistor, ElementPlacement.Series, values.A),
                    new NetworkElementSpec("R2", Core.Circuit.ComponentType.Resistor, ElementPlacement.Shunt, values.C),
                    new NetworkElementSpec("R3", Core.Circuit.ComponentType.Resistor, ElementPlacement.Series, values.B)
                }
                : new[]
                {
                    new NetworkElementSpec("R1", Core.Circuit.ComponentType.Resistor, ElementPlacement.Shunt, values.A),
                    new NetworkElementSpec("R2", Core.Circuit.ComponentType.Resistor, ElementPlacement.Series, values.B),
                    new NetworkElementSpec("R3", Core.Circuit.ComponentType.Resistor, ElementPlacement.Shunt, values.C)
                };
            var descriptionText = family == TopologyFamily.TPads
                ? $"Unequal T-pad from {source:G6} ohm to {load:G6} ohm; R1={values.A:G6}, R2={values.C:G6}, R3={values.B:G6} ohm."
                : $"Unequal Pi-pad from {source:G6} ohm to {load:G6} ohm; R1={values.A:G6}, R2={values.B:G6}, R3={values.C:G6} ohm.";
            return new SynthesisAttempt(
                new NetworkTopologySpec(name, family, MatchingResponse.LowPass, elements, descriptionText),
                name, description, true, "Numerically solved matching pad");
        }
        return SynthesisAttempt.Failed(name, description, "No positive resistor solution satisfies both port impedances and the requested attenuation.");
    }

    private static (double A, double B, double C)? TryNewton(
        (double A, double B, double C) seed,
        double source,
        double load,
        double targetGain,
        Func<double, double, double, PadMath.Abcd> makeAbcd)
    {
        var x = new[] { Math.Log(seed.A), Math.Log(seed.B), Math.Log(seed.C) };
        for (var iteration = 0; iteration < 80; iteration++)
        {
            var values = x.Select(Math.Exp).ToArray();
            var residuals = PadMath.Residuals(makeAbcd(values[0], values[1], values[2]), source, load, targetGain);
            var norm = residuals.Sum(value => value * value);
            if (norm < 1e-20) break;
            var jacobian = new double[3, 3];
            for (var column = 0; column < 3; column++)
            {
                var step = 1e-5;
                var perturbed = (double[])x.Clone();
                perturbed[column] += step;
                var pValues = perturbed.Select(Math.Exp).ToArray();
                var pResiduals = PadMath.Residuals(makeAbcd(pValues[0], pValues[1], pValues[2]), source, load, targetGain);
                for (var row = 0; row < 3; row++) jacobian[row, column] = (pResiduals[row] - residuals[row]) / step;
            }
            var delta = Solve3(jacobian, residuals.Select(value => -value).ToArray());
            if (delta is null) return null;
            var maxChange = delta.Max(value => Math.Abs(value));
            var damping = maxChange > 2 ? 2 / maxChange : 1;
            for (var index = 0; index < 3; index++) x[index] += damping * delta[index];
            if (x.Any(value => !double.IsFinite(value) || value > 40 || value < -40)) return null;
        }
        var result = x.Select(Math.Exp).ToArray();
        return result.All(value => value > 0 && double.IsFinite(value)) ? (result[0], result[1], result[2]) : null;
    }

    private static double[]? Solve3(double[,] matrix, double[] right)
    {
        var a = (double[,])matrix.Clone();
        var b = (double[])right.Clone();
        for (var column = 0; column < 3; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < 3; row++) if (Math.Abs(a[row, column]) > Math.Abs(a[pivot, column])) pivot = row;
            if (Math.Abs(a[pivot, column]) < 1e-14) return null;
            if (pivot != column)
            {
                for (var item = column; item < 3; item++) (a[column, item], a[pivot, item]) = (a[pivot, item], a[column, item]);
                (b[column], b[pivot]) = (b[pivot], b[column]);
            }
            for (var row = column + 1; row < 3; row++)
            {
                var factor = a[row, column] / a[column, column];
                for (var item = column; item < 3; item++) a[row, item] -= factor * a[column, item];
                b[row] -= factor * b[column];
            }
        }
        var result = new double[3];
        for (var row = 2; row >= 0; row--)
        {
            var value = b[row];
            for (var column = row + 1; column < 3; column++) value -= a[row, column] * result[column];
            result[row] = value / a[row, row];
        }
        return result.All(double.IsFinite) ? result : null;
    }

    private static SynthesisAttempt SynthesizeL(AttenuationMatchingDesignRequest request)
    {
        var source = request.Source.ResistanceOhm;
        var load = request.Load.ResistanceOhm;
        if (Math.Abs(source - load) <= source * 1e-9)
            return SynthesisAttempt.Failed("Resistive L-Pad", "Minimum-loss L-pad",
                "A two-resistor L-pad cannot provide a matched, non-zero attenuation when both ports are equal; use a T/Pi pad or 0 dB bypass.");
        var target = request.Attenuation.TargetAttenuationDb;
        foreach (var orientation in new[] { "series-shunt", "shunt-series" })
        {
            var solved = SolveLPad(source, load, orientation);
            if (solved is null) continue;
            var (r1, r2, zin, zout, gain) = solved.Value;
            var attenuation = -10 * Math.Log10(Math.Max(gain, 1e-30));
            if (request.Mode is DesignMode.Attenuation &&
                Math.Abs(attenuation - target) > request.Attenuation.AllowedAttenuationErrorDb)
                continue;
            NetworkElementSpec[] elements = orientation == "series-shunt"
                ? [new("R1", Core.Circuit.ComponentType.Resistor, ElementPlacement.Series, r1),
                   new("R2", Core.Circuit.ComponentType.Resistor, ElementPlacement.Shunt, r2)]
                : [new("R1", Core.Circuit.ComponentType.Resistor, ElementPlacement.Shunt, r1),
                   new("R2", Core.Circuit.ComponentType.Resistor, ElementPlacement.Series, r2)];
            return new SynthesisAttempt(
                new NetworkTopologySpec("Resistive L-Pad", TopologyFamily.LPads, MatchingResponse.LowPass, elements,
                    $"Minimum-loss L-pad ({orientation}): {source:G6} ohm to {load:G6} ohm; R1={r1:G6}, R2={r2:G6} ohm; fixed attenuation {attenuation:G6} dB."),
                "Resistive L-Pad", "Minimum-loss unequal-port L-pad", true, "Solved two-resistor matching L-pad");
        }
        return SynthesisAttempt.Failed("Resistive L-Pad", "Minimum-loss L-pad",
            $"The fixed bidirectional L-pad loss is not within {request.Attenuation.AllowedAttenuationErrorDb:G4} dB of the {target:G6} dB target, or no positive solution exists.");
    }

    private static (double R1, double R2, double Zin, double Zout, double Gain)? SolveLPad(double source, double load, string orientation)
    {
        // Solve log(R1), log(R2) against the two port-resistance equations by Newton.
        var seeds = orientation == "series-shunt"
            ? new[] { (Math.Max(1e-3, source * .1), Math.Max(1e-3, load * .1)), (Math.Max(1e-3, source), Math.Max(1e-3, load)) }
            : new[] { (Math.Max(1e-3, source * .1), Math.Max(1e-3, source * .1)), (Math.Max(1e-3, source), Math.Max(1e-3, load)) };
        foreach (var seed in seeds)
        {
            var x = new[] { Math.Log(seed.Item1), Math.Log(seed.Item2) };
            for (var iteration = 0; iteration < 60; iteration++)
            {
                var ri = Math.Exp(x[0]);
                var rj = Math.Exp(x[1]);
                var (zin, zout) = orientation == "series-shunt"
                    ? (ri + rj * load / (rj + load), rj * (ri + source) / (rj + ri + source))
                    : (ri * (rj + load) / (ri + rj + load), rj + ri * source / (ri + source));
                var residuals = new[] { (zin - source) / Math.Max(source, load), (zout - load) / Math.Max(source, load) };
                if (residuals.Sum(value => value * value) < 1e-20) break;
                var jacobian = new double[2, 2];
                for (var column = 0; column < 2; column++)
                {
                    var step = 1e-5;
                    var p = (double[])x.Clone(); p[column] += step;
                    var p1 = Math.Exp(p[0]); var p2 = Math.Exp(p[1]);
                    var (pzin, pzout) = orientation == "series-shunt"
                        ? (p1 + p2 * load / (p2 + load), p2 * (p1 + source) / (p2 + p1 + source))
                        : (p1 * (p2 + load) / (p1 + p2 + load), p2 + p1 * source / (p1 + source));
                    jacobian[0, column] = ((pzin - source) / Math.Max(source, load) - residuals[0]) / step;
                    jacobian[1, column] = ((pzout - load) / Math.Max(source, load) - residuals[1]) / step;
                }
                var determinant = jacobian[0, 0] * jacobian[1, 1] - jacobian[0, 1] * jacobian[1, 0];
                if (Math.Abs(determinant) < 1e-14) break;
                var dx = (-residuals[0] * jacobian[1, 1] + residuals[1] * jacobian[0, 1]) / determinant;
                var dy = (-residuals[1] * jacobian[0, 0] + residuals[0] * jacobian[1, 0]) / determinant;
                x[0] += Math.Clamp(dx, -2, 2); x[1] += Math.Clamp(dy, -2, 2);
                if (x.Any(value => !double.IsFinite(value) || value > 40 || value < -40)) break;
            }
            var r1 = Math.Exp(x[0]); var r2 = Math.Exp(x[1]);
            var (zinFinal, zoutFinal) = orientation == "series-shunt"
                ? (r1 + r2 * load / (r2 + load), r2 * (r1 + source) / (r2 + r1 + source))
                : (r1 * (r2 + load) / (r1 + r2 + load), r2 + r1 * source / (r1 + source));
            if (Math.Abs(zinFinal / source - 1) < 1e-6 && Math.Abs(zoutFinal / load - 1) < 1e-6)
            {
                var abcd = orientation == "series-shunt"
                    ? new PadMath.Abcd(1 + r1 / r2, r1, 1 / r2, 1)
                    : new PadMath.Abcd(1, r2, 1 / r1, 1 + r2 / r1);
                var metrics = PadMath.Metrics(abcd, source, load);
                return (r1, r2, zinFinal, zoutFinal, metrics.Gain);
            }
        }
        return null;
    }

    private static SynthesisAttempt SynthesizeCascaded(AttenuationMatchingDesignRequest request)
    {
        var source = request.Source.ResistanceOhm;
        var load = request.Load.ResistanceOhm;
        var total = request.Attenuation.TargetAttenuationDb;
        if (total <= 0) return SynthesizeT(request);
        var stages = Math.Max(2, Math.Min(3, request.MaximumComponentCount is { } max && max < 6 ? 2 : 3));
        var elements = new List<NetworkElementSpec>();
        var totalStages = stages;
        var intermediate = new double[stages + 1];
        intermediate[0] = source; intermediate[^1] = load;
        for (var index = 1; index < stages; index++)
            intermediate[index] = Math.Pow(source, 1 - index / (double)stages) * Math.Pow(load, index / (double)stages);
        var perStage = total / stages;
        for (var stage = 0; stage < stages; stage++)
        {
            var stageRequest = request with
            {
                Source = new PortImpedanceModel("Source", intermediate[stage], 0, PortImpedanceType.PureResistance),
                Load = new PortImpedanceModel("Load", intermediate[stage + 1], 0, PortImpedanceType.PureResistance),
                Attenuation = request.Attenuation with { TargetAttenuationDb = perStage }
            };
            var attempt = SynthesizeT(stageRequest);
            if (!attempt.Feasible || attempt.Topology is null) return attempt with { Name = "Cascaded Pads", Description = "Multi-stage resistive pad" };
            var prefix = $"S{stage + 1}_";
            foreach (var element in attempt.Topology.Elements)
                elements.Add(element with { Reference = prefix + element.Reference });
        }
        var spec = new NetworkTopologySpec("Cascaded Resistive Pads", TopologyFamily.CascadedPads, MatchingResponse.LowPass, elements,
            $"{stages}-stage T-pad cascade from {source:G6} ohm to {load:G6} ohm; per-stage attenuation {perStage:G6} dB; total analysis is performed on the complete network.");
        return new SynthesisAttempt(spec, "Cascaded Resistive Pads", $"{stages}-stage T-pad cascade", true, "Cascaded matched sections");
    }

    private static string Name(TopologyFamily family) => family switch
    {
        TopologyFamily.TPads => "T-Pad",
        TopologyFamily.PiPads => "Pi-Pad",
        TopologyFamily.LPads => "Resistive L-Pad",
        TopologyFamily.CascadedPads => "Cascaded Pads",
        _ => family.ToString()
    };

    private static string Description(TopologyFamily family) => family switch
    {
        TopologyFamily.TPads => "Symmetric or unequal-impedance resistive T attenuator",
        TopologyFamily.PiPads => "Symmetric or unequal-impedance resistive Pi attenuator",
        TopologyFamily.LPads => "Minimum-loss two-resistor L-pad",
        TopologyFamily.CascadedPads => "Multi-stage resistive attenuator",
        _ => "Passive resistive network"
    };
}
