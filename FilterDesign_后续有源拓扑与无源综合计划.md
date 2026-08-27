# Circuit Module Designer：Filter Design 后续有源拓扑与无源综合计划

## 1. 文档目的

本文只规划 Filter Design 模块后续两项工作：

1. 补齐并验证有源滤波器 topology/response 组合；
2. 研究并实现 Bessel、Gaussian、Linear Phase 的无源 LC 综合。

本计划不涉及新的主界面、Specification Preview、通用 Schematic Renderer、PCB、具体运放型号库或 Multisim Automation。现有 Butterworth/Chebyshev-I、Sallen-Key LP/HP、MFB LP/HP/BP、Passive LC Butterworth/Chebyshev-I 应先作为回归基线冻结。

“补齐”不等于让每个 topology 与 LP/HP/BP/BS 做没有理论依据的笛卡尔积。只有具备明确电路结构、可解的正元件值、独立传递函数验证和最终 CircuitGraph AC 验证的组合才能进入 `Supported`。

## 2. 当前代码审查结论

### 2.1 已正式支持

| 模式 | Topology | Response |
|---|---|---|
| Active | Sallen-Key | LP、HP |
| Active | MFB | LP、HP、BP |
| Passive | RC | 一阶 LP、HP |
| Passive | LC | Butterworth/Chebyshev-I 的 LP、HP、BP、BS |

### 2.2 已有代码但尚未取得支持资格

- `SallenKeyBandPassSectionCalculator` 已存在，也有对应 CircuitGraph assembler 分支，但没有通过当前支持矩阵。
- `BainterNotchSectionCalculator`、Bainter CircuitGraph assembler 已存在，但 Bainter 被整体禁用。
- Sallen-Key BP 的局部 `Verify()` 与 Bainter 的局部 `Verify()` 目前仍主要依据实现公式本身，缺少从真实 CircuitGraph/MNA 独立推导的传递函数交叉验证。
- `FilterSupportMatrix` 当前先全局拒绝 Active Band-Stop，这是安全行为，不能在验证前删除。
- MFB Band-Stop 没有正式 solver；不能把 MFB LP、HP 或 BP 的元件替换规则套过来。

### 2.3 有源拓扑的工程约束

- Sallen-Key LP/HP 中运放非反相增益 `K` 会影响 Q；Sallen-Key BP 中 Q 与中心增益的耦合更强，增益不能完全独立指定。
- MFB BP 适合中低 Q section，但具有反相极性及 Q、中心频率、中心增益耦合。
- Band-Stop section 的标准形式应明确为：

  `H(s) = K × (s² + ωz²) / (s² + (ωp/Qp)s + ωp²)`

  solver 必须同时实现目标极点频率 `ωp`、零点频率 `ωz`、Q 和 section gain，不能只让电路在某个频率“看起来有凹口”。
- TI 的工程资料指出，Sallen-Key/MFB 通常用于 LP、HP、BP，而 Band-Stop 因 Sallen-Key ringing 和 MFB 同时满足 gain/natural-frequency 的困难，更适合 Bainter topology。因此 v1 的目标是让 Bainter 成为正式 BS 实现，而不是强制宣称 Sallen-Key BS/MFB BS 都支持。

参考资料：

- Analog Devices AN-649：Sallen-Key LP/HP/BP 与 MFB LP/HP/BP 的设计关系，特别说明 Sallen-Key BP 的 Q 会决定增益：<https://www.analog.com/en/resources/app-notes/an-649.html>
- Analog Devices MT-203，Bainter Notch Filter Design Equations：<https://www.analog.com/media/en/training-seminars/tutorials/mt-203.pdf>
- TI 关于 Band-Stop topology 选择的工程说明：<https://www.ti.com/document-viewer/lit/html/SSZT444/GUID-2F32308F-2FD9-44DB-943F-18E1DBD4C791>
- TI 对 Sallen-Key/MFB notch 实现困难的分析：<https://www.ti.com/lit/an/slyt618/slyt618.pdf>

## 3. 工作流 A：有源 topology 补齐

## A0. 先建立独立的 section 电路验证基准

这是后续所有 topology 开放支持前的共同前置工作。

### A0.1 新增独立验证表示

建议增加：

