using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Validation;

internal static class OpAmpFeasibilityEvaluator
{
    public static void AddMetrics(List<VerificationMetric> metrics, AnalogComputeDesignRequest request,
        double requiredGbwHz, double requiredSlewVoltPerSecond)
    {
        if (request.OpAmp.UseIdealOpAmp) return;
        metrics.Add(new VerificationMetric("Required GBW", requiredGbwHz, request.OpAmp.GainBandwidthHz,
            request.OpAmp.GainBandwidthHz - requiredGbwHz, 0,
            request.OpAmp.GainBandwidthHz >= requiredGbwHz, "Hz"));
        metrics.Add(new VerificationMetric("Required slew rate", requiredSlewVoltPerSecond, request.OpAmp.SlewRateVoltPerSecond,
            request.OpAmp.SlewRateVoltPerSecond - requiredSlewVoltPerSecond, 0,
            request.OpAmp.SlewRateVoltPerSecond >= requiredSlewVoltPerSecond, "V/s"));
    }

    public static double RequiredGbwForResistiveStages(CircuitGraph graph, double maximumFrequencyHz,
        double designMargin = 10)
    {
        var maximumNoiseGain = graph.Components
            .Where(component => component.Type == ComponentType.OpAmp)
            .Select(opAmp => ResistiveNoiseGain(graph, opAmp))
            .DefaultIfEmpty(1)
            .Max();
        return maximumFrequencyHz * maximumNoiseGain * designMargin;
    }

    public static double RequiredGbw(double maximumFrequencyHz, double maximumNoiseGain,
        double designMargin = 10) => maximumFrequencyHz * maximumNoiseGain * designMargin;

    public static double RequiredSlew(double maximumFrequencyHz, double maximumStagePeakVoltage) =>
        2 * Math.PI * maximumFrequencyHz * maximumStagePeakVoltage;

    private static double ResistiveNoiseGain(CircuitGraph graph, CircuitComponent opAmp)
    {
        var minus = graph.GetNetForPin(opAmp.GetPin("IN-"))?.Name ??
                    throw new InvalidOperationException($"{opAmp.Reference}.IN- is not connected.");
        var output = graph.GetNetForPin(opAmp.GetPin("OUT"))?.Name ??
                     throw new InvalidOperationException($"{opAmp.Reference}.OUT is not connected.");
        var attached = graph.Components.Where(component => component.Type == ComponentType.Resistor)
            .Select(component => (Component: component, Nets: component.Pins
                .Select(pin => graph.GetNetForPin(pin)?.Name).Where(name => name is not null).Cast<string>().ToArray()))
            .Where(item => item.Nets.Contains(minus, StringComparer.OrdinalIgnoreCase)).ToArray();
        var feedback = attached.SingleOrDefault(item => item.Nets.Contains(output, StringComparer.OrdinalIgnoreCase));
        if (feedback.Component is null)
            throw new InvalidOperationException($"No resistive feedback path was found for {opAmp.Reference}.");
        var rf = feedback.Component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm);
        var inputConductance = attached.Where(item => !ReferenceEquals(item.Component, feedback.Component))
            .Sum(item => 1d / item.Component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm));
        return 1 + rf * inputConductance;
    }
}
