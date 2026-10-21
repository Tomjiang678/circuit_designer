using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Enums;
using CircuitModuleDesigner.Modules.SignalAttenuationMatching.Models;

namespace CircuitModuleDesigner.Modules.SignalAttenuationMatching.Validation;

public static class DesignRequestValidator
{
    public static IReadOnlyList<AttenuationMatchingDiagnostic> Validate(AttenuationMatchingDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = new List<AttenuationMatchingDiagnostic>();
        void Error(string code, string description, string? parameter = null, double? actual = null, string? expected = null, string? action = null) =>
            diagnostics.Add(new(code, DiagnosticSeverity.Error, description, parameter, actual, expected, action));

        ValidatePort(request.Source, "Source", Error);
        ValidatePort(request.Load, "Load", Error);
        if (!(request.DesignFrequencyHz > 0) || !double.IsFinite(request.DesignFrequencyHz))
            Error("INVALID_DESIGN_FREQUENCY", "The design frequency must be finite and greater than zero.", "Design frequency", request.DesignFrequencyHz, "> 0 Hz", null);
        if (!(request.StartFrequencyHz > 0) || !(request.StopFrequencyHz > request.StartFrequencyHz) ||
            !double.IsFinite(request.StartFrequencyHz) || !double.IsFinite(request.StopFrequencyHz))
            Error("INVALID_SWEEP_RANGE", "The sweep range must be finite, positive and ordered.", "Start/stop frequency", null, "0 < start < stop");
        if (request.DesignFrequencyHz < request.StartFrequencyHz || request.DesignFrequencyHz > request.StopFrequencyHz)
            Error("DESIGN_FREQUENCY_OUTSIDE_SWEEP", "The design frequency must lie inside the sweep range.", "Design frequency", request.DesignFrequencyHz, $"{request.StartFrequencyHz:G6}..{request.StopFrequencyHz:G6} Hz");
        if (request.SweepPoints is < 21 or > 4001)
            Error("INVALID_SWEEP_POINTS", "The sweep point count must be between 21 and 4001.", "Sweep points", request.SweepPoints, "21..4001");
        if (!double.IsFinite(request.Signal.Value) || request.Signal.Value <= 0)
            Error("INVALID_SIGNAL_LEVEL", "The source signal level must be finite and positive.", "Source signal", request.Signal.Value, "> 0");
        if (request.Signal.Unit == SignalLevelUnit.AvailablePowerDbm && request.Signal.Value < -100)
            Error("INVALID_SIGNAL_LEVEL", "The source power is below the supported dBm range.", "Source signal", request.Signal.Value, ">= -100 dBm");

        if (!double.IsFinite(request.Attenuation.TargetAttenuationDb) || request.Attenuation.TargetAttenuationDb < 0)
            Error("INVALID_ATTENUATION", "Target attenuation must be finite and non-negative.", "Target attenuation", request.Attenuation.TargetAttenuationDb, ">= 0 dB");
        if (!double.IsFinite(request.Attenuation.AllowedAttenuationErrorDb) || request.Attenuation.AllowedAttenuationErrorDb <= 0)
            Error("INVALID_ATTENUATION_TOLERANCE", "Allowed attenuation error must be finite and positive.", "Allowed attenuation error", request.Attenuation.AllowedAttenuationErrorDb, "> 0 dB");
        if (!(request.Attenuation.MinimumInputReturnLossDb > 0) || !(request.Attenuation.MinimumOutputReturnLossDb > 0))
            Error("INVALID_RETURN_LOSS_REQUIREMENT", "Return-loss requirements must be positive dB values.", "Minimum return loss", null, "> 0 dB");
        if (request.Attenuation.MaximumInputPowerWatt is { } inputPower && (!(inputPower > 0) || !double.IsFinite(inputPower)))
            Error("INVALID_MAX_INPUT_POWER", "Maximum input power must be finite and positive.", "Maximum input power", inputPower, "> 0 W");

        if (!(request.Matching.TargetInputReturnLossDb > 0) || !double.IsFinite(request.Matching.TargetInputReturnLossDb))
            Error("INVALID_MATCHING_RETURN_LOSS", "Target input return loss must be finite and positive.", "Target input return loss", request.Matching.TargetInputReturnLossDb, "> 0 dB");
        if (request.Matching.TargetOutputReturnLossDb is { } outputReturnLoss && (!(outputReturnLoss > 0) || !double.IsFinite(outputReturnLoss)))
            Error("INVALID_MATCHING_RETURN_LOSS", "Target output return loss must be finite and positive.", "Target output return loss", outputReturnLoss, "> 0 dB");
        if (!(request.Matching.MaximumInsertionLossDb >= 0) || !double.IsFinite(request.Matching.MaximumInsertionLossDb))
            Error("INVALID_INSERTION_LOSS", "Maximum insertion loss must be finite and non-negative.", "Maximum insertion loss", request.Matching.MaximumInsertionLossDb, ">= 0 dB");
        if (request.Matching.TargetBandwidthHz < 0 || !double.IsFinite(request.Matching.TargetBandwidthHz))
            Error("INVALID_BANDWIDTH", "Target bandwidth must be finite and non-negative.", "Target bandwidth", request.Matching.TargetBandwidthHz, ">= 0 Hz");

        ValidateConstraints(request.Components, Error);
        if (request.MaximumComponentCount is { } count && count < 1)
            Error("INVALID_COMPONENT_COUNT", "Maximum component count must be at least one.", "Maximum component count", count, ">= 1");
        if (request.Topology.Mode == TopologySelectionMode.Manual && request.Topology.Family is null)
            Error("MISSING_TOPOLOGY", "Manual topology selection requires a topology family.", "Topology family", null, "A topology family");
        if (request.Topology.Mode == TopologySelectionMode.Manual && request.Topology.Family is { } family && !FamilyAllowed(family, request.Mode))
            Error("TOPOLOGY_NOT_ALLOWED_IN_MODE", $"Topology {family} is not valid for design mode {request.Mode}.", "Topology family", null, "A family valid for the selected mode");
        if (request.Mode == DesignMode.ImpedanceMatching && request.Attenuation.TargetAttenuationDb > 1e-9)
            diagnostics.Add(new("ATTENUATION_IGNORED_IN_MATCHING_MODE", DiagnosticSeverity.Info,
                "Attenuation target is ignored in Impedance Matching mode; matching is the primary objective."));
        return diagnostics;
    }

