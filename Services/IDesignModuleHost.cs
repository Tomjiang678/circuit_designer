namespace CircuitModuleDesigner.Services;

/// <summary>
/// Minimal surface the application shell needs from a design module. Keeping it
/// this small lets MainForm stay free of module-specific types while every
/// wizard can still provide a saveable design, a netlist and a Calculate action.
/// </summary>
public interface IDesignModuleHost
{
    /// <summary>Human-readable module name shown in the header, status bar and saved documents.</summary>
    string ModuleTitle { get; }

    /// <summary>True when the module currently holds a generated, verified design.</summary>
    bool HasDesign { get; }

    /// <summary>Module-specific JSON snapshot of the current design. Empty when there is no design.</summary>
    string BuildDesignJson();

    /// <summary>Human-readable summary of the current design. Empty when there is no design.</summary>
    string BuildDesignSummary();

    /// <summary>SPICE netlist when the module can produce one for the current design; otherwise null.</summary>
    string? BuildNetlist();

    /// <summary>Runs the module's primary design/calculate action.</summary>
    void Calculate();

    /// <summary>Unsubscribes the module from the shared navigation tree.</summary>
    void DetachNavigation();
}
