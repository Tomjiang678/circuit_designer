using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Analysis;

/// <summary>
/// Bode-Fano pre-screen. For canonical series R-L/R-C port models the normalized
/// integral violation is a hard feasibility error. Measured VNA data has no
/// guaranteed one-pole equivalent, so its sampled local-Q result remains advisory.
/// </summary>
public static class FanoLimitEvaluator
{
    public static IReadOnlyList<AttenuationMatchingDiagnostic> Screen(AttenuationMatchingDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode != DesignMode.ImpedanceMatching) return [];

        var diagnostics = new List<AttenuationMatchingDiagnostic>();
        var bands = request.MatchingPlan.EffectiveBands(request);
        for (var bandIndex = 0; bandIndex < bands.Count; bandIndex++)
        {
            var band = bands[bandIndex];
            if (band.IsPoint) continue;
            var targetRl = Math.Max(band.TargetInputReturnLossDb, band.TargetOutputReturnLossDb ?? 0);
            var targetGamma = Math.Pow(10, -targetRl / 20);
            var logInverseGamma = Math.Log(1 / Math.Max(targetGamma, 1e-15));
            var fractionalBandwidth = (band.StopFrequencyHz - band.StartFrequencyHz) / band.CenterFrequencyHz;
            var requiredIntegral = fractionalBandwidth * logInverseGamma;
            var sampleCount = Math.Clamp(request.MatchingPlan.MinimumPointsPerBand, 9, 41);
            var frequencies = MatchingPlanEvaluator.Frequencies(band, sampleCount);

            foreach (var port in new[] { request.Source, request.Load })
            {
                var sampledQ = new List<(double Q, double Frequency)>();
                foreach (var frequency in frequencies)
                {
                    System.Numerics.Complex impedance;
                    try { impedance = port.ImpedanceAt(frequency); }
                    catch { continue; }
                    var resistance = impedance.Real;
                    var reactance = impedance.Imaginary;
                    if (!(resistance > 0) || !double.IsFinite(resistance) || !double.IsFinite(reactance) || Math.Abs(reactance) < 1e-15)
                        continue;
                    var q = Math.Abs(reactance) / resistance;
                    if (q > 0 && double.IsFinite(q)) sampledQ.Add((q, frequency));
                }
                if (sampledQ.Count == 0) continue;

                var limiting = sampledQ.MaxBy(item => item.Q);
                var bound = Math.PI / limiting.Q;
                if (requiredIntegral <= bound) continue;
                var measuredModel = port.Type == PortImpedanceType.Vna;
                var model = measuredModel ? "sampled local-equivalent" : "series R-L/R-C";
                diagnostics.Add(new AttenuationMatchingDiagnostic(
                    measuredModel ? "FANO_BANDWIDTH_SCREEN" : "FANO_BANDWIDTH_LIMIT",
                    measuredModel ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                    $"Frequency target {bandIndex + 1} exceeds the {model} Bode-Fano integral screen for {port.Name}; limiting Q={limiting.Q:G3} at {limiting.Frequency:G6} Hz.",
                    "Fano/Bode normalized integral", requiredIntegral, $"<= {bound:G6} (π/Q)",
                    "Reduce bandwidth or return-loss target, or use a different load/network architecture."));
            }
        }
        return diagnostics;
    }
}
