using System.Numerics;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using CircuitModuleDesigner.Core.Analysis;
using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Core.Circuit.Spice;
using CircuitModuleDesigner.Core.Schematic;
using CircuitModuleDesigner.Core.Schematic.Symbols;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Approximation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Analysis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Design;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Engines;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Enums;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Models;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Reports;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Sections;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Schematic.Collision;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Gain;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Services;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.Verification;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis.PassiveLc;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Validation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Topology;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Controls;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Engines;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Reporting;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Schematic;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Controls;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Analysis;
using CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Services;
using AnalogComponentSelectionConstraints = CircuitModuleDesigner.Modules.PrecisionAnalogComputing.Models.ComponentSelectionConstraints;
using ComponentSelectionConstraints = CircuitModuleDesigner.Modules.Filters.FilterDesign.Models.ComponentSelectionConstraints;

if (args is ["--run-filter-rows", var inputPath, var outputPath])
{
    var service = new FilterManualTestRowService();
    var engine = new FilterDesignEngine();
    var completed = File.ReadAllLines(inputPath).Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(line =>
        {
            var parsed = service.Parse(line);
            return service.Complete(parsed, engine.Design(parsed.Request));
        });
    File.WriteAllLines(outputPath, completed, new System.Text.UTF8Encoding(true));
    return;
}

var tests = new (string Name, Action Run)[]
{
    ("Butterworth order", TestButterworthOrder),
    ("Chebyshev-I order", TestChebyshevOrder),
    ("Chebyshev-I ladder prototype", TestChebyshevLadderPrototype),
    ("Butterworth LHP poles", TestButterworthPoles),
    ("Bessel normalized poles and order", TestBesselPrototypeAndOrder),
    ("FilterPro tabulated prototypes", TestTabulatedPrototypes),
    ("FilterPro tabulated order search", TestTabulatedOrderSearch),
    ("Approximation published response samples", TestApproximationReferenceSamples),
    ("Bessel group-delay flatness", TestBesselGroupDelayFlatness),
    ("Pole pair section Q", TestSectionDecomposition),
    ("LP to HP transformation", TestHighPassTransformation),
    ("BP/BS center transformation", TestBandTransformations),
    ("Band-pass cascade gain normalization", TestBandPassGainNormalization),
    ("Band-stop shared notch zeros", TestBandStopSharedNotchZeros),
    ("Sallen-Key inverse verification", TestSallenKeyVerification),
    ("CircuitGraph section transfer verification", TestCircuitGraphSectionTransferVerification),
    ("Sallen-Key band-pass CircuitGraph audit", TestSallenKeyBandPassCircuitGraphAudit),
    ("Bainter CircuitGraph transfer audit", TestBainterCircuitGraphTransferAudit),
    ("Bainter E-series notch-depth gate", TestBainterStandardValueNotchDepth),
    ("Bainter multi-section gain and polarity", TestBainterMultiSectionGain),
    ("Single-Ended Active v1 freeze benchmarks", TestSingleEndedActiveV1Freeze),
    ("Sallen-Key band-pass inverse verification", TestSallenKeyBandPassVerification),
    ("MFB low-pass inverse verification", TestMfbLowPassVerification),
    ("MFB high-pass inverse verification", TestMfbHighPassVerification),
    ("MFB band-pass inverse verification", TestMfbBandPassVerification),
    ("E-series optimization", TestStandardValueOptimization),
    ("Active LP end-to-end", TestActiveLowPassEndToEnd),
    ("Active HP Chebyshev end-to-end", TestActiveHighPassEndToEnd),
    ("Bessel active end-to-end", TestActiveBesselEndToEnd),
    ("Sallen-Key band-pass end-to-end", TestSallenKeyBandPassEndToEnd),
    ("ADI tabulated responses end-to-end", TestTabulatedResponsesEndToEnd),
    ("Numerical-order approximations end-to-end", TestNumericalOrderApproximationsEndToEnd),
    ("Central support matrix", TestSupportMatrix),
    ("Active supported matrix end-to-end", TestActiveSupportedMatrixEndToEnd),
    ("Passive RC end-to-end", TestPassiveRcEndToEnd),
    ("Passive LC end-to-end", TestPassiveLcEndToEnd),
    ("Passive LC high-pass end-to-end", TestPassiveLcHighPassEndToEnd),
    ("Passive LC band-pass end-to-end", TestPassiveLcBandPassEndToEnd),
    ("Passive LC band-edge attenuation", TestPassiveLcBandEdgeAttenuation),
    ("Supported filter edge-spec audit", TestSupportedFilterEdgeSpecAudit),
    ("Automatic-order active edge-spec audit", TestAutomaticOrderActiveEdgeSpecAudit),
    ("Even-order Chebyshev passive LC edge audit", TestEvenOrderChebyshevPassiveLcEdgeAudit),
    ("Passive LC band-stop end-to-end", TestPassiveLcBandStopEndToEnd),
    ("Chebyshev-I passive LC end-to-end", TestChebyshevLcEndToEnd),
    ("Passive LC termination and prototype audit", TestPassiveLcTerminationAudit),
    ("Bessel LC positive-real Cauer synthesis", TestBesselLcCauerSynthesis),
    ("Bessel LC responses end-to-end", TestBesselLcResponsesEndToEnd),
    ("Gaussian and Linear-Phase LC feasibility audit", TestTabulatedLcFeasibilityAudit),
    ("Gaussian and Linear-Phase supported LC end-to-end", TestTabulatedLcSupportedEndToEnd),
    ("Filter v1 closure support matrix", TestFilterV1ClosureSupportMatrix),
    ("LC band transformation resonance", TestLcBandTransformationResonance),
    ("LC standard-value optimization", TestLcStandardValueOptimization),
    ("Schematic pin alignment", TestSchematicPinAlignment),
    ("Schematic component clearance", TestSchematicComponentClearance),
    ("Schematic occupied bounds", TestSchematicOccupiedBounds),
    ("Schematic wires avoid op-amps", TestSchematicWiresAvoidOpAmps),
    ("Selected topology reference schematics", TestSelectedTopologyReferences),
    ("MFB low-pass end-to-end", TestMfbLowPassEndToEnd),
    ("MFB high-pass end-to-end", TestMfbHighPassEndToEnd)
    ,("MFB band-pass end-to-end", TestMfbBandPassEndToEnd)
    ,("Active Bainter band-stop end-to-end", TestActiveBandStopEndToEnd)
    ,("Integrated active section gain", TestIntegratedActiveSectionGain)
    ,("High-pass default edge ordering", TestHighPassDefaultEdgeOrdering)
    ,("Band-stop default edge ordering", TestBandStopDefaultEdgeOrdering)
    ,("Op-amp input symbol separation", TestOpAmpInputSymbolSeparation)
    ,("Op-amp inputs electrically isolated", TestOpAmpInputsElectricallyIsolated)
    ,("Op-amp input drawing isolation", TestOpAmpInputDrawingIsolation)
    ,("Schematic junctions are real branches", TestSchematicJunctionsAreRealBranches)
    ,("LC routing has no retraced needles", TestLcRoutingHasNoRetracedNeedles)
    ,("Component ranges are enforced", TestComponentRangesAreEnforced)
    ,("Default active band-stop selects Bainter", TestDefaultActiveBandStopIsPractical)
    ,("Bainter schematic remains clean", TestBainterWiresAvoidComponentBodies)
    ,("Passive band modes expose RC topology and LC", TestPassiveBandModesSelectLc)
    ,("RC AC analysis known point", TestRcAcAnalysisKnownPoint)
    ,("Designed filter actual analysis", TestDesignedFilterActualAnalysis)
    ,("Passive LC insertion-gain analysis", TestPassiveLcInsertionGainAnalysis)
    ,("Multisim CIR export", TestMultisimCirExport)
    ,("LTspice common serializer golden syntax", TestLtspiceCommonSerializer)
    ,("LTspice op-amp subcircuit strategy", TestLtspiceOpAmpSubcircuit)
    ,("SPICE directive profiles", TestSpiceDirectiveProfiles)
    ,("SPICE invariant numeric formatting", TestSpiceInvariantFormatting)
    ,("Zero-ohm LC termination is rejected honestly", TestZeroOhmDefaultSource)
    ,("Passive response reference mathematics", TestPassiveResponseReferenceMathematics)
    ,("Passive RC and LC UI defaults", TestPassiveTerminationDefaults)
    ,("Passive loaded RC LP and HP equations", TestPassiveLoadedRcEquations)
    ,("Passive two-corner RC Band-Pass", TestPassiveTwoCornerRcBandPass)
    ,("Passive Twin-T compound matching", TestPassiveTwinTCompoundMatching)
    ,("LC termination policy and even-order Chebyshev", TestLcTerminationPolicies)
    ,("LC ladder orientation equivalence", TestLcLadderOrientations)
    ,("LC impedance scaling 50 75 600 ohm", TestLcImpedanceScaling)
    ,("Passive report and CIR boundary semantics", TestPassiveReportAndCirBoundary)
    ,("Passive LC structured feasibility results", TestPassiveLcStructuredFeasibility)
    ,("Passive manual test-row round trip", TestPassiveManualTestRowRoundTrip)
    ,("Combined active passive test-row round trip", TestCombinedManualTestRowRoundTrip)
    ,("Finite loaded RC 48-point regressions", TestFiniteLoadedRcRegressions)
    ,("RC Band-Pass and Twin-T structured diagnostics", TestRcBandAndTwinTDiagnostics)
    ,("Theoretical versus selected-value capability stages", TestCapabilityFailureStages)
    ,("Active Band-Pass feasibility reason codes", TestActiveBandPassFeasibilityReasons)
    ,("Filter v1 final closure classifications", TestFilterV1FinalClosureClassifications)
    ,("Filter design report model", TestFilterDesignReportModel)
    ,("Offline HTML and JSON report export", TestFilterDesignReportExport)
    ,("Engineering unit conversion and editable ranges", TestEngineeringUnitConversionAndRanges)
    ,("Sallen-Key gain feedback pin exits outward", TestSallenKeyGainFeedbackPinExit)
    ,("Official E48/E96 significant-digit values", TestOfficialHighDensityESeriesValues)
    ,("Manual under-order capability is not Supported", TestManualUnderOrderCapability)
    ,("Bounded E24 cascade optimization", TestBoundedE24CascadeOptimization)
    ,("Bainter K1 range freedom and lightweight availability", TestBainterK1RangeFreedomAndAvailability)
    ,("Precision analog affine CircuitGraph verification", TestPrecisionAnalogAffineCircuitGraph)
    ,("Precision analog E24 joint ratio optimization", TestPrecisionAnalogE24Optimization)
    ,("Precision analog E12 exhaustive bounded search", TestPrecisionAnalogE12Optimization)
    ,("Precision analog honest E12 gain-three limit", TestPrecisionAnalogExactE12GainThree)
    ,("Precision analog eight-input synthesis", TestPrecisionAnalogEightInputs)
    ,("Precision analog interactive multi-input sweep", TestPrecisionAnalogInteractiveSweep)
    ,("Precision analog rail-limited multi-stage sweep", TestPrecisionAnalogRailLimitedSweep)
    ,("Precision analog Multisim CIR export", TestPrecisionAnalogCirExport)
    ,("Precision analog nonlinear and AC CIR export", TestPrecisionAnalogExtendedCirExport)
    ,("Precision analog range mapper", TestPrecisionAnalogRangeMapper)
    ,("Precision analog supply capability", TestPrecisionAnalogSupplyCapability)
    ,("Precision analog abstract diode model", TestPrecisionAnalogAbstractDiode)
    ,("Precision analog schematic and in-app report", TestPrecisionAnalogPresentation)
    ,("Precision analog wizard initializes operation list", TestPrecisionAnalogWizardInitialization)
    ,("Precision analog UI result pages remain stable", TestPrecisionAnalogUiResultPageStability)
    ,("Precision analog operation fields survive DPI scaling", TestPrecisionAnalogDpiFieldLayout)
    ,("Precision analog half-wave end-to-end", TestPrecisionAnalogHalfWave)
    ,("Precision analog full-wave end-to-end", TestPrecisionAnalogFullWave)
    ,("Precision analog practical integrator end-to-end", TestPrecisionAnalogIntegrator)
    ,("Precision analog practical differentiator end-to-end", TestPrecisionAnalogDifferentiator)
    ,("Precision analog logarithmic end-to-end", TestPrecisionAnalogLog)
    ,("Precision analog antilog end-to-end", TestPrecisionAnalogAntilog)
    ,("Precision analog E24 E96 operation matrix", TestPrecisionAnalogSeriesMatrix)
    ,("Precision analog invalid specification matrix", TestPrecisionAnalogInvalidSpecificationMatrix)
    ,("Precision analog constrained practical matrix", TestPrecisionAnalogConstrainedPracticalMatrix)
    ,("Precision analog performance and render smoke", TestPrecisionAnalogPerformanceAndRender)
    ,("Precision analog v1 capability failures and freeze", TestPrecisionAnalogV1CapabilityFailures)
};

var failures = new List<string>();
foreach (var test in tests)
{
    if (args.Length > 0 && !test.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)) continue;
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failures.Add($"FAIL {test.Name}: {exception.Message}"); Console.WriteLine(failures[^1]); }
}
if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) failed.");
    Environment.ExitCode = 1;
    return;
}
Console.WriteLine($"All {tests.Length} filter design tests passed.");

static void TestButterworthOrder() => Equal(8, new ButterworthOrderCalculator().CalculateMinimumOrder(1, 40, 2));
static void TestChebyshevOrder() => Equal(5, new ChebyshevOrderCalculator().CalculateMinimumOrder(1, 40, 2));

static void TestPrecisionAnalogAffineCircuitGraph()
{
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Inputs = [new("U1", -5, -1, 1), new("U2", 5, -1, 1), new("U3", 0.25, -2, 2)],
        OutputOffsetVoltage = 1.2,
        SupplyVccVoltage = 15,
        SupplyVeeVoltage = -15,
        MaximumFunctionErrorPercent = 0.01,
        Components = new AnalogComponentSelectionConstraints { OptimizeStandardValues = false, BiasCompensation = true }
    });
    Require(result.Success, result.Capability.Message);
    var graph = result.CircuitGraph ?? throw new InvalidOperationException("Affine design did not produce a CircuitGraph.");
    Require(graph.Components.Count(component => component.Type == ComponentType.OpAmp) == 2, "Mixed signs must use the explainable two-stage topology.");
    foreach (var metric in result.VerificationMetrics.Where(metric => metric.Name.StartsWith("Coefficient") || metric.Name == "Output offset"))
        Require(metric.Pass, $"Independent affine metric failed: {metric.Name}, target={metric.Target}, actual={metric.Actual}.");
    Require(graph.Nets.Any(net => net.Name == "VCC") && graph.Nets.Any(net => net.Name == "VEE"), "Supply rails must be explicit named nets.");
    Require(!graph.Nets.Any(net => net.Name.Equals("VREF", StringComparison.OrdinalIgnoreCase)), "The module must not accept or create an independent VREF source.");
}

static void TestPrecisionAnalogE24Optimization()
{
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Inputs = [new("U1", 2, -1, 1), new("U2", -0.5, -1, 1)],
        OutputOffsetVoltage = 1,
        MaximumFunctionErrorPercent = 5,
        Components = new AnalogComponentSelectionConstraints { ResistorSeries = AnalogStandardSeries.E24, OptimizeStandardValues = true }
    });
    Require(result.Success, result.Capability.Message);
    Require(result.Components.Where(component => component.Quantity == "Resistance").All(component => component.Series == AnalogStandardSeries.E24), "Selected resistors did not retain E24 provenance.");
    Require(result.VerificationMetrics.Where(metric => metric.Name.StartsWith("Coefficient") || metric.Name == "Output offset").All(metric => metric.Pass), "E24 result did not pass actual CircuitGraph verification.");
}

static void TestPrecisionAnalogE12Optimization()
{
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Inputs = [new("U1", 1.7), new("U2", -0.65)], OutputOffsetVoltage = .8,
        MaximumFunctionErrorPercent = 10,
        Components = new AnalogComponentSelectionConstraints { ResistorSeries = AnalogStandardSeries.E12, OptimizeStandardValues = true }
    });
    Require(result.Success, result.Capability.Message);
    Require(result.VerificationMetrics.Where(metric => metric.Name.StartsWith("Coefficient") || metric.Name == "Output offset").All(metric => metric.Pass), "E12 exhaustive search did not pass its configured actual-function tolerance.");
}

static void TestPrecisionAnalogExactE12GainThree()
{
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Inputs = [new("U1", 3)], OutputOffsetVoltage = 0,
        MaximumFunctionErrorPercent = 1,
        Components = new AnalogComponentSelectionConstraints { ResistorSeries = AnalogStandardSeries.E12, OptimizeStandardValues = true }
    });
    Require(!result.Success && result.Capability.ReasonCode == AnalogCapabilityReasonCode.StandardValueAccuracyInsufficient,
        "A single-resistor E12 ratio must not be falsely accepted as 3 within 1%.");
    var coefficient = result.VerificationMetrics.Single(metric => metric.Name == "Coefficient U1");
    Require(Nearly(coefficient.Actual, 3.030303, 1e-5), $"Unexpected best E12 gain-three coefficient {coefficient.Actual}.");
    var e24 = new PrecisionAnalogDesignEngine().Design(result.Request with { Components = result.Request.Components with { ResistorSeries = AnalogStandardSeries.E24 } });
    Require(e24.Success && Math.Abs(e24.VerificationMetrics.Single(metric => metric.Name == "Coefficient U1").Actual - 3) < .001, "E24 should realize nominal gain 3 inside 1%.");
}

static void TestPrecisionAnalogEightInputs()
{
    var inputs = Enumerable.Range(1, 8).Select(index => new AnalogInputTerm($"U{index}", index % 2 == 0 ? -5 : 5)).ToArray();
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Inputs = inputs, MaximumFunctionErrorPercent = 1,
        Components = new AnalogComponentSelectionConstraints { ResistorSeries = AnalogStandardSeries.E24 }
    });
    Require(result.Success, result.Capability.Message);
    Require(result.VerificationMetrics.Count(metric => metric.Name.StartsWith("Coefficient")) == 8, "Eight-input design did not independently extract every coefficient.");
}

static void TestPrecisionAnalogInteractiveSweep()
{
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Inputs = [new("U1", 2), new("U2", -3), new("U3", .5)], OutputOffsetVoltage = 1,
        MaximumFunctionErrorPercent = 5,
        Components = new AnalogComponentSelectionConstraints { ResistorSeries = AnalogStandardSeries.E96 }
    });
    Require(result.Success, result.Capability.Message);
    var analysis = new AffineSweepAnalysisService().Analyze(result, "U2", new Dictionary<string, double> { ["U1"] = 2, ["U3"] = -1 }, -2, 2, 101);
    Equal(101, analysis.Points.Count);
    Require(Nearly(analysis.Points[0].Target, 10.5, 1e-12) && Nearly(analysis.Points[^1].Target, -1.5, 1e-12), "Interactive sweep did not hold the other n-1 inputs at their configured values.");
    Require(analysis.Points.Max(point => Math.Abs(point.Actual - point.Target)) < .2, "Actual CircuitGraph sweep diverged from the target curve.");
}

static void TestPrecisionAnalogRailLimitedSweep()
{
    var inputs = Enumerable.Range(1, 8).Select(index => new AnalogInputTerm($"U{index}", 1)).ToArray();
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Inputs = inputs, MaximumFunctionErrorPercent = 5,
        SupplyVccVoltage = 15, SupplyVeeVoltage = -15
    });
    Require(result.Success, result.Capability.Message);
    var fixedValues = inputs.ToDictionary(input => input.Name, _ => 1d);
    fixedValues["U4"] = 111;
    var analysis = new AffineSweepAnalysisService().Analyze(result, "U1", fixedValues, -6, 6, 101);
    Require(analysis.Points.Any(point => point.IsSupplyLimited), "Rail-limited sweep did not report internal/output op-amp saturation.");
    Require(analysis.Points.All(point => point.Actual >= -15.000001 && point.Actual <= 15.000001), "Rail-limited CircuitGraph simulation exceeded VCC/VEE.");
    Require(analysis.Points.Any(point => Math.Abs(point.Target) > 15 && Math.Abs(point.Actual) <= 15.000001), "Target and rail-limited actual curves were not distinguished.");
    var document = new PrecisionAnalogSchematicLayoutService().Layout(result.CircuitGraph!);
    Require(new ComponentCollisionDetector().Find(document, 4).Count == 0, "Eight-input schematic has overlapping component bodies after dynamic row layout.");
}

static void TestPrecisionAnalogCirExport()
{
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Inputs = [new("U1", 1), new("U2", -1)], OutputOffsetVoltage = .5,
        MaximumFunctionErrorPercent = 5
    });
    Require(result.Success, result.Capability.Message);
    var cir = new PrecisionAnalogSpiceExportService().Export(result);
    foreach (var token in new[] { "V_VCC VCC 0 DC", "V_VEE VEE 0 DC", "V_U1 U1 0 DC", "V_U2 U2 0 DC", "E_U1", ".DC V_U1", ".PRINT DC V(VOUT)", ".END" })
        Require(cir.Contains(token, StringComparison.Ordinal), $"Precision Analog CIR export omitted {token}.");
}

static void TestPrecisionAnalogExtendedCirExport()
{
    var halfWave = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Operation = AnalogComputeOperationType.PrecisionHalfWaveRectifier, GainK = 1,
        MinimumInputVoltage = -2, MaximumInputVoltage = 2, MaximumFunctionErrorPercent = 5
    });
    Require(halfWave.Success, halfWave.Capability.Message);
    var rectifierExportRejected=false;
    try { _=new PrecisionAnalogSpiceExportService().Export(halfWave); }
    catch(NotSupportedException exception) { rectifierExportRejected=exception.Message.Contains("ideal-switch",StringComparison.OrdinalIgnoreCase); }
    Require(rectifierExportRejected,"Ideal-switch rectifier verification must not silently export a different Shockley-diode circuit.");

    var antilog = new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.ExponentialAmplifier) with
    {MinimumInputVoltage=-.2,MaximumInputVoltage=.2,ReferenceVoltage=1,LogScaleVoltage=.1});
    Require(antilog.Success,antilog.Capability.Message);
    var nonlinear = new PrecisionAnalogSpiceExportService().Export(antilog);
    Require(nonlinear.Contains("D1 ",StringComparison.Ordinal)&&nonlinear.Contains(".MODEL CMD_DIODE D(IS=",StringComparison.Ordinal)&&
            nonlinear.Contains(".TEMP ",StringComparison.Ordinal)&&nonlinear.Contains(".DC V_VIN",StringComparison.Ordinal),
        "Verified Shockley-junction Antilog export is incomplete.");

    var integrator = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Operation = AnalogComputeOperationType.PracticalIntegrator, IntegrationConstantPerSecond = 1_000,
        LowerCornerHz = 10, UpperCornerHz = 10_000, MinimumInputVoltage = -.05, MaximumInputVoltage = .05,
        MaximumFunctionErrorPercent = 10
    });
    Require(integrator.Success, integrator.Capability.Message);
    var dynamic = new PrecisionAnalogSpiceExportService().Export(integrator);
    Require(dynamic.Contains("C1 ", StringComparison.Ordinal) && dynamic.Contains("V_U1 VIN 0 DC 0 AC 1e+0 0", StringComparison.Ordinal) &&
            dynamic.Contains(".AC DEC 60 1e+0 1e+5", StringComparison.Ordinal) && !dynamic.Contains(".DC ", StringComparison.Ordinal),
        $"Precision dynamic export is not an AC netlist.\n{dynamic}");
}

