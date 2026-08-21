namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;

public sealed record FilterDesignAvailability(bool CanDesign, string Message)
{
    public static FilterDesignAvailability Available(string message) => new(true, message);
    public static FilterDesignAvailability Unavailable(string message) => new(false, message);
}
