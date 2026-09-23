using CircuitModuleDesigner.Core.Circuit;
using CircuitModuleDesigner.Modules.Comparators.Models;
using CircuitModuleDesigner.Modules.Comparators.Validation;
using CircuitModuleDesigner.Modules.Filters.FilterDesign.Synthesis;

namespace CircuitModuleDesigner.Modules.Comparators.Engines;

public sealed class ComparatorDesignEngine
{
    private const double NominalResistance = 10_000;

    public ComparatorDesignResult Design(ComparatorDesignRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = ComparatorRequestValidator.Validate(request);
        if (errors.Count > 0) return Failed(request, string.Join(" ", errors));
        try
        {
            var synthesized = request.Kind == ComparatorDesignerKind.SchmittTrigger
                ? SynthesizeSchmitt(request) : SynthesizeWindow(request);
            var verification = new ComparatorCircuitVerifier().Verify(synthesized);
            var hardFailure = verification.Any(metric => !metric.Pass);
            var warnings = synthesized.Warnings.ToList();
            var supplySpan = request.SupplyHighVoltage - request.SupplyLowVoltage;
            if (verification.Any(metric => Math.Abs(metric.ErrorVoltage) > supplySpan * .01))
                warnings.Add("One or more selected-value threshold errors exceed 1% of the supply span.");
            if (request.Kind == ComparatorDesignerKind.SchmittTrigger &&
                request.UpperThresholdVoltage - request.LowerThresholdVoltage < .01 * supplySpan)
                warnings.Add(request.UpperThresholdVoltage == request.LowerThresholdVoltage
                    ? "Zero hysteresis is supported, but it provides no hysteresis noise margin."
                    : "The requested hysteresis is less than 1% of the supply span; check the available noise margin.");
            if (request.Kind == ComparatorDesignerKind.WindowComparator &&
                new[] { request.LowerBoundaryHysteresisVoltage, request.UpperBoundaryHysteresisVoltage }.Any(value => value > 0 && value < .01 * supplySpan))
                warnings.Add("A nonzero boundary hysteresis is less than 1% of the supply span; check the available noise margin.");
            var status = hardFailure ? ComparatorDesignStatus.Failed : warnings.Count > 0 ? ComparatorDesignStatus.Warning : ComparatorDesignStatus.Pass;
            return Copy(synthesized, status, hardFailure ? "Independent CircuitGraph threshold verification failed." : status == ComparatorDesignStatus.Pass ? "PASS" : "WARNING", verification, warnings);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return Failed(request, "Unable to synthesize this threshold network: " + exception.Message);
        }
    }