static void TestPrecisionAnalogRangeMapper()
{
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Operation = AnalogComputeOperationType.VoltageRangeMapper,
        InputLowVoltage = 0.5, InputHighVoltage = 4.5,
        OutputLowVoltage = 0, OutputHighVoltage = 3.3,
        MaximumFunctionErrorPercent = 2,
        Components = new AnalogComponentSelectionConstraints { ResistorSeries = AnalogStandardSeries.E96 }
    });
    Require(result.Success, result.Capability.Message);
    Require(Nearly(result.Request.Inputs[0].Coefficient, 0.825, 1e-12), "Range Mapper scale was not derived from endpoints.");
    Require(Nearly(result.Request.OutputOffsetVoltage, -0.4125, 1e-12), "Range Mapper offset was not derived from endpoints.");
    Require(result.Analysis?.Points.Count == 51, "Range Mapper must provide a full-range target/actual DC sweep.");
    var plotted = new AffineSweepAnalysisService().Analyze(result, "VIN", new Dictionary<string, double>(), result.Request.InputLowVoltage, result.Request.InputHighVoltage);
    Require(Nearly(plotted.Points[0].Input, .5, 1e-12) && Nearly(plotted.Points[^1].Input, 4.5, 1e-12), "Range Mapper analysis horizontal axis does not use the configured input range.");
    Require(Nearly(plotted.Points[0].Target, 0, 1e-12) && Nearly(plotted.Points[^1].Target, 3.3, 1e-12), "Range Mapper analysis vertical target does not use the configured output range.");
    Require(plotted.Points.All(point => point.Actual >= result.RequiredVeeVoltage - 1e-9 && point.Actual <= result.RequiredVccVoltage + 1e-9), "Range Mapper analysis did not enforce VCC/VEE limits.");
}

static void TestPrecisionAnalogSupplyCapability()
{
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest { SupplyVccVoltage = 0, SupplyVeeVoltage = -15 });
    Require(!result.Success && result.Capability.ReasonCode == AnalogCapabilityReasonCode.SupplyRailsInvalid, "Invalid VCC/VEE was not rejected structurally.");
}

static void TestPrecisionAnalogAbstractDiode()
{
    var diode = ComponentFactory.Diode("D1");
    Require(diode.Type == ComponentType.Diode && diode.GetPin("A").Name == "A" && diode.GetPin("K").Name == "K", "Abstract diode A/K pins are missing.");
    Require(diode.Parameters.Values.Count == 0, "Vendor-specific junction parameters must not be stored in CircuitGraph component parameters.");
    var junction = new JunctionModelParameters { SaturationCurrentAmpere = 1e-12, EmissionCoefficient = 1.1, TemperatureCelsius = 25 };
    Require(junction.ThermalVoltage > .025 && junction.ThermalVoltage < .026, "Shockley thermal voltage is outside the 25 °C range.");
    Require(junction.Current(.6) > 0, "Shockley junction current must increase for forward voltage.");
}

static void TestPrecisionAnalogPresentation()
{
    Require(new AnalogComponentSelectionConstraints().ResistorSeries == AnalogStandardSeries.E96, "Precision Analog must default to E96.");
    Require(new FilterDesignState().ResistorSeries == StandardComponentSeries.E96, "Filter Design must default resistors to E96.");
    var result = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Inputs = [new("U1", 1), new("U2", -1)], OutputOffsetVoltage = .5,
        MaximumFunctionErrorPercent = 5,
        Components = new AnalogComponentSelectionConstraints { ResistorSeries = AnalogStandardSeries.E96 }
    });
    Require(result.Success, result.Capability.Message);
    var graph = result.CircuitGraph ?? throw new InvalidOperationException("Presentation test has no circuit.");
    var document = new PrecisionAnalogSchematicLayoutService().Layout(graph);
    Equal(graph.Components.Count, document.PlacedComponents.Count);
    Require(document.Wires.Count > 0, "Precision Analog schematic contains no routed nets.");
    var collisions = new ComponentCollisionDetector().Find(document, 4);
    Require(collisions.Count == 0, "Precision Analog operation-aware layout contains overlapping component bodies: " + string.Join(", ", collisions.Select(pair => $"{pair.First.Component.Reference}/{pair.Second.Component.Reference}")));
    var wireCrossings = new List<string>();
    foreach (var item in document.PlacedComponents)
    {
        var interior = SchematicGeometry.GetBounds(item);
        foreach (var wire in document.Wires)
        for (var index = 1; index < wire.Points.Count; index++)
            if (SegmentCrossesInterior(wire.Points[index - 1], wire.Points[index], interior))
                wireCrossings.Add($"{wire.NetName}/{item.Component.Reference}");
    }
    Require(wireCrossings.Count == 0, "Precision Analog wire crosses a component body: " + string.Join(", ", wireCrossings.Distinct()));
    var report = new PrecisionAnalogDesignReportBuilder().BuildText(result);
    Require(report.Contains("CapabilityLevel") && report.Contains("CapabilityReasonCode") && report.Contains("VCC") && report.Contains("VEE"), "In-app report omitted capability or supply/reference information.");
    var reportService = new PrecisionAnalogDesignReportService();
    var html = reportService.ExportHtml(result); var json = reportService.ExportJson(result);
    Require(html.Contains("<!DOCTYPE html>") && html.Contains("2. Component Values") && html.Contains("3. Actual Verification") && html.Contains("4. JSON Snapshot") && html.Contains("<table>"), "Precision Analog structured HTML report export is incomplete.");
    Require(!html.Contains("Warnings and Design Notes") && !html.Contains("Calculation Trace") && !html.Contains("Circuit Summary"), "Precision Analog concise report still contains removed sections.");
    Require(json.Contains("CircuitModuleDesigner.PrecisionAnalogDesign.v1") && json.Contains("\"Circuit\"") && json.Contains("\"Verification\""), "Precision Analog JSON snapshot is incomplete.");
}

static void TestPrecisionAnalogWizardInitialization()
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            using var tree = new TreeView();
            using var wizard = new PrecisionAnalogWizardControl();
            wizard.CreateControl();
            var advancedPanel=(Control)typeof(PrecisionAnalogWizardControl).GetField("advancedPanel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(wizard)!;
            Require(!advancedPanel.Visible,"Advanced electrical/component constraints must not occupy the primary specification surface by default.");
            using var host = new Form();
            host.Controls.Add(wizard);
            host.CreateControl();
            var seriesChoices = Descendants(wizard).OfType<ComboBox>()
                .Where(box => box.Items.Cast<object>().Any(item => item is AnalogStandardSeries)).ToArray();
            Require(seriesChoices.Length == 2 && seriesChoices.All(box => Equals(box.SelectedItem, AnalogStandardSeries.E96)),
                "Hosted Precision Analog series selectors must default to E96: " + string.Join(", ", seriesChoices.Select(box => box.SelectedItem)));
            seriesChoices[0].SelectedItem = AnalogStandardSeries.E24;
            host.Controls.Remove(wizard);
            host.Controls.Add(wizard);
            Require(Equals(seriesChoices[0].SelectedItem, AnalogStandardSeries.E24), "Reparenting must preserve the user's series selection.");
            seriesChoices[0].SelectedItem = AnalogStandardSeries.E96;
            host.Controls.Remove(wizard);
            Require(tree.Nodes.Count == 0, "Preloaded Precision Analog wizard changed navigation before activation.");
            wizard.AttachNavigation(tree);
            Require(tree.Nodes.Count == 1 && tree.Nodes[0].Nodes.Count == Enum.GetValues<AnalogComputeOperationType>().Length, "Wizard navigation did not expose all planned operations.");
            var inputMinimum=(NumericUpDown)typeof(PrecisionAnalogWizardControl).GetField("inputMinimum",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(wizard)!;
            var operationField=typeof(PrecisionAnalogWizardControl).GetField("selectedOperation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var operationChanged=typeof(PrecisionAnalogWizardControl).GetMethod("OperationChanged",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            void Select(AnalogComputeOperationType operation){operationField.SetValue(wizard,operation);operationChanged.Invoke(wizard,null);}
            Select(AnalogComputeOperationType.LogarithmicAmplifier);
            Require(inputMinimum.Value==.01m,"Log operation did not restore its own positive-domain input default.");
            var operationFields=(FlowLayoutPanel)typeof(PrecisionAnalogWizardControl).GetField("operationFields",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(wizard)!;
            var logLabels=operationFields.Controls.Cast<Control>().Where(control=>control.Visible).SelectMany(Descendants).OfType<Label>().Where(label=>label.Visible).Select(label=>label.Text).ToArray();
            Require(logLabels.SequenceEqual(new[]{"Vin Min (V):","Vin Max (V):","Vref (V):","Log Scale S (V):"}),"Log UI fields are incomplete or still expose internal junction parameters: "+string.Join(",",logLabels));
            var referenceVoltage=(NumericUpDown)typeof(PrecisionAnalogWizardControl).GetField("referenceVoltage",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(wizard)!;
            referenceVoltage.Value=2.5m;
            var buildRequest=typeof(PrecisionAnalogWizardControl).GetMethod("BuildRequest",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var logRequest=(AnalogComputeDesignRequest)buildRequest.Invoke(wizard,null)!;
            Require(Nearly(logRequest.ReferenceVoltage,2.5,1e-12),"The editable Vref value was not copied into the Log design request.");
            Require(typeof(PrecisionAnalogWizardControl).GetField("temperature",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic) is null&&
                    typeof(PrecisionAnalogWizardControl).GetField("ideality",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic) is null,
                "Temperature/ideality input controls still exist in the simplified UI.");
            Select(AnalogComputeOperationType.PrecisionHalfWaveRectifier);
            Require(inputMinimum.Value==-5m,"Rectifier inherited the Log operation input range instead of its own bipolar state.");
            inputMinimum.Value=-3m;
            Select(AnalogComputeOperationType.PrecisionFullWaveRectifier);
            Select(AnalogComputeOperationType.PrecisionHalfWaveRectifier);
            Require(inputMinimum.Value==-3m,"Rectifier operation state was not preserved across lightweight navigation changes.");
            var visibleLabels=operationFields.Controls.Cast<Control>().Where(control=>control.Visible).SelectMany(Descendants).OfType<Label>().Where(label=>label.Visible).Select(label=>label.Text).ToArray();
            Require(visibleLabels.SequenceEqual(new[]{"Polarity:","Gain K:","Vin Min (V):","Vin Max (V):"}),"Half-wave field visibility/order is unstable: "+string.Join(",",visibleLabels));
            var halfPolarity=(ComboBox)typeof(PrecisionAnalogWizardControl).GetField("halfPolarity",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(wizard)!;
            Require(halfPolarity.Visible&&halfPolarity.Items.Count==2,"Half-wave Positive/Negative selector disappeared after operation switching.");
            Select(AnalogComputeOperationType.PrecisionFullWaveRectifier);
            var outputPolarity=(ComboBox)typeof(PrecisionAnalogWizardControl).GetField("outputPolarity",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(wizard)!;
            Require(outputPolarity.Visible&&outputPolarity.Items.Count==2,"Full-wave Positive/Negative selector disappeared after operation switching.");
            wizard.DetachNavigation();
        }
        catch (Exception exception) { failure = exception; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start(); thread.Join();
    if (failure is not null) throw new InvalidOperationException("Precision Analog wizard construction failed: "+failure.Message, failure);
}

static void TestPrecisionAnalogUiResultPageStability()
{
    Exception? failure=null;
    var thread=new Thread(() =>
    {
        try
        {
            using var wizard=new PrecisionAnalogWizardControl();wizard.CreateControl();
            var result=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PrecisionHalfWaveRectifier));
            Require(result.Success,result.Capability.Message);
            var show=typeof(PrecisionAnalogWizardControl).GetMethod("ShowResult",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var tabs=(TabControl)typeof(PrecisionAnalogWizardControl).GetField("results",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(wizard)!;
            var analysisHost=(Panel)typeof(PrecisionAnalogWizardControl).GetField("analysisHost",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(wizard)!;
            var original=tabs.TabPages.Cast<TabPage>().ToArray();
            show.Invoke(wizard,[result]);wizard.PerformLayout();
            var rectifierPlot=analysisHost.Controls[0];var rectifierBounds=rectifierPlot.Bounds;
            for(var index=0;index<20;index++){show.Invoke(wizard,[result]);wizard.PerformLayout();}
            Require(tabs.TabPages.Count==5,"Repeated Design changed the result tab count.");
            Require(original.SequenceEqual(tabs.TabPages.Cast<TabPage>()),"Repeated Design recreated one or more result TabPages.");
            Require(ReferenceEquals(rectifierPlot,analysisHost.Controls[0])&&rectifierBounds==rectifierPlot.Bounds,
                "Repeated Rectifier Design rebuilt or moved the Vin-Vout plot control.");
            var log=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.LogarithmicAmplifier) with
            {MinimumInputVoltage=.1,MaximumInputVoltage=10,ReferenceVoltage=1,LogScaleVoltage=.1});
            var antilog=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.ExponentialAmplifier) with
            {MinimumInputVoltage=-.2,MaximumInputVoltage=.2,ReferenceVoltage=1,LogScaleVoltage=.1});
            Require(log.Success&&antilog.Success,"Log/Antilog UI reuse fixtures failed synthesis.");
            show.Invoke(wizard,[log]);var transferPlot=analysisHost.Controls[0];show.Invoke(wizard,[log]);
            Require(ReferenceEquals(transferPlot,analysisHost.Controls[0]),"Repeated Log rendering rebuilt the Vin-Vout plot control.");
            show.Invoke(wizard,[antilog]);
            Require(ReferenceEquals(transferPlot,analysisHost.Controls[0]),"Log-to-Antilog rendering rebuilt the compatible Vin-Vout plot control.");
            var integrator=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PracticalIntegrator) with
            {IntegrationConstantPerSecond=1_000,LowerCornerHz=10,UpperCornerHz=10_000});
            var differentiator=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PracticalDifferentiator) with
            {DifferentiationConstantSecond=.001,DifferentiationUsefulUpperFrequencyHz=1_000});
            Require(integrator.Success&&differentiator.Success,"Dynamic UI reuse fixtures failed synthesis.");
            show.Invoke(wizard,[integrator]);wizard.PerformLayout();
            var bodePlot=analysisHost.Controls[0];var bodeBounds=bodePlot.Bounds;
            var dynamicTabs=Descendants(bodePlot).OfType<TabControl>().Single();
            Require(dynamicTabs.TabPages.Count==3,"Dynamic Analysis must contain Magnitude, Phase, and Time-Domain pages.");
            var dynamicChildren=dynamicTabs.TabPages.Cast<TabPage>().SelectMany(page=>page.Controls.Cast<Control>())
                .Select(control=>(Control:control,Bounds:control.Bounds)).ToArray();
            var pageChildCounts=tabs.TabPages.Cast<TabPage>().Select(page=>page.Controls.Count).ToArray();
            for(var index=0;index<20;index++){show.Invoke(wizard,[integrator]);wizard.PerformLayout();}
            Require(ReferenceEquals(bodePlot,analysisHost.Controls[0]),"Repeated Integrator Design rebuilt the Bode control.");
            Require(bodePlot.Bounds==bodeBounds,"Repeated Integrator Design moved or resized the Bode control.");
            Require(dynamicChildren.All(item=>item.Control.Bounds==item.Bounds),"Repeated Integrator Design moved an Analysis child control.");
            Require(pageChildCounts.SequenceEqual(tabs.TabPages.Cast<TabPage>().Select(page=>page.Controls.Count)),"Repeated Integrator Design duplicated result-page controls.");
            show.Invoke(wizard,[differentiator]);wizard.PerformLayout();
            Require(ReferenceEquals(bodePlot,analysisHost.Controls[0]),"Integrator-to-Differentiator rendering rebuilt the compatible Bode control.");
        }
        catch(Exception exception){failure=exception;}
    });
    thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
    if(failure is not null)throw new InvalidOperationException("Precision Analog repeated-Design UI regression failed: "+failure.Message,failure);
}

static void TestPrecisionAnalogDpiFieldLayout()
{
    Exception? failure=null;
    var thread=new Thread(() =>
    {
        try
        {
            foreach(var scale in new[]{1f,1.25f,1.5f})
            {
                using var wizard=new PrecisionAnalogWizardControl{Size=new Size((int)(1000*scale),(int)(700*scale))};wizard.CreateControl();
                if(scale!=1)wizard.Scale(new SizeF(scale,scale));wizard.PerformLayout();
                var operation=typeof(PrecisionAnalogWizardControl).GetField("selectedOperation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                var fields=(FlowLayoutPanel)typeof(PrecisionAnalogWizardControl).GetField("operationFields",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(wizard)!;
                var change=typeof(PrecisionAnalogWizardControl).GetMethod("OperationChanged",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                foreach(var expected in new (AnalogComputeOperationType Operation,int Fields)[]
                {
                    (AnalogComputeOperationType.PrecisionHalfWaveRectifier,4),(AnalogComputeOperationType.PrecisionFullWaveRectifier,4),
                    (AnalogComputeOperationType.LogarithmicAmplifier,4),(AnalogComputeOperationType.ExponentialAmplifier,4),
                    (AnalogComputeOperationType.PracticalIntegrator,2),(AnalogComputeOperationType.PracticalDifferentiator,2)
                })
                {
                    operation.SetValue(wizard,expected.Operation);change.Invoke(wizard,null);wizard.PerformLayout();fields.PerformLayout();
                    var visible=fields.Controls.Cast<Control>().Where(control=>control.Visible).ToArray();
                    Require(visible.Length==expected.Fields,$"{expected.Operation} lost field groups at DPI scale {scale}.");
                    Require(visible[0].Left<=fields.Padding.Left+2,$"{expected.Operation} retained a stale horizontal offset at DPI scale {scale}: left={visible[0].Left}.");
                    Require(visible.All(control=>control.Width>0&&control.Height>0&&control.Bottom<=fields.ClientSize.Height),$"{expected.Operation} contains a collapsed or clipped field group at DPI scale {scale}.");
                    for(var first=0;first<visible.Length;first++)for(var second=first+1;second<visible.Length;second++)
                        Require(!visible[first].Bounds.IntersectsWith(visible[second].Bounds),$"{expected.Operation} field groups overlap at DPI scale {scale}: {first}/{second}");
                    foreach(var pair in visible)
                    {
                        var children=pair.Controls.Cast<Control>().Where(control=>control.Visible).ToArray();
                        Require(children.Length==2&&!children[0].Bounds.IntersectsWith(children[1].Bounds),$"{expected.Operation} label and input overlap at DPI scale {scale}.");
                    }
                }
            }
        }
        catch(Exception exception){failure=exception;}
    });
    thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
    if(failure is not null)throw new InvalidOperationException("Precision Analog DPI field layout failed: "+failure.Message,failure);
}

static IEnumerable<Control> Descendants(Control root)
{
    foreach(Control child in root.Controls){yield return child;foreach(var descendant in Descendants(child))yield return descendant;}
}

static AnalogComputeDesignRequest PrecisionOperation(AnalogComputeOperationType operation) => new()
{
    Operation=operation,Inputs=[new("VIN",1)],MinimumInputVoltage=-2,MaximumInputVoltage=2,
    MaximumFunctionErrorPercent=2,SupplyVccVoltage=15,SupplyVeeVoltage=-15,
    Components=new AnalogComponentSelectionConstraints
    {
        ResistorSeries=AnalogStandardSeries.E96,CapacitorSeries=AnalogStandardSeries.E96,
        ResistorMinimumOhm=1_000,ResistorMaximumOhm=1_000_000,
        CapacitorMinimumFarad=10e-12,CapacitorMaximumFarad=10e-6,OptimizeStandardValues=true
    }
};

static void RequirePrecisionPresentation(AnalogComputeDesignResult result)
{
    Require(result.Success,result.Capability.ReasonCode+": "+result.Capability.Message);
    Require(result.CircuitGraph is not null&&result.CircuitGraph.Components.Count>0&&result.Analysis?.Points.Count>=50,"Precision operation omitted CircuitGraph or analysis.");
    var graph=result.CircuitGraph!;
    Require(result.VerificationMetrics.Count>0&&result.VerificationMetrics.All(metric=>metric.Pass),"Precision operation contains a failed verification metric.");
    var document=new PrecisionAnalogSchematicLayoutService().Layout(graph);
    Equal(graph.Components.Count,document.PlacedComponents.Count);
    Require(document.Wires.Count>0,"Precision operation schematic contains no wiring.");
    var bodyCollisions=new ComponentCollisionDetector().Find(document,4);
    Require(bodyCollisions.Count==0,"Precision operation schematic contains overlapping component bodies: "+string.Join(", ",bodyCollisions.Select(pair=>$"{pair.First.Component.Reference}/{pair.Second.Component.Reference}")));
    var report=new PrecisionAnalogDesignReportService();
    Require(report.ExportHtml(result).Contains(result.TopologyName,StringComparison.Ordinal)&&report.ExportJson(result).Contains("\"Verification\"",StringComparison.Ordinal),"Precision operation report is incomplete.");
}

static void TestPrecisionAnalogHalfWave()
{
    foreach(var polarity in Enum.GetValues<HalfWavePolarity>())
    {
        var result=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PrecisionHalfWaveRectifier) with
        {GainK=2,HalfWavePolarity=polarity,MaximumExpectedFrequencyHz=1_000});
        RequirePrecisionPresentation(result);
        Require(result.CircuitGraph!.Components.Count(component=>component.Type==ComponentType.Diode)==2,"Half-wave topology does not contain both feedback diodes.");
        Require(result.CircuitGraph.GetNetForPin("D1","A")!.Name=="U1_RAW"&&result.CircuitGraph.GetNetForPin("D1","K")!.Name=="VOUT","Half-wave output diode A/K direction is reversed.");
        Require(result.CircuitGraph.GetNetForPin("D2","A")!.Name=="SUM"&&result.CircuitGraph.GetNetForPin("D2","K")!.Name=="U1_RAW","Half-wave local-feedback diode A/K direction is reversed.");
        Require(result.CircuitGraph.Components.Any(component=>component.Reference=="U2")== (polarity==HalfWavePolarity.Positive),"Positive half-wave must invert the input before the negative-drive rectifier core.");
        RequireRectifierSchematicClear(result);
        var blocked=result.Analysis!.Points.Where(point=>polarity==HalfWavePolarity.Positive?point.Input<0:point.Input>0);
        Require(blocked.Max(point=>Math.Abs(point.Actual))<1e-8,"Half-wave blocking region is not zero.");
    }
    var simplifiedUi=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PrecisionHalfWaveRectifier) with
    {GainK=3,HalfWavePolarity=HalfWavePolarity.Negative,MinimumInputVoltage=-5,MaximumInputVoltage=5});
    Require(simplifiedUi.Success,"Hidden rectifier sweep defaults must not trigger an output-swing failure when the UI does not request an input range.");
    Require(simplifiedUi.VerificationMetrics.All(metric=>metric.Name!="Output swing"),"Simplified rectifier UI must not report a hidden output-swing gate.");
    var clippedUi=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PrecisionHalfWaveRectifier) with
    {GainK=10,HalfWavePolarity=HalfWavePolarity.Positive,MinimumInputVoltage=-5,MaximumInputVoltage=5});
    Require(clippedUi.Success,"Rectifier UI analysis clipping must not reject an otherwise valid topology.");
    Require(clippedUi.Analysis!.Points.Any(point=>point.IsSupplyLimited)&&clippedUi.Analysis.Points.Max(point=>point.Actual)<=15+1e-9,"Rectifier Analysis did not show the rail-clipped selected-value curve.");
    Require(clippedUi.Analysis.AnalysisType.Contains("Vin-Vout",StringComparison.OrdinalIgnoreCase)&&
            Nearly(clippedUi.Analysis.Points[0].Input,-5,1e-12)&&Nearly(clippedUi.Analysis.Points[^1].Input,5,1e-12),
        "Rectifier primary Analysis must remain the independently verified Vin-Vout transfer.");
}

