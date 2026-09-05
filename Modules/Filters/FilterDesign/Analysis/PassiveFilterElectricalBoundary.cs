using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;

public enum PassiveBoundaryComponentRole { ExternalSourceTermination, FilterNetwork, ExternalLoadTermination }

public sealed record PassiveExternalTermination(
    string Reference,
    PassiveBoundaryComponentRole Role,
    string FromNet,
    string ToNet,
    double ResistanceOhm);

public sealed record LoadedCircuitContext(
    CircuitGraph FilterNetwork,
    string SourceNode,
    string FilterInputNode,
    string OutputNode,
    string GroundNode,
    double SourceResistanceOhm,
    double LoadResistanceOhm,
    IReadOnlyList<PassiveExternalTermination> ExternalTerminations)
{
    public bool HasFiniteSourceTermination => SourceResistanceOhm > 0;
    public double DirectThroughVoltageRatio => LoadResistanceOhm / (SourceResistanceOhm + LoadResistanceOhm);
}

/// <summary>
/// Defines the passive electrical boundary as VSRC - Rs - VIN - filter - VOUT - RL - GND.
/// The returned CircuitGraph remains the filter network; external terminations are explicit analysis metadata.
/// </summary>
public sealed class PassiveFilterLoadedCircuitAssembler
{
    public LoadedCircuitContext Assemble(CircuitGraph filterNetwork, FilterDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(filterNetwork);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Mode != FilterMode.Passive) throw new ArgumentException("A passive request is required.", nameof(request));
        if (!filterNetwork.Nets.Any(net => net.Name.Equals("VIN", StringComparison.OrdinalIgnoreCase)) ||
            !filterNetwork.Nets.Any(net => net.Name.Equals("VOUT", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The passive filter network must expose VIN and VOUT.");
        if (request.SourceResistanceOhm < 0 || !double.IsFinite(request.SourceResistanceOhm))
            throw new ArgumentOutOfRangeException(nameof(request.SourceResistanceOhm), "Rs must be finite and greater than or equal to zero.");
        if (request.LoadResistanceOhm <= 0 || !double.IsFinite(request.LoadResistanceOhm))
            throw new ArgumentOutOfRangeException(nameof(request.LoadResistanceOhm), "RL must be finite and greater than zero.");

        var terminations = new List<PassiveExternalTermination>();
        if (request.SourceResistanceOhm > 0)
            terminations.Add(new("RS_EXT", PassiveBoundaryComponentRole.ExternalSourceTermination, "VSRC", "VIN", request.SourceResistanceOhm));
        terminations.Add(new("RL_EXT", PassiveBoundaryComponentRole.ExternalLoadTermination, "VOUT", "GND", request.LoadResistanceOhm));
        return new(filterNetwork, "VSRC", "VIN", "VOUT", "GND",
            request.SourceResistanceOhm, request.LoadResistanceOhm, terminations);
    }
}

public static class PassiveResponseReferenceMath
{
    public static double AbsoluteVoltageTransfer(double outputVoltage, double sourceVoltage) => outputVoltage / sourceVoltage;
    public static double FilterNodeVoltageTransfer(double outputVoltage, double inputNodeVoltage) => outputVoltage / inputNodeVoltage;

    public static double TransducerPowerGain(double absoluteVoltageTransfer, double sourceResistanceOhm, double loadResistanceOhm)
    {
        if (sourceResistanceOhm <= 0) throw new InvalidOperationException("Transducer gain requires Rs > 0 because available source power is undefined for an ideal zero-ohm source.");
        if (loadResistanceOhm <= 0) throw new ArgumentOutOfRangeException(nameof(loadResistanceOhm));
        return 4 * sourceResistanceOhm / loadResistanceOhm * absoluteVoltageTransfer * absoluteVoltageTransfer;
    }

    public static double InsertionPowerRatioRelativeToThrough(double absoluteVoltageTransfer, double sourceResistanceOhm, double loadResistanceOhm)
    {
        if (sourceResistanceOhm < 0 || loadResistanceOhm <= 0) throw new ArgumentOutOfRangeException(nameof(sourceResistanceOhm));
        var through = loadResistanceOhm / (sourceResistanceOhm + loadResistanceOhm);
        var normalizedVoltage = absoluteVoltageTransfer / through;
        return normalizedVoltage * normalizedVoltage;
    }
}
