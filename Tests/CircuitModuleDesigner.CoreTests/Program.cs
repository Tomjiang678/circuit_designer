using System.Numerics;
using CircuitModuleDesigner.Core.Analysis;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Circuit.Spice;
using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Core.Schematic.Symbols;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Services;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;

var tests = new (string Name, Action Run)[]
{
    ("Butterworth order", TestButterworthOrder),
    ("Chebyshev-I order", TestChebyshevOrder),
    ("Chebyshev-I ladder prototype", TestChebyshevLadderPrototype),
    ("Butterworth LHP poles", TestButterworthPoles),
    ("Bessel normalized poles and order", TestBesselPrototypeAndOrder),
    ("FilterPro tabulated prototypes", TestTabulatedPrototypes),
    ("FilterPro tabulated order search", TestTabulatedOrderSearch),
    ("Pole pair section Q", TestSectionDecomposition),
    ("LP to HP transformation", TestHighPassTransformation),
    ("BP/BS center transformation", TestBandTransformations),
    ("Band-pass cascade gain normalization", TestBandPassGainNormalization),
    ("Band-stop shared notch zeros", TestBandStopSharedNotchZeros),
    ("Sallen-Key inverse verification", TestSallenKeyVerification),
    ("Sallen-Key band-pass inverse verification", TestSallenKeyBandPassVerification),
    ("MFB low-pass inverse verification", TestMfbLowPassVerification),
    ("MFB high-pass inverse verification", TestMfbHighPassVerification),
    ("MFB band-pass inverse verification", TestMfbBandPassVerification),
    ("E-series optimization", TestStandardValueOptimization),
    ("Active LP end-to-end", TestActiveLowPassEndToEnd),
    ("Active HP Chebyshev end-to-end", TestActiveHighPassEndToEnd),
    ("Active Bessel end-to-end", TestActiveBesselEndToEnd),
    ("Sallen-Key band-pass end-to-end", TestSallenKeyBandPassEndToEnd),
    ("FilterPro responses end-to-end", TestTabulatedResponsesEndToEnd),
    ("Central support matrix", TestSupportMatrix),
    ("Active supported matrix end-to-end", TestActiveSupportedMatrixEndToEnd),
    ("Passive RC end-to-end", TestPassiveRcEndToEnd),
    ("Passive LC end-to-end", TestPassiveLcEndToEnd),
    ("Passive LC high-pass end-to-end", TestPassiveLcHighPassEndToEnd),
    ("Passive LC band-pass end-to-end", TestPassiveLcBandPassEndToEnd),
    ("Passive LC band-stop end-to-end", TestPassiveLcBandStopEndToEnd),
    ("Chebyshev-I passive LC end-to-end", TestChebyshevLcEndToEnd),
    ("LC band transformation resonance", TestLcBandTransformationResonance),
    ("LC standard-value optimization", TestLcStandardValueOptimization),
    ("Schematic pin alignment", TestSchematicPinAlignment),
    ("Schematic component clearance", TestSchematicComponentClearance),
    ("Schematic wires avoid op-amps", TestSchematicWiresAvoidOpAmps),
    ("Selected topology reference schematics", TestSelectedTopologyReferences),
    ("MFB low-pass end-to-end", TestMfbLowPassEndToEnd),
    ("MFB high-pass end-to-end", TestMfbHighPassEndToEnd)
    ,("MFB band-pass end-to-end", TestMfbBandPassEndToEnd)
    ,("Active band-stop end-to-end", TestActiveBandStopEndToEnd)
    ,("Integrated active section gain", TestIntegratedActiveSectionGain)
    ,("High-pass default edge ordering", TestHighPassDefaultEdgeOrdering)
    ,("Band-stop default edge ordering", TestBandStopDefaultEdgeOrdering)
    ,("Op-amp input symbol separation", TestOpAmpInputSymbolSeparation)
    ,("Op-amp inputs electrically isolated", TestOpAmpInputsElectricallyIsolated)
    ,("Op-amp input drawing isolation", TestOpAmpInputDrawingIsolation)
    ,("Schematic junctions are real branches", TestSchematicJunctionsAreRealBranches)
    ,("LC routing has no retraced needles", TestLcRoutingHasNoRetracedNeedles)
    ,("Component ranges are enforced", TestComponentRangesAreEnforced)
    ,("Default active band-stop is practical", TestDefaultActiveBandStopIsPractical)
    ,("Bainter wires avoid component bodies", TestBainterWiresAvoidComponentBodies)
    ,("Passive band modes select LC", TestPassiveBandModesSelectLc)
    ,("RC AC analysis known point", TestRcAcAnalysisKnownPoint)
    ,("Designed filter actual analysis", TestDesignedFilterActualAnalysis)
    ,("Passive LC insertion-gain analysis", TestPassiveLcInsertionGainAnalysis)
    ,("Multisim CIR export", TestMultisimCirExport)
};

var failures = new List<string>();
foreach (var test in tests)
{
    if (args.Length > 0 && !test.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)) continue;
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failures.Add($"FAIL {test.Name}: {exception.Message}"); Console.WriteLine(failures[^1]); }
}
if (failures.Count > 0) throw new InvalidOperationException($"{failures.Count} test(s) failed.");
Console.WriteLine($"All {tests.Length} filter design tests passed.");

static void TestButterworthOrder() => Equal(8, new ButterworthOrderCalculator().CalculateMinimumOrder(1, 40, 2));
static void TestChebyshevOrder() => Equal(5, new ChebyshevOrderCalculator().CalculateMinimumOrder(1, 40, 2));

static void TestChebyshevLadderPrototype()
{
    var values = new ChebyshevPrototypeDesigner().Design(3, 1).LadderGValues;
    Equal(5, values.Count); Require(Nearly(values[1], 2.0236, 5e-4), "Unexpected 1 dB Chebyshev g1."); Require(Nearly(values[2], 0.9941, 5e-4), "Unexpected 1 dB Chebyshev g2."); Require(Nearly(values[3], 2.0236, 5e-4), "Unexpected 1 dB Chebyshev g3."); Require(Nearly(values[4], 1, 1e-12), "Odd-order Chebyshev termination must be unity.");
}

static void TestButterworthPoles()
{
    var prototype = new ButterworthPrototypeDesigner().Design(5, 1);
    Require(prototype.Poles.All(pole => pole.Real < 0), "All Butterworth poles must be in the left half-plane.");
    Require(prototype.Poles.All(pole => Nearly(Complex.Abs(pole), 1, 1e-12)), "Butterworth poles must lie on the unit circle.");
}

static void TestBesselPrototypeAndOrder()
{
    var prototype = new BesselPrototypeDesigner().Design(2, 1);
    Require(prototype.Poles.All(pole => pole.Real < 0), "Bessel poles must be stable.");
    Require(Nearly(prototype.Poles[0].Real, -1.101601, 2e-5), "Bessel order-2 real pole coordinate mismatch.");
    Require(Nearly(Math.Abs(prototype.Poles[0].Imaginary), 0.63601, 2e-5), "Bessel order-2 imaginary pole coordinate mismatch.");
    var order = new BesselOrderCalculator().CalculateMinimumOrder(1, 40, 10); Require(order >= 1 && order <= 10, "Bessel numerical order search returned an invalid order.");
}

static void TestTabulatedPrototypes()
{
    var linear = new TabulatedPrototypeDesigner(FilterApproximation.LinearPhase05Degree).Design(3, 1);
    Require(linear.Poles.Count == 3 && linear.Poles.All(p => p.Real < 0), "Linear-phase table did not produce three stable poles.");
    Require(linear.Poles.Any(p => Nearly(p.Real, -.6969, 1e-5) && Nearly(Math.Abs(p.Imaginary), 1.1318, 1e-5)), "Linear-phase 0.5 degree table coordinate mismatch.");
    var gaussian = new TabulatedPrototypeDesigner(FilterApproximation.GaussianTo12Db).Design(5, 1);
    Require(gaussian.Poles.Count == 5 && gaussian.Poles.Any(p => Nearly(p.Real, -.6650, 1e-5) && Nearly(p.Imaginary, 0, 1e-9)), "Gaussian-to-12 dB real pole mismatch.");
}

