using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Synthesis;

public enum LNetworkVariant
{
    SeriesL_ShuntC,
    SeriesC_ShuntL,
    ShuntC_SeriesL,
    ShuntL_SeriesC
}

public sealed record ReactancePair(double SeriesReactanceOhm, double ShuntReactanceOhm, bool ShuntAtInput, MatchingResponse Response)
{
    public bool IsSeriesInductor => SeriesReactanceOhm > 0;
    public bool IsShuntCapacitor => ShuntReactanceOhm < 0;
}

internal static class ReactanceMatchingMath
{
    public static Complex InputImpedance(Complex load, double seriesReactance, double shuntReactance, bool shuntAtInput) =>
        shuntAtInput
            ? Parallel(new Complex(0, shuntReactance), new Complex(0, seriesReactance) + load)
            : new Complex(0, seriesReactance) + Parallel(new Complex(0, shuntReactance), load);

    public static Complex OutputImpedance(Complex source, double seriesReactance, double shuntReactance, bool shuntAtInput) =>
        shuntAtInput
            ? new Complex(0, seriesReactance) + Parallel(new Complex(0, shuntReactance), source)
            : Parallel(new Complex(0, shuntReactance), new Complex(0, seriesReactance) + source);

    public static Complex Parallel(Complex first, Complex second) => first * second / (first + second);

    public static double[] Residuals(Complex source, Complex load, double seriesReactance, double shuntReactance, bool shuntAtInput)
    {
        var zin = InputImpedance(load, seriesReactance, shuntReactance, shuntAtInput);
        var target = Complex.Conjugate(source);
        var scale = Math.Max(1, Math.Max(source.Magnitude, load.Magnitude));
        var zout = OutputImpedance(source, seriesReactance, shuntReactance, shuntAtInput);
        var outputTarget = Complex.Conjugate(load);
        // The output residual is intentionally weighted lower: for a reciprocal
        // two-element L network it is normally implied by the input match, but it
        // catches wrong sign/position solutions during the solve.
        return
        [
            (zin.Real - target.Real) / scale,
            (zin.Imaginary - target.Imaginary) / scale,
            .25 * (zout.Real - outputTarget.Real) / scale,
            .25 * (zout.Imaginary - outputTarget.Imaginary) / scale
        ];
    }

    public static ReactancePair? Solve(Complex source, Complex load, bool shuntAtInput, MatchingResponse response)
    {
        var high = Math.Max(source.Real, load.Real);
        var low = Math.Min(source.Real, load.Real);
        if (!(high > 0) || !(low > 0)) return null;
        var ratio = high / low;
        if (ratio <= 1 + 1e-9) return null;
        var q = Math.Sqrt(ratio - 1);
        var seriesMagnitude = q * low;
        var shuntMagnitude = high / q;
        var seriesSign = response == MatchingResponse.HighPass ? -1 : 1;
        var shuntSign = response == MatchingResponse.HighPass ? 1 : -1;
        var seeds = new[]
        {
            (seriesSign * seriesMagnitude, shuntSign * shuntMagnitude),
            (seriesSign * seriesMagnitude * .7, shuntSign * shuntMagnitude * 1.3),
            (seriesSign * seriesMagnitude * 1.3, shuntSign * shuntMagnitude * .7)
        };
        foreach (var seed in seeds)
        {
            var solved = TryNewton(source, load, shuntAtInput, response, seed.Item1, seed.Item2);
            if (solved is null) continue;
            return solved;
        }
        return null;
    }

    private static ReactancePair? TryNewton(Complex source, Complex load, bool shuntAtInput, MatchingResponse response, double seriesSeed, double shuntSeed)
    {
        if (!double.IsFinite(seriesSeed) || !double.IsFinite(shuntSeed)) return null;
        var variables = new[] { seriesSeed, shuntSeed };
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var residuals = Residuals(source, load, variables[0], variables[1], shuntAtInput);
            var norm = residuals.Sum(value => value * value);
            if (norm < 1e-18) break;
            var jacobian = new double[residuals.Length, 2];
            for (var column = 0; column < 2; column++)
            {
                var step = 1e-5 * Math.Max(1, Math.Abs(variables[column]));
                var perturbed = (double[])variables.Clone();
                perturbed[column] += step;
                var changed = Residuals(source, load, perturbed[0], perturbed[1], shuntAtInput);
                for (var row = 0; row < residuals.Length; row++) jacobian[row, column] = (changed[row] - residuals[row]) / step;
            }
            var normal = new double[2, 2];
            var rhs = new double[2];
            for (var row = 0; row < residuals.Length; row++)
            {
                normal[0, 0] += jacobian[row, 0] * jacobian[row, 0];
                normal[0, 1] += jacobian[row, 0] * jacobian[row, 1];
                normal[1, 1] += jacobian[row, 1] * jacobian[row, 1];
                rhs[0] -= jacobian[row, 0] * residuals[row];
                rhs[1] -= jacobian[row, 1] * residuals[row];
            }
            normal[1, 0] = normal[0, 1];
            normal[0, 0] += 1e-12;
            normal[1, 1] += 1e-12;
            var determinant = normal[0, 0] * normal[1, 1] - normal[0, 1] * normal[1, 0];
            if (Math.Abs(determinant) < 1e-24) return null;
            var ds = (rhs[0] * normal[1, 1] - rhs[1] * normal[0, 1]) / determinant;
            var dp = (normal[0, 0] * rhs[1] - normal[1, 0] * rhs[0]) / determinant;
            var maxStep = Math.Max(Math.Abs(ds), Math.Abs(dp));
            var damping = maxStep > Math.Max(1, Math.Abs(source.Magnitude)) ? 2 / maxStep : 1;
            variables[0] += damping * ds;
            variables[1] += damping * dp;
            if (!double.IsFinite(variables[0]) || !double.IsFinite(variables[1])) return null;
        }

