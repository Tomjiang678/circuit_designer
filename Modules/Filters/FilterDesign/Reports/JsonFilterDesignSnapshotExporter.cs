using CircuitModuleDesigner.Core.Reporting;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;

public sealed class JsonFilterDesignSnapshotExporter
{
    public string Export(FilterDesignSnapshot snapshot) => System.Text.Json.JsonSerializer.Serialize(snapshot, ReportJson.CamelCase);
}