static void TestTabulatedOrderSearch()
{
    foreach (var approximation in new[] { FilterApproximation.GaussianTo6Db, FilterApproximation.GaussianTo12Db, FilterApproximation.LinearPhase005Degree, FilterApproximation.LinearPhase05Degree })
    {
        var calculator = new TabulatedPrototypeOrderCalculator(approximation); var order = calculator.CalculateMinimumOrder(1, 40, 10);
        var poles = TabulatedPrototypeCatalog.GetPoles(approximation, order); var scale = calculator.CalculateCutoffAngularFrequency(1, 1, order);
        var stopAttenuation = TabulatedPrototypeOrderCalculator.AttenuationDb(poles, 10 / scale);
        Require(stopAttenuation >= 40 - 1e-8, $"{approximation} order search does not meet stopband attenuation.");
    }
}

static void TestSectionDecomposition()
{
    var prototype = new ButterworthPrototypeDesigner().Design(2, 1);
    var transformed = new AnalogFrequencyTransformationService().TransformLowPassOrHighPass(prototype, FilterResponseType.LowPass, 2 * Math.PI * 1_000);
    var section = new FilterSectionBuilder().Build(transformed).Single();
    Require(Nearly(section.PoleFrequencyHz, 1_000, 1e-9), "Section pole frequency mismatch.");
    Require(Nearly(section.Q, Math.Sqrt(0.5), 1e-9), "Second-order Butterworth Q mismatch.");
}

static void TestHighPassTransformation()
{
    var prototype = new ButterworthPrototypeDesigner().Design(1, 1);
    var transformed = new AnalogFrequencyTransformationService().TransformLowPassOrHighPass(prototype, FilterResponseType.HighPass, 2 * Math.PI * 2_000);
    Require(Nearly(Complex.Abs(transformed.Poles.Single()), 2 * Math.PI * 2_000, 1e-9), "HP pole frequency mismatch.");
}

static void TestBandTransformations()
{
    var prototype = new ButterworthPrototypeDesigner().Design(1, 1); var service = new AnalogFrequencyTransformationService(); const double center = 10_000; const double bandwidth = 2_000;
    foreach (var transformed in new[] { service.TransformBandPass(prototype, center, bandwidth), service.TransformBandStop(prototype, center, bandwidth) })
    {
        Equal(2, transformed.RealizedOrder); var product = transformed.Poles[0] * transformed.Poles[1]; Require(Nearly(product.Magnitude, center * center, 1e-9), "Band transformation must preserve center-frequency product.");
    }
}

static void TestBandPassGainNormalization()
{
    var prototype=new ButterworthPrototypeDesigner().Design(3,1); const double center=2*Math.PI*1_000; const double bandwidth=2*Math.PI*200;
    var sections=new FilterSectionBuilder().Build(new AnalogFrequencyTransformationService().TransformBandPass(prototype,center,bandwidth)); Complex total=Complex.One; var s=new Complex(0,center);
    foreach(var section in sections){var n=Evaluate(section.NumeratorCoefficients,s);var d=Evaluate(section.DenominatorCoefficients,s);total*=n/d;}
    Require(Nearly(total.Magnitude,1,1e-10),"Band-pass section cascade was not normalized to unity at f0.");
}

static void TestBandStopSharedNotchZeros()
{
    var prototype=new ButterworthPrototypeDesigner().Design(3,1); const double center=2*Math.PI*1_000; const double bandwidth=2*Math.PI*200;
    var sections=new FilterSectionBuilder().Build(new AnalogFrequencyTransformationService().TransformBandStop(prototype,center,bandwidth)); Complex atNotch=Complex.One,atDc=Complex.One;
    foreach(var section in sections){atNotch*=Evaluate(section.NumeratorCoefficients,new Complex(0,center))/Evaluate(section.DenominatorCoefficients,new Complex(0,center));atDc*=Evaluate(section.NumeratorCoefficients,Complex.Zero)/Evaluate(section.DenominatorCoefficients,Complex.Zero);}
    Require(atNotch.Magnitude<1e-10,"Band-stop sections do not share the transformed notch zero."); Require(Nearly(atDc.Magnitude,1,1e-10),"Band-stop cascade DC gain is not normalized.");
}

static void TestSallenKeyVerification()
{
    const double q = 0.7071067811865476; const double r = 10_000; var w0 = 2 * Math.PI * 1_000; var c1 = 1 / (2 * q * w0 * r); var c2 = 4 * q * q * c1;
    var actual = SallenKeySectionCalculator.Verify(FilterResponseType.LowPass, r, r, c1, c2);
    Require(Nearly(actual.frequency, 1_000, 1e-9) && Nearly(actual.q, q, 1e-9), "Sallen-Key inverse calculation mismatch.");
}

static void TestSallenKeyBandPassVerification()
{
    const double q=1;const double f=1_000;const double r1=10_000;var w=2*Math.PI*f;var c1=2/(w*r1);var inner=(6.5-1/q)/3;var r4=10_000d;var r5=r4/(inner-1);var natural=inner*q;var ratio=1/natural;var rb=10_000d;var ra=rb*(1-ratio)/ratio;
    var actual=SallenKeyBandPassSectionCalculator.Verify(r1,r1/3,2*r1,c1,c1/2,r4,r5,ra,rb);
    Require(Nearly(actual.FrequencyHz,f,1e-9)&&Nearly(actual.Q,q,1e-9)&&Nearly(actual.GainMagnitude,1,1e-9),"Sallen-Key BP inverse equations mismatch.");
}

static void TestMfbLowPassVerification()
{
    var actual = MfbLowPassSectionCalculator.Verify(15_400, 15_400, 3_480, 10e-9, 47e-9);
    Require(Nearly(actual.FrequencyHz, 1_000, 0.025), "TI MFB example frequency mismatch.");
    Require(Nearly(actual.Q, Math.Sqrt(0.5), 0.035), "TI MFB example Q mismatch.");
    Require(Nearly(actual.GainMagnitude, 1, 1e-12), "MFB gain verification mismatch.");
}

static void TestMfbHighPassVerification()
{
    const double q = 0.7071067811865476; const double r5 = 10_000; var r2 = 9 * q * q * r5;
    var c = 1 / (2 * Math.PI * 1_000 * Math.Sqrt(r2 * r5));
    var actual = MfbHighPassSectionCalculator.Verify(c, r2, c, c, r5);
    Require(Nearly(actual.FrequencyHz, 1_000, 1e-9), "MFB high-pass frequency mismatch.");
    Require(Nearly(actual.Q, q, 1e-9), "MFB high-pass Q mismatch.");
    Require(Nearly(actual.GainMagnitude, 1, 1e-12), "MFB high-pass gain mismatch.");
}

static void TestMfbBandPassVerification()
{
    const double q=2; const double r1=10_000; var c=q/(2*Math.PI*1_000*r1); var actual=MfbBandPassSectionCalculator.Verify(r1,2*r1,r1/(2*q*q-1),c,c);
    Require(Nearly(actual.FrequencyHz,1_000,1e-9),"MFB band-pass frequency mismatch."); Require(Nearly(actual.Q,q,1e-9),"MFB band-pass Q mismatch."); Require(Nearly(actual.GainMagnitude,1,1e-9),"MFB band-pass gain mismatch.");
}

static void TestStandardValueOptimization()
{
    var section = new AnalogFilterSection(1, SectionOrder.Second, FilterResponseType.LowPass, 1_000, 0.70710678, 1, [Math.Pow(2*Math.PI*1000,2)], [1,2*Math.PI*1000/0.70710678,Math.Pow(2*Math.PI*1000,2)]);
    var request = Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 1_000, 4_000, optimize: true);
    var result = new SallenKeySectionCalculator().Calculate(section, request);
    Require(result.Components.All(value => value.Series == null || StandardValueSeries.IsSeriesValue(value.SelectedValue, value.Series.Value, 5e-3)), "Selected values must belong to the requested E-series.");
}

static void TestActiveLowPassEndToEnd()
{
    var result = new FilterDesignEngine().Design(Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 1_000, 4_000));
    Require(result.IsImplemented, result.Message); Equal(4, result.PrototypeOrder); Equal(2, result.Sections.Count); Require(result.CircuitGraph!.Components.Count == 10, "A fourth-order Sallen-Key design must contain two RRC C + op-amp sections."); Require(result.SchematicDocument!.PlacedComponents.Count == result.CircuitGraph.Components.Count, "Every component must be placed.");
}

static void TestActiveHighPassEndToEnd()
{
    var result = new FilterDesignEngine().Design(WideRanges(Request(FilterMode.Active, FilterResponseType.HighPass, FilterApproximation.ChebyshevTypeI, FilterImplementationType.SallenKey, 2_000, 1_000)));
    Require(result.IsImplemented, result.Message); Equal(5, result.PrototypeOrder); Require(result.Sections.Count == 3, "A fifth-order design must contain one first-order and two second-order sections.");
}