    private static ComparatorDesignResult SynthesizeSchmitt(ComparatorDesignRequest request)
    {
        var span = request.OutputHighVoltage - request.OutputLowVoltage;
        var hysteresis = request.UpperThresholdVoltage - request.LowerThresholdVoltage;
        if (hysteresis == 0) return SynthesizeZeroHysteresisComparator(request);
        var continuous = new Dictionary<string, double>();
        double reference;
        if (request.Topology == SchmittTopology.Inverting)
        {
            var alpha = hysteresis / span;
            reference = (request.LowerThresholdVoltage - alpha * request.OutputLowVoltage) / (1 - alpha);
            var feedbackFactor = (1 - alpha) / alpha;
            var scale = SelectScale(request, 1, feedbackFactor);
            continuous["RREF"] = scale;
            continuous["RFB"] = scale * feedbackFactor;
        }
        else
        {
            var ratio = hysteresis / span;
            reference = (request.UpperThresholdVoltage + ratio * request.OutputLowVoltage) / (1 + ratio);
            var scale = SelectScale(request, ratio, 1);
            continuous["RIN"] = scale * ratio;
            continuous["RFB"] = scale;
        }
        if (!(reference >= request.SupplyLowVoltage && reference <= request.SupplyHighVoltage))
            throw new InvalidOperationException($"The required bias-node voltage ({reference:G6} V) lies outside VEE..VCC.");

        var railFraction = (reference - request.SupplyLowVoltage) / (request.SupplyHighVoltage - request.SupplyLowVoltage);
        if (!(railFraction > 0 && railFraction < 1))
            throw new InvalidOperationException("The required bias-node voltage lies on a supply rail and cannot be synthesized by a finite two-resistor rail divider.");

        var firstName = request.Topology == SchmittTopology.Inverting ? "RREF" : "RIN";
        const double DividerImpedanceFraction = 0.2;
        if (request.Topology == SchmittTopology.Inverting)
        {
            var feedbackFactor = continuous["RFB"] / continuous[firstName];
            var totalReferenceResistance = SelectScale(request,
                1 - DividerImpedanceFraction,
                feedbackFactor,
                DividerImpedanceFraction / (1 - railFraction),
                DividerImpedanceFraction / railFraction);
            var dividerThevenin = totalReferenceResistance * DividerImpedanceFraction;
            continuous[firstName] = totalReferenceResistance - dividerThevenin;
            continuous["RFB"] = totalReferenceResistance * feedbackFactor;
            continuous["RVT"] = dividerThevenin / railFraction;
            continuous["RVB"] = dividerThevenin / (1 - railFraction);
        }
        else
        {
            var inputRatio = continuous[firstName] / continuous["RFB"];
            var dividerThevenin = SelectScale(request, inputRatio, 1, 1 / (1 - railFraction), 1 / railFraction);
            continuous[firstName] = dividerThevenin * inputRatio;
            continuous["RFB"] = dividerThevenin;
            continuous["RVT"] = dividerThevenin / railFraction;
            continuous["RVB"] = dividerThevenin / (1 - railFraction);
        }

        var first = Nearby(continuous[firstName], request); var feedback = Nearby(continuous["RFB"], request);
        var dividerTop = Nearby(continuous["RVT"], request); var dividerBottom = Nearby(continuous["RVB"], request);
        var best = (from a in first from b in feedback from rt in dividerTop from rb in dividerBottom
                    let actualReference = RailDividerVoltage(request, rt, rb)
                    let thresholds = SchmittThresholds(request, actualReference, a, b, Parallel(rt, rb))
                    let score = ThresholdScore(request.LowerThresholdVoltage, request.UpperThresholdVoltage, thresholds.Lower, thresholds.Upper)
                    orderby score,
                        Math.Abs(Math.Log(a / continuous[firstName])) + Math.Abs(Math.Log(b / continuous["RFB"])) +
                        Math.Abs(Math.Log(rt / continuous["RVT"])) + Math.Abs(Math.Log(rb / continuous["RVB"]))
                    select (a, b, rt, rb, actualReference, thresholds, score)).First();

        var selected = new[]
        {
            new ComparatorSelectedResistor(firstName, continuous[firstName], best.a),
            new ComparatorSelectedResistor("RFB", continuous["RFB"], best.b),
            new ComparatorSelectedResistor("RVT", continuous["RVT"], best.rt),
            new ComparatorSelectedResistor("RVB", continuous["RVB"], best.rb)
        };
        var graph = BuildSchmittGraph(request, selected);
        var trace = new List<string>
        {
            $"Target thresholds: VTL={request.LowerThresholdVoltage:G6} V, VTH={request.UpperThresholdVoltage:G6} V; H={hysteresis:G6} V.",
            request.Topology == SchmittTopology.Inverting
                ? $"Continuous network: alpha=H/(VOH-VOL)={hysteresis:G6}/{span:G6}={hysteresis/span:G8}; required rail-divider bias={(reference):G8} V."
                : $"Continuous network: k=RIN/RFB=H/(VOH-VOL)={hysteresis/span:G8}; required rail-divider bias={(reference):G8} V.",
            $"Continuous resistors: {string.Join(", ", selected.Select(item => $"{item.Reference}={item.ContinuousOhm:G8} ohm"))}.",
            $"Rail-derived bias divider: VCC--RVT--NBIAS--RVB--{(request.SupplyLowVoltage == 0 ? "GND" : "VEE")}; continuous bias={reference:G8} V.",
            request.OptimizeStandardValues
                ? $"Coupled {request.ResistorSeries} search evaluated {first.Count * feedback.Count * dividerTop.Count * dividerBottom.Count} four-resistor sets; score jointly minimized VTL/VTH and hysteresis error (not independent nearest rounding)."
                : "Standard-value optimization disabled; the continuous resistor solution is used directly.",
            $"Selected: {string.Join(", ", selected.Select(item => $"{item.Reference}={item.SelectedOhm:G8} ohm"))}; score={best.score:G8}.",
            $"Recomputed from selected rail divider and feedback network: bias={best.actualReference:G8} V, VTL={best.thresholds.Lower:G8} V, VTH={best.thresholds.Upper:G8} V, H={best.thresholds.Width:G8} V, center={best.thresholds.Center:G8} V."
        };
        return new ComparatorDesignResult { Request = request, Status = ComparatorDesignStatus.Pass, Message = "Synthesized", CircuitGraph = graph, ActualThresholds = best.thresholds, BiasNodeVoltage = best.actualReference, Resistors = selected, CalculationTrace = trace };
    }

