using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public interface IFilterCircuitSynthesizer
{
    CircuitGraph Synthesize(FilterSynthesisRequest request);
}
