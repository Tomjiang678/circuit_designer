using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

public sealed class FilterCircuitSynthesizer : IFilterCircuitSynthesizer
{
    private readonly IReadOnlyDictionary<FilterImplementationType, IFilterTopologyBuilder> builders;

    public FilterCircuitSynthesizer(IEnumerable<IFilterTopologyBuilder> builders)
    {
        ArgumentNullException.ThrowIfNull(builders);
        var map = new Dictionary<FilterImplementationType, IFilterTopologyBuilder>();
        foreach (var builder in builders)
        {
            if (!map.TryAdd(builder.ImplementationType, builder))
                throw new ArgumentException($"A topology builder for '{builder.ImplementationType}' is already registered.", nameof(builders));
        }
        this.builders = map;
    }

    public CircuitGraph Synthesize(FilterSynthesisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.DesignResult.IsImplemented)
            throw new InvalidOperationException("A circuit cannot be synthesized before the filter design result is implemented.");
        if (!builders.TryGetValue(request.Implementation, out var builder))
            throw new NotSupportedException($"No topology builder is registered for '{request.Implementation}'.");
        return builder.Build(request);
    }
}