    private static void ValidatePort(PortImpedanceModel port, string name, Action<string, string, string?, double?, string?, string?> error)
    {
        if (!double.IsFinite(port.ResistanceOhm) || !(port.ResistanceOhm > 0))
            error("INVALID_PORT_RESISTANCE", $"{name} resistance must be finite and positive.", $"{name} resistance", port.ResistanceOhm, "> 0 ohm", null);
        if (!double.IsFinite(port.ReactanceOhm))
            error("INVALID_PORT_REACTANCE", $"{name} reactance must be finite.", $"{name} reactance", port.ReactanceOhm, "finite ohm", null);
        if (port.Type == PortImpedanceType.PureResistance && Math.Abs(port.ReactanceOhm) > 1e-12)
            error("INVALID_PORT_MODEL", $"{name} is configured as a pure resistance but has a nonzero reactance.", $"{name} reactance", port.ReactanceOhm, "0 ohm", null);
    }

    private static void ValidateConstraints(ComponentConstraints constraints, Action<string, string, string?, double?, string?, string?> error)
    {
        if (!(constraints.MinimumResistanceOhm > 0) || !(constraints.MaximumResistanceOhm >= constraints.MinimumResistanceOhm))
            error("INVALID_RESISTOR_RANGE", "Resistance range must be positive and ordered.", "Resistance range", null, "0 < min <= max", null);
        if (!(constraints.MinimumCapacitanceFarad > 0) || !(constraints.MaximumCapacitanceFarad >= constraints.MinimumCapacitanceFarad))
            error("INVALID_CAPACITOR_RANGE", "Capacitance range must be positive and ordered.", "Capacitance range", null, "0 < min <= max", null);
        if (!(constraints.MinimumInductanceHenry > 0) || !(constraints.MaximumInductanceHenry >= constraints.MinimumInductanceHenry))
            error("INVALID_INDUCTOR_RANGE", "Inductance range must be positive and ordered.", "Inductance range", null, "0 < min <= max", null);
        if (!(constraints.MaximumResistorDissipationWatt > 0) || !double.IsFinite(constraints.MaximumResistorDissipationWatt))
            error("INVALID_POWER_LIMIT", "Maximum resistor dissipation must be finite and positive.", "Resistor dissipation", constraints.MaximumResistorDissipationWatt, "> 0 W", null);
        if (constraints.CapacitorEsrOhm < 0 || constraints.InductorSeriesResistanceOhm < 0 || constraints.InductorQ < 0)
            error("INVALID_PARASITIC", "ESR, inductor series resistance and Q cannot be negative.", "Parasitic model", null, ">= 0", null);
        if (constraints.MaximumSearchCombinations is < 1 or > 200_000)
            error("INVALID_SEARCH_BUDGET", "Maximum search combinations must be between 1 and 200000.", "Search combinations", constraints.MaximumSearchCombinations, "1..200000", null);
        if (constraints.MaximumCandidateCount is < 1 or > 100)
            error("INVALID_CANDIDATE_LIMIT", "Maximum candidate count must be between 1 and 100.", "Candidate count", constraints.MaximumCandidateCount, "1..100", null);
    }

    private static bool FamilyAllowed(TopologyFamily family, DesignMode mode) => mode switch
    {
        DesignMode.Attenuation => family is TopologyFamily.TPads or TopologyFamily.PiPads or TopologyFamily.LPads or TopologyFamily.CascadedPads,
        DesignMode.ImpedanceMatching => family is TopologyFamily.LMatching or TopologyFamily.PiMatching or TopologyFamily.TMatching,
        _ => false
    };
}
