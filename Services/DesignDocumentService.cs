using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CircuitModuleDesigner.Models;

namespace CircuitModuleDesigner.Services;

/// <summary>
/// Reads and writes <c>.cmddesign</c> files: a small envelope that identifies the
/// module and stores its JSON snapshot, summary and optional netlist. The shell
/// can therefore open any saved design without knowing the module's types.
/// </summary>
public sealed class DesignDocumentService
{
    public const string Extension = ".cmddesign";
    public const string FileFilter = "Circuit design (*.cmddesign)|*.cmddesign|All files (*.*)|*.*";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DesignDocument Create(IDesignModuleHost host, string category, DateTimeOffset? savedAt = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!host.HasDesign)
            throw new InvalidOperationException("Generate a verified design before saving it.");
        if (host.IsStale)
            throw new InvalidOperationException("Parameters changed after the last design. Recalculate before saving it.");
        var snapshot = host.BuildDesignJson();
        ValidateSnapshot(snapshot);
        string? netlist;
        try { netlist = host.BuildNetlist(); }
        catch { netlist = null; }
        return new DesignDocument
        {
            Category = category ?? string.Empty,
            Module = host.ModuleTitle,
            SavedAtUtc = savedAt ?? DateTimeOffset.UtcNow,
            Summary = host.BuildDesignSummary(),
            SnapshotJson = snapshot,
            Netlist = string.IsNullOrWhiteSpace(netlist) ? null : netlist
        };
    }

    public string Serialize(DesignDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, Options);
    }

    public DesignDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("The design file is empty.");
        DesignDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<DesignDocument>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The file is not a valid design document (invalid JSON).", exception);
        }
        if (document is null)
            throw new InvalidDataException("The design file does not contain a design document.");
        if (!string.Equals(document.Schema, DesignDocument.CurrentSchema, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported design schema '{document.Schema}'. This build opens '{DesignDocument.CurrentSchema}'.");
        if (string.IsNullOrWhiteSpace(document.Module))
            throw new InvalidDataException("The design document does not identify its module.");
        ValidateSnapshot(document.SnapshotJson);
        return document;
    }

    public void Save(DesignDocument document, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A file path is required.", nameof(fileName));
        File.WriteAllText(fileName, Serialize(document), new UTF8Encoding(false));
    }

    public DesignDocument Load(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A file path is required.", nameof(fileName));
        return Deserialize(File.ReadAllText(fileName));
    }

    /// <summary>Indented rendering used by the read-only design viewer.</summary>
    public static string Pretty(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static void ValidateSnapshot(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
            throw new InvalidDataException("The design document has no module snapshot.");
        try
        {
            using var _ = JsonDocument.Parse(snapshot);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The module snapshot is not valid JSON.", exception);
        }
    }
}