static void TestActiveBesselEndToEnd()
{
    var result = new FilterDesignEngine().Design(Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Bessel, FilterImplementationType.SallenKey, 1_000, 10_000));
    Require(result.IsImplemented, result.Message); Require(result.PrototypePoles.All(pole => pole.Real < 0), "Generated Bessel design is unstable."); Require(result.CircuitGraph!.Components.Count > 0, "Bessel design did not generate a circuit.");
}

static void TestSallenKeyBandPassEndToEnd()
{
    var diagnostic=new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,1_000,2_000,manualOrder:1));
    Require(!HasOverlappingWireSegments(diagnostic.SchematicDocument!),"Sallen-Key band-pass schematic contains duplicate same-net wire geometry.");
    var result=new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,1_000,2_000,manualOrder:2));
    Require(result.IsImplemented,result.Message);Equal(2,result.Sections.Count);Equal(22,result.CircuitGraph!.Components.Count);Require(result.CircuitGraph.Components.Count(c=>c.Type==ComponentType.OpAmp)==4,"Each Sallen-Key BP section requires its filter amplifier and normalization buffer.");
    var optimized=new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,1_000,2_000,optimize:true,manualOrder:1));
    Require(optimized.IsImplemented,optimized.Message);Require(optimized.Sections.SelectMany(s=>s.Components).All(v=>v.Series==null||StandardValueSeries.IsSeriesValue(v.SelectedValue,v.Series.Value,5e-3)),"Sallen-Key BP optimizer selected a non-series value.");
}

static void TestTabulatedResponsesEndToEnd()
{
    foreach (var approximation in new[] { FilterApproximation.GaussianTo6Db, FilterApproximation.GaussianTo12Db, FilterApproximation.LinearPhase005Degree, FilterApproximation.LinearPhase05Degree })
    {
        var result = new FilterDesignEngine().Design(Request(FilterMode.Active, FilterResponseType.LowPass, approximation, FilterImplementationType.SallenKey, 1_000, 10_000, manualOrder: 3));
        Require(result.IsImplemented, $"{approximation}: {result.Message}"); Equal(3, result.PrototypeOrder); Require(result.CircuitGraph!.Components.Count > 0, $"{approximation} generated no circuit.");
    }
}

static void TestSupportMatrix()
{
    foreach(var approximation in Enum.GetValues<FilterApproximation>())
    foreach(var response in Enum.GetValues<FilterResponseType>())
    {
        foreach(var topology in new[]{FilterImplementationType.SallenKey,FilterImplementationType.MultipleFeedback,FilterImplementationType.BainterNotch})
        {var request=Request(FilterMode.Active,response,approximation,topology,1_000,2_000,manualOrder:3);var expected=response==FilterResponseType.BandStop?topology==FilterImplementationType.BainterNotch:topology is FilterImplementationType.SallenKey or FilterImplementationType.MultipleFeedback;Require(FilterSupportMatrix.Evaluate(request).IsSupported==expected,$"Unexpected active support decision for {approximation}/{response}/{topology}.");}
        foreach(var topology in new[]{FilterImplementationType.Rc,FilterImplementationType.Lc})
        {var request=Request(FilterMode.Passive,response,approximation,topology,1_000,2_000,manualOrder:1);var classic=approximation is FilterApproximation.Butterworth or FilterApproximation.ChebyshevTypeI or FilterApproximation.Bessel;var expected=topology==FilterImplementationType.Rc?classic&&response is (FilterResponseType.LowPass or FilterResponseType.HighPass):approximation is FilterApproximation.Butterworth or FilterApproximation.ChebyshevTypeI;Require(FilterSupportMatrix.Evaluate(request).IsSupported==expected,$"Unexpected passive support decision for {approximation}/{response}/{topology}.");}
    }
}

static void TestActiveSupportedMatrixEndToEnd()
{
    foreach(var approximation in Enum.GetValues<FilterApproximation>())
    foreach(var response in Enum.GetValues<FilterResponseType>())
    foreach(var topology in response==FilterResponseType.BandStop?new[]{FilterImplementationType.BainterNotch}:new[]{FilterImplementationType.SallenKey,FilterImplementationType.MultipleFeedback})
    {
        var fp=response==FilterResponseType.HighPass?10_000:1_000;var fs=response==FilterResponseType.HighPass?1_000:10_000;
        var request=Request(FilterMode.Active,response,approximation,topology,fp,fs,manualOrder:3) with { StopbandAttenuationDb=20 };
        request.ComponentConstraints.ResistorMinimumOhm=100;request.ComponentConstraints.ResistorMaximumOhm=10_000_000;
        var result=new FilterDesignEngine().Design(request);
        Require(result.IsImplemented,$"{approximation}/{response}/{topology}: {result.Message}");Require(result.CircuitGraph!.Components.Count>0,"Supported active matrix design generated no components.");Require(result.SchematicDocument!.PlacedComponents.Count==result.CircuitGraph.Components.Count,"Supported active matrix design did not place every component.");
    }
}

static void TestPassiveLcEndToEnd()
{
    var result = new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 2_000, manualOrder: 3));
    Require(result.IsImplemented, result.Message); Equal(3, result.CircuitGraph!.Components.Count); Require(result.CircuitGraph.Components.Count(component => component.Type == ComponentType.Inductor) == 2, "Third-order LP ladder requires two inductors."); Require(result.CircuitGraph.Components.Count(component => component.Type == ComponentType.Capacitor) == 1, "Third-order LP ladder requires one capacitor.");
}

static void TestPassiveLcHighPassEndToEnd()
{
    var result = new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.HighPass, FilterApproximation.Butterworth, FilterImplementationType.Lc, 2_000, 1_000, manualOrder: 3));
    Require(result.IsImplemented, result.Message); Equal(3, result.CircuitGraph!.Components.Count);
    Require(result.CircuitGraph.Components.Count(component => component.Type == ComponentType.Capacitor) == 2, "Third-order HP ladder requires two series capacitors.");
    Require(result.CircuitGraph.Components.Count(component => component.Type == ComponentType.Inductor) == 1, "Third-order HP ladder requires one shunt inductor.");
}

static void TestPassiveLcBandPassEndToEnd()
{
    var result = new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.BandPass, FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 2_000, manualOrder: 3));
    Require(result.IsImplemented, result.Message); Equal(3, result.PrototypeOrder); Equal(6, result.RealizedOrder); Equal(6, result.CircuitGraph!.Components.Count);
    Require(result.CircuitGraph.GetNet("VIN").Pins.Count == 1, "BP ladder input must start with one series resonator branch.");
}

static void TestPassiveLcBandStopEndToEnd()
{
    var result = new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.BandStop, FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 2_000, manualOrder: 3));
    Require(result.IsImplemented, result.Message); Equal(3, result.PrototypeOrder); Equal(6, result.RealizedOrder); Equal(6, result.CircuitGraph!.Components.Count);
    Require(result.CircuitGraph.GetNet("VIN").Pins.Count == 2, "BS ladder input must feed both arms of the first parallel resonator.");
}

static void TestChebyshevLcEndToEnd()
{
    foreach (var response in new[] { FilterResponseType.LowPass, FilterResponseType.HighPass, FilterResponseType.BandPass, FilterResponseType.BandStop })
    {
        var result = new FilterDesignEngine().Design(Request(FilterMode.Passive, response, FilterApproximation.ChebyshevTypeI, FilterImplementationType.Lc, response == FilterResponseType.HighPass ? 2_000 : 1_000, response == FilterResponseType.HighPass ? 1_000 : 2_000, manualOrder: 3));
        Require(result.IsImplemented, $"{response}: {result.Message}"); Require(result.CircuitGraph!.Components.Count > 0, $"{response} Chebyshev LC graph is empty.");
    }
}

static void TestLcBandTransformationResonance()
{
    var specification = new NormalizedFilterSpecification(FilterResponseType.BandPass, FilterApproximation.Butterworth, 1, 2, 2, 1, 40, 1, 1, 1, true, null, 2 * Math.PI * 1_000, 2 * Math.PI * 200);
    var service = new PassiveLcTransformationService();
    foreach (var response in new[] { FilterResponseType.BandPass, FilterResponseType.BandStop })
    {
        var actualSpec = specification with { ResponseType = response };
        var branch = service.Transform([1, 2, 1], 1, response, 50, actualSpec).Single();
        var inductance = branch.Parts.Single(part => part.ComponentType == ComponentType.Inductor).Value;
        var capacitance = branch.Parts.Single(part => part.ComponentType == ComponentType.Capacitor).Value;
        Require(Nearly(1 / Math.Sqrt(inductance * capacitance), actualSpec.CenterAngularFrequency, 1e-10), $"{response} resonator does not tune to f0.");
    }
}

