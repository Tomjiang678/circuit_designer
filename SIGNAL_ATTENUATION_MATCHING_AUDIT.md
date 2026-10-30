# Signal Attenuation & Impedance Matching — Delivery Audit

## Status

The module is implemented as a fourth top-level WinForms wizard, with real synthesis, a final `CircuitGraph`, independent verification, analysis, schematic, SPICE, HTML/JSON reporting and `.cmddesign` snapshot support.

It is **not** a UI-only placeholder: the candidate table, verification status and exports are driven by the same final circuit graph.

## Architecture integration

- `MainForm` category: `Signal Attenuation & Impedance Matching`
- Host contract: `IDesignModuleHost` (no module-specific types in `MainForm`)
- Core reuse: `CircuitGraph`, `CircuitBuilder`, `CircuitComponent`, `ComponentFactory`, `SchematicDocument`, `CircuitSpiceExportOptions`, `SpiceText`, `ReportJson`, `IDesignModuleHost`
- Module root: `Modules/SignalAttenuationMatching/`
- Module design notes: [DESIGN.md](Modules/SignalAttenuationMatching/DESIGN.md)
- Module user/support notes: [README.md](Modules/SignalAttenuationMatching/README.md)

## Implemented modes

| Mode | Status | Evidence |
|---|---|---|
| Attenuation | Implemented | T/Pi/L/cascaded pads, equal/unequal ports, 0 dB equal bypass, power/return-loss verification |
| Impedance Matching | Implemented | LC L (4 positions), Pi/T LP/HP/mixed, real/complex ports, Q/bandwidth |

## Verification evidence

- Full regression: **150 tests passed** (144 baseline + 6 module tests).
- 300-point deterministic matrix plus an engineering-coverage test for E-series, expected failures, Touchstone 2.0/continuation, complex-port SPICE consistency, and bandwidth isolation.
  - 100 attenuation points (50–200 Ω ports, 1–20 dB, T/Pi).
  - 100 L-matching points (six port pairs/frequencies).
  - 100 Pi/T points (LP/HP, various Q).
- Representative tests:
  - 50 Ω → 50 Ω 3 dB and 6 dB T-pad; 0 dB uses a true zero-component direct connection.
  - 50 Ω → 50 Ω 10 dB Pi-pad; 75 Ω → 75 Ω 20 dB Pi-pad.
  - 75 Ω ↔ 50 Ω unequal T/Pi/L-pad.
  - 0 dB bypass and rejection of 50 Ω → 25 Ω 6 dB (below minimum matched loss).
  - 50 Ω → 200 Ω LC, 50 Ω → 1 kΩ @ 100 MHz ≈ 346.87 nH / 6.94 pF.
  - Complex source/load, mixed Pi, ESR/Q-enabled graph.
  - HTML report contains schematic SVG, independent verification and SPICE; JSON parses; design/testbench netlists are separated.
- Raw matrix: [300-point-matrix.csv](artifacts/SignalAttenuationMatching/300-point-matrix.csv).

## Build / test commands

Verified normal Release builds:

```powershell
dotnet build CircuitModuleDesigner.csproj -c Release
dotnet build Tests\CircuitModuleDesigner.CoreTests\CircuitModuleDesigner.CoreTests.csproj -c Release
.\Tests\CircuitModuleDesigner.CoreTests\bin\Release\net10.0-windows\CircuitModuleDesigner.CoreTests.exe
```

Result: **150 circuit-design tests passed**. A clean alternate output was also used during development because the currently running Debug application locks the `bin\Debug` exe. Close the running app before rebuilding Debug.

No LTspice solver executable was available in this environment. SPICE validation is serializer/golden-text and consistency based; it does not claim a simulator run.

## Scope / limitations

- Passive lumped R/L/C only.
- Ideal element baseline; ESR/Q is modeled as series resistance in the final graph.
- Custom R+jX is a single-frequency model; Series R-L/R-C is frequency-dependent.
- No transmission lines, PCB parasitics, EM simulation, transformer/balun, or vendor matching ICs.
- No Monte Carlo/yield analysis.
- Bandwidth is bounded by the finite sweep and stated thresholds.
- No `.asc` coordinate export.
- Complex-port resistive pads are rejected; the Impedance Matching mode must be used.
- The module does not modify the existing three modules' design math or outputs.

## Graphics

- Real T/Pi/LC topology previews with standard symbols.
- Final-graph schematic with junctions and ground rail.
- Dual-axis frequency response and a normalized Smith chart with resistance/reactance/VSWR grids. The Smith tab is hidden when a matched pad would only produce a center-point locus.
- Curve and Smith charts have a hover cursor readout; circuit schematics and topology previews do not.
- Report SVG uses the same graph topology and improved component/ground drawing.

## UI screenshots

- [T-pad preview](artifacts/SignalAttenuationMatching/topology-tpad.png)
- [Pi-pad preview](artifacts/SignalAttenuationMatching/topology-pipad.png)
- [Candidate page with final-graph schematic](artifacts/SignalAttenuationMatching/candidates.png)
- [Actual schematic](artifacts/SignalAttenuationMatching/schematic.png)
- [Frequency response](artifacts/SignalAttenuationMatching/response.png)
- [Smith chart](artifacts/SignalAttenuationMatching/smith.png)
