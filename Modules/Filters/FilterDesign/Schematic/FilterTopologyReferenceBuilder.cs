using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic;

public sealed class FilterTopologyReferenceBuilder
{
    private readonly FilterSchematicLayoutService layoutService = new();

    public SchematicDocument Build(FilterMode mode, FilterImplementationType implementation) =>
        mode == FilterMode.Active
            ? implementation switch
            {
                FilterImplementationType.MultipleFeedback => BuildMfb(FilterResponseType.LowPass),
                FilterImplementationType.BainterNotch => BuildBainter(),
                _ => BuildSallenKey(FilterResponseType.LowPass)
            }
            : implementation == FilterImplementationType.Lc ? BuildLc(FilterResponseType.LowPass) : BuildRc(FilterResponseType.LowPass);

    private static SchematicDocument BuildBainter()
    {
        var state = new FilterDesignState
        {
            Mode = FilterMode.Active,
            ResponseType = FilterResponseType.BandStop,
            Implementation = FilterImplementationType.BainterNotch,
            OrderMode = FilterOrderMode.SpecifyOrder,
            SpecifiedOrder = 1,
            PassbandLowerHz = 500,
            StopbandLowerHz = 750,
            StopbandUpperHz = 1_000,
            PassbandUpperHz = 1_500,
            OptimizeStandardValues = false
        };
        var result = new FilterDesignEngine().Design(FilterDesignRequest.FromState(state));
        return result.SchematicDocument ?? throw new InvalidOperationException(result.Message);
    }

    private SchematicDocument BuildMfb(FilterResponseType response)
    {
        var builder = new CircuitBuilder(); var hints = new List<FilterPlacementHint>();
        var opAmp = builder.Add(ComponentFactory.OpAmp("U1")); builder.CreateNet("VCC"); builder.CreateNet("VEE");
        CircuitComponent input, toInput, shunt, feedbackTop, feedbackBottom;
        if (response == FilterResponseType.BandPass)
        {
            input = builder.Add(ComponentFactory.Resistor("R1", 10_000)); feedbackBottom = builder.Add(ComponentFactory.Resistor("R2", 20_000));
            toInput = builder.Add(ComponentFactory.Capacitor("C1", 10e-9)); feedbackTop = builder.Add(ComponentFactory.Capacitor("C2", 10e-9)); shunt = builder.Add(ComponentFactory.Resistor("R3", 10_000));
        }
        else if (response == FilterResponseType.HighPass)
        {
            input = builder.Add(ComponentFactory.Capacitor("C1", 10e-9)); feedbackBottom = builder.Add(ComponentFactory.Resistor("R2", 10_000));
            toInput = builder.Add(ComponentFactory.Capacitor("C3", 10e-9)); feedbackTop = builder.Add(ComponentFactory.Capacitor("C4", 10e-9)); shunt = builder.Add(ComponentFactory.Resistor("R5", 10_000));
        }
        else
        {
            input = builder.Add(ComponentFactory.Resistor("R1", 10_000)); feedbackTop = builder.Add(ComponentFactory.Resistor("R2", 10_000));
            toInput = builder.Add(ComponentFactory.Resistor("R3", 5_000)); feedbackBottom = builder.Add(ComponentFactory.Capacitor("C1", 10e-9)); shunt = builder.Add(ComponentFactory.Capacitor("C2", 40e-9));
        }
        builder.Connect("VIN", input.GetPin("1"));
        builder.Connect("N_SUM", input.GetPin("2"), feedbackTop.GetPin("1"), toInput.GetPin("1"), shunt.GetPin("1"));
        builder.Connect("N_INV", toInput.GetPin("2"), feedbackBottom.GetPin("1"), opAmp.GetPin("IN-"));
        builder.Connect("VOUT", feedbackTop.GetPin("2"), feedbackBottom.GetPin("2"), opAmp.GetPin("OUT"));
        builder.Connect("GND", shunt.GetPin("2"), opAmp.GetPin("IN+"));
        builder.Connect("VCC", opAmp.GetPin("V+")); builder.Connect("VEE", opAmp.GetPin("V-"));
        hints.Add(new(input.Reference, 1, "MfbInput", 0, 0)); hints.Add(new(toInput.Reference, 1, "MfbToInput", 1, 0));
        hints.Add(new(shunt.Reference, 1, "MfbShunt", 1, 1)); hints.Add(new(feedbackTop.Reference, 1, "MfbFeedbackTop", 1, -1));
        hints.Add(new(feedbackBottom.Reference, 1, "MfbFeedbackBottom", 2, 1)); hints.Add(new(opAmp.Reference, 1, "MfbOpAmp", 3, 0));
        return layoutService.Layout(new FilterCircuitBuildResult(builder.Build(), hints));
    }

