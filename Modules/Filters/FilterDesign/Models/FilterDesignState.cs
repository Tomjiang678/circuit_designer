using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;

namespace CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;

public sealed class FilterDesignState
{
    public event EventHandler? StateChanged;
    public FilterMode Mode { get; set; } = FilterMode.Active;
    public FilterResponseType ResponseType { get; set; } = FilterResponseType.LowPass;
    public FilterApproximation Approximation { get; set; } = FilterApproximation.Butterworth;
    public FilterImplementationType Implementation { get; set; } = FilterImplementationType.SallenKey;
    public FilterSignalArchitecture SignalArchitecture { get; set; } = FilterSignalArchitecture.SingleEnded;
    public FilterOrderMode OrderMode { get; set; } = FilterOrderMode.CalculateAutomatically;
    public int SpecifiedOrder { get; set; } = 2;
    public BandFrequencyInputMode BandInputMode { get; set; } = BandFrequencyInputMode.Edges;
    public double PassbandRippleDb { get; set; } = 1;
    public double StopbandAttenuationDb { get; set; } = 40;
    public double PassbandGain { get; set; } = 1;
    public double PassbandEdgeHz { get; set; } = 1_000;
    public double StopbandEdgeHz { get; set; } = 3_000;
    public double StopbandLowerHz { get; set; } = 500;
    public double PassbandLowerHz { get; set; } = 900;
    public double PassbandUpperHz { get; set; } = 1_100;
    public double StopbandUpperHz { get; set; } = 1_500;
    public double CenterFrequencyHz { get; set; } = 1_000;
    public double PassbandBandwidthHz { get; set; } = 200;
    public double StopbandBandwidthHz { get; set; } = 1_000;
    public double SourceResistanceOhm { get; set; }
    public LcTerminationMode LcTerminationMode { get; set; } = LcTerminationMode.IdealVoltageSource;
    public double LoadResistanceOhm { get; set; } = 50;
    public double PreferredResistanceOhm { get; set; } = 10_000;
    public double PreferredResistorMinOhm { get; set; } = 1_000;
    public double PreferredResistorMaxOhm { get; set; } = 100_000;
    public double PreferredCapacitorMinF { get; set; } = 100e-12;
    public double PreferredCapacitorMaxF { get; set; } = 1e-6;
    public StandardComponentSeries ResistorSeries { get; set; } = StandardComponentSeries.E24;
    public StandardComponentSeries CapacitorSeries { get; set; } = StandardComponentSeries.E24;
    public StandardComponentSeries InductorSeries { get; set; } = StandardComponentSeries.E24;
    public double ResistorTolerancePercent { get; set; } = 1;
    public double CapacitorTolerancePercent { get; set; } = 5;
    public double InductorTolerancePercent { get; set; } = 5;
    public double InductorMinimumH { get; set; } = 100e-9;
    public double InductorMaximumH { get; set; } = 10;
    public double PassiveLcCapacitorMinimumF { get; set; } = 1e-12;
    public double PassiveLcCapacitorMaximumF { get; set; } = 10e-3;
    public bool OptimizeStandardValues { get; set; }
    public FilterOptimizationTarget OptimizationTarget { get; set; } = FilterOptimizationTarget.MinimizeFrequencyError;

    public void SelectResponseType(FilterResponseType responseType)
    {
        ResponseType = responseType;
        if (responseType == FilterResponseType.HighPass && StopbandEdgeHz >= PassbandEdgeHz)
        {
            StopbandEdgeHz = 1_000;
            PassbandEdgeHz = 3_000;
        }
        else if (responseType == FilterResponseType.LowPass && PassbandEdgeHz >= StopbandEdgeHz)
        {
            PassbandEdgeHz = 1_000;
            StopbandEdgeHz = 3_000;
        }
        else if (responseType == FilterResponseType.BandStop &&
                 !(PassbandLowerHz < StopbandLowerHz && StopbandLowerHz < StopbandUpperHz && StopbandUpperHz < PassbandUpperHz))
        {
            PassbandLowerHz = 500;
            StopbandLowerHz = 750;
            StopbandUpperHz = 1_000;
            PassbandUpperHz = 1_500;
        }
        else if (responseType == FilterResponseType.BandPass &&
                 !(StopbandLowerHz < PassbandLowerHz && PassbandLowerHz < PassbandUpperHz && PassbandUpperHz < StopbandUpperHz))
        {
            StopbandLowerHz = 500;
            PassbandLowerHz = 900;
            PassbandUpperHz = 1_100;
            StopbandUpperHz = 1_500;
        }
        NormalizeImplementation();
    }

    private void NormalizeImplementation()
    {
        if (Mode == FilterMode.Active)
        {
            Implementation = ResponseType == FilterResponseType.BandStop
                ? FilterImplementationType.BainterNotch
                : Implementation is FilterImplementationType.SallenKey or FilterImplementationType.MultipleFeedback
                    ? Implementation
                    : FilterImplementationType.SallenKey;
            return;
        }

        // Passive BP/BS cannot be realized by the current first-order RC path.
        // Select LC immediately so the wizard never carries an impossible RC state
        // through Specifications/Approximation before the user reaches step 4.
        Implementation = ResponseType is FilterResponseType.BandPass or FilterResponseType.BandStop
            ? FilterImplementationType.Lc
            : Implementation is FilterImplementationType.Rc or FilterImplementationType.Lc
                ? Implementation
                : FilterImplementationType.Rc;
    }

    public void NotifyChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
