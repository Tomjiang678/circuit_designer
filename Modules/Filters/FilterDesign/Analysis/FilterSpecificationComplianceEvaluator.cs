using CircuitModuleDesigner.Core.Analysis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;

public sealed record FilterEdgeMeasurement(
    string Label,
    double FrequencyHz,
    double ActualMagnitudeDb,
    double LimitMagnitudeDb,
    bool IsPassband,
    bool MeetsLimit);

public sealed record FilterSpecificationCompliance(
    IReadOnlyList<FilterEdgeMeasurement> Edges,
    bool MeetsAllEdges,
    IReadOnlyList<string> Warnings);

/// <summary>Checks the final CircuitGraph AC response, not the ideal prototype, against the entered edge limits.</summary>
public sealed class FilterSpecificationComplianceEvaluator
{
    private const double NumericalToleranceDb = .1;

    public FilterSpecificationCompliance Evaluate(FilterDesignRequest request, AcAnalysisResult analysis)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(analysis);
        var topologyBasedRc = request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc;
        var topologyBasedBandRc = topologyBasedRc && request.ResponseType is FilterResponseType.BandPass or FilterResponseType.BandStop;
        var referenceDb = request.Mode == FilterMode.Active
            ? 20 * Math.Log10(request.PassbandGain)
            : topologyBasedRc ? PassbandReferenceDb(request, analysis) : 0;
        var passbandLimit = referenceDb - (request.ResponseType == FilterResponseType.BandPass && topologyBasedBandRc ? 3.2 : request.PassbandRippleDb);
        var stopbandLimit = referenceDb - request.StopbandAttenuationDb;
        var definitions = GetEdges(request);
        var measurements = definitions.Select(edge =>
        {
            var point = analysis.Points.MinBy(value => Math.Abs(Math.Log(value.FrequencyHz / edge.FrequencyHz)))
                ?? throw new InvalidOperationException("AC analysis contains no frequency points.");
            var meets = edge.IsPassband
                ? point.MagnitudeDb >= passbandLimit - NumericalToleranceDb
                : point.MagnitudeDb <= stopbandLimit + NumericalToleranceDb;
            return new FilterEdgeMeasurement(edge.Label, edge.FrequencyHz, point.MagnitudeDb,
                edge.IsPassband ? passbandLimit : stopbandLimit, edge.IsPassband, meets);
        }).ToArray();
        var warnings = measurements.Where(value => !value.MeetsLimit).Select(value =>
            $"Final circuit misses {value.Label} at {value.FrequencyHz:G6} Hz: {value.ActualMagnitudeDb:0.###} dB; " +
            (value.IsPassband ? $"minimum allowed is {value.LimitMagnitudeDb:0.###} dB." : $"maximum allowed is {value.LimitMagnitudeDb:0.###} dB.")).ToArray();
        return new FilterSpecificationCompliance(measurements, warnings.Length == 0, warnings);
    }

    private static double PassbandReferenceDb(FilterDesignRequest request, AcAnalysisResult analysis)
    {
        var passband = analysis.Points.Where(point => request.ResponseType switch
        {
            FilterResponseType.LowPass => point.FrequencyHz <= request.PassbandEdgeHz,
            FilterResponseType.HighPass => point.FrequencyHz >= request.PassbandEdgeHz,
            FilterResponseType.BandPass => point.FrequencyHz >= request.PassbandLowerHz && point.FrequencyHz <= request.PassbandUpperHz,
            FilterResponseType.BandStop => point.FrequencyHz <= request.PassbandLowerHz || point.FrequencyHz >= request.PassbandUpperHz,
            _ => false
        }).ToArray();
        return (passband.Length == 0 ? analysis.Points : passband).Max(point => point.MagnitudeDb);
    }

    private static IReadOnlyList<(string Label, double FrequencyHz, bool IsPassband)> GetEdges(FilterDesignRequest request) =>
        request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc && request.ResponseType == FilterResponseType.BandStop
            ?
            [
                ("fp1", request.PassbandLowerHz, true),
                ("notch", Math.Sqrt(request.StopbandLowerHz * request.StopbandUpperHz), false),
                ("fp2", request.PassbandUpperHz, true)
            ]
            :
        request.ResponseType switch
        {
            FilterResponseType.LowPass or FilterResponseType.HighPass =>
            [
                ("fp", request.PassbandEdgeHz, true),
                ("fs", request.StopbandEdgeHz, false)
            ],
            FilterResponseType.BandPass =>
            [
                ("fs1", request.StopbandLowerHz, false),
                ("fp1", request.PassbandLowerHz, true),
                ("fp2", request.PassbandUpperHz, true),
                ("fs2", request.StopbandUpperHz, false)
            ],
            FilterResponseType.BandStop =>
            [
                ("fp1", request.PassbandLowerHz, true),
                ("fs1", request.StopbandLowerHz, false),
                ("fs2", request.StopbandUpperHz, false),
                ("fp2", request.PassbandUpperHz, true)
            ],
            _ => throw new NotSupportedException($"No edge definition exists for {request.ResponseType}.")
        };
}