static void TestLcStandardValueOptimization()
{
    foreach(var response in new[]{FilterResponseType.LowPass,FilterResponseType.BandPass,FilterResponseType.BandStop})
    {
        var result=new FilterDesignEngine().Design(Request(FilterMode.Passive,response,FilterApproximation.Butterworth,FilterImplementationType.Lc,1_000,2_000,optimize:true,manualOrder:3));Require(result.IsImplemented,$"{response}: {result.Message}");
        foreach(var component in result.CircuitGraph!.Components.Where(c=>c.Type is ComponentType.Capacitor or ComponentType.Inductor))
        {var parameter=component.Type==ComponentType.Capacitor?ComponentParameterNames.CapacitanceFarad:ComponentParameterNames.InductanceHenry;var value=component.Parameters.GetRequiredValue(parameter);var series=component.Type==ComponentType.Capacitor?StandardComponentSeries.E24:StandardComponentSeries.E12;Require(StandardValueSeries.IsSeriesValue(value,series,5e-3),$"{component.Reference} is not a selected E-series value.");}
    }
}

static void TestPassiveRcEndToEnd()
{
    foreach(var approximation in new[]{FilterApproximation.Butterworth,FilterApproximation.ChebyshevTypeI,FilterApproximation.Bessel})
    foreach(var response in new[]{FilterResponseType.LowPass,FilterResponseType.HighPass})
    {var result = new FilterDesignEngine().Design(Request(FilterMode.Passive,response,approximation,FilterImplementationType.Rc,response==FilterResponseType.LowPass?1_000:2_000,response==FilterResponseType.LowPass?2_000:1_000,manualOrder:1));Require(result.IsImplemented,$"{approximation}/{response}: {result.Message}");Equal(2,result.CircuitGraph!.Components.Count);Require(result.CircuitGraph.GetNet("VIN").Pins.Count==1,"RC input net is incorrect.");Require(result.CircuitGraph.GetNet("VOUT").Pins.Count==2,"RC output node must join R and C.");if(approximation==FilterApproximation.Bessel)Require(result.Warnings.Count>0,"A manually forced Bessel RC design must warn that it misses the requested attenuation.");}
}

static void TestSchematicPinAlignment()
{
    var results = new[]
    {
        new FilterDesignEngine().Design(Request(FilterMode.Active, FilterResponseType.HighPass, FilterApproximation.ChebyshevTypeI, FilterImplementationType.SallenKey, 2_000, 1_000, manualOrder:2)),
        new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.BandPass, FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 2_000, manualOrder: 3)),
        new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.BandStop, FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 2_000, manualOrder: 3))
        ,new FilterDesignEngine().Design(Request(FilterMode.Active, FilterResponseType.BandStop, FilterApproximation.Butterworth, FilterImplementationType.BainterNotch, 1_000, 2_000, manualOrder: 1))
    };
    foreach (var result in results)
    {
        Require(result.IsImplemented, result.Message);
        var document = result.SchematicDocument!;
        Require(!HasOverlappingWireSegments(document), "Schematic contains duplicate same-net wire geometry after uniqueness normalization.");
        var placements = document.PlacedComponents.ToDictionary(item => item.Component.Reference, StringComparer.OrdinalIgnoreCase);
        var wirePoints = document.Wires.SelectMany(item => item.Points).ToArray();
        foreach (var net in document.Circuit.Nets)
        foreach (var pin in net.Pins)
        {
            if(pin.Name is "V+" or "V-")continue;
            var anchor = SchematicGeometry.GetWorldPin(placements[pin.ComponentReference], pin.Name);
            Require(wirePoints.Any(point => Nearly(point.X, anchor.X, 1e-9) && Nearly(point.Y, anchor.Y, 1e-9)), $"Wire endpoint is not aligned to {pin.Id}.");
        }
    }
}

static void TestSchematicComponentClearance()
{
    var results=new[]{new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.LowPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,1_000,4_000)),new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,1_000,2_000,manualOrder:1)),new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.MultipleFeedback,1_000,2_000,manualOrder:1)),new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandStop,FilterApproximation.Butterworth,FilterImplementationType.BainterNotch,1_000,2_000,manualOrder:1))};
    foreach(var result in results){Require(result.IsImplemented,result.Message);var boxes=result.SchematicDocument!.PlacedComponents.Select(item=>(item.Component.Reference,Bounds:SchematicGeometry.GetBounds(item,8))).ToArray();for(var i=0;i<boxes.Length;i++)for(var j=i+1;j<boxes.Length;j++)Require(!Intersects(boxes[i].Bounds,boxes[j].Bounds),$"{boxes[i].Reference} overlaps {boxes[j].Reference} in graph with {result.CircuitGraph!.Components.Count} components.");}
}

static void TestSchematicWiresAvoidOpAmps()
{
    var cases=new[]{(FilterResponseType.LowPass,FilterImplementationType.SallenKey),(FilterResponseType.HighPass,FilterImplementationType.SallenKey),(FilterResponseType.BandPass,FilterImplementationType.SallenKey),(FilterResponseType.BandPass,FilterImplementationType.MultipleFeedback),(FilterResponseType.BandStop,FilterImplementationType.BainterNotch)};
    foreach (var item in cases)
    {
        var response=item.Item1;var result = new FilterDesignEngine().Design(Request(FilterMode.Active, response, FilterApproximation.Butterworth, item.Item2, response == FilterResponseType.LowPass ? 1_000 : 2_000, response == FilterResponseType.LowPass ? 4_000 : 1_000,manualOrder:response is FilterResponseType.BandPass or FilterResponseType.BandStop?1:null));
        Require(result.IsImplemented, result.Message);
        foreach (var opAmp in result.SchematicDocument!.PlacedComponents.Where(item => item.Component.Type == ComponentType.OpAmp))
        {
            var interior = SchematicGeometry.GetBounds(opAmp, -5);
            foreach (var wire in result.SchematicDocument.Wires)
            for (var index = 1; index < wire.Points.Count; index++)
                Require(!SegmentCrossesInterior(wire.Points[index - 1], wire.Points[index], interior), $"Wire {wire.NetName} {wire.Points[index-1]}->{wire.Points[index]} crosses {opAmp.Component.Reference} in {item.Item2}/{response}.");
        }
    }
}

static void TestSelectedTopologyReferences()
{
    var cases = new[]
    {
        (FilterMode.Active, FilterImplementationType.SallenKey),
        (FilterMode.Active, FilterImplementationType.MultipleFeedback),
        (FilterMode.Active, FilterImplementationType.BainterNotch),
        (FilterMode.Passive, FilterImplementationType.Rc),
        (FilterMode.Passive, FilterImplementationType.Lc)
    };
    var builder = new FilterTopologyReferenceBuilder();
    foreach (var item in cases)
    {
        var document = builder.Build(item.Item1, item.Item2);
        Require(document.PlacedComponents.Count == document.Circuit.Components.Count, $"Reference {item} did not place every component.");
        var placements = document.PlacedComponents.ToDictionary(value => value.Component.Reference, StringComparer.OrdinalIgnoreCase);
        var wirePoints = document.Wires.SelectMany(value => value.Points).ToArray();
        foreach (var net in document.Circuit.Nets)
        foreach (var pin in net.Pins)
        {
            if(pin.Name is "V+" or "V-")continue;
            var anchor = SchematicGeometry.GetWorldPin(placements[pin.ComponentReference], pin.Name);
            Require(wirePoints.Contains(anchor), $"Reference {item} has an unconnected drawing pin {pin.Id}.");
        }
    }
}

static void TestMfbLowPassEndToEnd()
{
    var engine = new FilterDesignEngine();
    var request = Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Butterworth, FilterImplementationType.MultipleFeedback, 1_000, 4_000);
    Require(engine.GetAvailability(request).CanDesign, engine.GetAvailability(request).Message);
    var result = engine.Design(request);
    Require(result.IsImplemented, result.Message);
    Equal(2, result.Sections.Count);
    Equal(12, result.CircuitGraph!.Components.Count);
    Require(result.CircuitGraph.Components.Count(component => component.Type == ComponentType.OpAmp) == 2, "Fourth-order MFB must contain two op-amps.");
    Require(result.Sections.All(section => Math.Abs(section.FrequencyError) <= 0.08 && Math.Abs(section.QError) <= 0.12), "MFB section verification exceeded its error limits.");
    Require(result.SchematicDocument!.PlacedComponents.Count == result.CircuitGraph.Components.Count, "Every MFB component must be placed.");
}


