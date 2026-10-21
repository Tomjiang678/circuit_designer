using System.Numerics;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Synthesis;

/// <summary>
/// Pi/T synthesis uses a virtual intermediate resistance and two independent
/// L-section matches. The two series (Pi) or shunt (T) elements are combined before
/// the final network is built; the resulting three-element network is re-analyzed
/// as a whole, so this is not a naive cascade of two independently terminated L pads.
/// </summary>
public sealed class PiMatchingSynthesizer
{
    public SynthesisAttempt Synthesize(AttenuationMatchingDesignRequest request, MatchingResponse response)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (response == MatchingResponse.Mixed) return SynthesizeMixed(request);
        var source = request.Source.ImpedanceAt(request.DesignFrequencyHz);
        var load = request.Load.ImpedanceAt(request.DesignFrequencyHz);
        var low = Math.Min(source.Real, load.Real);
        var high = Math.Max(source.Real, load.Real);
        var ratioQ = high > low ? Math.Sqrt(high / low - 1) : 0;
        var designQ = request.Topology.DesignQ ?? Math.Max(.5, ratioQ);
        if (!(designQ > 0) || !double.IsFinite(designQ))
            return SynthesisAttempt.Failed("Pi Matching", "Pi LC matching network", "The design Q must be finite and positive.", response);
        var virtualResistance = low / (1 + designQ * designQ);
        if (!(virtualResistance > 0) || !double.IsFinite(virtualResistance))
            return SynthesisAttempt.Failed("Pi Matching", "Pi LC matching network", "The selected Q does not produce a positive virtual resistance.", response);

        var input = ReactanceMatchingMath.Solve(source, new Complex(virtualResistance, 0), true, response);
        if (input is null)
            return SynthesisAttempt.Failed("Pi Matching", "Pi LC matching network",
                "No valid input-side L section exists for this complex source and virtual resistance.", response);
        var output = ReactanceMatchingMath.Solve(new Complex(virtualResistance, 0), load, false, response);
        if (output is null)
            return SynthesisAttempt.Failed("Pi Matching", "Pi LC matching network",
                "No valid output-side L section exists for this load and virtual resistance.", response);

        var seriesReactance = input.SeriesReactanceOhm + output.SeriesReactanceOhm;
        var omega = 2 * Math.PI * request.DesignFrequencyHz;
        var seriesIsInductor = response == MatchingResponse.LowPass;
        var shuntIsCapacitor = response == MatchingResponse.LowPass;
        var seriesValue = seriesIsInductor ? seriesReactance / omega : -1 / (omega * seriesReactance);
        var shuntInputValue = shuntIsCapacitor ? -1 / (omega * input.ShuntReactanceOhm) : input.ShuntReactanceOhm / omega;
        var shuntOutputValue = shuntIsCapacitor ? -1 / (omega * output.ShuntReactanceOhm) : output.ShuntReactanceOhm / omega;
        if (!(seriesValue > 0) || !(shuntInputValue > 0) || !(shuntOutputValue > 0) ||
            !double.IsFinite(seriesValue) || !double.IsFinite(shuntInputValue) || !double.IsFinite(shuntOutputValue))
            return SynthesisAttempt.Failed("Pi Matching", "Pi LC matching network", "The solved reactances do not convert to finite positive L/C values.", response);

