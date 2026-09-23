# Precision Analog Computing 定向修复报告

## 1. 修复边界与架构结论

本轮未修改测试规格、tolerance、元件范围、E-series、物理约束或拓扑，也未绕过 independent verification。设计链保持为：

`Specification → Validation/Capability → Continuous Synthesis → Component Range → Coupled E-series Optimization → Final CircuitGraph → Independent Verification`

Final CircuitGraph 仍是最终 capability 的判定来源。OperatingEnvelopeExceeded、finite-op-amp GBW/Slew、constant-output Range Mapper、domain/junction/topology 约束均保持上一轮已经验证的行为。

输入与冻结结果：

- 96 点输入：`artifacts/AnalogCompute_Targeted_96_TestPoints_FORMAT_FIXED.txt`
- 本轮前快照：`artifacts/AnalogCompute_Targeted_96_FINAL_ROUND_BEFORE.txt`
- 本轮后结果：`artifacts/AnalogCompute_Targeted_96_FINAL_ROUND_AFTER.txt`
- 500 点结果：`artifacts/PrecisionAnalogComputing_500_RESULTS_FINAL_ROUND.txt`

## 2. Before / After 总览

第一轮“按失败证据修复”的总体变化如下；本次标准值优化收敛轮没有改变 capability 数量。

| Overall Capability | 原始 Before | 上一轮 After | 本轮 Final |
|---|---:|---:|---:|
| Supported | 53 | 38 | 38 |
| Unsupported | 43 | 58 | 58 |
| Total | 96 | 96 | 96 |

Supported 数量下降来自 rail envelope、GBW 与 Slew 等真实 capability gate，不是 tolerance 或元件范围变化。本轮 Point 62 的最优误差明显改善，但仍未跨过原始 2% 标准，因此保持 Unsupported。

## 3. 每个 Operation 的结果

| Operation | Test count | 原始 Before supported | Final supported |
|---|---:|---:|---:|
| ExponentialAmplifier | 12 | 6 | 1 |
| GeneralLinearCombination | 12 | 9 | 5 |
| LogarithmicAmplifier | 12 | 6 | 5 |
| PracticalDifferentiator | 12 | 4 | 4 |
| PracticalIntegrator | 12 | 5 | 5 |
| PrecisionFullWaveRectifier | 12 | 7 | 4 |
| PrecisionHalfWaveRectifier | 12 | 9 | 6 |
| VoltageRangeMapper | 12 | 7 | 8 |

## 4. ReasonCode 分布

| ReasonCode | 原始 Before | Final |
|---|---:|---:|
| None | 53 | 38 |
| InvalidInput | 12 | 10 |
| ComponentRangeImpossible | 5 | 5 |
| StandardValueAccuracyInsufficient | 15 | 15 |
| JunctionCurrentOutOfRange | 3 | 3 |
| GbBandwidthInsufficient | 6 | 7 |
| SlewRateInsufficient | 0 | 1 |
| TopologyCapabilityUnsupported | 2 | 2 |
| OperatingEnvelopeExceeded | 0 | 15 |

## 5. Standard-value optimization audit

### 5.1 公共设施与搜索策略

公共 `AnalogPreferredValueSeries` 增加了两类候选生成能力：

- 从 continuous target 周围生成多档上下 E-series 邻域，而非 nearest-only。
- 对 `fixedFactor × candidate ≈ targetProduct` 返回满足容差的完整候选集；若没有满足者，再返回有界邻域用于选择最优失败证据。

拓扑搜索采用：continuous solution → 结构化比例/RC-product 候选 → 低成本 analytical score → Final CircuitGraph → independent verifier。没有执行 N^8 无脑笛卡尔积，也没有用 component distance 代替电路指标。

`Optimize=false` 直接保留 continuous/theoretical 元件值，元件没有 E-series provenance；`Optimize=true` 的 R/C 必须来自用户指定 series。批量 runner 已加入断言，96 点与 500 点均通过，没有改变 Optimize=false 路径。

### 5.2 Point 62：PrecisionFullWaveRectifier_TGT_NEGATIVE_OUTPUT_GAIN2

结论：找到比旧结果更优的合法 E48 组件组合，但仍不存在满足全部原始 2% verification gate 的组合，因此保持 `StandardValueAccuracyInsufficient`。

最佳组件：

| Component | Value |
|---|---:|
| R1 | 11.5 kΩ |
| R2 | 24.9 kΩ |
| R3 | 10.5 kΩ |
| R4 | 11.5 kΩ |
| R5 | 21.5 kΩ |