static void TestPrecisionAnalogFullWave()
{
    foreach(var polarity in Enum.GetValues<RectifierOutputPolarity>())
    {
        var result=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PrecisionFullWaveRectifier) with
        {GainK=1.5,OutputPolarity=polarity});
        RequirePrecisionPresentation(result);
        Require(result.CircuitGraph!.Components.Count(component=>component.Type==ComponentType.Diode)==2,"Full-wave topology must contain the two feedback-steering diodes.");
        var positive=result.VerificationMetrics.Single(metric=>metric.Name=="Positive-side gain").Actual;
        var negative=result.VerificationMetrics.Single(metric=>metric.Name=="Negative-side gain").Actual;
        Require(Math.Abs(positive-negative)<=.015,"Full-wave positive/negative paths exceed the allowed coupled-ratio mismatch.");
        Require(result.CircuitGraph.GetNetForPin("D1","A")!.Name=="RAW_RECT"&&result.CircuitGraph.GetNetForPin("D1","K")!.Name=="HALF_RECT","Full-wave output diode A/K direction is reversed.");
        Require(result.CircuitGraph.GetNetForPin("D2","A")!.Name=="SUM_RECT"&&result.CircuitGraph.GetNetForPin("D2","K")!.Name=="RAW_RECT","Full-wave local-feedback diode A/K direction is reversed.");
        RequireRectifierSchematicClear(result);
        Require(result.Analysis!.Points.All(point=>polarity==RectifierOutputPolarity.Positive?point.Actual>=-1e-9:point.Actual<=1e-9),"Full-wave output polarity was not implemented in the selected CircuitGraph.");
    }
}

static void RequireRectifierSchematicClear(AnalogComputeDesignResult result)
{
    var document=new PrecisionAnalogSchematicLayoutService().Layout(result.CircuitGraph!);
    var bodyCollisions=new ComponentCollisionDetector().Find(document,12);
    Require(bodyCollisions.Count==0,"Rectifier schematic component/value regions are too close: "+string.Join(", ",bodyCollisions.Select(pair=>$"{pair.First.Component.Reference}/{pair.Second.Component.Reference}")));
    var crossings=new List<string>();
    foreach(var item in document.PlacedComponents)
    {
        var interior=SchematicGeometry.GetBounds(item);
        foreach(var wire in document.Wires)
        for(var index=1;index<wire.Points.Count;index++)
            if(SegmentCrossesInterior(wire.Points[index-1],wire.Points[index],interior))crossings.Add($"{wire.NetName}/{item.Component.Reference}");
    }
    Require(crossings.Count==0,"Rectifier wire crosses a component body: "+string.Join(", ",crossings.Distinct()));
}

static void TestPrecisionAnalogIntegrator()
{
    var result=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PracticalIntegrator) with
    {IntegrationConstantPerSecond=1_000,LowerCornerHz=10,UpperCornerHz=10_000,MinimumInputVoltage=-.05,MaximumInputVoltage=.05});
    RequirePrecisionPresentation(result);
    Require(result.CircuitGraph!.Components.Any(component=>component.Type==ComponentType.Capacitor)&&
            result.VerificationMetrics.Any(metric=>metric.Name=="Integration constant Ki"),"Integrator did not use/verify its feedback capacitor and Ki.");
    Require(result.Analysis!.Points.All(point=>point.ActualPhaseDegrees.HasValue),"Integrator analysis omitted MNA phase.");
    var time=result.Analysis.TimeDomain!;
    Require(time.InputWaveform=="Square Wave"&&time.OutputWaveform=="Triangle Wave"&&time.Points.Count>600,
        "Integrator Analysis did not generate a three-cycle square-to-triangle demonstration.");
    var positiveSlope=time.Points.Zip(time.Points.Skip(1),(first,second)=>(first,second))
        .Where(pair=>pair.first.InputVoltage>0&&pair.second.InputVoltage>0)
        .Average(pair=>pair.second.ActualOutputVoltage-pair.first.ActualOutputVoltage);
    var negativeSlope=time.Points.Zip(time.Points.Skip(1),(first,second)=>(first,second))
        .Where(pair=>pair.first.InputVoltage<0&&pair.second.InputVoltage<0)
        .Average(pair=>pair.second.ActualOutputVoltage-pair.first.ActualOutputVoltage);
    Require(positiveSlope<0&&negativeSlope>0,"Integrator time-domain polarity is not inverting.");
    var selectedValues=result.Components.ToDictionary(component=>component.Reference,component=>component.SelectedValue);
    var changedComponents=result.Components.Select(component=>component.Reference=="R1"
        ? component with{SelectedValue=component.SelectedValue*1.2}:component).ToArray();
    var changedTime=new DynamicTimeDomainAnalysisService().Analyze(result.Request,changedComponents);
    Require(Math.Abs(changedTime.ActualOutputAmplitudeVoltage-time.ActualOutputAmplitudeVoltage)>1e-5,
        "Integrator time-domain result is not derived from final selected component values.");
    var integratorPoint=result.Analysis.Points[result.Analysis.Points.Count/2];
    var integratorOmega=2*Math.PI*integratorPoint.Input;
    var integratorTransfer=-selectedValues["R2"]/selectedValues["R1"]/
        new Complex(1,integratorOmega*selectedValues["R2"]*selectedValues["C1"]);
    Require(Math.Abs(20*Math.Log10(integratorTransfer.Magnitude)-integratorPoint.Actual)<1e-6,
        "Integrator Bode curve is inconsistent with selected-value CircuitGraph MNA.");
    Require(result.TargetRelation.Contains("Rf/Rin",StringComparison.Ordinal)&&
            result.Calculations.Any(record=>record.Expression.Contains("Ki=1/(Rin*Cf)",StringComparison.Ordinal))&&
            result.Calculations.Any(record=>record.Expression.Contains("fL=1/(2*pi*Rf*Cf)",StringComparison.Ordinal)),
        "Integrator calculations do not document the complete practical transfer and design equations.");
    Require(result.Components.All(component=>component.Series is null or AnalogStandardSeries.E96),"Integrator selected components lost E96 provenance.");
    foreach(var series in new[]{AnalogStandardSeries.E24,AnalogStandardSeries.E48,AnalogStandardSeries.E96})
    {
        var request=PrecisionOperation(AnalogComputeOperationType.PracticalIntegrator) with
        {IntegrationConstantPerSecond=1_000,LowerCornerHz=10,UpperCornerHz=10_000};
        var selected=new PrecisionAnalogDesignEngine().Design(request with
        {Components=request.Components with{ResistorSeries=series,CapacitorSeries=series}});
        Require(selected.Success,$"Integrator {series} coupled optimization failed: {selected.Capability.Message}");
        Require(selected.Components.All(component=>component.Series is null||component.Series==series),$"Integrator {series} provenance is incorrect.");
    }
    RequireCleanDynamicSchematic(result);
}

static void TestPrecisionAnalogDifferentiator()
{
    var defaultResult = new PrecisionAnalogDesignEngine().Design(new AnalogComputeDesignRequest
    {
        Operation = AnalogComputeOperationType.PracticalDifferentiator,
        DifferentiationConstantSecond = .001, DifferentiationUsefulUpperFrequencyHz = 1000,
        MaximumFunctionErrorPercent = 1
    });
    Require(defaultResult.Success, "Default E96 differentiator at 1% failed: " + defaultResult.Capability.Message);
    foreach (var constant in new[] { .0001, .0003, .001 })
    foreach (var frequency in new[] { 100d, 300d, 1000d })
    {
        var selected = new PrecisionAnalogDesignEngine().Design(defaultResult.Request with
        { DifferentiationConstantSecond = constant, DifferentiationUsefulUpperFrequencyHz = frequency });
        Require(selected.Success, $"E96 differentiator Kd={constant}, fUseful={frequency}, 1%: {selected.Capability.Message}");
    }
    var tooStrict = new PrecisionAnalogDesignEngine().Design(defaultResult.Request with { MaximumFunctionErrorPercent = .001 });
    Require(!tooStrict.Success && tooStrict.Capability.ReasonCode == AnalogCapabilityReasonCode.StandardValueAccuracyInsufficient,
        "Unattainable E96 accuracy must still fail independent verification.");
    var outOfRange = new PrecisionAnalogDesignEngine().Design(defaultResult.Request with { DifferentiationUsefulUpperFrequencyHz = 10_000 });
    Require(!outOfRange.Success && outOfRange.Capability.ReasonCode == AnalogCapabilityReasonCode.ComponentRangeImpossible,
        "Physically infeasible differentiator ranges must still be rejected.");
    var result=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PracticalDifferentiator) with
    {DifferentiationConstantSecond=.001,LowerCornerHz=100,UpperCornerHz=10_000,MinimumInputVoltage=-.1,MaximumInputVoltage=.1});
    RequirePrecisionPresentation(result);
    Equal(2,result.CircuitGraph!.Components.Count(component=>component.Type==ComponentType.Capacitor));
    Require(result.VerificationMetrics.Any(metric=>metric.Name=="Upper limiting corner"),"Differentiator did not independently verify both limiting poles.");
    var theoreticalPhase=result.Analysis!.Points.Select(point=>point.TargetPhaseDegrees!.Value).ToArray();
    Require(theoreticalPhase.Zip(theoreticalPhase.Skip(1),(first,second)=>Math.Abs(second-first)).Max()<30,
        "Differentiator theoretical phase contains an atan2 branch-cut jump.");
    var time=result.Analysis!.TimeDomain!;
    Require(time.InputWaveform=="Triangle Wave"&&time.OutputWaveform=="Square Wave"&&time.Points.Count>900,
        "Differentiator Analysis did not generate a three-cycle triangle-to-square demonstration.");
    var rising=time.Points.Where(point=>point.Periods-Math.Floor(point.Periods) is >.08 and <.42)
        .Average(point=>point.ActualOutputVoltage);
    var falling=time.Points.Where(point=>point.Periods-Math.Floor(point.Periods) is >.58 and <.92)
        .Average(point=>point.ActualOutputVoltage);
    Require(rising<0&&falling>0,"Differentiator time-domain polarity is not Vout=-Kd*dVin/dt.");
    var selectedValues=result.Components.ToDictionary(component=>component.Reference,component=>component.SelectedValue);
    var changedComponents=result.Components.Select(component=>component.Reference=="R2"
        ? component with{SelectedValue=component.SelectedValue*1.2}:component).ToArray();
    var changedTime=new DynamicTimeDomainAnalysisService().Analyze(result.Request,changedComponents);
    Require(Math.Abs(changedTime.ActualOutputAmplitudeVoltage-time.ActualOutputAmplitudeVoltage)>1e-5,
        "Differentiator time-domain result is not derived from final selected component values.");
    var differentiatorPoint=result.Analysis.Points[result.Analysis.Points.Count/2];
    var s=new Complex(0,2*Math.PI*differentiatorPoint.Input);
    var differentiatorTransfer=-selectedValues["R2"]*selectedValues["C1"]*s/
        ((Complex.One+s*selectedValues["R1"]*selectedValues["C1"])*
         (Complex.One+s*selectedValues["R2"]*selectedValues["C2"]));
    Require(Math.Abs(20*Math.Log10(differentiatorTransfer.Magnitude)-differentiatorPoint.Actual)<1e-6,
        "Differentiator Bode curve is inconsistent with selected-value CircuitGraph MNA.");
    Require(result.TargetRelation.Contains("(1+s Rin Cin)(1+s Rf Cf)",StringComparison.Ordinal)&&
            result.Calculations.Any(record=>record.Expression.Contains("Kd=Rf*Cin",StringComparison.Ordinal)),
        "Differentiator does not document its complete bounded transfer and Kd relationship.");
    var automatic=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PracticalDifferentiator) with
    {DifferentiationConstantSecond=.001,DifferentiationUsefulUpperFrequencyHz=1_000});
    Require(automatic.Success,automatic.Capability.Message);
    var first=automatic.VerificationMetrics.Single(metric=>metric.Name=="Lower limiting corner");
    var second=automatic.VerificationMetrics.Single(metric=>metric.Name=="Upper limiting corner");
    Require(Nearly(first.Target,5_000,1e-12)&&Nearly(second.Target,10_000,1e-12),
        "Automatic differentiator bandwidth did not produce the documented 5x/10x limiting poles.");
    foreach(var series in new[]{AnalogStandardSeries.E24,AnalogStandardSeries.E48,AnalogStandardSeries.E96})
    {
        var request=PrecisionOperation(AnalogComputeOperationType.PracticalDifferentiator) with
        {DifferentiationConstantSecond=.001,DifferentiationUsefulUpperFrequencyHz=100,
         MaximumFunctionErrorPercent=series==AnalogStandardSeries.E24?6:2};
        var selected=new PrecisionAnalogDesignEngine().Design(request with
        {Components=request.Components with{ResistorSeries=series,CapacitorSeries=series}});
        Require(selected.Success,$"Differentiator {series} coupled optimization failed: {selected.Capability.Message}");
        Require(selected.Components.All(component=>component.Series is null||component.Series==series),$"Differentiator {series} provenance is incorrect.");
    }
    RequireCleanDynamicSchematic(result);
}

static void RequireCleanDynamicSchematic(AnalogComputeDesignResult result)
{
    var document=new PrecisionAnalogSchematicLayoutService().Layout(result.CircuitGraph!);
    Require(document.TextAnnotations.Count==0,"Dynamic-operation schematic contains non-circuit captions.");
    var collisions=new ComponentCollisionDetector().Find(document,12);
    Require(collisions.Count==0,"Dynamic-operation schematic component/value regions overlap: "+
        string.Join(", ",collisions.Select(pair=>$"{pair.First.Component.Reference}/{pair.Second.Component.Reference}")));
    foreach(var wire in document.Wires)
    foreach(var segment in wire.Points.Zip(wire.Points.Skip(1),(first,second)=>(First:first,Second:second)))
    {
        var crossed=document.PlacedComponents.FirstOrDefault(component=>SegmentCrossesInterior(segment.First,segment.Second,SchematicGeometry.GetBounds(component)));
        Require(crossed is null,$"Dynamic-operation net {wire.NetName} crosses {crossed?.Component.Reference}.");
    }
}

static void TestPrecisionAnalogLog()
{
    var result=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.LogarithmicAmplifier) with
    {MinimumInputVoltage=.1,MaximumInputVoltage=10,ReferenceVoltage=1,LogScaleVoltage=.1,
     Junction=new(){Behavior=DiodeBehaviorModel.ShockleyJunction,SaturationCurrentAmpere=1e-12,EmissionCoefficient=1,TemperatureCelsius=25}});
    RequirePrecisionPresentation(result);
    Require(result.Analysis!.AnalysisType.Contains("Vin-Vout",StringComparison.OrdinalIgnoreCase)&&result.Analysis.Points.All(point=>point.Input>0&&point.Theoretical.HasValue)&&
            Nearly(result.Analysis.Points[0].Input,.1,1e-12)&&Nearly(result.Analysis.Points[^1].Input,10,1e-9),"Log Analysis is not the configured Vin-Vout transfer.");
    Require(result.Warnings.Any(warning=>warning.Contains("vendor-independent",StringComparison.OrdinalIgnoreCase)),"Log matching warning is missing.");
    RequireCleanJunctionSchematic(result);
}

static void TestPrecisionAnalogAntilog()
{
    var result=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.ExponentialAmplifier) with
    {MinimumInputVoltage=-.2,MaximumInputVoltage=.2,ReferenceVoltage=1,LogScaleVoltage=.1,
     Junction=new(){Behavior=DiodeBehaviorModel.ShockleyJunction,SaturationCurrentAmpere=1e-12,EmissionCoefficient=1,TemperatureCelsius=25}});
    RequirePrecisionPresentation(result);
    var zero=result.Analysis!.Points.MinBy(point=>Math.Abs(point.Input))!;
    Require(Math.Abs(zero.Actual-1)<.03,"Antilog reference output at Vin=0 is incorrect.");
    Require(result.Analysis.AnalysisType.Contains("Vin-Vout",StringComparison.OrdinalIgnoreCase)&&result.Analysis.Points.All(point=>point.Theoretical.HasValue)&&
            Nearly(result.Analysis.Points[0].Input,-.2,1e-12)&&Nearly(result.Analysis.Points[^1].Input,.2,1e-12),"Antilog Analysis is not the configured Vin-Vout transfer.");
    RequireCleanJunctionSchematic(result);
}

static void RequireCleanJunctionSchematic(AnalogComputeDesignResult result)
{
    var document=new PrecisionAnalogSchematicLayoutService().Layout(result.CircuitGraph!);
    Require(document.TextAnnotations.Count==0,"Log/Antilog schematic should contain only the circuit, without functional-area captions.");
    var collisions=new ComponentCollisionDetector().Find(document,12);
    Require(collisions.Count==0,"Log/Antilog schematic contains overlapping component/value regions: "+string.Join(", ",collisions.Select(pair=>$"{pair.First.Component.Reference}/{pair.Second.Component.Reference}")));
    foreach(var label in document.NetLabels)
        Require(!document.PlacedComponents.Any(component=>PointInside(label.Position,SchematicGeometry.GetBounds(component,4))),
            $"Net label {label.Text} overlaps a component body.");
    foreach(var wire in document.Wires)
    foreach(var segment in wire.Points.Zip(wire.Points.Skip(1),(first,second)=>(First:first,Second:second)))
        Require(!document.PlacedComponents.Any(component=>SegmentCrossesInterior(segment.First,segment.Second,SchematicGeometry.GetBounds(component))),
            $"Net {wire.NetName} crosses a component body in the Log/Antilog layout.");

    var positions=document.PlacedComponents.ToDictionary(component=>component.Component.Reference,component=>component.Position);
    if(result.Request.Operation==AnalogComputeOperationType.LogarithmicAmplifier)
    {
        Require(Nearly(positions["U1"].X,positions["U2"].X,1e-12)&&positions["U1"].Y<positions["U2"].Y,
            "Matched Log cells are not aligned as parallel signal modules.");
        Require(positions["D1"].Y<positions["U1"].Y&&positions["D2"].Y<positions["U2"].Y&&positions["U3"].X>positions["U1"].X,
            "Log junction feedback and output scale stage lost their module ordering.");
    }
    else
    {
        Require(positions["R1"].X<positions["U2"].X&&positions["U2"].X<positions["D1"].X&&positions["D1"].X<positions["U3"].X,
            "Antilog input, junction, and output stages are not ordered left to right.");
        Require(positions["D2"].Y>positions["D1"].Y&&positions["U1"].Y>positions["U2"].Y,
            "Antilog matched-reference module is not grouped below the signal path.");
    }

    static bool PointInside(SchematicPoint point,SchematicRect bounds)=>
        point.X>bounds.X&&point.X<bounds.X+bounds.Width&&point.Y>bounds.Y&&point.Y<bounds.Y+bounds.Height;
}

static void TestPrecisionAnalogV1CapabilityFailures()
{
    Require(PrecisionAnalogV1Status.IsFrozen,"Precision Analog v1 is not marked frozen.");
    var invalidLog=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.LogarithmicAmplifier) with
    {MinimumInputVoltage=0,MaximumInputVoltage=1});
    Require(!invalidLog.Success&&invalidLog.Capability.ReasonCode==AnalogCapabilityReasonCode.InvalidInput,"Log Vin<=0 did not return InvalidInput.");
    var overflow=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.ExponentialAmplifier) with
    {MinimumInputVoltage=-10,MaximumInputVoltage=10,ReferenceVoltage=1,LogScaleVoltage=.1});
    Require(!overflow.Success&&overflow.Capability.ReasonCode==AnalogCapabilityReasonCode.InvalidInput,"Antilog overflow guard did not fail explicitly.");
    var swing=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PrecisionHalfWaveRectifier) with
    {GainK=20,MinimumInputVoltage=-2,MaximumInputVoltage=2});
    Require(swing.Success&&swing.Analysis!.Points.Any(point=>point.IsSupplyLimited)&&
            swing.Warnings.Any(warning=>warning.Contains("clipped",StringComparison.OrdinalIgnoreCase)),
        "Output swing exceedance should remain a successful design and appear as clipped Analysis samples plus a warning.");
    var affineSwing=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.GeneralLinearCombination) with
    {Inputs=[new("U1",20,-2,2,0)]});
    Require(affineSwing.Success&&affineSwing.Warnings.Any(warning=>warning.Contains("beyond",StringComparison.OrdinalIgnoreCase)),
        "Linear/Affine output swing is still incorrectly treated as a synthesis failure.");
    var junctionSwing=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.ExponentialAmplifier) with
    {MinimumInputVoltage=-.2,MaximumInputVoltage=.2,ReferenceVoltage=10,LogScaleVoltage=.1});
    Require(junctionSwing.Success&&junctionSwing.Analysis!.Points.Any(point=>point.IsSupplyLimited)&&
            junctionSwing.Warnings.Any(warning=>warning.Contains("clipped",StringComparison.OrdinalIgnoreCase)),
        "Log/Antilog output swing is still incorrectly treated as a verification failure.");
    var slow=new PrecisionAnalogDesignEngine().Design(PrecisionOperation(AnalogComputeOperationType.PracticalDifferentiator) with
    {DifferentiationConstantSecond=.001,LowerCornerHz=100,UpperCornerHz=10_000,MinimumInputVoltage=-.01,MaximumInputVoltage=.01,
     OpAmp=new(){UseIdealOpAmp=false,GainBandwidthHz=1_000,SlewRateVoltPerSecond=1_000_000}});
    Require(!slow.Success&&slow.Capability.ReasonCode==AnalogCapabilityReasonCode.GbBandwidthInsufficient,"Insufficient GBW was not classified.");
}

static void TestPrecisionAnalogSeriesMatrix()
{
    var operations=Enum.GetValues<AnalogComputeOperationType>();
    foreach(var series in new[]{AnalogStandardSeries.E24,AnalogStandardSeries.E96})
    foreach(var operation in operations)
    {
        var request=operation switch
        {
            AnalogComputeOperationType.GeneralLinearCombination=>PrecisionOperation(operation) with
                {Inputs=[new("U1",.7,-1,1),new("U2",-1.3,-1,1)],OutputOffsetVoltage=.2},
            AnalogComputeOperationType.VoltageRangeMapper=>PrecisionOperation(operation) with
                {InputLowVoltage=.5,InputHighVoltage=4.5,OutputLowVoltage=.2,OutputHighVoltage=3.1},
            AnalogComputeOperationType.PrecisionHalfWaveRectifier=>PrecisionOperation(operation) with {GainK=1.5},
            AnalogComputeOperationType.PrecisionFullWaveRectifier=>PrecisionOperation(operation) with {GainK=1.5},
            AnalogComputeOperationType.LogarithmicAmplifier=>PrecisionOperation(operation) with
                {MinimumInputVoltage=.1,MaximumInputVoltage=10,ReferenceVoltage=1,LogScaleVoltage=.1},
            AnalogComputeOperationType.ExponentialAmplifier=>PrecisionOperation(operation) with
                {MinimumInputVoltage=-.2,MaximumInputVoltage=.2,ReferenceVoltage=1,LogScaleVoltage=.1},
            AnalogComputeOperationType.PracticalIntegrator=>PrecisionOperation(operation) with
                {IntegrationConstantPerSecond=1_000,LowerCornerHz=10,UpperCornerHz=10_000},
            AnalogComputeOperationType.PracticalDifferentiator=>PrecisionOperation(operation) with
                {DifferentiationConstantSecond=.001,DifferentiationUsefulUpperFrequencyHz=100},
            _=>throw new InvalidOperationException()
        };
        request=request with
        {
            MaximumFunctionErrorPercent=series==AnalogStandardSeries.E24?8:2,
            Components=request.Components with{ResistorSeries=series,CapacitorSeries=series}
        };
        var result=new PrecisionAnalogDesignEngine().Design(request);
        Require(result.Success,$"{operation} {series}: {result.Capability.ReasonCode}: {result.Capability.Message}");
        Require(result.VerificationMetrics.Count>0&&result.VerificationMetrics.All(metric=>metric.Pass),
            $"{operation} {series} bypassed independent verification.");
        Require(result.Components.All(component=>component.Series is null||component.Series==series),
            $"{operation} {series} component provenance is inconsistent.");
    }
}

