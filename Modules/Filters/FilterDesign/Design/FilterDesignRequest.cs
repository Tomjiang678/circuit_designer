using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;

public sealed record FilterDesignRequest
{
    public required FilterMode Mode { get; init; }
    public required FilterResponseType ResponseType { get; init; }
    public required FilterApproximation Approximation { get; init; }
    public required FilterImplementationType Implementation { get; init; }
    public required FilterSignalArchitecture SignalArchitecture { get; init; }
    public required FilterOrderMode OrderMode { get; init; }
    public required int SpecifiedOrder { get; init; }
    public required double PassbandRippleDb { get; init; }
    public required double StopbandAttenuationDb { get; init; }
    public required double PassbandGain { get; init; }
    public required double PassbandEdgeHz { get; init; }
    public required double StopbandEdgeHz { get; init; }
    public required double StopbandLowerHz { get; init; }
    public required double PassbandLowerHz { get; init; }
    public required double PassbandUpperHz { get; init; }
    public required double StopbandUpperHz { get; init; }
    public required double SourceResistanceOhm { get; init; }
    public required double LoadResistanceOhm { get; init; }
    public required double PreferredResistanceOhm { get; init; }
    public required ComponentSelectionConstraints ComponentConstraints { get; init; }
    public LcTerminationMode LcTerminationMode { get; init; } = LcTerminationMode.DoublyTerminated;
    public LcTerminationPolicy LcTerminationPolicy { get; init; } = LcTerminationPolicy.MatchedUserTermination;
    public LcLadderOrientation LcLadderOrientation { get; init; } = LcLadderOrientation.Auto;
    public PassiveResponseReference PassiveResponseReference { get; init; } = PassiveResponseReference.TransducerGain;
    public PassiveRcTopology PassiveRcTopology { get; init; } = PassiveRcTopology.FirstOrderLowPass;

    public static FilterDesignRequest FromState(FilterDesignState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new FilterDesignRequest
        {
            Mode = state.Mode,
            ResponseType = state.ResponseType,
            Approximation = state.Approximation,
            Implementation = state.Implementation,
            SignalArchitecture = state.SignalArchitecture,
            OrderMode = state.OrderMode,
            SpecifiedOrder = state.SpecifiedOrder,
            PassbandRippleDb = state.PassbandRippleDb,
            StopbandAttenuationDb = state.StopbandAttenuationDb,
            PassbandGain = state.PassbandGain,
            PassbandEdgeHz = state.PassbandEdgeHz,
            StopbandEdgeHz = state.StopbandEdgeHz,
            StopbandLowerHz = state.StopbandLowerHz,
            PassbandLowerHz = state.PassbandLowerHz,
            PassbandUpperHz = state.PassbandUpperHz,
            StopbandUpperHz = state.StopbandUpperHz,
            SourceResistanceOhm = state.SourceResistanceOhm,
            LoadResistanceOhm = state.LoadResistanceOhm,
            PreferredResistanceOhm = state.PreferredResistanceOhm,
            ComponentConstraints = new ComponentSelectionConstraints
            {
                ResistorSeries = state.ResistorSeries,
                CapacitorSeries = state.CapacitorSeries,
                InductorSeries = state.InductorSeries,
                ResistorTolerancePercent = state.ResistorTolerancePercent,
                CapacitorTolerancePercent = state.CapacitorTolerancePercent,
                InductorTolerancePercent = state.InductorTolerancePercent,
                ResistorMinimumOhm = state.PreferredResistorMinOhm,
                ResistorMaximumOhm = state.PreferredResistorMaxOhm,
                CapacitorMinimumF = state.Mode == FilterMode.Passive && state.Implementation == FilterImplementationType.Lc ? state.PassiveLcCapacitorMinimumF : state.PreferredCapacitorMinF,
                CapacitorMaximumF = state.Mode == FilterMode.Passive && state.Implementation == FilterImplementationType.Lc ? state.PassiveLcCapacitorMaximumF : state.PreferredCapacitorMaxF,
                InductorMinimumH = state.InductorMinimumH,
                InductorMaximumH = state.InductorMaximumH,
                OptimizeStandardValues = state.OptimizeStandardValues,
                OptimizationTarget = state.OptimizationTarget
            },
            LcTerminationMode = state.LcTerminationMode,
            LcTerminationPolicy = state.LcTerminationPolicy,
            LcLadderOrientation = state.LcLadderOrientation,
            PassiveResponseReference = state.Implementation == FilterImplementationType.Lc
                ? PassiveResponseReference.InsertionLossRelativeToThrough
                : PassiveResponseReference.AbsoluteVoltageTransfer,
            PassiveRcTopology = state.PassiveRcTopology
        };
    }
}
