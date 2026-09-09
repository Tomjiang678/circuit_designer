using System.Text;

namespace CircuitModuleDesigner.Core.Circuit.Spice;

/// <summary>
/// Backward-compatible generic SPICE facade. Component mapping is owned by
/// CircuitGraphSpiceSerializer and is shared by all simulator adapters.
/// </summary>
public class CircuitSpiceExporter : ICircuitSpiceExporter
{
    private readonly CircuitGraphSpiceSerializer serializer;

    public CircuitSpiceExporter(CircuitGraphSpiceSerializer? serializer = null) => this.serializer = serializer ?? new CircuitGraphSpiceSerializer();

    public virtual string Export(CircuitGraph graph, CircuitSpiceExportOptions options) => Write(serializer.Serialize(graph, options, DialectOverride));

    protected virtual SpiceNetlistDialect? DialectOverride => null;

    internal static string Write(SpiceNetlistDocument document)
    {
        var text = new StringBuilder();
        text.AppendLine(document.Title);
        foreach (var comment in document.Comments) text.AppendLine("* " + comment);
        text.AppendLine();
        foreach (var line in document.ElementLines) text.AppendLine(line);
        if (document.ModelAndLibraryLines.Count > 0)
        {
            text.AppendLine();
            foreach (var line in document.ModelAndLibraryLines) text.AppendLine(line);
        }
        if (document.AnalysisLines.Count > 0 || document.OutputLines.Count > 0)
        {
            text.AppendLine();
            foreach (var line in document.AnalysisLines) text.AppendLine(line);
            foreach (var line in document.OutputLines) text.AppendLine(line);
        }
        text.AppendLine(".END");
        return text.ToString();
    }
}

public sealed class LtspiceNetlistExporter : CircuitSpiceExporter
{
    public LtspiceNetlistExporter(CircuitGraphSpiceSerializer? serializer = null) : base(serializer) { }
    protected override SpiceNetlistDialect? DialectOverride => SpiceNetlistDialect.Ltspice;
}

public sealed class MultisimNetlistExporter : CircuitSpiceExporter
{
    public MultisimNetlistExporter(CircuitGraphSpiceSerializer? serializer = null) : base(serializer) { }
    protected override SpiceNetlistDialect? DialectOverride => SpiceNetlistDialect.Multisim;
}
