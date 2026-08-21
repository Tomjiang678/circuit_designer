using CircuitModuleDesigner.Core.Circuit;

namespace CircuitModuleDesigner.Core.Analysis;

public interface ICircuitAcAnalysisService
{
    AcAnalysisResult Analyze(CircuitGraph graph, AcAnalysisOptions options, double? sourceResistanceOhm = null, double? loadResistanceOhm = null);
}
