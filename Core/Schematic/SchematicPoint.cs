namespace CircuitModuleDesigner.Core.Schematic;

public readonly record struct SchematicPoint(double X, double Y);

public readonly record struct SchematicSize(double Width, double Height);

public readonly record struct SchematicRect(double X, double Y, double Width, double Height)
{
    public static SchematicRect Centered(SchematicSize size) =>
        new(-size.Width / 2, -size.Height / 2, size.Width, size.Height);
}