static void TestMfbHighPassEndToEnd()
{
    var engine = new FilterDesignEngine();
    var request = WideRanges(Request(FilterMode.Active, FilterResponseType.HighPass, FilterApproximation.ChebyshevTypeI, FilterImplementationType.MultipleFeedback, 2_000, 1_000));
    Require(engine.GetAvailability(request).CanDesign, engine.GetAvailability(request).Message);
    var result = engine.Design(request);
    Require(result.IsImplemented, result.Message);
    Equal(3, result.Sections.Count);
    Require(result.CircuitGraph!.Components.Count(component => component.Type == ComponentType.OpAmp) == 3, "Fifth-order MFB HP must contain three active stages.");
    Require(result.SchematicDocument!.PlacedComponents.Count == result.CircuitGraph.Components.Count, "Every MFB HP component must be placed.");
}

static void TestMfbBandPassEndToEnd()
{
    var request=WideRanges(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.MultipleFeedback,1_000,2_000,manualOrder:2));
    var engine=new FilterDesignEngine(); Require(engine.GetAvailability(request).CanDesign,engine.GetAvailability(request).Message); var result=engine.Design(request);
    Require(result.IsImplemented,result.Message); Equal(4,result.RealizedOrder); Equal(2,result.Sections.Count); Equal(12,result.CircuitGraph!.Components.Count);
}

static void TestActiveBandStopEndToEnd()
{
    var request=Request(FilterMode.Active,FilterResponseType.BandStop,FilterApproximation.Butterworth,FilterImplementationType.BainterNotch,1_000,2_000,manualOrder:1);
    var result=new FilterDesignEngine().Design(request);Require(result.IsImplemented,result.Message);Equal(2,result.RealizedOrder);
    Equal(11,result.CircuitGraph!.Components.Count);Require(result.CircuitGraph.Components.Count(component=>component.Type==ComponentType.OpAmp)==3,"A Bainter notch section requires three op-amps.");
    Require(result.Sections.Single().Components.Count==8,"A unity-gain Bainter section requires six resistors and two capacitors.");
    Require(result.SchematicDocument!.PlacedComponents.Count==result.CircuitGraph.Components.Count,"Band-stop schematic did not place every component.");
    var section=result.Sections.Single();Require(Math.Abs(section.FrequencyError)<1e-9&&Math.Abs(section.QError)<1e-9,"Bainter inverse verification did not recover f0/Q.");
}

static void TestIntegratedActiveSectionGain()
{
    var engine = new FilterDesignEngine();
    var firstOrder = WideRanges(Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 1_000, 4_000, manualOrder: 1) with { PassbandGain = 1.25 });
    var firstOrderResult = engine.Design(firstOrder);
    Require(firstOrderResult.IsImplemented, firstOrderResult.Message);
    var gainNetWires = firstOrderResult.SchematicDocument!.Wires.Where(wire => wire.NetName == "N_S1_SECTIONGAIN_INV").ToArray();
    var gainNetPoints = gainNetWires.SelectMany(wire => wire.Points).ToArray();
    Require(gainNetPoints.Max(point => point.Y) - gainNetPoints.Min(point => point.Y) < 250,
        "First-order integrated gain network was routed as redundant tall parallel trunks.");
    Require(!HasOverlappingWireSegments(firstOrderResult.SchematicDocument), "First-order integrated gain network contains duplicate wire geometry.");
    Require(!HasDanglingNonPinEndpoint(firstOrderResult.SchematicDocument, "N_S1_SECTIONGAIN_INV"),
        "First-order integrated gain network contains a wire spur beyond its junction.");

    var sallenKey = WideRanges(Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 1_000, 4_000, optimize: true, manualOrder: 2) with { PassbandGain = 2 });
    var skResult = engine.Design(sallenKey);
    Require(skResult.IsImplemented, skResult.Message);
    Require(skResult.AdditionalNetworkComponents.Count == 0, "A0 must not create a separate output-gain network.");
    Require(skResult.CircuitGraph!.Components.Count(component => component.Type == ComponentType.OpAmp) == 1, "A second-order Sallen-Key filter must use its filter op-amp for gain.");
    Require(!skResult.CircuitGraph.Nets.Any(net => net.Name.Equals("N_FILTER_OUT", StringComparison.OrdinalIgnoreCase)), "The obsolete pre-gain output net must not exist.");
    Require(skResult.Sections.Single().Components.Any(value => value.Role == "RF") && skResult.Sections.Single().Components.Any(value => value.Role == "RG"), "Sallen-Key section is missing its integrated gain resistors.");
    Require(Math.Abs(skResult.Sections.Single().GainError) < .08, "Sallen-Key standard values did not recover A0 within the verification limit.");
    Require(Math.Abs(skResult.AnalysisResult!.Points[0].MagnitudeDb - 20 * Math.Log10(2)) < .15, "Actual Sallen-Key passband response does not include A0.");
    RequireCleanSchematic(skResult);

    var skHighPass = WideRanges(Request(FilterMode.Active, FilterResponseType.HighPass, FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 4_000, 1_000, manualOrder: 2) with { PassbandGain = 2 });
    var skHighPassResult = engine.Design(skHighPass);
    Require(skHighPassResult.IsImplemented, skHighPassResult.Message);
    Require(skHighPassResult.CircuitGraph!.Components.Count(component => component.Type == ComponentType.OpAmp) == 1, "High-pass Sallen-Key must use its filter op-amp for A0.");
    Require(Nearly(skHighPassResult.Sections.Single().ActualGain, 2, 1e-10), "High-pass Sallen-Key did not synthesize A0.");

    foreach (var response in new[] { FilterResponseType.LowPass, FilterResponseType.HighPass })
    {
        var fp = response == FilterResponseType.LowPass ? 1_000 : 4_000;
        var fs = response == FilterResponseType.LowPass ? 4_000 : 1_000;
        var request = WideRanges(Request(FilterMode.Active, response, FilterApproximation.Butterworth, FilterImplementationType.MultipleFeedback, fp, fs, manualOrder: 2) with { PassbandGain = .5 });
        var result = engine.Design(request);
        Require(result.IsImplemented, result.Message);
        Require(result.CircuitGraph!.Components.Count(component => component.Type == ComponentType.OpAmp) == 1, $"MFB {response} must not append a gain op-amp.");
        Require(Nearly(result.Sections.Single().ActualGain, .5, 1e-10), $"MFB {response} did not synthesize A0 inside the biquad.");
    }

    var mfbBandPass = WideRanges(Request(FilterMode.Active, FilterResponseType.BandPass, FilterApproximation.Butterworth, FilterImplementationType.MultipleFeedback, 1_000, 2_000, manualOrder: 2) with { PassbandGain = .5 });
    var mfbBandPassResult = engine.Design(mfbBandPass);
    Require(mfbBandPassResult.IsImplemented, mfbBandPassResult.Message);
    Require(mfbBandPassResult.CircuitGraph!.Components.Count(component => component.Type == ComponentType.OpAmp) == mfbBandPassResult.Sections.Count, "MFB band-pass A0 must not append an op-amp.");
    var centerFrequency = Math.Sqrt(mfbBandPass.PassbandLowerHz * mfbBandPass.PassbandUpperHz);
    var centerPoint = mfbBandPassResult.AnalysisResult!.Points.MinBy(point => Math.Abs(Math.Log(point.FrequencyHz / centerFrequency)))!;
    Require(Math.Abs(centerPoint.MagnitudeDb - 20 * Math.Log10(.5)) < .2, $"MFB band-pass center gain is {centerPoint.MagnitudeDb:G6} dB, not A0.");

    var skBandPass = WideRanges(Request(FilterMode.Active, FilterResponseType.BandPass, FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 1_000, 2_000, manualOrder: 2) with { PassbandGain = 1.5 });
    var skBandPassResult = engine.Design(skBandPass);
    Require(skBandPassResult.IsImplemented, skBandPassResult.Message);
    var skCenter = Math.Sqrt(skBandPass.PassbandLowerHz * skBandPass.PassbandUpperHz);
    var skCenterPoint = skBandPassResult.AnalysisResult!.Points.MinBy(point => Math.Abs(Math.Log(point.FrequencyHz / skCenter)))!;
    Require(Math.Abs(skCenterPoint.MagnitudeDb - 20 * Math.Log10(1.5)) < .25, $"Sallen-Key band-pass center gain is {skCenterPoint.MagnitudeDb:G6} dB, not A0.");

    var notch = WideRanges(Request(FilterMode.Active, FilterResponseType.BandStop, FilterApproximation.Butterworth, FilterImplementationType.BainterNotch, 1_000, 2_000, manualOrder: 1) with { PassbandGain = 2 });
    var notchResult = engine.Design(notch);
    Require(notchResult.IsImplemented, notchResult.Message);
    Require(notchResult.CircuitGraph!.Components.Count(component => component.Type == ComponentType.OpAmp) == 3, "Bainter gain must use U3, not append U4.");
    Require(notchResult.Sections.Single().Components.Any(value => value.Role == "R7") && notchResult.Sections.Single().Components.Any(value => value.Role == "R8"), "Bainter K2 gain resistors are missing.");
    Require(Nearly(notchResult.Sections.Single().ActualGain, 2, 1e-10), "Bainter K2 verification did not recover A0.");
    var lowerNotchGain = notchResult.AnalysisResult!.Points[0].MagnitudeDb;
    var upperNotchGain = notchResult.AnalysisResult.Points[^1].MagnitudeDb;
    Require(Math.Abs(lowerNotchGain - 20 * Math.Log10(2)) < .35, $"Bainter lower passband is {lowerNotchGain:G6} dB instead of A0 through K1.");
    Require(Math.Abs(upperNotchGain - 20 * Math.Log10(2)) < .35, $"Bainter upper passband is {upperNotchGain:G6} dB instead of A0 through K2.");
    RequireCleanSchematic(notchResult);

    var unsupported = sallenKey with { PassbandGain = .5 };
    Require(!engine.GetAvailability(unsupported).CanDesign, "Sallen-Key A0 below 1 must be rejected instead of creating a hidden attenuator.");
}

