namespace CircuitModuleDesigner.Models;

/// <summary>User preferences persisted between sessions.</summary>
public sealed class AppSettings
{
    public bool ConfirmOnExit { get; set; } = true;
    public bool ShowNavigationPane { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;
    public bool RememberWindowBounds { get; set; } = true;
    public bool ReopenLastCategory { get; set; } = true;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public string LastCategory { get; set; } = string.Empty;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
