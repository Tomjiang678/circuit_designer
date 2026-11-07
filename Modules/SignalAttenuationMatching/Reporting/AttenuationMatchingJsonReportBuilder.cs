using System.Text;
using System.Text.Json;
using CircuitModuleDesigner.Core.Reporting;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Services;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Reporting;

public sealed class AttenuationMatchingJsonReportBuilder
{
    private readonly AttenuationMatchingSpiceExportService spice = new();

    public string Build(AttenuationMatchingDesignResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var selected = result.SelectedCandidate;
        var snapshot = new
        {
            Schema = "CircuitModuleDesigner.SignalAttenuationMatching.v1",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            result.Request.Mode,
            DesignStatus = result.Status.ToString(),
            result.Message,
            Request = new
            {
                Source = Port(result.Request.Source),
                Load = Port(result.Request.Load),
                result.Request.DesignFrequencyHz,
                result.Request.StartFrequencyHz,
                result.Request.StopFrequencyHz,
                result.Request.SweepPoints,
                result.Request.LogarithmicSweep,
                Signal = new { Value = result.Request.Signal.Value, Unit = result.Request.Signal.Unit.ToString(), Description = result.Request.Signal.Description },
                Attenuation = result.Request.Attenuation,
                Matching = result.Request.Matching,
                MatchingPlan = result.Request.MatchingPlan,
                Components = result.Request.Components,
                Topology = result.Request.Topology,
                result.Request.Objective
            },
            Candidates = result.Candidates.Select(candidate => new
            {
                candidate.Rank,
                candidate.Id,
                candidate.TopologyName,
                Family = candidate.Family.ToString(),
                Response = candidate.Response.ToString(),
                Status = candidate.Status.ToString(),
                candidate.StatusMessage,
                ComponentCount = candidate.Components.Count,
                Metrics = Metrics(candidate.Metrics),
                Sweep = new { candidate.Sweep.LowerBandEdgeHz, candidate.Sweep.UpperBandEdgeHz, candidate.Sweep.BandwidthHz, candidate.Sweep.LoadedQ, candidate.Sweep.BandwidthLimitedBySweep, candidate.Sweep.Warnings, candidate.Sweep.BandMetrics },
                candidate.Score
            }).ToArray(),
            Selected = selected is null ? null : new
            {
                selected.Id,
                selected.TopologyName,
                selected.TopologyDescription,
                selected.LimitNote,
                Family = selected.Family.ToString(),
                Response = selected.Response.ToString(),
                Status = selected.Status.ToString(),
                selected.StatusMessage,
                Metrics = Metrics(selected.Metrics),
                Components = selected.Components.Select(component => new
                {
                    component.Reference,
                    Type = component.Type.ToString(),
                    component.IdealValue,
                    component.SelectedValue,
                    component.Unit,
                    component.ErrorPercent,
                    component.DissipationWatt,
                    component.Rating
                }).ToArray(),
                Diagnostics = selected.Diagnostics,
                Sweep = Sweep(selected.Sweep),
                DesignNetlist = spice.ExportDesign(selected, result.Request),
                TestbenchAvailable = AttenuationMatchingSpiceExportService.SupportsTestbench(result.Request),
                TestbenchUnavailableReason = AttenuationMatchingSpiceExportService.SupportsTestbench(result.Request) ? null : AttenuationMatchingSpiceExportService.VnaTestbenchUnavailable,
                TestbenchNetlist = AttenuationMatchingSpiceExportService.SupportsTestbench(result.Request) ? spice.ExportTestbench(selected, result.Request) : null
            },
            result.Diagnostics,
            Assumptions = new[]
            {
                "Only ideal lumped R/L/C elements are modeled.",
                "Series R-L/R-C port reactances are frequency-dependent. Full VNA port tables are interpolated across their measured range and do not have an automatic lumped SPICE testbench equivalent.",
                "Source and load are external test conditions, not part of the design CircuitGraph.",
                "Matching bandwidth is derived from the finite frequency sweep and the stated return-loss/insertion-loss gates.",
                "No transmission lines, PCB parasitics, vendor component models or electromagnetic simulation are included."
            }
        };
        return JsonSerializer.Serialize(snapshot, ReportJson.CamelCase);
    }

    private static object Port(PortImpedanceModel port) => new
    {
        port.Name,
        ResistanceOhm = port.ResistanceOhm,
        ReactanceOhm = port.ReactanceOhm,
        Type = port.Type.ToString(),
        port.ReferenceFrequencyHz,
        port.EquivalentInductanceHenry,
        port.EquivalentCapacitanceFarad,
        VnaData = port.VnaData,
        Model = port.ModelDescription
    };

    private static object Metrics(NetworkMetrics metrics) => new
    {
        metrics.FrequencyHz,
        SourceImpedance = ComplexValue(metrics.SourceImpedance),
        LoadImpedance = ComplexValue(metrics.LoadImpedance),
        InputImpedance = ComplexValue(metrics.InputImpedance),
        OutputImpedance = ComplexValue(metrics.OutputImpedance),
        InputReflectionCoefficient = ComplexValue(metrics.InputReflectionCoefficient),
        OutputReflectionCoefficient = ComplexValue(metrics.OutputReflectionCoefficient),
        metrics.TransducerGain,
        metrics.TransducerGainDb,
        metrics.AttenuationDb,
        metrics.InsertionLossDb,
        metrics.RelativeInsertionLossDb,
        metrics.MatchedPortAttenuationDb,
        metrics.S11Db,
        metrics.S21Db,
        metrics.S22Db,
        metrics.InputReturnLossDb,
        metrics.OutputReturnLossDb,
        metrics.VswrIn,
        metrics.VswrOut,
        metrics.BandwidthHz,
        metrics.LowerBandEdgeHz,
        metrics.UpperBandEdgeHz,
        metrics.LoadedQ,
        metrics.MaximumResistorDissipationWatt,
        metrics.TotalResistorDissipationWatt,
        metrics.ComponentCount,
        metrics.IsPassive,
        metrics.Warnings
    };

    private static object Sweep(AcSweepResult sweep) => new
    {
        sweep.LowerBandEdgeHz,
        sweep.UpperBandEdgeHz,
        sweep.BandwidthHz,
        sweep.LoadedQ,
        sweep.BandwidthLimitedBySweep,
        sweep.Warnings,
        sweep.BandMetrics,
        Points = sweep.Points.Select(point => new
        {
            point.FrequencyHz,
            point.TransducerGain,
            point.TransducerGainDb,
            point.AttenuationDb,
            point.InputReturnLossDb,
            point.OutputReturnLossDb,
            InputImpedance = ComplexValue(point.InputImpedance),
            OutputImpedance = ComplexValue(point.OutputImpedance),
            InputReflectionCoefficient = ComplexValue(point.InputReflectionCoefficient),
            point.VswrIn,
            point.VswrOut
        }).ToArray()
    };

    private static object ComplexValue(System.Numerics.Complex value) => new { Real = value.Real, Imaginary = value.Imaginary, Magnitude = value.Magnitude, PhaseDegrees = value.Phase * 180 / Math.PI };
}
