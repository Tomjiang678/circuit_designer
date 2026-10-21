using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Synthesis;

namespace CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Analysis;

public sealed class DynamicTimeDomainAnalysisService
{
    public DynamicTimeDomainAnalysisResult Analyze(AnalogComputeDesignRequest request,IReadOnlyList<SelectedAnalogComponent> components)
        =>Analyze(request,components,null);

    public DynamicTimeDomainAnalysisResult AnalyzeAtFrequency(AnalogComputeDesignRequest request,
        IReadOnlyList<SelectedAnalogComponent> components,double frequencyHz)
    {
        if(!double.IsFinite(frequencyHz)||frequencyHz<=0)
            throw new ArgumentOutOfRangeException(nameof(frequencyHz),"Frequency must be finite and positive.");
        return Analyze(request,components,frequencyHz);
    }

    private static DynamicTimeDomainAnalysisResult Analyze(AnalogComputeDesignRequest request,
        IReadOnlyList<SelectedAnalogComponent> components,double? frequencyHz)
    {
        var values=components.ToDictionary(component=>component.Reference,component=>component.SelectedValue);
        return request.Operation switch
        {
            AnalogComputeOperationType.PracticalIntegrator=>Integrator(request,values,frequencyHz),
            AnalogComputeOperationType.PracticalDifferentiator=>Differentiator(request,values,frequencyHz),
            _=>throw new ArgumentException("Time-domain dynamic analysis supports only Integrator and Differentiator.",nameof(request))
        };
    }

    private static DynamicTimeDomainAnalysisResult Integrator(AnalogComputeDesignRequest request,
        IReadOnlyDictionary<string,double> values,double? requestedFrequency)
    {
        var parameters=DynamicOperationParameters.Resolve(request);
        var actualKi=1/(values["R1"]*values["C1"]);
        var tau=values["R2"]*values["C1"];
        var dcGain=values["R2"]/values["R1"];
        var frequency=requestedFrequency??Math.Max(parameters.FirstCornerHz*5,
            Math.Min(Math.Sqrt(parameters.FirstCornerHz*parameters.SecondCornerHz),parameters.SecondCornerHz/5));
        if(requestedFrequency is null)frequency=LimitFrequencyForOpAmp(request,frequency);
        var swing=AvailableSwing(request);
        var amplitude=Math.Min(1d,.8*swing*frequency/request.IntegrationConstantPerSecond);
        if(!request.OpAmp.UseIdealOpAmp&&double.IsFinite(request.OpAmp.SlewRateVoltPerSecond))
            amplitude=Math.Min(amplitude,.5*request.OpAmp.SlewRateVoltPerSecond/Math.Max(actualKi,1e-30));
        amplitude=Math.Max(amplitude,1e-6);
        const int samplesPerCycle=240;const int warmupCycles=10;const int visibleCycles=3;
        var dt=1/(frequency*samplesPerCycle);var total=(warmupCycles+visibleCycles)*samplesPerCycle;
        var selected=0d;var ideal=0d;var decay=Math.Exp(-dt/tau);var points=new List<DynamicTimeDomainPoint>(visibleCycles*samplesPerCycle+1);
        for(var index=0;index<=total;index++)
        {
            var cycle=index/(double)samplesPerCycle;var input=(cycle-Math.Floor(cycle))<.5?amplitude:-amplitude;
            ideal+=-request.IntegrationConstantPerSecond*input*dt;
            selected=selected*decay-dcGain*input*(1-decay);
            if(index>=warmupCycles*samplesPerCycle)
                points.Add(new(cycle-warmupCycles,input,ideal,Math.Clamp(selected,-swing,swing)));
        }
        Center(points);
        return Result("Square Wave","Triangle Wave",frequency,amplitude,points);
    }

