namespace CircuitModuleDesigner.Core.Schematic;

public sealed record SchematicJunction(string NetName, SchematicPoint Position);

public sealed record SchematicNetLabel(string NetName, string Text, SchematicPoint Position);

public sealed record SchematicTextAnnotation(string Text, SchematicPoint Position);