static void TestPrecisionAnalogInvalidSpecificationMatrix()
{
    var invalid=new AnalogComputeDesignRequest[]
    {
        PrecisionOperation(AnalogComputeOperationType.GeneralLinearCombination) with {SupplyVccVoltage=double.NaN},
        PrecisionOperation(AnalogComputeOperationType.GeneralLinearCombination) with {Inputs=[new("U1",1),new("u1",2)]},
        PrecisionOperation(AnalogComputeOperationType.GeneralLinearCombination) with {Inputs=[new("U1",1,-1,1,2)]},
        PrecisionOperation(AnalogComputeOperationType.VoltageRangeMapper) with {OutputLowVoltage=1,OutputHighVoltage=1},
        PrecisionOperation(AnalogComputeOperationType.PrecisionHalfWaveRectifier) with {GainK=double.PositiveInfinity},
        PrecisionOperation(AnalogComputeOperationType.PrecisionFullWaveRectifier) with {MinimumInputVoltage=1,MaximumInputVoltage=-1},
        PrecisionOperation(AnalogComputeOperationType.LogarithmicAmplifier) with {MinimumInputVoltage=0,MaximumInputVoltage=1},
        PrecisionOperation(AnalogComputeOperationType.ExponentialAmplifier) with {MinimumInputVoltage=-10,MaximumInputVoltage=10,LogScaleVoltage=.1},
        PrecisionOperation(AnalogComputeOperationType.PracticalIntegrator) with {IntegrationConstantPerSecond=0},
        PrecisionOperation(AnalogComputeOperationType.PracticalDifferentiator) with {DifferentiationConstantSecond=double.NaN}
    };
    foreach(var request in invalid)
    {
        var result=new PrecisionAnalogDesignEngine().Design(request);
        Require(!result.Success&&result.FailureStage==AnalogDesignFailureStage.SpecificationValidation&&
                result.Capability.ReasonCode is AnalogCapabilityReasonCode.InvalidInput or AnalogCapabilityReasonCode.SupplyRailsInvalid,
            $"{request.Operation} invalid specification was not classified before synthesis.");
    }
}

static void TestPrecisionAnalogConstrainedPracticalMatrix()
{
    foreach(var operation in Enum.GetValues<AnalogComputeOperationType>())
    {
        var request=PrecisionOperation(operation) with
        {
            Components=new AnalogComponentSelectionConstraints
            {
                ResistorSeries=AnalogStandardSeries.E24,CapacitorSeries=AnalogStandardSeries.E24,
                ResistorMinimumOhm=10_000,ResistorMaximumOhm=10_000,
                CapacitorMinimumFarad=1e-9,CapacitorMaximumFarad=1e-9,OptimizeStandardValues=true
            }
        };
        if(operation==AnalogComputeOperationType.LogarithmicAmplifier)
            request=request with{MinimumInputVoltage=.1,MaximumInputVoltage=1};
        if(operation==AnalogComputeOperationType.ExponentialAmplifier)
            request=request with{MinimumInputVoltage=-.1,MaximumInputVoltage=.1};
        var result=new PrecisionAnalogDesignEngine().Design(request);
        Require(result.Success&&result.VerificationMetrics.All(metric=>metric.Pass) ||
                !result.Success&&result.Capability.ReasonCode is AnalogCapabilityReasonCode.ComponentRangeImpossible or AnalogCapabilityReasonCode.StandardValueAccuracyInsufficient,
            $"{operation} narrow-range outcome is neither verified nor a precise range/standard-value failure: {result.Capability.ReasonCode}.");
    }

    foreach(var operation in new[]{AnalogComputeOperationType.PrecisionHalfWaveRectifier,
                AnalogComputeOperationType.PracticalIntegrator,AnalogComputeOperationType.PracticalDifferentiator})
    {
        var request=PrecisionOperation(operation) with
        {
            MinimumInputVoltage=-.01,MaximumInputVoltage=.01,
            OpAmp=new(){UseIdealOpAmp=false,GainBandwidthHz=1,SlewRateVoltPerSecond=1}
        };
        if(operation==AnalogComputeOperationType.PracticalIntegrator)
            request=request with{IntegrationConstantPerSecond=1_000,LowerCornerHz=10,UpperCornerHz=10_000};
        if(operation==AnalogComputeOperationType.PracticalDifferentiator)
            request=request with{DifferentiationConstantSecond=.001,DifferentiationUsefulUpperFrequencyHz=100};
        var result=new PrecisionAnalogDesignEngine().Design(request);
        Require(!result.Success&&result.FailureStage==AnalogDesignFailureStage.IndependentVerification&&
                result.Capability.ReasonCode is AnalogCapabilityReasonCode.GbBandwidthInsufficient or AnalogCapabilityReasonCode.SlewRateInsufficient,
            $"{operation} practical op-amp pressure was not classified by independent verification.");
    }
}

static void TestPrecisionAnalogPerformanceAndRender()
{
    var requests=Enum.GetValues<AnalogComputeOperationType>().Select(operation=>operation switch
    {
        AnalogComputeOperationType.LogarithmicAmplifier=>PrecisionOperation(operation) with {MinimumInputVoltage=.1,MaximumInputVoltage=10},
        AnalogComputeOperationType.ExponentialAmplifier=>PrecisionOperation(operation) with {MinimumInputVoltage=-.2,MaximumInputVoltage=.2},
        AnalogComputeOperationType.PracticalIntegrator=>PrecisionOperation(operation) with {IntegrationConstantPerSecond=1_000,LowerCornerHz=10,UpperCornerHz=10_000},
        AnalogComputeOperationType.PracticalDifferentiator=>PrecisionOperation(operation) with {DifferentiationConstantSecond=.001,DifferentiationUsefulUpperFrequencyHz=100},
        _=>PrecisionOperation(operation)
    }).ToArray();
    var engine=new PrecisionAnalogDesignEngine();
    var coreWatch=Stopwatch.StartNew();
    var results=new List<AnalogComputeDesignResult>();
    for(var repeat=0;repeat<5;repeat++)
    foreach(var request in requests)
    {
        var result=engine.Design(request);Require(result.Success,$"Performance fixture {request.Operation}: {result.Capability.Message}");
        if(repeat==4)results.Add(result);
    }
    coreWatch.Stop();

    TimeSpan buildRequestElapsed=TimeSpan.Zero,renderElapsed=TimeSpan.Zero;Exception? failure=null;
    var thread=new Thread(() =>
    {
        try
        {
            using var wizard=new PrecisionAnalogWizardControl();wizard.CreateControl();
            var build=typeof(PrecisionAnalogWizardControl).GetMethod("BuildRequest",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var show=typeof(PrecisionAnalogWizardControl).GetMethod("ShowResult",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            var watch=Stopwatch.StartNew();for(var index=0;index<100;index++)_ = build.Invoke(wizard,null);watch.Stop();buildRequestElapsed=watch.Elapsed;
            watch.Restart();foreach(var result in results)for(var index=0;index<20;index++){show.Invoke(wizard,[result]);wizard.PerformLayout();}watch.Stop();renderElapsed=watch.Elapsed;
        }
        catch(Exception exception){failure=exception;}
    });
    thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
    if(failure is not null)throw new InvalidOperationException("Precision performance/render fixture failed.",failure);
    Console.WriteLine($"[PRECISION PERF] Core40={coreWatch.Elapsed.TotalMilliseconds:F1} ms; BuildRequest100={buildRequestElapsed.TotalMilliseconds:F1} ms; Render160={renderElapsed.TotalMilliseconds:F1} ms");
    Require(coreWatch.Elapsed<TimeSpan.FromSeconds(10)&&renderElapsed<TimeSpan.FromSeconds(10),"Precision Analog performance smoke exceeded the 10 s safety budget.");
}

static void TestChebyshevLadderPrototype()
{
    var values = new ChebyshevPrototypeDesigner().Design(3, 1).LadderGValues;
    Equal(5, values.Count); Require(Nearly(values[1], 2.0236, 5e-4), "Unexpected 1 dB Chebyshev g1."); Require(Nearly(values[2], 0.9941, 5e-4), "Unexpected 1 dB Chebyshev g2."); Require(Nearly(values[3], 2.0236, 5e-4), "Unexpected 1 dB Chebyshev g3."); Require(Nearly(values[4], 1, 1e-12), "Odd-order Chebyshev termination must be unity.");
}

static void TestEngineeringUnitConversionAndRanges()
{
    var kilohertz = EngineeringUnits.Frequency.Single(unit => unit.Symbol == "kHz");
    var microfarad = EngineeringUnits.Capacitance.Single(unit => unit.Symbol == "µF");
    var millihenry = EngineeringUnits.Inductance.Single(unit => unit.Symbol == "mH");
    Require(Nearly(kilohertz.ToBase(2.5), 2_500, 1e-12), "kHz input did not convert to Hz.");
    Require(Nearly(microfarad.ToBase(0.47), 470e-9, 1e-12), "µF input did not convert to farads.");
    Require(Nearly(millihenry.FromBase(0.015), 15, 1e-12), "Henry value did not convert to mH.");
    Require(EngineeringUnits.BestFor(1_000, EngineeringUnits.Frequency).Symbol == "kHz", "Frequency did not choose a compact unit.");

    var activeState = new FilterDesignState
    {
        Mode = FilterMode.Active,
        PreferredCapacitorMinF = 220e-12,
        PreferredCapacitorMaxF = 470e-9
    };
    var activeRequest = FilterDesignRequest.FromState(activeState);
    Require(Nearly(activeRequest.ComponentConstraints.CapacitorMinimumF, 220e-12, 1e-12), "Active editable capacitor minimum was not mapped to the request.");
    Require(Nearly(activeRequest.ComponentConstraints.CapacitorMaximumF, 470e-9, 1e-12), "Active editable capacitor maximum was not mapped to the request.");

    var lcState = new FilterDesignState
    {
        Mode = FilterMode.Passive,
        Implementation = FilterImplementationType.Lc,
        PassiveLcCapacitorMinimumF = 10e-12,
        PassiveLcCapacitorMaximumF = 22e-6,
        InductorMinimumH = 1e-6,
        InductorMaximumH = 100e-3
    };
    var lcRequest = FilterDesignRequest.FromState(lcState);
    Require(Nearly(lcRequest.ComponentConstraints.CapacitorMinimumF, 10e-12, 1e-12), "LC editable capacitor minimum was not mapped to the request.");
    Require(Nearly(lcRequest.ComponentConstraints.CapacitorMaximumF, 22e-6, 1e-12), "LC editable capacitor maximum was not mapped to the request.");
    Require(Nearly(lcRequest.ComponentConstraints.InductorMinimumH, 1e-6, 1e-12), "LC editable inductor minimum was not mapped to the request.");
    Require(Nearly(lcRequest.ComponentConstraints.InductorMaximumH, 100e-3, 1e-12), "LC editable inductor maximum was not mapped to the request.");
}

static void TestSallenKeyGainFeedbackPinExit()
{
    var request = Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Butterworth,
        FilterImplementationType.SallenKey, 2_000, 8_000, manualOrder: 2) with { PassbandGain = 2 };
    request.ComponentConstraints.ResistorMinimumOhm = 1_000;
    request.ComponentConstraints.ResistorMaximumOhm = 100_000;
    request.ComponentConstraints.CapacitorMinimumF = 100e-12;
    request.ComponentConstraints.CapacitorMaximumF = 1e-6;
    request.ComponentConstraints.OptimizeStandardValues = true;

    var result = new FilterDesignEngine().Design(request);
    Require(result.IsImplemented, result.Message);
    var document = result.SchematicDocument!;
    var feedback = document.PlacedComponents.Single(item => item.Component.Reference == "R3");
    var leftPin = SchematicGeometry.GetWorldPin(feedback, "1");
    var attachedSegments = document.Wires
        .SelectMany(wire => wire.Points.Zip(wire.Points.Skip(1), (first, second) => (first, second)))
        .Where(segment => NearlyPoint(segment.first, leftPin) || NearlyPoint(segment.second, leftPin))
        .ToArray();
    Require(attachedSegments.Length > 0, "R3 left pin has no visible wire segment.");
    Require(attachedSegments.Any(segment =>
            (NearlyPoint(segment.first, leftPin) ? segment.second : segment.first).X < leftPin.X - .01),
        "R3 left-pin wire must leave to the left instead of doubling back through the resistor symbol.");
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

static void TestApproximationReferenceSamples()
{
    var references = new[]
    {
        (FilterApproximation.GaussianTo6Db, .658059, 3.010293, 12.035113),
        (FilterApproximation.GaussianTo12Db, .654503, 3.010061, 14.149857),
        (FilterApproximation.LinearPhase005Degree, .711073, 3.010449, 14.778214),
        (FilterApproximation.LinearPhase05Degree, .576273, 3.009940, 15.101010)
    };
    foreach (var (approximation, atHalf, atOne, atTwo) in references)
    {
        var prototype = new TabulatedPrototypeDesigner(approximation).Design(4, 1);
        Require(prototype.Poles.All(pole => pole.Real < 0), $"{approximation} has an unstable published pole.");
        Require(Nearly(PrototypeResponseEvaluator.AttenuationDb(prototype, .5), atHalf, 2e-6), $"{approximation} response mismatch at normalized 0.5 rad/s.");
        Require(Nearly(PrototypeResponseEvaluator.AttenuationDb(prototype, 1), atOne, 2e-6), $"{approximation} is not normalized to the published -3 dB frequency.");
        Require(Nearly(PrototypeResponseEvaluator.AttenuationDb(prototype, 2), atTwo, 2e-6), $"{approximation} response mismatch at normalized 2 rad/s.");
        Require(Nearly(Complex.Abs(PrototypeResponseEvaluator.Evaluate(prototype, 0)), 1, 1e-10), $"{approximation} DC gain is not unity.");
    }
}

static void TestBesselGroupDelayFlatness()
{
    foreach (var order in new[] { 2, 3, 4, 5 })
    {
        var prototype = new BesselPrototypeDesigner().Design(order, 1);
        Require(prototype.Poles.Count == order && prototype.Poles.All(pole => pole.Real < 0), $"Bessel order {order} is not stable.");
        Require(Nearly(PrototypeResponseEvaluator.AttenuationDb(prototype, 1), 3.0102999566, 3e-6), $"Bessel order {order} is not -3 dB normalized.");
        var dc = PrototypeResponseEvaluator.GroupDelay(prototype, 0);
        var low = PrototypeResponseEvaluator.GroupDelay(prototype, .02);
        Require(Math.Abs(low - dc) / dc < 1e-5, $"Bessel order {order} does not show maximally-flat low-frequency group delay.");
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

static void TestCircuitGraphSectionTransferVerification()
{
    foreach (var (response, topology, expectedPolarity) in new[]
    {
        (FilterResponseType.LowPass, FilterImplementationType.SallenKey, 1),
        (FilterResponseType.HighPass, FilterImplementationType.SallenKey, 1),
        (FilterResponseType.LowPass, FilterImplementationType.MultipleFeedback, -1),
        (FilterResponseType.HighPass, FilterImplementationType.MultipleFeedback, -1),
        (FilterResponseType.BandPass, FilterImplementationType.MultipleFeedback, -1)
    })
    {
        var request = response switch
        {
            FilterResponseType.HighPass => Request(FilterMode.Active, response, FilterApproximation.ChebyshevTypeI, topology, 2_000, 500),
            FilterResponseType.BandPass => Request(FilterMode.Active, response, FilterApproximation.Butterworth, topology, 1_000, 2_000, manualOrder: 2),
            _ => Request(FilterMode.Active, response, FilterApproximation.Butterworth, topology, 1_000, 4_000)
        };
        var result = new FilterDesignEngine().Design(WideRanges(request));
        Require(result.IsImplemented, $"{topology}/{response}: {result.Message}");
        var section = result.Sections.First(value => value.Target.Order == SectionOrder.Second);
        var graph = new FilterCircuitAssembler().BuildActiveSection(request, section);
        var target = SectionTransferTarget.FromSection(section.Target, expectedPolarity);
        var measurement = new CircuitSectionMnaVerifier().Verify(graph, target,
            new SectionTransferVerificationOptions(2e-4, 3e-4, OpAmpOpenLoopGain: 1e7));
        Require(measurement.SampleCount >= 20, "Independent section verifier used too few AC samples.");
        Require(measurement.IsValid, $"{topology}/{response}: {string.Join(", ", measurement.Errors)}");
    }
}

static void TestSallenKeyBandPassVerification()
{
    const double q=1;const double f=1_000;const double r1=10_000;var w=2*Math.PI*f;var c1=2/(w*r1);var inner=(6.5-1/q)/3;var r4=10_000d;var r5=r4/(inner-1);var natural=inner*q;var ratio=1/natural;var rb=10_000d;var ra=rb*(1-ratio)/ratio;
    var actual=SallenKeyBandPassSectionCalculator.Verify(r1,r1/3,2*r1,c1,c1/2,r4,r5,ra,rb);
    Require(Nearly(actual.FrequencyHz,f,1e-9)&&Nearly(actual.Q,q,1e-9)&&Nearly(actual.GainMagnitude,1,1e-9),"Sallen-Key BP inverse equations mismatch.");
}

static void TestSallenKeyBandPassCircuitGraphAudit()
{
    const double frequency = 1_000;
    const double q = 1;
    var omega = 2 * Math.PI * frequency;
    var target = new AnalogFilterSection(1, SectionOrder.Second, FilterResponseType.BandPass,
        frequency, q, 1, [0, omega / q, 0], [1, omega / q, omega * omega]);
    foreach (var series in new StandardComponentSeries?[] { null, StandardComponentSeries.E24, StandardComponentSeries.E96 })
    {
        var request = WideRanges(Request(FilterMode.Active, FilterResponseType.BandPass,
            FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 1_000, 2_000,
            optimize: series.HasValue, manualOrder: 1));
        if (series.HasValue)
        {
            request.ComponentConstraints.ResistorSeries = series.Value;
            request.ComponentConstraints.CapacitorSeries = series.Value;
        }
        var synthesized = new SallenKeyBandPassSectionCalculator().Calculate(target, request);
        var graph = new FilterCircuitAssembler().BuildActiveSection(request, synthesized);
        var measurement = new CircuitSectionMnaVerifier().Verify(graph, SectionTransferTarget.FromSection(target),
            series.HasValue ? new SectionTransferVerificationOptions(.13, .20) : new SectionTransferVerificationOptions(3e-4, 5e-4));
        Require(measurement.IsValid, $"{series?.ToString() ?? "theoretical"}: {string.Join(", ", measurement.Errors)}");
        if (series.HasValue)
            Require(synthesized.Components.All(value => StandardValueSeries.IsSeriesValue(value.SelectedValue, series.Value, 5e-3)),
                $"Sallen-Key BP selected a component outside {series}.");
    }
}

static void TestBainterCircuitGraphTransferAudit()
{
    const double poleFrequency = 900;
    const double zeroFrequency = 1_000;
    const double q = 2;
    var wp = 2 * Math.PI * poleFrequency;
    var wz = 2 * Math.PI * zeroFrequency;
    var section = new AnalogFilterSection(1, SectionOrder.Second, FilterResponseType.BandStop,
        poleFrequency, q, 1, [1, 0, wz * wz], [1, wp / q, wp * wp]);
    var request = WideRanges(Request(FilterMode.Active, FilterResponseType.BandStop,
        FilterApproximation.Butterworth, FilterImplementationType.BainterNotch, 1_000, 2_000, manualOrder: 1));
    var synthesized = new BainterNotchSectionCalculator().Calculate(section, request);
    var graph = new FilterCircuitAssembler().BuildActiveSection(request, synthesized);
    var measurement = new CircuitSectionMnaVerifier().Verify(graph, SectionTransferTarget.FromSection(section),
        new SectionTransferVerificationOptions(5e-4, 8e-4));
    Require(measurement.IsValid, string.Join(", ", measurement.Errors));
}

static void TestBainterStandardValueNotchDepth()
{
    const double poleFrequency = 900;
    const double zeroFrequency = 1_000;
    const double q = 2;
    var wp = 2 * Math.PI * poleFrequency;
    var wz = 2 * Math.PI * zeroFrequency;
    var section = new AnalogFilterSection(1, SectionOrder.Second, FilterResponseType.BandStop,
        poleFrequency, q, 1, [1, 0, wz * wz], [1, wp / q, wp * wp]);
    foreach (var (series, attenuation) in new[]
    {
        (StandardComponentSeries.E12, 15d),
        (StandardComponentSeries.E24, 20d),
        (StandardComponentSeries.E48, 28d),
        (StandardComponentSeries.E96, 35d)
    })
    {
        var request = WideRanges(Request(FilterMode.Active, FilterResponseType.BandStop,
            FilterApproximation.Butterworth, FilterImplementationType.BainterNotch, 1_000, 2_000,
            optimize: true, manualOrder: 1) with { StopbandAttenuationDb = attenuation });
        request.ComponentConstraints.ResistorSeries = series;
        request.ComponentConstraints.CapacitorSeries = series;
        var synthesized = new BainterNotchSectionCalculator().Calculate(section, request);
        var graph = new FilterCircuitAssembler().BuildActiveSection(request, synthesized);
        var measurement = new CircuitSectionMnaVerifier().Verify(graph, SectionTransferTarget.FromSection(section),
            new SectionTransferVerificationOptions(.08, .12));
        Require(measurement.NotchAttenuationDb >= attenuation - .1,
            $"{series} Bainter notch depth is only {measurement.NotchAttenuationDb:0.###} dB.");
    }

    var impossible = WideRanges(Request(FilterMode.Active, FilterResponseType.BandStop,
        FilterApproximation.Butterworth, FilterImplementationType.BainterNotch, 1_000, 2_000,
        optimize: true, manualOrder: 1) with { StopbandAttenuationDb = 120 });
    impossible.ComponentConstraints.ResistorSeries = StandardComponentSeries.E12;
    impossible.ComponentConstraints.CapacitorSeries = StandardComponentSeries.E12;
    var failedHonestly = false;
    try
    {
        var synthesized = new BainterNotchSectionCalculator().Calculate(section, impossible);
        var graph = new FilterCircuitAssembler().BuildActiveSection(impossible, synthesized);
        var measurement = new CircuitSectionMnaVerifier().Verify(graph, SectionTransferTarget.FromSection(section),
            new SectionTransferVerificationOptions(.08, .12));
        failedHonestly = measurement.NotchAttenuationDb >= impossible.StopbandAttenuationDb - .1;
    }
    catch (InvalidOperationException exception)
    {
        failedHonestly = exception.Message.Contains("BainterNotchDepthInsufficientWithSelectedSeries", StringComparison.Ordinal);
    }
    Require(failedHonestly, "An E12 notch must either meet the requested nominal depth in MNA or return the structured notch-depth failure.");
    var capabilityResult = new FilterDesignEngine().Design(impossible);
    Require(!capabilityResult.IsImplemented && capabilityResult.Capability?.ReasonCode ==
            FilterSupportReasonCode.BainterNotchDepthInsufficientWithSelectedSeries,
        "Bainter notch-depth failure was not propagated through the unified capability result.");
}

static void TestBainterMultiSectionGain()
{
    foreach (var order in new[] { 2, 3 })
    foreach (var gain in new[] { .5, 1d, 2d })
    {
        var request = WideRanges(Request(FilterMode.Active, FilterResponseType.BandStop,
            FilterApproximation.Butterworth, FilterImplementationType.BainterNotch, 1_000, 2_000,
            manualOrder: order) with { PassbandGain = gain, StopbandAttenuationDb = 3.1 });
        var result = new FilterDesignEngine().Design(request);
        Require(result.IsImplemented, $"order {order}, A0={gain}: {result.Message}");
        Equal(order * 2, result.RealizedOrder);
        Require(result.Verification?.IsValid == true && result.SpecificationCompliance?.MeetsAllEdges == true,
            $"order {order}, A0={gain} did not pass whole-circuit verification.");
        var zeroFrequencies = result.Sections.Select(section =>
            Math.Sqrt(section.Target.NumeratorCoefficients[^1] / section.Target.NumeratorCoefficients[0]) / (2 * Math.PI)).ToArray();
        Require(zeroFrequencies.All(value => Nearly(value, zeroFrequencies[0], 1e-10)),
            "Bainter transformed sections lost the shared notch zero.");
        Require(result.Sections.Select(section => section.Target.PoleFrequencyHz).DistinctBy(value => Math.Round(value, 6)).Count() == order,
            "Each transformed Bainter section must retain its own pole frequency/Q data.");
    }
}

static void TestSingleEndedActiveV1Freeze()
{
    Require(FilterV1Status.SingleEndedActiveComplete, "Single-Ended Active v1 completion marker is not set.");
    var cases = new[]
    {
        WideRanges(Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 1_000, 4_000, manualOrder: 4)),
        WideRanges(Request(FilterMode.Active, FilterResponseType.HighPass, FilterApproximation.ChebyshevTypeI, FilterImplementationType.MultipleFeedback, 4_000, 1_000, manualOrder: 5) with { PassbandGain = 2 }),
        WideRanges(Request(FilterMode.Active, FilterResponseType.BandPass, FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 1_000, 2_000, manualOrder: 2)),
        WideRanges(Request(FilterMode.Active, FilterResponseType.BandPass, FilterApproximation.ChebyshevTypeI, FilterImplementationType.MultipleFeedback, 1_000, 2_000, manualOrder: 2) with { PassbandGain = .5 }),
        WideRanges(Request(FilterMode.Active, FilterResponseType.BandStop, FilterApproximation.Butterworth, FilterImplementationType.BainterNotch, 1_000, 2_000, manualOrder: 2) with { PassbandGain = .5, StopbandAttenuationDb = 3.1 }),
        WideRanges(Request(FilterMode.Active, FilterResponseType.BandStop, FilterApproximation.ChebyshevTypeI, FilterImplementationType.BainterNotch, 1_000, 2_000, manualOrder: 3) with { PassbandGain = 2, StopbandAttenuationDb = 3.1 })
    };
    foreach (var request in cases)
    {
        var result = new FilterDesignEngine().Design(request);
        Require(result.IsImplemented && result.Verification?.IsValid == true, $"freeze {request.Approximation}/{request.ResponseType}/{request.Implementation}: {result.Message}");
        Require(result.CircuitGraph is { Components.Count: > 0 } && result.AnalysisResult?.Points.Count >= 20,
            "Freeze benchmark did not reach selected components and whole-circuit MNA.");
        var report = new FilterDesignReportBuilder().Build(result);
        Require(report.Sections.Count == result.Sections.Count && report.Verification.Count > 0,
            "Freeze benchmark did not reach the Filter Design Report.");
    }
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

static void TestOfficialHighDensityESeriesValues()
{
    Require(StandardValueSeries.IsSeriesValue(1_050, StandardComponentSeries.E48), "1.05 kΩ must be an official E48 value.");
    Require(StandardValueSeries.IsSeriesValue(9_310, StandardComponentSeries.E96), "9.31 kΩ must be an official E96 value.");
    Require(StandardValueSeries.IsSeriesValue(49_900, StandardComponentSeries.E96), "49.9 kΩ must be an official E96 value.");
    Require(!StandardValueSeries.IsSeriesValue(9_306, StandardComponentSeries.E96), "9.306 kΩ is a formula artifact, not the 3-significant-digit E96 nominal value.");
}

static void TestManualUnderOrderCapability()
{
    var request = Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Butterworth,
        FilterImplementationType.SallenKey, 2_000, 8_000, optimize: true, manualOrder: 2) with
    {
        StopbandAttenuationDb = 30,
        PassbandGain = 2
    };
    request.ComponentConstraints.ResistorMinimumOhm = 1_000;
    request.ComponentConstraints.ResistorMaximumOhm = 100_000;
    request.ComponentConstraints.CapacitorMinimumF = 100e-12;
    request.ComponentConstraints.CapacitorMaximumF = 1e-6;

    var result = new FilterDesignEngine().Design(request);
    Require(result.IsImplemented, "A manually requested under-order circuit should remain inspectable.");
    Require(result.RequiredOrder == 3 && result.PrototypeOrder == 2, "Manual-order audit did not reproduce test point 2.");
    Require(!result.MeetsRequestedOrder && !result.MeetsSpecifications, "Under-order design incorrectly claims specification compliance.");
    Require(result.Capability is { IsSupported: false, ReasonCode: FilterSupportReasonCode.SpecifiedOrderInsufficient },
        "Under-order design must expose SpecifiedOrderInsufficient rather than a selected-value failure.");
    Require(!result.IsFullySupported, "Under-order circuit must not be advertised as fully supported.");
    Require(result.SpecificationCompliance!.Edges.Single(edge => edge.Label == "fs").MeetsLimit == false,
        "Test point 2 unexpectedly meets its requested fs attenuation.");
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
    var result = new FilterDesignEngine().Design(WideRanges(Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Bessel, FilterImplementationType.SallenKey, 1_000, 10_000, manualOrder:4)));
    Require(result.IsImplemented, result.Message); Equal(4, result.PrototypeOrder);
    Require(result.PrototypeNormalization?.Kind == PrototypeNormalizationKind.Minus3DbMagnitude, "Bessel normalization metadata is missing.");
    Require(result.Verification?.IsValid == true && result.CircuitGraph!.Components.Count > 0, "Bessel final CircuitGraph was not verified.");
}

static void TestSallenKeyBandPassEndToEnd()
{
    var request=WideRanges(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,1_000,2_000,manualOrder:2));
    var result=new FilterDesignEngine().Design(request);
    Require(result.IsImplemented, result.Message);
    Equal(4, result.RealizedOrder); Equal(2, result.Sections.Count);
    Require(result.Sections.All(section => section.TopologyGain != null && section.TopologyGain.RequiredOutputOrInterstageScale is > 0 and <= 1),
        "Sallen-Key BP did not expose its Q-coupled intrinsic gain and output scale.");
    Require(result.Verification?.IsValid == true, "Sallen-Key BP did not pass final independent verification.");
}

static void TestTabulatedResponsesEndToEnd()
{
    foreach (var approximation in new[] { FilterApproximation.GaussianTo6Db, FilterApproximation.GaussianTo12Db, FilterApproximation.LinearPhase005Degree, FilterApproximation.LinearPhase05Degree })
    {
        var result = new FilterDesignEngine().Design(WideRanges(Request(FilterMode.Active, FilterResponseType.LowPass, approximation, FilterImplementationType.MultipleFeedback, 1_000, 10_000, manualOrder: 4)));
        Require(result.IsImplemented, $"{approximation}: {result.Message}"); Equal(4, result.PrototypeOrder);
        Require(result.PrototypeNormalization?.ReferenceSource.Contains("AN-649", StringComparison.Ordinal) == true, $"{approximation} source metadata is missing.");
        Require(result.Verification?.IsValid == true, $"{approximation} CircuitGraph verification failed.");
    }
}

static void TestNumericalOrderApproximationsEndToEnd()
{
    foreach (var approximation in new[]
    {
        FilterApproximation.Bessel, FilterApproximation.GaussianTo6Db,
        FilterApproximation.GaussianTo12Db, FilterApproximation.LinearPhase005Degree,
        FilterApproximation.LinearPhase05Degree
    })
    {
        var request = WideRanges(Request(FilterMode.Active, FilterResponseType.LowPass, approximation,
            FilterImplementationType.MultipleFeedback, 1_000, 10_000) with { StopbandAttenuationDb = 30 });
        var result = new FilterDesignEngine().Design(request);
        Require(result.IsImplemented, $"{approximation} numerical-order design failed: {result.Message}");
        Require(result.MeetsRequestedOrder && result.Verification?.IsValid == true, $"{approximation} numerical-order design was not verified.");
        Require(result.SpecificationCompliance?.Edges.All(edge => edge.MeetsLimit) == true, $"{approximation} selected order does not meet the requested edge specifications.");
    }
}

static void TestSupportMatrix()
{
    var unsupportedBs = FilterSupportMatrix.Evaluate(Request(FilterMode.Active, FilterResponseType.BandStop,
        FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 1_000, 2_000, manualOrder: 2));
    Require(!unsupportedBs.IsSupported && unsupportedBs.ReasonCode == FilterSupportReasonCode.ResponseUnavailable,
        "Sallen-Key Band-Stop must expose a structured response-unavailable reason.");
    foreach(var approximation in Enum.GetValues<FilterApproximation>())
    foreach(var response in Enum.GetValues<FilterResponseType>())
    {
        foreach(var topology in new[]{FilterImplementationType.SallenKey,FilterImplementationType.MultipleFeedback,FilterImplementationType.BainterNotch})
        {var request=Request(FilterMode.Active,response,approximation,topology,1_000,2_000,manualOrder:3);var expected=topology==FilterImplementationType.SallenKey?response is FilterResponseType.LowPass or FilterResponseType.HighPass or FilterResponseType.BandPass:topology==FilterImplementationType.MultipleFeedback?response is FilterResponseType.LowPass or FilterResponseType.HighPass or FilterResponseType.BandPass:response==FilterResponseType.BandStop;Require(FilterSupportMatrix.Evaluate(request).IsSupported==expected,$"Unexpected active support decision for {approximation}/{response}/{topology}.");}
        foreach(var topology in new[]{FilterImplementationType.Rc,FilterImplementationType.Lc})
        {var request=Request(FilterMode.Passive,response,approximation,topology,1_000,2_000,manualOrder:1);var lc=approximation is FilterApproximation.Butterworth or FilterApproximation.ChebyshevTypeI or FilterApproximation.Bessel;var expected=topology==FilterImplementationType.Rc||lc;Require(FilterSupportMatrix.Evaluate(request).IsSupported==expected,$"Unexpected passive support decision for {approximation}/{response}/{topology}.");}
    }
}

static void TestActiveSupportedMatrixEndToEnd()
{
    foreach(var approximation in Enum.GetValues<FilterApproximation>())
    foreach(var response in Enum.GetValues<FilterResponseType>())
    foreach(var topology in new[]{FilterImplementationType.SallenKey,FilterImplementationType.MultipleFeedback,FilterImplementationType.BainterNotch})
    {
        var fp=response==FilterResponseType.HighPass?10_000:1_000;var fs=response==FilterResponseType.HighPass?1_000:10_000;
        var request=Request(FilterMode.Active,response,approximation,topology,fp,fs,manualOrder:3) with { StopbandAttenuationDb=20 };
        if(!new FilterDesignEngine().GetAvailability(request).CanDesign)continue;
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
    var epsilon = Math.Sqrt(Math.Pow(10, result.DesignRequest!.PassbandRippleDb / 10) - 1);
    var expectedCutoffHz = result.DesignRequest.PassbandEdgeHz * Math.Pow(epsilon, 1d / result.PrototypeOrder);
    Require(Nearly(result.NormalizedSpecification!.CutoffAngularFrequency / (2 * Math.PI), expectedCutoffHz, 1e-10),
        "High-pass Butterworth cutoff scaling must place fc below fp.");
    var atPassbandEdge = result.AnalysisResult!.Points.Single(point => Nearly(point.FrequencyHz, result.DesignRequest.PassbandEdgeHz, 1e-12));
    Require(Math.Abs(atPassbandEdge.MagnitudeDb + result.DesignRequest.PassbandRippleDb) < .01,
        $"Passive LC high-pass insertion gain at fp is {atPassbandEdge.MagnitudeDb:G6} dB instead of -Rp.");
}

static void TestPassiveLcBandPassEndToEnd()
{
    var result = new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.BandPass, FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 2_000, manualOrder: 3));
    Require(result.IsImplemented, result.Message); Equal(3, result.PrototypeOrder); Equal(6, result.RealizedOrder); Equal(6, result.CircuitGraph!.Components.Count);
    Require(result.CircuitGraph.GetNet("VIN").Pins.Count == 1, "BP ladder input must start with one series resonator branch.");
}