    private SchematicDocument BuildSallenKey(FilterResponseType response)
    {
        if (response == FilterResponseType.BandPass) return BuildSallenKeyBandPass();
        var effectiveResponse = response is FilterResponseType.LowPass or FilterResponseType.HighPass ? response : FilterResponseType.LowPass;
        var builder = new CircuitBuilder(); var hints = new List<FilterPlacementHint>();
        var r1 = builder.Add(ComponentFactory.Resistor("R1", 10_000)); var r2 = builder.Add(ComponentFactory.Resistor("R2", 10_000));
        var c1 = builder.Add(ComponentFactory.Capacitor("C1", 10e-9)); var c2 = builder.Add(ComponentFactory.Capacitor("C2", 10e-9));
        var opAmp = builder.Add(ComponentFactory.OpAmp("U1")); builder.CreateNet("GND"); builder.CreateNet("VCC"); builder.CreateNet("VEE");
        if (effectiveResponse == FilterResponseType.LowPass)
        {
            builder.Connect("VIN", r1.GetPin("1")); builder.Connect("N1", r1.GetPin("2"), r2.GetPin("1"), c2.GetPin("1"));
            builder.Connect("N2", r2.GetPin("2"), c1.GetPin("1"), opAmp.GetPin("IN+")); builder.Connect("GND", c1.GetPin("2"));
            builder.Connect("VOUT", c2.GetPin("2"), opAmp.GetPin("OUT"), opAmp.GetPin("IN-"));
            hints.Add(new(r1.Reference, 1, "Series1", 0, 0)); hints.Add(new(r2.Reference, 1, "Series2", 1, 0)); hints.Add(new(c1.Reference, 1, "Shunt", 2, 1)); hints.Add(new(c2.Reference, 1, "Feedback", 1, -1));
        }
        else
        {
            builder.Connect("VIN", c1.GetPin("1")); builder.Connect("N1", c1.GetPin("2"), c2.GetPin("1"), r2.GetPin("1"));
            builder.Connect("N2", c2.GetPin("2"), r1.GetPin("1"), opAmp.GetPin("IN+")); builder.Connect("GND", r1.GetPin("2"));
            builder.Connect("VOUT", r2.GetPin("2"), opAmp.GetPin("OUT"), opAmp.GetPin("IN-"));
            hints.Add(new(c1.Reference, 1, "Series1", 0, 0)); hints.Add(new(c2.Reference, 1, "Series2", 1, 0)); hints.Add(new(r1.Reference, 1, "Shunt", 2, 1)); hints.Add(new(r2.Reference, 1, "Feedback", 1, -1));
        }
        builder.Connect("VCC", opAmp.GetPin("V+")); builder.Connect("VEE", opAmp.GetPin("V-")); hints.Add(new(opAmp.Reference, 1, "OpAmp", 3, 0));
        return layoutService.Layout(new FilterCircuitBuildResult(builder.Build(), hints));
    }

