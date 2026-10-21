using System.Text;
using System.Text.Json;
using CircuitModuleDesigner.Models;

namespace CircuitModuleDesigner.Services;

/// <summary>Persists <see cref="AppSettings"/> under the user's local application data folder.</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AppSettingsStore(string? fileName = null)
    {
        FileName = fileName ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CircuitModuleDesigner",
            "settings.json");
    }

    public string FileName { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FileName)) return new AppSettings();
            var json = File.ReadAllText(FileName);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var directory = Path.GetDirectoryName(FileName);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(FileName, JsonSerializer.Serialize(settings, Options), new UTF8Encoding(false));
    }
}