        var seriesType = seriesIsInductor ? ComponentType.Inductor : ComponentType.Capacitor;
        var shuntType = shuntIsCapacitor ? ComponentType.Capacitor : ComponentType.Inductor;
        var elements = new[]
        {
            new NetworkElementSpec(shuntType == ComponentType.Inductor ? "L1" : "C1", shuntType, ElementPlacement.Shunt, shuntInputValue),
            new NetworkElementSpec(seriesType == ComponentType.Inductor ? "L2" : "C2", seriesType, ElementPlacement.Series, seriesValue),
            new NetworkElementSpec(shuntType == ComponentType.Inductor ? "L3" : "C3", shuntType, ElementPlacement.Shunt, shuntOutputValue)
        };
        var responseName = response == MatchingResponse.LowPass ? "Low-Pass" : "High-Pass";
        var description = $"{responseName} Pi network: virtual resistance {virtualResistance:G6} ohm, design Q={designQ:G6}; shunt {shuntType} at input/output and series {seriesType} between them.";
        return new SynthesisAttempt(
            new NetworkTopologySpec($"{responseName} Pi Matching", TopologyFamily.PiMatching, response, elements, description,
                "Design Q is a synthesis choice; loaded Q is measured separately from the final frequency sweep."),
            $"{responseName} Pi Matching", description, true, $"Virtual-resistance Pi synthesis with Q={designQ:G6}", response);
    }

    public SynthesisAttempt SynthesizeMixed(AttenuationMatchingDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.Source.ImpedanceAt(request.DesignFrequencyHz);
        var load = request.Load.ImpedanceAt(request.DesignFrequencyHz);
        var low = Math.Min(source.Real, load.Real);
        var high = Math.Max(source.Real, load.Real);
        var ratioQ = high > low ? Math.Sqrt(high / low - 1) : 0;
        var designQ = request.Topology.DesignQ ?? Math.Max(.5, ratioQ);
        var virtualResistance = low / (1 + designQ * designQ);
        var input = ReactanceMatchingMath.Solve(source, new Complex(virtualResistance, 0), true, MatchingResponse.LowPass);
        var output = ReactanceMatchingMath.Solve(new Complex(virtualResistance, 0), load, false, MatchingResponse.HighPass);
        if (input is null || output is null)
            return SynthesisAttempt.Failed("Mixed Pi Matching", "Mixed low/high-pass Pi network",
                "No positive-reactance mixed Pi solution exists for this port pair and virtual resistance.", MatchingResponse.Mixed);
        var omega = 2 * Math.PI * request.DesignFrequencyHz;
        var cIn = -1 / (omega * input.ShuntReactanceOhm);
        var lIn = input.SeriesReactanceOhm / omega;
        var cOut = -1 / (omega * output.SeriesReactanceOhm);
        var lOut = output.ShuntReactanceOhm / omega;
        if (!(cIn > 0) || !(lIn > 0) || !(cOut > 0) || !(lOut > 0))
            return SynthesisAttempt.Failed("Mixed Pi Matching", "Mixed low/high-pass Pi network",
                "The mixed Pi solution did not produce finite positive L/C values.", MatchingResponse.Mixed);
        var elements = new[]
        {
            new NetworkElementSpec("C1", ComponentType.Capacitor, ElementPlacement.Shunt, cIn),
            new NetworkElementSpec("L2", ComponentType.Inductor, ElementPlacement.Series, lIn),
            new NetworkElementSpec("C3", ComponentType.Capacitor, ElementPlacement.Series, cOut),
            new NetworkElementSpec("L4", ComponentType.Inductor, ElementPlacement.Shunt, lOut)
        };
        var description = $"Mixed Pi: low-pass input section (shunt C + series L) and high-pass output section (series C + shunt L); virtual R={virtualResistance:G6} Ω, Q={designQ:G6}.";
        return new SynthesisAttempt(new NetworkTopologySpec("Mixed Pi Matching", TopologyFamily.PiMatching, MatchingResponse.Mixed, elements, description,
            "Four-element mixed-reactance network: the series branch contains both L and C, so it is not a single low-pass or high-pass Pi."),
            "Mixed Pi Matching", description, true, "Mixed Pi synthesis", MatchingResponse.Mixed);
    }
}

public sealed class TMatchingSynthesizer
{
    public SynthesisAttempt Synthesize(AttenuationMatchingDesignRequest request, MatchingResponse response)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (response == MatchingResponse.Mixed) return SynthesizeMixed(request);
        var source = request.Source.ImpedanceAt(request.DesignFrequencyHz);
        var load = request.Load.ImpedanceAt(request.DesignFrequencyHz);
        var low = Math.Min(source.Real, load.Real);
        var high = Math.Max(source.Real, load.Real);
        var ratioQ = high > low ? Math.Sqrt(high / low - 1) : 0;
        var designQ = request.Topology.DesignQ ?? Math.Max(.5, ratioQ);
        if (!(designQ > 0) || !double.IsFinite(designQ))
            return SynthesisAttempt.Failed("T Matching", "T LC matching network", "The design Q must be finite and positive.", response);
        var virtualResistance = high * (1 + designQ * designQ);
        if (!(virtualResistance > 0) || !double.IsFinite(virtualResistance))
            return SynthesisAttempt.Failed("T Matching", "T LC matching network", "The selected Q does not produce a positive virtual resistance.", response);

        var input = ReactanceMatchingMath.Solve(source, new Complex(virtualResistance, 0), false, response);
        if (input is null)
            return SynthesisAttempt.Failed("T Matching", "T LC matching network",
                "No valid input-side L section exists for this source and virtual resistance.", response);
        var output = ReactanceMatchingMath.Solve(new Complex(virtualResistance, 0), load, true, response);
        if (output is null)
            return SynthesisAttempt.Failed("T Matching", "T LC matching network",
                "No valid output-side L section exists for this load and virtual resistance.", response);

