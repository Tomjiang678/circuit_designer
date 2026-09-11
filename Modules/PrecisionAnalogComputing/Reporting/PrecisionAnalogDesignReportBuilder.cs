using System.Text;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Reporting;

public sealed class PrecisionAnalogDesignReportBuilder
{
    public string BuildText(AnalogComputeDesignResult result)
    {
        if (!result.Success) throw new InvalidOperationException("A design report can only be built from a successful verified result.");
        var text = new StringBuilder();
        text.AppendLine("PRECISION ANALOG COMPUTING DESIGN REPORT");
        text.AppendLine($"Target: {result.TargetRelation}");
        text.AppendLine($"Topology: {result.TopologyName}");
        text.AppendLine($"Supply: VCC={result.RequiredVccVoltage:G9} V, GND=0 V, VEE={result.RequiredVeeVoltage:G9} V");
        text.AppendLine("DesignStatus: Success");
        text.AppendLine($"FailureStage: {result.FailureStage}");
        text.AppendLine($"CapabilityLevel: {result.Capability.Level}");
        text.AppendLine($"CapabilityReasonCode: {result.Capability.ReasonCode}");
        text.AppendLine();
        text.AppendLine("COMPONENT VALUES");
        foreach (var component in result.Components)
            text.AppendLine($"{component.Reference,-6} {component.Role,-38} continuous={component.TheoreticalValue:G9} {component.Unit}, selected={component.SelectedValue:G9} {component.Unit}");
        text.AppendLine();
        text.AppendLine("ACTUAL VERIFICATION");
        foreach (var metric in result.VerificationMetrics)
            text.AppendLine($"{metric.Name,-28} target={metric.Target:G9} actual={metric.Actual:G9} error={metric.Error:G6} tolerance={metric.Tolerance:G6} {(metric.Pass ? "PASS" : "FAIL")}");
        text.AppendLine();
        text.AppendLine("DESIGN EQUATIONS");
        foreach(var calculation in result.Calculations)
        {
            text.AppendLine($"{calculation.Title}: {calculation.Expression} => {calculation.Result}");
            if(!string.IsNullOrWhiteSpace(calculation.Note))text.AppendLine("  Note: "+calculation.Note);
        }
        if(result.Warnings.Count>0)
        {
            text.AppendLine();
            text.AppendLine("ENGINEERING NOTES");
            foreach(var warning in result.Warnings)text.AppendLine("- "+warning);
        }
        return text.ToString();
    }
}
