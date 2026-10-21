using System.Globalization;
using System.Text;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Circuit.Spice;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Topology;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Services;

public sealed class AttenuationMatchingSpiceExportService
{
    public string ExportDesign(TopologyCandidate candidate, AttenuationMatchingDesignRequest request, SpiceNetlistDialect dialect = SpiceNetlistDialect.Ltspice)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var options = new CircuitSpiceExportOptions($"Circuit Module Designer - {candidate.TopologyName}")
        {
            Dialect = dialect,
            AdditionalComments =
            [
                $"Passive design network only; source/load are external test conditions at {request.DesignFrequencyHz:G6} Hz.",
                "Only ideal R/L/C elements are represented; no PCB parasitics, transmission lines or vendor models are included."
            ]
        };
        return Create(dialect).Export(candidate.Graph, options);
    }

    /// <summary>
    /// Testbench graph contains the design network plus explicit source and load
    /// equivalents. Source/load impedance is never added both as a graph element
    /// and as an exporter option.
    /// </summary>
    public string ExportTestbench(TopologyCandidate candidate, AttenuationMatchingDesignRequest request, SpiceNetlistDialect dialect = SpiceNetlistDialect.Ltspice)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var graph = BuildTestbenchGraph(candidate.Graph, request);
        var options = new CircuitSpiceExportOptions($"Circuit Module Designer - {candidate.TopologyName} testbench")
        {
            Dialect = dialect,
            Sources = [new SpiceIndependentSource("V_SRC", SpiceSourceKind.Voltage, "VSRC", "0", AcMagnitude: 1)],
            Analyses = [new SpiceAcAnalysis(SpiceSweepScale.Decade, 40, request.StartFrequencyHz, request.StopFrequencyHz)],
            OutputExpressions = ["V(VIN)", "V(VOUT)"],
            AdditionalComments =
            [
                $"AC testbench: V_SRC is a 1 V RMS Thevenin source at {request.DesignFrequencyHz:G6} Hz; source and load models are explicit R/L/C elements.",
                $"Source model: {request.Source.ModelDescription}.",
                $"Load model: {request.Load.ModelDescription}.",
                "For a series R-L/R-C port the reactance is frequency-dependent; a Custom R+jX port is represented by its design-frequency equivalent and is not a broadband physical model."
            ]
        };
        return Create(dialect).Export(graph, options);
    }

    public string Export(TopologyCandidate candidate, AttenuationMatchingDesignRequest request, SpiceNetlistDialect dialect = SpiceNetlistDialect.Ltspice) =>
        ExportTestbench(candidate, request, dialect);

    public void SaveTestbench(TopologyCandidate candidate, AttenuationMatchingDesignRequest request, string fileName, SpiceNetlistDialect dialect = SpiceNetlistDialect.Ltspice)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A netlist file path is required.", nameof(fileName));
        File.WriteAllText(fileName, ExportTestbench(candidate, request, dialect), new UTF8Encoding(false));
    }

    public void SaveDesign(TopologyCandidate candidate, AttenuationMatchingDesignRequest request, string fileName, SpiceNetlistDialect dialect = SpiceNetlistDialect.Ltspice)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A netlist file path is required.", nameof(fileName));
        File.WriteAllText(fileName, ExportDesign(candidate, request, dialect), new UTF8Encoding(false));
    }

    private static ICircuitSpiceExporter Create(SpiceNetlistDialect dialect) => dialect switch
    {
        SpiceNetlistDialect.Ltspice => new LtspiceNetlistExporter(),
        SpiceNetlistDialect.Multisim => new MultisimNetlistExporter(),
        _ => new CircuitSpiceExporter()
    };

    private static CircuitGraph BuildTestbenchGraph(CircuitGraph design, AttenuationMatchingDesignRequest request)
    {
        var builder = new CircuitBuilder();
        foreach (var component in design.Components) builder.Add(component);
        foreach (var net in design.Nets) builder.Connect(net.Name, net.Pins.ToArray());

        var source = request.Source;
        builder.Add(ComponentFactory.Resistor("R_SRC", source.ResistanceOhm));
        builder.Connect("VSRC", builder.ResolvePin("R_SRC", "1"));
        if (source.Type == PortImpedanceType.Custom && Math.Abs(source.ReactanceOhm) > 1e-12)
        {
            if (source.ReactanceOhm > 0)
            {
                builder.Add(ComponentFactory.Inductor("L_SRC", source.ReactanceOhm / (2 * Math.PI * request.DesignFrequencyHz)));
                builder.Connect("N_SRC", builder.ResolvePin("R_SRC", "2"), builder.ResolvePin("L_SRC", "1"));
                builder.Connect(NetworkTopologyBuilder.InputNet, builder.ResolvePin("L_SRC", "2"));
            }
            else
            {
                builder.Add(ComponentFactory.Capacitor("C_SRC", -1 / (2 * Math.PI * request.DesignFrequencyHz * source.ReactanceOhm)));
                builder.Connect("N_SRC", builder.ResolvePin("R_SRC", "2"), builder.ResolvePin("C_SRC", "1"));
                builder.Connect(NetworkTopologyBuilder.InputNet, builder.ResolvePin("C_SRC", "2"));
            }
        }
        else
            builder.Connect(NetworkTopologyBuilder.InputNet, builder.ResolvePin("R_SRC", "2"));
        var load = request.Load;
        builder.Add(ComponentFactory.Resistor("R_LOAD", load.ResistanceOhm));
        if (load.Type == PortImpedanceType.Custom && Math.Abs(load.ReactanceOhm) > 1e-12)
        {
            if (load.ReactanceOhm > 0)
            {
                builder.Add(ComponentFactory.Inductor("L_LOAD", load.ReactanceOhm / (2 * Math.PI * request.DesignFrequencyHz)));
                builder.Connect(NetworkTopologyBuilder.OutputNet, builder.ResolvePin("R_LOAD", "1"));
                builder.Connect("N_LOAD", builder.ResolvePin("R_LOAD", "2"), builder.ResolvePin("L_LOAD", "1"));
                builder.Connect(NetworkTopologyBuilder.GroundNet, builder.ResolvePin("L_LOAD", "2"));
            }
            else
            {
                builder.Add(ComponentFactory.Capacitor("C_LOAD", -1 / (2 * Math.PI * request.DesignFrequencyHz * load.ReactanceOhm)));
                builder.Connect(NetworkTopologyBuilder.OutputNet, builder.ResolvePin("R_LOAD", "1"));
                builder.Connect("N_LOAD", builder.ResolvePin("R_LOAD", "2"), builder.ResolvePin("C_LOAD", "1"));
                builder.Connect(NetworkTopologyBuilder.GroundNet, builder.ResolvePin("C_LOAD", "2"));
            }
        }
        else
        {
            builder.Connect(NetworkTopologyBuilder.OutputNet, builder.ResolvePin("R_LOAD", "1"));
            builder.Connect(NetworkTopologyBuilder.GroundNet, builder.ResolvePin("R_LOAD", "2"));
        }        return builder.Build();
    }
}
