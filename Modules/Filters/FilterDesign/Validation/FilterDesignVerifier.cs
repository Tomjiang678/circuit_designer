using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Gain;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Verification;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;

public sealed record FilterDesignVerificationReport(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>Final gate over selected parts, the actual graph response and generated geometry.</summary>
public sealed class FilterDesignVerifier
{
    private readonly FilterCircuitAssembler sectionAssembler = new();
    private readonly ICircuitSectionTransferVerifier sectionTransferVerifier = new CircuitSectionMnaVerifier();

    public FilterDesignVerificationReport Verify(
        FilterDesignRequest request,
        IReadOnlyList<SynthesizedSection> sections,
        FilterCircuitBuildResult circuit,
        SchematicDocument schematic,
        FilterSpecificationCompliance compliance,
        GainDistributionSummary gain)
    {
        var errors = new List<string>();
        var warnings = new List<string>(gain.Warnings);
        foreach (var section in sections)
        {
            if (section.Components.Any(value => value.SelectedValue <= 0 || !double.IsFinite(value.SelectedValue)))
                errors.Add($"Section {section.Target.Index} contains a non-positive or non-finite selected component value.");
            var topologyBasedPassiveRc = request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc;
            if (!topologyBasedPassiveRc &&
                (Math.Abs(section.FrequencyError) > .08 || Math.Abs(section.QError) > .12 || Math.Abs(section.GainError) > .08))
                errors.Add($"Section {section.Target.Index} failed reverse verification: f0 {section.FrequencyError:P2}, Q {section.QError:P2}, gain {section.GainError:P2}.");
        }

        if (request.Mode == FilterMode.Active)
        {
            var metricTolerance = request.ComponentConstraints.OptimizeStandardValues ? .13 : 5e-4;
            var sampleTolerance = request.ComponentConstraints.OptimizeStandardValues ? .20 : 8e-4;
            foreach (var section in sections)
            {
                try
                {
                    var graph = sectionAssembler.BuildActiveSection(request, section);
                    var target = SectionTransferTarget.FromSection(section.Target, section.PolaritySign);
                    var measurement = sectionTransferVerifier.Verify(graph, target,
                        new SectionTransferVerificationOptions(metricTolerance, sampleTolerance));
                    if (!measurement.IsValid)
                        errors.Add($"Section {section.Target.Index} failed independent CircuitGraph/MNA transfer verification: {string.Join(", ", measurement.Errors)}.");
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    errors.Add($"Section {section.Target.Index} independent CircuitGraph/MNA verification could not run: {exception.Message}");
                }
            }
        }

        try { FilterCircuitGraphValidator.EnsureFilterRequirements(circuit.CircuitGraph, request.Mode); }
        catch (InvalidOperationException exception) { errors.Add(exception.Message); }

        var boxes = schematic.PlacedComponents.Select(item => (item.Component.Reference, Bounds: SchematicGeometry.GetBounds(item, 8))).ToArray();
        for (var first = 0; first < boxes.Length; first++)
        for (var second = first + 1; second < boxes.Length; second++)
            if (Intersects(boxes[first].Bounds, boxes[second].Bounds))
                errors.Add($"Schematic components {boxes[first].Reference} and {boxes[second].Reference} overlap.");

        foreach (var opAmp in schematic.PlacedComponents.Where(item => item.Component.Type == Core.Circuit.ComponentType.OpAmp))
        {
            var interior = SchematicGeometry.GetBounds(opAmp, -5);
            foreach (var wire in schematic.Wires)
            for (var index = 1; index < wire.Points.Count; index++)
                if (CrossesInterior(wire.Points[index - 1], wire.Points[index], interior))
                    errors.Add($"Wire {wire.NetName} segment {wire.Points[index - 1]} -> {wire.Points[index]} crosses the body of {opAmp.Component.Reference} ({interior}).");
        }

        if (!compliance.MeetsAllEdges)
        {
            if (! (request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Rc) &&
                request.OrderMode == FilterOrderMode.CalculateAutomatically &&
                !(request.Mode == FilterMode.Passive && request.Implementation == FilterImplementationType.Lc && request.SourceResistanceOhm == 0))
                errors.AddRange(compliance.Warnings);
            else warnings.AddRange(compliance.Warnings);
        }
        if (request.Mode == FilterMode.Active &&
            Math.Abs(gain.ActualPassbandPeakGain - gain.RequestedOverallGain) / gain.RequestedOverallGain > .05)
            errors.Add($"Whole-circuit passband gain {gain.ActualPassbandPeakGain:0.######} V/V does not realize requested A0={gain.RequestedOverallGain:0.######} V/V within 5%.");
        return new(errors.Count == 0, errors.Distinct().ToArray(), warnings.Distinct().ToArray());
    }

    private static bool Intersects(SchematicRect a, SchematicRect b) =>
        a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

    private static bool CrossesInterior(SchematicPoint a, SchematicPoint b, SchematicRect box)
    {
        if (Math.Abs(a.X - b.X) < 1e-9)
            return a.X > box.X && a.X < box.X + box.Width && Math.Max(a.Y, b.Y) > box.Y && Math.Min(a.Y, b.Y) < box.Y + box.Height;
        if (Math.Abs(a.Y - b.Y) < 1e-9)
            return a.Y > box.Y && a.Y < box.Y + box.Height && Math.Max(a.X, b.X) > box.X && Math.Min(a.X, b.X) < box.X + box.Width;
        return true;
    }
}
