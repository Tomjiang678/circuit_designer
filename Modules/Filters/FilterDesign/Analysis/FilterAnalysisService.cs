using CircuitModuleDesigner.Core.Analysis;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;

public sealed class FilterAnalysisService
{
    private readonly ICircuitAcAnalysisService analyzer;
    public FilterAnalysisService(ICircuitAcAnalysisService? analyzer = null) => this.analyzer = analyzer ?? new CircuitAcAnalysisService();

    public AcAnalysisOptions CreateOptions(FilterDesignRequest request)
    {
        var frequencies = request.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass
            ? new[] { request.PassbandEdgeHz, request.StopbandEdgeHz }
            : new[] { request.StopbandLowerHz, request.PassbandLowerHz, request.PassbandUpperHz, request.StopbandUpperHz };
        var valid = frequencies.Where(value => value > 0 && double.IsFinite(value)).ToArray();
        if (valid.Length == 0) throw new InvalidOperationException("No valid frequencies are available for AC analysis.");
        var additional = valid.ToList();
        if (request.ResponseType is FilterResponseType.BandPass or FilterResponseType.BandStop)
            additional.Add(Math.Sqrt(request.PassbandLowerHz * request.PassbandUpperHz));
        return new AcAnalysisOptions(Math.Max(1e-3, valid.Min() / 10), valid.Max() * 10, 40, AdditionalFrequenciesHz: additional);
    }

    public AcAnalysisResult Analyze(CircuitGraph graph, FilterDesignRequest request)
    {
        var options = CreateOptions(request);
        return request.Mode == FilterMode.Passive
            ? new PassiveFilterAnalysisService(analyzer).Analyze(graph, request, options).Primary
            : analyzer.Analyze(graph, options);
    }

    public PassiveFilterAnalysisResult AnalyzePassive(CircuitGraph graph, FilterDesignRequest request) =>
        new PassiveFilterAnalysisService(analyzer).Analyze(graph, request, CreateOptions(request));
}
