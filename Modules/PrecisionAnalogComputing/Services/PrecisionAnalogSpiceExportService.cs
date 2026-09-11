using System.Text;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Circuit.Spice;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Services;

/// <summary>Adds Precision Analog stimuli/model metadata; all CircuitGraph component serialization is shared.</summary>
public sealed class PrecisionAnalogSpiceExportService
{
    private readonly ICircuitSpiceExporter exporter;
    public PrecisionAnalogSpiceExportService(ICircuitSpiceExporter? exporter = null) => this.exporter = exporter ?? new LtspiceNetlistExporter();

    public string Export(AnalogComputeDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Success) throw new InvalidOperationException("Only a successful verified design can be exported.");
        if (result.Request.Operation is AnalogComputeOperationType.PrecisionHalfWaveRectifier or AnalogComputeOperationType.PrecisionFullWaveRectifier)
            throw new NotSupportedException("SPICE export is unavailable for precision rectifiers because v1 verification uses ideal-switch diode states; exporting a Shockley model would not reproduce the verified transfer.");
        var graph = result.CircuitGraph ?? throw new InvalidOperationException("The design result has no CircuitGraph.");
        var request = result.Request;
        var graphNets = graph.Nets.Select(net => net.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = new List<SpiceIndependentSource>
        {
            new("V_VCC", SpiceSourceKind.Voltage, "VCC", "0", request.SupplyVccVoltage),
            new("V_VEE", SpiceSourceKind.Voltage, "VEE", "0", request.SupplyVeeVoltage)
        };

        var inputSourceNames = new List<string>();
        for (var index = 0; index < request.Inputs.Count; index++)
        {
            var input = request.Inputs[index];
            var node = graphNets.Contains(input.Name) ? input.Name : index == 0 && graphNets.Contains("VIN") ? "VIN" : input.Name;
            var sourceName = "V_" + input.Name;
            inputSourceNames.Add(sourceName);
            var ac = IsDynamic(request.Operation) && index == 0 ? 1d : (double?)null;
            sources.Add(new(sourceName, SpiceSourceKind.Voltage, node, "0", input.NominalVoltage, ac));
        }

        var analyses = new List<SpiceAnalysisDirective>();
        if (IsDynamic(request.Operation))
        {
            var parameters = DynamicOperationParameters.Resolve(request);
            analyses.Add(new SpiceAcAnalysis(SpiceSweepScale.Decade, 60,
                parameters.SweepStartFrequencyHz, parameters.SweepStopFrequencyHz));
        }
        else
        {
            var (start, stop) = SweepRange(request);
            analyses.Add(new SpiceDcAnalysis(inputSourceNames[0], start, stop, (stop - start) / 100));
        }

        var hasDiodes = graph.Components.Any(component => component.Type == ComponentType.Diode);
        var options = new CircuitSpiceExportOptions($"Circuit Module Designer - Precision Analog {request.Operation}")
        {
            Dialect = SpiceNetlistDialect.Ltspice,
            Sources = sources,
            Analyses = analyses,
            OutputExpressions = [IsDynamic(request.Operation) ? "VM(VOUT) VP(VOUT)" : "V(VOUT)"],
            HighImpedanceOutputLoadOhm = 1e9,
            OpAmpOpenLoopGain = 1e9,
            DiodeModel = hasDiodes ? new SpiceDiodeModelDefinition("CMD_DIODE", request.Junction.SaturationCurrentAmpere, request.Junction.EmissionCoefficient) : null,
            TemperatureCelsius = hasDiodes ? request.Junction.TemperatureCelsius : null,
            AdditionalComments = []
        };
        return exporter.Export(graph, options);
    }

    public void Save(AnalogComputeDesignResult result, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A .cir or .net file path is required.", nameof(fileName));
        File.WriteAllText(fileName, Export(result), new UTF8Encoding(false));
    }

    private static bool IsDynamic(AnalogComputeOperationType operation) => operation is AnalogComputeOperationType.PracticalIntegrator or AnalogComputeOperationType.PracticalDifferentiator;

    private static (double Start, double Stop) SweepRange(AnalogComputeDesignRequest request)
    {
        if (request.Operation == AnalogComputeOperationType.VoltageRangeMapper) return (request.InputLowVoltage, request.InputHighVoltage);
        if (request.Operation == AnalogComputeOperationType.GeneralLinearCombination && request.Inputs.Count > 0)
            return (request.Inputs[0].MinimumVoltage, request.Inputs[0].MaximumVoltage);
        return (request.MinimumInputVoltage, request.MaximumInputVoltage);
    }
}
