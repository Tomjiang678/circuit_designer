using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Gain;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

public sealed class FilterCircuitAssembler
{
    public CircuitGraph BuildActiveSection(FilterDesignRequest request, SynthesizedSection section)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(section);
        if (request.Mode != FilterMode.Active) throw new ArgumentException("Section graph extraction is for active sections.", nameof(request));
        var build = request.Implementation switch
        {
            FilterImplementationType.MultipleFeedback => BuildMfb(request, [section], 1),
            FilterImplementationType.BainterNotch => BuildBainter(request, [section], 1),
            FilterImplementationType.SallenKey => BuildSallenKey(request, [section], 1),
            _ => throw new NotSupportedException($"No active section assembler exists for {request.Implementation}.")
        };
        return build.CircuitGraph;
    }

    public FilterCircuitBuildResult Build(
        FilterDesignRequest request,
        NormalizedFilterSpecification specification,
        FilterPrototype prototype,
        IReadOnlyList<SynthesizedSection> sections,
        GainDistributionPlan gainPlan)
    {
        var activePrototypeNormalization = request.Mode == FilterMode.Active &&
                                           request.Approximation == FilterApproximation.ChebyshevTypeI &&
                                           prototype.Order % 2 == 0
            ? Math.Pow(10, -request.PassbandRippleDb / 20)
            : 1d;
        activePrototypeNormalization *= gainPlan.OutputScale;
        return request.Mode == FilterMode.Active
            ? request.Implementation switch
            {
                FilterImplementationType.MultipleFeedback => BuildMfb(request, sections, activePrototypeNormalization),
                FilterImplementationType.BainterNotch => BuildBainter(request, sections, activePrototypeNormalization),
                _ => BuildSallenKey(request, sections, activePrototypeNormalization)
            }
            : request.Implementation == FilterImplementationType.Rc
                ? BuildPassiveRc(request, sections)
                : BuildPassiveLc(request, specification, prototype);
    }

    private static FilterCircuitBuildResult BuildBainter(FilterDesignRequest request, IReadOnlyList<SynthesizedSection> sections, double outputNormalization)
    {
        var builder = new CircuitBuilder();
        var allocator = new ReferenceDesignatorAllocator();
        var hints = new List<FilterPlacementHint>();
        builder.CreateNet("GND"); builder.CreateNet("VCC"); builder.CreateNet("VEE");
        var inputNet = "VIN";
        for (var position = 0; position < sections.Count; position++)
        {
            var outputNet = position == sections.Count - 1
                ? outputNormalization < 1 - 1e-12 ? "N_FILTER_CORE_OUT" : "VOUT"
                : $"N_SEC_{position + 1}";
            AddBainterSection(builder, allocator, hints, sections[position], inputNet, outputNet, position + 1);
            inputNet = outputNet;
        }
        var networkValues = AddOutputNormalization(builder, allocator, hints, request, outputNormalization, sections.Count);
        var graph = builder.Build();
        FilterCircuitGraphValidator.EnsureFilterRequirements(graph, request.Mode);
        return new FilterCircuitBuildResult(graph, hints, networkValues);
    }

    private static void AddBainterSection(CircuitBuilder builder, ReferenceDesignatorAllocator allocator, List<FilterPlacementHint> hints,
        SynthesizedSection section, string inputNet, string outputNet, int sectionIndex)
    {
        var values = section.Components.ToDictionary(value => value.Role);
        var r1 = Add(builder, allocator, values["R1"]); var r2 = Add(builder, allocator, values["R2"]);
        var r3 = Add(builder, allocator, values["R3"]); var r4 = Add(builder, allocator, values["R4"]);
        var r5 = Add(builder, allocator, values["R5"]); var r6 = Add(builder, allocator, values["R6"]);
        var c1 = Add(builder, allocator, values["C1"]); var c2 = Add(builder, allocator, values["C2"]);
        var u1 = builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));
        var u2 = builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));
        var u3 = builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));
        var firstInv = $"N_S{sectionIndex}_BN_U1_INV"; var firstOut = $"N_S{sectionIndex}_BN_U1_OUT";
        var secondInv = $"N_S{sectionIndex}_BN_U2_INV"; var secondOut = $"N_S{sectionIndex}_BN_U2_OUT";
        var mix = $"N_S{sectionIndex}_BN_MIX";
        builder.Connect(inputNet, r1.GetPin("1"), c2.GetPin("1"));
        builder.Connect(firstInv, r1.GetPin("2"), r2.GetPin("1"), u1.GetPin("IN-"));
        builder.Connect(firstOut, r2.GetPin("2"), r3.GetPin("1"), u1.GetPin("OUT"));
        builder.Connect(secondInv, r3.GetPin("2"), c1.GetPin("1"), r4.GetPin("1"), u2.GetPin("IN-"));
        builder.Connect(secondOut, c1.GetPin("2"), r5.GetPin("1"), u2.GetPin("OUT"));
        builder.Connect(mix, r5.GetPin("2"), c2.GetPin("2"), r6.GetPin("1"), u3.GetPin("IN+"));
        builder.Connect(outputNet, r4.GetPin("2"), u3.GetPin("OUT"));
        ConnectNonInvertingGain(builder, allocator, hints, values, u3, outputNet, sectionIndex, "BainterGain", "R7", "R8");
        builder.Connect("GND", u1.GetPin("IN+"), u2.GetPin("IN+"), r6.GetPin("2"));
        builder.Connect("VCC", u1.GetPin("V+"), u2.GetPin("V+"), u3.GetPin("V+"));
        builder.Connect("VEE", u1.GetPin("V-"), u2.GetPin("V-"), u3.GetPin("V-"));
        hints.Add(new(r1.Reference, sectionIndex, "BainterR1", 0, 0)); hints.Add(new(r2.Reference, sectionIndex, "BainterR2", 0, 0));
        hints.Add(new(r3.Reference, sectionIndex, "BainterR3", 0, 0)); hints.Add(new(r4.Reference, sectionIndex, "BainterR4", 0, 0));
        hints.Add(new(r5.Reference, sectionIndex, "BainterR5", 0, 0)); hints.Add(new(r6.Reference, sectionIndex, "BainterR6", 0, 0));
        hints.Add(new(c1.Reference, sectionIndex, "BainterC1", 0, 0)); hints.Add(new(c2.Reference, sectionIndex, "BainterC2", 0, 0));
        hints.Add(new(u1.Reference, sectionIndex, "BainterU1", 0, 0)); hints.Add(new(u2.Reference, sectionIndex, "BainterU2", 0, 0)); hints.Add(new(u3.Reference, sectionIndex, "BainterU3", 0, 0));
    }

    private static FilterCircuitBuildResult BuildMfb(FilterDesignRequest request, IReadOnlyList<SynthesizedSection> sections, double outputNormalization)
    {
        var builder = new CircuitBuilder();
        var allocator = new ReferenceDesignatorAllocator();
        var hints = new List<FilterPlacementHint>();
        builder.CreateNet("GND"); builder.CreateNet("VCC"); builder.CreateNet("VEE");
        var inputNet = "VIN";
        for (var position = 0; position < sections.Count; position++)
        {
            var section = sections[position];
            var outputNet = position == sections.Count - 1
                ? outputNormalization < 1 - 1e-12 ? "N_FILTER_CORE_OUT" : "VOUT"
                : $"N_SEC_{position + 1}";
            if (section.Target.Order == SectionOrder.First)
                AddActiveFirstOrder(builder, allocator, hints, section, inputNet, outputNet, position + 1);
            else if (section.Target.ResponseType == FilterResponseType.HighPass)
                AddMfbHighPassSecondOrder(builder, allocator, hints, section, inputNet, outputNet, position + 1);
            else if (section.Target.ResponseType == FilterResponseType.BandPass)
                AddMfbBandPassSecondOrder(builder, allocator, hints, section, inputNet, outputNet, position + 1);
            else
                AddMfbLowPassSecondOrder(builder, allocator, hints, section, inputNet, outputNet, position + 1);
            inputNet = outputNet;
        }
        var networkValues = AddOutputNormalization(builder, allocator, hints, request, outputNormalization, sections.Count);
        var graph = builder.Build();
        FilterCircuitGraphValidator.EnsureFilterRequirements(graph, request.Mode);
        return new FilterCircuitBuildResult(graph, hints, networkValues);
    }

    private static void AddMfbBandPassSecondOrder(CircuitBuilder builder, ReferenceDesignatorAllocator allocator, List<FilterPlacementHint> hints, SynthesizedSection section, string inputNet, string outputNet, int sectionIndex)
    {
        var values = section.Components.ToDictionary(value => value.Role);
        var r1=Add(builder,allocator,values["R1"]); var r2=Add(builder,allocator,values["R2"]); var rb=Add(builder,allocator,values["RB"]);
        var c1=Add(builder,allocator,values["C1"]); var c2=Add(builder,allocator,values["C2"]); var op=builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));
        var node=$"N_S{sectionIndex}_SUM"; var inv=$"N_S{sectionIndex}_INV";
        builder.Connect(inputNet,r1.GetPin("1")); builder.Connect(node,r1.GetPin("2"),rb.GetPin("1"),c1.GetPin("1"),c2.GetPin("1"));
        builder.Connect(inv,c1.GetPin("2"),r2.GetPin("1"),op.GetPin("IN-")); builder.Connect(outputNet,c2.GetPin("2"),r2.GetPin("2"),op.GetPin("OUT"));
        builder.Connect("GND",rb.GetPin("2"),op.GetPin("IN+")); builder.Connect("VCC",op.GetPin("V+")); builder.Connect("VEE",op.GetPin("V-"));
        hints.Add(new(r1.Reference,sectionIndex,"MfbInput",0,0)); hints.Add(new(c1.Reference,sectionIndex,"MfbToInput",1,0)); hints.Add(new(rb.Reference,sectionIndex,"MfbShunt",1,1));
        hints.Add(new(c2.Reference,sectionIndex,"MfbFeedbackTop",1,-1)); hints.Add(new(r2.Reference,sectionIndex,"MfbFeedbackBottom",2,1)); hints.Add(new(op.Reference,sectionIndex,"MfbOpAmp",3,0));
    }

    private static void AddMfbHighPassSecondOrder(CircuitBuilder builder, ReferenceDesignatorAllocator allocator, List<FilterPlacementHint> hints, SynthesizedSection section, string inputNet, string outputNet, int sectionIndex)
    {
        var values = section.Components.ToDictionary(value => value.Role);
        var c1 = Add(builder, allocator, values["C1"]); var r2 = Add(builder, allocator, values["R2"]); var c3 = Add(builder, allocator, values["C3"]);
        var c4 = Add(builder, allocator, values["C4"]); var r5 = Add(builder, allocator, values["R5"]);
        var opAmp = builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));
        var summingNode = $"N_S{sectionIndex}_SUM"; var invertingNode = $"N_S{sectionIndex}_INV";
        builder.Connect(inputNet, c1.GetPin("1"));
        builder.Connect(summingNode, c1.GetPin("2"), c4.GetPin("1"), c3.GetPin("1"), r5.GetPin("1"));
        builder.Connect(invertingNode, c3.GetPin("2"), r2.GetPin("1"), opAmp.GetPin("IN-"));
        builder.Connect(outputNet, c4.GetPin("2"), r2.GetPin("2"), opAmp.GetPin("OUT"));
        builder.Connect("GND", r5.GetPin("2"), opAmp.GetPin("IN+"));
        builder.Connect("VCC", opAmp.GetPin("V+")); builder.Connect("VEE", opAmp.GetPin("V-"));
        hints.Add(new(c1.Reference, sectionIndex, "MfbInput", 0, 0)); hints.Add(new(c3.Reference, sectionIndex, "MfbToInput", 1, 0));
        hints.Add(new(r5.Reference, sectionIndex, "MfbShunt", 1, 1)); hints.Add(new(c4.Reference, sectionIndex, "MfbFeedbackTop", 1, -1));
        hints.Add(new(r2.Reference, sectionIndex, "MfbFeedbackBottom", 2, 1)); hints.Add(new(opAmp.Reference, sectionIndex, "MfbOpAmp", 3, 0));
    }

    private static void AddMfbLowPassSecondOrder(CircuitBuilder builder, ReferenceDesignatorAllocator allocator, List<FilterPlacementHint> hints, SynthesizedSection section, string inputNet, string outputNet, int sectionIndex)
    {
        var values = section.Components.ToDictionary(value => value.Role);
        var r1 = Add(builder, allocator, values["R1"]); var r2 = Add(builder, allocator, values["R2"]); var r3 = Add(builder, allocator, values["R3"]);
        var c1 = Add(builder, allocator, values["C1"]); var c2 = Add(builder, allocator, values["C2"]);
        var opAmp = builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));
        var summingNode = $"N_S{sectionIndex}_SUM";
        var invertingNode = $"N_S{sectionIndex}_INV";
        builder.Connect(inputNet, r1.GetPin("1"));
        builder.Connect(summingNode, r1.GetPin("2"), r2.GetPin("1"), r3.GetPin("1"), c2.GetPin("1"));
        builder.Connect(invertingNode, r3.GetPin("2"), c1.GetPin("1"), opAmp.GetPin("IN-"));
        builder.Connect(outputNet, r2.GetPin("2"), c1.GetPin("2"), opAmp.GetPin("OUT"));
        builder.Connect("GND", c2.GetPin("2"), opAmp.GetPin("IN+"));
        builder.Connect("VCC", opAmp.GetPin("V+")); builder.Connect("VEE", opAmp.GetPin("V-"));
        hints.Add(new(r1.Reference, sectionIndex, "MfbInput", 0, 0));
        hints.Add(new(r3.Reference, sectionIndex, "MfbToInput", 1, 0));
        hints.Add(new(c2.Reference, sectionIndex, "MfbShunt", 1, 1));
        hints.Add(new(r2.Reference, sectionIndex, "MfbFeedbackTop", 1, -1));
        hints.Add(new(c1.Reference, sectionIndex, "MfbFeedbackBottom", 2, 1));
        hints.Add(new(opAmp.Reference, sectionIndex, "MfbOpAmp", 3, 0));
    }

    private static FilterCircuitBuildResult BuildSallenKey(FilterDesignRequest request, IReadOnlyList<SynthesizedSection> sections, double outputNormalization)
    {
        var builder = new CircuitBuilder();
        var allocator = new ReferenceDesignatorAllocator();
        var hints = new List<FilterPlacementHint>();
        builder.CreateNet("GND"); builder.CreateNet("VCC"); builder.CreateNet("VEE");
        var inputNet = "VIN";
        for (var sectionPosition = 0; sectionPosition < sections.Count; sectionPosition++)
        {
            var section = sections[sectionPosition];
            var outputNet = sectionPosition == sections.Count - 1
                ? outputNormalization < 1 - 1e-12 ? "N_FILTER_CORE_OUT" : "VOUT"
                : $"N_SEC_{sectionPosition + 1}";
            if (section.Target.Order == SectionOrder.First)
                AddActiveFirstOrder(builder, allocator, hints, section, inputNet, outputNet, sectionPosition + 1);
            else if (section.Target.ResponseType == FilterResponseType.BandPass)
                AddSallenKeyBandPassSecondOrder(builder, allocator, hints, section, inputNet, outputNet, sectionPosition + 1);
            else
                AddSallenKeySecondOrder(builder, allocator, hints, section, inputNet, outputNet, sectionPosition + 1);
            inputNet = outputNet;
        }
        var networkValues = AddOutputNormalization(builder, allocator, hints, request, outputNormalization, sections.Count);
        var graph = builder.Build();
        FilterCircuitGraphValidator.EnsureFilterRequirements(graph, request.Mode);
        return new FilterCircuitBuildResult(graph, hints, networkValues);
    }

    private static void AddSallenKeyBandPassSecondOrder(CircuitBuilder builder,ReferenceDesignatorAllocator allocator,List<FilterPlacementHint> hints,SynthesizedSection section,string inputNet,string outputNet,int sectionIndex)
    {
        var v=section.Components.ToDictionary(x=>x.Role);var r1=Add(builder,allocator,v["R1"]);var r2=Add(builder,allocator,v["R2"]);var r3=Add(builder,allocator,v["R3"]);var r4=Add(builder,allocator,v["R4"]);var r5=Add(builder,allocator,v["R5"]);var ra=Add(builder,allocator,v["RA"]);var rb=Add(builder,allocator,v["RB"]);var c1=Add(builder,allocator,v["C1"]);var c2=Add(builder,allocator,v["C2"]);
        var filterOp=builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));var buffer=builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));
        var a=$"N_S{sectionIndex}_A";var b=$"N_S{sectionIndex}_B";var inv=$"N_S{sectionIndex}_INV";var raw=$"N_S{sectionIndex}_RAW";var normalized=$"N_S{sectionIndex}_NORM";
        builder.Connect(inputNet,r1.GetPin("1"));builder.Connect(a,r1.GetPin("2"),c1.GetPin("1"),r2.GetPin("1"));builder.Connect(b,c1.GetPin("2"),c2.GetPin("1"),r3.GetPin("1"),filterOp.GetPin("IN+"));
        builder.Connect(raw,r2.GetPin("2"),r4.GetPin("1"),filterOp.GetPin("OUT"),ra.GetPin("1"));builder.Connect(inv,r4.GetPin("2"),r5.GetPin("1"),filterOp.GetPin("IN-"));
        builder.Connect(normalized,ra.GetPin("2"),rb.GetPin("1"),buffer.GetPin("IN+"));builder.Connect(outputNet,buffer.GetPin("OUT"),buffer.GetPin("IN-"));
        builder.Connect("GND",c2.GetPin("2"),r3.GetPin("2"),r5.GetPin("2"),rb.GetPin("2"));builder.Connect("VCC",filterOp.GetPin("V+"),buffer.GetPin("V+"));builder.Connect("VEE",filterOp.GetPin("V-"),buffer.GetPin("V-"));
        hints.Add(new(r1.Reference,sectionIndex,"SkBpInput",0,0));hints.Add(new(c1.Reference,sectionIndex,"SkBpSeries",1,0));hints.Add(new(r2.Reference,sectionIndex,"SkBpFeedback",1,-1));hints.Add(new(c2.Reference,sectionIndex,"SkBpShuntLeft",2,1));hints.Add(new(r3.Reference,sectionIndex,"SkBpShuntRight",2,1));
        hints.Add(new(r4.Reference,sectionIndex,"SkBpGainTop",3,1));hints.Add(new(r5.Reference,sectionIndex,"SkBpGainBottom",3,2));hints.Add(new(filterOp.Reference,sectionIndex,"SkBpOpAmp",3,0));hints.Add(new(ra.Reference,sectionIndex,"SkBpAttenuator",4,0));hints.Add(new(rb.Reference,sectionIndex,"SkBpAttenuatorShunt",4,1));hints.Add(new(buffer.Reference,sectionIndex,"SkBpBuffer",5,0));
    }

    private static void AddActiveFirstOrder(CircuitBuilder builder, ReferenceDesignatorAllocator allocator, List<FilterPlacementHint> hints, SynthesizedSection section, string inputNet, string outputNet, int sectionIndex)
    {
        var r = Add(builder, allocator, section.Components.Single(value => value.Role == "R"));
        var c = Add(builder, allocator, section.Components.Single(value => value.Role == "C"));
        var opAmp = builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));
        var filterNode = $"N_S{sectionIndex}_FILTER";
        if (section.Target.ResponseType == FilterResponseType.LowPass)
        {
            builder.Connect(inputNet, r.GetPin("1")); builder.Connect(filterNode, r.GetPin("2"), c.GetPin("1"), opAmp.GetPin("IN+")); builder.Connect("GND", c.GetPin("2"));
        }
        else
        {
            builder.Connect(inputNet, c.GetPin("1")); builder.Connect(filterNode, c.GetPin("2"), r.GetPin("1"), opAmp.GetPin("IN+")); builder.Connect("GND", r.GetPin("2"));
        }
        builder.Connect(outputNet, opAmp.GetPin("OUT"));
        ConnectNonInvertingGain(builder, allocator, hints, section.Components.ToDictionary(value => value.Role), opAmp, outputNet, sectionIndex, "SectionGain", "RF", "RG");
        builder.Connect("VCC", opAmp.GetPin("V+")); builder.Connect("VEE", opAmp.GetPin("V-"));
        var series = section.Target.ResponseType == FilterResponseType.LowPass ? r : c;
        var shunt = section.Target.ResponseType == FilterResponseType.LowPass ? c : r;
        hints.Add(new(series.Reference, sectionIndex, "Series", 0, 0)); hints.Add(new(shunt.Reference, sectionIndex, "Shunt", 1, 1)); hints.Add(new(opAmp.Reference, sectionIndex, "Buffer", 2, 0));
    }

    private static void AddSallenKeySecondOrder(CircuitBuilder builder, ReferenceDesignatorAllocator allocator, List<FilterPlacementHint> hints, SynthesizedSection section, string inputNet, string outputNet, int sectionIndex)
    {
        var values = section.Components.ToDictionary(value => value.Role);
        var r1 = Add(builder, allocator, values["R1"]); var r2 = Add(builder, allocator, values["R2"]); var c1 = Add(builder, allocator, values["C1"]); var c2 = Add(builder, allocator, values["C2"]);
        var opAmp = builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));
        var nodeA = $"N_S{sectionIndex}_A"; var nodeB = $"N_S{sectionIndex}_B";
        if (section.Target.ResponseType == FilterResponseType.LowPass)
        {
            builder.Connect(inputNet, r1.GetPin("1")); builder.Connect(nodeA, r1.GetPin("2"), r2.GetPin("1"), c2.GetPin("1")); builder.Connect(nodeB, r2.GetPin("2"), c1.GetPin("1"), opAmp.GetPin("IN+")); builder.Connect("GND", c1.GetPin("2")); builder.Connect(outputNet, c2.GetPin("2"), opAmp.GetPin("OUT"));
        }
        else
        {
            builder.Connect(inputNet, c1.GetPin("1")); builder.Connect(nodeA, c1.GetPin("2"), c2.GetPin("1"), r2.GetPin("1")); builder.Connect(nodeB, c2.GetPin("2"), r1.GetPin("1"), opAmp.GetPin("IN+")); builder.Connect("GND", r1.GetPin("2")); builder.Connect(outputNet, r2.GetPin("2"), opAmp.GetPin("OUT"));
        }
        ConnectNonInvertingGain(builder, allocator, hints, values, opAmp, outputNet, sectionIndex, "SectionGain", "RF", "RG");
        builder.Connect("VCC", opAmp.GetPin("V+")); builder.Connect("VEE", opAmp.GetPin("V-"));
        if (section.Target.ResponseType == FilterResponseType.LowPass)
        {
            hints.Add(new(r1.Reference, sectionIndex, "Series1", 0, 0)); hints.Add(new(r2.Reference, sectionIndex, "Series2", 1, 0));
            hints.Add(new(c1.Reference, sectionIndex, "Shunt", 2, 1)); hints.Add(new(c2.Reference, sectionIndex, "Feedback", 1, -1));
        }
        else
        {
            hints.Add(new(c1.Reference, sectionIndex, "Series1", 0, 0)); hints.Add(new(c2.Reference, sectionIndex, "Series2", 1, 0));
            hints.Add(new(r1.Reference, sectionIndex, "Shunt", 2, 1)); hints.Add(new(r2.Reference, sectionIndex, "Feedback", 1, -1));
        }
        hints.Add(new(opAmp.Reference, sectionIndex, "OpAmp", 3, 0));
    }

    private static void ConnectNonInvertingGain(CircuitBuilder builder, ReferenceDesignatorAllocator allocator,
        List<FilterPlacementHint> hints, IReadOnlyDictionary<string, SynthesizedComponentValue> values,
        CircuitComponent opAmp, string outputNet, int sectionIndex, string rolePrefix, string feedbackRole, string shuntRole)
    {
        if (!values.TryGetValue(feedbackRole, out var feedbackValue) || !values.TryGetValue(shuntRole, out var shuntValue))
        {
            builder.Connect(outputNet, opAmp.GetPin("IN-"));
            return;
        }
        var feedback = Add(builder, allocator, feedbackValue);
        var shunt = Add(builder, allocator, shuntValue);
        var invertingNet = $"N_S{sectionIndex}_{rolePrefix.ToUpperInvariant()}_INV";
        builder.Connect(invertingNet, opAmp.GetPin("IN-"), feedback.GetPin("1"), shunt.GetPin("1"));
        builder.Connect(outputNet, feedback.GetPin("2"));
        builder.Connect("GND", shunt.GetPin("2"));
        hints.Add(new(feedback.Reference, sectionIndex, $"{rolePrefix}Feedback", 3, -1));
        hints.Add(new(shunt.Reference, sectionIndex, $"{rolePrefix}Shunt", 3, 1));
    }

    private static IReadOnlyList<SynthesizedComponentValue> AddOutputNormalization(
        CircuitBuilder builder,
        ReferenceDesignatorAllocator allocator,
        List<FilterPlacementHint> hints,
        FilterDesignRequest request,
        double ratio,
        int sectionCount)
    {
        if (ratio >= 1 - 1e-12) return Array.Empty<SynthesizedComponentValue>();
        if (ratio <= 0 || !double.IsFinite(ratio))
            throw new InvalidOperationException("The active prototype output-normalization ratio is invalid.");

        var constraints = request.ComponentConstraints;
        var resistanceRatio = (1 - ratio) / ratio;
        var lowerShunt = Math.Max(constraints.ResistorMinimumOhm, constraints.ResistorMinimumOhm / resistanceRatio);
        var upperShunt = Math.Min(constraints.ResistorMaximumOhm, constraints.ResistorMaximumOhm / resistanceRatio);
        if (lowerShunt > upperShunt)
            throw new InvalidOperationException("Chebyshev even-order output normalization cannot fit the selected resistor range.");
        var theoreticalShunt = Math.Clamp(request.PreferredResistanceOhm, lowerShunt, upperShunt);
        var theoreticalSeries = theoreticalShunt * resistanceRatio;
        var selectedSeries = theoreticalSeries;
        var selectedShunt = theoreticalShunt;
        StandardComponentSeries? series = null;
        if (constraints.OptimizeStandardValues)
        {
            var seriesCandidates = StandardValueSeries.GetNearby(theoreticalSeries, constraints.ResistorSeries,
                constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm, 3);
            var shuntCandidates = StandardValueSeries.GetNearby(theoreticalShunt, constraints.ResistorSeries,
                constraints.ResistorMinimumOhm, constraints.ResistorMaximumOhm, 3);
            var best = seriesCandidates.SelectMany(first => shuntCandidates.Select(second =>
                    (Series: first, Shunt: second, Error: Math.Abs(second / (first + second) - ratio))))
                .MinBy(candidate => candidate.Error);
            selectedSeries = best.Series;
            selectedShunt = best.Shunt;
            series = constraints.ResistorSeries;
        }

        var userAttenuation = request.PassbandGain < 1 - 1e-12;
        var rolePrefix = userAttenuation ? "InterstageAttenuator" : "PrototypeNormalization";
        var values = new[]
        {
            new SynthesizedComponentValue($"{rolePrefix}Series", ComponentType.Resistor,
                ComponentParameterNames.ResistanceOhm, theoreticalSeries, selectedSeries, series, constraints.ResistorTolerancePercent),
            new SynthesizedComponentValue($"{rolePrefix}Shunt", ComponentType.Resistor,
                ComponentParameterNames.ResistanceOhm, theoreticalShunt, selectedShunt, series, constraints.ResistorTolerancePercent)
        };
        var seriesResistor = Add(builder, allocator, values[0]);
        var shuntResistor = Add(builder, allocator, values[1]);
        builder.Connect("N_FILTER_CORE_OUT", seriesResistor.GetPin("1"));
        var dividerOutput = userAttenuation ? "N_OUTPUT_SCALE" : "VOUT";
        builder.Connect(dividerOutput, seriesResistor.GetPin("2"), shuntResistor.GetPin("1"));
        builder.Connect("GND", shuntResistor.GetPin("2"));
        var sectionIndex = Math.Max(1, sectionCount);
        hints.Add(new(seriesResistor.Reference, sectionIndex, $"{rolePrefix}Series", 8, 0));
        hints.Add(new(shuntResistor.Reference, sectionIndex, $"{rolePrefix}Shunt", 8, 1));
        if (userAttenuation)
        {
            var buffer = builder.Add(ComponentFactory.OpAmp(allocator.Next(ComponentType.OpAmp)));
            builder.Connect(dividerOutput, buffer.GetPin("IN+"));
            builder.Connect("VOUT", buffer.GetPin("OUT"), buffer.GetPin("IN-"));
            builder.Connect("VCC", buffer.GetPin("V+")); builder.Connect("VEE", buffer.GetPin("V-"));
            hints.Add(new(buffer.Reference, sectionIndex, "OutputScaleBuffer", 9, 0));
        }
        return values;
    }

    private static FilterCircuitBuildResult BuildPassiveRc(FilterDesignRequest request, IReadOnlyList<SynthesizedSection> sections)
    {
        if (sections.Count != 1)
            throw new NotSupportedException("A topology-based passive RC design must contain one synthesized network record.");
        var builder = new CircuitBuilder(); var allocator = new ReferenceDesignatorAllocator(); var hints = new List<FilterPlacementHint>(); builder.CreateNet("GND");
        var section = sections[0];
        if (request.ResponseType == FilterResponseType.BandPass)
        {
            var values = section.Components.ToDictionary(value => value.Role);
            var cHp = Add(builder, allocator, values["C_HP"]); var rHp = Add(builder, allocator, values["R_HP"]);
            var rLp = Add(builder, allocator, values["R_LP"]); var cLp = Add(builder, allocator, values["C_LP"]);
            builder.Connect("VIN", cHp.GetPin("1"));
            builder.Connect("N_RC_BP_HP", cHp.GetPin("2"), rHp.GetPin("1"), rLp.GetPin("1"));
            builder.Connect("GND", rHp.GetPin("2"), cLp.GetPin("2"));
            builder.Connect("VOUT", rLp.GetPin("2"), cLp.GetPin("1"));
            hints.Add(new(cHp.Reference, 1, "RcBpSeries", 0, 0)); hints.Add(new(rHp.Reference, 1, "RcBpShunt", 2, 2));
            hints.Add(new(rLp.Reference, 1, "RcBpSeries", 4, 0)); hints.Add(new(cLp.Reference, 1, "RcBpShunt", 6, 2));
            var bandGraph = builder.Build(); FilterCircuitGraphValidator.EnsureFilterRequirements(bandGraph, request.Mode); return new(bandGraph, hints);
        }
        if (request.ResponseType == FilterResponseType.BandStop)
        {
            var values = section.Components.ToDictionary(value => value.Role);
            var r1 = Add(builder, allocator, values["R1"]); var r2 = Add(builder, allocator, values["R2"]);
            var rsa = Add(builder, allocator, values["R_SHUNT_A"]); var rsb = Add(builder, allocator, values["R_SHUNT_B"]);
            var c1 = Add(builder, allocator, values["C1"]); var c2 = Add(builder, allocator, values["C2"]);
            var csa = Add(builder, allocator, values["C_SHUNT_A"]); var csb = Add(builder, allocator, values["C_SHUNT_B"]);
            builder.Connect("VIN", r1.GetPin("1"), c1.GetPin("1"));
            builder.Connect("N_TWIN_R", r1.GetPin("2"), r2.GetPin("1"), csa.GetPin("1"), csb.GetPin("1"));
            builder.Connect("N_TWIN_C", c1.GetPin("2"), c2.GetPin("1"), rsa.GetPin("1"), rsb.GetPin("1"));
            builder.Connect("VOUT", r2.GetPin("2"), c2.GetPin("2"));
            builder.Connect("GND", csa.GetPin("2"), csb.GetPin("2"), rsa.GetPin("2"), rsb.GetPin("2"));
            hints.Add(new(r1.Reference, 1, "TwinTSeries", 0, -1)); hints.Add(new(r2.Reference, 1, "TwinTSeries", 3, -1));
            hints.Add(new(c1.Reference, 1, "TwinTSeries", 0, 1)); hints.Add(new(c2.Reference, 1, "TwinTSeries", 3, 1));
            hints.Add(new(csa.Reference, 1, "TwinTShunt", 1, 0)); hints.Add(new(csb.Reference, 1, "TwinTShunt", 2, 1));
            hints.Add(new(rsa.Reference, 1, "TwinTShunt", 1, 3)); hints.Add(new(rsb.Reference, 1, "TwinTShunt", 3, 3));
            var notchGraph = builder.Build(); FilterCircuitGraphValidator.EnsureFilterRequirements(notchGraph, request.Mode); return new(notchGraph, hints);
        }
        if (section.Target.Order != SectionOrder.First)
            throw new NotSupportedException("First-order passive RC LP/HP requires one first-order section.");
        var r = Add(builder, allocator, section.Components.Single(value => value.Role == "R")); var c = Add(builder, allocator, section.Components.Single(value => value.Role == "C"));
        if (request.ResponseType == FilterResponseType.LowPass)
        { builder.Connect("VIN", r.GetPin("1")); builder.Connect("VOUT", r.GetPin("2"), c.GetPin("1")); builder.Connect("GND", c.GetPin("2")); hints.Add(new(r.Reference, 1, "Series", 0, 0)); hints.Add(new(c.Reference, 1, "Shunt", 1, 1)); }
        else
        { builder.Connect("VIN", c.GetPin("1")); builder.Connect("VOUT", c.GetPin("2"), r.GetPin("1")); builder.Connect("GND", r.GetPin("2")); hints.Add(new(c.Reference, 1, "Series", 0, 0)); hints.Add(new(r.Reference, 1, "Shunt", 1, 1)); }
        var graph = builder.Build(); FilterCircuitGraphValidator.EnsureFilterRequirements(graph, request.Mode); return new(graph, hints);
    }

    private static FilterCircuitBuildResult BuildPassiveLc(FilterDesignRequest request, NormalizedFilterSpecification specification, FilterPrototype prototype)
    {
        var ladderPrototype = prototype.LcLadderPrototype ?? throw new NotSupportedException($"{prototype.Approximation} does not provide a verified normalized LC ladder prototype.");
        var impedanceScale = request.SourceResistanceOhm / ladderPrototype.SourceTerminationG0;
        var theoreticalBranches = new PassiveLcTransformationService().Transform(ladderPrototype, request.ResponseType, impedanceScale, specification);
        var branches = new PassiveLcStandardValueOptimizer().Optimize(theoreticalBranches, request.ComponentConstraints, specification,
            candidate => ScoreLoadedLcCandidate(candidate, request));
        return AssemblePassiveLcBranches(branches, request);
    }

    private static double ScoreLoadedLcCandidate(IReadOnlyList<LcRealizationBranch> branches, FilterDesignRequest request)
    {
        var graph = AssemblePassiveLcBranches(branches, request).CircuitGraph;
        var service = new FilterAnalysisService();
        var options = service.CreateOptions(request) with { PointsPerDecade = 4 };
        var analysis = new PassiveFilterAnalysisService().Analyze(graph, request, options).Primary;
        var compliance = new FilterSpecificationComplianceEvaluator().Evaluate(request, analysis);
        double score = 0;
        foreach (var edge in compliance.Edges)
        {
            var violation = edge.IsPassband
                ? Math.Max(0, edge.LimitMagnitudeDb - edge.ActualMagnitudeDb)
                : Math.Max(0, edge.ActualMagnitudeDb - edge.LimitMagnitudeDb);
            score += 100 * violation * violation + .02 * Math.Abs(edge.ActualMagnitudeDb - edge.LimitMagnitudeDb);
        }
        return score;
    }

    private static FilterCircuitBuildResult AssemblePassiveLcBranches(IReadOnlyList<LcRealizationBranch> branches, FilterDesignRequest request)
    {
        var builder = new CircuitBuilder(); var allocator = new ReferenceDesignatorAllocator(); var hints = new List<FilterPlacementHint>(); builder.CreateNet("GND");
        var currentNet = "VIN"; var seriesBranches = branches.Count(item => item.Connection == LcBranchConnection.Series); var seriesPosition = 0; var layoutColumn = 0;
        foreach (var branch in branches)
        {
            if (branch.Connection == LcBranchConnection.Series)
            {
                seriesPosition++;
                var nextNet = seriesPosition == seriesBranches ? "VOUT" : $"N_LADDER_{seriesPosition}";
                AddLcSeriesBranch(builder, allocator, hints, branch, currentNet, nextNet, ref layoutColumn);
                currentNet = nextNet;
            }
            else
            {
                AddLcShuntBranch(builder, allocator, hints, branch, currentNet, ref layoutColumn);
            }
        }
        if (!string.Equals(currentNet, "VOUT", StringComparison.OrdinalIgnoreCase)) builder.CreateNet("VOUT");
        var graph = builder.Build(); FilterCircuitGraphValidator.EnsureFilterRequirements(graph, request.Mode);
        var values=branches.SelectMany(branch=>branch.Parts.Select((part,index)=>new SynthesizedComponentValue($"g{branch.PrototypeIndex}:{part.ComponentType}{index+1}",part.ComponentType,part.ComponentType==ComponentType.Inductor?ComponentParameterNames.InductanceHenry:ComponentParameterNames.CapacitanceFarad,part.TheoreticalValue,part.Value,part.SelectedSeries,part.TolerancePercent))).ToArray();
        return new(graph, hints, values);
    }

    private static void AddLcSeriesBranch(CircuitBuilder builder, ReferenceDesignatorAllocator allocator, List<FilterPlacementHint> hints, LcRealizationBranch branch, string inputNet, string outputNet, ref int column)
    {
        if (branch.PartsInParallel)
        {
            foreach (var (part, offset) in branch.Parts.Select((value, index) => (value, index)))
            {
                var component = AddLcPart(builder, allocator, part); builder.Connect(inputNet, component.GetPin("1")); builder.Connect(outputNet, component.GetPin("2"));
                hints.Add(new(component.Reference, 1, offset == 0 ? "ParallelSeriesTop" : "ParallelSeriesBottom", column, offset == 0 ? -1 : 1));
            }
            column += 2; return;
        }
        var current = inputNet;
        for (var index = 0; index < branch.Parts.Count; index++)
        {
            var next = index == branch.Parts.Count - 1 ? outputNet : $"N_LC_{branch.PrototypeIndex}_{index + 1}";
            var component = AddLcPart(builder, allocator, branch.Parts[index]); builder.Connect(current, component.GetPin("1")); builder.Connect(next, component.GetPin("2"));
            hints.Add(new(component.Reference, 1, "SeriesElement", column++, 0)); current = next;
        }
    }

    private static void AddLcShuntBranch(CircuitBuilder builder, ReferenceDesignatorAllocator allocator, List<FilterPlacementHint> hints, LcRealizationBranch branch, string signalNet, ref int column)
    {
        if (branch.PartsInParallel)
        {
            foreach (var (part, offset) in branch.Parts.Select((value, index) => (value, index)))
            {
                var component = AddLcPart(builder, allocator, part); builder.Connect(signalNet, component.GetPin("1")); builder.Connect("GND", component.GetPin("2"));
                hints.Add(new(component.Reference, 1, offset == 0 ? "ParallelShuntLeft" : "ParallelShuntRight", column, 1));
            }
            column += 1; return;
        }
        var current = signalNet;
        for (var index = 0; index < branch.Parts.Count; index++)
        {
            var next = index == branch.Parts.Count - 1 ? "GND" : $"N_LC_SHUNT_{branch.PrototypeIndex}_{index + 1}";
            var component = AddLcPart(builder, allocator, branch.Parts[index]); builder.Connect(current, component.GetPin("1")); builder.Connect(next, component.GetPin("2"));
            hints.Add(new(component.Reference, 1, index == 0 ? "ShuntSeriesUpper" : "ShuntSeriesLower", column, index + 1)); current = next;
        }
        column += 1;
    }

    private static CircuitComponent AddLcPart(CircuitBuilder builder, ReferenceDesignatorAllocator allocator, LcRealizationPart part) => part.ComponentType switch
    {
        ComponentType.Inductor => builder.Add(ComponentFactory.Inductor(allocator.Next(ComponentType.Inductor), part.Value)),
        ComponentType.Capacitor => builder.Add(ComponentFactory.Capacitor(allocator.Next(ComponentType.Capacitor), part.Value)),
        _ => throw new NotSupportedException($"LC realization cannot create {part.ComponentType}.")
    };

    private static CircuitComponent Add(CircuitBuilder builder, ReferenceDesignatorAllocator allocator, SynthesizedComponentValue value) => value.ComponentType switch
    {
        ComponentType.Resistor => builder.Add(ComponentFactory.Resistor(allocator.Next(ComponentType.Resistor), value.SelectedValue)),
        ComponentType.Capacitor => builder.Add(ComponentFactory.Capacitor(allocator.Next(ComponentType.Capacitor), value.SelectedValue)),
        ComponentType.Inductor => builder.Add(ComponentFactory.Inductor(allocator.Next(ComponentType.Inductor), value.SelectedValue)),
        _ => throw new NotSupportedException($"Cannot create synthesized component type {value.ComponentType}.")
    };
}