    private static ComparatorDesignResult SynthesizeZeroHysteresisComparator(ComparatorDesignRequest request)
    {
        var threshold = request.LowerThresholdVoltage;
        var railFraction = (threshold - request.SupplyLowVoltage) / (request.SupplyHighVoltage - request.SupplyLowVoltage);
        if (!(railFraction > 0 && railFraction < 1))
            throw new InvalidOperationException("A zero-hysteresis threshold on a supply rail cannot be synthesized by a finite two-resistor rail divider.");

        var scale = SelectScale(request, 1 - railFraction, railFraction);
        var topTheory = scale * (1 - railFraction);
        var bottomTheory = scale * railFraction;
        // A divider ratio is invariant under common impedance scaling. Search the
        // complete bounded series here so an exact ratio at another decade is not
        // missed merely because it is not near the nominal continuous scale.
        var topCandidates = request.OptimizeStandardValues
            ? StandardValueSeries.GetNearby(topTheory, request.ResistorSeries, request.MinimumResistanceOhm, request.MaximumResistanceOhm, 4096)
            : [topTheory];
        var bottomCandidates = request.OptimizeStandardValues
            ? StandardValueSeries.GetNearby(bottomTheory, request.ResistorSeries, request.MinimumResistanceOhm, request.MaximumResistanceOhm, 4096)
            : [bottomTheory];
        var best = (from top in topCandidates
                    from bottom in bottomCandidates
                    let actual = RailDividerVoltage(request, top, bottom)
                    orderby Math.Abs(actual - threshold),
                        Math.Abs(Math.Log(top / topTheory)) + Math.Abs(Math.Log(bottom / bottomTheory))
                    select (top, bottom, actual)).First();
        var selected = new[]
        {
            new ComparatorSelectedResistor("RVT", topTheory, best.top),
            new ComparatorSelectedResistor("RVB", bottomTheory, best.bottom)
        };
        var graph = BuildZeroHysteresisGraph(request, selected);
        var actualThresholds = new ComparatorThresholds(best.actual, best.actual);
        var trace = new List<string>
        {
            $"Zero-hysteresis comparator: VTL=VTH={threshold:G8} V.",
            "No positive-feedback resistor is used; the switching threshold is derived only from the finite VCC/VEE rail divider.",
            $"Continuous divider: RVT={topTheory:G8} ohm, RVB={bottomTheory:G8} ohm.",
            request.OptimizeStandardValues
                ? $"Coupled {request.ResistorSeries} divider search evaluated {topCandidates.Count * bottomCandidates.Count} pairs."
                : "Standard-value optimization disabled; the continuous divider values are used directly.",
            $"Selected divider: RVT={best.top:G8} ohm, RVB={best.bottom:G8} ohm; recomputed threshold={best.actual:G8} V."
        };
        return new ComparatorDesignResult
        {
            Request = request, Status = ComparatorDesignStatus.Pass, Message = "Synthesized",
            CircuitGraph = graph, ActualThresholds = actualThresholds, BiasNodeVoltage = best.actual,
            Resistors = selected, CalculationTrace = trace
        };
    }