static void TestHighPassDefaultEdgeOrdering()
{
    var state=new FilterDesignState();state.SelectResponseType(FilterResponseType.HighPass);
    Require(state.StopbandEdgeHz<state.PassbandEdgeHz,"High-pass defaults must satisfy fs < fp.");
    Require(Nearly(state.StopbandEdgeHz,1_000,1e-12)&&Nearly(state.PassbandEdgeHz,3_000,1e-12),"Unexpected high-pass default edge values.");
}

static void TestBandStopDefaultEdgeOrdering()
{
    var state=new FilterDesignState();state.SelectResponseType(FilterResponseType.BandStop);
    Require(state.PassbandLowerHz<state.StopbandLowerHz&&state.StopbandLowerHz<state.StopbandUpperHz&&state.StopbandUpperHz<state.PassbandUpperHz,"Band-stop defaults must satisfy fp1 < fs1 < fs2 < fp2.");
    Require(Nearly(state.StopbandLowerHz,750,1e-12)&&Nearly(state.StopbandUpperHz,1_000,1e-12),"Unexpected band-stop default stopband edges.");
    Require(state.Implementation==FilterImplementationType.BainterNotch,"Active Band-Stop must select Bainter Notch by default.");
}

static void TestOpAmpInputSymbolSeparation()
{
    var symbol=SymbolDefinitionCatalog.Get(ComponentType.OpAmp);var plus=symbol.GetPinAnchor("IN+").Position;var minus=symbol.GetPinAnchor("IN-").Position;
    Require(Math.Abs(minus.Y-plus.Y)>=40,"Op-amp +/- input anchors are too close for legible schematic rendering.");
    Require(!Nearly(plus.Y,minus.Y,1e-12),"Op-amp +/- inputs must not share the same anchor.");
}

static void TestOpAmpInputsElectricallyIsolated()
{
    foreach(var result in ActiveSchematicCases())
    foreach(var opAmp in result.CircuitGraph!.Components.Where(component=>component.Type==ComponentType.OpAmp))
    {
        var plus=result.CircuitGraph.GetNetForPin(opAmp.GetPin("IN+"));var minus=result.CircuitGraph.GetNetForPin(opAmp.GetPin("IN-"));
        Require(plus!=null&&minus!=null&&!ReferenceEquals(plus,minus),$"{opAmp.Reference} IN+ and IN- are shorted.");
    }
}

static void TestOpAmpInputDrawingIsolation()
{
    foreach(var result in ActiveSchematicCases())
    {
        var document=result.SchematicDocument!;var placed=document.PlacedComponents.ToDictionary(item=>item.Component.Reference,StringComparer.OrdinalIgnoreCase);
        foreach(var opAmp in result.CircuitGraph!.Components.Where(component=>component.Type==ComponentType.OpAmp))
        foreach(var pinName in new[]{"IN+","IN-"})
        {
            var pin=opAmp.GetPin(pinName);var net=result.CircuitGraph.GetNetForPin(pin)!;var anchor=SchematicGeometry.GetWorldPin(placed[opAmp.Reference],pinName);
            var escape=document.Wires.First(wire=>wire.NetName.Equals(net.Name,StringComparison.OrdinalIgnoreCase)&&wire.Points[0]==anchor);
            foreach(var other in document.Wires.Where(wire=>!wire.NetName.Equals(net.Name,StringComparison.OrdinalIgnoreCase)))
            for(var index=1;index<other.Points.Count;index++)
                Require(!SegmentsIntersect(escape.Points[0],escape.Points[1],other.Points[index-1],other.Points[index]),$"{opAmp.Reference} {pinName} escape is visually shorted to net {other.NetName}.");
        }
    }
}

static IReadOnlyList<FilterDesignResult> ActiveSchematicCases()=>
[
    new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.LowPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,1_000,4_000,manualOrder:2)),
    new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.LowPass,FilterApproximation.Butterworth,FilterImplementationType.MultipleFeedback,1_000,4_000,manualOrder:2)),
    new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.HighPass,FilterApproximation.Butterworth,FilterImplementationType.MultipleFeedback,3_000,1_000,manualOrder:2)),
    new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.MultipleFeedback,1_000,2_000,manualOrder:1)),
    new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandStop,FilterApproximation.Butterworth,FilterImplementationType.BainterNotch,1_000,2_000,manualOrder:1))
];

static void TestLcRoutingHasNoRetracedNeedles()
{
    var result=new FilterDesignEngine().Design(Request(FilterMode.Passive,FilterResponseType.BandStop,FilterApproximation.Butterworth,FilterImplementationType.Lc,1_000,2_000,manualOrder:7));
    Require(result.IsImplemented,result.Message);
    foreach(var wire in result.SchematicDocument!.Wires)
    for(var index=1;index+1<wire.Points.Count;index++)
        Require(wire.Points[index-1]!=wire.Points[index+1],$"Wire {wire.NetName} contains an A-B-A retrace at {wire.Points[index]}.");
}

static void TestComponentRangesAreEnforced()
{
    var request=Request(FilterMode.Active,FilterResponseType.HighPass,FilterApproximation.Butterworth,FilterImplementationType.MultipleFeedback,3_000,1_000,manualOrder:2);
    request.ComponentConstraints.ResistorMaximumOhm=2_000;
    var result=new FilterDesignEngine().Design(request);
    Require(!result.IsImplemented,"A design with components outside the selected range must not report success.");
}

static void TestDefaultActiveBandStopIsPractical()
{
    var state=new FilterDesignState();state.SelectResponseType(FilterResponseType.BandStop);
    var result=new FilterDesignEngine().Design(FilterDesignRequest.FromState(state));
    Require(result.IsImplemented,result.Message);Equal(4,result.RequiredOrder);Equal(4,result.Sections.Count);
    Equal(44,result.CircuitGraph!.Components.Count);
    Require(result.CircuitGraph.Components.All(component=>component.Type==ComponentType.OpAmp||component.Parameters.Values.Values.All(value=>value>0)),"Default Bainter circuit contains an invalid component value.");
}

static void TestBainterWiresAvoidComponentBodies()
{
    var result=new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandStop,FilterApproximation.Butterworth,FilterImplementationType.BainterNotch,1_000,2_000,manualOrder:1));
    Require(result.IsImplemented,result.Message);
    foreach(var placed in result.SchematicDocument!.PlacedComponents.Where(component=>component.Component.Reference=="R6"))
    {
        var interior=SchematicGeometry.GetBounds(placed,-5);
        foreach(var wire in result.SchematicDocument.Wires)
        for(var index=1;index<wire.Points.Count;index++)
            Require(!SegmentCrossesInterior(wire.Points[index-1],wire.Points[index],interior),$"Wire {wire.NetName} {string.Join(" -> ",wire.Points)} crosses {placed.Component.Reference} bounds {interior}.");
    }
}

