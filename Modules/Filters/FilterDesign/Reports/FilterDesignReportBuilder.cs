using System.Globalization;
using System.Reflection;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;

public sealed class FilterDesignReportBuilder
{
    public FilterDesignReportModel Build(FilterDesignResult result, DateTimeOffset? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsImplemented || result.Verification?.IsValid != true || result.DesignRequest == null || result.CircuitGraph == null)
            throw new InvalidOperationException("A design report can only be generated from a successful FilterDesignVerifier-approved result.");
        var request = result.DesignRequest;
        var components = BuildComponents(result, request).ToArray();
        var poles = result.PrototypePoles.Select((pole, index) => new ReportPole(index + 1, pole.Real, pole.Imaginary, false))
            .Concat(result.PrototypeZeros.Select((zero, index) => new ReportPole(index + 1, zero.Real, zero.Imaginary, true))).ToArray();
        var sections = result.Sections.Select(section => new ReportSection(
            section.Target.Index, section.Target.Order.ToString(), section.Target.ResponseType.ToString(),
            section.Target.PoleFrequencyHz, section.Target.Q, section.Target.SectionGain,
            section.IntrinsicTopologyGain, section.ActualGain, section.PolaritySign < 0 ? "Inverting" : "Non-inverting",
            section.Target.NumeratorCoefficients, section.Target.DenominatorCoefficients,
            section.FrequencyError, section.QError, section.GainError)).ToArray();
        var summary = BuildSummary(result, request);
        var inputs = BuildInputs(request, result);
        var sourceLoadReference = BuildSourceLoadReference(result, request);
        var approximation = BuildApproximation(result, request);
        var gain = BuildGain(result);
        var verification = BuildVerification(result, request);
        var circuitSummary = BuildCircuitSummary(result, request);
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
        var snapshot = new FilterDesignSnapshot(
            "circuit-module-designer.filter-design/1",
            result.DesignedAtUtc,
            BuildSnapshotRequest(request), components, poles,
            new Dictionary<string, object>
            {
                ["Status"] = result.Status.ToString(), ["RequiredOrder"] = result.RequiredOrder,
                ["PrototypeOrder"] = result.PrototypeOrder, ["RealizedOrder"] = result.RealizedOrder,
                ["MeetsSpecifications"] = result.MeetsSpecifications,
                ["ActualPassbandPeakGain"] = result.GainDistribution?.ActualPassbandPeakGain ?? 0,
                ["Warnings"] = result.Warnings.ToArray()
            });
        return new(generatedAtUtc ?? DateTimeOffset.UtcNow, version, summary, inputs, sourceLoadReference, approximation,
            poles, sections, components, gain, verification, result.Warnings, circuitSummary,
            result.CalculationTrace, snapshot);
    }

    private static IReadOnlyList<ReportField> BuildSummary(FilterDesignResult result, FilterDesignRequest request)
    {
        var fields = new List<ReportField>
        {
        new("Design time (UTC)", result.DesignedAtUtc.ToString("u", CultureInfo.InvariantCulture)),
        new("Design status", result.Status.ToString()),
        new("Capability supported", (result.Capability?.IsSupported == true).ToString()),
        new("Capability level", result.Capability?.Level.ToString() ?? "Not recorded"),
        new("Capability reason", result.Capability?.ReasonCode.ToString() ?? "Not recorded"),
        new("Capability message", result.Capability?.Message ?? "Not recorded"),
        new("Suggested alternative", string.IsNullOrWhiteSpace(result.Capability?.SuggestedAlternative)
            ? "None required"
            : result.Capability.SuggestedAlternative),
        new("Filter type", request.Mode.ToString()), new("Response", request.ResponseType.ToString()),
        new("Approximation", request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc ? "Not applied (topology-based RC)" : request.Approximation.ToString()),
        new("Implementation", request.Implementation.ToString()),
        new("Required prototype order", result.RequiredOrder.ToString(CultureInfo.InvariantCulture)),
        new("Implemented prototype order", result.PrototypeOrder.ToString(CultureInfo.InvariantCulture)),
        new("Realized transfer-function order", result.RealizedOrder.ToString(CultureInfo.InvariantCulture)),
        new("Order mode", request.OrderMode.ToString()),
        new("LC termination mode", request.Implementation == FilterImplementationType.Lc ? request.LcTerminationMode.ToString() : "N/A"),
        new("Resistor / capacitor series", $"{request.ComponentConstraints.ResistorSeries} / {request.ComponentConstraints.CapacitorSeries}")
        };
        if (request.Mode == FilterMode.Active)
        {
            fields.Insert(14, new("Signal architecture", request.SignalArchitecture.ToString()));
            fields.Insert(15, new("Requested A0", F(request.PassbandGain), "V/V"));
        }
        return fields;
    }

    private static IReadOnlyList<ReportField> BuildSourceLoadReference(FilterDesignResult result, FilterDesignRequest request)
    {
        if (request.Mode != FilterMode.Passive || result.PassiveAnalysis is not { } passive)
            return [new("Applicability", "Active filter: passive source/load boundary not used")];
        var fields = new List<ReportField>
        {
            new("Electrical boundary", "VSRC → Rs → VIN → filter network → VOUT → RL → GND"),
            new("VSRC", "Ideal Thevenin open-circuit source voltage"),
            new("VIN", request.SourceResistanceOhm == 0 ? "Filter input node; identical to VSRC because Rs = 0" : "Loaded filter input node after Rs"),
            new("VOUT", "Loaded output node across RL"),
            new("Source resistance Rs", F(request.SourceResistanceOhm), "Ω"),
            new("Load resistance RL", F(request.LoadResistanceOhm), "Ω"),
            new("Termination mode", request.Implementation == FilterImplementationType.Lc ? request.LcTerminationMode.ToString() : "Loaded voltage-transfer RC"),
            new("LC termination policy", request.Implementation == FilterImplementationType.Lc ? request.LcTerminationPolicy.ToString() : "N/A"),
            new("Ladder orientation", request.Implementation == FilterImplementationType.Lc ? request.LcLadderOrientation.ToString() : "N/A"),
            new("Primary response reference", passive.PrimaryReference.ToString()),
            new("Direct-through VOUT/VSRC", F(passive.Boundary.DirectThroughVoltageRatio), "V/V"),
            new("Direct-through absolute level", F(20 * Math.Log10(passive.Boundary.DirectThroughVoltageRatio)), "dB"),
            new("Absolute voltage transfer", "VOUT / VSRC"),
            new("Filter-node voltage transfer", "VOUT / VIN"),
            new("Insertion response", "|H / Hthrough|²; direct through is 0 dB")
        };
        fields.Add(request.SourceResistanceOhm > 0
            ? new("Transducer gain", "PL / Pavs = 4·Rs/RL·|VOUT/VSRC|²")
            : new("Transducer gain", "Not defined for Rs = 0 because available source power is unbounded"));
        return fields;
    }

    private static IReadOnlyList<ReportField> BuildInputs(FilterDesignRequest request, FilterDesignResult result)
    {
        var fields = new List<ReportField>
        {
            new("Allowable passband ripple Rp", F(request.PassbandRippleDb), "dB"),
            new("Stopband attenuation As", F(request.StopbandAttenuationDb), "dB")
        };
        if (request.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass)
        {
            fields.Add(new("Passband edge fp", F(request.PassbandEdgeHz), "Hz"));
            fields.Add(new("Stopband edge fs", F(request.StopbandEdgeHz), "Hz"));
        }
        else
        {
            fields.AddRange([
                new("Lower stopband edge fs1", F(request.StopbandLowerHz), "Hz"),
                new("Lower passband edge fp1", F(request.PassbandLowerHz), "Hz"),
                new("Upper passband edge fp2", F(request.PassbandUpperHz), "Hz"),
                new("Upper stopband edge fs2", F(request.StopbandUpperHz), "Hz"),
                new("Geometric center f0", F(Math.Sqrt(request.PassbandLowerHz * request.PassbandUpperHz)), "Hz"),
                new("Passband bandwidth", F(request.PassbandUpperHz - request.PassbandLowerHz), "Hz")]);
        }
        fields.AddRange([
            new("Series source resistance Rs", F(request.SourceResistanceOhm), "Ω"),
            new("Load resistance RL", F(request.LoadResistanceOhm), "Ω"),
            new("Preferred resistance scale", F(request.PreferredResistanceOhm), "Ω"),
            new("Resistor tolerance", F(request.ComponentConstraints.ResistorTolerancePercent), "%"),
            new("Capacitor tolerance", F(request.ComponentConstraints.CapacitorTolerancePercent), "%"),
            new("Standard-value optimization", request.ComponentConstraints.OptimizeStandardValues ? "Enabled" : "Disabled")]);
        return fields;
    }

    private static IReadOnlyList<ReportField> BuildApproximation(FilterDesignResult result, FilterDesignRequest request)
    {
        var metadata = result.PrototypeNormalization;
        var fields = new List<ReportField>
        {
            new("Approximation", request.Approximation.ToString()),
            new("Definition", result.ApproximationDescription),
            new("Normalization", metadata?.Name ?? "Not recorded"),
            new("Normalization details", metadata?.Definition ?? "Not recorded"),
            new("Order calculation", request.OrderMode == FilterOrderMode.SpecifyOrder ? "Manual order with numerical specification check" : metadata?.OrderDeterminationMethod ?? "Not recorded"),
            new("Reference", metadata?.ReferenceSource ?? "Not recorded"),
            new("Prototype numerator gain", F(result.PrototypeGain))
        };
        fields.AddRange(result.ApproximationParameters.Select(value => new ReportField(value.Key, F(value.Value))));
        if (result.LcLadderPrototype is { } ladder)
        {
            fields.Add(new("LC synthesis method", ladder.SynthesisMethod));
            fields.Add(new("LC transfer reference", ladder.ExpectedTransferReference));
            fields.Add(new("LC source/load normalized termination", $"g0={F(ladder.SourceTerminationG0)}, gN+1={F(ladder.LoadTerminationGN1)}"));
            fields.Add(new("LC normalized ladder values", string.Join(", ", ladder.Elements.Select(value => $"g{value.Index}={F(value.GValue)}"))));
            fields.Add(new("LC ladder validation", ladder.ValidationMetrics.Summary));
            if (ladder.ExtractionRecords is { Count: > 0 })
                fields.Add(new("Cauer extraction steps", ladder.ExtractionRecords.Count.ToString(CultureInfo.InvariantCulture)));
            if (result.PassiveComponentAssumptions is { } assumptions)
            {
                fields.Add(new("L/C electrical model", assumptions.UsesIdealInductorsAndCapacitors ? "Ideal L/C" : "Practical parasitic model"));
                fields.Add(new("Inductor Q / DCR", assumptions.InductorQ is null ? "Not specified (v1 extension point)" : $"Q={F(assumptions.InductorQ.Value)}, DCR={F(assumptions.InductorDcrOhm ?? 0)} Ω"));
                fields.Add(new("Capacitor ESR", assumptions.CapacitorEsrOhm is null ? "Not specified (v1 extension point)" : $"{F(assumptions.CapacitorEsrOhm.Value)} Ω"));
            }
        }
        if (request.Approximation == FilterApproximation.Bessel)
        {
            var prototype = new Approximation.FilterPrototype(request.Approximation, result.PrototypeOrder, result.PrototypePoles, [],
                result.PrototypeZeros, Normalization: result.PrototypeNormalization);
            fields.Add(new("Normalized DC group delay", F(Approximation.PrototypeResponseEvaluator.GroupDelay(prototype, 0)), "s (normalized)"));
        }
        return fields;
    }

    private static IEnumerable<ReportComponent> BuildComponents(FilterDesignResult result, FilterDesignRequest request)
    {
        foreach (var section in result.Sections)
        foreach (var component in section.Components)
            yield return Convert(section.Target.Index, SectionTopology(request, section), component);
        foreach (var component in result.AdditionalNetworkComponents)
            yield return Convert(0, "Gain / normalization network", component);
    }

    private static ReportComponent Convert(int section, string topology, SynthesizedComponentValue value)
    {
        var unit = value.ComponentType switch { ComponentType.Resistor => "ohm", ComponentType.Capacitor => "F", ComponentType.Inductor => "H", _ => "" };
        return new(section, topology, value.Role, value.ComponentType.ToString(), value.TheoreticalValue,
            value.SelectedValue, unit, value.Series?.ToString() ?? "Theoretical", value.TolerancePercent, value.RelativeError);
    }

    private static string SectionTopology(FilterDesignRequest request, SynthesizedSection section) =>
        $"{request.Implementation} {section.Target.ResponseType} {section.Target.Order}";

    private static IReadOnlyList<ReportField> BuildGain(FilterDesignResult result)
    {
        if (result.DesignRequest?.Mode == FilterMode.Passive)
            return [new("Passive gain semantics", "No arbitrary A0; passband level is measured from the selected loaded response reference")];
        var gain = result.GainDistribution!;
        var auxiliaryBuffer = result.AdditionalNetworkComponents.Any(value => value.Role.StartsWith("InterstageAttenuator", StringComparison.Ordinal));
        return
        [
            new("Requested overall gain", F(gain.RequestedOverallGain), "V/V"),
            new("Raw cascade gain product", F(gain.RawCascadeGainMagnitude), "V/V"),
            new("Interstage/output scale product", F(gain.InterstageScaleProduct)),
            new("Attenuator / isolation buffer", auxiliaryBuffer ? "Present" : "Not required"),
            new("Expected overall polarity", gain.ExpectedOverallPolarity.ToString()),
            new("Actual passband peak gain", F(gain.ActualPassbandPeakGain), "V/V"),
            new("Reference phase", F(gain.ReferencePhaseDegrees), "degree")
        ];
    }

    private static IReadOnlyList<ReportVerificationRow> BuildVerification(FilterDesignResult result, FilterDesignRequest request)
    {
        var rows = new List<ReportVerificationRow>();
        if (result.GainDistribution != null)
        {
            var target = result.GainDistribution.RequestedOverallGain;
            var actual = result.GainDistribution.ActualPassbandPeakGain;
            rows.Add(new("Passband gain A0", F(target), F(actual), P((actual - target) / target), Math.Abs(actual - target) / target <= .05));
        }
        if (result.AnalysisResult != null)
        {
            var passband = result.AnalysisResult.Points.Where(point => request.ResponseType switch
            {
                FilterResponseType.LowPass => point.FrequencyHz <= request.PassbandEdgeHz,
                FilterResponseType.HighPass => point.FrequencyHz >= request.PassbandEdgeHz,
                FilterResponseType.BandPass => point.FrequencyHz >= request.PassbandLowerHz && point.FrequencyHz <= request.PassbandUpperHz,
                FilterResponseType.BandStop => point.FrequencyHz <= request.PassbandLowerHz || point.FrequencyHz >= request.PassbandUpperHz,
                _ => false
            }).ToArray();
            if (passband.Length > 0)
            {
                var ripple = passband.Max(point => point.MagnitudeDb) - passband.Min(point => point.MagnitudeDb);
                rows.Add(new("Passband ripple Rp", $"<= {F(request.PassbandRippleDb)} dB", $"{F(ripple)} dB", $"{F(ripple - request.PassbandRippleDb)} dB", ripple <= request.PassbandRippleDb + .1));
            }
        }
        if (result.SpecificationCompliance != null)
            rows.AddRange(result.SpecificationCompliance.Edges.Select(edge => new ReportVerificationRow(
                edge.Label, $"{F(edge.LimitMagnitudeDb)} dB", $"{F(edge.ActualMagnitudeDb)} dB",
                $"{F(edge.ActualMagnitudeDb - edge.LimitMagnitudeDb)} dB", edge.MeetsLimit)));
        rows.AddRange(result.Sections.SelectMany(section => new[]
        {
            new ReportVerificationRow($"Section {section.Target.Index} f0", F(section.Target.PoleFrequencyHz), F(section.ActualPoleFrequencyHz), P(section.FrequencyError), Math.Abs(section.FrequencyError) <= .08),
            new ReportVerificationRow($"Section {section.Target.Index} Q", F(section.Target.Q), F(section.ActualQ), P(section.QError), Math.Abs(section.QError) <= .12),
            new ReportVerificationRow($"Section {section.Target.Index} gain", F(section.Target.SectionGain), F(section.ActualGain), P(section.GainError), Math.Abs(section.GainError) <= .08)
        }));
        return rows;
    }

    private static ReportCircuitSummary BuildCircuitSummary(FilterDesignResult result, FilterDesignRequest request)
    {
        var graph = result.CircuitGraph!;
        int Count(ComponentType type) => graph.Components.Count(value => value.Type == type);
        var chain = string.Join(" -> ", result.Sections.Select(section => $"Section {section.Target.Index} {request.Implementation} {section.Target.ResponseType}")) + " -> Output";
        var external = result.PassiveAnalysis?.Boundary.ExternalTerminations ?? [];
        var externalSummary = external.Count == 0 ? "None" : string.Join(", ", external.Select(value => $"{value.Reference}={F(value.ResistanceOhm)} Ω ({value.Role})"));
        return new(Count(ComponentType.Resistor), Count(ComponentType.Capacitor), Count(ComponentType.Inductor),
            Count(ComponentType.OpAmp), graph.Nets.Count, result.Sections.Count, chain, external.Count, externalSummary);
    }

    private static IReadOnlyDictionary<string, object> BuildSnapshotRequest(FilterDesignRequest request) =>
        new Dictionary<string, object>
        {
            ["Mode"] = request.Mode.ToString(), ["ResponseType"] = request.ResponseType.ToString(),
            ["Approximation"] = request.Approximation.ToString(), ["Implementation"] = request.Implementation.ToString(),
            ["LcTerminationMode"] = request.LcTerminationMode.ToString(),
            ["LcTerminationPolicy"] = request.LcTerminationPolicy.ToString(),
            ["LcLadderOrientation"] = request.LcLadderOrientation.ToString(),
            ["PassiveResponseReference"] = request.PassiveResponseReference.ToString(),
            ["PassiveRcTopology"] = request.PassiveRcTopology.ToString(),
            ["OrderMode"] = request.OrderMode.ToString(), ["SpecifiedOrder"] = request.SpecifiedOrder,
            ["RpDb"] = request.PassbandRippleDb, ["AsDb"] = request.StopbandAttenuationDb,
            ["A0Linear"] = request.PassbandGain, ["PassbandEdgeHz"] = request.PassbandEdgeHz,
            ["StopbandEdgeHz"] = request.StopbandEdgeHz, ["StopbandLowerHz"] = request.StopbandLowerHz,
            ["PassbandLowerHz"] = request.PassbandLowerHz, ["PassbandUpperHz"] = request.PassbandUpperHz,
            ["StopbandUpperHz"] = request.StopbandUpperHz, ["SourceResistanceOhm"] = request.SourceResistanceOhm,
            ["LoadResistanceOhm"] = request.LoadResistanceOhm,
            ["PreferredResistanceOhm"] = request.PreferredResistanceOhm,
            ["ResistorSeries"] = request.ComponentConstraints.ResistorSeries.ToString(),
            ["CapacitorSeries"] = request.ComponentConstraints.CapacitorSeries.ToString(),
            ["InductorSeries"] = request.ComponentConstraints.InductorSeries.ToString(),
            ["ResistorTolerancePercent"] = request.ComponentConstraints.ResistorTolerancePercent,
            ["CapacitorTolerancePercent"] = request.ComponentConstraints.CapacitorTolerancePercent,
            ["InductorTolerancePercent"] = request.ComponentConstraints.InductorTolerancePercent,
            ["ResistorMinimumOhm"] = request.ComponentConstraints.ResistorMinimumOhm,
            ["ResistorMaximumOhm"] = request.ComponentConstraints.ResistorMaximumOhm,
            ["CapacitorMinimumFarad"] = request.ComponentConstraints.CapacitorMinimumF,
            ["CapacitorMaximumFarad"] = request.ComponentConstraints.CapacitorMaximumF,
            ["InductorMinimumHenry"] = request.ComponentConstraints.InductorMinimumH,
            ["InductorMaximumHenry"] = request.ComponentConstraints.InductorMaximumH,
            ["OptimizeStandardValues"] = request.ComponentConstraints.OptimizeStandardValues,
            ["OptimizationTarget"] = request.ComponentConstraints.OptimizationTarget.ToString()
        };

    private static string F(double value) => value.ToString("G9", CultureInfo.InvariantCulture);
    private static string P(double value) => value.ToString("P4", CultureInfo.InvariantCulture);
}
