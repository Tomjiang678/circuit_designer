using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public interface IFilterTopologyBuilder
{
    FilterImplementationType ImplementationType { get; }
    CircuitGraph Build(FilterSynthesisRequest request);
}
