using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Core.Schematic.Symbols;

namespace CircuitModuleDesigner.Modules.Comparators.Schematic;

public sealed class ComparatorSchematicLayoutService
{
    public SchematicDocument Layout(CircuitGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.Components.Any(component => component.Reference.Equals("UOUT", StringComparison.OrdinalIgnoreCase))
            ? LayoutWindow(graph)
            : LayoutSchmitt(graph);
    }

    private static SchematicDocument LayoutSchmitt(CircuitGraph graph)
    {
        if (!graph.Components.Any(component => component.Reference.Equals("RFB", StringComparison.OrdinalIgnoreCase)))
            return LayoutZeroHysteresis(graph);
        var document = new SchematicDocument(graph);
        var placed = new Dictionary<string, PlacedComponent>(StringComparer.OrdinalIgnoreCase);
        var inputResistor = graph.Components.Any(component => component.Reference == "RREF") ? "RREF" : "RIN";
        var inverting = inputResistor == "RREF";
        var lowRail = graph.Nets.Any(net => net.Name == "VEE") ? "VEE" : "GND";

        Put("U1", 720, 360);
        Put("RFB", 650, 120, ComponentOrientation.Left);
        Put(inputResistor, 480, 338);
        Put("RVT", 250, 120, ComponentOrientation.Down);
        Put("RVB", 250, 300, ComponentOrientation.Down);

        var dividerTop = Pin("RVT", "2");
        var dividerBottom = Pin("RVB", "1");
        var dividerNode = new SchematicPoint(dividerTop.X, (dividerTop.Y + dividerBottom.Y) / 2);
        Wire("NBIAS", dividerTop, dividerBottom);
        document.AddJunction(new("NBIAS", dividerNode));

        var positiveInput = Pin("U1", "IN+");
        var negativeInput = Pin("U1", "IN-");
        var feedbackNode = new SchematicPoint(600, positiveInput.Y);
        var feedbackUpper = new SchematicPoint(feedbackNode.X, Pin("RFB", "2").Y);

        if (inverting)
        {
            Wire("NBIAS", dividerNode, new(400, dividerNode.Y), new(400, Pin("RREF", "1").Y), Pin("RREF", "1"));
            Wire("NTH", Pin("RREF", "2"), positiveInput);
            Wire("NTH", feedbackNode, feedbackUpper, Pin("RFB", "2"));
            document.AddJunction(new("NTH", feedbackNode));
            ExternalLeft("VIN", negativeInput, 105);
        }
        else
        {
            ExternalLeft("VIN", Pin("RIN", "1"), 105);
            Wire("NPOS", Pin("RIN", "2"), positiveInput);
            Wire("NPOS", feedbackNode, feedbackUpper, Pin("RFB", "2"));
            document.AddJunction(new("NPOS", feedbackNode));
            Wire("NBIAS", dividerNode, new(555, dividerNode.Y), new(555, negativeInput.Y), negativeInput);
        }

        var output = Pin("U1", "OUT");
        var outputBranch = new SchematicPoint(850, output.Y);
        Wire("VOUT", Pin("RFB", "1"), new(outputBranch.X, Pin("RFB", "1").Y), outputBranch);
        Wire("VOUT", output, new(950, output.Y));
        document.AddJunction(new("VOUT", outputBranch));
        Label("VOUT", new(950, output.Y));

        SupplyStub("U1", "V+", "VCC", 55);
        SupplyStub("U1", "V-", lowRail, 55);
        SupplyStub("RVT", "1", "VCC", 42);
        SupplyStub("RVB", "2", lowRail, 42);
        return document;

        void Put(string reference, double x, double y, ComponentOrientation orientation = ComponentOrientation.Right)
        {
            var item = new PlacedComponent(graph.GetComponent(reference), new(x, y), orientation);
            placed.Add(reference, item);
            document.AddPlacedComponent(item);
        }

        SchematicPoint Pin(string reference, string pin) => SchematicGeometry.GetWorldPin(placed[reference], pin);
        void Wire(string net, params SchematicPoint[] points) => document.AddWire(new(net, points));
        void Label(string net, SchematicPoint point) => document.AddNetLabel(new(net, net, point));

        void ExternalLeft(string net, SchematicPoint start, double length)
        {
            var end = new SchematicPoint(start.X - length, start.Y);
            Wire(net, start, end);
            Label(net, end);
        }

        void SupplyStub(string reference, string pin, string net, double length)
        {
            var start = Pin(reference, pin);
            var direction = SchematicGeometry.GetWorldPinDirection(placed[reference], pin);
            var end = direction == SymbolPinDirection.Up
                ? new SchematicPoint(start.X, start.Y - length)
                : new SchematicPoint(start.X, start.Y + length);
            Wire(net, start, end);
            Label(net, end);
        }
    }