- `SectionTransferTarget`
  - ResponseType
  - PoleAngularFrequency
  - ZeroAngularFrequency（notch/BP 需要）
  - Q
  - TargetGainMagnitude
  - TargetPolarity
  - Numerator/Denominator
- `SectionTransferMeasurement`
  - Actual poles/zeros
  - Actual f0/Q/gain
  - DC、center、high-frequency gain
  - Frequency/Q/gain/zero errors
- `ICircuitSectionTransferVerifier`
  - 输入最终 selected R/C 和 section CircuitGraph fragment；
  - 使用独立 MNA/传递函数提取，而不是调用 solver 自身公式；
  - 输出实际 transfer function 和验证结果。

### A0.2 双重验证原则

每个 section 必须经过两条相互独立的验证路径：

1. solver 解析公式反算 `f0/Q/gain/zero`；
2. 从最终 CircuitGraph fragment 建立理想运放 MNA，扫描/拟合或直接求出传递函数。

两条结果必须彼此一致，并与 `AnalogFilterSection` 目标一致。这样可发现“公式和 builder 一起接错，但各自内部测试仍通过”的问题。

### A0.3 通用验收阈值

- 理论连续值：f0/Q/gain/zero 相对误差建议 `< 1e-8`；
- 标准值优化后：使用现有工程阈值，但必须分别记录 f0、Q、gain、zero error；
- 所有 R/C 必须为有限正值并处于用户范围；
- CircuitGraph 中 IN+、IN- 不得短接，反馈路径必须存在；
- section AC response 在至少 20 个对数频率参考点与目标传递函数一致；
- 级联后再由现有 whole-circuit verifier 检查 A0、Rp、As 和边缘频率。

## A1. 正式完成 Sallen-Key Band-Pass

### A1.1 现有代码处理方式

不新建 `SallenKeyBandPassSectionCalculator2`。直接审查并重构现有：

- `Synthesis/SallenKeyBandPassSectionCalculator.cs`
- `Topology/FilterCircuitAssembler.cs` 中 `AddSallenKeyBandPassSecondOrder`

把 solver 移到与 LP/HP 相同的职责结构，例如：

`Synthesis/SallenKey/SallenKeyBandPassSectionSolver.cs`

### A1.2 必须重新确认的数学内容

- 固定当前采用的 Sallen-Key BP 电路图和节点定义；
- 从该电路节点方程重新推导 numerator/denominator；
- 明确：
  - `ω0(R,C,K)`；
  - `Q(R,C,K)`；
  - center gain；
  - 极性；
  - 可实现的 Q/K/section-gain 区域；
  - 元件比例与 component spread。
- 将当前代码中的常数关系和输出衰减网络逐项对照推导，不能仅因局部 `Verify()` 返回目标值就认定正确。

### A1.3 Gain Distribution 接口

Sallen-Key BP solver 应返回：

- `IntrinsicTopologyGain`：该 Q 所要求的自然中心增益；
- `AvailableGainRange`；
- `RequiredOutputOrInterstageScale`；
- `ActualSectionGain`。

如果目标 section gain 小于 intrinsic gain，可以使用明确标记的衰减器；只有衰减器会加载下一级时才添加 follower。若下一 section 本身具有足够高输入阻抗且验证通过，不应机械增加 buffer。

### A1.4 支持条件

只有当每个 section 都满足以下条件时，当前设计才可用 Sallen-Key BP：

- Q、K 落在正元件可实现区域；
- 标准值优化后误差通过；
- 总体 A0 和极性通过 whole-circuit verification；
- 内部峰值增益没有触发不可接受的 dynamic-range failure。

不满足时返回具体的 `SynthesisFailure`，并建议改用 MFB BP，而不是生成额外的假电路。

## A2. 把 Bainter Notch 变成正式 Active Band-Stop 路径

### A2.1 为什么优先 Bainter

Bainter 是独立的 notch topology，能够针对 `ωp`、`ωz`、Q 和 gain 设计，适合作为 Active BS 的正式 v1 实现。它不应继续隐藏在“像 Sallen-Key/MFB 一样的普通选项”后面。

### A2.2 重构现有 calculator

审查并保留现有：

- `Synthesis/BainterNotchSectionCalculator.cs`
- `Topology/FilterCircuitAssembler.cs` 的 Bainter fragment builder

