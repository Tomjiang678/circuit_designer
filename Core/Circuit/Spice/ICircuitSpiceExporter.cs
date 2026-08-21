namespace CircuitModuleDesigner.Core.Circuit.Spice;

public interface ICircuitSpiceExporter
{
    string Export(CircuitGraph graph, CircuitSpiceExportOptions options);
}