    private SchematicDocument BuildSallenKeyBandPass()
    {
        var b=new CircuitBuilder();var h=new List<FilterPlacementHint>();b.CreateNet("VCC");b.CreateNet("VEE");
        var r1=b.Add(ComponentFactory.Resistor("R1",10_000));var r2=b.Add(ComponentFactory.Resistor("R2",3_333));var r3=b.Add(ComponentFactory.Resistor("R3",20_000));var r4=b.Add(ComponentFactory.Resistor("R4",10_000));var r5=b.Add(ComponentFactory.Resistor("R5",12_000));var ra=b.Add(ComponentFactory.Resistor("R6",3_000));var rb=b.Add(ComponentFactory.Resistor("R7",10_000));var c1=b.Add(ComponentFactory.Capacitor("C1",10e-9));var c2=b.Add(ComponentFactory.Capacitor("C2",5e-9));var u1=b.Add(ComponentFactory.OpAmp("U1"));var u2=b.Add(ComponentFactory.OpAmp("U2"));
        b.Connect("VIN",r1.GetPin("1"));b.Connect("N_A",r1.GetPin("2"),c1.GetPin("1"),r2.GetPin("1"));b.Connect("N_B",c1.GetPin("2"),c2.GetPin("1"),r3.GetPin("1"),u1.GetPin("IN+"));b.Connect("N_RAW",r2.GetPin("2"),r4.GetPin("1"),u1.GetPin("OUT"),ra.GetPin("1"));b.Connect("N_INV",r4.GetPin("2"),r5.GetPin("1"),u1.GetPin("IN-"));b.Connect("N_NORM",ra.GetPin("2"),rb.GetPin("1"),u2.GetPin("IN+"));b.Connect("VOUT",u2.GetPin("OUT"),u2.GetPin("IN-"));b.Connect("GND",c2.GetPin("2"),r3.GetPin("2"),r5.GetPin("2"),rb.GetPin("2"));b.Connect("VCC",u1.GetPin("V+"),u2.GetPin("V+"));b.Connect("VEE",u1.GetPin("V-"),u2.GetPin("V-"));
        h.Add(new(r1.Reference,1,"SkBpInput",0,0));h.Add(new(c1.Reference,1,"SkBpSeries",1,0));h.Add(new(r2.Reference,1,"SkBpFeedback",1,-1));h.Add(new(c2.Reference,1,"SkBpShuntLeft",2,1));h.Add(new(r3.Reference,1,"SkBpShuntRight",2,1));h.Add(new(r4.Reference,1,"SkBpGainTop",3,1));h.Add(new(r5.Reference,1,"SkBpGainBottom",3,2));h.Add(new(u1.Reference,1,"SkBpOpAmp",3,0));h.Add(new(ra.Reference,1,"SkBpAttenuator",4,0));h.Add(new(rb.Reference,1,"SkBpAttenuatorShunt",4,1));h.Add(new(u2.Reference,1,"SkBpBuffer",5,0));
        return layoutService.Layout(new FilterCircuitBuildResult(b.Build(),h));
    }

    private SchematicDocument BuildRc(FilterResponseType response)
    {
        var highPass = response == FilterResponseType.HighPass;
        var builder = new CircuitBuilder(); var hints = new List<FilterPlacementHint>(); builder.CreateNet("GND");
        var resistor = builder.Add(ComponentFactory.Resistor("R1", 10_000)); var capacitor = builder.Add(ComponentFactory.Capacitor("C1", 10e-9));
        var series = highPass ? capacitor : resistor; var shunt = highPass ? resistor : capacitor;
        builder.Connect("VIN", series.GetPin("1")); builder.Connect("VOUT", series.GetPin("2"), shunt.GetPin("1")); builder.Connect("GND", shunt.GetPin("2"));
        hints.Add(new(series.Reference, 1, "Series", 0, 0)); hints.Add(new(shunt.Reference, 1, "Shunt", 1, 1));
        return layoutService.Layout(new FilterCircuitBuildResult(builder.Build(), hints));
    }

    private SchematicDocument BuildLc(FilterResponseType response)
    {
        var builder = new CircuitBuilder(); var hints = new List<FilterPlacementHint>(); builder.CreateNet("GND");
        switch (response)
        {
            case FilterResponseType.HighPass:
                AddSimpleLcLadder(builder, hints, ComponentType.Capacitor, ComponentType.Inductor, ComponentType.Capacitor);
                break;
            case FilterResponseType.BandPass:
                AddBandPassLc(builder, hints);
                break;
            case FilterResponseType.BandStop:
                AddBandStopLc(builder, hints);
                break;
            default:
                AddSimpleLcLadder(builder, hints, ComponentType.Inductor, ComponentType.Capacitor, ComponentType.Inductor);
                break;
        }
        return layoutService.Layout(new FilterCircuitBuildResult(builder.Build(), hints));
    }

