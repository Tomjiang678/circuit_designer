using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;

public enum SectionOrder
{
    First = 1,
    Second = 2
}

public sealed record AnalogFilterSection(
    int Index,
    SectionOrder Order,
    FilterResponseType ResponseType,
    double PoleFrequencyHz,
    double Q,
    double SectionGain,
    IReadOnlyList<double> NumeratorCoefficients,
    IReadOnlyList<double> DenominatorCoefficients);
