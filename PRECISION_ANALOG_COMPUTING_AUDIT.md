# Precision Analog Computing v1 全面审计

审计日期：2026-09-12  
范围：General Linear Combination、Voltage Range Mapper、Precision Half-Wave Rectifier、Precision Full-Wave / Absolute Value、Log Amplifier、Antilog / Exponential Amplifier、Practical Integrator、Practical Differentiator。  
明确排除：Precision Limiter / Clipper（按需求不审计）。Filters 未被本次审计修改，但执行了完整回归。

状态含义：`Found` 表示确认存在问题；`Fixed` 表示问题已修复并有回归证据；`Not Found` 表示专项扫描未发现；`Intentionally Kept` 表示明确保留的 v1 边界或工程取舍。

## 最终结论

**Precision Analog Computing v1 已达到可以 FROZEN 的状态。**

- 未留下 Critical 或 High 级开放缺陷。
- 所有成功结果都来自最终选值 CircuitGraph 的 operation-specific independent verification。
- 8 个在范围内的模块均通过 E24/E96、边界、拓扑、Analysis、Schematic、Report/Netlist 及 UI 回归。
- Release build：0 warning、0 error。
- 全套测试：**134/134 PASS**，其中包含完整 Filter 回归。
- 冻结结论不代表扩大能力边界：precision rectifier 的真实器件瞬态 SPICE、结温梯度、器件失配和厂商宏模型仍属于明确声明的 v1 限制。

## Top 10 最重要发现

1. **Fixed / Critical** — Antilog 原 CircuitGraph 的参考结与指数结没有形成实现目标传递函数的真实电流路径；旧验证器却直接按理想 `exp` 判定成功。已重建 matched-junction reference、指数驱动和 transimpedance output 三段真实拓扑。
2. **Fixed / Critical** — 在改用 Shockley 方程复验后发现 Log 输出差分级极性相反。已交换 RAW_LOG/RAW_REF 在差分级的角色，使 `Vout=-S·ln(Vin/Vref)` 与图、网表和验证一致。
3. **Fixed / High** — Log/Antilog 旧验证主要复述目标公式，没有从最终电阻和结模型重算。现按最终 CircuitGraph、`Is`、`n`、温度和精确 `ln(1+I/Is)` / `exp` 关系验证。
4. **Fixed / High** — 静态非线性 verifier 缺少严格拓扑门禁，重接线仍可能通过公式验证。现对 Rectifier、Full-Wave、Log、Antilog 的关键 net/pin 集合逐一验证。
5. **Fixed / High** — Rectifier UI 主图把 Vin→Vout 结果二次转换成归一化时间正弦图。现直接显示 independent verifier 生成的 Vin→Vout transfer；重复波形服务与控件已删除。
6. **Fixed / High** — Rectifier 的 SPICE 使用 Shockley diode，而验证使用 ideal-switch，属于静默模型不一致。现明确返回 unsupported，不再导出貌似可用但与验证不同的网表。
7. **Fixed / Medium** — repeated Design 会重建 Rectifier 与 Linear Combination Analysis 控件并重复绑定事件。现按兼容 Analysis 类型复用控件，仅替换数据；20 次连续设计保持引用、数量和 Bounds 稳定。
8. **Fixed / Medium** — Full-Wave 联合优化的公共阻值尺度只搜索一个局部窗口，可能漏掉另一 decade 的更优解。现完整枚举有界 E-series 公共尺度，再联合评分正增益、负增益和 symmetry。
9. **Fixed / Medium** — validation、failure stage 和异常分类不完整，verification failure 可被误报为 ComponentRange。现由 canonical request validator 和明确的 SpecificationValidation / Synthesis / IndependentVerification 阶段统一分类。
10. **Fixed / Medium** — Calculations 页自行拼装部分结果，缺少 Continuous/Selected 对照。现复用 canonical report builder，统一显示 Target、Continuous、Selected、Actual error、公式和 warning。

## UI