建议拆分为：

- `BainterNotchSectionSolver`
- `BainterNotchTransferVerifier`
- `BainterNotchStandardValueOptimizer`
- 现有 assembler 只读取 solver 已确定的 component roles。

### A2.3 重点检查

- 现有 `wp`、`wz` 公式是否与 CircuitGraph 中 R1…R8、C1/C2 的真实连接一致；
- 三个抽象 OpAmp 的 IN+/IN-/OUT 网络是否与 MT-203 拓扑一致；
- K1/K2、section gain 与总体 A0 的关系；
- 标准值后 notch depth 对 R/C 匹配误差非常敏感，评分必须包含 zero-frequency error 和 notch depth；
- BP/BS 频率变换产生的每个二阶 section 可能有不同 `ωp`，但共享或接近同一 `ωz`，assembler 必须按 section 数据生成，不能固定一个模板频率；
- 多级 Bainter 级联的极性、增益和内部峰值。

### A2.4 标准值优化策略

notch depth 对独立舍入非常敏感，不能逐个选择最近 E-series。建议：

1. 围绕理论 C1/C2 和关键 resistor ratio 生成关联候选；
2. 优先保持决定 `ωz` 的乘积/比例；
3. 评分至少包含：pole-frequency error、Q error、zero-frequency error、gain error、notch attenuation；
4. 候选必须通过 selected-value CircuitGraph AC analysis；
5. 如果 E24/E12 无法达到用户 As，明确要求 E96、关闭标准值优化或降低 As。

### A2.5 UI/支持矩阵

- 当 Response=Band-Stop 时，Implementation 应显示 `Bainter Notch`；
- Sallen-Key/MFB BS 继续显示 Unsupported 或不列出；
- `FilterSupportMatrix` 不能再全局拒绝所有 Active BS，而应根据 `Bainter + section feasibility` 判断；
- Butterworth、Chebyshev-I、Bessel、Gaussian、Linear Phase 都可先进入 prototype/BS transformation，但最终是否 Supported 由各 Bainter section 的实际可实现性决定。

## A3. MFB Band-Stop：先研究，再决定是否产品化

不建议把 MFB BS 作为 Bainter 之前的交付目标。

### A3.1 研究任务

- 固定一种明确的 two-stage MFB notch topology；
- 推导完整传递函数和可实现条件；
- 比较它与 Bainter 在 gain、Q、notch depth、component spread、op-amp 数量上的差异；
- 对高阶 BS 中的多组 `ωp/Q/ωz` 做可行域扫描。

### A3.2 决策门槛

仅当以下条件满足才增加 `MfbBandStopSectionSolver`：

- 存在稳定的正元件解；
- 可以独立控制或可靠满足 `ωp/ωz/Q/gain`；
- E96 条件下能达到有意义的 notch depth；
- 相比 Bainter 至少有元件数量、运放数量或动态范围方面的明确优势。

若不满足，MFB BS 永久保持 Unsupported 是正确的工程结论，不属于“模块没做完”。

## A4. Sallen-Key Band-Stop：不作为 v1 必需项

Sallen-Key notch 往往需要 LP/HP 路径匹配和求和，不能复用基础 LP/HP VCVS cell。计划中只进行拓扑研究和 reference test，不在没有独立优势时产品化。v1 以 Bainter BS 覆盖 Band-Stop 需求。

## A5. Fully Differential

Fully Differential 是独立 architecture，不是给现有单端 MFB 加一个 checkbox。建议放在有源单端 topology 全部稳定之后：

- 增加 differential input/output、VOCM、成对元件和差模/共模网络；
- 建立 FDA-specific MFB solver 和 CircuitGraph symbol/model；
- 不尝试把 Sallen-Key 机械转换为 fully differential。

它不属于本轮 A1/A2 的阻塞项。

## 4. 工作流 B：Bessel/Gaussian/Linear Phase 的 Passive LC

## B0. 先修正概念：poles 不等于 ladder g-values

`FilterPrototype.Poles` 描述某个电压传递函数；`g0...gN+1` 描述带终端条件的 normalized lossless ladder。仅有 poles 无法安全推出当前 assembler 所需的 ladder 元件。

因此不能执行：

