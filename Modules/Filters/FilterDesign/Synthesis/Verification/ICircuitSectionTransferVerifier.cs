using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Verification;

public interface ICircuitSectionTransferVerifier
{
    SectionTransferMeasurement Verify(CircuitGraph sectionCircuit, SectionTransferTarget target,
        SectionTransferVerificationOptions? options = null);
}

public sealed record SectionTransferVerificationOptions(
    double MetricRelativeTolerance = 1e-5,
    double SampleRelativeTolerance = 2e-5,
    int PointsPerDecade = 24,
    double OpAmpOpenLoopGain = 1e7);