    private static DynamicTimeDomainAnalysisResult Differentiator(AnalogComputeDesignRequest request,
        IReadOnlyDictionary<string,double> values,double? requestedFrequency)
    {
        var parameters=DynamicOperationParameters.Resolve(request);
        var frequency=requestedFrequency??Math.Max(.001,parameters.UsefulUpperFrequencyHz/5);
        if(requestedFrequency is null)frequency=LimitFrequencyForOpAmp(request,frequency);
        var actualKd=values["R2"]*values["C1"];
        var tau1=values["R1"]*values["C1"];
        var tau2=values["R2"]*values["C2"];
        var swing=AvailableSwing(request);
        var amplitude=Math.Min(1d,.2*swing/(4*request.DifferentiationConstantSecond*frequency));
        amplitude=Math.Max(amplitude,1e-6);
        const int samplesPerCycle=320;const int warmupCycles=14;const int visibleCycles=3;
        var dt=1/(frequency*samplesPerCycle);var total=(warmupCycles+visibleCycles)*samplesPerCycle;
        var highPass=0d;var output=0d;var points=new List<DynamicTimeDomainPoint>(visibleCycles*samplesPerCycle+1);
        for(var index=0;index<=total;index++)
        {
            var cycle=index/(double)samplesPerCycle;var phase=cycle-Math.Floor(cycle);
            var input=phase<.5?-amplitude+4*amplitude*phase:3*amplitude-4*amplitude*phase;
            var slope=phase<.5?4*amplitude*frequency:-4*amplitude*frequency;
            StepCascadeExact(ref highPass,ref output,slope,actualKd,tau1,tau2,dt);
            if(index>=warmupCycles*samplesPerCycle)
                points.Add(new(cycle-warmupCycles,input,-request.DifferentiationConstantSecond*slope,Math.Clamp(output,-swing,swing)));
        }
        return Result("Triangle Wave","Square Wave",frequency,amplitude,points);
    }

    private static void StepCascadeExact(ref double highPass,ref double output,double slope,double kd,double tau1,double tau2,double dt)
    {
        // Exact zero-order-hold state transition for a constant triangle-wave slope.
        // This remains stable when the visible sample interval is much longer than the high-frequency pole time constant.
        var equilibriumHighPass=tau1*slope;
        var transient=highPass-equilibriumHighPass;
        var firstDecay=Math.Exp(-dt/tau1);
        var secondDecay=Math.Exp(-dt/tau2);
        var equilibriumOutput=-kd*slope;
        var transientContribution=Math.Abs(tau1-tau2)>1e-12*Math.Max(tau1,tau2)
            ? -kd*transient*(firstDecay-secondDecay)/(tau1-tau2)
            : -kd*transient*dt*firstDecay/(tau1*tau1);
        output=output*secondDecay+equilibriumOutput*(1-secondDecay)+transientContribution;
        highPass=equilibriumHighPass+transient*firstDecay;
    }

    private static DynamicTimeDomainAnalysisResult Result(string inputName,string outputName,double frequency,double inputAmplitude,List<DynamicTimeDomainPoint> points)
    {
        var targetAmplitude=(points.Max(point=>point.TargetOutputVoltage)-points.Min(point=>point.TargetOutputVoltage))/2;
        var actualAmplitude=(points.Max(point=>point.ActualOutputVoltage)-points.Min(point=>point.ActualOutputVoltage))/2;
        var error=targetAmplitude>0?100*(actualAmplitude-targetAmplitude)/targetAmplitude:0;
        return new(inputName,outputName,frequency,inputAmplitude,targetAmplitude,actualAmplitude,error,points);
    }

    private static void Center(List<DynamicTimeDomainPoint> points)
    {
        var targetCenter=(points.Max(point=>point.TargetOutputVoltage)+points.Min(point=>point.TargetOutputVoltage))/2;
        var actualCenter=(points.Max(point=>point.ActualOutputVoltage)+points.Min(point=>point.ActualOutputVoltage))/2;
        for(var index=0;index<points.Count;index++)points[index]=points[index] with
        {TargetOutputVoltage=points[index].TargetOutputVoltage-targetCenter,ActualOutputVoltage=points[index].ActualOutputVoltage-actualCenter};
    }

    private static double LimitFrequencyForOpAmp(AnalogComputeDesignRequest request,double frequency)=>request.OpAmp.UseIdealOpAmp
        ? frequency
        : Math.Max(.001,Math.Min(frequency,request.OpAmp.GainBandwidthHz/100));

    private static double AvailableSwing(AnalogComputeDesignRequest request)=>Math.Max(1e-6,Math.Min(
        request.SupplyVccVoltage-request.OpAmp.OutputSwingHeadroomVoltage,
        -request.SupplyVeeVoltage-request.OpAmp.OutputSwingHeadroomVoltage));
}
