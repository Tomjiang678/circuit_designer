using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

/// <summary>Loaded first-order passive RC synthesis using VSRC-Rs-network-VOUT-RL.</summary>
public sealed class PassiveRcSectionCalculator
{
    public SynthesizedSection Calculate(AnalogFilterSection section, FilterDesignRequest request)
    {
        if (section.ResponseType == FilterResponseType.BandPass)
            return CalculateTwoCornerBandPass(section, request);
        if (section.ResponseType == FilterResponseType.BandStop)
            return CalculateTwinT(section, request);
        if (section.Order != SectionOrder.First)
            throw new NotSupportedException("Passive RC synthesis is restricted to one first-order section.");
        if (section.ResponseType is not (FilterResponseType.LowPass or FilterResponseType.HighPass))
            throw new NotSupportedException("The first-order passive RC solver supports Low-Pass and High-Pass only.");
        if (request.SourceResistanceOhm < 0 || request.LoadResistanceOhm <= 0)
            throw new ArgumentException("Loaded RC synthesis requires Rs >= 0 and RL > 0.");

        var x = request.ComponentConstraints;
        var rippleFactor = Math.Sqrt(Math.Pow(10, request.PassbandRippleDb / 10) - 1);
        var targetFrequency = section.ResponseType == FilterResponseType.LowPass
            ? request.PassbandEdgeHz / rippleFactor
            : request.PassbandEdgeHz * rippleFactor;
        var targetSection = section with { PoleFrequencyHz = targetFrequency };
        var omega = 2 * Math.PI * targetFrequency;
        var minimumPassbandGain = Math.Pow(10, -request.PassbandRippleDb / 20);
        var theoreticalR = section.ResponseType == FilterResponseType.LowPass
            ? ChooseLowPassResistance(request, minimumPassbandGain)
            : ChooseHighPassResistance(request, minimumPassbandGain);
        var theoreticalC = section.ResponseType == FilterResponseType.LowPass
            ? 1 / (omega * Parallel(request.SourceResistanceOhm + theoreticalR, request.LoadResistanceOhm))
            : 1 / (omega * (request.SourceResistanceOhm + Parallel(theoreticalR, request.LoadResistanceOhm)));
        if (theoreticalC < x.CapacitorMinimumF || theoreticalC > x.CapacitorMaximumF)
            throw new InvalidOperationException("RcLoadedPoleNotAchievable: the required loaded capacitance is outside the selected component range.");

        var rCandidates = x.OptimizeStandardValues
            ? StandardValueSeries.GetNearby(theoreticalR, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, 4)
            : [theoreticalR];
        var cCandidates = x.OptimizeStandardValues
            ? StandardValueSeries.GetNearby(theoreticalC, x.CapacitorSeries, x.CapacitorMinimumF, x.CapacitorMaximumF, 4)
            : [theoreticalC];
        Candidate? best = null;
        foreach (var r in rCandidates)
        foreach (var c in cCandidates)
        {
            var measured = Measure(section.ResponseType, request.SourceResistanceOhm, request.LoadResistanceOhm, r, c);
            var frequencyError = (measured.FrequencyHz - targetFrequency) / targetFrequency;
            var gainError = measured.PassbandGain - 1;
            var preference = Math.Abs(Math.Log(r / request.PreferredResistanceOhm));
            var passbandPenalty = measured.PassbandGain < minimumPassbandGain
                ? 20 * (minimumPassbandGain - measured.PassbandGain)
                : 0;
            var score = Math.Abs(frequencyError) + .25 * Math.Abs(gainError) + .01 * preference + passbandPenalty;
            if (best is null || score < best.Score)
                best = new(r, c, measured.FrequencyHz, measured.PassbandGain, frequencyError, gainError, score);
        }
        var choice = best ?? throw new InvalidOperationException("RcLoadedPoleNotAchievable: no valid R/C candidate was found.");
        var values = new[]
        {
            new SynthesizedComponentValue("R", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm,
                theoreticalR, choice.Resistance, choice.Resistance == theoreticalR ? null : x.ResistorSeries, x.ResistorTolerancePercent),
            new SynthesizedComponentValue("C", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad,
                theoreticalC, choice.Capacitance, choice.Capacitance == theoreticalC ? null : x.CapacitorSeries, x.CapacitorTolerancePercent)
        };
        return new(targetSection, values, choice.FrequencyHz, 0, choice.PassbandGain,
            choice.FrequencyError, 0, choice.GainError);
    }