- **Fixed / High** — Rectifier Analysis 改为 Vin→Vout 主图；删除非必要的三周期说明性波形路径。
- **Fixed / Medium** — Linear Combination 新增可复用 Analysis control；same-type repeated Design 不重建控件或事件。
- **Fixed / Medium** — Rectifier、Log、Antilog、Range Mapper 共用可复用 transfer plot 更新路径。
- **Fixed / Medium** — Electrical / Practical Constraints 与 Component Constraints 默认收纳到 `Advanced`，主界面只保留设计目标。
- **Fixed / Medium** — Ideal OpAmp 启用时 GBW/SR 禁用；选择 practical op-amp 后启用。Rectifier 的 Max f 只在 practical 模式显示，因为此时它才是必要设计目标。
- **Fixed / Low** — E96 是 R/C selector 的真实默认值，宿主 reparent 后不会被 CurrencyManager 重置。
- **Fixed / Low** — Log、Antilog、Integrator、Differentiator 按模块保存各自输入状态，切换后不串值。
- **Not Found / High** — Design 路径没有 `Controls.Clear()`、整窗 `Refresh()`、`Update()`、`Invalidate(true)` 或 `Application.DoEvents()`。
- **Not Found / Medium** — 100%/125%/150% DPI 自动化布局检查未发现 operation fields 重叠、残留偏移或丢失。
- **Not Found / Medium** — 连续 20 次 Rectifier、Log/Antilog、Integrator/Differentiator Design 未改变 result TabPage 数量、控件引用或 Bounds。
- **Intentionally Kept / Low** — 在切换不兼容的 Analysis 类型或 success/failure view 时，host 会替换其唯一 child；同类型 Design 只更新数据。这是页面类型切换，不是每次 Design 重建 UI。

## Architecture

- **Fixed / High** — 当前 canonical 流程为：Request specification → `AnalogComputeRequestValidator` → operation synthesizer/optimizer → final `CircuitGraph` → operation verifier → `AnalogComputeDesignResult` → UI render。
- **Fixed / Medium** — 从 Wizard 删除旧的 `CreateInteractiveAnalysisControl`、Range Mapper/Linear Combination 重复 builder 和分析事件绑定实现。
- **Fixed / Medium** — 从 code-behind 删除 Range Mapper 的电气 scale/offset 重算；实际换算只在 engine 中完成。
- **Fixed / Medium** — 删除各 synthesizer 内重复的基础 specification validation，由 request validator 作为 canonical implementation。
- **Fixed / Low** — 删除未使用的 tolerance、offset/bias request 字段、旧 capability enum 值和废弃 range-feasibility 开关。
- **Not Found / High** — UI/Controls 不包含元件求解、E-series 搜索、CircuitBuilder 或 independent verification。
- **Not Found / Medium** — Domain synthesis/validation 不引用 WinForms；Core 未引入 Precision Analog domain-specific 类型。
- **Not Found / Medium** — 模块内 E-series、DC MNA、AC MNA、SPICE serializer、result model 均只有一个 canonical 实现。
- **Intentionally Kept / Low** — `PrecisionAnalogWizardControl` 仍是较长的 UI composition class，但职责限于控件构造、request snapshot 和局部 render，不再承担电气算法。冻结前不做纯风格拆分。
- **Intentionally Kept / Low** — operation-aware schematic layout service 较长，但保持单一“由 CircuitGraph 生成布局”的职责；为统一而拆成大量小类没有质量收益。

## Topology

- **Not Found / High** — General Linear Combination 使用 sign-partitioned inverting summers，最终图、MNA、Schematic 和 SPICE 都读取同一个 CircuitGraph。
- **Not Found / High** — Range Mapper 只把端点规格 canonical 化为 affine request，之后完全走同一 affine CircuitGraph 流程。
- **Not Found / Critical** — Half-Wave 是两二极管 feedback-clamped precision rectifier；positive half-wave 包含真实 polarity-recovery op-amp，不是软件 clamp。
- **Not Found / Critical** — Full-Wave 使用 precision half-wave core、weighted absolute-value summer，并按输出极性选择真实 recovery stage；正负输入侧均验证 `K|Vin|`。
- **Fixed / Critical** — Log 的 matched input/reference junction 与差分比例级现在实现正确负极性。
- **Fixed / Critical** — Antilog 现在包含 reference log cell、scaled exponent drive、exponential junction 和 transimpedance output，结电流真实流过 D1/R8。
- **Not Found / Critical** — Integrator CircuitGraph 为 `Rin + (Rf || Cf)`，传递函数为 `-(Rf/Rin)/(1+sRfCf)`。
- **Not Found / Critical** — Differentiator CircuitGraph 为 `Cin-Rin` 串联输入与 `Rf || Cf` 反馈，传递函数为 `-RfCin·s/[(1+sRinCin)(1+sRfCf)]`。

## Math