    private static ComparatorDesignResult SynthesizeWindow(ComparatorDesignRequest request)
    {
        var lowerTargets = (Falling: request.LowerThresholdVoltage - request.LowerBoundaryHysteresisVoltage / 2, Rising: request.LowerThresholdVoltage + request.LowerBoundaryHysteresisVoltage / 2);
        var upperTargets = (Falling: request.UpperThresholdVoltage - request.UpperBoundaryHysteresisVoltage / 2, Rising: request.UpperThresholdVoltage + request.UpperBoundaryHysteresisVoltage / 2);
        var lower = BoundaryCandidates("L", lowerTargets.Falling, lowerTargets.Rising, request);
        var upper = BoundaryCandidates("U", upperTargets.Falling, upperTargets.Rising, request);
        var best = (from l in lower.Take(16) from u in upper.Take(16)
                    let widthError = Math.Abs((u.Rising - l.Rising) - (upperTargets.Rising - lowerTargets.Rising)) + Math.Abs((u.Falling - l.Falling) - (upperTargets.Falling - lowerTargets.Falling))
                    let score = l.Score + u.Score + widthError / Math.Max(1e-9, request.UpperThresholdVoltage - request.LowerThresholdVoltage)
                    where l.Rising < u.Rising && l.Falling < u.Falling
                    orderby score select (l, u, score)).FirstOrDefault();
        if (best.l is null) throw new InvalidOperationException("No ordered E-series window candidate exists in the selected resistance range.");
        var boundaryResistors = best.l.Resistors.Concat(best.u.Resistors).ToArray();
        var combinerResistors = SynthesizeWindowCombiner(request);
        var selected = boundaryResistors.Concat(combinerResistors).ToArray();
        var thresholds = new WindowBoundaryThresholds(best.l.Falling, best.l.Rising, best.u.Falling, best.u.Rising);
        var graph = BuildWindowGraph(request, selected);
        var targetWidth = request.UpperThresholdVoltage - request.LowerThresholdVoltage;
        var trace = new List<string>
        {
            $"Target window: lower={request.LowerThresholdVoltage:G6} V, upper={request.UpperThresholdVoltage:G6} V, width={targetWidth:G6} V.",
            $"Boundary targets: lower fall/rise={lowerTargets.Falling:G6}/{lowerTargets.Rising:G6} V; upper fall/rise={upperTargets.Falling:G6}/{upperTargets.Rising:G6} V.",
            "Each reference node is solved as (VCC/Rtop + VEE/Rbottom + Vout/Rfb) / (1/Rtop + 1/Rbottom + 1/Rfb); Rfb is omitted for ideal zero hysteresis.",
            $"Continuous lower: {string.Join(", ", best.l.Resistors.Select(item => $"{item.Reference}={item.ContinuousOhm:G8} ohm"))}.",
            $"Continuous upper: {string.Join(", ", best.u.Resistors.Select(item => $"{item.Reference}={item.ContinuousOhm:G8} ohm"))}.",
            request.OptimizeStandardValues
                ? $"Coupled {request.ResistorSeries} candidate search retained the best boundary sets, then jointly minimized four threshold errors and rising/falling window-width error; score={best.score:G8}."
                : "Standard-value optimization disabled; continuous boundary and combiner resistor values are used directly.",
            $"Output combiner uses saturating op-amp UOUT with matched signal resistors and rail-bias resistors; mode={request.OutputMode}.",
            $"Selected: {string.Join(", ", selected.Select(item => $"{item.Reference}={item.SelectedOhm:G8} ohm"))}.",
            $"Recomputed: lower fall/rise={thresholds.LowerFalling:G8}/{thresholds.LowerRising:G8} V; upper fall/rise={thresholds.UpperFalling:G8}/{thresholds.UpperRising:G8} V."
        };
        var actual = new ComparatorThresholds((thresholds.LowerFalling + thresholds.LowerRising) / 2, (thresholds.UpperFalling + thresholds.UpperRising) / 2);
        return new ComparatorDesignResult { Request = request, Status = ComparatorDesignStatus.Pass, Message = "Synthesized", CircuitGraph = graph, ActualThresholds = actual, WindowThresholds = thresholds, Resistors = selected, CalculationTrace = trace };
    }