static void TestPassiveLcBandEdgeAttenuation()
{
    var request = Request(FilterMode.Passive, FilterResponseType.BandPass, FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 2_000);
    var result = new FilterDesignEngine().Design(request);
    Require(result.IsImplemented, result.Message);
    var analysis = result.AnalysisResult ?? throw new InvalidOperationException("Passive LC band-pass analysis is missing.");
    foreach (var frequency in new[] { request.PassbandLowerHz, request.PassbandUpperHz })
    {
        var point = analysis.Points.Single(value => Nearly(value.FrequencyHz, frequency, 1e-12));
        Require(Math.Abs(point.MagnitudeDb + request.PassbandRippleDb) < .02,
            $"Passive LC band-pass gain at fp={frequency:G6} Hz is {point.MagnitudeDb:G6} dB instead of -Rp.");
    }
    foreach (var frequency in new[] { request.StopbandLowerHz, request.StopbandUpperHz })
    {
        var point = analysis.Points.Single(value => Nearly(value.FrequencyHz, frequency, 1e-12));
        Require(point.MagnitudeDb <= -request.StopbandAttenuationDb + .02,
            $"Passive LC band-pass attenuation at fs={frequency:G6} Hz is only {-point.MagnitudeDb:G6} dB.");
    }
}

static void TestSupportedFilterEdgeSpecAudit()
{
    var failures = new List<string>();
    var engine = new FilterDesignEngine();
    foreach (var approximation in Enum.GetValues<FilterApproximation>())
    foreach (var response in Enum.GetValues<FilterResponseType>())
    foreach (var topology in new[] { FilterImplementationType.SallenKey, FilterImplementationType.MultipleFeedback, FilterImplementationType.BainterNotch })
    {
        var fp = response == FilterResponseType.HighPass ? 10_000 : 1_000;
        var fs = response == FilterResponseType.HighPass ? 1_000 : 10_000;
        var request = WideRanges(Request(FilterMode.Active, response, approximation, topology, fp, fs, manualOrder: 3) with
        {
            StopbandAttenuationDb = 20,
            PassbandGain = 1
        });
        if (!FilterSupportMatrix.Evaluate(request).IsSupported) continue;
        AuditEdgeSpecification(engine.Design(request), failures, $"Active/{approximation}/{response}/{topology}", true);
    }

    foreach (var approximation in new[] { FilterApproximation.Butterworth, FilterApproximation.ChebyshevTypeI })
    foreach (var response in Enum.GetValues<FilterResponseType>())
    {
        var fp = response == FilterResponseType.HighPass ? 10_000 : 1_000;
        var fs = response == FilterResponseType.HighPass ? 1_000 : 10_000;
        var request = Request(FilterMode.Passive, response, approximation, FilterImplementationType.Lc, fp, fs, manualOrder: 3) with
        {
            StopbandAttenuationDb = 20
        };
        AuditEdgeSpecification(engine.Design(request), failures, $"Passive/{approximation}/{response}/LC", true);
    }
    foreach (var approximation in new[] { FilterApproximation.Butterworth, FilterApproximation.ChebyshevTypeI })
    foreach (var response in new[] { FilterResponseType.LowPass, FilterResponseType.HighPass })
    {
        var fp = response == FilterResponseType.HighPass ? 10_000 : 1_000;
        var fs = response == FilterResponseType.HighPass ? 1_000 : 10_000;
        var request = Request(FilterMode.Passive, response, approximation, FilterImplementationType.Rc, fp, fs, manualOrder: 1) with
        {
            StopbandAttenuationDb = 20
        };
        AuditEdgeSpecification(engine.Design(request), failures, $"Passive/{approximation}/{response}/RC", false);
    }
    Require(failures.Count == 0, "Edge specification audit failed:\n  " + string.Join("\n  ", failures));
}

static void TestAutomaticOrderActiveEdgeSpecAudit()
{
    var failures = new List<string>();
    var engine = new FilterDesignEngine();
    foreach (var approximation in new[] { FilterApproximation.Butterworth, FilterApproximation.ChebyshevTypeI })
    foreach (var response in Enum.GetValues<FilterResponseType>())
    foreach (var topology in new[] { FilterImplementationType.SallenKey, FilterImplementationType.MultipleFeedback, FilterImplementationType.BainterNotch })
    {
        var fp = response == FilterResponseType.HighPass ? 10_000 : 1_000;
        var fs = response == FilterResponseType.HighPass ? 1_000 : 10_000;
        var request = WideRanges(Request(FilterMode.Active, response, approximation, topology, fp, fs));
        if (!FilterSupportMatrix.Evaluate(request).IsSupported) continue;
        var result = engine.Design(request);
        AuditEdgeSpecification(result, failures, $"Auto/Active/{approximation}/{response}/{topology}", true);
        if (result.IsImplemented && !result.MeetsSpecifications)
            failures.Add($"Auto/Active/{approximation}/{response}/{topology}: engine compliance flag is false");
    }
    Require(failures.Count == 0, "Automatic-order active audit failed:\n  " + string.Join("\n  ", failures));
}

static void TestEvenOrderChebyshevPassiveLcEdgeAudit()
{
    var failures = new List<string>();
    var engine = new FilterDesignEngine();
    var loadRatio = new ChebyshevPrototypeDesigner().Design(2, 1).LadderGValues[^1];
    foreach (var response in Enum.GetValues<FilterResponseType>())
    {
        var fp = response == FilterResponseType.HighPass ? 10_000 : 1_000;
        var fs = response == FilterResponseType.HighPass ? 1_000 : 10_000;
        var request = Request(FilterMode.Passive, response, FilterApproximation.ChebyshevTypeI,
            FilterImplementationType.Lc, fp, fs, manualOrder: 2) with
        {
            StopbandAttenuationDb = 20,
            LoadResistanceOhm = 50 * loadRatio
        };
        AuditEdgeSpecification(engine.Design(request), failures, $"Passive/Chebyshev-even/{response}/LC", false);
    }
    Require(failures.Count == 0, "Even-order passive Chebyshev audit failed:\n  " + string.Join("\n  ", failures));
}

static void AuditEdgeSpecification(FilterDesignResult result, ICollection<string> failures, string name, bool checkStopband)
{
    if (!result.IsImplemented || result.AnalysisResult == null || result.DesignRequest == null)
    {
        failures.Add($"{name}: design failed: {result.Message}");
        return;
    }
    var request = result.DesignRequest;
    var passbandReferenceDb = request.Mode == FilterMode.Active ? 20 * Math.Log10(request.PassbandGain) : 0;
    var passbandTargetDb = passbandReferenceDb - request.PassbandRippleDb;
    var stopbandLimitDb = passbandReferenceDb - request.StopbandAttenuationDb;
    var passbandEdges = request.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass
        ? new[] { request.PassbandEdgeHz }
        : new[] { request.PassbandLowerHz, request.PassbandUpperHz };
    var stopbandEdges = request.ResponseType is FilterResponseType.LowPass or FilterResponseType.HighPass
        ? new[] { request.StopbandEdgeHz }
        : new[] { request.StopbandLowerHz, request.StopbandUpperHz };
    foreach (var frequency in passbandEdges)
    {
        var point = result.AnalysisResult.Points.MinBy(value => Math.Abs(Math.Log(value.FrequencyHz / frequency)))!;
        if (Math.Abs(point.MagnitudeDb - passbandTargetDb) > .25)
            failures.Add($"{name}: fp={frequency:G6} Hz is {point.MagnitudeDb:0.###} dB, expected {passbandTargetDb:0.###} dB");
    }
    if (!checkStopband || !result.MeetsRequestedOrder) return;
    foreach (var frequency in stopbandEdges)
    {
        var point = result.AnalysisResult.Points.MinBy(value => Math.Abs(Math.Log(value.FrequencyHz / frequency)))!;
        if (point.MagnitudeDb > stopbandLimitDb + .25)
            failures.Add($"{name}: fs={frequency:G6} Hz is {point.MagnitudeDb:0.###} dB, limit {stopbandLimitDb:0.###} dB");
    }
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

static void TestPassiveLcTerminationAudit()
{
    foreach (var approximation in new[] { FilterApproximation.Butterworth, FilterApproximation.ChebyshevTypeI })
    foreach (var order in Enumerable.Range(1, 6))
    {
        var transfer = approximation == FilterApproximation.Butterworth
            ? new ButterworthPrototypeDesigner().Design(order, 1)
            : new ChebyshevPrototypeDesigner().Design(order, 1);
        var ladder = transfer.LcLadderPrototype ?? throw new InvalidOperationException("Classic LC ladder metadata is missing.");
        Require(ladder.TerminationMode == LcTerminationMode.DoublyTerminated && ladder.Elements.Count == order,
            "Classic LC prototype is not explicitly doubly terminated.");
        Require(ladder.Elements.All(value => value.GValue > 0 && double.IsFinite(value.GValue)),
            "Classic LC prototype contains a non-positive normalized element.");
        Require(ladder.AsLegacyGValues().Zip(transfer.LadderGValues).All(pair => Nearly(pair.First, pair.Second, 1e-12)),
            "Independent LC ladder model does not reproduce the audited classic g-values.");
    }

    var terminated = Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.Butterworth,
        FilterImplementationType.Lc, 1_000, 3_000, manualOrder: 3) with
        { LcTerminationMode = LcTerminationMode.DoublyTerminated, SourceResistanceOhm = 50, LoadResistanceOhm = 50 };
    var result = new FilterDesignEngine().Design(terminated);
    Require(result.IsImplemented && result.LcLadderPrototype?.TerminationMode == LcTerminationMode.DoublyTerminated,
        result.Message);
    Require(result.AnalysisResult?.MagnitudeReferenceDescription.Contains("available source power", StringComparison.OrdinalIgnoreCase) == true,
        "Doubly terminated LC analysis is not using the transducer-power reference.");

    var ideal = terminated with { LcTerminationMode = LcTerminationMode.IdealVoltageSource, SourceResistanceOhm = 0 };
    var decision = FilterSupportMatrix.Evaluate(ideal);
    Require(!decision.IsSupported && decision.ReasonCode == FilterSupportReasonCode.LcTerminationModeMismatch,
        "Ideal Voltage Source mode must not silently consume a doubly terminated g prototype.");
}

static void TestBesselLcCauerSynthesis()
{
    foreach (var order in Enumerable.Range(1, 6))
    {
        var transfer = new BesselPrototypeDesigner().Design(order, 1);
        var ladder = transfer.LcLadderPrototype ?? throw new InvalidOperationException($"Bessel order {order} ladder is missing.");
        Require(ladder.SynthesisMethod.Contains("Cauer", StringComparison.Ordinal) &&
                ladder.ExtractionRecords?.Count == order,
            $"Bessel order {order} did not retain its insertion-loss/Cauer extraction trace.");
        Require(ladder.Elements.Count == order && ladder.Elements.All(value => value.GValue > 0 && double.IsFinite(value.GValue)),
            $"Bessel order {order} produced a non-positive normalized element.");
        Require(ladder.ValidationMetrics.ReconstructedResponseMatches && ladder.ValidationMetrics.MaximumMagnitudeError < 2e-5,
            $"Bessel order {order} loaded ladder does not reconstruct its target response.");
    }
    var firstOrder = new BesselPrototypeDesigner().Design(1, 1).LcLadderPrototype!;
    Require(Nearly(firstOrder.Elements.Single().GValue, 2, 1e-10) && Nearly(firstOrder.LoadTerminationGN1, 1, 1e-10),
        "First-order Bessel insertion-loss ladder must match the analytical series-L g1=2 reference.");
}

