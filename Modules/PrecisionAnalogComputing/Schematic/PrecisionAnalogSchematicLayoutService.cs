using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Schematic;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Schematic;

/// <summary>Operation-aware left-to-right layout for affine summing circuits.</summary>
public sealed class PrecisionAnalogSchematicLayoutService
{
    public SchematicDocument Layout(CircuitGraph graph)
    {
        if(graph.Components.Any(component=>component.Type==ComponentType.Diode) &&
           graph.Nets.Any(net=>net.Name is "SUM_RECT" or "SUM"))
            return LayoutRectifierGraph(graph);
        if (graph.Components.Any(component => component.Type is ComponentType.Diode or ComponentType.Capacitor) ||
            graph.Components.Count(component => component.Type == ComponentType.OpAmp) > 2)
            return LayoutOperationGraph(graph);
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

    private static SchematicDocument LayoutRectifierGraph(CircuitGraph graph)
    {
        var document=new SchematicDocument(graph);
        var placed=new Dictionary<string,PlacedComponent>(StringComparer.OrdinalIgnoreCase);
        var fullWave=graph.Nets.Any(net=>net.Name=="SUM_ABS");

        if(fullWave)
        {
            Put("R1",new(260,190)); Put("U1",new(570,190)); Put("D1",new(780,190));
            Put("R2",new(900,25),ComponentOrientation.Left); Put("D2",new(570,350));
            Put("R3",new(690,530)); Put("R4",new(900,405)); Put("U2",new(1080,505)); Put("R5",new(1080,335));
            if(Has("U3")){Put("R6",new(1300,505));Put("U3",new(1540,505));Put("R7",new(1540,335));}
        }
        else
        {
            var hasInputInverter=Has("U2");
            if(hasInputInverter)
            {
                Put("R3",new(210,240));Put("U2",new(470,240));Put("R4",new(470,75));
                Put("R1",new(700,240));
            }
            else Put("R1",new(300,240));
            var coreX=hasInputInverter?980d:610d;
            Put("U1",new(coreX,240));Put("R2",new(coreX,75));Put("D1",new(coreX+210,240));Put("D2",new(coreX,410));
        }

        foreach(var net in graph.Nets)
        {
            var pins=net.Pins.Where(pin=>placed.ContainsKey(pin.ComponentReference)).ToArray();
            var anchors=pins.Select(pin=>SchematicGeometry.GetWorldPin(placed[pin.ComponentReference],pin.Name)).ToArray();
            if(anchors.Length==0)continue;
            if(fullWave&&net.Name=="SUM_RECT")
            {
                var input=SchematicGeometry.GetWorldPin(placed["U1"],"IN-");var busX=input.X-58;
                var ordinary=pins.Where(pin=>pin.ComponentReference!="R2").Select(pin=>SchematicGeometry.GetWorldPin(placed[pin.ComponentReference],pin.Name)).ToArray();
                document.AddWire(new(net.Name,[new SchematicPoint(busX,-35),new SchematicPoint(busX,ordinary.Max(point=>point.Y))]));
                foreach(var anchor in ordinary)document.AddWire(new(net.Name,[anchor,new SchematicPoint(busX,anchor.Y)]));
                var feedback=SchematicGeometry.GetWorldPin(placed["R2"],"1");
                document.AddWire(new(net.Name,[feedback,new SchematicPoint(feedback.X+50,feedback.Y),new SchematicPoint(feedback.X+50,-35),new SchematicPoint(busX,-35)]));
                continue;
            }
            if(fullWave&&net.Name=="HALF_RECT")
            {
                const double halfBusX=850;
                document.AddWire(new(net.Name,[new SchematicPoint(halfBusX,anchors.Min(point=>point.Y)),new SchematicPoint(halfBusX,anchors.Max(point=>point.Y))]));
                foreach(var anchor in anchors)document.AddWire(new(net.Name,[anchor,new SchematicPoint(halfBusX,anchor.Y)]));
                continue;
            }
            if(net.Name is "VCC" or "VEE" or "GND")
            {
                foreach(var anchor in anchors)
                {
                    var vertical=net.Name=="VCC"?-38:net.Name=="VEE"?38:0;
                    var end=vertical==0?new SchematicPoint(anchor.X-48,anchor.Y):new SchematicPoint(anchor.X,anchor.Y+vertical);
                    document.AddWire(new(net.Name,[anchor,end]));document.AddNetLabel(new(net.Name,net.Name,end));
                }
                continue;
            }
            if(net.Name=="VIN")
            {
                var vinTrunkX=110d;var entry=new SchematicPoint(55,anchors.Min(point=>point.Y));
                document.AddWire(new(net.Name,[entry,new SchematicPoint(vinTrunkX,entry.Y),new SchematicPoint(vinTrunkX,anchors.Max(point=>point.Y))]));
                foreach(var anchor in anchors)document.AddWire(new(net.Name,[new SchematicPoint(vinTrunkX,anchor.Y),anchor]));
                document.AddNetLabel(new(net.Name,"VIN",entry));continue;
            }
            if(net.Name=="VOUT")
            {
                var busX=anchors.Max(point=>point.X)+70;
                document.AddWire(new(net.Name,[new SchematicPoint(busX,anchors.Min(point=>point.Y)),new SchematicPoint(busX,anchors.Max(point=>point.Y))]));
                foreach(var anchor in anchors)document.AddWire(new(net.Name,[anchor,new SchematicPoint(busX,anchor.Y)]));
                var end=new SchematicPoint(busX+85,anchors.Last().Y);document.AddWire(new(net.Name,[new SchematicPoint(busX,end.Y),end]));document.AddNetLabel(new(net.Name,"VOUT",end));continue;
            }

            var inputPin=pins.FirstOrDefault(pin=>graph.GetComponent(pin.ComponentReference).Type==ComponentType.OpAmp&&pin.Name=="IN-");
            var outputPin=pins.FirstOrDefault(pin=>graph.GetComponent(pin.ComponentReference).Type==ComponentType.OpAmp&&pin.Name=="OUT");
            var trunkX=inputPin is not null?SchematicGeometry.GetWorldPin(placed[inputPin.ComponentReference],inputPin.Name).X-58:
                outputPin is not null?SchematicGeometry.GetWorldPin(placed[outputPin.ComponentReference],outputPin.Name).X+58:
                (anchors.Min(point=>point.X)+anchors.Max(point=>point.X))/2;
            document.AddWire(new(net.Name,[new SchematicPoint(trunkX,anchors.Min(point=>point.Y)),new SchematicPoint(trunkX,anchors.Max(point=>point.Y))]));
            foreach(var anchor in anchors)
            {
                document.AddWire(new(net.Name,[anchor,new SchematicPoint(trunkX,anchor.Y)]));
                if(anchors.Length>2)document.AddJunction(new(net.Name,new SchematicPoint(trunkX,anchor.Y)));
            }
        }
        return document;

        bool Has(string reference)=>graph.Components.Any(component=>component.Reference.Equals(reference,StringComparison.OrdinalIgnoreCase));
        void Put(string reference,SchematicPoint position,ComponentOrientation orientation=ComponentOrientation.Right)
        {
            if(!Has(reference))return;
            var component=graph.GetComponent(reference);var item=new PlacedComponent(component,position,orientation);
            placed.Add(reference,item);document.AddPlacedComponent(item);
        }
    }

    private static SchematicDocument LayoutOperationGraph(CircuitGraph graph)
    {
        var document=new SchematicDocument(graph);
        var placed=new Dictionary<string,PlacedComponent>(StringComparer.OrdinalIgnoreCase);
        var passives=graph.Components.Where(component=>component.Type!=ComponentType.OpAmp)
            .OrderBy(component=>component.Type).ThenBy(component=>NumericSuffix(component.Reference)).ToArray();
        for(var index=0;index<passives.Length;index++)
        {
            var position=new SchematicPoint(170+(index%3)*185,125+(index/3)*92);
            var item=new PlacedComponent(passives[index],position,ComponentOrientation.Right);
            placed.Add(passives[index].Reference,item);document.AddPlacedComponent(item);
        }
        var opAmps=graph.Components.Where(component=>component.Type==ComponentType.OpAmp)
            .OrderBy(component=>NumericSuffix(component.Reference)).ToArray();
        for(var index=0;index<opAmps.Length;index++)
        {
            var item=new PlacedComponent(opAmps[index],new SchematicPoint(730+index*330,520),ComponentOrientation.Right);
            placed.Add(opAmps[index].Reference,item);document.AddPlacedComponent(item);
        }
        foreach(var net in graph.Nets)
        {
            var anchors=net.Pins.Where(pin=>placed.ContainsKey(pin.ComponentReference))
                .Select(pin=>SchematicGeometry.GetWorldPin(placed[pin.ComponentReference],pin.Name)).ToArray();
            if(anchors.Length==0)continue;
            if(IsNamedExternal(net.Name)||anchors.Length==1)
            {
                foreach(var anchor in anchors)
                {
                    var end=new SchematicPoint(anchor.X+(anchor.X<650?-55:55),anchor.Y);
                    document.AddWire(new(net.Name,[anchor,end]));document.AddNetLabel(new(net.Name,net.Name,end));
                }
                continue;
            }
            if(anchors.Length==2)
            {
                var middleX=(anchors[0].X+anchors[1].X)/2;
                document.AddWire(new(net.Name,[anchors[0],new SchematicPoint(middleX,anchors[0].Y),new SchematicPoint(middleX,anchors[1].Y),anchors[1]]));
                continue;
            }
            var busX=anchors.Max(point=>point.X)+45;
            document.AddWire(new(net.Name,[new SchematicPoint(busX,anchors.Min(point=>point.Y)),new SchematicPoint(busX,anchors.Max(point=>point.Y))]));
            foreach(var anchor in anchors)
            {
                document.AddWire(new(net.Name,[anchor,new SchematicPoint(busX,anchor.Y)]));
                document.AddJunction(new(net.Name,new SchematicPoint(busX,anchor.Y)));
            }
        }
        return document;
    }

    private static bool IsNamedExternal(string name)=>name is "VIN" or "VOUT" or "VCC" or "VEE" or "GND" or "VREF" or "VREF_HIGH" or "VREF_LOW";

    private static bool IsOutputNet(string name) => name is "VOUT" or "STAGE_1_OUT";
    private static int NumericSuffix(string reference) => int.TryParse(new string(reference.SkipWhile(character => !char.IsDigit(character)).ToArray()), out var value) ? value : int.MaxValue;
}
