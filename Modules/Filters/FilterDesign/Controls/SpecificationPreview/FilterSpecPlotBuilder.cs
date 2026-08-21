using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls.SpecificationPreview;

public static class FilterSpecPlotBuilder
{
    public static FilterSpecPlotModel Build(FilterDesignState state)
    {
        var errors = FilterSpecificationValidator.Validate(state);
        if (errors.Count > 0) return new() { IsValid = false, ResponseType = state.ResponseType, Approximation = state.Approximation, Message = "Enter valid filter specifications to preview.\r\n" + errors[0] };
        var edges = GetEdges(state);
        var min = Math.Max(0.1, edges.Min() / 3);
        var max = edges.Max() * 3;
        var points = new List<PlotPoint>();
        for (var i = 0; i < 120; i++)
        {
            var f = min * Math.Pow(max / min, i / 119.0);
            points.Add(new PlotPoint(f, Shape(state, f)));
        }
        var markers = BuildMarkers(state);
        return new FilterSpecPlotModel { IsValid = true, ResponseType = state.ResponseType, Approximation = state.Approximation, MinimumFrequencyHz = min, MaximumFrequencyHz = max, Response = points, Markers = markers, Bands = BuildBands(state) };
    }

    private static double[] GetEdges(FilterDesignState s)
    {
        if (s.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass) return new[] { s.PassbandEdgeHz, s.StopbandEdgeHz };
        if (s.BandInputMode == BandFrequencyInputMode.Edges)
        {
            return s.ResponseType == FilterResponseType.BandPass
                ? new[] { s.StopbandLowerHz, s.PassbandLowerHz, s.PassbandUpperHz, s.StopbandUpperHz }
                : new[] { s.PassbandLowerHz, s.StopbandLowerHz, s.StopbandUpperHz, s.PassbandUpperHz };
        }
        return s.ResponseType == FilterResponseType.BandPass
            ? new[] { s.CenterFrequencyHz - s.StopbandBandwidthHz / 2, s.CenterFrequencyHz - s.PassbandBandwidthHz / 2, s.CenterFrequencyHz + s.PassbandBandwidthHz / 2, s.CenterFrequencyHz + s.StopbandBandwidthHz / 2 }
            : new[] { s.CenterFrequencyHz - s.PassbandBandwidthHz / 2, s.CenterFrequencyHz - s.StopbandBandwidthHz / 2, s.CenterFrequencyHz + s.StopbandBandwidthHz / 2, s.CenterFrequencyHz + s.PassbandBandwidthHz / 2 };
    }

    private static double Shape(FilterDesignState s, double f)
    {
        var pass = s.Mode == FilterMode.Active ? 20 * Math.Log10(s.PassbandGain) : 0;
        var r = s.Approximation == FilterApproximation.ChebyshevTypeI ? Math.Sin(Math.Log10(Math.Max(f, 1)) * 22) * s.PassbandRippleDb / 3 : 0;
        var e = GetEdges(s);
        double db;
        switch (s.ResponseType)
        {
            case FilterResponseType.LowPass: db = f <= e[0] ? pass + r : pass - s.StopbandAttenuationDb * Math.Clamp(Math.Log10(f / e[0]) / Math.Log10(e[1] / e[0]), 0, 1); break;
            case FilterResponseType.HighPass: db = f >= e[0] ? pass + r : pass - s.StopbandAttenuationDb * Math.Clamp(Math.Log10(e[0] / f) / Math.Log10(e[0] / e[1]), 0, 1); break;
            case FilterResponseType.BandPass: db = f >= e[1] && f <= e[2] ? pass + r : pass - s.StopbandAttenuationDb * Math.Clamp(f < e[1] ? Math.Log10(e[1] / f) / Math.Log10(e[1] / e[0]) : Math.Log10(f / e[2]) / Math.Log10(e[3] / e[2]), 0, 1); break;
            default: db = f <= e[0] || f >= e[3] ? pass + r : pass - s.StopbandAttenuationDb * Math.Clamp(f < e[1] ? Math.Log10(f / e[0]) / Math.Log10(e[1] / e[0]) : Math.Log10(e[3] / f) / Math.Log10(e[3] / e[2]), 0, 1); break;
        }
        return Math.Clamp(db, -60, 10);
    }

    private static IReadOnlyList<PlotMarker> BuildMarkers(FilterDesignState s)
    {
        var p = s.Mode == FilterMode.Active ? 20 * Math.Log10(s.PassbandGain) : 0;
        var e = GetEdges(s);
        var list = new List<PlotMarker>();
        var passReference = s.ResponseType switch
        {
            FilterResponseType.BandPass => e[1],
            FilterResponseType.BandStop => e[0],
            _ => e[0]
        };
        if (s.Mode == FilterMode.Active) list.Add(new(passReference, p, $"A0 = {s.PassbandGain:0.###} V/V"));
        list.Add(new(passReference, p - s.PassbandRippleDb, $"Rp = {s.PassbandRippleDb:0.##} dB"));
        switch (s.ResponseType)
        {
            case FilterResponseType.LowPass:
            case FilterResponseType.HighPass:
                list.Add(new(e[0], p, $"fp = {FormatFrequency(e[0])}"));
                list.Add(new(e[1], -s.StopbandAttenuationDb, $"fs = {FormatFrequency(e[1])}, As = {s.StopbandAttenuationDb:0.##} dB"));
                break;
            case FilterResponseType.BandPass:
                list.Add(new(e[0], -s.StopbandAttenuationDb, $"fs1 = {FormatFrequency(e[0])}"));
                list.Add(new(e[1], p, $"fp1 = {FormatFrequency(e[1])}"));
                list.Add(new(e[2], p, $"fp2 = {FormatFrequency(e[2])}"));
                list.Add(new(e[3], -s.StopbandAttenuationDb, $"fs2 = {FormatFrequency(e[3])}"));
                break;
            default:
                list.Add(new(e[0], p, $"fp1 = {FormatFrequency(e[0])}"));
                list.Add(new(e[1], -s.StopbandAttenuationDb, $"fs1 = {FormatFrequency(e[1])}"));
                list.Add(new(e[2], -s.StopbandAttenuationDb, $"fs2 = {FormatFrequency(e[2])}"));
                list.Add(new(e[3], p, $"fp2 = {FormatFrequency(e[3])}"));
                break;
        }
        return list;
    }

    private static IReadOnlyList<PlotBand> BuildBands(FilterDesignState s)
    {
        var e = GetEdges(s);
        return s.ResponseType switch { FilterResponseType.LowPass => new[] { new PlotBand(e[0], e[1], $"As = {s.StopbandAttenuationDb:0.##} dB") }, FilterResponseType.HighPass => new[] { new PlotBand(e[1], e[0], $"As = {s.StopbandAttenuationDb:0.##} dB") }, FilterResponseType.BandPass => new[] { new PlotBand(e[0], e[3], $"BWs = {FormatFrequency(e[3] - e[0])}"), new PlotBand(e[1], e[2], $"BWp = {FormatFrequency(e[2] - e[1])}") }, _ => new[] { new PlotBand(e[1], e[2], $"BWs = {FormatFrequency(e[2] - e[1])}, As = {s.StopbandAttenuationDb:0.##} dB") } };
    }

    private static string FormatFrequency(double frequency) => frequency switch
    {
        >= 1e6 => $"{frequency / 1e6:0.###} MHz",
        >= 1e3 => $"{frequency / 1e3:0.###} kHz",
        _ => $"{frequency:0.###} Hz"
    };
}