- **Fixed / High** — Request validator 覆盖 NaN、Infinity、rail 顺序、零/负规格、重复 input name、nominal 超范围、空/超 8 inputs、非法 enum 和空 constraints/model。
- **Fixed / High** — Log 明确拒绝 `Vin<=0`；Antilog 明确守卫 `|Vin/S|<=80`，并在最终结电流验证中阻止电气过流。
- **Fixed / High** — Log/Antilog 使用 Shockley junction、温度热电压、`Is`、`n` 和实际 divider/current resistor；不再以理想 ln/exp 冒充电路。
- **Not Found / High** — Integrator 的 Ki、leak fL、DC gain 与最终 R1/R2/C1 关系一致。
- **Not Found / High** — Differentiator 的 Kd、两个 limiting poles、Useful fMax=first pole/5 与最终 R1/R2/C1/C2 一致。
- **Fixed / Medium** — output swing 在所有模块统一为成功结果中的 warning + Analysis clipping，不再有的模块 hard fail、有的模块忽略。
- **Not Found / Medium** — SPICE 数值使用 invariant serializer 和 SPICE suffix 规则；未复用 UI engineering formatter。

## Optimization

- **Not Found / Critical** — 默认 R/C series 均为 E96，E24/E48/E96 由同一个 preferred-value implementation 生成。
- **Fixed / Medium** — E48/E96 base arrays 缓存，不再在每次候选生成时重新计算。
- **Not Found / High** — Affine 对公共 feedback resistor 做完整有界 E-series 搜索，并对每个最终比值重新评分，不是逐个 nearest rounding。
- **Fixed / Medium** — Half-Wave 与 junction ratio/divider 在完整有界尺度上搜索；不再只检查单一局部尺度。
- **Fixed / Medium** — Full-Wave 对公共尺度完整搜索，并同时评分 positive gain、negative gain、symmetry 和 resistor spread。
- **Not Found / High** — Integrator 联合搜索 Cf/Rin/Rf，同时评分 Ki 与 fL。
- **Not Found / High** — Differentiator联合搜索 Cin/Rin/Rf/Cf，同时评分 Kd、两极点、全频带 magnitude bound 和 phase bound。
- **Not Found / High** — 每个候选成功与否最终由选值 CircuitGraph verifier 决定；优化器分数不能直接设置 verified=true。

## Verification

- **Not Found / Critical** — Integrator/Differentiator 使用公共 `CircuitAcAnalysisService` 对最终 CircuitGraph 做 MNA AC verification。
- **Fixed / Critical** — Piecewise 与 junction verifier 对关键 net 的精确 pin membership 做拓扑门禁，再从 graph 元件值独立重算 transfer。
- **Not Found / High** — Affine verifier 用 `LinearCircuitDcVerifier` 从 CircuitGraph 解 coefficient 与 offset。
- **Not Found / High** — 所有 `Success=true` 结果都有非空且全部 PASS 的 verification metrics。
- **Fixed / High** — verification exception/failure 归到 IndependentVerification；不再笼统映射为 ComponentRange。
- **Not Found / Medium** — 当前模型不暴露含混的 TheoryCircuit/TheoryMeets/SelectedCircuit/SelectedMeets 组合；Continuous component values、Selected graph 和 verification metrics 含义分离。

## Analysis

- **Fixed / High** — Linear、Mapper、Half/Full Rectifier、Log、Antilog 的主 Analysis 均为 Vin→Vout transfer。
- **Not Found / High** — Target/Continuous 与 Selected/Actual 数据来自不同路径；测试会修改最终元件并确认实际曲线随之变化。
- **Not Found / High** — Integrator 包含 Magnitude、Phase、square→triangle，且正输入对应负斜率。
- **Not Found / High** — Differentiator 包含 Magnitude、Phase、triangle→square，且正斜率对应负输出。
- **Fixed / Medium** — Dynamic verifier 与 SPICE exporter 共用 canonical sweep start/stop；Differentiator sweep 包含 Useful fMax 与两限制极点。
- **Not Found / Medium** — demo frequency/amplitude 自动选在有效工作区，不新增用户输入；图上已移除 demo frequency/amplitude 说明行。
- **Not Found / Medium** — corner/Useful markers 和 legend 与实际数据语义一致；数据更新只触发局部 `Invalidate()`。

## Schematic