Final CircuitGraph 指标：

| Metric | Target | Actual | Tolerance | Result |
|---|---:|---:|---:|---|
| Positive-side gain | 2 | 2.04761904762 | 0.04 | Fail |
| Negative-side gain | 2 | 2.00039607525 | 0.04 | Pass |
| Half-axis symmetry mismatch | 0 | 0.04722297236 | 0.04 | Fail |
| Maximum transfer error | 0 | 0.04761904762 | 0.04 | Fail |
| Crossover error | 0 | 0 | 1e-9 | Pass |

旧 best 的 WorstMetricRatio 约为 1.4436；新 best 为约 1.19048。改进是真实的 optimizer 改进，但不足以把该规格判为 Supported。

搜索把第二级 weighted summer 表示成比例与 overall impedance scale：遍历允许范围内的 E48 summer scale、direct/rectified branch ratio，并从完整 Ri/Rf 标准比例集合中选择目标比例两侧的候选。对固定 scale/branch 而言，四项 verification error 对目标比例距离单调，因此两侧 bracket 是判定该子空间是否存在可行解的充分集合，不会漏掉可通过点。

- 最终结构化评估候选数：262,320。
- Scalar-feasible 候选数：0。
- 早期未剪枝的等价 exhaustive audit 曾评估 3,389,409 个候选，得到相同 best 和同样的 0 feasible 结论。
- 最终判断：在 E48、1 kΩ～1 MΩ、2% 下无可行解；未降低 verifier 标准。

### 5.3 Point 77：PracticalDifferentiator_TGT_VALID_LOW_BW

结论：coupled R/C 搜索没有找到遗漏的可行组合，保持 `StandardValueAccuracyInsufficient`。

最佳组件：

| Component | Value |
|---|---:|
| Rin | 160 kΩ |
| Cin | 10 nF |
| Rf | 1 kΩ |
| Cf | 787 nF |

Final CircuitGraph 指标：

| Metric | Target | Actual | Tolerance | Result |
|---|---:|---:|---:|---|
| Kd | 1.0e-5 s | 1.0e-5 s | 1.0e-7 s | Pass |
| Lower limiting corner | 100 Hz | 99.47183943 Hz | 1 Hz | Pass |
| Upper limiting corner | 200 Hz | 202.22991498 Hz | 2 Hz | Fail |
| Maximum AC magnitude error | 0 | 0.04946105233 | 0.10142747565 | Pass |
| Maximum AC phase error | 0° | 0.20590462359° | 0.75° | Pass |

搜索先完整生成可满足 `Rin·Cin` 与 `Rf·Cf` pole-product gate 的 RC pairs，再组合检查 `Rf·Cin = Kd`，score 同时包含 normalized Kd、两个 corner、代表频点 magnitude 与 phase。最后仍由原公式

`H(s) = -s Rf Cin / ((1+s Rin Cin)(1+s Rf Cf))`

构建的 Final CircuitGraph 验证。

- 结构化评估候选数：38,262。
- Scalar-feasible 候选数：0。
- 最终判断：在指定 E24 resistor、E48 capacitor、范围和 1% tolerance 下无可行解。

### 5.4 TargetedExtra：Point 93 / 95 / 96

这三点都没有因名称含 `Nominal` 而被强制放行。

| Point | Best components | Remaining failed metrics | Candidates / conclusion |
|---:|---|---|---|
| 93 LogarithmicAmplifier | R3=107 kΩ, R4=113 kΩ；其余见结果文件 | Log scale：target 0.05，actual 0.04971629488，tol 0.00025 | E96 10 kΩ～10 MΩ 有 289 个 feedback scales；每个 scale 取数学上最优 input，等价覆盖 83,521 个 ratio pairs。最佳 ratio 仍超限，无可行解。 |
| 95 PracticalIntegrator | Rin=10 kΩ, Rf=15.8 kΩ, Cf=10 µF | Leak corner：1 / 1.007309766 / 0.005；AC magnitude：0 / 0.062639066 / 0.058321235 | 1,846 个 coupled product candidates，scalar-feasible=0；保持 Unsupported。 |
| 96 PracticalDifferentiator | Rin=113 kΩ, Cin=560 pF, Rf=17.8 kΩ, Cf=1.8 nF | Lower：2500 / 2515.090757 / 12.5 Hz；Upper：5000 / 4967.382743 / 25 Hz | 15,925 个 coupled candidates，scalar-feasible=0；保持 Unsupported。 |

