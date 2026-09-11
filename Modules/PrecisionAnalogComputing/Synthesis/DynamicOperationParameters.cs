using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

internal sealed record DynamicOperationParameters(
    double FirstCornerHz,
    double SecondCornerHz,
    double UsefulUpperFrequencyHz,
    double SweepStartFrequencyHz,
    double SweepStopFrequencyHz)
{
    public static DynamicOperationParameters Resolve(AnalogComputeDesignRequest request)
    {
        if (request.Operation == AnalogComputeOperationType.PracticalIntegrator)
            return new(request.LowerCornerHz, request.UpperCornerHz, request.UpperCornerHz,
                Math.Max(.001, request.LowerCornerHz / 10), request.UpperCornerHz * 10);

        if (request.Operation != AnalogComputeOperationType.PracticalDifferentiator)
            throw new ArgumentException("Dynamic parameters apply only to Integrator or Differentiator.", nameof(request));

        if (request.DifferentiationUsefulUpperFrequencyHz > 0)
        {
            var useful = request.DifferentiationUsefulUpperFrequencyHz;
            return new(useful * 5, useful * 10, useful,
                Math.Max(.001, useful / 100), useful * 100);
        }

        return new(request.LowerCornerHz, request.UpperCornerHz, request.LowerCornerHz / 5,
            Math.Max(.001, request.LowerCornerHz / 500), request.UpperCornerHz * 10);
    }
}