    public static (double FrequencyHz, double PassbandGain) Measure(
        FilterResponseType response, double sourceResistanceOhm, double loadResistanceOhm,
        double internalResistanceOhm, double capacitanceFarad)
    {
        if (response == FilterResponseType.LowPass)
        {
            var series = sourceResistanceOhm + internalResistanceOhm;
            return (1 / (2 * Math.PI * capacitanceFarad * Parallel(series, loadResistanceOhm)),
                loadResistanceOhm / (series + loadResistanceOhm));
        }
        var shunt = Parallel(internalResistanceOhm, loadResistanceOhm);
        return (1 / (2 * Math.PI * capacitanceFarad * (sourceResistanceOhm + shunt)),
            shunt / (sourceResistanceOhm + shunt));
    }

    public static Complex EvaluateLoadedFirstOrder(FilterResponseType response, double frequencyHz,
        double sourceResistanceOhm, double loadResistanceOhm, double internalResistanceOhm, double capacitanceFarad)
    {
        var measured = Measure(response, sourceResistanceOhm, loadResistanceOhm, internalResistanceOhm, capacitanceFarad);
        var normalized = Complex.ImaginaryOne * frequencyHz / measured.FrequencyHz;
        return response switch
        {
            FilterResponseType.LowPass => measured.PassbandGain / (1 + normalized),
            FilterResponseType.HighPass => measured.PassbandGain * normalized / (1 + normalized),
            _ => throw new ArgumentOutOfRangeException(nameof(response), "Loaded first-order evaluation supports LP/HP only.")
        };
    }

    private static double ChooseLowPassResistance(FilterDesignRequest request, double minimumGain)
    {
        var x = request.ComponentConstraints;
        var maximumForGain = request.LoadResistanceOhm * (1 / minimumGain - 1) - request.SourceResistanceOhm;
        var maximum = Math.Min(x.ResistorMaximumOhm, maximumForGain);
        if (maximum < x.ResistorMinimumOhm)
            throw new InvalidOperationException("RcPassbandLossTooHigh: Rs/RL and the resistor range cannot meet the requested passband loss.");
        return Math.Clamp(request.PreferredResistanceOhm, x.ResistorMinimumOhm, maximum);
    }

    private static double ChooseHighPassResistance(FilterDesignRequest request, double minimumGain)
    {
        var x = request.ComponentConstraints;
        if (request.SourceResistanceOhm <= 0)
            return Math.Clamp(request.PreferredResistanceOhm, x.ResistorMinimumOhm, x.ResistorMaximumOhm);
        var requiredParallel = request.SourceResistanceOhm * minimumGain / (1 - minimumGain);
        if (requiredParallel >= request.LoadResistanceOhm)
            throw new InvalidOperationException("RcPassbandLossTooHigh: RL is too small relative to Rs for the requested high-pass gain.");
        var minimumInternal = requiredParallel * request.LoadResistanceOhm / (request.LoadResistanceOhm - requiredParallel);
        var minimum = Math.Max(x.ResistorMinimumOhm, minimumInternal);
        if (minimum > x.ResistorMaximumOhm)
            throw new InvalidOperationException("RcPassbandLossTooHigh: the required high-pass shunt resistance exceeds the selected range.");
        return Math.Clamp(request.PreferredResistanceOhm, minimum, x.ResistorMaximumOhm);
    }

    private static double Parallel(double first, double second) => first * second / (first + second);