static void TestBesselLcResponsesEndToEnd()
{
    foreach (var response in Enum.GetValues<FilterResponseType>())
    {
        var fp = response == FilterResponseType.HighPass ? 2_000 : 1_000;
        var fs = response == FilterResponseType.HighPass ? 1_000 : 2_000;
        var request = Request(FilterMode.Passive, response, FilterApproximation.Bessel,
            FilterImplementationType.Lc, fp, fs, optimize: true, manualOrder: 3) with
        { LcTerminationMode = LcTerminationMode.DoublyTerminated };
        var result = new FilterDesignEngine().Design(request);
        Require(result.IsImplemented, $"Bessel LC {response}: {result.Message}");
        Require(result.LcLadderPrototype?.ValidationMetrics.ReconstructedResponseMatches == true &&
                result.Verification?.IsValid == true && result.CircuitGraph?.Components.Count > 0,
            $"Bessel LC {response} did not complete ladder and loaded CircuitGraph verification.");
        Require(result.CircuitGraph!.Components.Where(value => value.Type is ComponentType.Inductor or ComponentType.Capacitor)
            .All(component => component.Type == ComponentType.Inductor
                ? StandardValueSeries.IsSeriesValue(component.Parameters.GetRequiredValue(ComponentParameterNames.InductanceHenry), request.ComponentConstraints.InductorSeries, 5e-3)
                : StandardValueSeries.IsSeriesValue(component.Parameters.GetRequiredValue(ComponentParameterNames.CapacitanceFarad), request.ComponentConstraints.CapacitorSeries, 5e-3)),
            $"Bessel LC {response} contains a non-selected L/C value.");
        if (response == FilterResponseType.LowPass)
        {
            var report = new FilterDesignReportBuilder().Build(result);
            Require(report.DesignSummary.Any(value => value.Name == "LC termination mode" && value.Value == "DoublyTerminated") &&
                    report.ApproximationDetails.Any(value => value.Name == "LC synthesis method" && value.Value.Contains("Cauer", StringComparison.Ordinal)) &&
                    report.ApproximationDetails.Any(value => value.Name == "Cauer extraction steps" && value.Value == "3"),
                "Bessel LC report omitted termination, synthesis method, or Cauer trace summary.");
        }
    }
}

static void TestTabulatedLcFeasibilityAudit()
{
    var unsupported = new HashSet<(FilterApproximation, int)>
    {
        (FilterApproximation.GaussianTo6Db, 10),
        (FilterApproximation.GaussianTo12Db, 3),
        (FilterApproximation.GaussianTo12Db, 8),
        (FilterApproximation.GaussianTo12Db, 10),
        (FilterApproximation.LinearPhase005Degree, 10)
    };
    foreach (var approximation in new[]
    {
        FilterApproximation.GaussianTo6Db, FilterApproximation.GaussianTo12Db,
        FilterApproximation.LinearPhase005Degree, FilterApproximation.LinearPhase05Degree
    })
    foreach (var order in TabulatedPrototypeCatalog.GetOrders(approximation))
    {
        var audit = LcPrototypeCapabilityRegistry.Evaluate(approximation, order);
        Require(audit.IsSupported == !unsupported.Contains((approximation, order)),
            $"{approximation} order {order} feasibility changed unexpectedly: {audit.ReasonCode} {audit.Message}");
        Require(audit.IsSupported
                ? audit.Prototype?.ValidationMetrics.ReconstructedResponseMatches == true && audit.Prototype.Elements.All(value => value.GValue > 0)
                : audit.Prototype == null && audit.ReasonCode is "LcPrototypeNotPositiveReal" or "LcLadderExtractionProducedNegativeElement" or "LcFinalReconstructionFailed",
            $"{approximation} order {order} did not produce a complete and explicit feasibility conclusion.");
    }

    var rejected = Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.GaussianTo12Db,
        FilterImplementationType.Lc, 1_000, 3_000, manualOrder: 3);
    var decision = FilterSupportMatrix.Evaluate(rejected);
    Require(!decision.IsSupported && decision.ReasonCode == FilterSupportReasonCode.LcPrototypeNotPositiveReal,
        "A non-positive-real family/order must be rejected by the central capability matrix.");
}

static void TestTabulatedLcSupportedEndToEnd()
{
    foreach (var approximation in new[]
    {
        FilterApproximation.GaussianTo6Db, FilterApproximation.GaussianTo12Db,
        FilterApproximation.LinearPhase005Degree, FilterApproximation.LinearPhase05Degree
    })
    foreach (var response in Enum.GetValues<FilterResponseType>())
    {
        var fp = response == FilterResponseType.HighPass ? 2_000 : 1_000;
        var fs = response == FilterResponseType.HighPass ? 1_000 : 2_000;
        var request = Request(FilterMode.Passive, response, approximation, FilterImplementationType.Lc,
            fp, fs, manualOrder: 4);
        var result = new FilterDesignEngine().Design(request);
        Require(result.IsImplemented && result.LcLadderPrototype?.ValidationMetrics.ReconstructedResponseMatches == true &&
                result.Verification?.IsValid == true,
            $"{approximation}/{response}: {result.Message}");
    }
}

static void TestFilterV1ClosureSupportMatrix()
{
    var matrix = FilterV1Status.BuildSupportMatrix();
    Require(matrix.Any(value => value.Mode == FilterMode.Active && value.Response == FilterResponseType.BandStop &&
                                value.Implementation == FilterImplementationType.BainterNotch && value.IsSupported),
        "Closure matrix omitted verified Bainter Active Band-Stop.");
    Require(matrix.Where(value => value.Mode == FilterMode.Active && value.Response == FilterResponseType.BandStop &&
                                  value.Implementation is FilterImplementationType.SallenKey or FilterImplementationType.MultipleFeedback)
        .All(value => !value.IsSupported), "Closure matrix incorrectly opens Sallen-Key/MFB Band-Stop.");
    Require(matrix.Any(value => value.Approximation == FilterApproximation.Bessel && value.IsSupported &&
                                value.Conditions.Contains("1-6", StringComparison.Ordinal)),
        "Closure matrix omitted the qualified Bessel LC order range.");
    Require(matrix.Where(value => value.Mode == FilterMode.Passive && value.Implementation == FilterImplementationType.Rc)
        .All(value => value.IsSupported && value.Conditions.Contains("Topology-based", StringComparison.Ordinal)),
        "Closure matrix does not expose all four verified topology-based passive RC responses.");
    Require(matrix.Any(value => value.Approximation == FilterApproximation.GaussianTo12Db &&
                                value.Conditions.EndsWith("3", StringComparison.Ordinal) && !value.IsSupported &&
                                value.Reason == "LcPrototypeNotPositiveReal"),
        "Closure matrix omitted a tested Gaussian positive-real failure.");
    Require(matrix.Where(value => value.Mode == FilterMode.Passive && value.Implementation == FilterImplementationType.Lc && value.IsSupported)
        .All(value => value.Conditions.Contains("DoublyTerminated", StringComparison.Ordinal)),
        "A supported LC entry lacks its explicit termination condition.");
}

static void TestLcBandTransformationResonance()
{
    var specification = new NormalizedFilterSpecification(FilterResponseType.BandPass, FilterApproximation.Butterworth, 1, 2, 2, 1, 40, 1, 1, 1, true, null, 2 * Math.PI * 1_000, 2 * Math.PI * 200);
    var service = new PassiveLcTransformationService();
    foreach (var response in new[] { FilterResponseType.BandPass, FilterResponseType.BandStop })
    {
        var actualSpec = specification with { ResponseType = response };
        var prototype = ClassicLcPrototypeFactory.Create(FilterApproximation.Butterworth, 1, [1, 2, 1],
            "test", "reference");
        var branch = service.Transform(prototype, response, 50, actualSpec).Single();
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
    foreach(var approximation in new[]{FilterApproximation.Butterworth,FilterApproximation.ChebyshevTypeI})
    foreach(var response in new[]{FilterResponseType.LowPass,FilterResponseType.HighPass})
    {var result = new FilterDesignEngine().Design(Request(FilterMode.Passive,response,approximation,FilterImplementationType.Rc,response==FilterResponseType.LowPass?1_000:2_000,response==FilterResponseType.LowPass?2_000:1_000,manualOrder:1));Require(result.IsImplemented,$"{approximation}/{response}: {result.Message}");Equal(2,result.CircuitGraph!.Components.Count);Require(result.CircuitGraph.GetNet("VIN").Pins.Count==1,"RC input net is incorrect.");Require(result.CircuitGraph.GetNet("VOUT").Pins.Count==2,"RC output node must join R and C.");}
}

static void TestSchematicPinAlignment()
{
    var results = new[]
    {
        new FilterDesignEngine().Design(Request(FilterMode.Active, FilterResponseType.HighPass, FilterApproximation.ChebyshevTypeI, FilterImplementationType.SallenKey, 2_000, 1_000, manualOrder:2)),
        new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.BandPass, FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 2_000, manualOrder: 3)),
        new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.BandStop, FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 2_000, manualOrder: 3))
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
    var results=new[]{new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.LowPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,1_000,4_000)),new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.HighPass,FilterApproximation.Butterworth,FilterImplementationType.SallenKey,3_000,1_000,manualOrder:2)),new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.MultipleFeedback,1_000,2_000,manualOrder:1))};
    foreach(var result in results){Require(result.IsImplemented,result.Message);var boxes=result.SchematicDocument!.PlacedComponents.Select(item=>(item.Component.Reference,Bounds:SchematicGeometry.GetBounds(item,8))).ToArray();for(var i=0;i<boxes.Length;i++)for(var j=i+1;j<boxes.Length;j++)Require(!Intersects(boxes[i].Bounds,boxes[j].Bounds),$"{boxes[i].Reference} overlaps {boxes[j].Reference} in graph with {result.CircuitGraph!.Components.Count} components.");}
}

static void TestSchematicOccupiedBounds()
{
    var result = new FilterDesignEngine().Design(WideRanges(Request(FilterMode.Active, FilterResponseType.LowPass,
        FilterApproximation.Butterworth, FilterImplementationType.SallenKey, 1_000, 4_000, manualOrder: 4) with { PassbandGain = 2 }));
    Require(result.IsImplemented, result.Message);
    var detector = new ComponentCollisionDetector();
    foreach (var placed in result.SchematicDocument!.PlacedComponents)
    {
        var occupied = detector.GetOccupiedBounds(placed);
        Require(occupied.OccupiedBounds.Width >= occupied.SymbolBounds.Width && occupied.PinKeepoutBounds.Count == placed.Component.Pins.Count,
            $"Occupied bounds for {placed.Component.Reference} do not include symbol/text/pin keepouts.");
    }
}

static void TestSchematicWiresAvoidOpAmps()
{
    var cases=new[]{(FilterResponseType.LowPass,FilterImplementationType.SallenKey),(FilterResponseType.HighPass,FilterImplementationType.SallenKey),(FilterResponseType.BandPass,FilterImplementationType.MultipleFeedback)};
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
    var result=new FilterDesignEngine().Design(request);
    Require(result.IsImplemented, result.Message);
    Equal(2, result.RealizedOrder); Equal(1, result.Sections.Count);
    Require(result.Verification?.IsValid == true, "Bainter Band-Stop did not pass the final verification gate.");
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

    Require(skResult.GainDistribution is { RequestedOverallGain: 2 }, "Gain distribution summary did not retain requested A0.");
    Require(skResult.Verification?.IsValid == true, "A0=2 Sallen-Key design did not pass the final verification gate.");
    Require(mfbBandPassResult.GainDistribution?.ExpectedOverallPolarity == CircuitModuleDesigner.Modules.Filters.FilterDesign.Gain.FilterStagePolarity.NonInverting,
        "Two MFB biquads must restore non-inverting overall polarity.");
    Require(Math.Cos(mfbBandPassResult.GainDistribution!.ReferencePhaseDegrees * Math.PI / 180) > .95,
        "Two MFB biquads do not recover non-inverting center-frequency phase in the actual CircuitGraph analysis.");

    var fourthOrder = WideRanges(Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Butterworth,
        FilterImplementationType.SallenKey, 1_000, 4_000, manualOrder: 4) with { PassbandGain = 2 });
    var fourthOrderResult = engine.Design(fourthOrder);
    Require(fourthOrderResult.IsImplemented, fourthOrderResult.Message);
    Require(Nearly(fourthOrderResult.Sections[0].Target.SectionGain, 1, 1e-12) && Nearly(fourthOrderResult.Sections[1].Target.SectionGain, 2, 1e-12),
        "A0=2 must be assigned explicitly to a realizable section, not mechanically spread as sqrt(2) across both sections.");
    Require(Math.Abs(fourthOrderResult.GainDistribution!.ActualPassbandPeakGain - 2) / 2 < .05,
        "Whole-circuit A0=2 verification failed for the fourth-order Sallen-Key cascade.");

    var oddMfb = WideRanges(Request(FilterMode.Active, FilterResponseType.BandPass, FilterApproximation.Butterworth,
        FilterImplementationType.MultipleFeedback, 1_000, 2_000, manualOrder: 1));
    var oddMfbResult = engine.Design(oddMfb);
    Require(oddMfbResult.IsImplemented, oddMfbResult.Message);
    Require(oddMfbResult.GainDistribution!.ExpectedOverallPolarity == FilterStagePolarity.Inverting &&
            oddMfbResult.Sections.Single().PolaritySign == -1,
        "One MFB biquad must retain its inverting polarity in the design result.");
    Require(Math.Cos(oddMfbResult.GainDistribution.ReferencePhaseDegrees * Math.PI / 180) < -.95,
        "One MFB biquad does not show inverting center-frequency phase in the actual CircuitGraph analysis.");

    var attenuatedSk = WideRanges(Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.Butterworth,
        FilterImplementationType.SallenKey, 1_000, 4_000, manualOrder: 2) with { PassbandGain = .5 });
    var attenuatedResult = engine.Design(attenuatedSk);
    Require(attenuatedResult.IsImplemented, attenuatedResult.Message);
    Require(attenuatedResult.AdditionalNetworkComponents.Any(value => value.Role == "InterstageAttenuatorSeries") &&
            attenuatedResult.CircuitGraph!.Components.Count(value => value.Type == ComponentType.OpAmp) == 2,
        "Sallen-Key A0 below unity must use a named attenuation network and one isolating follower.");
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
    Require(FilterSupportMatrix.Evaluate(FilterDesignRequest.FromState(state)).IsSupported,"Default Active Band-Stop must select the verified Bainter path.");
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
    new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.MultipleFeedback,1_000,2_000,manualOrder:1))
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
    Require(result.IsImplemented, result.Message);
}

static void TestBainterWiresAvoidComponentBodies()
{
    var result=new FilterDesignEngine().Design(Request(FilterMode.Active,FilterResponseType.BandStop,FilterApproximation.Butterworth,FilterImplementationType.BainterNotch,1_000,2_000,manualOrder:1));
    Require(result.IsImplemented, result.Message);
    RequireCleanSchematic(result);
}

static void TestPassiveBandModesSelectLc()
{
    var state=new FilterDesignState { Mode=FilterMode.Passive };
    state.SelectResponseType(FilterResponseType.BandPass);
    Require(state.Implementation==FilterImplementationType.Lc && state.PassiveRcTopology==PassiveRcTopology.TwoCornerBandPass,
        "Passive Band-Pass must default to LC while retaining the response-specific RC topology option.");
    state.SelectImplementation(FilterImplementationType.Rc);
    state.SelectResponseType(FilterResponseType.BandStop);
    Require(state.Implementation==FilterImplementationType.Rc && state.PassiveRcTopology==PassiveRcTopology.TwinTNotch,
        "Passive Band-Stop must expose the Twin-T RC implementation.");
    state.SelectImplementation(FilterImplementationType.Lc);
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
    var bandPass=new FilterDesignEngine().Design(WideRanges(Request(FilterMode.Active,FilterResponseType.BandPass,FilterApproximation.Butterworth,FilterImplementationType.MultipleFeedback,1_000,2_000,manualOrder:1)));
    Require(bandPass.IsImplemented,bandPass.Message);var center=Math.Sqrt(bandPass.DesignRequest!.PassbandLowerHz*bandPass.DesignRequest.PassbandUpperHz);var centerPoint=bandPass.AnalysisResult!.Points.MinBy(point=>Math.Abs(Math.Log(point.FrequencyHz/center)))!;
    Require(centerPoint.MagnitudeDb>-2,$"MFB band-pass AC analysis has an unexpected center loss ({centerPoint.MagnitudeDb:0.##} dB).");
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
    Require(lcNetlist.Contains("V_INPUT VSRC 0",StringComparison.Ordinal)&&lcNetlist.Contains("R_SOURCE VSRC VIN",StringComparison.Ordinal)&&lcNetlist.Contains("R_LOAD VOUT 0",StringComparison.Ordinal),"LC CIR must include its VSRC/Rs/VIN/VOUT/RL boundary.");
}

static void TestLtspiceCommonSerializer()
{
    var builder = new CircuitBuilder();
    var resistor = builder.Add(ComponentFactory.Resistor("R1", 1_000));
    var capacitor = builder.Add(ComponentFactory.Capacitor("C1", 2.2e-9));
    var inductor = builder.Add(ComponentFactory.Inductor("L1", 3.3e-3));
    var diode = builder.Add(ComponentFactory.Diode("D1"));
    builder.Connect("VIN", resistor.GetPin("1"));
    builder.Connect("N:RC/1", resistor.GetPin("2"), capacitor.GetPin("1"), inductor.GetPin("1"));
    builder.Connect("VOUT", inductor.GetPin("2"), diode.GetPin("A"));
    builder.Connect("GND", capacitor.GetPin("2"), diode.GetPin("K"));
    var graph = builder.Build();
    var options = new CircuitSpiceExportOptions("LTspice RLC and diode golden")
    {
        Sources =
        [
            new("V_INPUT", SpiceSourceKind.Voltage, "VIN", "GND", AcMagnitude: 1),
            new("I_BIAS", SpiceSourceKind.Current, "VOUT", "GND", 1e-6)
        ],
        Analyses = [new SpiceAcAnalysis(SpiceSweepScale.Decade, 40, 10, 1e6)],
        OutputExpressions = ["VM(VOUT) VP(VOUT)"],
        DiodeModel = new("CMD_DIODE", 1e-12, 1.1)
    };
    var netlist = new LtspiceNetlistExporter().Export(graph, options);
    foreach (var expected in new[]
    {
        "V_INPUT VIN 0 DC 0 AC 1e+0 0", "I_BIAS VOUT 0 DC 1e-6", "R1 VIN N_3A_RC_2F_1 1e+3",
        "C1 N_3A_RC_2F_1 0 2.2e-9", "L1 N_3A_RC_2F_1 VOUT 3.3e-3", "D1 VOUT 0 CMD_DIODE",
        ".MODEL CMD_DIODE D(IS=1e-12 N=1.1e+0)", ".AC DEC 40 1e+1 1e+6", ".END"
    }) Require(netlist.Contains(expected, StringComparison.Ordinal), $"LTspice netlist omitted or changed '{expected}'.\n{netlist}");
    Require(!netlist.Contains("GND", StringComparison.Ordinal), "SPICE ground was not normalized to node 0.");
}

static void TestLtspiceOpAmpSubcircuit()
{
    var builder = new CircuitBuilder(); var opAmp = builder.Add(ComponentFactory.OpAmp("U1"));
    builder.Connect("VIN", opAmp.GetPin("IN+")); builder.Connect("VOUT", opAmp.GetPin("IN-"), opAmp.GetPin("OUT"));
    builder.Connect("VCC", opAmp.GetPin("V+")); builder.Connect("VEE", opAmp.GetPin("V-"));
    var graph = builder.Build();
    var subcircuit = new SpiceOpAmpSubcircuit("CMD_OPAMP", ["IN+", "IN-", "V+", "V-", "OUT"], EmbeddedBodyLines: ["EGAIN OUT 0 IN_2B_ IN_2D_ 1e6"]);
    var netlist = new LtspiceNetlistExporter().Export(graph, new CircuitSpiceExportOptions("Subcircuit golden")
    {
        OpAmpMode = SpiceOpAmpExportMode.ExternalSubcircuit, OpAmpSubcircuit = subcircuit,
        Sources = [new("VCC_SOURCE", SpiceSourceKind.Voltage, "VCC", "0", 15), new("VEE_SOURCE", SpiceSourceKind.Voltage, "VEE", "0", -15)]
    });
    Require(netlist.Contains("X_U1 VIN VOUT VCC VEE VOUT CMD_OPAMP", StringComparison.Ordinal), "Op-amp was not emitted as an X subcircuit instance with explicit pin order.");
    Require(netlist.Contains(".SUBCKT CMD_OPAMP IN_2B_ IN_2D_ V_2B_ V_2D_ OUT", StringComparison.Ordinal) && netlist.Contains(".ENDS CMD_OPAMP", StringComparison.Ordinal), "Embedded .SUBCKT block is incomplete.");
    Require(!netlist.Contains("\nU1 ", StringComparison.Ordinal), "A bare U-device line was emitted even though SPICE does not define a generic U op-amp element.");
}

static void TestSpiceDirectiveProfiles()
{
    var builder = new CircuitBuilder(); var resistor = builder.Add(ComponentFactory.Resistor("R1", 10_000));
    builder.Connect("VIN", resistor.GetPin("1")); builder.Connect("GND", resistor.GetPin("2")); var graph = builder.Build();
    var transient = new LtspiceNetlistExporter().Export(graph, new CircuitSpiceExportOptions("Transient directives")
    {
        Sources = [new("V1", SpiceSourceKind.Voltage, "VIN", "0")],
        Analyses = [new SpiceTransientAnalysis(1e-6, 1e-3, 0, 5e-6)], OutputExpressions = ["V(VIN)"],
        Parameters = new Dictionary<string, double> { ["GAIN"] = 1.25 },
        LibraryIncludes = ["models/common.lib"], Libraries = [new("vendor.lib", "OPAMP_ENTRY")]
    });
    foreach (var expected in new[] { ".INCLUDE \"models/common.lib\"", ".LIB \"vendor.lib\" OPAMP_ENTRY", ".PARAM GAIN=1.25e+0", ".TRAN 1e-6 1e-3 0 5e-6", ".PRINT TRAN V(VIN)" })
        Require(transient.Contains(expected, StringComparison.Ordinal), $"SPICE directive profile omitted '{expected}'.");

    var operatingPoint = new LtspiceNetlistExporter().Export(graph, new CircuitSpiceExportOptions("Operating point")
    {
        Sources = [new("V1", SpiceSourceKind.Voltage, "VIN", "0", 1)], Analyses = [new SpiceOperatingPointAnalysis()]
    });
    Require(operatingPoint.Contains(".OP", StringComparison.Ordinal), "Operating-point directive was not serialized.");

    var currentSweep = new LtspiceNetlistExporter().Export(graph, new CircuitSpiceExportOptions("Current sweep")
    {
        Sources = [new("I_BIAS", SpiceSourceKind.Current, "VIN", "0")], Analyses = [new SpiceDcAnalysis("I_BIAS", 0, 1e-3, 1e-5)]
    });
    Require(currentSweep.Contains("I_BIAS VIN 0 DC 0", StringComparison.Ordinal) && currentSweep.Contains(".DC I_BIAS 0 1e-3 1e-5", StringComparison.Ordinal),
        "Independent current source or current-source DC sweep was serialized with voltage-source semantics.");
}

static void TestSpiceInvariantFormatting()
{
    var previous = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        var builder = new CircuitBuilder(); var resistor = builder.Add(ComponentFactory.Resistor("R1", 1.25e6));
        builder.Connect("VIN", resistor.GetPin("1")); builder.Connect("GND", resistor.GetPin("2"));
        var netlist = new MultisimNetlistExporter().Export(builder.Build(), new CircuitSpiceExportOptions("Culture golden")
        {
            Sources = [new("V1", SpiceSourceKind.Voltage, "VIN", "0", -1.25)], Analyses = [new SpiceDcAnalysis("V1", -1, 1, .1)]
        });
        Require(netlist.Contains("R1 VIN 0 1.25e+6", StringComparison.Ordinal) && netlist.Contains("V1 VIN 0 DC -1.25e+0", StringComparison.Ordinal), "SPICE values are not invariant scientific notation.");
        Require(!netlist.Contains("1,25", StringComparison.Ordinal) && !netlist.Contains("1M", StringComparison.Ordinal), "Localized decimal or ambiguous M suffix leaked into SPICE output.");
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
}