`Bessel/Gaussian/Linear poles -> Butterworth g-values -> LC`

正确链路应为：

`Approximation definition -> insertion-loss/power transfer function -> reflection polynomial spectral factorization -> positive-real driving-point function -> Cauer continued-fraction extraction -> normalized ladder -> LP/HP/BP/BS scaling -> loaded-circuit verification`

## B1. 先明确 Rs/RL 与响应基准

这是新 LC 综合前必须解决的前置问题。

当前项目默认 `Rs=0`，并在 LC scaling 中用 `RL` 代替 impedance scale；而经典 doubly-terminated `g` prototype 通常把源、负载终端共同纳入综合。两种问题不能混为一谈。

建议在模型中明确：

### 模式 1：Ideal Voltage Source

- `Rs=0`；
- 目标是 `Vout/Vsource`；
- 只存在显式 RL；
- 不能默认宣称标准双端 insertion-loss prototype 在移除 Rs 后仍保持原响应；
- 必须从这个边界条件重新验证或综合。

### 模式 2：Doubly Terminated / Insertion Loss

- `Rs>0` 且 RL 按 prototype termination ratio 确定；
- 目标可以使用 transducer gain 或明确的 available-power reference；
- 适合标准 ladder `g` 综合。

建议增加 `LcTerminationMode`，但保持用户要求的默认 `Rs=0`。如果某种 approximation 目前只支持双端综合，UI 必须明确提示，而不能悄悄插入 50 Ω 源阻抗。

在新增 approximation 前，应先对现有 Butterworth/Chebyshev-I 做两种模式的 reference audit，防止新算法建立在混淆的归一化基础上。

## B2. 建立独立的 LC prototype 模型

不要继续让 `FilterPrototype.LadderGValues` 同时承担所有语义。建议增加：

### `NormalizedLcLadderPrototype`

- Approximation
- Order
- Normalization
- SourceTerminationG0
- LoadTerminationGN1
- `IReadOnlyList<LcPrototypeElement>`
- StartElement（SeriesL 或 ShuntC）
- TerminationMode
- ExpectedPowerTransfer/VoltageTransfer metadata
- SynthesisMethod
- ValidationMetrics

### `LcPrototypeElement`

- Index
- Connection（Series/Shunt）
- Kind（NormalizedInductor/NormalizedCapacitor）
- GValue

### 独立接口

- `ILcLadderPrototypeDesigner`
- `ILcLadderPrototypeVerifier`
- `LcLadderPrototypeRegistry`

`PassiveLcTransformationService` 只消费已经验证的 ladder prototype，不再根据 approximation 名称猜测。

## B3. Bessel LC：第一优先级

Bessel 是最适合先研究的新增无源 approximation。

### B3.1 推荐路线

优先实现可复现的 insertion-loss synthesis，而不是只抄一张未注明 normalization/termination 的 g-value 表：

1. 从选定的 Bessel magnitude normalization 构造目标多项式；
2. 建立 power-loss ratio；
3. 计算反射多项式并进行稳定 spectral factorization；
4. 构造正实 driving-point impedance/admittance；
5. 使用 Cauer continued-fraction extraction 提取 series L / shunt C；
6. 要求所有余项和元件值为有限正数；
7. 重建 terminated ladder 的传递函数，与目标 prototype 比较；
8. 再交给现有 LP/HP/BP/BS transformation。

### B3.2 数值注意事项

- 多项式系数必须归一化，避免 8～10 阶条件数恶化；
- spectral factor 必须选择左半平面因子；
- 极小负值只能在明确浮点容差内截为零，不能把实质负元件取绝对值；
- continued fraction 每一步都保存余项和提取记录，供报告与诊断；
- 先支持 1～6 阶，再根据数值稳定性扩到 10 阶。

### B3.3 验收

- normalized element values 与至少一组独立公开参考表一致；
- reconstructed ladder 的 pole/response 与 Bessel 目标一致；
- LP/HP frequency scaling 正确；
- BP/BS 每个 prototype element 展开为正确的 series/parallel resonator；
- Rs/RL 条件和报告中的 magnitude reference 一致；
- selected E-series L/C 后重新进行 loaded CircuitGraph AC verification。

## B4. Gaussian 与 Linear Phase LC：设置可实现性门槛