- **Fixed / High** — Log/Antilog 专用布局按功能块重新整理，同时保持修正后的实际 net topology。
- **Not Found / High** — 所有布局从最终 CircuitGraph 和 selected component values 生成，不重新计算拓扑。
- **Not Found / Medium** — operation-specific collision、wire-through-body、label overlap 和左右模块顺序测试通过。
- **Not Found / Medium** — 连续设计通过替换 immutable `SchematicDocument` 清除旧线段/文字，不在 OnPaint 中修改控件树。
- **Not Found / Low** — OnPaint 中临时 Pen/Brush 均释放；未发现每次刷新泄漏 Font/Bitmap 的路径。

## Calculations / Report

- **Fixed / Medium** — Calculations 复用 canonical text report builder，包含 Target、Continuous、Selected、Actual、Error、Tolerance、公式与 warning。
- **Fixed / Low** — calculation practical notes 不再被 text report 丢弃。
- **Fixed / Low** — HTML component table 将含混的 `Theoretical` 标题改为 `Continuous`。
- **Not Found / High** — Report/JSON/Schematic/Netlist 均引用当前 `renderedResult`；未发现引用上次结果的缓存。
- **Not Found / High** — failure 状态不生成成功元件表或网表，Calculations 明确显示 FailureStage/Reason/Alternative。

## Diagnostics

- **Fixed / High** — 新增并贯通 FailureStage：SpecificationValidation、Synthesis、IndependentVerification。
- **Fixed / High** — 明确区分 InvalidInput、SupplyRailsInvalid、ComponentRangeImpossible、StandardValueAccuracyInsufficient、CircuitVerificationFailed、GBW、Slew Rate、JunctionCurrent 与 Unsupported Topology。
- **Fixed / Medium** — 未预期异常转换为 InternalError，并带所在阶段；不会直接冲出 engine 使应用崩溃。
- **Intentionally Kept / Medium** — output swing 是统一 warning，而非 hard failure；Analysis 明确显示 clipped samples。该政策适合“拓扑可实现但给定幅度会饱和”的 v1 语义。

## Performance

测量环境为同一工作区 Release 配置；wall-clock 会受文件缓存、JIT 和机器负载影响。

| 项目 | Baseline | After | 说明 |
|---|---:|---:|---|
| Incremental Release build | 1588.3 ms | 1427.9 ms | 0 warning / 0 error |
| Full regression | 130 tests / 16021.9 ms | 134 tests / 9988.1 ms | 测试增加 4 项；时间仅作趋势参考 |
| Core design | 未单列 | 40 designs / 1747.5 ms | 含 validation、synthesis/optimization/graph、verification/analysis |
| BuildRequest | 未单列 | 100 calls / 0.7 ms | UI-thread request snapshot |
| Result render update | 未单列 | 160 updates / 203.4 ms | 不含真实屏幕 GDI paint 和 lazy WebBrowser navigation |

- **Fixed / Medium** — E48/E96 base series 缓存；删除 rectifier transfer→waveform 的重复 sampling。
- **Fixed / Medium** — repeated render 复用 Analysis control，减少 immutable UI 树和 event handler 重建。
- **Not Found / Medium** — 未发现明显设计热点或 UI-thread 卡顿；核心设计在 `Task.Run` 中执行，render 留在 UI thread。
- **Intentionally Kept / Low** — synthesizer 当前把连续解、优化和 CircuitGraph build 作为一个紧密阶段，performance trace 将其合并计时；为纯计时拆散事务边界会降低可读性。

## Threading

- **Not Found / High** — `_designing` 防止快速重复点击重入，并在 `finally` 中恢复。
- **Fixed / High** — await 返回后先检查 IsDisposed/Disposing；Dispose 后不再更新控件。
- **Fixed / Medium** — 设计期间若 operation 已切换，旧 request 的结果会被丢弃，不覆盖新页面。
- **Not Found / High** — background task 只访问 immutable request/engine；WinForms 控件只在 UI thread 读写。
- **Not Found / High** — 没有 `Application.DoEvents`，没有后台线程直接访问 Control。

## Netlist

- **Not Found / Critical** — exporter 序列化最终 CircuitGraph，不重新合成元件或拓扑。
- **Fixed / High** — precision rectifier 因 v1 verifier 使用 ideal-switch，SPICE 明确 unsupported，避免静默导出不同 Shockley 行为。
- **Not Found / High** — Log/Antilog 导出相同 D1/D2 topology、同一 `Is`、`n`、temperature 与最终 selected resistors。
- **Fixed / Medium** — Integrator/Differentiator AC sweep 与 verifier 共用 DynamicOperationParameters。
- **Not Found / Medium** — DC/AC source、VCC/VEE、VOUT expression 和 op-amp strategy 均由公共 SPICE infrastructure 生成。

