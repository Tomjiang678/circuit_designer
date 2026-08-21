namespace CircuitModuleDesigner.Core.Circuit.Spice;

public sealed record CircuitSpiceExportOptions(
    string Title,
    double StartFrequencyHz,
    double StopFrequencyHz,
    int PointsPerDecade = 40,
    double OpAmpOpenLoopGain = 1e6,
    double? SourceResistanceOhm = null,
    double? LoadResistanceOhm = null);
