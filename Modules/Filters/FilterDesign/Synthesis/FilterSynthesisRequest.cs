using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public sealed class FilterSynthesisRequest
{
    public FilterDesignResult DesignResult { get; }
    public FilterImplementationType Implementation { get; }
    public FilterSignalArchitecture SignalArchitecture { get; }

    public FilterSynthesisRequest(
        FilterDesignResult designResult,
        FilterImplementationType implementation,
        FilterSignalArchitecture signalArchitecture)
    {
        DesignResult = designResult ?? throw new ArgumentNullException(nameof(designResult));
        Implementation = implementation;
        SignalArchitecture = signalArchitecture;
    }
}
