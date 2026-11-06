using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Synthesis;

/// <summary>
/// Expands a physical Pi/T seed into a real multi-section ladder. Intermediate
/// resistances follow a geometric progression, so each section performs a local
/// impedance transformation before all elements are jointly optimized.
/// </summary>
public static class CascadedMatchingSynthesizer
{
    public static SynthesisAttempt Expand(SynthesisAttempt seed, AttenuationMatchingDesignRequest request, int sections)
    {
        if (!seed.Feasible || seed.Topology is null) return seed;
        if (sections < 2) throw new ArgumentOutOfRangeException(nameof(sections));

        var sourceResistance = request.Source.ImpedanceAt(request.DesignFrequencyHz).Real;
        var loadResistance = request.Load.ImpedanceAt(request.DesignFrequencyHz).Real;
        if (!(sourceResistance > 0) || !(loadResistance > 0))
            return SynthesisAttempt.Failed(seed.Name, seed.Description, "Multi-section synthesis requires positive real port resistances.", seed.Response);

        var interfaceResistance = Enumerable.Range(0, sections + 1)
            .Select(index => sourceResistance * Math.Pow(loadResistance / sourceResistance, index / (double)sections))
            .ToArray();
        var elements = new List<NetworkElementSpec>(seed.Topology.Elements.Count * sections);
        var referenceIndex = 0;
        for (var section = 0; section < sections; section++)
        {
            var sectionSource = section == 0
                ? request.Source
                : new PortImpedanceModel($"Section {section + 1} source", interfaceResistance[section], 0, PortImpedanceType.PureResistance, request.DesignFrequencyHz);
            var sectionLoad = section == sections - 1
                ? request.Load
                : new PortImpedanceModel($"Section {section + 1} load", interfaceResistance[section + 1], 0, PortImpedanceType.PureResistance, request.DesignFrequencyHz);
            var sectionRequest = request with { Source = sectionSource, Load = sectionLoad };
            var local = seed.Topology.Family switch
            {
                TopologyFamily.PiMatching => new PiMatchingSynthesizer().Synthesize(sectionRequest, seed.Response),
                TopologyFamily.TMatching => new TMatchingSynthesizer().Synthesize(sectionRequest, seed.Response),
                _ => SynthesisAttempt.Failed(seed.Name, seed.Description, "Only Pi/T seeds can be expanded into matching sections.", seed.Response)
            };
            if (!local.Feasible || local.Topology is null)
                return SynthesisAttempt.Failed(seed.Name, seed.Description, $"Section {section + 1} could not be synthesized: {local.Reason}", seed.Response);
            foreach (var element in local.Topology.Elements)
            {
                referenceIndex++;
                elements.Add(element with
                {
                    Reference = NetworkTopologyBuilder.PrefixFor(element.Type) + referenceIndex
                });
            }
        }

        var name = $"{sections}-Section {seed.Name}";
        var description = $"{sections}-section cascaded ladder initialized from {seed.Name}; all {elements.Count} reactances are optimized independently against the complete frequency plan.";
        var topology = seed.Topology with
        {
            Name = name,
            Elements = elements,
            Description = description,
            LimitNote = "Finite-order lumped multi-section network; final acceptance uses the complete graph and the canonical band grid."
        };
        return new SynthesisAttempt(topology, name, description, true,
            $"Cascaded {sections}-section seed with independent multi-point optimization", seed.Response);
    }
}