static void TestPassiveBandModesSelectLc()
{
    var state=new FilterDesignState { Mode=FilterMode.Passive };
    state.SelectResponseType(FilterResponseType.BandPass);
    Require(state.Implementation==FilterImplementationType.Lc,"Passive Band-Pass must automatically select LC.");
    state.Implementation=FilterImplementationType.Rc;
    state.SelectResponseType(FilterResponseType.BandStop);
    Require(state.Implementation==FilterImplementationType.Lc,"Passive Band-Stop must automatically replace the invalid RC implementation with LC.");
    state.SelectResponseType(FilterResponseType.LowPass);
    Require(state.Implementation==FilterImplementationType.Lc,"Returning to Low-Pass should preserve the still-valid LC selection.");
    state.Mode=FilterMode.Active;state.SelectResponseType(FilterResponseType.LowPass);
    Require(state.Implementation==FilterImplementationType.SallenKey,"Switching from Passive LC to Active Low-Pass must select a valid active topology.");
}

static void TestRcAcAnalysisKnownPoint()
{
    var builder=new CircuitBuilder();var resistor=builder.Add(ComponentFactory.Resistor("R1",10_000));var capacitor=builder.Add(ComponentFactory.Capacitor("C1",10e-9));
    builder.Connect("VIN",resistor.GetPin("1"));builder.Connect("VOUT",resistor.GetPin("2"),capacitor.GetPin("1"));builder.Connect("GND",capacitor.GetPin("2"));
    var cutoff=1/(2*Math.PI*10_000*10e-9);
    var result=new CircuitAcAnalysisService().Analyze(builder.Build(),new AcAnalysisOptions(cutoff/1.001,cutoff*1.001,1));
    var point=result.Points.MinBy(value=>Math.Abs(value.FrequencyHz-cutoff))!;
    Require(Math.Abs(point.MagnitudeLinear-1/Math.Sqrt(2))<.002,$"RC magnitude at fc is {point.MagnitudeLinear:G6}, expected 0.7071.");
    Require(Math.Abs(point.PhaseDegrees+45)<.2,$"RC phase at fc is {point.PhaseDegrees:G6} degrees, expected -45 degrees.");
}

static void TestDesignedFilterActualAnalysis()
{
    var lowPass=new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.LowPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,1_000,4_000,manualOrder:2));
    Require(lowPass.IsImplemented,lowPass.Message);Require(lowPass.AnalysisResult?.Points.Count>20,"Designed filter contains no AC response points.");
    var low=lowPass.AnalysisResult!.Points[0].MagnitudeDb;var high=lowPass.AnalysisResult.Points[^1].MagnitudeDb;
    Require(low>-1&&high<low-30,$"Low-pass AC response is not decreasing as expected ({low:0.##} dB to {high:0.##} dB).");
    var notch=new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandStop,FilterApproximation.Butterworth,FilterImplementationType.BainterNotch,1_000,2_000,manualOrder:1));
    Require(notch.IsImplemented,notch.Message);var center=Math.Sqrt(notch.DesignRequest!.PassbandLowerHz*notch.DesignRequest.PassbandUpperHz);var notchPoint=notch.AnalysisResult!.Points.MinBy(point=>Math.Abs(Math.Log(point.FrequencyHz/center)))!;
    Require(notchPoint.MagnitudeDb<-25,$"Bainter AC analysis does not show its expected notch ({notchPoint.MagnitudeDb:0.##} dB).");
}

static void TestPassiveLcInsertionGainAnalysis()
{
    var request = Request(FilterMode.Passive, FilterResponseType.BandStop, FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 2_000, manualOrder: 3);
    var result = new FilterDesignEngine().Design(request);
    Require(result.IsImplemented, result.Message);
    var analysis = result.AnalysisResult ?? throw new InvalidOperationException("Passive LC analysis is missing.");
    Require(Nearly(analysis.MagnitudeReferenceLinear, .5, 1e-12), "50 ohm / 50 ohm LC insertion-gain reference must be 0.5 V/V.");
    var lowerPassband = analysis.Points[0].MagnitudeDb;
    var upperPassband = analysis.Points[^1].MagnitudeDb;
    Require(Math.Abs(lowerPassband) < .5 && Math.Abs(upperPassband) < .5,
        $"Passive LC passbands must be normalized near 0 dB, got {lowerPassband:0.###} dB and {upperPassband:0.###} dB.");
    var center = Math.Sqrt(request.PassbandLowerHz * request.PassbandUpperHz);
    var notch = analysis.Points.MinBy(point => Math.Abs(Math.Log(point.FrequencyHz / center)))!;
    Require(notch.MagnitudeDb < -80, $"Ideal LC band-stop transmission zero is unexpectedly shallow ({notch.MagnitudeDb:0.###} dB).");
}

static void TestMultisimCirExport()
{
    var active=new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.LowPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,1_000,4_000,manualOrder:2));
    Require(active.IsImplemented,active.Message);var netlist=new FilterSpiceExportService().Export(active);
    Require(netlist.StartsWith("Circuit Module Designer",StringComparison.Ordinal),"The first CIR line must be a title line.");
    Require(netlist.Contains("V_INPUT VIN 0 DC 0 AC 1",StringComparison.Ordinal),"CIR is missing its AC source.");
    Require(netlist.Contains("E_U1",StringComparison.Ordinal),"Abstract op-amp was not exported as a VCVS.");
    Require(netlist.Contains(".AC DEC 40",StringComparison.Ordinal)&&netlist.TrimEnd().EndsWith(".END",StringComparison.Ordinal),"CIR analysis/end directives are missing.");
    Require(!netlist.Contains(" VCC ",StringComparison.Ordinal)&&!netlist.Contains(" VEE ",StringComparison.Ordinal),"Abstract VCVS export must not leave floating supply nets.");
    var passive=new FilterDesignEngine().Design(Request(FilterMode.Passive,FilterResponseType.LowPass,FilterApproximation.Butterworth,FilterImplementationType.Lc,1_000,2_000,manualOrder:3));
    Require(passive.IsImplemented,passive.Message);var lcNetlist=passive.SpiceNetlist;
    Require(lcNetlist.Contains("R_SOURCE N_SOURCE VIN",StringComparison.Ordinal)&&lcNetlist.Contains("R_LOAD VOUT 0",StringComparison.Ordinal),"LC CIR must include its design source/load terminations.");
}

static void TestSchematicJunctionsAreRealBranches()
{
    var results=ActiveSchematicCases().Concat(new[]{new FilterDesignEngine().Design(Request(FilterMode.Passive,FilterResponseType.BandStop,FilterApproximation.Butterworth,FilterImplementationType.Lc,1_000,2_000,manualOrder:3))});
    foreach(var result in results)
    foreach(var junction in result.SchematicDocument!.Junctions)
    {
        var directions=new HashSet<int>();
        foreach(var wire in result.SchematicDocument.Wires.Where(wire=>wire.NetName.Equals(junction.NetName,StringComparison.OrdinalIgnoreCase)))
        for(var index=1;index<wire.Points.Count;index++)
        {
            var a=wire.Points[index-1];var b=wire.Points[index];
            if(!PointOnSegment(junction.Position,a,b))continue;
            if(a.X<junction.Position.X||b.X<junction.Position.X)directions.Add(0);
            if(a.X>junction.Position.X||b.X>junction.Position.X)directions.Add(1);
            if(a.Y<junction.Position.Y||b.Y<junction.Position.Y)directions.Add(2);
            if(a.Y>junction.Position.Y||b.Y>junction.Position.Y)directions.Add(3);
        }
        Require(directions.Count>=3,$"Junction {junction.NetName}@{junction.Position} is not a real three-way-or-greater wire branch.");
    }
}

