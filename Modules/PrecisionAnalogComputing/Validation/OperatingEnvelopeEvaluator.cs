using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

internal static class OperatingEnvelopeEvaluator
{
    public static (double Low, double High) OutputLimits(AnalogComputeDesignRequest request) =>
        (request.SupplyVeeVoltage + request.OpAmp.OutputSwingHeadroomVoltage,
         request.SupplyVccVoltage - request.OpAmp.OutputSwingHeadroomVoltage);

    public static double Excess(double value, double low, double high) =>
        value < low ? low - value : value > high ? value - high : 0;

    public static VerificationMetric Metric(
        IEnumerable<double> targetOutputs,
        IEnumerable<double> selectedOutputs,
        double low,
        double high,
        bool selectedCircuitSaturated = false)
    {
        var targetExcess = targetOutputs.Select(value => Excess(value, low, high)).DefaultIfEmpty().Max();
        var selectedExcess = selectedOutputs.Select(value => Excess(value, low, high)).DefaultIfEmpty().Max();
        var excess = Math.Max(targetExcess, selectedExcess);
        var pass = excess <= 1e-12 && !selectedCircuitSaturated;
        if (!pass && excess <= 1e-12) excess = 1e-12;
        return new VerificationMetric(
            "Operating envelope rail excess",
            0,
            excess,
            excess,
            0,
            pass,
            "V");
    }
}
