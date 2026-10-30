# Signal Attenuation & Impedance Matching — Design and Audit Notes

## 1. Scope

This module is the fourth top-level wizard in Circuit Module Designer. It is a passive-only design module: R, L and C elements, no op-amps, comparators, attenuator ICs or RF matching ICs.

The three modes are:

- **Attenuation**: resistive T/Pi/L/cascaded pads.
- **Impedance Matching**: LC L/Pi/T matching networks.

The final artifact is always a `CircuitGraph`, and the same graph is used by the schematic, SPICE exporters, reports, `.cmddesign` snapshot and independent verifier.

## 2. Architecture

| Layer | Files | Responsibility |
|---|---|---|
| Models/Enums | `Models/AttenuationMatchingModels.cs`, `Enums/AttenuationMatchingEnums.cs` | Request, port impedance, specifications, diagnostics, candidate, result, metrics |
| Topology | `Topology/NetworkTopologyBuilder.cs` | Converts a series/shunt element list into a real `CircuitGraph`; optional series ESR/Q resistors |
| Synthesis | `ResistiveAttenuatorSynthesizer`, `LMatchingSynthesizer`, `PiMatchingSynthesizer`, `TMatchingSynthesizer`, `StandardValueOptimizer` | Ideal synthesis and actual-value optimization |
| Analysis | `TwoPortNetworkAnalyzer` | MNA, transducer gain, return loss, VSWR, S-parameters, dissipation, sweep/bandwidth |
| Validation | `DesignRequestValidator`, `AttenuationMatchingCircuitVerifier` | Request feasibility and final-graph-only independent verification |
| Schematic | `AttenuationMatchingLayoutService`, `AttenuationMatchingSchematicSvgBuilder` | Orthogonal cascade layout and self-contained SVG |
| Reporting/Services | HTML, JSON, SPICE design/testbench, snapshot service | Delivery formats |
| Controls | Wizard, response plot, Smith chart, topology preview, engineering inputs | Seven-step UI |

`MainForm` only knows `IDesignModuleHost`; it does not reference this module's request/result types.

## 3. Synthesis mathematics

### 3.1 Matched T / Pi pads

For equal port resistance `R0`, attenuation `A` dB and `K = 10^(A/20)`:

```text
T:  Rseries = R0 (K-1)/(K+1)
    Rshunt  = 2 R0 K/(K²-1)

Pi: Rshunt  = R0 (K+1)/(K-1)
    Rseries = R0 (K²-1)/(2K)
```

0 dB is handled as a true zero-component direct connection only for equal ports; VIN and VOUT are aliases of the same graph node. Unequal 0 dB is rejected because a direct link cannot match both ports.

### 3.2 Unequal T / Pi pads

The T/Pi ABCD matrices are used to form three equations:

```text
Zin  = (A·ZL + B)/(C·ZL + D) = Rs
Zout = (D·Rs + B)/(C·Rs + A) = RL
GT   = 4RsRL / |A·RL + B + C·Rs·RL + D·Rs|² = Gtarget
```

The three positive resistors are solved with a bounded log-variable Newton method and multiple deterministic starts. The final solution is rejected if any resistor is non-positive/non-finite or any equation is outside tolerance.

This correctly rejects requirements below the minimum realizable matched-pad loss. For example, 50 Ω → 25 Ω at 6 dB has no matched T/Pi solution; 8 dB does.

### 3.3 Resistive L-pad

A two-resistor L-pad has only two degrees of freedom. For unequal ports it can match both ports, but the resulting attenuation is fixed by the impedance ratio. It is never treated as a free attenuation setting.

### 3.4 LC L matching

For real ports the classical relation is used for the initial reactance values:

```text
Q = sqrt(Rhigh/Rlow - 1)
|Xseries| = Q · Rlow
|Xshunt|  = Rhigh/Q
```

The actual sign/position follows the requested low-pass/high-pass form. The shunt element must be on the high-resistance side:

- source high → shunt at input;
- load high → shunt at output.

For complex ports the target is the conjugate source impedance, and a two-variable Newton solve is used for the true L-network equations. The solved network is then re-analyzed by MNA; a wrong-position/wrong-sign form is rejected, not silently rewritten.

### 3.5 Pi/T matching

Pi/T use a virtual intermediate resistance:

```text
Pi: Rv = min(Rs,RL)/(1 + Qdesign²)
T:  Rv = max(Rs,RL) · (1 + Qdesign²)
```

Two L sections are solved for the actual complex ports. For LP/HP the shared series elements (Pi) or shunt elements (T) are combined; mixed forms retain separate series/shunt L/C elements and are verified as a four-element network.

Design Q is only a synthesis choice. The loaded Q displayed in results is `f0 / measured bandwidth` from the final sweep.

### 3.6 Standard values

- Continuous mode uses the ideal solution.
- E12/E24/E48/E96 enumerate a bounded neighborhood of actual values per element.
- Every combination is rebuilt into a complete CircuitGraph and analyzed.
- Hard constraints are filtered before objective scoring.
- No ideal-value performance is ever reported as if it were the selected-value performance.