    private static SchematicDocument LayoutZeroHysteresis(CircuitGraph graph)
    {
        var document = new SchematicDocument(graph);
        var placed = new Dictionary<string, PlacedComponent>(StringComparer.OrdinalIgnoreCase);
        var lowRail = graph.Nets.Any(net => net.Name == "VEE") ? "VEE" : "GND";
        var inverting = graph.GetNetForPin("U1", "IN-")?.Name.Equals("VIN", StringComparison.OrdinalIgnoreCase) == true;

        Put("U1", 720, 330);
        Put("RVT", 300, 110, ComponentOrientation.Down);
        Put("RVB", 300, 290, ComponentOrientation.Down);

        var dividerTop = Pin("RVT", "2");
        var dividerBottom = Pin("RVB", "1");
        var dividerNode = new SchematicPoint(dividerTop.X, (dividerTop.Y + dividerBottom.Y) / 2);
        Wire("NBIAS", dividerTop, dividerBottom);
        var biasInput = Pin("U1", inverting ? "IN+" : "IN-");
        Wire("NBIAS", dividerNode, new(560, dividerNode.Y), new(560, biasInput.Y), biasInput);
        document.AddJunction(new("NBIAS", dividerNode));

        var signalInput = Pin("U1", inverting ? "IN-" : "IN+");
        var vinEnd = new SchematicPoint(signalInput.X - 150, signalInput.Y);
        Wire("VIN", signalInput, vinEnd); Label("VIN", vinEnd);
        var output = Pin("U1", "OUT");
        var outputEnd = new SchematicPoint(output.X + 150, output.Y);
        Wire("VOUT", output, outputEnd); Label("VOUT", outputEnd);

        SupplyStub("U1", "V+", "VCC", 55);
        SupplyStub("U1", "V-", lowRail, 55);
        SupplyStub("RVT", "1", "VCC", 42);
        SupplyStub("RVB", "2", lowRail, 42);
        return document;

        void Put(string reference, double x, double y, ComponentOrientation orientation = ComponentOrientation.Right)
        {
            var item = new PlacedComponent(graph.GetComponent(reference), new(x, y), orientation);
            placed.Add(reference, item); document.AddPlacedComponent(item);
        }
        SchematicPoint Pin(string reference, string pin) => SchematicGeometry.GetWorldPin(placed[reference], pin);
        void Wire(string net, params SchematicPoint[] points) => document.AddWire(new(net, points));
        void Label(string net, SchematicPoint point) => document.AddNetLabel(new(net, net, point));
        void SupplyStub(string reference, string pin, string net, double length)
        {
            var start = Pin(reference, pin);
            var direction = SchematicGeometry.GetWorldPinDirection(placed[reference], pin);
            var end = direction == SymbolPinDirection.Up
                ? new SchematicPoint(start.X, start.Y - length)
                : new SchematicPoint(start.X, start.Y + length);
            Wire(net, start, end); Label(net, end);
        }
    }

    private static SchematicDocument LayoutWindow(CircuitGraph graph)
    {
        var document = new SchematicDocument(graph);
        var placed = new Dictionary<string, PlacedComponent>(StringComparer.OrdinalIgnoreCase);
        var lowRail = graph.Nets.Any(net => net.Name == "VEE") ? "VEE" : "GND";
        var plusSourceNet = graph.GetNetForPin("RPS", "1")!.Name;
        var topComparator = plusSourceNet.Equals("NLOW", StringComparison.OrdinalIgnoreCase) ? "ULOW" : "UHIGH";
        var bottomComparator = topComparator == "ULOW" ? "UHIGH" : "ULOW";

        Put(topComparator, 520, 260);
        Put(bottomComparator, 520, 620);
        Put("UOUT", 1100, 440);
        PlaceBoundary(topComparator, 130, 350);
        PlaceBoundary(bottomComparator, 490, 710);
        PlaceFeedback(topComparator, 100);
        PlaceFeedback(bottomComparator, 800);
        Put("RPS", 760, 260);
        Put("RMS", 760, 620);
        Put("RPB", 980, 338, ComponentOrientation.Down);
        Put("RMB", 980, 542, ComponentOrientation.Up);

        RouteBoundary(topComparator);
        RouteBoundary(bottomComparator);
        RouteComparatorOutput(topComparator, "RPS", 260);
        RouteComparatorOutput(bottomComparator, "RMS", 620);

        var plusInput = Pin("UOUT", "IN+");
        var minusInput = Pin("UOUT", "IN-");
        Wire("NCOMB_P", Pin("RPS", "2"), new(900, 260), new(900, plusInput.Y), plusInput);
        Wire("NCOMB_P", Pin("RPB", "2"), new(Pin("RPB", "2").X, plusInput.Y));
        document.AddJunction(new("NCOMB_P", new(Pin("RPB", "2").X, plusInput.Y)));
        Wire("NCOMB_M", Pin("RMS", "2"), new(930, 620), new(930, minusInput.Y), minusInput);
        Wire("NCOMB_M", Pin("RMB", "2"), new(Pin("RMB", "2").X, minusInput.Y));
        document.AddJunction(new("NCOMB_M", new(Pin("RMB", "2").X, minusInput.Y)));

        var topInput = Pin(topComparator, "IN-");
        var bottomInput = Pin(bottomComparator, "IN-");
        Wire("VIN", new(150, topInput.Y), new(150, bottomInput.Y));
        Wire("VIN", new(150, topInput.Y), topInput);
        Wire("VIN", new(150, bottomInput.Y), bottomInput);
        var vinLabel = new SchematicPoint(85, (topInput.Y + bottomInput.Y) / 2);
        Wire("VIN", new(150, vinLabel.Y), vinLabel);
        Label("VIN", vinLabel);
        document.AddJunction(new("VIN", new(150, vinLabel.Y)));

        var output = Pin("UOUT", "OUT");
        var outputEnd = new SchematicPoint(1260, output.Y);
        Wire("VOUT", output, outputEnd);
        Label("VOUT", outputEnd);

        foreach (var comparator in new[] { "ULOW", "UHIGH", "UOUT" })
        {
            SupplyStub(comparator, "V+", "VCC", 42);
            SupplyStub(comparator, "V-", lowRail, 42);
        }

        RailStub("RPB", "1", 42);
        RailStub("RMB", "1", 42);
        foreach (var suffix in new[] { "L", "U" })
        {
            SupplyStub("RT" + suffix, "1", "VCC", 42);
            SupplyStub("RB" + suffix, "2", lowRail, 42);
        }

        return document;

        bool Has(string reference) => graph.Components.Any(component => component.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase));
        string Suffix(string comparator) => comparator == "ULOW" ? "L" : "U";
        string OutputNet(string comparator) => comparator == "ULOW" ? "NLOW" : "NHIGH";

