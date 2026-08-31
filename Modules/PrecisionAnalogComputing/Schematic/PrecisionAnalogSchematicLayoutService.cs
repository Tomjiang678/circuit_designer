using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Schematic;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Schematic;

/// <summary>Operation-aware left-to-right layout for affine summing circuits.</summary>
public sealed class PrecisionAnalogSchematicLayoutService
{
    public SchematicDocument Layout(CircuitGraph graph)
    {
        var document = new SchematicDocument(graph);
        var placed = new Dictionary<string, PlacedComponent>(StringComparer.OrdinalIgnoreCase);
        var opAmps = graph.Components.Where(component => component.Type == ComponentType.OpAmp).OrderBy(component => component.Reference).ToArray();
        var opAmpX = opAmps.Length == 1 ? new[] { 650d } : new[] { 500d, 1_020d };
        const double opAmpY = 380;
        for (var index = 0; index < opAmps.Length; index++) Place(opAmps[index], new(opAmpX[index], opAmpY));

        var inputRow = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var resistor in graph.Components.Where(component => component.Type == ComponentType.Resistor).OrderBy(component => NumericSuffix(component.Reference)))
        {
            var net1 = Net(resistor, "1");
            var net2 = Net(resistor, "2");
            var sum = new[] { net1, net2 }.FirstOrDefault(name => name.StartsWith("SUM", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("_PLUS", StringComparison.OrdinalIgnoreCase));
            var plus = new[] { net1, net2 }.FirstOrDefault(name => name.EndsWith("_PLUS", StringComparison.OrdinalIgnoreCase));
            var output = new[] { net1, net2 }.FirstOrDefault(IsOutputNet);
            var stage = sum == "SUM_1" || plus == "SUM_1_PLUS" ? 0 : opAmps.Length - 1;
            var isFeedback = (sum == "SUM_1" && output == "STAGE_1_OUT") || (sum == "SUM_OUT" && output == "VOUT");
            if (isFeedback) // feedback path is always above its own op-amp
                Place(resistor, new(opAmpX[stage], 100), ComponentOrientation.Left);
            else if (sum is not null)
            {
                var row = inputRow.GetValueOrDefault(sum);
                inputRow[sum] = row + 1;
                var y = output == "STAGE_1_OUT" ? opAmpY : 230 + row * 95;
                if (output != "STAGE_1_OUT" && y > 300) y += 180;
                Place(resistor, new(opAmpX[stage] - 255, y));
            }
            else if (plus is not null) // bias-current compensation, aligned with IN+
                Place(resistor, new(opAmpX[stage] - 55, opAmpY - 170), ComponentOrientation.Down);
            else
                Place(resistor, new(180, 700 + placed.Count * 45));
        }

        foreach (var net in graph.Nets.Where(net => net.Name.StartsWith("SUM", StringComparison.OrdinalIgnoreCase) && !net.Name.EndsWith("_PLUS", StringComparison.OrdinalIgnoreCase)))
            RouteSummingBus(net);
        foreach (var net in graph.Nets.Where(net => IsOutputNet(net.Name))) RouteOutput(net);
        foreach (var net in graph.Nets.Where(net => net.Name.EndsWith("_PLUS", StringComparison.OrdinalIgnoreCase))) RoutePointToPoint(net);
        foreach (var net in graph.Nets.Where(net => !net.Name.StartsWith("SUM", StringComparison.OrdinalIgnoreCase) && !IsOutputNet(net.Name))) RouteLocalLabels(net);

        return document;

        void RouteSummingBus(CircuitNet net)
        {
            var anchors = Anchors(net).ToArray();
            var opAmpPin = net.Pins.First(pin => graph.GetComponent(pin.ComponentReference).Type == ComponentType.OpAmp && pin.Name == "IN-");
            var input = SchematicGeometry.GetWorldPin(placed[opAmpPin.ComponentReference], opAmpPin.Name);
            var trunkX = input.X - 55;
            var minimumY = Math.Min(input.Y, anchors.Min(point => point.Y));
            var maximumY = Math.Max(input.Y, anchors.Max(point => point.Y));
            document.AddWire(new(net.Name, [new(trunkX, minimumY), new(trunkX, maximumY)]));
            foreach (var anchor in anchors.Where(anchor => anchor != input))
            {
                document.AddWire(new(net.Name, [anchor, new SchematicPoint(trunkX, anchor.Y)]));
                document.AddJunction(new(net.Name, new(trunkX, anchor.Y)));
            }
            document.AddWire(new(net.Name, [new(trunkX, input.Y), input]));
            document.AddJunction(new(net.Name, new(trunkX, input.Y)));
        }

        void RouteOutput(CircuitNet net)
        {
            var pins = net.Pins.Where(pin => placed.ContainsKey(pin.ComponentReference)).ToArray();
            var outputPin = pins.First(pin => graph.GetComponent(pin.ComponentReference).Type == ComponentType.OpAmp && pin.Name == "OUT");
            var output = SchematicGeometry.GetWorldPin(placed[outputPin.ComponentReference], outputPin.Name);
            var busX = output.X + 70;
            var other = pins.Where(pin => pin != outputPin).Select(pin => SchematicGeometry.GetWorldPin(placed[pin.ComponentReference], pin.Name)).ToArray();
            var ys = other.Select(point => point.Y).Append(output.Y).ToArray();
            document.AddWire(new(net.Name, [new(busX, ys.Min()), new(busX, ys.Max())]));
            document.AddWire(new(net.Name, [output, new SchematicPoint(busX, output.Y)]));
            foreach (var anchor in other) document.AddWire(new(net.Name, [anchor, new SchematicPoint(busX, anchor.Y)]));
            if (net.Name == "VOUT")
            {
                var end = new SchematicPoint(busX + 95, output.Y);
                document.AddWire(new(net.Name, [new SchematicPoint(busX, output.Y), end]));
                document.AddNetLabel(new(net.Name, "VOUT", end));
            }
        }

        void RoutePointToPoint(CircuitNet net)
        {
            var anchors = Anchors(net).ToArray();
            if (anchors.Length != 2) { RouteLocalLabels(net); return; }
            var middleX = (anchors[0].X + anchors[1].X) / 2;
            document.AddWire(new(net.Name, [anchors[0], new(middleX, anchors[0].Y), new(middleX, anchors[1].Y), anchors[1]]));
        }

        void RouteLocalLabels(CircuitNet net)
        {
            foreach (var pin in net.Pins.Where(pin => placed.ContainsKey(pin.ComponentReference)))
            {
                var anchor = SchematicGeometry.GetWorldPin(placed[pin.ComponentReference], pin.Name);
                SchematicPoint end;
                if (pin.Name == "V+") end = new(anchor.X, anchor.Y - 45);
                else if (pin.Name == "V-") end = new(anchor.X, anchor.Y + 45);
                else end = new(anchor.X - 75, anchor.Y);
                document.AddWire(new(net.Name, [anchor, end]));
                document.AddNetLabel(new(net.Name, net.Name, end));
            }
        }

        IEnumerable<SchematicPoint> Anchors(CircuitNet net) => net.Pins.Where(pin => placed.ContainsKey(pin.ComponentReference))
            .Select(pin => SchematicGeometry.GetWorldPin(placed[pin.ComponentReference], pin.Name));
        string Net(CircuitComponent component, string pin) => graph.GetNetForPin(component.Reference, pin)?.Name ?? "";
        void Place(CircuitComponent component, SchematicPoint position, ComponentOrientation orientation = ComponentOrientation.Right)
        {
            var item = new PlacedComponent(component, position, orientation); placed.Add(component.Reference, item); document.AddPlacedComponent(item);
        }
    }

    private static bool IsOutputNet(string name) => name is "VOUT" or "STAGE_1_OUT";
    private static int NumericSuffix(string reference) => int.TryParse(new string(reference.SkipWhile(character => !char.IsDigit(character)).ToArray()), out var value) ? value : int.MaxValue;
}
