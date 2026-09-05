# 48-Point Regression Repair Report

## Outcome

- Final 48-row result: 39 `Success` (valid generated CircuitGraph), 9 `UnsupportedDesignCombination`, 0 `SynthesisFailure`.
- 19 rows fully meet specifications; 20 rows generate valid circuits but honestly report a specification/capability failure.
- No row returns `PendingIndependentVerification`.
- Build: 0 warnings, 0 errors. Automated regression: 115/115 passed.

## #26 / #28 finite Rs/RL

The physical LP/HP formulas and CircuitGraphs were already close, but specification compliance used a fixed 0 dB passband reference. Normal attenuation caused by finite Rs/RL was therefore incorrectly added to Rp, and automatic topology-based RC edge misses were promoted to invalid-circuit failures.

RC edge compliance now uses the actual loaded passband peak as the shape reference while retaining absolute VOUT/VSRC as a reported result. Formula and whole loaded CircuitGraph/MNA are cross-checked at multiple frequencies.

- #26: `Success / Supported`; Rs=600 Ω, RL=100 kΩ.
- #28: `Success / Supported`; Rs=1 kΩ, RL=47 kΩ.
- External Rs/RL remain analysis boundary elements and are not checked against filter-component R ranges.

## #29 / #30 Two-Corner RC Band-Pass

The topology is the real unbuffered network `C_HP series → R_HP shunt → R_LP series → C_LP shunt`, including Rs and RL. R1 and R2 are now searched independently over bounded impedance scales; C1/C2 candidates are recomputed for each pair. Actual corners are the two -3 dB crossings relative to the loaded band-pass peak and are measured from the full network response, not from multiplied isolated sections.

Both test inputs generate valid CircuitGraphs, but their requested passband/stopband edges cannot all be met by this loaded unbuffered topology:

- #29: `Success / RcBandPassCornersNotAchievable`.
- #30: `Success / RcBandPassCornersNotAchievable`.

Calculations now record actual lower/upper corners, peak gain and whole-loaded-MNA verification.

## #31 / #32 Twin-T

The implemented network uses equal series R arms and equal series C arms, plus two equal capacitors in parallel for 2C and two equal resistors in parallel for R/2. All eight physical components remain in CircuitGraph.

The Rs=0 analytic evaluator previously stamped the input branches twice: once as ground branches and once as fixed-source branches. That made synthesis evaluation disagree with the real graph. The duplicate stamp was removed and analytic transfer is now cross-checked against loaded CircuitGraph/MNA.

Both cases achieve a deep notch, but their requested passband edges are too close to the notch for Rp=3 dB:

- #31: notch about -38.02 dB; `TwinTRequestedResponseNotAchievable`.
- #32: notch about -47.80 dB; `TwinTRequestedResponseNotAchievable`.

They are no longer mislabeled as component-range or pending-verification failures.

## Capability stages

The result now records separate gates:

1. theoretical continuous CircuitGraph valid;
2. theoretical circuit meets specification;
3. selected-value CircuitGraph valid;
4. selected-value circuit meets specification;
5. failure stage and precise reason.

The Test Row result keeps the existing 57-column layout and writes these diagnostics into the final detail field. Calculations/Report consume the same structured stage record.

Former LC `LcSelectedValuesFailSpecification` rows reclassified as `SpecifiedOrderInsufficient`:

- #33, #36, #37, #39, #40, #44, #45, #46.

The standard-value distinction is independently exercised by #6: continuous theory passes, E24 fails with `SelectedValuesFailSpecification`, and the otherwise identical E96 design passes.

## Active Band-Pass

- #15 is a real Sallen-Key BP positive-component/gain-coupling infeasibility: `SallenKeyBpGainCouplingViolation`.
- #18 contains an MFB BP section with Q ≤ 0.707: `SectionQOutOfRange`.

Both paths already have independent topology verification; neither is reported as pending.

## Frozen LC regression

- Matched 50/75/600 Ω behavior remains verified: absolute through voltage is approximately -6.0206 dB and insertion response is approximately 0 dB.
- #38 remains `LcPrototypeRequiresNativeTermination`.
- Prototype-Native still derives RL; incompatible Custom termination remains `LcTerminationRatioUnsupported` (#41).
- Bessel LP/HP remain supported (#42/#43).
- LinearPhase negative-element extraction remains blocked (#48).

## Final reason distribution

- `Supported`: 19
- `SpecifiedOrderInsufficient`: 14
- `SelectedValuesFailSpecification`: 2
- `RcBandPassCornersNotAchievable`: 2
- `TwinTRequestedResponseNotAchievable`: 2
- `ResponseUnavailable`: 3
- `SallenKeyBpGainCouplingViolation`: 1
- `SectionQOutOfRange`: 1
- `ArchitectureUnavailable`: 1
- `LcPrototypeRequiresNativeTermination`: 1
- `LcTerminationRatioUnsupported`: 1
- `LcLadderExtractionProducedNegativeElement`: 1