    private static IReadOnlyList<ComparatorSelectedResistor> SynthesizeWindowCombiner(ComparatorDesignRequest request)
    {
        var supplySpan = request.SupplyHighVoltage - request.SupplyLowVoltage;
        var outputSpan = request.OutputHighVoltage - request.OutputLowVoltage;
        var biasRatio = 2 * supplySpan / outputSpan;
        var scale = SelectScale(request, 1, biasRatio);
        var signalCandidates = Nearby(scale, request);
        var biasCandidates = Nearby(scale * biasRatio, request);
        var best = (from signal in signalCandidates from bias in biasCandidates
                    let ratioError = Math.Abs(bias / signal - biasRatio)
                    orderby ratioError
                    select (signal, bias)).First();
        return
        [
            new("RPS", scale, best.signal),
            new("RMS", scale, best.signal),
            new("RPB", scale * biasRatio, best.bias),
            new("RMB", scale * biasRatio, best.bias)
        ];
    }

    private static IReadOnlyList<BoundaryCandidate> BoundaryCandidates(string suffix, double falling, double rising, ComparatorDesignRequest request)
    {
        var alpha = (rising - falling) / (request.OutputHighVoltage - request.OutputLowVoltage);
        var centerBase = (falling + rising) / 2 - alpha * (request.OutputHighVoltage + request.OutputLowVoltage) / 2;
        var beta = (centerBase - (1 - alpha) * request.SupplyLowVoltage) / (request.SupplyHighVoltage - request.SupplyLowVoltage);
        var gamma = 1 - alpha - beta;
        if (beta <= 0 || gamma <= 0 || alpha < 0) throw new InvalidOperationException($"Boundary {suffix} requires an unrealizable rail/reference conductance ratio.");
        var coefficients = alpha > 1e-12 ? new[] { 1 / beta, 1 / gamma, 1 / alpha } : new[] { 1 / beta, 1 / gamma };
        var scale = SelectScale(request, coefficients);
        var totalConductance = 1 / scale;
        var continuous = new Dictionary<string, double>
        {
            [$"RT{suffix}"] = 1 / (beta * totalConductance),
            [$"RB{suffix}"] = 1 / (gamma * totalConductance)
        };
        if (alpha > 1e-12) continuous[$"RF{suffix}"] = 1 / (alpha * totalConductance);
        var top = Nearby(continuous[$"RT{suffix}"], request); var bottom = Nearby(continuous[$"RB{suffix}"], request);
        var feedback = alpha > 1e-12 ? Nearby(continuous[$"RF{suffix}"], request) : new[] { double.PositiveInfinity };
        return (from rt in top from rb in bottom from rf in feedback
                let actual = BoundaryThreshold(request, rt, rb, rf)
                let score = ThresholdScore(falling, rising, actual.Falling, actual.Rising)
                orderby score
                select new BoundaryCandidate(actual.Falling, actual.Rising, score,
                    new[] { new ComparatorSelectedResistor($"RT{suffix}", continuous[$"RT{suffix}"], rt), new ComparatorSelectedResistor($"RB{suffix}", continuous[$"RB{suffix}"], rb) }
                    .Concat(alpha > 1e-12 ? [new ComparatorSelectedResistor($"RF{suffix}", continuous[$"RF{suffix}"], rf)] : []).ToArray())).Take(32).ToArray();
    }

