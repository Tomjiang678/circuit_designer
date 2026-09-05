using CircuitModuleDesigner.Core.Analysis;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;

public sealed record PassiveFilterAnalysisResult(
    LoadedCircuitContext Boundary,
    PassiveResponseReference PrimaryReference,
    AcAnalysisResult Primary,
    AcAnalysisResult AbsoluteVoltageTransfer,
    AcAnalysisResult FilterNodeVoltageTransfer,
    AcAnalysisResult? TransducerGain,
    AcAnalysisResult InsertionLossRelativeToThrough);

public sealed class PassiveFilterAnalysisService
{
    private readonly ICircuitAcAnalysisService analyzer;
    private readonly PassiveFilterLoadedCircuitAssembler assembler = new();

    public PassiveFilterAnalysisService(ICircuitAcAnalysisService? analyzer = null) =>
        this.analyzer = analyzer ?? new CircuitAcAnalysisService();

    public PassiveFilterAnalysisResult Analyze(CircuitGraph filterNetwork, FilterDesignRequest request, AcAnalysisOptions baseOptions)
    {
        var boundary = assembler.Assemble(filterNetwork, request);
        var absolute = analyzer.Analyze(filterNetwork, baseOptions with
        {
            MagnitudeReferenceLinear = 1,
            MagnitudeReferenceDescription = $"Absolute voltage transfer VOUT/VSRC; VSRC-Rs({request.SourceResistanceOhm:G6} Ω)-VIN-filter-VOUT-RL({request.LoadResistanceOhm:G6} Ω)"
        }, request.SourceResistanceOhm > 0 ? request.SourceResistanceOhm : null, request.LoadResistanceOhm);

        var nodeTransfer = DeriveNodeTransfer(absolute,
            $"Filter-node voltage transfer VOUT/VIN with RL={request.LoadResistanceOhm:G6} Ω");

        AcAnalysisResult? transducer = null;
        if (request.SourceResistanceOhm > 0)
        {
            var equivalentVoltage = .5 * Math.Sqrt(request.LoadResistanceOhm / request.SourceResistanceOhm);
            transducer = Scale(absolute, equivalentVoltage,
                $"Transducer power gain relative to available source power; Rs={request.SourceResistanceOhm:G6} Ω, RL={request.LoadResistanceOhm:G6} Ω");
        }

        var throughVoltage = boundary.DirectThroughVoltageRatio;
        var insertion = Scale(absolute, throughVoltage,
            $"Insertion response relative to direct through (0 dB); through VOUT/VSRC={throughVoltage:G9}");

        var primaryReference = request.Implementation == FilterImplementationType.Rc
            ? PassiveResponseReference.AbsoluteVoltageTransfer
            : request.PassiveResponseReference;
        var primary = primaryReference switch
        {
            PassiveResponseReference.AbsoluteVoltageTransfer => absolute,
            PassiveResponseReference.FilterNodeVoltageTransfer => nodeTransfer,
            PassiveResponseReference.TransducerGain => transducer ?? throw new InvalidOperationException("Transducer gain cannot be the primary response when Rs=0."),
            PassiveResponseReference.InsertionLossRelativeToThrough => insertion,
            _ => throw new ArgumentOutOfRangeException(nameof(request.PassiveResponseReference))
        };
        return new(boundary, primaryReference, primary, absolute, nodeTransfer, transducer, insertion);
    }

    private static AcAnalysisResult Scale(AcAnalysisResult absolute, double voltageReference, string description) => new()
    {
        Points = absolute.Points.Select(point =>
        {
            var magnitude = point.MagnitudeLinear / voltageReference;
            return new FrequencyResponsePoint(point.FrequencyHz, magnitude,
                20 * Math.Log10(Math.Max(magnitude, 1e-15)), point.PhaseDegrees);
        }).ToArray(),
        Warnings = absolute.Warnings,
        MagnitudeReferenceLinear = voltageReference,
        MagnitudeReferenceDescription = description
    };

    private static AcAnalysisResult DeriveNodeTransfer(AcAnalysisResult loaded, string description) => new()
    {
        Points = loaded.Points.Select(point =>
        {
            var input = point.InputNodeVoltage ?? throw new InvalidOperationException("VIN phasor was not retained by AC analysis.");
            var output = point.OutputNodeVoltage ?? throw new InvalidOperationException("VOUT phasor was not retained by AC analysis.");
            var transfer = output / input;
            return new FrequencyResponsePoint(point.FrequencyHz, transfer.Magnitude,
                20 * Math.Log10(Math.Max(transfer.Magnitude, 1e-15)),
                Math.Atan2(transfer.Imaginary, transfer.Real) * 180 / Math.PI, input, output);
        }).ToArray(),
        Warnings = loaded.Warnings,
        MagnitudeReferenceLinear = 1,
        MagnitudeReferenceDescription = description
    };
}