AN-649 中的 Gaussian transitional 和 equiripple Linear Phase poles 首先是 all-pole active-filter transfer prototypes。它们并不自动保证能按当前“纯无损 L/C + 指定终端”的形式提取出正的 ladder 元件。

### B4.1 逐 family、逐 order 检查

对以下 family 分别执行 synthesis feasibility：

- Gaussian-to-6 dB，order 3～10；
- Gaussian-to-12 dB，order 3～10；
- Linear Phase 0.05°，order 2～10；
- Linear Phase 0.5°，order 2～10。

每个 order 必须检查：

- power/reflection polynomial 是否可形成实系数稳定 spectral factor；
- driving-point function 是否 positive-real；
- Cauer extraction 是否得到全正 L/C；
- termination ratio 是否可接受；
- reconstructed loaded response 是否与目标一致。

### B4.2 三种可能结论

1. **全部通过**：该 family/order 正式支持 Passive LC；
2. **只在特定 termination/阶数通过**：SupportMatrix 精确到 order 和 termination mode；
3. **无法由当前 lossless ladder 实现**：保持 Unsupported，并说明需要 lossy ladder、bridged-T、transformer 或更一般网络综合；不要伪造 g 值。

## B5. Passive RC 保持现有限制

Bessel/Gaussian/Linear Phase 不应通过多个无缓冲 RC section 直接级联来宣称支持。级间加载会改变 poles。除非以后实现完整 RC ladder/driving-point synthesis，否则 Passive RC 仍限定为真实的一阶 LP/HP。

## 5. SupportMatrix 重构计划

当前名称白名单应逐步改为分层能力判定：

1. `Prototype capability`：approximation/order 是否可生成；
2. `Transformation capability`：LP/HP/BP/BS 是否有统一数学变换；
3. `Topology family capability`：是否有 response-specific solver；
4. `Section feasibility`：当前 f0/Q/gain/zero 是否存在正元件解；
5. `Range feasibility`：是否落入用户 R/C/L 范围；
6. `Standard-value feasibility`：标准值后是否满足误差和 notch depth；
7. `Final verification`：CircuitGraph loaded response 是否满足 A0/Rp/As。

建议返回结构化结果：

`FilterCapabilityEvaluation { Level, IsSupported, ReasonCode, Message, SuggestedAlternative }`

例如：

- `SallenKeyBpGainCouplingViolation -> 建议 MFB BP`
- `BainterNotchDepthInsufficientWithE24 -> 建议 E96`
- `LcPrototypeNotPositiveReal -> 当前 approximation/order 不能用纯 LC ladder`
- `LcTerminationModeMismatch -> 该 prototype 需要双端终接`

## 6. 测试计划

## 6.1 Active reference tests

- Sallen-Key BP：至少 Q=0.8、2、5，A0=0.5、1、2；
- Bainter BS：`ωp=ωz` 与 `ωp≠ωz`；
- 每个 solver 的理论值、E24、E96 三组；
- 独立 MNA frequency samples，不调用 solver 的 `Verify()`；
- 检查 op-amp 输入没有短接、feedback net 存在、reference 唯一；
- 4/6/8 阶 Active BS 级联；
- A0=2、A0<1、奇偶反相 section 的 overall gain/polarity；
- 高 Q、窄带 BS 的 dynamic-range warning。

## 6.2 Passive LC synthesis tests

- Bessel order 1～6 normalized ladder reference；
- 正实性和全部 g 值为正；
- 由 g 值重建 transfer function，而不是只检查 g 数量；
- Ideal Source 与 Doubly Terminated 两种边界分别测试；
- LP/HP/BP/BS transformation；
- Rs/RL 不匹配必须明确失败；
- Gaussian/Linear Phase 每个 order 输出明确 Supported/Unsupported 结论；
- E-series 后的 loaded CircuitGraph AC response 满足规格。

## 6.3 End-to-end acceptance cases

至少固定以下验收案例：

1. 4 阶 Butterworth Active BP Sallen-Key；
2. 5 阶 Chebyshev-I Active BP MFB；
3. 4 阶 Butterworth Active BS Bainter；
4. A0=2 的多 section Active BS，验证 gain distribution；
5. 3 阶 Bessel Passive LC LP；
6. 4 阶 Bessel Passive LC HP；
7. 3 阶 Bessel Passive LC BP/BS；
8. Gaussian/Linear Phase 的首个可实现 LC reference，或明确的 positive-real failure test。