## Tests

- **Fixed / High** — 新增 8 operations × E24/E96 operation matrix；所有成功结果必须有全 PASS independent metrics。
- **Fixed / High** — 新增 NaN/Infinity、重复输入名、nominal 越界、端点退化、反转范围、Log 非正域、Antilog overflow、动态零/NaN 规格矩阵。
- **Fixed / High** — 新增所有模块窄组件范围的 verified-or-precise-failure 回归。
- **Fixed / High** — 新增 Rectifier、Integrator、Differentiator practical GBW/SR pressure 分类。
- **Not Found / High** — Full-Wave 正负极性/正负半轴 symmetry、Log domain、Antilog reference/overflow、Integrator/Differentiator Bode 和 time-domain polarity 均有测试。
- **Not Found / Medium** — repeated Design 20 次、DPI 100/125/150、Schematic collision/wire、Report/JSON、SPICE invariant formatting 均通过。
- **Not Found / Critical** — 最终结果：**134 tests passed, 0 failed**；完整 Filter 回归通过。

## 修改文件

本次审计涉及的 Precision Analog 源码：

- `Models/AnalogComputeModels.cs`
- `Engines/PrecisionAnalogDesignEngine.cs`
- `Validation/AnalogComputeRequestValidator.cs`
- `Validation/AffineDesignVerifier.cs`
- `Validation/PiecewiseOperationVerifier.cs`
- `Validation/JunctionOperationVerifier.cs`
- `Validation/DynamicOperationVerifier.cs`
- `Synthesis/AffineNetworkSynthesizer.cs`
- `Synthesis/AnalogPreferredValueSeries.cs`
- `Synthesis/PiecewiseOperationSynthesizer.cs`
- `Synthesis/JunctionOperationSynthesizer.cs`
- `Synthesis/DynamicOperationSynthesizer.cs`
- `Synthesis/DynamicOperationParameters.cs`
- `Analysis/DynamicTimeDomainAnalysisService.cs`
- `Controls/PrecisionAnalogWizardControl.cs`
- `Controls/LinearCombinationAnalysisControl.cs`
- `Controls/DynamicAnalysisControl.cs`
- `Controls/DynamicWaveformPlotControl.cs`
- `Controls/AffineTransferPlotControl.cs`
- `Controls/AnalogBodePlotControl.cs`
- `Schematic/PrecisionAnalogSchematicLayoutService.cs`
- `Reporting/PrecisionAnalogDesignReportBuilder.cs`
- `Reporting/PrecisionAnalogDesignReportService.cs`
- `Services/PrecisionAnalogSpiceExportService.cs`
- `Tests/CircuitModuleDesigner.CoreTests/Program.cs`

Filters 源码未由本审计改动；工作区中已有的 Filter 修改被保留，并由 134 项全量回归覆盖。

## 删除的重复或废弃代码

- 删除 `RectifierWaveformAnalysisService` 与 `RectifierWaveformPlotControl`：主 Analysis 直接使用 canonical Vin→Vout verification result。
- 删除 Wizard 内旧 `CreateInteractiveAnalysisControl`、`CreateRangeMapperAnalysisControl`、`CreateLinearCombinationAnalysisControl`、`CreateAnalysisLayout`。
- 删除 code-behind Range Mapper scale/offset 计算。
- 删除 synthesizer 中重复的 specification validation。
- 删除未使用 request tolerance/offset/bias 字段、range-feasibility 开关、废弃 capability enum 值。
- E48/E96 生成改为单一缓存实现。

## 仍然存在的已知限制

- **Intentionally Kept / Medium** — Precision rectifier v1 的静态验证是 ideal-switch piecewise 模型；未宣称真实 diode recovery/crossover transient 精度，因此不提供误导性的 nonlinear SPICE 导出。
- **Intentionally Kept / Medium** — Log/Antilog 假设 matched junction、等温和给定 Shockley 参数；器件失配、自热、温度梯度和封装寄生需要器件级选择与校准，Report 会明确警告。
- **Intentionally Kept / Low** — 静态模块的 op-amp 是抽象约束，不替代厂商 macro-model；动态模块和 practical rectifier 提供 GBW/SR gate。
- **Intentionally Kept / Low** — 自动 UI 测试覆盖逻辑 DPI scaling/resize 和稳定性，不等同于每块实体显示器上的人工视觉验收。
- **Intentionally Kept / Low** — headless render benchmark 不包含实际 GDI paint、打印和 WebBrowser HTML navigation 时间。