### 3.7 Parasitic model

When capacitor ESR or inductor Q/series resistance is enabled, series `R_PAR_<reference>` components are inserted into the final graph. They therefore affect MNA, S-parameters, return loss, dissipation, verification and SPICE. They are not report-only annotations.

## 4. Analysis definitions

```text
Pavs = |Vs|² / (4 Re(Zs))          (RMS Thevenin source)
GT   = Pload / Pavs
Attenuation/InsertionLoss = -10 log10(GT)
Matched-port attenuation  = -20 log10(|S21|)  (real references)
Γport = (Zport - Zref*) / (Zport + Zref)
ReturnLoss = -20 log10(|Γ|)
VSWR = (1+|Γ|)/(1-|Γ|)             (real reference only)
Bandwidth = continuous band around f0 satisfying return-loss/insertion-loss gates
LoadedQ = f0 / Bandwidth
```

S11/S21/S22 use a separate real-reference solve. S12 is left N/A in this module. A port `X` at `f0` is converted to a series L (positive X) or C (negative X), so the internal sweep and SPICE testbench use the same frequency-dependent impedance.

## 5. Independent verification

`AttenuationMatchingCircuitVerifier`:

1. Reads only the final CircuitGraph and request.
2. Rejects non-passive component types.
3. Checks finite, positive, in-range, standard values (parasitic resistors exempted as physical model elements).
4. Checks topology/component count and declared values.
5. Checks for missing ports, empty nets and dangling non-port nets.
6. Re-runs MNA at the design frequency and a full frequency sweep.
7. Recomputes attenuation, input/output return loss, S-parameters, VSWR, passivity, resistor dissipation and bandwidth.
8. Compares the candidate's claimed performance to the independent result and rejects stale metrics.
9. Emits structured diagnostics with code/severity/description/parameter/actual/expected/action.

Representative codes:

- `TOPOLOGY_MISMATCH`
- `COMPONENT_VALUE_OUT_OF_RANGE`
- `COMPONENT_NOT_STANDARD_VALUE`
- `RETURN_LOSS_REQUIREMENT_NOT_MET`
- `INSERTION_LOSS_REQUIREMENT_NOT_MET`
- `ATTENUATION_OUT_OF_TOLERANCE`
- `POWER_DISSIPATION_EXCEEDED`
- `PASSIVITY_VIOLATION`
- `DANGLING_NET`
- `STALE_RESULT`
- `NUMERICAL_SINGULARITY`
- `BANDWIDTH_REQUIREMENT_NOT_MET`

Status is Supported only when no errors exist; Warning only when non-blocking model/bandwidth warnings remain; otherwise Failed.

## 6. Topology support matrix

| Family | Supported cases | Rejected / limited cases |
|---|---|---|
| T-Pad | equal and unequal real ports; 0 dB equal bypass | complex ports; below minimum realizable loss; 0 dB unequal |
| Pi-Pad | equal and unequal real ports; 0 dB equal bypass | complex ports; below minimum realizable loss; 0 dB unequal |
| L-Pad | unequal real ports, fixed minimum loss | equal ports with nonzero attenuation; arbitrary attenuation |
| Cascaded Pads | 2–3 matched T stages | overly high target loss outside component ranges |
| LC L | 4 series/shunt positions; LP/HP; real and complex ports | equal real ports; wrong high-side position |
| LC Pi | LP/HP/mixed; real and complex ports | unstable/no positive-reactance solution |
| LC T | LP/HP/mixed; real and complex ports | unstable/no positive-reactance solution |

## 7. Tests and evidence

- Full suite: **150 tests passed** (baseline + 6 module tests).
- 300-point deterministic matrix: [CSV](../../artifacts/SignalAttenuationMatching/300-point-matrix.csv) — 300/300 accepted, 0 failed.
- Specific checks:
  - 50 Ω → 50 Ω 3/6 dB T-pad; 10/20 dB Pi-pad.
  - 75 Ω ↔ 50 Ω unequal pads.
  - 0 dB bypass and infeasible 50 Ω → 25 Ω 6 dB.
  - 50 Ω → 200 Ω and 200 Ω → 50 Ω L/Pi/T.
  - 50 Ω → 1 kΩ at 100 MHz ≈ 346.87 nH / 6.94 pF.
  - Complex source/load, mixed Pi, ESR/Q-enabled graph.
  - Reports contain schematic SVG, verification and SPICE sections.
  - SPICE design netlist excludes source/load; testbench includes them exactly once.

## 8. Known limitations

- Ideal lumped R/L/C baseline; ESR/Q is a series-resistance approximation.
- No distributed/transmission-line/microstrip/EM simulation.
- Port reactance is defined at the design frequency and swept as the equivalent series L/C.
- No transformer/balun/vendor matching IC or encrypted model support.
- No Monte Carlo/tolerance-yield analysis.
- Bandwidth is bounded by the finite sweep and declared thresholds.
- No LTspice `.asc` coordinate file.
- Fully differential/multi-port networks are not implemented.