每个案例必须验证：order、sections、理论元件、标准元件、反算 transfer、CircuitGraph、最终边缘规格和报告内容。

## 7. 建议实施顺序

### Milestone 1：验证基础

- 建立 CircuitGraph/MNA section transfer verifier；
- 冻结当前已支持组合的回归结果；
- 清理 SupportMatrix 的分层 reason code。

完成标志：现有所有支持组合通过独立 transfer verification，不能只通过 solver 自验。

### Milestone 2：Sallen-Key BP

- 重构现有 BP calculator；
- 完成独立方程和可行域；
- 完成 gain distribution、标准值和 end-to-end tests；
- 通过后才开放 SupportMatrix。

### Milestone 3：Bainter Active BS

- 审计 calculator、CircuitGraph 和三运放连接；
- 完成 zero/notch-depth-aware optimizer；
- 打通 Active BS 全链路；
- UI 对 BS 只提供正式支持的 Bainter。

### Milestone 4：LC termination 审计

- 明确 Ideal Source / Doubly Terminated；
- 重新验证现有 Butterworth/Chebyshev-I；
- 建立独立 ladder prototype model。

### Milestone 5：Bessel Passive LC

- 完成 insertion-loss/Cauer synthesis；
- 先 LP/HP，再复用现有 transform 完成 BP/BS；
- 完成标准值和 loaded AC verification。

### Milestone 6：Gaussian/Linear Phase LC 可行性与实现

- 逐 family/order 做 positive-real 和 Cauer extraction；
- 只开放验证通过的组合；
- 不可实现的组合保留明确诊断。

### Milestone 7：可选拓扑研究

- MFB BS 产品化决策；
- Sallen-Key BS 产品化决策；
- Fully Differential 独立项目。

## 8. 预计新增/调整的代码职责

建议路径可按现有结构微调，但不要创建平行 Engine：

```text
Modules/Filters/FilterDesign/
  Synthesis/
    Verification/
      ICircuitSectionTransferVerifier.cs
      CircuitSectionMnaVerifier.cs
      SectionTransferMeasurement.cs
    SallenKey/
      SallenKeyBandPassSectionSolver.cs
    Bainter/
      BainterNotchSectionSolver.cs
      BainterNotchStandardValueOptimizer.cs
    PassiveLc/
      NormalizedLcLadderPrototype.cs
      ILcLadderPrototypeDesigner.cs
      InsertionLossPolynomialBuilder.cs
      HurwitzSpectralFactorizer.cs
      PositiveRealLadderExtractor.cs
      BesselLcPrototypeDesigner.cs
      LcLadderPrototypeVerifier.cs
  Validation/
    FilterCapabilityEvaluation.cs
    FilterSupportMatrix.cs
```

`FilterDesignEngine` 继续只负责调度；approximation designer 不知道 topology；topology solver 不知道 approximation 名称；CircuitGraph builder 不重新计算元件；报告只读取最终结果。

## 9. 最终完成定义

### 有源部分完成

- SK LP/HP/BP 和 MFB LP/HP/BP 均经过独立传递函数验证；
- Active BS 至少有正式 Bainter 实现；
- 所有正式支持组合经过 selected-value CircuitGraph whole-filter verification；
- 不合理组合提前给出具体原因和替代建议；
- 不把 MFB BS/SK BS 未产品化误判为 bug。

### 无源部分完成

- termination 和响应 reference 定义明确；
- Bessel 至少完成可验证的 Passive LC LP/HP，并在 transformation 验证后支持 BP/BS；
- Gaussian/Linear Phase 只在 positive-real ladder synthesis 成功的 order/termination 上开放；
- 任何 g 值都能追溯到 synthesis/reference，并能重建目标 loaded transfer；
- 不再以“有 poles”为理由宣称可生成 LC ladder。

达到以上条件后，Filter Design 模块的 topology/approximation 主链路可以视为 v1 完成；之后的运放 GBW、容差 Monte Carlo、Group Delay/Step Response 属于下一层工程增强。
