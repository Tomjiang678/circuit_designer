namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

public enum FilterMode { Active, Passive }
public enum FilterResponseType { LowPass, HighPass, BandPass, BandStop }
public enum FilterApproximation
{
    Butterworth,
    Bessel,
    ChebyshevTypeI,
    GaussianTo6Db,
    GaussianTo12Db,
    LinearPhase005Degree,
    LinearPhase05Degree
}
public enum FilterImplementationType { SallenKey, MultipleFeedback, BainterNotch, Rc, Lc }
public enum FilterSignalArchitecture { SingleEnded, FullyDifferential }
public enum FilterOrderMode { CalculateAutomatically, SpecifyOrder }
public enum BandFrequencyInputMode { Edges, CenterAndBandwidth }
public enum StandardComponentSeries { E12, E24, E48, E96 }
public enum FilterOptimizationTarget { MinimizeFrequencyError, MinimizeQError }
public enum FrequencyUnit { Hz, KHz, MHz }
public enum ResistanceUnit { Ohm, KOhm, MOhm }
public enum CapacitanceUnit { PF, NF, UF, MF }
