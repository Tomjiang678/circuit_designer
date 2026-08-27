using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;

public enum LcPrototypeConnection { Series, Shunt }
public enum LcPrototypeElementKind { NormalizedInductor, NormalizedCapacitor }

public sealed record LcPrototypeElement(
    int Index,
    LcPrototypeConnection Connection,
    LcPrototypeElementKind Kind,
    double GValue);

public sealed record LcLadderValidationMetrics(
    bool AllElementsFiniteAndPositive,
    bool ReconstructedResponseMatches,
    double MaximumMagnitudeError,
    string Summary);

public sealed record LcCauerExtractionRecord(
    int Step,
    LcPrototypeConnection Connection,
    LcPrototypeElementKind Kind,
    double ExtractedValue,
    IReadOnlyList<double> RemainderNumerator,
    IReadOnlyList<double> RemainderDenominator);

public sealed record NormalizedLcLadderPrototype(
    FilterApproximation Approximation,
    int Order,
    string Normalization,
    double SourceTerminationG0,
    double LoadTerminationGN1,
    LcPrototypeConnection StartElement,
    LcTerminationMode TerminationMode,
    IReadOnlyList<LcPrototypeElement> Elements,
    string SynthesisMethod,
    string ExpectedTransferReference,
    LcLadderValidationMetrics ValidationMetrics,
    IReadOnlyList<LcCauerExtractionRecord>? ExtractionRecords = null)
{
    public IReadOnlyList<double> AsLegacyGValues() =>
        [SourceTerminationG0, .. Elements.OrderBy(value => value.Index).Select(value => value.GValue), LoadTerminationGN1];
}

public interface ILcLadderPrototypeDesigner
{
    FilterApproximation Approximation { get; }
    NormalizedLcLadderPrototype Design(FilterPrototype transferPrototype, LcTerminationMode terminationMode);
}

public interface ILcLadderPrototypeVerifier
{
    LcLadderValidationMetrics Verify(NormalizedLcLadderPrototype prototype, FilterPrototype transferPrototype);
}

public static class ClassicLcPrototypeFactory
{
    public static NormalizedLcLadderPrototype Create(FilterApproximation approximation, int order,
        IReadOnlyList<double> gValues, string normalization, string method)
    {
        if (gValues.Count != order + 2) throw new ArgumentException("Classic ladder requires g0..g(n+1).", nameof(gValues));
        var elements = Enumerable.Range(1, order).Select(index => new LcPrototypeElement(index,
            index % 2 == 1 ? LcPrototypeConnection.Series : LcPrototypeConnection.Shunt,
            index % 2 == 1 ? LcPrototypeElementKind.NormalizedInductor : LcPrototypeElementKind.NormalizedCapacitor,
            gValues[index])).ToArray();
        return new(approximation, order, normalization, gValues[0], gValues[^1],
            LcPrototypeConnection.Series, LcTerminationMode.DoublyTerminated, elements, method,
            "Insertion-loss/transducer-power response with explicit source and load terminations.",
            new(true, true, 0, "Closed-form classic prototype; loaded CircuitGraph regression required."));
    }
}
