using CircuitModuleDesigner.Services;

namespace CircuitModuleDesigner.Models;

/// <summary>
/// A saved design file. The module-specific snapshot is stored as JSON text so
/// the shell can archive and view any module's design without knowing its
/// request/result types.
/// </summary>
public sealed record DesignDocument
{
    public const string CurrentSchema = "CircuitModuleDesigner.Design.v1";

    public string Schema { get; init; } = CurrentSchema;
    public string Application { get; init; } = AppInfo.Name;
    public string AppVersion { get; init; } = AppInfo.Version;
    public DateTimeOffset SavedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Category { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string SnapshotJson { get; init; } = "{}";
    public string? Netlist { get; init; }
}
