using System.Text.Json;
using System.Text.Json.Serialization;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;

public sealed class JsonFilterDesignSnapshotExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Export(FilterDesignSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);
}