static void TestPassiveReportAndCirBoundary()
{
    var lc = new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.LowPass,
        FilterApproximation.Butterworth, FilterImplementationType.Lc, 1_000, 3_000, manualOrder: 3));
    Require(lc.IsImplemented, lc.Message);
    var report = new FilterDesignReportBuilder().Build(lc);
    Require(report.SourceLoadReference.Any(value => value.Name == "Electrical boundary" && value.Value.Contains("VSRC", StringComparison.Ordinal)) &&
            report.SourceLoadReference.Any(value => value.Name == "Primary response reference" && value.Value == "TransducerGain") &&
            report.CircuitSummary.ExternalTerminations == 2,
        "Passive report omitted the loaded boundary, response reference, or external termination count.");
    Require((string)report.Snapshot.Request["LcTerminationPolicy"] == "PrototypeNativeTermination" &&
            (string)report.Snapshot.Request["PassiveResponseReference"] == "TransducerGain",
        $"Passive reproducibility snapshot omitted termination/reference semantics: policy={report.Snapshot.Request["LcTerminationPolicy"]}, reference={report.Snapshot.Request["PassiveResponseReference"]}.");

    var rcRequest = Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.Butterworth,
        FilterImplementationType.Rc, 1_000, 4_000, manualOrder: 1) with { SourceResistanceOhm = 100, LoadResistanceOhm = 10_000 };
    var rc = new FilterDesignEngine().Design(rcRequest);
    Require(rc.IsImplemented, rc.Message);
    Require(rc.SpiceNetlist.Contains("R_SOURCE VSRC VIN", StringComparison.Ordinal) && rc.SpiceNetlist.Contains("R_LOAD VOUT 0", StringComparison.Ordinal),
        "Passive RC CIR export omitted the same external Rs/RL used by loaded verification.");
}

static void TestPassiveLcStructuredFeasibility()
{
    var rangeRequest = Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.Butterworth,
        FilterImplementationType.Lc, 1_000, 3_000, manualOrder: 3);
    rangeRequest.ComponentConstraints.OptimizeStandardValues = false;
    rangeRequest.ComponentConstraints.InductorMinimumH = 1e-12;
    rangeRequest.ComponentConstraints.InductorMaximumH = 1e-9;
    var rangeFailure = new FilterDesignEngine().Design(rangeRequest);
    Require(!rangeFailure.IsImplemented && rangeFailure.Capability?.ReasonCode == FilterSupportReasonCode.LcComponentRangeViolation,
        $"LC range failure was not structured: {rangeFailure.Capability?.ReasonCode} / {rangeFailure.Message}");

    var narrow = Request(FilterMode.Passive, FilterResponseType.BandPass, FilterApproximation.Butterworth,
        FilterImplementationType.Lc, 1_000, 1_300, manualOrder: 3) with
    {
        StopbandLowerHz = 800,
        PassbandLowerHz = 1_000,
        PassbandUpperHz = 1_020,
        StopbandUpperHz = 1_300
    };
    var narrowResult = new FilterDesignEngine().Design(narrow);
    Require(narrowResult.IsImplemented && narrowResult.Warnings.Any(value => value.StartsWith("IdealComponentAssumptionMayBeUnrealistic", StringComparison.Ordinal)),
        $"Narrow-band LC design omitted its ideal-component warning: {narrowResult.Message}");
}

static void TestPassiveManualTestRowRoundTrip()
{
    var fields = Enumerable.Repeat("无", FilterManualTestRowService.ColumnCount).ToArray();
    fields[0] = "2"; fields[1] = "RC finite source"; fields[2] = "Rc"; fields[3] = "FirstOrderLowPass";
    fields[4] = "LowPass"; fields[5] = "Butterworth"; fields[6] = "Automatic"; fields[8] = "3"; fields[9] = "20";
    fields[10] = "2000"; fields[11] = "40000"; fields[16] = "600"; fields[17] = "100000";
    fields[20] = "10000"; fields[21] = "1000"; fields[22] = "100000"; fields[23] = "1E-10"; fields[24] = "1E-6";
    fields[27] = "1"; fields[28] = "E96"; fields[29] = "E96"; fields[31] = "200";
    var csvRow = string.Join(',', fields);
    var service = new FilterManualTestRowService();
    var parsed = service.Parse(csvRow);
    Require(parsed.Request.Mode == FilterMode.Passive && parsed.Request.Implementation == FilterImplementationType.Rc &&
            parsed.Request.ResponseType == FilterResponseType.LowPass && Nearly(parsed.Request.SourceResistanceOhm, 600, 1e-12),
        "Manual test-row parser did not reproduce the configured passive request.");
    var result = new FilterDesignEngine().Design(parsed.Request);
    var completed = service.Complete(parsed, result);
    Require(completed.Split(',', StringSplitOptions.None).Length == FilterManualTestRowService.ColumnCount,
        "Completed CSV test row changed its column count.");
    var excelRow = csvRow.Replace(',', '\t');
    Require(service.Parse(excelRow).Delimiter == '\t', "Excel tab-separated row was not accepted.");
}

static void TestCombinedManualTestRowRoundTrip()
{
    var service = new FilterManualTestRowService();

    var active = Enumerable.Repeat("无", FilterManualTestRowService.ComprehensiveColumnCount).ToArray();
    active[0] = "1"; active[1] = "Active LP"; active[2] = "Active"; active[3] = "LowPass";
    active[4] = "Butterworth"; active[5] = "SallenKey"; active[6] = "SingleEnded"; active[7] = "Specified";
    active[8] = "2"; active[9] = "1"; active[10] = "30"; active[11] = "1"; active[12] = "1000"; active[13] = "3000";
    active[23] = "10000"; active[24] = "1000"; active[25] = "1000000"; active[26] = "1E-10"; active[27] = "1E-6";
    active[30] = "1"; active[31] = "E24"; active[32] = "E24"; active[34] = "1000";
    var parsedActive = service.Parse(string.Join(',', active));
    Require(parsedActive.IsComprehensive && parsedActive.Request.Mode == FilterMode.Active && parsedActive.Request.Implementation == FilterImplementationType.SallenKey,
        "Combined-row parser did not reproduce the configured active request.");
    var completedActive = service.Complete(parsedActive, new FilterDesignEngine().Design(parsedActive.Request));
    Require(completedActive.Split(',', StringSplitOptions.None).Length == FilterManualTestRowService.ComprehensiveColumnCount,
        "Completed combined active row changed its column count.");
    var compactActive = service.Parse(string.Join(',', active.Take(FilterManualTestRowService.ComprehensiveInputColumnCount)));
    Require(compactActive.IsComprehensive && compactActive.Request.Mode == FilterMode.Active,
        "Compact 35-field TXT input row was not accepted.");
    var compactLine = string.Join(',', active.Take(FilterManualTestRowService.ComprehensiveInputColumnCount));
    var threeRows = service.ParseMany("测试点,header\r\n" + string.Join("\r\n", Enumerable.Repeat(compactLine, 3)));
    Require(threeRows.Count == 3 && threeRows.All(row => row.Request.Mode == FilterMode.Active),
        "Batch parser did not skip the header or preserve all compact input rows.");
    var maximum = service.ParseMany(string.Join("\n", Enumerable.Repeat(compactLine, FilterManualTestRowService.MaximumBatchRows)));
    Require(maximum.Count == FilterManualTestRowService.MaximumBatchRows, "The documented 300-row batch limit was not accepted.");
    var rejected = false;
    try { _ = service.ParseMany(string.Join("\n", Enumerable.Repeat(compactLine, FilterManualTestRowService.MaximumBatchRows + 1))); }
    catch (ArgumentException exception) { rejected = exception.Message.Contains("At most 300", StringComparison.Ordinal); }
    Require(rejected, "A batch larger than 300 rows was not rejected with a clear limit message.");
    var idealSource = active.Take(FilterManualTestRowService.ComprehensiveInputColumnCount).ToArray();
    idealSource[2] = "Passive"; idealSource[5] = "Lc"; idealSource[18] = "0"; idealSource[19] = "1000000";
    idealSource[21] = "IdealVoltageSource";
    var parsedIdealSource = service.Parse(string.Join(',', idealSource));
    Require(parsedIdealSource.Request.LcTerminationMode == LcTerminationMode.IdealVoltageSource,
        "Combined-row parser confused LC termination mode with LC termination policy.");

    var passive = Enumerable.Repeat("无", FilterManualTestRowService.ComprehensiveColumnCount).ToArray();
    passive[0] = "25"; passive[1] = "Passive RC LP"; passive[2] = "Passive"; passive[3] = "LowPass";
    passive[4] = "Butterworth"; passive[5] = "Rc"; passive[6] = "SingleEnded"; passive[7] = "Automatic";
    passive[9] = "3"; passive[10] = "20"; passive[11] = "1"; passive[12] = "1000"; passive[13] = "20000";
    passive[18] = "0"; passive[19] = "1000000"; passive[20] = "FirstOrderLowPass"; passive[23] = "10000";
    passive[24] = "1000"; passive[25] = "100000"; passive[26] = "1E-10"; passive[27] = "1E-6";
    passive[30] = "1"; passive[31] = "E96"; passive[32] = "E96"; passive[34] = "100";
    var parsedPassive = service.Parse(string.Join(',', passive));
    Require(parsedPassive.IsComprehensive && parsedPassive.Request.Mode == FilterMode.Passive && parsedPassive.Request.PassiveRcTopology == PassiveRcTopology.FirstOrderLowPass,
        "Combined-row parser did not reproduce the configured passive request.");
    var completedPassive = service.Complete(parsedPassive, new FilterDesignEngine().Design(parsedPassive.Request));
    Require(completedPassive.Split(',', StringSplitOptions.None).Length == FilterManualTestRowService.ComprehensiveColumnCount,
        "Completed combined passive row changed its column count.");
}

static void TestFiniteLoadedRcRegressions()
{
    var cases = new[]
    {
        Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.Butterworth, FilterImplementationType.Rc, 2_000, 40_000) with
        { SourceResistanceOhm = 600, LoadResistanceOhm = 100_000, PassbandRippleDb = 3, StopbandAttenuationDb = 20, PassiveRcTopology = PassiveRcTopology.FirstOrderLowPass },
        Request(FilterMode.Passive, FilterResponseType.HighPass, FilterApproximation.Butterworth, FilterImplementationType.Rc, 5_000, 500) with
        { SourceResistanceOhm = 1_000, LoadResistanceOhm = 47_000, PassbandRippleDb = 3, StopbandAttenuationDb = 20, PassiveRcTopology = PassiveRcTopology.FirstOrderHighPass }
    };
    foreach (var request in cases)
    {
        request.ComponentConstraints.ResistorSeries = StandardComponentSeries.E96;
        request.ComponentConstraints.CapacitorSeries = StandardComponentSeries.E96;
        request.ComponentConstraints.ResistorMinimumOhm = 1_000; request.ComponentConstraints.ResistorMaximumOhm = 100_000;
        request.ComponentConstraints.CapacitorMinimumF = 1e-10; request.ComponentConstraints.CapacitorMaximumF = 1e-5;
        var result = new FilterDesignEngine().Design(request);
        Require(result.IsFullySupported, $"Finite loaded {request.ResponseType} failed: {result.Capability?.ReasonCode} / {result.Message}");
        var values = result.Sections.Single().Components.ToDictionary(value => value.Role, value => value.SelectedValue);
        foreach (var frequency in new[] { request.StopbandEdgeHz, request.PassbandEdgeHz, request.PassbandEdgeHz * 7 })
        {
            var expected = PassiveRcSectionCalculator.EvaluateLoadedFirstOrder(request.ResponseType, frequency,
                request.SourceResistanceOhm, request.LoadResistanceOhm, values["R"], values["C"]);
            var point = result.PassiveAnalysis!.AbsoluteVoltageTransfer.Points.MinBy(value => Math.Abs(Math.Log(value.FrequencyHz / frequency)))!;
            var atPoint = PassiveRcSectionCalculator.EvaluateLoadedFirstOrder(request.ResponseType, point.FrequencyHz,
                request.SourceResistanceOhm, request.LoadResistanceOhm, values["R"], values["C"]);
            Require(Math.Abs(point.MagnitudeLinear - atPoint.Magnitude) < 2e-7,
                $"Loaded {request.ResponseType} formula/MNA mismatch at {frequency:G6} Hz (reference {expected.Magnitude:G9}).");
        }
    }
}

static void TestRcBandAndTwinTDiagnostics()
{
    var bp = Request(FilterMode.Passive, FilterResponseType.BandPass, FilterApproximation.Butterworth, FilterImplementationType.Rc, 1_000, 10_000) with
    { SourceResistanceOhm = 600, LoadResistanceOhm = 20_000, PassbandRippleDb = 3, StopbandAttenuationDb = 20,
      StopbandLowerHz = 1_000, PassbandLowerHz = 2_000, PassbandUpperHz = 8_000, StopbandUpperHz = 16_000,
      PassiveRcTopology = PassiveRcTopology.TwoCornerBandPass };
    bp.ComponentConstraints.ResistorSeries = StandardComponentSeries.E96; bp.ComponentConstraints.CapacitorSeries = StandardComponentSeries.E96;
    bp.ComponentConstraints.ResistorMinimumOhm = 1_000; bp.ComponentConstraints.ResistorMaximumOhm = 100_000;
    bp.ComponentConstraints.CapacitorMinimumF = 1e-10; bp.ComponentConstraints.CapacitorMaximumF = 1e-5;
    var bpResult = new FilterDesignEngine().Design(bp);
    Require(bpResult.IsImplemented && bpResult.CircuitGraph != null && bpResult.Capability?.ReasonCode == FilterSupportReasonCode.RcBandPassCornersNotAchievable,
        $"Two-Corner BP did not return a valid graph and specific feasibility reason: {bpResult.Capability?.ReasonCode}.");
    Require(bpResult.Capability?.ReasonCode != FilterSupportReasonCode.PendingIndependentVerification, "Verified Two-Corner BP returned PendingIndependentVerification.");

    var twin = Request(FilterMode.Passive, FilterResponseType.BandStop, FilterApproximation.Butterworth, FilterImplementationType.Rc, 500, 2_500) with
    { SourceResistanceOhm = 50, LoadResistanceOhm = 100_000, PassbandRippleDb = 3, StopbandAttenuationDb = 20,
      PassbandLowerHz = 600, StopbandLowerHz = 1_200, StopbandUpperHz = 1_800, PassbandUpperHz = 2_400,
      PassiveRcTopology = PassiveRcTopology.TwinTNotch };
    twin.ComponentConstraints.ResistorSeries = StandardComponentSeries.E96; twin.ComponentConstraints.CapacitorSeries = StandardComponentSeries.E96;
    twin.ComponentConstraints.ResistorMinimumOhm = 1_000; twin.ComponentConstraints.ResistorMaximumOhm = 100_000;
    twin.ComponentConstraints.CapacitorMinimumF = 1e-10; twin.ComponentConstraints.CapacitorMaximumF = 1e-5;
    var twinResult = new FilterDesignEngine().Design(twin);
    Require(twinResult.IsImplemented && twinResult.CircuitGraph!.Components.Count == 8 &&
            twinResult.Capability?.ReasonCode == FilterSupportReasonCode.TwinTRequestedResponseNotAchievable,
        $"Twin-T did not retain its compound graph/specific response reason: {twinResult.Capability?.ReasonCode}.");
    var tv = twinResult.Sections.Single().Components.ToDictionary(value => value.Role, value => value.SelectedValue);
    var sample = twinResult.PassiveAnalysis!.AbsoluteVoltageTransfer.Points.MinBy(value => Math.Abs(Math.Log(value.FrequencyHz / 1_469)))!;
    var analytic = PassiveRcSectionCalculator.EvaluateTwinT(sample.FrequencyHz, twin.SourceResistanceOhm, twin.LoadResistanceOhm, tv["R1"], tv["C1"]);
    Require(Math.Abs(sample.MagnitudeLinear - analytic.Magnitude) < 2e-7, "Twin-T compound analytic response does not match loaded CircuitGraph/MNA.");
}

static void TestCapabilityFailureStages()
{
    var insufficient = Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.Butterworth,
        FilterImplementationType.Lc, 100_000, 300_000, manualOrder: 3) with
    { PassbandRippleDb = 1, StopbandAttenuationDb = 30, SourceResistanceOhm = 50, LoadResistanceOhm = 50,
      LcTerminationPolicy = LcTerminationPolicy.MatchedUserTermination, LcLadderOrientation = LcLadderOrientation.SeriesFirst };
    insufficient.ComponentConstraints.ResistorSeries = insufficient.ComponentConstraints.CapacitorSeries = insufficient.ComponentConstraints.InductorSeries = StandardComponentSeries.E96;
    var insufficientResult = new FilterDesignEngine().Design(insufficient);
    Require(insufficientResult.IsImplemented && insufficientResult.Capability?.ReasonCode == FilterSupportReasonCode.SpecifiedOrderInsufficient,
        $"Theoretical under-order LC was misclassified as {insufficientResult.Capability?.ReasonCode}.");

    FilterDesignRequest ESeries(StandardComponentSeries series)
    {
        var request = Request(FilterMode.Active, FilterResponseType.HighPass, FilterApproximation.ChebyshevTypeI,
            FilterImplementationType.SallenKey, 8_000, 2_000, optimize: true, manualOrder: 4) with { PassbandRippleDb = 1, StopbandAttenuationDb = 40, PassbandGain = 2 };
        request.ComponentConstraints.ResistorSeries = request.ComponentConstraints.CapacitorSeries = series;
        request.ComponentConstraints.ResistorMinimumOhm = 1_000; request.ComponentConstraints.ResistorMaximumOhm = 1_000_000;
        request.ComponentConstraints.CapacitorMinimumF = 1e-10; request.ComponentConstraints.CapacitorMaximumF = 1e-6;
        return request;
    }
    var e24 = new FilterDesignEngine().Design(ESeries(StandardComponentSeries.E24));
    var e96 = new FilterDesignEngine().Design(ESeries(StandardComponentSeries.E96));
    Require(e24.IsImplemented && e24.Capability?.ReasonCode == FilterSupportReasonCode.SelectedValuesFailSpecification &&
            e24.StageDiagnostics is { TheoreticalMeetsSpecifications: true, SelectedMeetsSpecifications: false },
        $"E24 selected-value failure was not isolated from theory: {e24.Capability?.ReasonCode}.");
    Require(e96.IsFullySupported, $"Equivalent E96 design should pass: {e96.Capability?.ReasonCode}.");
}

static void TestActiveBandPassFeasibilityReasons()
{
    var sk = Request(FilterMode.Active, FilterResponseType.BandPass, FilterApproximation.Butterworth,
        FilterImplementationType.SallenKey, 1_000, 6_000, manualOrder: 4) with
    { StopbandLowerHz = 500, PassbandLowerHz = 1_000, PassbandUpperHz = 3_000, StopbandUpperHz = 6_000, StopbandAttenuationDb = 30 };
    var skResult = new FilterDesignEngine().Design(sk);
    Require(skResult.Capability?.ReasonCode == FilterSupportReasonCode.SallenKeyBpGainCouplingViolation,
        $"SK BP infeasibility remained generic: {skResult.Capability?.ReasonCode}.");
    var mfb = Request(FilterMode.Active, FilterResponseType.BandPass, FilterApproximation.Bessel,
        FilterImplementationType.MultipleFeedback, 500, 4_000, manualOrder: 4) with
    { StopbandLowerHz = 200, PassbandLowerHz = 500, PassbandUpperHz = 1_500, StopbandUpperHz = 4_000, StopbandAttenuationDb = 25 };
    mfb.ComponentConstraints.ResistorSeries = mfb.ComponentConstraints.CapacitorSeries = StandardComponentSeries.E96;
    var mfbResult = new FilterDesignEngine().Design(mfb);
    Require(mfbResult.Capability?.ReasonCode == FilterSupportReasonCode.SectionQOutOfRange,
        $"MFB BP infeasibility remained generic: {mfbResult.Capability?.ReasonCode}.");
    Require(new[] { skResult, mfbResult }.All(result => result.Capability?.ReasonCode != FilterSupportReasonCode.PendingIndependentVerification),
        "A verified Active BP path still returned PendingIndependentVerification.");
}

static void TestFilterV1FinalClosureClassifications()
{
    Require(FilterV1Status.FilterV1Frozen, "Filter v1 must remain explicitly marked frozen after closure.");
    FilterDesignRequest RestrictedLc(FilterResponseType response, bool restrictInductors)
    {
        var request = Request(FilterMode.Passive, response, FilterApproximation.Butterworth,
            FilterImplementationType.Lc, 10_000, 40_000, optimize: true, manualOrder: 4) with
        {
            SourceResistanceOhm = 50, LoadResistanceOhm = 50,
            LcTerminationPolicy = LcTerminationPolicy.MatchedUserTermination,
            StopbandLowerHz = response == FilterResponseType.BandPass ? 5_000 : 10_000,
            PassbandLowerHz = response == FilterResponseType.BandPass ? 10_000 : 5_000,
            PassbandUpperHz = response == FilterResponseType.BandPass ? 20_000 : 40_000,
            StopbandUpperHz = response == FilterResponseType.BandPass ? 40_000 : 20_000
        };
        request.ComponentConstraints.InductorMinimumH = restrictInductors ? 1e-6 : 1e-7;
        request.ComponentConstraints.InductorMaximumH = restrictInductors ? 2e-6 : 10;
        request.ComponentConstraints.CapacitorMinimumF = restrictInductors ? 1e-12 : 1e-9;
        request.ComponentConstraints.CapacitorMaximumF = restrictInductors ? .01 : 2e-9;
        request.ComponentConstraints.CapacitorSeries = request.ComponentConstraints.InductorSeries = StandardComponentSeries.E96;
        return request;
    }

    foreach (var response in new[] { FilterResponseType.BandPass, FilterResponseType.BandStop })
    foreach (var restrictInductors in new[] { true, false })
    {
        var result = new FilterDesignEngine().Design(RestrictedLc(response, restrictInductors));
        Require(result.Status == FilterDesignStatus.SynthesisFailure &&
                result.Capability is { Level: FilterCapabilityLevel.ComponentRangeFeasibility,
                    ReasonCode: FilterSupportReasonCode.LcComponentRangeViolation } &&
                result.Message.Contains("theoretical LC", StringComparison.OrdinalIgnoreCase),
            $"Restricted {response} {(restrictInductors ? "L" : "C")} range was misclassified: {result.Capability?.Level}/{result.Capability?.ReasonCode}.");
    }

    var mfb = Request(FilterMode.Active, FilterResponseType.BandPass, FilterApproximation.Butterworth,
        FilterImplementationType.MultipleFeedback, 1_500, 7_000, optimize: true, manualOrder: 6) with
    {
        StopbandLowerHz = 1_000, PassbandLowerHz = 1_500, PassbandUpperHz = 4_500,
        StopbandUpperHz = 7_000, PassbandGain = .5, StopbandAttenuationDb = 45
    };
    var mfbResult = new FilterDesignEngine().Design(mfb);
    Require(mfbResult.Capability?.ReasonCode == FilterSupportReasonCode.MfbBpGainOutOfRange,
        $"MFB BP gain-range failure remained generic: {mfbResult.Capability?.ReasonCode}.");

    var bainter = Request(FilterMode.Active, FilterResponseType.BandStop, FilterApproximation.Butterworth,
        FilterImplementationType.BainterNotch, 1_000, 16_000, optimize: true, manualOrder: 4) with
    {
        PassbandLowerHz = 1_000, StopbandLowerHz = 2_000, StopbandUpperHz = 8_000,
        PassbandUpperHz = 16_000, StopbandAttenuationDb = 30
    };
    bainter.ComponentConstraints.ResistorSeries = bainter.ComponentConstraints.CapacitorSeries = StandardComponentSeries.E24;
    var bainterResult = new FilterDesignEngine().Design(bainter);
    Require(bainterResult.Capability?.ReasonCode is FilterSupportReasonCode.BainterSelectedValueVerificationFailed or
            FilterSupportReasonCode.BainterNotchDepthInsufficientWithSelectedSeries,
        $"Bainter selected verification failure remained generic: {bainterResult.Capability?.ReasonCode}.");
    Require(new[] { mfbResult, bainterResult }.All(result => result.Capability?.ReasonCode != FilterSupportReasonCode.PendingIndependentVerification),
        "A known verified active path still returned PendingIndependentVerification.");

    var supported = new FilterDesignEngine().Design(Request(FilterMode.Passive, FilterResponseType.LowPass,
        FilterApproximation.Butterworth, FilterImplementationType.Rc, 1_000, 20_000, optimize: true, manualOrder: 1) with
        { PassbandRippleDb = 3, StopbandAttenuationDb = 20, SourceResistanceOhm = 0, LoadResistanceOhm = 1_000_000 });
    Require(supported.IsFullySupported && supported.StageDiagnostics is
        { TheoreticalCircuitValid: null, TheoreticalMeetsSpecifications: null },
        "A supported selected-value circuit did not mark unused theoretical verification as NotEvaluated.");
    var fields = Enumerable.Repeat(string.Empty, FilterManualTestRowService.ComprehensiveColumnCount).ToArray();
    var textRow = new FilterManualTestRowService().Complete(new ParsedFilterTestRow(fields, ',', supported.DesignRequest!, true), supported);
    Require(textRow.Contains("TheoryCircuit=N/A; TheoryMeets=N/A", StringComparison.Ordinal),
        "Test Row did not serialize tri-state theoretical diagnostics as N/A.");

    var linearPhase = Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.LinearPhase005Degree,
        FilterImplementationType.Lc, 50_000, 250_000, manualOrder: 10);
    var linearPhaseResult = new FilterDesignEngine().Design(linearPhase);
    Require(linearPhaseResult.Capability?.ReasonCode == FilterSupportReasonCode.LcLadderExtractionProducedNegativeElement &&
            linearPhaseResult.Message.Contains("all-positive finite LC ladder", StringComparison.Ordinal) &&
            !linearPhaseResult.Message.Contains("Bessel ladder", StringComparison.OrdinalIgnoreCase),
        "Approximation-neutral LC extraction failure text regressed.");
}

