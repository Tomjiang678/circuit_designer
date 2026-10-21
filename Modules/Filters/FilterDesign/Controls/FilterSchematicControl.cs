using System.ComponentModel;
using System.Drawing.Drawing2D;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Core.Schematic.Symbols;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;

public sealed class FilterSchematicControl : UserControl
{
    private const int CanvasMargin = 45;
    private SchematicDocument? document;
    private double zoom = 1;
    private double minimumX;
    private double minimumY;
    private bool showComponentValues = true;
    private bool avoidWireLabelCollisions = true;
    private bool showCursorCoordinates;
    private Point? cursorPoint;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SchematicDocument? Document
    {
        get => document;
        set { document = value; FitDocument(true); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Zoom
    {
        get => zoom;
        set { zoom = Math.Clamp(value, 0.65, 2.5); UpdateScrollExtent(); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowComponentValues
    {
        get => showComponentValues;
        set { showComponentValues = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AvoidWireLabelCollisions
    {
        get => avoidWireLabelCollisions;
        set { avoidWireLabelCollisions = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowCursorCoordinates
    {
        get => showCursorCoordinates;
        set { showCursorCoordinates = value; cursorPoint = null; Invalidate(); }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!showCursorCoordinates) return;
        cursorPoint = e.Location; Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!showCursorCoordinates) return;
        cursorPoint = null; Invalidate();
    }

    public FilterSchematicControl()
    {
        DoubleBuffered = true;
        BackColor = SystemColors.Window;
        ResizeRedraw = true;
        AutoScroll = true;
        TabStop = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Zoom In", null, (_, _) => Zoom *= 1.15);
        menu.Items.Add("Zoom Out", null, (_, _) => Zoom /= 1.15);
        menu.Items.Add("Fit Schematic", null, (_, _) => FitDocument(false));
        menu.Items.Add("Actual Size", null, (_, _) => Zoom = 1);
        ContextMenuStrip = menu;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == Keys.Control) { Zoom *= e.Delta > 0 ? 1.12 : 1 / 1.12; return; }
        base.OnMouseWheel(e);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScrollExtent(); }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(SystemColors.Window);
        if (document == null || document.PlacedComponents.Count == 0)
        {
            e.Graphics.DrawString("No schematic available.", Font, Brushes.DimGray, 20, 20);
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var wirePen = new Pen(Color.FromArgb(35, 35, 35), 1.35F) { LineJoin = LineJoin.Miter };
        foreach (var wire in document.Wires)
        {
            var points = wire.Points.Select(Map).ToArray();
            if (points.Length > 1) e.Graphics.DrawLines(wirePen, points);
        }
        foreach (var junction in document.Junctions)
        {
            var point = Map(junction.Position);
            e.Graphics.FillEllipse(Brushes.Black, point.X - 3, point.Y - 3, 6, 6);
        }

        var occupiedLabels = new List<RectangleF>();
        foreach (var annotation in document.TextAnnotations)
        {
            var point = Map(annotation.Position);
            using var font = new Font(Font, FontStyle.Bold);
            var size=e.Graphics.MeasureString(annotation.Text,font);
            var rectangle=new RectangleF(point,size);
            e.Graphics.DrawString(annotation.Text, font, Brushes.DimGray, rectangle);
            occupiedLabels.Add(RectangleF.Inflate(rectangle,4,3));
        }
        foreach (var placed in document.PlacedComponents) DrawComponent(e.Graphics, placed, occupiedLabels);
        foreach (var label in document.NetLabels) DrawNetLabel(e.Graphics, label, occupiedLabels);
        if (showCursorCoordinates && cursorPoint is { } pointer)
        {
            var x = (pointer.X - CanvasMargin - AutoScrollPosition.X) / Math.Max(1e-9, zoom) + minimumX;
            var y = (pointer.Y - CanvasMargin - AutoScrollPosition.Y) / Math.Max(1e-9, zoom) + minimumY;
            DrawCursorReadout(e.Graphics, pointer, x, y);
        }
    }

    private static void DrawCursorReadout(Graphics graphics, Point cursor, double x, double y)
    {
        var text = $"X {x:0}   Y {y:0}";
        using var font = new Font("Segoe UI", 8f);
        var size = graphics.MeasureString(text, font);
        var left = Math.Min(cursor.X + 14f, Math.Max(4f, graphics.VisibleClipBounds.Width - size.Width - 8));
        var top = Math.Min(cursor.Y + 14f, Math.Max(4f, graphics.VisibleClipBounds.Height - size.Height - 8));
        using var background = new SolidBrush(Color.FromArgb(220, 30, 30, 30));
        using var foreground = new SolidBrush(Color.White);
        graphics.FillRectangle(background, left - 3, top - 2, size.Width + 6, size.Height + 4);
        graphics.DrawString(text, font, foreground, left, top);
    }

    private void DrawComponent(Graphics graphics, PlacedComponent placed, List<RectangleF> occupiedLabels)
    {
        var center = Map(placed.Position);
        var state = graphics.Save();
        graphics.TranslateTransform(center.X, center.Y);
        graphics.RotateTransform(placed.Orientation switch { ComponentOrientation.Down => 90, ComponentOrientation.Left => 180, ComponentOrientation.Up => 270, _ => 0 });
        graphics.ScaleTransform((float)zoom, (float)zoom);
        using var pen = new Pen(Color.Black, (float)(1.45 / zoom)) { LineJoin = LineJoin.Round };
        DrawSymbol(graphics, SymbolDefinitionCatalog.Get(placed.Component.Type).Shape, pen);
        graphics.Restore(state);

        var logicalBounds = SchematicGeometry.GetBounds(placed);
        var topLeft = Map(new SchematicPoint(logicalBounds.X, logicalBounds.Y));
        var bottomRight = Map(new SchematicPoint(logicalBounds.X + logicalBounds.Width, logicalBounds.Y + logicalBounds.Height));
        var bounds = RectangleF.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        var value = showComponentValues ? FormatValue(placed.Component) : string.Empty;
        DrawPlacedLabel(graphics,
            value.Length == 0 ? placed.Component.Reference : $"{placed.Component.Reference}\r\n{value}",
            Brushes.Navy, bounds, placed.Orientation, occupiedLabels);
    }

    private static void DrawSymbol(Graphics graphics, SymbolShape shape, Pen pen)
    {
        switch (shape)
        {
            case SymbolShape.ResistorZigZag:
                graphics.DrawLines(pen, [new(-40, 0), new(-25, 0), new(-20, -9), new(-10, 9), new(0, -9), new(10, 9), new(20, -9), new(25, 0), new(40, 0)]);
                break;
            case SymbolShape.CapacitorPlates:
                graphics.DrawLine(pen, -40, 0, -7, 0); graphics.DrawLine(pen, -7, -14, -7, 14); graphics.DrawLine(pen, 7, -14, 7, 14); graphics.DrawLine(pen, 7, 0, 40, 0);
                break;
            case SymbolShape.InductorCoil:
                graphics.DrawLine(pen, -40, 0, -24, 0); for (var i = 0; i < 4; i++) graphics.DrawArc(pen, -24 + i * 12, -6, 12, 12, 180, 180); graphics.DrawLine(pen, 24, 0, 40, 0);
                break;
            case SymbolShape.OpAmpTriangle:
            case SymbolShape.ComparatorTriangle:
                graphics.DrawPolygon(pen, [new(-36, -38), new(-36, 38), new(42, 0)]);
                graphics.DrawLine(pen, -55, -22, -36, -22); graphics.DrawLine(pen, -55, 22, -36, 22); graphics.DrawLine(pen, 42, 0, 55, 0);
                using (var symbolFont = new Font("Segoe UI", 8F, FontStyle.Bold, GraphicsUnit.Point))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    graphics.DrawString("+", symbolFont, Brushes.Black, new RectangleF(-35, -31, 18, 18), format);
                    graphics.DrawString("−", symbolFont, Brushes.Black, new RectangleF(-35, 13, 18, 18), format);
                }
                break;
            case SymbolShape.LogicGate:
                graphics.DrawRectangle(pen, -30, -27, 60, 54);
                graphics.DrawLine(pen, -45, -17, -30, -17); graphics.DrawLine(pen, -45, 17, -30, 17); graphics.DrawLine(pen, 30, 0, 45, 0);
                using (var gateFont = new Font("Segoe UI", 8F, FontStyle.Bold, GraphicsUnit.Point))
                    graphics.DrawString("LOGIC", gateFont, Brushes.Black, -21, -8);
                break;
            case SymbolShape.DiodeJunction:
                graphics.DrawLine(pen, -40, 0, -15, 0);
                graphics.DrawPolygon(pen, [new PointF(-15, -14), new PointF(-15, 14), new PointF(12, 0)]);
                graphics.DrawLine(pen, 12, -14, 12, 14);
                graphics.DrawLine(pen, 12, 0, 40, 0);
                break;
            case SymbolShape.Mosfet:
                graphics.DrawLine(pen,-45,0,-12,0);
                graphics.DrawLine(pen,-12,-25,-12,25);
                graphics.DrawLine(pen,8,-28,8,28);
                graphics.DrawLine(pen,8,-18,18,-18);
                graphics.DrawLine(pen,18,-45,18,-18);
                graphics.DrawLine(pen,8,18,18,18);
                graphics.DrawLine(pen,18,18,18,45);
                graphics.DrawLine(pen,2,8,13,8);
                graphics.DrawLine(pen,13,8,8,4);
                graphics.DrawLine(pen,13,8,8,12);
                break;
            case SymbolShape.AnalogSwitch:
                graphics.DrawLine(pen,-45,0,-14,0);
                graphics.DrawLine(pen,14,0,45,0);
                graphics.DrawLine(pen,-14,0,13,-13);
                graphics.DrawEllipse(pen,-17,-3,6,6);
                graphics.DrawEllipse(pen,11,-3,6,6);
                graphics.DrawLine(pen,0,22,0,10);
                break;
        }
    }

    private void DrawPlacedLabel(Graphics graphics, string text, Brush brush, RectangleF componentBounds, ComponentOrientation orientation, List<RectangleF> occupied)
    {
        var size = graphics.MeasureString(text, Font);
        var vertical = orientation is ComponentOrientation.Up or ComponentOrientation.Down;
        var preferred = vertical
            ? new RectangleF(componentBounds.Right + 7, componentBounds.Top + (componentBounds.Height - size.Height) / 2, size.Width, size.Height)
            : new RectangleF(componentBounds.Left + (componentBounds.Width - size.Width) / 2, componentBounds.Top - size.Height - 5, size.Width, size.Height);
        var candidates = new[]
        {
            preferred,
            new RectangleF(componentBounds.Left - size.Width - 7, componentBounds.Top + (componentBounds.Height - size.Height) / 2, size.Width, size.Height),
            new RectangleF(componentBounds.Right + 7, componentBounds.Top - size.Height - 3, size.Width, size.Height),
            new RectangleF(componentBounds.Left - size.Width - 7, componentBounds.Top - size.Height - 3, size.Width, size.Height),
            new RectangleF(componentBounds.Left + (componentBounds.Width - size.Width) / 2, componentBounds.Bottom + 4, size.Width, size.Height),
            new RectangleF(componentBounds.Right + 7, componentBounds.Bottom + 3, size.Width, size.Height),
            new RectangleF(componentBounds.Left - size.Width - 7, componentBounds.Bottom + 3, size.Width, size.Height)
        };
        var selected = candidates.FirstOrDefault(candidate =>
            occupied.All(existing => !existing.IntersectsWith(candidate)) &&
            (!avoidWireLabelCollisions || !IntersectsAnyWire(candidate)));
        if (selected.Width <= 0) selected = preferred;
        PaintTextWithBackground(graphics, text, brush, selected);
        occupied.Add(RectangleF.Inflate(selected, 2, 1));
    }

    private bool IntersectsAnyWire(RectangleF rectangle)
    {
        if(document==null)return false;
        var inflated=RectangleF.Inflate(rectangle,2,2);
        foreach(var wire in document.Wires)
        for(var index=1;index<wire.Points.Count;index++)
        {
            var first=Map(wire.Points[index-1]);var second=Map(wire.Points[index]);
            if(Math.Abs(first.X-second.X)<.1F)
            {
                if(first.X>=inflated.Left&&first.X<=inflated.Right&&Math.Max(first.Y,second.Y)>=inflated.Top&&Math.Min(first.Y,second.Y)<=inflated.Bottom)return true;
            }
            else if(Math.Abs(first.Y-second.Y)<.1F&&first.Y>=inflated.Top&&first.Y<=inflated.Bottom&&Math.Max(first.X,second.X)>=inflated.Left&&Math.Min(first.X,second.X)<=inflated.Right)return true;
        }
        return false;
    }

    private void DrawNetLabel(Graphics graphics, SchematicNetLabel label, List<RectangleF> occupied)
    {
        var point = Map(label.Position);
        if (label.Text.Equals("GND", StringComparison.OrdinalIgnoreCase))
        {
            using var pen = new Pen(Color.Black, 1.2F);
            var scale=(float)zoom;
            graphics.DrawLine(pen,point.X,point.Y,point.X,point.Y+8*scale);
            graphics.DrawLine(pen,point.X-8*scale,point.Y+8*scale,point.X+8*scale,point.Y+8*scale);
            graphics.DrawLine(pen,point.X-5*scale,point.Y+12*scale,point.X+5*scale,point.Y+12*scale);
            graphics.DrawLine(pen,point.X-2*scale,point.Y+16*scale,point.X+2*scale,point.Y+16*scale);
            return;
        }
        var size = graphics.MeasureString(label.Text, Font);
        var rect = new RectangleF(point.X, point.Y - size.Height / 2, size.Width, size.Height);
        while (occupied.Any(existing => existing.IntersectsWith(rect))) rect.Y += size.Height + 2;
        PaintTextWithBackground(graphics, label.Text, Brushes.DarkRed, rect);
        occupied.Add(RectangleF.Inflate(rect, 2, 1));
    }

    private static void PaintTextWithBackground(Graphics graphics, string text, Brush brush, RectangleF rectangle)
    {
        graphics.FillRectangle(SystemBrushes.Window, RectangleF.Inflate(rectangle, 2, 1));
        graphics.DrawString(text, SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, brush, rectangle.Location);
    }

    private PointF Map(SchematicPoint point) => new((float)((point.X - minimumX) * zoom + CanvasMargin + AutoScrollPosition.X), (float)((point.Y - minimumY) * zoom + CanvasMargin + AutoScrollPosition.Y));

    private void FitDocument(bool initialFitOnly)
    {
        if (document == null || document.PlacedComponents.Count == 0) return;
        CalculateLogicalBounds(out minimumX, out minimumY, out var width, out _);
        var fit = Math.Min(1, Math.Max(300, ClientSize.Width - CanvasMargin * 2) / Math.Max(1, width));
        Zoom = initialFitOnly ? Math.Max(0.75, fit) : Math.Max(0.65, fit);
        AutoScrollPosition = Point.Empty;
    }

    private void UpdateScrollExtent()
    {
        if (document == null || document.PlacedComponents.Count == 0) return;
        CalculateLogicalBounds(out minimumX, out minimumY, out var width, out var height);
        AutoScrollMinSize = new Size((int)Math.Ceiling(width * zoom + CanvasMargin * 2), (int)Math.Ceiling(height * zoom + CanvasMargin * 2));
    }

    private void CalculateLogicalBounds(out double minX, out double minY, out double width, out double height)
    {
        var boxes = document!.PlacedComponents.Select(item => SchematicGeometry.GetBounds(item)).ToArray();
        var points = document.Wires.SelectMany(item => item.Points).Concat(document.Junctions.Select(item => item.Position)).Concat(document.NetLabels.Select(item => item.Position)).Concat(document.TextAnnotations.Select(item => item.Position)).ToArray();
        minX = Math.Min(boxes.Min(item => item.X), points.Min(item => item.X)) - 50;
        minY = Math.Min(boxes.Min(item => item.Y), points.Min(item => item.Y)) - 45;
        var maxX = Math.Max(boxes.Max(item => item.X + item.Width), points.Max(item => item.X)) + 80;
        var maxY = Math.Max(boxes.Max(item => item.Y + item.Height), points.Max(item => item.Y)) + 50;
        width = maxX - minX; height = maxY - minY;
    }

    private static string FormatValue(CircuitComponent component) => component.Type switch
    {
        ComponentType.Resistor => Engineering(component.Parameters.GetRequiredValue(ComponentParameterNames.ResistanceOhm), "Ω"),
        ComponentType.Capacitor => Engineering(component.Parameters.GetRequiredValue(ComponentParameterNames.CapacitanceFarad), "F"),
        ComponentType.Inductor => Engineering(component.Parameters.GetRequiredValue(ComponentParameterNames.InductanceHenry), "H"),
        _ => string.Empty
    };

    private static string Engineering(double value, string unit)
    {
        var prefixes = new[] { (1e6, "M"), (1e3, "k"), (1d, ""), (1e-3, "m"), (1e-6, "µ"), (1e-9, "n"), (1e-12, "p") };
        var prefix = prefixes.First(item => value >= item.Item1 || item.Item1 == 1e-12);
        return $"{value / prefix.Item1:0.###} {prefix.Item2}{unit}";
    }
}
