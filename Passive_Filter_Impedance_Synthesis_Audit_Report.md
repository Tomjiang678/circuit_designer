# Passive Filter Impedance & Synthesis Audit Report

## 1. Electrical boundary

All passive-filter analysis uses one boundary:

`VSRC -> Rs -> VIN -> Passive Filter Network -> VOUT -> RL -> GND`

- `VSRC`: ideal Thevenin open-circuit source voltage.
- `VIN`: actual loaded input node of the filter network.
- `VOUT`: output/load node.
- `Rs` and `RL`: external terminations. They participate in AC/MNA and CIR export, but are not counted as internally synthesized filter parts.
- With `Rs = 0`, `VSRC` and `VIN` are the same electrical node; no 0-ohm resistor is created.

## 2. Response references

- Absolute voltage transfer: `Habs = VOUT / VSRC`.
- Filter-node voltage transfer: `Hnode = VOUT / VIN`.
- Transducer power gain for `Rs > 0`: `Gt = PL/Pavs = 4 Rs/RL |VOUT/VSRC|^2`.
- Insertion response relative to through: `Gins = |Habs / Hthrough|^2`, where `Hthrough = RL/(Rs+RL)` and direct through is 0 dB.

For a 50-ohm source and 50-ohm load, direct through gives `VOUT/VSRC = 0.5`, or -6.0206 dB absolute voltage transfer. Relative insertion response is nevertheless 0 dB because the same source/load divider is the through reference. `20 log10(VOUT/VSRC)` is never labelled insertion loss.

RC uses AbsoluteVoltageTransfer as its primary specification reference. Doubly terminated LC uses InsertionLossRelativeToThrough by default in the UI; TransducerGain and both voltage-transfer views remain available in Analysis.

## 3. Passive RC

UI defaults are `Rs = 0 ohm`, `RL = 1 Mohm`, and Preferred R = `10 kohm`. Rs and RL are editable and cause a complete redesign.

Formal topology support:

| Response | Physical topology | Approximation semantics |
|---|---|---|
| Low-Pass | Loaded first-order series-R/shunt-C | Topology-based; approximation family is not claimed |
| High-Pass | Loaded first-order series-C/shunt-R | Topology-based; approximation family is not claimed |
| Band-Pass | Loaded two-corner RC network | Not called a second-order Butterworth/Chebyshev filter |
| Band-Stop | Loaded passive Twin-T notch | Not called an approximation-family Band-Stop filter |

For Low-Pass, with internal `R`, the solver uses:

`H(s) = [RL/(Rs+R+RL)] / [1 + s C ((Rs+R)||RL)]`.

For High-Pass, with `Rp = R||RL`, the solver uses:

`H(s) = [Rp/(Rs+Rp)] [s C (Rs+Rp)] / [1 + s C (Rs+Rp)]`.

Thus Rs/RL affect both pole and passband level. R/C candidates are jointly scored from the loaded response. The two-corner Band-Pass is optimized as one interacting four-part network and verified by its full CircuitGraph, not by multiplying isolated stages. Twin-T uses real compound pairs for `R/2` and `2C`, jointly selects its matched base R/C ratio, and verifies loaded notch frequency/depth.

## 4. Passive LC

UI defaults are linked `Rs = RL = 50 ohm`; presets are 50/75/600/1k/Custom.

Termination policies:

- `MatchedUserTermination`: requires `Rs = RL` and a normalized prototype termination ratio `gN+1/g0 = 1`.
- `PrototypeNativeTermination`: `R0 = Rs/g0`, and the engine derives `RL = R0*gN+1`.
- `CustomIndependentTermination`: accepted only when the entered Rs/RL ratio equals the verified prototype-native ratio.

An even-order Chebyshev-I ladder whose native `gN+1/g0` is not unity is rejected in Matched mode with `LcPrototypeRequiresNativeTermination`; the program does not overwrite the prototype or silently reinterpret a 50/50 system. `Rs = 0` remains Unsupported (`LcTerminationModeMismatch`) because the current g-value path is a doubly terminated insertion-loss synthesis, not an independently synthesized single-ended voltage-transfer ladder.

`SeriesFirst`, `ShuntFirst`, and `Auto` are electrical alternatives, not drawing-only choices. Shunt-first is produced by a verified dual ladder only for compatible equal prototype terminations. Auto compares same-type L/C spread and chooses the lower-spread realization; non-unit native terminations retain the compatible orientation.

