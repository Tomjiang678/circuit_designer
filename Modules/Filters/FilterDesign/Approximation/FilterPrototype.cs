using System.Numerics;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;

public sealed record FilterPrototype(
    FilterApproximation Approximation,
    int Order,
    IReadOnlyList<Complex> Poles,
    IReadOnlyList<double> LadderGValues);