    private static void AddSimpleLcLadder(CircuitBuilder builder, List<FilterPlacementHint> hints, ComponentType first, ComponentType shuntType, ComponentType last)
    {
        var a = Add(builder, first, "1"); var shunt = Add(builder, shuntType, "1"); var b = Add(builder, last, "2");
        builder.Connect("VIN", a.GetPin("1")); builder.Connect("N1", a.GetPin("2"), shunt.GetPin("1"), b.GetPin("1")); builder.Connect("GND", shunt.GetPin("2")); builder.Connect("VOUT", b.GetPin("2"));
        hints.Add(new(a.Reference, 1, "SeriesElement", 0, 0)); hints.Add(new(shunt.Reference, 1, "ShuntSeriesUpper", 1, 1)); hints.Add(new(b.Reference, 1, "SeriesElement", 2, 0));
    }

    private static void AddBandPassLc(CircuitBuilder builder, List<FilterPlacementHint> hints)
    {
        var l1 = Add(builder, ComponentType.Inductor, "1"); var c1 = Add(builder, ComponentType.Capacitor, "1");
        var l2 = Add(builder, ComponentType.Inductor, "2"); var c2 = Add(builder, ComponentType.Capacitor, "2");
        var l3 = Add(builder, ComponentType.Inductor, "3"); var c3 = Add(builder, ComponentType.Capacitor, "3");
        builder.Connect("VIN", l1.GetPin("1")); builder.Connect("N_SER1", l1.GetPin("2"), c1.GetPin("1")); builder.Connect("N1", c1.GetPin("2"), l2.GetPin("1"), c2.GetPin("1"), l3.GetPin("1"));
        builder.Connect("GND", l2.GetPin("2"), c2.GetPin("2")); builder.Connect("N_SER2", l3.GetPin("2"), c3.GetPin("1")); builder.Connect("VOUT", c3.GetPin("2"));
        hints.Add(new(l1.Reference,1,"SeriesElement",0,0)); hints.Add(new(c1.Reference,1,"SeriesElement",1,0)); hints.Add(new(l2.Reference,1,"ParallelShuntLeft",2,1)); hints.Add(new(c2.Reference,1,"ParallelShuntRight",2,1)); hints.Add(new(l3.Reference,1,"SeriesElement",3,0)); hints.Add(new(c3.Reference,1,"SeriesElement",4,0));
    }

    private static void AddBandStopLc(CircuitBuilder builder, List<FilterPlacementHint> hints)
    {
        var l1 = Add(builder, ComponentType.Inductor, "1"); var c1 = Add(builder, ComponentType.Capacitor, "1");
        var l2 = Add(builder, ComponentType.Inductor, "2"); var c2 = Add(builder, ComponentType.Capacitor, "2");
        var l3 = Add(builder, ComponentType.Inductor, "3"); var c3 = Add(builder, ComponentType.Capacitor, "3");
        builder.Connect("VIN", l1.GetPin("1"), c1.GetPin("1")); builder.Connect("N1", l1.GetPin("2"), c1.GetPin("2"), l2.GetPin("1"), l3.GetPin("1"), c3.GetPin("1"));
        builder.Connect("N_SHUNT", l2.GetPin("2"), c2.GetPin("1")); builder.Connect("GND", c2.GetPin("2")); builder.Connect("VOUT", l3.GetPin("2"), c3.GetPin("2"));
        hints.Add(new(l1.Reference,1,"ParallelSeriesTop",0,-1)); hints.Add(new(c1.Reference,1,"ParallelSeriesBottom",0,1)); hints.Add(new(l2.Reference,1,"ShuntSeriesUpper",2,1)); hints.Add(new(c2.Reference,1,"ShuntSeriesLower",2,2)); hints.Add(new(l3.Reference,1,"ParallelSeriesTop",3,-1)); hints.Add(new(c3.Reference,1,"ParallelSeriesBottom",3,1));
    }

    private static CircuitComponent Add(CircuitBuilder builder, ComponentType type, string number) => type switch
    {
        ComponentType.Resistor => builder.Add(ComponentFactory.Resistor($"R{number}", 10_000)),
        ComponentType.Capacitor => builder.Add(ComponentFactory.Capacitor($"C{number}", 10e-9)),
        ComponentType.Inductor => builder.Add(ComponentFactory.Inductor($"L{number}", 1e-3)),
        _ => throw new NotSupportedException()
    };
}