    private static SynthesizedSection CalculateTwoCornerBandPass(AnalogFilterSection section, FilterDesignRequest request)
    {
        var x = request.ComponentConstraints;
        var targetLow = request.PassbandLowerHz;
        var targetHigh = request.PassbandUpperHz;
        if (!(targetLow > 0 && targetHigh > targetLow))
            throw new ArgumentException("RcBandPassCornersNotAchievable: lower corner must be below upper corner.");
        var rTheory = Math.Clamp(request.PreferredResistanceOhm, x.ResistorMinimumOhm, x.ResistorMaximumOhm);
        var cHighPassTheory = 1 / (2 * Math.PI * targetLow * rTheory);
        var cLowPassTheory = 1 / (2 * Math.PI * targetHigh * rTheory);
        if (cHighPassTheory < x.CapacitorMinimumF || cHighPassTheory > x.CapacitorMaximumF ||
            cLowPassTheory < x.CapacitorMinimumF || cLowPassTheory > x.CapacitorMaximumF)
            throw new InvalidOperationException("RcBandPassCornersNotAchievable: initial capacitance scale is outside the selected range.");

        BandPassCandidate? best = null;
        foreach (var r1 in ResistanceScales(rTheory, x))
        foreach (var r2 in ResistanceScales(rTheory, x))
        {
            var c1Theory = 1 / (2 * Math.PI * targetLow * r1);
            var c2Theory = 1 / (2 * Math.PI * targetHigh * r2);
            if (c1Theory < x.CapacitorMinimumF || c1Theory > x.CapacitorMaximumF ||
                c2Theory < x.CapacitorMinimumF || c2Theory > x.CapacitorMaximumF) continue;
            foreach (var c1 in Candidates(c1Theory, x.CapacitorSeries, x.CapacitorMinimumF, x.CapacitorMaximumF, x.OptimizeStandardValues, 2))
            foreach (var c2 in Candidates(c2Theory, x.CapacitorSeries, x.CapacitorMinimumF, x.CapacitorMaximumF, x.OptimizeStandardValues, 2))
            {
            var measured = MeasureBandPass(request.SourceResistanceOhm, request.LoadResistanceOhm, r1, c1, r2, c2, targetLow, targetHigh);
            var cornerError = Math.Abs(Math.Log(measured.LowerCornerHz / targetLow)) + Math.Abs(Math.Log(measured.UpperCornerHz / targetHigh));
            var score = cornerError + .03 * Math.Abs(Math.Log(r1 / request.PreferredResistanceOhm)) + .03 * Math.Abs(Math.Log(r2 / request.PreferredResistanceOhm));
            if (best is null || score < best.Score) best = new(r1, c1, r2, c2, measured, score);
            }
        }
        var choice = best ?? throw new InvalidOperationException("RcBandPassCornersNotAchievable: no joint R/C candidate was found.");
        var targetCenter = Math.Sqrt(targetLow * targetHigh);
        var actualCenter = Math.Sqrt(choice.Measurement.LowerCornerHz * choice.Measurement.UpperCornerHz);
        var targetQ = targetCenter / (targetHigh - targetLow);
        var actualQ = actualCenter / (choice.Measurement.UpperCornerHz - choice.Measurement.LowerCornerHz);
        return new(section,
        [
            Value("R_HP", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, rTheory, choice.R1, x.ResistorSeries, x.ResistorTolerancePercent),
            Value("C_HP", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, cHighPassTheory, choice.C1, x.CapacitorSeries, x.CapacitorTolerancePercent),
            Value("R_LP", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, rTheory, choice.R2, x.ResistorSeries, x.ResistorTolerancePercent),
            Value("C_LP", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, cLowPassTheory, choice.C2, x.CapacitorSeries, x.CapacitorTolerancePercent)
        ], actualCenter, actualQ, choice.Measurement.PeakGain,
            (actualCenter - targetCenter) / targetCenter, targetQ == 0 ? 0 : (actualQ - targetQ) / targetQ,
            choice.Measurement.PeakGain - 1);
    }