static void TestZeroOhmDefaultSource()
{
    var defaultState = new FilterDesignState { Mode = FilterMode.Passive };
    defaultState.SelectResponseType(FilterResponseType.LowPass);
    Require(defaultState.Implementation == FilterImplementationType.Lc && defaultState.SourceResistanceOhm == 50 && defaultState.LoadResistanceOhm == 50 && defaultState.LcTerminationMode == LcTerminationMode.DoublyTerminated,
        "Passive mode must default to the matched 50/50 ohm LC boundary.");
    defaultState.SelectImplementation(FilterImplementationType.Rc);
    Require(defaultState.SourceResistanceOhm == 0 && defaultState.LoadResistanceOhm == 1_000_000 && defaultState.LcTerminationMode == LcTerminationMode.IdealVoltageSource,
        "Explicit RC selection must retain the independently editable ideal-source/high-load defaults.");
    var request = Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.Butterworth,
        FilterImplementationType.Lc, 1_000, 3_000) with
        { SourceResistanceOhm = 0, LcTerminationMode = LcTerminationMode.IdealVoltageSource };
    var result = new FilterDesignEngine().Design(request);
    Require(!result.IsImplemented && result.Status == FilterDesignStatus.UnsupportedDesignCombination &&
            result.Message.Contains("doubly terminated", StringComparison.OrdinalIgnoreCase),
        "Rs=0 must not be mislabeled as a classic doubly terminated LC prototype.");
}

static void TestPassiveResponseReferenceMathematics()
{
    const double matchedAbsolute = .5;
    Require(Nearly(20 * Math.Log10(matchedAbsolute), -6.020599913, 1e-9), "50/50 direct-through absolute voltage must be -6.0206 dB.");
    Require(Nearly(PassiveResponseReferenceMath.TransducerPowerGain(matchedAbsolute, 50, 50), 1, 1e-12), "Matched direct-through transducer gain must be unity.");
    Require(Nearly(PassiveResponseReferenceMath.InsertionPowerRatioRelativeToThrough(matchedAbsolute, 50, 50), 1, 1e-12), "Matched direct-through insertion response must be 0 dB.");

    const double unequalAbsolute = .75;
    Require(Nearly(PassiveResponseReferenceMath.TransducerPowerGain(unequalAbsolute, 25, 75), .75, 1e-12), "Unequal direct-through transducer gain is incorrect.");
    Require(Nearly(PassiveResponseReferenceMath.InsertionPowerRatioRelativeToThrough(unequalAbsolute, 25, 75), 1, 1e-12), "Unequal direct-through must remain the 0 dB insertion reference.");
    var rejected = false;
    try { _ = PassiveResponseReferenceMath.TransducerPowerGain(1, 0, 1_000_000); }
    catch (InvalidOperationException) { rejected = true; }
    Require(rejected, "Rs=0 must reject available-source-power transducer gain.");
}

static void TestPassiveTerminationDefaults()
{
    var state = new FilterDesignState { Mode = FilterMode.Passive };
    state.SelectResponseType(FilterResponseType.LowPass);
    Require(state.Implementation == FilterImplementationType.Lc && state.SourceResistanceOhm == 50 && state.LoadResistanceOhm == 50 && state.LinkLcSourceAndLoad,
        "Passive mode must default to linked 50/50 ohm LC.");
    state.SelectImplementation(FilterImplementationType.Rc);
    Require(state.SourceResistanceOhm == 0 && state.LoadResistanceOhm == 1_000_000,
        "Explicitly selected passive RC must retain its Rs=0 ohm and RL=1 Mohm defaults.");
    state.SelectImplementation(FilterImplementationType.Lc);
    state.SourceResistanceOhm = 75; state.LoadResistanceOhm = 600;
    state.SelectImplementation(FilterImplementationType.Rc);
    state.SelectImplementation(FilterImplementationType.Lc);
    Require(state.SourceResistanceOhm == 75 && state.LoadResistanceOhm == 600,
        "Switching implementations must preserve independently entered LC Rs/RL values.");
}

static void TestPassiveLoadedRcEquations()
{
    foreach (var response in new[] { FilterResponseType.LowPass, FilterResponseType.HighPass })
    {
        var request = Request(FilterMode.Passive, response, FilterApproximation.ChebyshevTypeI,
            FilterImplementationType.Rc, response == FilterResponseType.LowPass ? 1_000 : 3_000,
            response == FilterResponseType.LowPass ? 3_000 : 1_000, manualOrder: 1) with
        { SourceResistanceOhm = 1_000, LoadResistanceOhm = 100_000, PassbandRippleDb = 2 };
        var result = new FilterDesignEngine().Design(request);
        Require(result.IsImplemented, $"{response}: {result.Message}");
        var section = result.Sections.Single();
        var values = section.Components.ToDictionary(value => value.Role, value => value.SelectedValue);
        var measured = PassiveRcSectionCalculator.Measure(response, request.SourceResistanceOhm,
            request.LoadResistanceOhm, values["R"], values["C"]);
        Require(Nearly(section.ActualPoleFrequencyHz, measured.FrequencyHz, 1e-12) && Nearly(section.ActualGain, measured.PassbandGain, 1e-12),
            $"{response} loaded RC solver does not use Rs/RL in its reported pole and passband gain.");
        Require(result.PassiveAnalysis?.PrimaryReference == PassiveResponseReference.AbsoluteVoltageTransfer,
            "Passive RC primary analysis must be VOUT/VSRC.");
    }
}

static void TestPassiveTwoCornerRcBandPass()
{
    var request = Request(FilterMode.Passive, FilterResponseType.BandPass, FilterApproximation.Butterworth,
        FilterImplementationType.Rc, 1_000, 10_000, optimize: true) with
    {
        SourceResistanceOhm = 0, LoadResistanceOhm = 1_000_000,
        StopbandLowerHz = 100, PassbandLowerHz = 1_000, PassbandUpperHz = 10_000, StopbandUpperHz = 100_000,
        StopbandAttenuationDb = 12, PassiveRcTopology = PassiveRcTopology.TwoCornerBandPass
    };
    var result = new FilterDesignEngine().Design(request);
    Require(result.IsImplemented, result.Message);
    var roles = result.Sections.Single().Components.Select(value => value.Role).ToHashSet();
    Require(new[] { "R_HP", "C_HP", "R_LP", "C_LP" }.All(roles.Contains), "Two-corner RC Band-Pass components are incomplete.");
    Require(result.CircuitGraph!.Components.Count == 4 && result.PassiveAnalysis?.PrimaryReference == PassiveResponseReference.AbsoluteVoltageTransfer,
        "Two-corner RC Band-Pass did not produce a loaded four-component CircuitGraph.");
}

static void TestPassiveTwinTCompoundMatching()
{
    var request = Request(FilterMode.Passive, FilterResponseType.BandStop, FilterApproximation.ChebyshevTypeI,
        FilterImplementationType.Rc, 100, 1_000, optimize: true) with
    {
        SourceResistanceOhm = 0, LoadResistanceOhm = 1_000_000,
        PassbandLowerHz = 100, StopbandLowerHz = 800, StopbandUpperHz = 1_250, PassbandUpperHz = 10_000,
        StopbandAttenuationDb = 40, PassiveRcTopology = PassiveRcTopology.TwinTNotch
    };
    var result = new FilterDesignEngine().Design(request);
    Require(result.IsImplemented, result.Message);
    Require(result.CircuitGraph!.Components.Count == 8, "Twin-T must realize R/2 and 2C with real paired components.");
    var section = result.Sections.Single();
    var values = section.Components.ToDictionary(value => value.Role, value => value.SelectedValue);
    Require(Nearly(values["R_SHUNT_A"], values["R_SHUNT_B"], 1e-12) && Nearly(values["C_SHUNT_A"], values["C_SHUNT_B"], 1e-12),
        "Twin-T compound matching pairs are not equal standard values.");
    var notch = Math.Sqrt(request.StopbandLowerHz * request.StopbandUpperHz);
    var point = result.AnalysisResult!.Points.MinBy(value => Math.Abs(Math.Log(value.FrequencyHz / notch)))!;
    Require(point.MagnitudeDb < -40, $"Twin-T actual loaded notch depth is only {point.MagnitudeDb:0.###} dB.");
}

static void TestLcTerminationPolicies()
{
    var ladder = new ChebyshevPrototypeDesigner().Design(2, 1).LcLadderPrototype!;
    var nativeRatio = ladder.LoadTerminationGN1 / ladder.SourceTerminationG0;
    Require(Math.Abs(nativeRatio - 1) > .01, "Even-order Chebyshev reference must exercise a non-unit termination ratio.");
    var basis = Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.ChebyshevTypeI,
        FilterImplementationType.Lc, 1_000, 3_000, manualOrder: 2) with { SourceResistanceOhm = 50, LoadResistanceOhm = 50 };
    var matched = new FilterDesignEngine().Design(basis with { LcTerminationPolicy = LcTerminationPolicy.MatchedUserTermination });
    Require(!matched.IsImplemented && matched.Capability?.ReasonCode == FilterSupportReasonCode.LcPrototypeRequiresNativeTermination,
        "Matched mode must reject a non-unit native Chebyshev termination ratio.");
    var native = new FilterDesignEngine().Design(basis with { LcTerminationPolicy = LcTerminationPolicy.PrototypeNativeTermination });
    Require(native.IsImplemented, native.Message);
    Require(Nearly(native.DesignRequest!.LoadResistanceOhm, 50 * nativeRatio, 1e-9),
        "Prototype-Native mode did not derive RL from gN+1/g0.");
    var customBad = new FilterDesignEngine().Design(basis with { LcTerminationPolicy = LcTerminationPolicy.CustomIndependentTermination, LoadResistanceOhm = 60 });
    Require(!customBad.IsImplemented && customBad.Capability?.ReasonCode == FilterSupportReasonCode.LcTerminationRatioUnsupported,
        "Custom unequal termination must reject an unsupported ratio.");
}

static void TestLcLadderOrientations()
{
    var basis = Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.Butterworth,
        FilterImplementationType.Lc, 1_000, 3_000, manualOrder: 3) with
    { LcTerminationPolicy = LcTerminationPolicy.MatchedUserTermination, SourceResistanceOhm = 50, LoadResistanceOhm = 50 };
    var series = new FilterDesignEngine().Design(basis with { LcLadderOrientation = LcLadderOrientation.SeriesFirst });
    var shunt = new FilterDesignEngine().Design(basis with { LcLadderOrientation = LcLadderOrientation.ShuntFirst });
    var automatic = new FilterDesignEngine().Design(basis with { LcLadderOrientation = LcLadderOrientation.Auto });
    Require(series.IsImplemented && shunt.IsImplemented && automatic.IsImplemented,
        $"LC orientation design failed: {series.Message} / {shunt.Message} / {automatic.Message}");
    Require(series.LcLadderPrototype!.StartElement == LcPrototypeConnection.Series && shunt.LcLadderPrototype!.StartElement == LcPrototypeConnection.Shunt,
        "Explicit LC ladder orientation was not retained in the normalized prototype.");
    var sfp = series.AnalysisResult!.Points.MinBy(point => Math.Abs(Math.Log(point.FrequencyHz / basis.PassbandEdgeHz)))!;
    var hfp = shunt.AnalysisResult!.Points.MinBy(point => Math.Abs(Math.Log(point.FrequencyHz / basis.PassbandEdgeHz)))!;
    Require(Math.Abs(sfp.MagnitudeDb - hfp.MagnitudeDb) < .05, "Series-first and Shunt-first loaded responses are not equivalent.");
}

static void TestLcImpedanceScaling()
{
    var results = new[] { 50d, 75d, 600d }.Select(impedance => new FilterDesignEngine().Design(
        Request(FilterMode.Passive, FilterResponseType.LowPass, FilterApproximation.Butterworth,
            FilterImplementationType.Lc, 1_000, 3_000, manualOrder: 3) with
        {
            SourceResistanceOhm = impedance, LoadResistanceOhm = impedance,
            LcTerminationPolicy = LcTerminationPolicy.MatchedUserTermination,
            LcLadderOrientation = LcLadderOrientation.SeriesFirst
        })).ToArray();
    Require(results.All(result => result.IsImplemented), "50/75/600 ohm LC scaling did not remain supported.");
    var inductors = results.Select(result => result.CircuitGraph!.Components.First(component => component.Type == ComponentType.Inductor).Parameters.GetRequiredValue(ComponentParameterNames.InductanceHenry)).ToArray();
    var capacitors = results.Select(result => result.CircuitGraph!.Components.First(component => component.Type == ComponentType.Capacitor).Parameters.GetRequiredValue(ComponentParameterNames.CapacitanceFarad)).ToArray();
    Require(Nearly(inductors[1] / inductors[0], 75d / 50, 1e-9) && Nearly(inductors[2] / inductors[0], 600d / 50, 1e-9), "LC inductors do not scale with impedance.");
    Require(Nearly(capacitors[1] / capacitors[0], 50d / 75, 1e-9) && Nearly(capacitors[2] / capacitors[0], 50d / 600, 1e-9), "LC capacitors do not scale inversely with impedance.");
}

static FilterDesignResult CreateReportDesign()
{
    var request = WideRanges(Request(FilterMode.Active, FilterResponseType.LowPass,
        FilterApproximation.Bessel, FilterImplementationType.SallenKey, 1_000, 10_000, manualOrder: 4));
    var result = new FilterDesignEngine().Design(request);
    Require(result.IsImplemented && result.Verification?.IsValid == true, result.Message);
    return result;
}

static void TestFilterDesignReportModel()
{
    var result = CreateReportDesign();
    var generated = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    var report = new FilterDesignReportBuilder().Build(result, generated);
    Require(report.GeneratedAtUtc == generated, "Report generation time is not deterministic.");
    Require(report.DesignSummary.Any(field => field.Name == "Design status" && field.Value == "Success"), "Report summary does not expose Design status.");
    Require(report.DesignSummary.Any(field => field.Name == "Capability level" && field.Value == "FinalCircuitVerification"), "Report summary does not expose CapabilityLevel.");
    Require(report.DesignSummary.Any(field => field.Name == "Capability reason" && field.Value == "Supported"), "Report summary does not expose CapabilityReasonCode.");
    Require(report.DesignSummary.Any(field => field.Name == "Approximation" && field.Value == "Bessel"), "Report summary lost the approximation.");
    Require(report.ApproximationDetails.Any(field => field.Name == "Normalization" && field.Value.Contains("3 dB", StringComparison.Ordinal)), "Report omitted Bessel normalization.");
    Require(report.PrototypePolesAndZeros.Count(value => !value.IsZero) == result.PrototypeOrder, "Report pole table is incomplete.");
    Require(report.Sections.Count == result.Sections.Count && report.Components.Count > 0, "Report section/component design is incomplete.");
    Require(report.Verification.Any(row => row.Metric == "Passband gain A0"), "Report does not include actual overall-gain verification.");
    Require(report.CircuitSummary.OpAmps == result.CircuitGraph!.Components.Count(component => component.Type == ComponentType.OpAmp), "Report circuit summary did not use the final CircuitGraph.");
    Require(report.CalculationTrace.Count > 3, "Engineering calculation trace is missing.");
    Require(report.Snapshot.Request["PassbandEdgeHz"] is double value && Nearly(value, 1_000, 1e-12), "Snapshot does not preserve normalized SI-unit input.");
    var rejected = false;
    try { _ = new FilterDesignReportBuilder().Build(new FilterDesignResult { Status = FilterDesignStatus.SynthesisFailure }); }
    catch (InvalidOperationException) { rejected = true; }
    Require(rejected, "Report builder accepted an unsuccessful/unverified design.");
}

static void TestFilterDesignReportExport()
{
    var report = new FilterDesignReportBuilder().Build(CreateReportDesign(),
        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)) with
    {
        Warnings = ["Unsafe <tag> & quoted \"value\""]
    };
    var html = new HtmlFilterReportExporter().Export(report);
    foreach (var heading in new[] { "1. Design Summary", "2. Input Specifications", "3. Approximation Details",
        "4. Section Decomposition", "5. Component Design", "6. Gain Distribution", "7. Verification Results",
        "8. Warnings and Design Notes", "9. Circuit Summary", "Calculation Trace / Engineering Explanation",
        "10. Design Reproducibility" })
        Require(html.Contains(heading, StringComparison.Ordinal), $"HTML report is missing {heading}.");
    Require(html.StartsWith("<!DOCTYPE html>", StringComparison.Ordinal), "Exporter did not produce a complete HTML document.");
    Require(html.Contains("Unsafe &lt;tag&gt; &amp; quoted &quot;value&quot;", StringComparison.Ordinal), "HTML report did not escape user-visible text.");
    Require(!html.Contains("http://", StringComparison.OrdinalIgnoreCase) && !html.Contains("https://", StringComparison.OrdinalIgnoreCase), "Offline report depends on a network resource.");
    var json = new JsonFilterDesignSnapshotExporter().Export(report.Snapshot);
    using var document = System.Text.Json.JsonDocument.Parse(json);
    Require(document.RootElement.GetProperty("schemaVersion").GetString() == "circuit-module-designer.filter-design/1", "JSON snapshot schema is missing.");
    Require(document.RootElement.GetProperty("components").GetArrayLength() == report.Components.Count, "JSON snapshot lost selected components.");
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

static void TestBoundedE24CascadeOptimization()
{
    var lowPass = Request(FilterMode.Active, FilterResponseType.LowPass, FilterApproximation.ChebyshevTypeI,
        FilterImplementationType.MultipleFeedback, 2_000, 8_000, optimize: true) with
    {
        PassbandRippleDb = .5,
        StopbandAttenuationDb = 45
    };
    lowPass.ComponentConstraints.CapacitorMinimumF = 100e-12;
    lowPass.ComponentConstraints.CapacitorMaximumF = 1e-6;

    var bandPass = Request(FilterMode.Active, FilterResponseType.BandPass, FilterApproximation.Butterworth,
        FilterImplementationType.SallenKey, 1_000, 4_000, optimize: true) with
    {
        StopbandLowerHz = 500,
        PassbandLowerHz = 1_000,
        PassbandUpperHz = 2_000,
        StopbandUpperHz = 4_000
    };
    bandPass.ComponentConstraints.CapacitorMinimumF = 100e-12;
    bandPass.ComponentConstraints.CapacitorMaximumF = 1e-6;

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var lowPassResult = new FilterDesignEngine().Design(lowPass);
    Require(lowPassResult.IsFullySupported, $"First-period point 3 must pass E24 cascade optimization: {lowPassResult.Message}");
    var bandPassResult = new FilterDesignEngine().Design(bandPass);
    stopwatch.Stop();
    Require(bandPassResult.IsFullySupported, $"First-period point 13 must pass E24 cascade optimization: {bandPassResult.Message}");
    Require(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Bounded E24 cascade optimization took {stopwatch.Elapsed.TotalSeconds:0.###} s.");
}

static void TestBainterK1RangeFreedomAndAvailability()
{
    var request = Request(FilterMode.Active, FilterResponseType.BandStop, FilterApproximation.Butterworth,
        FilterImplementationType.BainterNotch, 1_000, 2_000, optimize: true) with
    {
        PassbandRippleDb = 1,
        StopbandAttenuationDb = 40,
        PassbandLowerHz = 1_000,
        StopbandLowerHz = 2_000,
        StopbandUpperHz = 4_000,
        PassbandUpperHz = 8_000
    };
    request.ComponentConstraints.ResistorSeries = StandardComponentSeries.E96;
    request.ComponentConstraints.CapacitorSeries = StandardComponentSeries.E96;
    request.ComponentConstraints.ResistorMinimumOhm = 1_000;
    request.ComponentConstraints.ResistorMaximumOhm = 100_000;
    request.ComponentConstraints.CapacitorMinimumF = 100e-12;
    request.ComponentConstraints.CapacitorMaximumF = 1e-6;

    var engine = new FilterDesignEngine();
    _ = engine.GetAvailability(request); // JIT warm-up is not part of the UI latency assertion.
    var availabilityWatch = System.Diagnostics.Stopwatch.StartNew();
    var availability = engine.GetAvailability(request);
    availabilityWatch.Stop();
    Require(availability.CanDesign, availability.Message);
    Require(availabilityWatch.Elapsed < TimeSpan.FromMilliseconds(500),
        $"Capability lookup blocked for {availabilityWatch.Elapsed.TotalMilliseconds:0.###} ms.");

    var result = engine.Design(request);
    Require(result.IsFullySupported, $"E96 Bainter range-freedom regression failed: {result.Message}");
    Require(result.Sections.Any(section =>
    {
        var values = section.Components.ToDictionary(component => component.Role, component => component.SelectedValue);
        return Math.Abs(values["R2"] / values["R1"] - 1) > .01;
    }), "Bainter synthesis did not exercise the K1=R2/R1 range-balancing freedom.");
}

static FilterDesignRequest Request(FilterMode mode, FilterResponseType response, FilterApproximation approximation, FilterImplementationType implementation, double fp, double fs, bool optimize = false, int? manualOrder = null) => new()
{
    Mode = mode, ResponseType = response, Approximation = approximation, Implementation = implementation, SignalArchitecture = FilterSignalArchitecture.SingleEnded, OrderMode = manualOrder.HasValue ? FilterOrderMode.SpecifyOrder : FilterOrderMode.CalculateAutomatically, SpecifiedOrder = manualOrder ?? 2, PassbandRippleDb = 1, StopbandAttenuationDb = 40, PassbandGain = 1, PassbandEdgeHz = fp, StopbandEdgeHz = fs,
    StopbandLowerHz = response == FilterResponseType.BandStop ? 900 : 500, PassbandLowerHz = response == FilterResponseType.BandStop ? 500 : 900, PassbandUpperHz = response == FilterResponseType.BandStop ? 1_500 : 1_100, StopbandUpperHz = response == FilterResponseType.BandStop ? 1_100 : 1_500,
    SourceResistanceOhm = implementation == FilterImplementationType.Rc ? 0 : 50,
    LoadResistanceOhm = implementation == FilterImplementationType.Rc ? 1_000_000 : 50,
    LcTerminationPolicy = implementation == FilterImplementationType.Lc ? LcTerminationPolicy.PrototypeNativeTermination : LcTerminationPolicy.MatchedUserTermination,
    PreferredResistanceOhm = 10_000,
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
