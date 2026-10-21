using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CircuitModuleDesigner.Core.Reporting;

/// <summary>
/// Shared JSON settings for design reports/snapshots. Report payloads always use
/// indented JSON, readable enum names and named NaN/Infinity literals so a
/// report can round-trip through any of the three modules.
/// </summary>
public static class ReportJson
{
    public static readonly JsonSerializerOptions Default = Create(camelCase: false);
    public static readonly JsonSerializerOptions CamelCase = Create(camelCase: true);

    private static JsonSerializerOptions Create(bool camelCase) => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = camelCase ? JsonNamingPolicy.CamelCase : null,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };
}