    private static SynthesizedSection CalculateTwinT(AnalogFilterSection section, FilterDesignRequest request)
    {
        var x = request.ComponentConstraints;
        var target = Math.Sqrt(request.StopbandLowerHz * request.StopbandUpperHz);
        var rTheory = Math.Clamp(request.PreferredResistanceOhm, x.ResistorMinimumOhm, x.ResistorMaximumOhm);
        var cTheory = 1 / (2 * Math.PI * target * rTheory);
        if (cTheory < x.CapacitorMinimumF || cTheory > x.CapacitorMaximumF)
            throw new InvalidOperationException("TwinTNotchDepthInsufficient: the required capacitance is outside the selected range.");
        TwinTCandidate? best = null;
        foreach (var r in Candidates(rTheory, x.ResistorSeries, x.ResistorMinimumOhm, x.ResistorMaximumOhm, x.OptimizeStandardValues, 5))
        foreach (var c in Candidates(cTheory, x.CapacitorSeries, x.CapacitorMinimumF, x.CapacitorMaximumF, x.OptimizeStandardValues, 5))
        {
            var measured = MeasureTwinT(request.SourceResistanceOhm, request.LoadResistanceOhm, r, c, target);
            var score = Math.Abs(Math.Log(measured.NotchFrequencyHz / target)) + 3 * measured.NotchGain + .01 * Math.Abs(Math.Log(r / request.PreferredResistanceOhm));
            if (best is null || score < best.Score) best = new(r, c, measured, score);
        }
        var choice = best ?? throw new InvalidOperationException("TwinTNotchDepthInsufficient: no matched standard-value candidate was found.");
        var series = x.OptimizeStandardValues ? x.ResistorSeries : (StandardComponentSeries?)null;
        var capacitorSeries = x.OptimizeStandardValues ? x.CapacitorSeries : (StandardComponentSeries?)null;
        return new(section,
        [
            new("R1", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, rTheory, choice.R, series, x.ResistorTolerancePercent),
            new("R2", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, rTheory, choice.R, series, x.ResistorTolerancePercent),
            new("R_SHUNT_A", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, rTheory, choice.R, series, x.ResistorTolerancePercent),
            new("R_SHUNT_B", ComponentType.Resistor, ComponentParameterNames.ResistanceOhm, rTheory, choice.R, series, x.ResistorTolerancePercent),
            new("C1", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, cTheory, choice.C, capacitorSeries, x.CapacitorTolerancePercent),
            new("C2", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, cTheory, choice.C, capacitorSeries, x.CapacitorTolerancePercent),
            new("C_SHUNT_A", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, cTheory, choice.C, capacitorSeries, x.CapacitorTolerancePercent),
            new("C_SHUNT_B", ComponentType.Capacitor, ComponentParameterNames.CapacitanceFarad, cTheory, choice.C, capacitorSeries, x.CapacitorTolerancePercent)
        ], choice.Measurement.NotchFrequencyHz, section.Q, choice.Measurement.PassbandGain,
            (choice.Measurement.NotchFrequencyHz - target) / target, 0, choice.Measurement.PassbandGain - 1);
    }

    public static Complex EvaluateTwoCornerBandPass(double frequencyHz, double rs, double rl, double r1, double c1, double r2, double c2)
    {
        var s = new Complex(0, 2 * Math.PI * frequencyHz);
        var zc1 = 1 / (s * c1);
        var zLoad = 1 / (1d / rl + s * c2);
        var downstream = 1 / (1d / r1 + 1d / (r2 + zLoad));
        return downstream / (rs + zc1 + downstream) * zLoad / (r2 + zLoad);
    }

    private static BandPassMeasurement MeasureBandPass(double rs, double rl, double r1, double c1, double r2, double c2, double targetLow, double targetHigh)
    {
        var frequencies = Enumerable.Range(0, 101).Select(index => targetLow / 8 * Math.Pow(targetHigh / targetLow * 64, index / 100d)).ToArray();
        var gains = frequencies.Select(f => EvaluateTwoCornerBandPass(f, rs, rl, r1, c1, r2, c2).Magnitude).ToArray();
        var peakIndex = Array.IndexOf(gains, gains.Max());
        var threshold = gains[peakIndex] / Math.Sqrt(2);
        var lower = Enumerable.Range(0, peakIndex + 1).MinBy(index => Math.Abs(gains[index] - threshold));
        var upper = Enumerable.Range(peakIndex, gains.Length - peakIndex).MinBy(index => Math.Abs(gains[index] - threshold));
        return new(frequencies[lower], frequencies[upper], gains[peakIndex]);
    }