LP/HP/BP/BS use the common frequency transformation. BP/BS expand every normalized element into the appropriate series/parallel resonator. Standard-value optimization first jointly preserves resonator frequency, then performs two bounded coordinate-search passes. Every candidate is rebuilt as a loaded CircuitGraph and scored at actual specification edges. The final selected graph is independently analyzed again at full resolution.

The v1 circuit model uses ideal L/C. Narrow fractional bandwidth adds `IdealComponentAssumptionMayBeUnrealistic`; excessive same-type component spread adds `NarrowBandComponentSpreadTooLarge`. DesignResult has explicit future fields for inductor Q/DCR and capacitor ESR; they are not fabricated in v1.

## 5. LC approximation capability

All entries below additionally require DoublyTerminated mode, compatible termination ratio/orientation, positive finite components, component-range feasibility, selected-value loaded CircuitGraph verification, and final edge compliance.

| Approximation | Prototype orders | LP/HP/BP/BS |
|---|---:|---|
| Butterworth | Regression 1-6 | Supported subject to final per-design gate |
| Chebyshev-I | Regression 1-6 | Supported; non-unit native termination may require PrototypeNative |
| Bessel | 1-6 | Supported via insertion-loss polynomial, Hurwitz spectral factor, positive-real driving-point function, Cauer extraction, and reconstructed loaded response |
| Gaussian to 6 dB | 3-9 | Positive-real prototype supported; order 10 Unsupported |
| Gaussian to 12 dB | 4-7, 9 | Positive-real prototype supported; orders 3, 8, 10 Unsupported |
| Linear Phase 0.05 degree | 2-9 | Positive-real prototype supported; order 10 Unsupported |
| Linear Phase 0.5 degree | 2-10 | Positive-real prototype supported |

Order-4 Gaussian/Linear-Phase designs have end-to-end LP/HP/BP/BS regression coverage. Other positive-real listed orders remain subject to the same final per-design loaded-circuit gate. Unsupported orders return the audited positive-real/spectral-factor/Cauer reason; no g-values are invented.

## 6. UI, Analysis, Calculations, Report and CIR

- Passive A0 is hidden; a passive circuit reports actual passband voltage/insertion level rather than promising gain above unity.
- Analysis offers Absolute Voltage, Filter Node, Transducer Power Gain (only when Rs > 0), and through-normalized Insertion Response.
- Calculations and HTML/JSON reports contain a dedicated Source/Load/Response Reference section, termination policy, orientation, g-values, selected values, loaded verification, and ideal-component warnings.
- Circuit Summary counts the filter network separately from external Rs/RL.
- CIR export uses `V_INPUT VSRC`, `R_SOURCE VSRC VIN` when Rs > 0, and `R_LOAD VOUT 0` for every passive RC/LC design. With Rs = 0 the ideal source is connected directly to VIN.

## 7. Structured capability failures

The passive path now emits specific reasons including `RcLoadedPoleNotAchievable`, `RcPassbandLossTooHigh`, `RcBandPassCornersNotAchievable`, `TwinTNotchDepthInsufficient`, `LcTerminationModeMismatch`, `LcTerminationRatioUnsupported`, `LcPrototypeRequiresNativeTermination`, `LcComponentRangeViolation`, `NarrowBandComponentSpreadTooLarge`, and `LcSelectedValuesFailSpecification`.

`FilterDesignStatus.Success` retains the existing application meaning “a circuit/result was generated.” Formal support is `IsFullySupported == true` and `CapabilityReasonCode == Supported`. A generated circuit that misses an entered edge remains visible for diagnosis, but its capability is false with a topology-specific reason; it is never represented as Supported.

## 8. Corrected concepts

1. VIN is no longer overloaded as both ideal source voltage and loaded filter input.
2. Absolute voltage transfer is no longer called insertion loss.
3. The -6.0206 dB 50/50 source/load divider is separated from 0 dB through-normalized insertion response.
4. Rs=0 can no longer enter classic doubly terminated LC synthesis.
5. LC impedance scaling uses `Rs/g0`, and RL is derived from `R0*gN+1` in native mode.
6. RC pole calculations now include both source and load impedance.
7. Passive RC approximation/order labels no longer imply unavailable high-order pole families.
8. Passive RC CIR files now include the same external load/source boundary used by verification.
9. LC E-series selection now scores rebuilt loaded CircuitGraphs instead of ending at independent nearest-value rounding.

