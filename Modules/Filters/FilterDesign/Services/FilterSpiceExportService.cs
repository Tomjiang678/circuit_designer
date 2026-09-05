using System.Text;
using CircuitModuleDesigner.Core.Circuit.Spice;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Services;

public interface IFilterSpiceExportService
{
    string Export(FilterDesignResult result);
    void Save(FilterDesignResult result, string filePath);
}

public sealed class FilterSpiceExportService : IFilterSpiceExportService
{
    private readonly ICircuitSpiceExporter exporter;
    public FilterSpiceExportService(ICircuitSpiceExporter? exporter = null) => this.exporter = exporter ?? new CircuitSpiceExporter();

    public string Export(FilterDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var graph = result.CircuitGraph ?? throw new InvalidOperationException("The design result does not contain a CircuitGraph.");
        var request = result.DesignRequest ?? throw new InvalidOperationException("The design result does not contain its design request.");
        var analysis = new FilterAnalysisService().CreateOptions(request);
        var passive = request.Mode == FilterMode.Passive;
        var title = $"Circuit Module Designer - {request.Mode} {request.ResponseType} {request.Approximation} {request.Implementation}";
        return exporter.Export(graph, new CircuitSpiceExportOptions(title, analysis.StartFrequencyHz, analysis.StopFrequencyHz, analysis.PointsPerDecade,
            SourceResistanceOhm: passive && request.SourceResistanceOhm > 0 ? request.SourceResistanceOhm : null,
            LoadResistanceOhm: passive ? request.LoadResistanceOhm : null));
    }

    public void Save(FilterDesignResult result, string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A CIR file path is required.", nameof(filePath));
        File.WriteAllText(filePath, Export(result), new UTF8Encoding(false));
    }
}