        var shuntReactance = ReactanceMatchingMath.Parallel(new Complex(0, input.ShuntReactanceOhm), new Complex(0, output.ShuntReactanceOhm)).Imaginary;
        var omega = 2 * Math.PI * request.DesignFrequencyHz;
        var seriesIsInductor = response == MatchingResponse.LowPass;
        var shuntIsCapacitor = response == MatchingResponse.LowPass;
        var seriesInputValue = seriesIsInductor ? input.SeriesReactanceOhm / omega : -1 / (omega * input.SeriesReactanceOhm);
        var seriesOutputValue = seriesIsInductor ? output.SeriesReactanceOhm / omega : -1 / (omega * output.SeriesReactanceOhm);
        var shuntValue = shuntIsCapacitor ? -1 / (omega * shuntReactance) : shuntReactance / omega;
        if (!(seriesInputValue > 0) || !(seriesOutputValue > 0) || !(shuntValue > 0) ||
            !double.IsFinite(seriesInputValue) || !double.IsFinite(seriesOutputValue) || !double.IsFinite(shuntValue))
            return SynthesisAttempt.Failed("T Matching", "T LC matching network", "The solved reactances do not convert to finite positive L/C values.", response);

        var seriesType = seriesIsInductor ? ComponentType.Inductor : ComponentType.Capacitor;
        var shuntType = shuntIsCapacitor ? ComponentType.Capacitor : ComponentType.Inductor;
        var elements = new[]
        {
            new NetworkElementSpec(seriesType == ComponentType.Inductor ? "L1" : "C1", seriesType, ElementPlacement.Series, seriesInputValue),
            new NetworkElementSpec(shuntType == ComponentType.Inductor ? "L2" : "C2", shuntType, ElementPlacement.Shunt, shuntValue),
            new NetworkElementSpec(seriesType == ComponentType.Inductor ? "L3" : "C3", seriesType, ElementPlacement.Series, seriesOutputValue)
        };
        var responseName = response == MatchingResponse.LowPass ? "Low-Pass" : "High-Pass";
        var description = $"{responseName} T network: virtual resistance {virtualResistance:G6} ohm, design Q={designQ:G6}; series {seriesType} on both sides and shunt {shuntType} at the center.";
        return new SynthesisAttempt(
            new NetworkTopologySpec($"{responseName} T Matching", TopologyFamily.TMatching, response, elements, description,
                "Design Q is a synthesis choice; loaded Q is measured separately from the final frequency sweep."),
            $"{responseName} T Matching", description, true, $"Virtual-resistance T synthesis with Q={designQ:G6}", response);
    }

    public SynthesisAttempt SynthesizeMixed(AttenuationMatchingDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = request.Source.ImpedanceAt(request.DesignFrequencyHz);
        var load = request.Load.ImpedanceAt(request.DesignFrequencyHz);
        var low = Math.Min(source.Real, load.Real);
        var high = Math.Max(source.Real, load.Real);
        var ratioQ = high > low ? Math.Sqrt(high / low - 1) : 0;
        var designQ = request.Topology.DesignQ ?? Math.Max(.5, ratioQ);
        var virtualResistance = high * (1 + designQ * designQ);
        var input = ReactanceMatchingMath.Solve(source, new Complex(virtualResistance, 0), false, MatchingResponse.LowPass);
        var output = ReactanceMatchingMath.Solve(new Complex(virtualResistance, 0), load, true, MatchingResponse.HighPass);
        if (input is null || output is null)
            return SynthesisAttempt.Failed("Mixed T Matching", "Mixed low/high-pass T network",
                "No positive-reactance mixed T solution exists for this port pair and virtual resistance.", MatchingResponse.Mixed);
        var omega = 2 * Math.PI * request.DesignFrequencyHz;
        var lIn = input.SeriesReactanceOhm / omega;
        var cIn = -1 / (omega * input.ShuntReactanceOhm);
        var lOut = output.ShuntReactanceOhm / omega;
        var cOut = -1 / (omega * output.SeriesReactanceOhm);
        if (!(lIn > 0) || !(cIn > 0) || !(lOut > 0) || !(cOut > 0))
            return SynthesisAttempt.Failed("Mixed T Matching", "Mixed low/high-pass T network",
                "The mixed T solution did not produce finite positive L/C values.", MatchingResponse.Mixed);
        var elements = new[]
        {
            new NetworkElementSpec("L1", ComponentType.Inductor, ElementPlacement.Series, lIn),
            new NetworkElementSpec("C2", ComponentType.Capacitor, ElementPlacement.Shunt, cIn),
            new NetworkElementSpec("L3", ComponentType.Inductor, ElementPlacement.Shunt, lOut),
            new NetworkElementSpec("C4", ComponentType.Capacitor, ElementPlacement.Series, cOut)
        };
        var description = $"Mixed T: low-pass input section (series L + shunt C) and high-pass output section (shunt L + series C); virtual R={virtualResistance:G6} Ω, Q={designQ:G6}.";
        return new SynthesisAttempt(new NetworkTopologySpec("Mixed T Matching", TopologyFamily.TMatching, MatchingResponse.Mixed, elements, description,
            "Four-element mixed-reactance T: two shunt elements meet at the center; it is not a single low-pass or high-pass T."),
            "Mixed T Matching", description, true, "Mixed T synthesis", MatchingResponse.Mixed);
    }
}
