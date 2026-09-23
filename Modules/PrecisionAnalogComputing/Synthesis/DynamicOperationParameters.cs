using System.Numerics;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

internal sealed record DynamicOperationParameters(
    double FirstCornerHz,
    double SecondCornerHz,
    double UsefulUpperFrequencyHz,
    double SweepStartFrequencyHz,
    double SweepStopFrequencyHz)
{
    public static Complex DifferentiatorTransfer(double frequency, double kd, double firstCorner, double secondCorner)
    {
        var s = new Complex(0, 2 * Math.PI * frequency);
        return -kd * s / ((Complex.One + s / (2*Math.PI*firstCorner)) *
                          (Complex.One + s / (2*Math.PI*secondCorner)));
    }

    public static Complex IntegratorTransfer(double frequency,double ki,double leakCorner) =>
        -ki/new Complex(2*Math.PI*leakCorner,2*Math.PI*frequency);

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