Point 93/95/96 的最终 capability 与本轮前相同；审计未发现可被旧 optimizer 漏掉的合法 E-series 解。

### 5.5 修改范围

- 修改 common optimizer：是，扩展 `AnalogPreferredValueSeries` 的 nearby/product candidate 生成。
- 修改 topology-specific optimizer：是，修改 FullWave 的 ratio+scale 搜索和 Integrator/Differentiator 的 coupled RC-product 搜索。
- 修改 topology、transfer formula、tolerance、range 或 E-series：否。
- 修改 OperatingEnvelope、GBW/Slew、Range Mapper、junction/topology 语义：否。

## 6. StandardValueAccuracyInsufficient 分类

### Optimizer weakness fixed

- FullWave 不再先独立 round 部分电阻；正负半轴与 weighted summer 作为整体比例网络评分。
- Integrator/Differentiator 不再固定一类元件后独立 round；RC pole products 与 Ki/Kd 同时进入结构化组合。
- Point 62 的 best 明显改善，证明旧 neighborhood 确实偏窄。

### Genuinely infeasible under selected E-series/range/tolerance

Point 62、77、93、95、96 均在扩展后的完整“可能通过区域”搜索中得到 0 feasible，保持 `StandardValueAccuracyInsufficient`。本轮没有任何一个 96 点通过降低标准转为 Supported。

Point 81/84/85/87 等大幅超限点未为提高通过率而强行优化；它们仍由原 independent verifier 给出真实失败。

## 7. Regression anchors

以下最终状态全部保持：

| Point | Final result |
|---:|---|
| 50 | OperatingEnvelopeExceeded |
| 51 | GbBandwidthInsufficient |
| 52 | SlewRateInsufficient |
| 54 | OperatingEnvelopeExceeded |
| 55 | InvalidInput |
| 56 | Supported |
| 59 | OperatingEnvelopeExceeded |
| 60 | GbBandwidthInsufficient |
| 63 | OperatingEnvelopeExceeded |
| 64 | GbBandwidthInsufficient |
| 68 | OperatingEnvelopeExceeded |
| 69 | Supported |
| 70 | InvalidInput |
| 71 | TopologyCapabilityUnsupported |
| 72 | OperatingEnvelopeExceeded |
| 73 | Supported |
| 74 | InvalidInput |
| 75 | ComponentRangeImpossible |
| 76 | GbBandwidthInsufficient |
| 78 | InvalidInput |
| 79 | ComponentRangeImpossible |
| 80 | GbBandwidthInsufficient |

Point 71 的测试名原意是 junction-current reject，但实际首先触发合法条件 `S > n·Vt`：0.05 V 小于约 0.0513851582 V。它属于 test intent 与 first-failure constraint 不一致，不是实现错误，未修改为 JunctionCurrentOutOfRange。

## 8. 本轮修改的源码文件

- `Modules/PrecisionAnalogComputing/Synthesis/AnalogPreferredValueSeries.cs`
- `Modules/PrecisionAnalogComputing/Synthesis/PiecewiseOperationSynthesizer.cs`
- `Modules/PrecisionAnalogComputing/Synthesis/DynamicOperationSynthesizer.cs`
- `Tests/PrecisionAnalogBatchSmokeTest/Program.cs`

上一轮已正确的 validation、OperatingEnvelope、op-amp practical checks 与 UI 文件本轮没有再改写。

## 9. 最终验证结果

### 96-point regression

- 96/96 rows parsed and completed。
- Supported = 38，Unsupported = 58。
- Point 62、77、93、95、96 的组件、全部 metric 和 optimizer candidate audit 已写入 runner 输出。

### Original 500-point regression

- 500/500 rows parsed and completed。
- Supported = 152，Unsupported = 348。
- Reason distribution 与本轮前一致；未出现旧 regression 回退。
- Optimize=false / Optimize=true provenance assertions 通过。

### Core tests and performance

- 139/139 tests passed。
- Precision Analog Core40 = 599.7 ms，低于 10 s safety budget。
- 搜索性能优化前曾达到 50.9 s；使用数学上充分的 ratio bracket 剪枝后，best、metric 和 feasible=0 结论不变。

### dotnet build

- `dotnet build CircuitModuleDesigner.csproj -c Release --no-restore`
- 结果：成功，0 warnings，0 errors。