        var final = Residuals(source, load, variables[0], variables[1], shuntAtInput);
        if (final.Sum(value => value * value) > 1e-12) return null;
        if (response == MatchingResponse.LowPass && (variables[0] <= 0 || variables[1] >= 0)) return null;
        if (response == MatchingResponse.HighPass && (variables[0] >= 0 || variables[1] <= 0)) return null;
        return new ReactancePair(variables[0], variables[1], shuntAtInput, response);
    }
}

public sealed class LMatchingSynthesizer
{
    public SynthesisAttempt Synthesize(AttenuationMatchingDesignRequest request, LNetworkVariant variant)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.Source.ImpedanceAt(request.DesignFrequencyHz);
        var load = request.Load.ImpedanceAt(request.DesignFrequencyHz);
        var (seriesType, shuntType, shuntAtInput, response) = Define(variant);
        if (shuntAtInput && source.Real <= load.Real)
            return SynthesisAttempt.Failed(Name(variant), Description(variant),
                "This shunt-at-input L form requires the source resistance to be higher than the load resistance; the opposite high-side form must be used.");
        if (!shuntAtInput && load.Real <= source.Real)
            return SynthesisAttempt.Failed(Name(variant), Description(variant),
                "This shunt-at-output L form requires the load resistance to be higher than the source resistance; the opposite high-side form must be used.");
        var pair = ReactanceMatchingMath.Solve(source, load, shuntAtInput, response);
        if (pair is null)
            return SynthesisAttempt.Failed(Name(variant), Description(variant),
                "No positive-reactance solution satisfies the conjugate input/output match for this L position and response; try the opposite high-side form or another topology.");
        var omega = 2 * Math.PI * request.DesignFrequencyHz;
        var seriesValue = seriesType == ComponentType.Inductor
            ? pair.SeriesReactanceOhm / omega
            : -1 / (omega * pair.SeriesReactanceOhm);
        var shuntValue = shuntType == ComponentType.Inductor
            ? pair.ShuntReactanceOhm / omega
            : -1 / (omega * pair.ShuntReactanceOhm);
        if (!(seriesValue > 0) || !(shuntValue > 0) || !double.IsFinite(seriesValue) || !double.IsFinite(shuntValue))
            return SynthesisAttempt.Failed(Name(variant), Description(variant), "The solved reactances do not convert to finite positive L/C values.");
        var elements = shuntAtInput
            ? new[]
            {
                new NetworkElementSpec(shuntType == ComponentType.Inductor ? "L1" : "C1", shuntType, ElementPlacement.Shunt, shuntValue),
                new NetworkElementSpec(seriesType == ComponentType.Inductor ? "L2" : "C2", seriesType, ElementPlacement.Series, seriesValue)
            }
            : new[]
            {
                new NetworkElementSpec(seriesType == ComponentType.Inductor ? "L1" : "C1", seriesType, ElementPlacement.Series, seriesValue),
                new NetworkElementSpec(shuntType == ComponentType.Inductor ? "L2" : "C2", shuntType, ElementPlacement.Shunt, shuntValue)
            };
        var description = $"{Name(variant)} at {request.DesignFrequencyHz:G6} Hz: series {seriesType} = {seriesValue:G6}, shunt {shuntType} = {shuntValue:G6}; input conjugate match at the design frequency.";
        return new SynthesisAttempt(
            new NetworkTopologySpec(Name(variant), TopologyFamily.LMatching, response, elements, description,
                "Two ideal reactances; a single-frequency match. The physical component model is not a broadband port impedance model."),
            Name(variant), Description(variant), true, "Solved conjugate L match", response);
    }

    private static (ComponentType Series, ComponentType Shunt, bool ShuntAtInput, MatchingResponse Response) Define(LNetworkVariant variant) => variant switch
    {
        LNetworkVariant.SeriesL_ShuntC => (ComponentType.Inductor, ComponentType.Capacitor, false, MatchingResponse.LowPass),
        LNetworkVariant.SeriesC_ShuntL => (ComponentType.Capacitor, ComponentType.Inductor, false, MatchingResponse.HighPass),
        LNetworkVariant.ShuntC_SeriesL => (ComponentType.Inductor, ComponentType.Capacitor, true, MatchingResponse.LowPass),
        LNetworkVariant.ShuntL_SeriesC => (ComponentType.Capacitor, ComponentType.Inductor, true, MatchingResponse.HighPass),
        _ => throw new ArgumentOutOfRangeException(nameof(variant))
    };

    public static string Name(LNetworkVariant variant) => variant switch
    {
        LNetworkVariant.SeriesL_ShuntC => "Series L + Shunt C",
        LNetworkVariant.SeriesC_ShuntL => "Series C + Shunt L",
        LNetworkVariant.ShuntC_SeriesL => "Shunt C + Series L",
        LNetworkVariant.ShuntL_SeriesC => "Shunt L + Series C",
        _ => variant.ToString()
    };

    public static string Description(LNetworkVariant variant) => variant switch
    {
        LNetworkVariant.SeriesL_ShuntC => "Low-pass L network; shunt capacitor at the output side",
        LNetworkVariant.SeriesC_ShuntL => "High-pass L network; shunt inductor at the output side",
        LNetworkVariant.ShuntC_SeriesL => "Low-pass L network; shunt capacitor at the input side",
        LNetworkVariant.ShuntL_SeriesC => "High-pass L network; shunt inductor at the input side",
        _ => "LC L matching network"
    };
}