    public static Complex EvaluateTwinT(double frequencyHz, double rs, double rl, double r, double c)
    {
        var s = new Complex(0, 2 * Math.PI * frequencyHz);
        var size = rs > 0 ? 4 : 3;
        var y = new Complex[size, size];
        var b = new Complex[size];
        var vin = rs > 0 ? 0 : -1;
        var lowMid = rs > 0 ? 1 : 0;
        var highMid = rs > 0 ? 2 : 1;
        var vout = rs > 0 ? 3 : 2;
        void Branch(int a, int z, Complex admittance)
        {
            if (a >= 0) y[a, a] += admittance;
            if (z >= 0) y[z, z] += admittance;
            if (a >= 0 && z >= 0) { y[a, z] -= admittance; y[z, a] -= admittance; }
        }
        void ToFixed(int node, Complex admittance, Complex voltage) { y[node, node] += admittance; b[node] += admittance * voltage; }
        if (rs > 0) ToFixed(vin, 1 / rs, Complex.One);
        if (rs > 0) { Branch(vin, lowMid, 1 / r); Branch(vin, highMid, s * c); }
        Branch(lowMid, vout, 1 / r); Branch(lowMid, -1, 2 * s * c);
        Branch(highMid, vout, s * c); Branch(highMid, -1, 2 / r);
        Branch(vout, -1, 1 / rl);
        if (rs == 0) { ToFixed(lowMid, 1 / r, Complex.One); ToFixed(highMid, s * c, Complex.One); }
        return Solve(y, b)[vout];
    }

    private static TwinTMeasurement MeasureTwinT(double rs, double rl, double r, double c, double target)
    {
        var frequencies = Enumerable.Range(0, 241).Select(index => target / 4 * Math.Pow(16, index / 240d)).ToArray();
        var gains = frequencies.Select(f => EvaluateTwinT(f, rs, rl, r, c).Magnitude).ToArray();
        var notch = Array.IndexOf(gains, gains.Min());
        return new(frequencies[notch], gains[notch], Math.Max(gains[0], gains[^1]));
    }

    private static Complex[] Solve(Complex[,] matrix, Complex[] rhs)
    {
        var n = rhs.Length;
        for (var k = 0; k < n; k++)
        {
            var pivot = Enumerable.Range(k, n - k).MaxBy(row => matrix[row, k].Magnitude);
            if (matrix[pivot, k].Magnitude < 1e-24) throw new InvalidOperationException("Twin-T nodal matrix is singular.");
            if (pivot != k) { for (var j = k; j < n; j++) (matrix[k, j], matrix[pivot, j]) = (matrix[pivot, j], matrix[k, j]); (rhs[k], rhs[pivot]) = (rhs[pivot], rhs[k]); }
            for (var i = k + 1; i < n; i++) { var factor = matrix[i, k] / matrix[k, k]; for (var j = k; j < n; j++) matrix[i, j] -= factor * matrix[k, j]; rhs[i] -= factor * rhs[k]; }
        }
        var result = new Complex[n];
        for (var i = n - 1; i >= 0; i--) { var value = rhs[i]; for (var j = i + 1; j < n; j++) value -= matrix[i, j] * result[j]; result[i] = value / matrix[i, i]; }
        return result;
    }

    private static IReadOnlyList<double> Candidates(double value, StandardComponentSeries series, double minimum, double maximum, bool optimize, int eachSide) =>
        optimize ? StandardValueSeries.GetNearby(value, series, minimum, maximum, eachSide) : [value];
    private static IReadOnlyList<double> ResistanceScales(double preferred, ComponentSelectionConstraints constraints)
    {
        if (!constraints.OptimizeStandardValues) return [preferred];
        return new[] { .1, .25, .5, 1d, 2, 4, 10 }
            .Select(scale => Math.Clamp(preferred * scale, constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm))
            .SelectMany(value => StandardValueSeries.GetNearby(value, constraints.ResistorSeries,
                constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm, 1))
            .Distinct().Order().ToArray();
    }
    private static SynthesizedComponentValue Value(string role, ComponentType type, string parameter, double theory, double selected, StandardComponentSeries series, double tolerance) =>
        new(role, type, parameter, theory, selected, selected == theory ? null : series, tolerance);
    private sealed record Candidate(double Resistance, double Capacitance, double FrequencyHz,
        double PassbandGain, double FrequencyError, double GainError, double Score);
    private sealed record BandPassMeasurement(double LowerCornerHz, double UpperCornerHz, double PeakGain);
    private sealed record BandPassCandidate(double R1, double C1, double R2, double C2, BandPassMeasurement Measurement, double Score);
    private sealed record TwinTMeasurement(double NotchFrequencyHz, double NotchGain, double PassbandGain);
    private sealed record TwinTCandidate(double R, double C, TwinTMeasurement Measurement, double Score);
}