static FilterDesignRequest Request(FilterMode mode, FilterResponseType response, FilterApproximation approximation, FilterImplementationType implementation, double fp, double fs, bool optimize = false, int? manualOrder = null) => new()
{
    Mode = mode, ResponseType = response, Approximation = approximation, Implementation = implementation, SignalArchitecture = FilterSignalArchitecture.SingleEnded, OrderMode = manualOrder.HasValue ? FilterOrderMode.SpecifyOrder : FilterOrderMode.CalculateAutomatically, SpecifiedOrder = manualOrder ?? 2, PassbandRippleDb = 1, StopbandAttenuationDb = 40, PassbandGain = 1, PassbandEdgeHz = fp, StopbandEdgeHz = fs,
    StopbandLowerHz = response == FilterResponseType.BandStop ? 900 : 500, PassbandLowerHz = response == FilterResponseType.BandStop ? 500 : 900, PassbandUpperHz = response == FilterResponseType.BandStop ? 1_500 : 1_100, StopbandUpperHz = response == FilterResponseType.BandStop ? 1_100 : 1_500,
    SourceResistanceOhm = 50, LoadResistanceOhm = 50, PreferredResistanceOhm = 10_000,
    ComponentConstraints = new ComponentSelectionConstraints { ResistorSeries = StandardComponentSeries.E24, CapacitorSeries = StandardComponentSeries.E24, InductorSeries = StandardComponentSeries.E12, ResistorTolerancePercent = 1, CapacitorTolerancePercent = 5, InductorTolerancePercent = 5, ResistorMinimumOhm = 1_000, ResistorMaximumOhm = 100_000, CapacitorMinimumF = 1e-12, CapacitorMaximumF = 10e-3, InductorMinimumH=100e-9, InductorMaximumH=10, OptimizeStandardValues = optimize, OptimizationTarget = FilterOptimizationTarget.MinimizeFrequencyError }
};

static FilterDesignRequest WideRanges(FilterDesignRequest request)
{
    request.ComponentConstraints.ResistorMinimumOhm=100;
    request.ComponentConstraints.ResistorMaximumOhm=10_000_000;
    return request;
}

static void Equal(int expected, int actual) { if (expected != actual) throw new InvalidOperationException($"Expected {expected}, got {actual}."); }
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static bool Nearly(double actual, double expected, double relativeTolerance) => Math.Abs(actual - expected) <= relativeTolerance * Math.Max(1, Math.Abs(expected));
static void RequireCleanSchematic(FilterDesignResult result)
{
    var document = result.SchematicDocument ?? throw new InvalidOperationException("Schematic document is missing.");
    Require(document.PlacedComponents.Count == result.CircuitGraph!.Components.Count, "Integrated-gain schematic did not place every component.");
    var boxes = document.PlacedComponents.Select(item => (item.Component.Reference, Bounds: SchematicGeometry.GetBounds(item, 8))).ToArray();
    for (var i = 0; i < boxes.Length; i++)
    for (var j = i + 1; j < boxes.Length; j++)
        Require(!Intersects(boxes[i].Bounds, boxes[j].Bounds), $"Integrated-gain schematic overlaps {boxes[i].Reference} and {boxes[j].Reference}.");
    foreach (var opAmp in document.PlacedComponents.Where(item => item.Component.Type == ComponentType.OpAmp))
    {
        var interior = SchematicGeometry.GetBounds(opAmp, -5);
        foreach (var wire in document.Wires)
        for (var index = 1; index < wire.Points.Count; index++)
            Require(!SegmentCrossesInterior(wire.Points[index - 1], wire.Points[index], interior), $"Integrated-gain wire {wire.NetName} crosses {opAmp.Component.Reference}.");
    }
}
static Complex Evaluate(IReadOnlyList<double> coefficients, Complex s) { Complex value=0; foreach(var coefficient in coefficients) value=value*s+coefficient; return value; }
static bool Intersects(SchematicRect a, SchematicRect b) => a.X < b.X + b.Width && a.X + a.Width > b.X && a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;
static bool SegmentCrossesInterior(SchematicPoint a, SchematicPoint b, SchematicRect box)
{
    const double epsilon = 1e-6;
    var left = box.X + epsilon; var right = box.X + box.Width - epsilon; var top = box.Y + epsilon; var bottom = box.Y + box.Height - epsilon;
    if (Nearly(a.Y, b.Y, 1e-12)) return a.Y > top && a.Y < bottom && Math.Max(a.X, b.X) > left && Math.Min(a.X, b.X) < right;
    if (Nearly(a.X, b.X, 1e-12)) return a.X > left && a.X < right && Math.Max(a.Y, b.Y) > top && Math.Min(a.Y, b.Y) < bottom;
    return false;
}

static bool SegmentsIntersect(SchematicPoint a,SchematicPoint b,SchematicPoint c,SchematicPoint d)
{
    const double e=1e-9;
    var abHorizontal=Math.Abs(a.Y-b.Y)<e;var cdHorizontal=Math.Abs(c.Y-d.Y)<e;
    if(abHorizontal&&cdHorizontal)
        return Math.Abs(a.Y-c.Y)<e&&Math.Max(Math.Min(a.X,b.X),Math.Min(c.X,d.X))<=Math.Min(Math.Max(a.X,b.X),Math.Max(c.X,d.X))+e;
    if(!abHorizontal&&!cdHorizontal)
        return Math.Abs(a.X-c.X)<e&&Math.Max(Math.Min(a.Y,b.Y),Math.Min(c.Y,d.Y))<=Math.Min(Math.Max(a.Y,b.Y),Math.Max(c.Y,d.Y))+e;
    var h1=abHorizontal?a:c;var h2=abHorizontal?b:d;var v1=abHorizontal?c:a;var v2=abHorizontal?d:b;
    return v1.X>=Math.Min(h1.X,h2.X)-e&&v1.X<=Math.Max(h1.X,h2.X)+e&&h1.Y>=Math.Min(v1.Y,v2.Y)-e&&h1.Y<=Math.Max(v1.Y,v2.Y)+e;
}

static bool PointOnSegment(SchematicPoint point,SchematicPoint a,SchematicPoint b)
{
    const double e=.01;
    var within=point.X>=Math.Min(a.X,b.X)-e&&point.X<=Math.Max(a.X,b.X)+e&&point.Y>=Math.Min(a.Y,b.Y)-e&&point.Y<=Math.Max(a.Y,b.Y)+e;
    return within&&(Math.Abs(a.X-b.X)<e?Math.Abs(point.X-a.X)<e:Math.Abs(point.Y-a.Y)<e);
}

static bool HasOverlappingWireSegments(SchematicDocument document)
{
    foreach(var group in document.Wires.GroupBy(wire=>wire.NetName,StringComparer.OrdinalIgnoreCase))
    {
        var segments=group.SelectMany(wire=>wire.Points.Zip(wire.Points.Skip(1),(a,b)=>(A:a,B:b))).ToArray();
        for(var first=0;first<segments.Length;first++)
        for(var second=first+1;second<segments.Length;second++)
        {
            var a=segments[first];var b=segments[second];
            var ah=Math.Abs(a.A.Y-a.B.Y)<.01;var bh=Math.Abs(b.A.Y-b.B.Y)<.01;
            if(ah!=bh)continue;
            if(ah&&Math.Abs(a.A.Y-b.A.Y)<.01&&OverlapLength(a.A.X,a.B.X,b.A.X,b.B.X)>.01)return true;
            if(!ah&&Math.Abs(a.A.X-b.A.X)<.01&&OverlapLength(a.A.Y,a.B.Y,b.A.Y,b.B.Y)>.01)return true;
        }
    }
    return false;
}

static bool HasDanglingNonPinEndpoint(SchematicDocument document, string netName)
{
    var placements = document.PlacedComponents.ToDictionary(
        item => item.Component.Reference,
        StringComparer.OrdinalIgnoreCase);
    var pinPoints = document.Circuit.GetNet(netName).Pins
        .Select(pin => SchematicGeometry.GetWorldPin(placements[pin.ComponentReference], pin.Name))
        .ToArray();
    var segments = document.Wires
        .Where(wire => wire.NetName.Equals(netName, StringComparison.OrdinalIgnoreCase))
        .SelectMany(wire => wire.Points.Zip(wire.Points.Skip(1), (a, b) => (A: a, B: b)))
        .ToArray();

    foreach (var point in segments.SelectMany(segment => new[] { segment.A, segment.B }).Distinct())
    {
        var degree = segments.Count(segment => NearlyPoint(segment.A, point) || NearlyPoint(segment.B, point));
        if (degree == 1 && !pinPoints.Any(pinPoint => NearlyPoint(pinPoint, point)))
            return true;
    }
    return false;
}

static bool NearlyPoint(SchematicPoint first, SchematicPoint second) =>
    Nearly(first.X, second.X, .01) && Nearly(first.Y, second.Y, .01);

static double OverlapLength(double a1,double a2,double b1,double b2)=>
    Math.Max(0,Math.Min(Math.Max(a1,a2),Math.Max(b1,b2))-Math.Max(Math.Min(a1,a2),Math.Min(b1,b2)));