    private static (double Falling, double Rising) BoundaryThreshold(ComparatorDesignRequest r, double rt, double rb, double rf)
    {
        var gt = 1 / rt; var gb = 1 / rb; var gf = double.IsPositiveInfinity(rf) ? 0 : 1 / rf; var sum = gt + gb + gf;
        var fixedPart = (gt * r.SupplyHighVoltage + gb * r.SupplyLowVoltage) / sum;
        return (fixedPart + gf * r.OutputLowVoltage / sum, fixedPart + gf * r.OutputHighVoltage / sum);
    }

    private static ComparatorThresholds SchmittThresholds(
        ComparatorDesignRequest r,
        double reference,
        double first,
        double feedback,
        double dividerTheveninResistance)
    {
        if (r.Topology == SchmittTopology.Inverting)
        {
            var railReferenceBranch = first + dividerTheveninResistance;
            double Threshold(double output) => (reference / railReferenceBranch + output / feedback) / (1 / railReferenceBranch + 1 / feedback);
            return new(Threshold(r.OutputLowVoltage), Threshold(r.OutputHighVoltage));
        }
        var ratio = first / feedback;
        return new(reference * (1 + ratio) - ratio * r.OutputHighVoltage, reference * (1 + ratio) - ratio * r.OutputLowVoltage);
    }

    private static double RailDividerVoltage(ComparatorDesignRequest request, double top, double bottom) =>
        (request.SupplyHighVoltage / top + request.SupplyLowVoltage / bottom) / (1 / top + 1 / bottom);

    private static double Parallel(double first, double second) => 1 / (1 / first + 1 / second);

    private static double ThresholdScore(double targetLow, double targetHigh, double actualLow, double actualHigh)
    {
        var scale = Math.Max(1e-9, targetHigh - targetLow);
        var threshold = (Math.Abs(actualLow - targetLow) + Math.Abs(actualHigh - targetHigh)) / scale;
        var width = Math.Abs((actualHigh - actualLow) - scale) / scale;
        return threshold + width;
    }

    private static IReadOnlyList<double> Nearby(double target, ComparatorDesignRequest request)
    {
        if (target < request.MinimumResistanceOhm || target > request.MaximumResistanceOhm)
            throw new InvalidOperationException($"Continuous resistance {target:G6} ohm is outside {request.MinimumResistanceOhm:G6}..{request.MaximumResistanceOhm:G6} ohm.");
        if (!request.OptimizeStandardValues) return [target];
        return StandardValueSeries.GetNearby(target, request.ResistorSeries, request.MinimumResistanceOhm, request.MaximumResistanceOhm, 4);
    }

    private static double SelectScale(ComparatorDesignRequest request, params double[] coefficients)
    {
        if (coefficients.Any(value => !(value > 0) || !double.IsFinite(value))) throw new InvalidOperationException("The required resistor ratio is not finite and positive.");
        var lower = coefficients.Max(value => request.MinimumResistanceOhm / value);
        var upper = coefficients.Min(value => request.MaximumResistanceOhm / value);
        if (lower > upper) throw new InvalidOperationException("The required resistor ratios cannot fit simultaneously inside the selected resistance range.");
        return Math.Clamp(NominalResistance, lower, upper);
    }

