namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;

public enum DesignMode
{
    Attenuation,
    ImpedanceMatching
}

public enum TopologyFamily
{
    DirectConnection,
    TPads,
    PiPads,
    LPads,
    CascadedPads,
    LMatching,
    PiMatching,
    TMatching,
}

public enum MatchingResponse
{
    LowPass,
    HighPass,
    Mixed
}

public enum PortImpedanceType
{
    PureResistance,
    SeriesRL,
    SeriesRC
}

public enum OptimizationObjective
{
    BestAttenuationAccuracy,
    BestMatching,
    BestBandwidth,
    MinimumInsertionLoss,
    MinimumComponents,
    MinimumComponentStress,
    Balanced
}

public enum ComponentValueMode
{
    Continuous,
    E12,
    E24,
    E48,
    E96
}

public enum SignalLevelUnit
{
    RmsVoltage,
    AvailablePowerDbm,
    AvailablePowerWatt
}

public enum DesignStatus
{
    Supported,
    Warning,
    Failed
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public enum TopologySelectionMode
{
    Automatic,
    Manual
}