        void Put(string reference, double x, double y, ComponentOrientation orientation = ComponentOrientation.Right)
        {
            var item = new PlacedComponent(graph.GetComponent(reference), new(x, y), orientation);
            placed.Add(reference, item);
            document.AddPlacedComponent(item);
        }

        void PlaceBoundary(string comparator, double topY, double bottomY)
        {
            var suffix = Suffix(comparator);
            var dividerX = suffix == "L" ? 270d : 360d;
            Put("RT" + suffix, dividerX, topY, ComponentOrientation.Down);
            Put("RB" + suffix, dividerX, bottomY, ComponentOrientation.Down);
        }

        void PlaceFeedback(string comparator, double y)
        {
            var reference = "RF" + Suffix(comparator);
            if (Has(reference)) Put(reference, 620, y, ComponentOrientation.Left);
        }

        SchematicPoint Pin(string reference, string pin) => SchematicGeometry.GetWorldPin(placed[reference], pin);
        void Wire(string net, params SchematicPoint[] points) => document.AddWire(new(net, points));
        void Label(string net, SchematicPoint point) => document.AddNetLabel(new(net, net, point));

        void RouteBoundary(string comparator)
        {
            var suffix = Suffix(comparator);
            var net = "NREF_" + suffix;
            var top = "RT" + suffix;
            var bottom = "RB" + suffix;
            var referenceInput = Pin(comparator, "IN+");
            var dividerNode = new SchematicPoint(Pin(top, "2").X, referenceInput.Y);
            Wire(net, Pin(top, "2"), Pin(bottom, "1"));
            Wire(net, dividerNode, referenceInput);
            document.AddJunction(new(net, dividerNode));

            var feedback = "RF" + suffix;
            if (!Has(feedback)) return;
            var branchX = 440d;
            Wire(net, new(branchX, referenceInput.Y), new(branchX, Pin(feedback, "2").Y), Pin(feedback, "2"));
            document.AddJunction(new(net, new(branchX, referenceInput.Y)));
        }

        void RouteComparatorOutput(string comparator, string signalResistor, double signalY)
        {
            var net = OutputNet(comparator);
            Wire(net, Pin(comparator, "OUT"), Pin(signalResistor, "1"));
            var feedback = "RF" + Suffix(comparator);
            if (!Has(feedback)) return;
            var branchX = 680d;
            Wire(net, new(branchX, signalY), new(branchX, Pin(feedback, "1").Y), Pin(feedback, "1"));
            document.AddJunction(new(net, new(branchX, signalY)));
        }

        void SupplyStub(string reference, string pin, string net, double length)
        {
            var start = Pin(reference, pin);
            var direction = SchematicGeometry.GetWorldPinDirection(placed[reference], pin);
            var end = direction == SymbolPinDirection.Up
                ? new SchematicPoint(start.X, start.Y - length)
                : new SchematicPoint(start.X, start.Y + length);
            Wire(net, start, end);
            Label(net, end);
        }

        void RailStub(string reference, string pin, double length)
        {
            var net = graph.GetNetForPin(reference, pin)!.Name;
            SupplyStub(reference, pin, net, length);
        }
    }
}