    private static CircuitGraph BuildSchmittGraph(ComparatorDesignRequest r, IReadOnlyList<ComparatorSelectedResistor> values)
    {
        var b = new CircuitBuilder(); var u = b.Add(ComponentFactory.Comparator("U1", new GenericComparatorModel(r.OutputLowVoltage, r.OutputHighVoltage, r.OutputBehavior).Validate()));
        var rf = b.Add(ComponentFactory.Resistor("RFB", values.Single(x => x.Reference == "RFB").SelectedOhm));
        var rvt = b.Add(ComponentFactory.Resistor("RVT", values.Single(x => x.Reference == "RVT").SelectedOhm));
        var rvb = b.Add(ComponentFactory.Resistor("RVB", values.Single(x => x.Reference == "RVB").SelectedOhm));
        var lowRail = r.SupplyLowVoltage == 0 ? "GND" : "VEE";
        b.Connect("VCC", rvt.GetPin("1")); b.Connect("NBIAS", rvt.GetPin("2"), rvb.GetPin("1")); b.Connect(lowRail, rvb.GetPin("2"));
        if (r.Topology == SchmittTopology.Inverting)
        {
            var rr = b.Add(ComponentFactory.Resistor("RREF", values.Single(x => x.Reference == "RREF").SelectedOhm));
            b.Connect("VIN", u.GetPin("IN-")); b.Connect("NTH", u.GetPin("IN+"), rr.GetPin("2"), rf.GetPin("2"));
            b.Connect("NBIAS", rr.GetPin("1")); b.Connect("VOUT", u.GetPin("OUT"), rf.GetPin("1"));
        }
        else
        {
            var ri = b.Add(ComponentFactory.Resistor("RIN", values.Single(x => x.Reference == "RIN").SelectedOhm));
            b.Connect("VIN", ri.GetPin("1")); b.Connect("NPOS", ri.GetPin("2"), rf.GetPin("2"), u.GetPin("IN+"));
            b.Connect("NBIAS", u.GetPin("IN-")); b.Connect("VOUT", u.GetPin("OUT"), rf.GetPin("1"));
        }
        b.Connect("VCC", u.GetPin("V+")); b.Connect(lowRail, u.GetPin("V-"));
        return b.Build();
    }

    private static CircuitGraph BuildZeroHysteresisGraph(ComparatorDesignRequest r, IReadOnlyList<ComparatorSelectedResistor> values)
    {
        var b = new CircuitBuilder();
        var u = b.Add(ComponentFactory.Comparator("U1", new GenericComparatorModel(r.OutputLowVoltage, r.OutputHighVoltage, r.OutputBehavior).Validate()));
        var rvt = b.Add(ComponentFactory.Resistor("RVT", values.Single(x => x.Reference == "RVT").SelectedOhm));
        var rvb = b.Add(ComponentFactory.Resistor("RVB", values.Single(x => x.Reference == "RVB").SelectedOhm));
        var lowRail = r.SupplyLowVoltage == 0 ? "GND" : "VEE";
        b.Connect("VCC", rvt.GetPin("1"));
        b.Connect("NBIAS", rvt.GetPin("2"), rvb.GetPin("1"));
        b.Connect(lowRail, rvb.GetPin("2"));
        if (r.Topology == SchmittTopology.Inverting)
            b.Connect("VIN", u.GetPin("IN-"));
        else
            b.Connect("VIN", u.GetPin("IN+"));
        b.Connect("NBIAS", r.Topology == SchmittTopology.Inverting ? u.GetPin("IN+") : u.GetPin("IN-"));
        b.Connect("VOUT", u.GetPin("OUT"));
        b.Connect("VCC", u.GetPin("V+"));
        b.Connect(lowRail, u.GetPin("V-"));
        return b.Build();
    }

