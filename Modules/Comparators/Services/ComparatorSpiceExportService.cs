using CircuitModuleDesigner.Core.Circuit.Spice;
using CircuitModuleDesigner.Modules.Comparators.Models;
using System.Text;

namespace CircuitModuleDesigner.Modules.Comparators.Services;

public sealed class ComparatorSpiceExportService
{
    private readonly ICircuitSpiceExporter exporter;
    public ComparatorSpiceExportService(ICircuitSpiceExporter? exporter = null) => this.exporter = exporter ?? new LtspiceNetlistExporter();

    public string Export(ComparatorDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Success || result.CircuitGraph is null) throw new InvalidOperationException("A successful comparator design is required for SPICE export.");
        var r = result.Request; var oscillator=r.Kind==ComparatorDesignerKind.RelaxationOscillator;
        var sources = new List<SpiceIndependentSource>{new("V_CC", SpiceSourceKind.Voltage, "VCC", "0", r.SupplyHighVoltage)};
        if(!oscillator)sources.Insert(0,new("V_INPUT", SpiceSourceKind.Voltage, "VIN", "0", r.SupplyLowVoltage));
        if (r.SupplyLowVoltage != 0) sources.Add(new("V_EE", SpiceSourceKind.Voltage, "VEE", "0", r.SupplyLowVoltage));
        CircuitSpiceExportOptions options;
        if(oscillator)
        {
            var frequency=result.Oscillator?.ActualFrequencyHz ?? double.NaN;
            if(!double.IsFinite(frequency)||frequency<=0)
                throw new InvalidOperationException("The relaxation-oscillator result has no positive finite oscillation frequency; SPICE transient export cannot choose a time step.");
            var period=1/frequency;
            options=new CircuitSpiceExportOptions("Comparator relaxation oscillator")
            {
                Sources=sources,Analyses=[new SpiceTransientAnalysis(period/250,period*5,0,period/100)],
                OutputExpressions=["V(VOUT)","V(NCAP)"],HighImpedanceOutputLoadOhm=1e9,
                AdditionalComments=[$"Ideal relaxation oscillator; calculated f={frequency:G9} Hz, duty={result.Oscillator!.ActualDutyCyclePercent:G7}%.",
                    "Behavioral comparator export intentionally makes no propagation-delay, offset, bias-current, capacitor-tolerance, or noise claim."]
            };
        }
        else
        {
            var start = r.SupplyLowVoltage; var stop = r.SupplyHighVoltage; var step = Math.Max((stop - start) / 500, 1e-6);
            options=new CircuitSpiceExportOptions("Comparator threshold design")
            {
                Sources = sources, Analyses = [new SpiceDcAnalysis("V_INPUT", start, stop, step)], OutputExpressions = ["V(VOUT)"], HighImpedanceOutputLoadOhm = 1e9,
                AdditionalComments = [$"Static GenericComparatorModel; VOH={r.OutputHighVoltage:G9} V, VOL={r.OutputLowVoltage:G9} V.", "Behavioral comparator export intentionally makes no propagation-delay, offset, bias-current, or noise claim."]
            };
        }
        return exporter.Export(result.CircuitGraph, options);
    }

    public void Save(ComparatorDesignResult result, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("An export file path is required.", nameof(fileName));
        File.WriteAllText(fileName, Export(result), new UTF8Encoding(false));
    }
}