    private static CircuitGraph BuildWindowGraph(ComparatorDesignRequest r, IReadOnlyList<ComparatorSelectedResistor> values)
    {
        var b = new CircuitBuilder();
        var low = b.Add(ComponentFactory.Comparator("ULOW", new GenericComparatorModel(r.OutputLowVoltage, r.OutputHighVoltage, r.OutputBehavior).Validate()));
        var high = b.Add(ComponentFactory.Comparator("UHIGH", new GenericComparatorModel(r.OutputLowVoltage, r.OutputHighVoltage, r.OutputBehavior).Validate()));
        var output = b.Add(ComponentFactory.SaturatingOpAmp("UOUT", r.OutputLowVoltage, r.OutputHighVoltage));
        var rps = Add("RPS"); var rms = Add("RMS"); var rpb = Add("RPB"); var rmb = Add("RMB");
        var lowRail = r.SupplyLowVoltage == 0 ? "GND" : "VEE";
        AddBoundary("L", low, "NLOW", "NREF_L"); AddBoundary("U", high, "NHIGH", "NREF_U");
        b.Connect("VIN", low.GetPin("IN-"), high.GetPin("IN-"));
        b.Connect("NLOW", low.GetPin("OUT")); b.Connect("NHIGH", high.GetPin("OUT"));
        if (r.OutputMode == WindowOutputMode.InsideWindowHigh)
        {
            b.Connect("NHIGH", rps.GetPin("1")); b.Connect("NLOW", rms.GetPin("1"));
            b.Connect(lowRail, rpb.GetPin("1")); b.Connect("VCC", rmb.GetPin("1"));
        }
        else
        {
            b.Connect("NLOW", rps.GetPin("1")); b.Connect("NHIGH", rms.GetPin("1"));
            b.Connect("VCC", rpb.GetPin("1")); b.Connect(lowRail, rmb.GetPin("1"));
        }
        b.Connect("NCOMB_P", rps.GetPin("2"), rpb.GetPin("2"), output.GetPin("IN+"));
        b.Connect("NCOMB_M", rms.GetPin("2"), rmb.GetPin("2"), output.GetPin("IN-"));
        b.Connect("VOUT", output.GetPin("OUT"));
        b.Connect("VCC", low.GetPin("V+"), high.GetPin("V+"), output.GetPin("V+"));
        b.Connect(lowRail, low.GetPin("V-"), high.GetPin("V-"), output.GetPin("V-"));
        return b.Build();

        void AddBoundary(string suffix, CircuitComponent comparator, string output, string node)
        {
            var rt = Add($"RT{suffix}"); var rb = Add($"RB{suffix}");
            b.Connect(node, comparator.GetPin("IN+"), rt.GetPin("2"), rb.GetPin("1")); b.Connect("VCC", rt.GetPin("1")); b.Connect(r.SupplyLowVoltage == 0 ? "GND" : "VEE", rb.GetPin("2"));
            var feedbackValue = values.FirstOrDefault(x => x.Reference == $"RF{suffix}");
            if (feedbackValue is not null) { var rf = b.Add(ComponentFactory.Resistor(feedbackValue.Reference, feedbackValue.SelectedOhm)); b.Connect(node, rf.GetPin("2")); b.Connect(output, rf.GetPin("1")); }
        }
        CircuitComponent Add(string reference) { var value = values.Single(x => x.Reference == reference); return b.Add(ComponentFactory.Resistor(reference, value.SelectedOhm)); }
    }

    private static ComparatorDesignResult Failed(ComparatorDesignRequest request, string message) => new() { Request = request, Status = ComparatorDesignStatus.Failed, Message = message };
    private static ComparatorDesignResult Copy(ComparatorDesignResult r, ComparatorDesignStatus status, string message, IReadOnlyList<ComparatorVerificationMetric> verification, IReadOnlyList<string> warnings) => new()
    {
        Request = r.Request, Status = status, Message = message, CircuitGraph = r.CircuitGraph, ActualThresholds = r.ActualThresholds, WindowThresholds = r.WindowThresholds,
        BiasNodeVoltage = r.BiasNodeVoltage, Resistors = r.Resistors, Verification = verification, CalculationTrace = r.CalculationTrace, Warnings = warnings
    };
    private sealed record BoundaryCandidate(double Falling, double Rising, double Score, IReadOnlyList<ComparatorSelectedResistor> Resistors);
}
