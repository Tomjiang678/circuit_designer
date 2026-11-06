# 多频 / 宽带阻抗匹配扩展规划

> 状态：Phase 0–2 已落地；当前实现包含频段模型、统一硬约束网格、单节优化和自动多节 Pi/T ladder。
> 目标模块：`Modules/SignalAttenuationMatching`  
> 适用范围：无源 LC 阻抗匹配；有源匹配、传输线/微带和电磁仿真不在本阶段范围内。

---

## 1. 背景与现状

当前模块的 `Impedance Matching` 是**单频匹配**：

- 在 `Design frequency (f0)` 处做共轭匹配；
- L/Pi/T 网络按 f0 的源、负载阻抗综合；
- Pi/T 的 `Design Q` 只影响单频综合的虚拟电阻和元件值；
- 频响扫描、回波损耗和 Smith 图只用于**展示和验证**，没有参与综合；
- 候选的“带宽”是扫描后统计出来的结果，不是设计目标。

这带来两个限制：

1. 不能保证一段频带内的回波损耗、插入损耗或 VSWR；
2. 不能因为用户要求“10–20 MHz 内 RL ≥ 20 dB”而自动增加网络阶数或调整拓扑。

Pi/T 拓扑本身有宽带潜力，但当前只用了它在 f0 的三个方程自由度，没有把多个频点纳入目标函数。

---

## 2. 目标与非目标

### 2.1 目标

新增一个与单频模式并列的 **Broadband / Multiband Matching** 设计模式，支持：

- 连续频段：`[f_start, f_stop]`；
- 多个离散频段：例如 2.4 GHz 和 5 GHz；
- 带内目标掩模（mask）：
  - 最小输入/输出回波损耗；
  - 最大插入损耗；
  - 可选 VSWR 目标；
  - 每个频段独立权重；
- 源/负载：
  - 单频 `R+jX`；
  - 频率相关 series R-L / R-C；
  - 完整 Touchstone 频响数据（S1P/S2P）；
- 自动/手动拓扑；
- 多节网络；
- 连续值和 E12/E24/E48/E96 标准值；
- 独立的多频点最终验证；
- 与当前 Schematic、SPICE、HTML/JSON、`.cmddesign` 一致的交付链路。

### 2.2 非目标（本阶段）

- 有源匹配、负阻抗、非福斯特匹配；
- 传输线、微带、PCB 电磁仿真；
- 完整厂商器件模型和封装模型；
- 自动生成天线/滤波器联合设计；
- 用单频点数据假装成完整宽带测量。

---

## 3. 核心设计原则

1. **单频模式保留**：单频是宽带的一个特例，不强行把现有流程改成宽带。
2. **同一个 `CircuitGraph`**：综合、分析、原理图、SPICE、报告和验证继续使用最终图。
3. **综合与验证分离**：综合器只给候选；最终必须经过独立 MNA 验证。
4. **频段目标显式化**：带宽不是“扫描后顺便算出来”，而是用户输入的目标。
5. **测量数据模型显式化**：单频 R+jX、series L/C、完整 VNA 表是三种不同的模型。
6. **失败要可解释**：Fano/Bode 极限、数据范围不足、标准值不可达，都要给出明确失败原因。
7. **UI 渐进披露**：默认流程简单，只有选择宽带模式后才出现频段、多节和优化预算。

---

## 4. 用户侧一体化流程

### 4.1 推荐向导结构

保留现有单频流程，在其前面增加模式分叉：

```text
1. Design Mode
   ├─ Single-frequency Matching
   ├─ Broadband / Multiband Matching
   └─ Attenuation (existing)
2. Source & Load
3. Band / Performance Targets
4. Topology & Network Size
5. Component Constraints
6. Candidate & Verification
```

单频模式隐藏 Band 页；宽带模式才显示。

### 4.2 Step 1：模式选择

| 选项 | 行为 |
|---|---|
| Single-frequency Matching | 现有 L/Pi/T，按 f0 综合 |
| Broadband / Multiband Matching | 显示频段编辑器，按频段掩模优化 |
| Attenuation | 现有电阻 Pad，不进入 LC 宽带流程 |

不要用“扫描起止频率”直接推断用户要做宽带；扫描范围和设计目标是两件事。

### 4.3 Step 2：Source & Load

每个端口支持三种模型：

| 模型 | 用户输入 | 内部行为 |
|---|---|---|
| Single-frequency R+jX | R、X @ f0 | 只在 f0 使用该复阻抗 |
| Series R-L / R-C | R、X @ f0 | 转成 L 或 C，随频率变化 |
| VNA table | `.s1p` / `.s2p` | 使用整条测量曲线插值 |

VNA 导入要求：

- 选择 S11 或 S22（S2P 时弹窗选择）；
- 明确参考阻抗 Z0；
- 明确测量频率范围；
- 频率范围必须覆盖整个目标频段；
- 不允许外推；
- 显示“design-frequency equivalent”与“full measured curve”的区别；
- 报告和 `.cmddesign` 保存来源、端口、Z0、范围。

**关键原则：**
单频 R+jX 不能自动推导成整条 VNA 曲线；只有用户明确选择 series L/C 或 VNA table，才使用频率相关模型。

### 4.4 Step 3：频段与目标

#### 连续频段

```text
Band 1: 10 MHz – 20 MHz
  Target input RL:  ≥ 20 dB
  Target output RL: ≥ 15 dB (optional)
  Max insertion loss: ≤ 1.0 dB
  Weight: 1.0
```

#### 多频段

```text
Band 1: 2.40 – 2.50 GHz, RL ≥ 15 dB, weight 1.0
Band 2: 5.15 – 5.85 GHz, RL ≥ 12 dB, weight 0.7
```

#### 输入项

- `Start frequency` / `Stop frequency`；
- 频段数量；
- 每个频段的目标 RL、可选输出 RL、最大插入损耗；
- 权重；
- 频率采样策略：
  - 自动：按频段跨度和分析器分辨率生成网格；
  - 手动：每频段最少点数；
- 可选相位/群时延目标（后续版本）。

#### 校验

- 频段不能重叠或必须显式合并；
- 起点 < 终点；
- 目标 RL 为正；
- 插入损耗非负；
- 目标频段必须落在端口测量范围内；
- 目标带宽/变换比超出 Fano 可行域时提前警告，不进入无意义的长时间优化。

### 4.5 Step 4：拓扑与网络规模

#### 自动模式

用户只需给：

- 最大节数 / 最大元件数；
- 可用响应：Low-pass / High-pass / Band-pass；
- 是否允许混合电抗；
- 候选预算；
- 是否允许级联。

系统枚举：

- 单节 L/Pi/T；
- 多节 L/Pi/T 级联；
- 低通/高通/带通 ladder；
- 以单频解作为初值的候选。

#### 手动模式

- 拓扑族；
- 节数；
- 响应类型；
- 目标 Q / 纹波；
- 元件数量上限。

#### 重要约束

- Pi/T 单节的 3 个元件无法覆盖任意宽带；多节是必要能力；
- 不允许为了“看起来宽带”而无限加元件；
- 每增加一节都必须经过最终验证和元件数量惩罚。

### 4.6 Step 5：器件约束

沿用现有：

- L/C 范围；
- E12/E24/E48/E96；
- ESR、Inductor Q、串联电阻；
- 最大电阻功耗；
- 搜索组合上限。

宽带模式新增或预留：

- 最大元件数；
- 最大节数；
- 电容耐压/纹波电流；
- 电感 RMS/峰值/饱和电流；
- SRF；
- 高频有效工作区；
- 元件降额。

如果额定值尚未填写，候选不能直接标 `Supported`，最多 `Warning`，或标记为“未完成器件级验证”。

### 4.7 Step 6：综合、优化与候选

执行链路：

```text
Band plan
  → frequency grid
  → seed generation
  → topology enumeration
  → continuous optimization
  → standard-value optimization
  → independent MNA verification
  → Pareto/score ranking
  → candidate list
```

UI 显示：

- 频段内最差回波损耗及对应频率；
- 最大插入损耗；
- 是否所有频段通过；
- 网络节数/元件数；
- 最大器件应力；
- 标准值状态；
- 失败原因。

候选表不应只显示 RL In。至少应显示：

```text
RL In worst | RL Out worst | Max IL | Band pass | Components | Status
```

详细指标放在详情区，避免主表过宽。

### 4.8 Step 7：验证与导出

导出前必须：

- 使用最终 `CircuitGraph` 重新做全频段 MNA；
- 包含频段边界、中心点和至少一个自适应加密点；
- 检查所有频段目标；
- 检查被动性、元件范围、标准值和功耗；
- 检查 SPICE 与内部模型一致；
- 失败候选禁止导出 `Supported` 结果。

导出内容：

- Schematic；
- Smith 图；
- 频响曲线；
- 元件表；
- SPICE design/testbench；
- HTML/JSON；
- `.cmddesign`；
- 频段掩模和测量来源元数据。

---

## 5. 算法方案

### 5.1 数据模型

建议新增独立模型，不把宽带字段塞进当前单频 `AttenuationMatchingDesignRequest`：

```csharp
public sealed record BandSegment
{
    public required double StartFrequencyHz { get; init; }
    public required double StopFrequencyHz { get; init; }
    public required double TargetInputReturnLossDb { get; init; }
    public double? TargetOutputReturnLossDb { get; init; }
    public double MaximumInsertionLossDb { get; init; } = double.PositiveInfinity;
    public double Weight { get; init; } = 1.0;
}

public sealed record BroadbandSpecification
{
    public required IReadOnlyList<BandSegment> Bands { get; init; }
    public int MinimumPointsPerBand { get; init; } = 31;
    public int MaximumSections { get; init; } = 4;
    public int MaximumComponents { get; init; } = 12;
    public bool AllowCascadedSections { get; init; } = true;
}
```

端口模型建议扩展为：

```csharp
public interface IPortImpedanceModel
{
    Complex ImpedanceAt(double frequencyHz);
    double MinimumFrequencyHz { get; }
    double MaximumFrequencyHz { get; }
    string ModelDescription { get; }
}
```

实现：

- `SingleFrequencyPortModel`；
- `SeriesRlcPortModel`；
- `TouchstonePortModel`。

### 5.2 频率网格

不要直接用 UI 的扫描点做优化点。优化网格应独立生成：

- 每个频段至少 `MinimumPointsPerBand`；
- 对数频率优先，低 Q 窄带可线性；
- 强制包含：
  - 每个频段起点/终点；
  - 频段几何中心；
  - 设计频率 `f0`（如果落在频段内）；
  - 已有扫描的极值点；
- 优化过程中根据误差极值自适应加密；
- 最终验证使用比优化更密的网格。

### 5.3 误差与目标函数

对每个频点 `f`：

```text
E_RL_in(f)  = max(0, targetRLin(f)  - actualRLin(f))
E_RL_out(f) = max(0, targetRLout(f) - actualRLout(f))
E_IL(f)     = max(0, actualIL(f)    - maxIL(f))
```

加权误差：

```text
J = max_f [ w(f) * max(E_RL_in, E_RL_out, E_IL) ]
```

或使用平滑的 p-norm 近似，便于连续优化：

```text
J_p = ( Σ w_i * e_i^p )^(1/p)
```

推荐：

- **连续优化阶段**：加权最小二乘 + IRLS（迭代重加权最小二乘）逼近 minimax；
- **最终排序阶段**：真正的最小最大误差 + 元件数量/应力惩罚；
- **标准值阶段**：离散组合搜索，不能用连续值冒充。

### 5.4 初值生成

宽带优化不能从随机值开始。初值来源：

1. 当前单频 Pi/T 解：
   - 在频段中心、下边带、上边带分别综合；
   - 作为多起点。
2. 低通/高通原型：
   - Butterworth/Chebyshev 原型 + 阻抗缩放；
   - 带通变换。
3. 多节 L/Pi/T 级联：
   - 每节按局部阻抗变换生成；
   - 再用整体优化修正。
4. 用户手动拓扑：
   - 手动值作为初值。

### 5.5 连续优化

候选变量：

```text
x = [L1, C1, L2, C2, ...]
```

约束：

- 元件值上下界；
- 节数固定时的拓扑合法性；
- 被动性；
- 不允许负元件。

建议算法：

- **有界坐标下降 / pattern search**：实现简单，适合初版；
- **Nelder–Mead**：无导数，适合 3–10 个变量；
- **Levenberg–Marquardt**：需要误差雅可比，适合最小二乘；
- **IRLS**：从最小二乘逐步逼近 minimax；
- **多起点**：中心、下边带、上边带、原型解。

不引入第三方数值库；优先使用项目内已有数学工具和自实现小规模优化器。

### 5.6 标准值离散优化

连续解不能直接当最终值。流程：

1. 对每个元件生成邻近标准值集合；
2. 以连续解为参考排序；
3. 使用 beam search / 分支限界，而不是简单笛卡尔积截断；
4. 每评估一个组合都重新构建 `CircuitGraph` 并 MNA；
5. 用真实频段指标排序；
6. 记录搜索预算和是否被截断。

候选预算建议：

```text
MaximumCombinations: 20k–200k
Beam width: 32–128
最多保留候选: 20–50
```

超过预算时报告 `bounded search`，不能声称全局最优。

### 5.7 多节拓扑

单节 Pi/T 只有 3 个电抗元件，无法覆盖任意宽带。建议分两级：

#### 第一级：单节 Pi/T 的多频优化

- 复用现有 Pi/T；
- 把 Q 作为带宽/纹波参数；
- 用频段误差优化 L/C；
- 适合窄带/中等带宽；
- 实现成本最低。

#### 第二级：级联多节

- 2–4 节 L/Pi/T；
- 每节独立低通/高通；
- 节间用虚拟阻抗连接；
- 总元件数受限；
- 需要新的 `CascadedMatchingSpec` 和布局。

不建议用“无限增加 Pi/T”解决问题；每节都要有元件数量惩罚和验证。

### 5.8 Fano / Bode 可行性检查

在优化前做快速可行性判断：

- 负载 Q；
- 阻抗变换比；
- 目标带宽；
- 最大允许回波损耗；
- 可用元件数。

如果明显超出无源匹配极限，直接给出：

```text
BANDWIDTH_LIMIT
```

并解释需要更多节数、降低带宽目标或改变负载。不要花几分钟优化一个理论上不可行的目标。

### 5.9 多频段与连续频段统一

统一用 `BandSegment` 列表表示：

- 一个连续频段：一个 segment；
- 多频段：多个 segment；
- 频段间不要求匹配，优化只对 segment 内部计分；
- 频段之间可以设置“不关心”区间。

### 5.10 验证与优化分离

优化器可以使用粗网格、连续值、近似梯度；最终验证必须：

- 使用最终 `CircuitGraph`；
- 使用最终标准值；
- 使用密集频段网格；
- 重新做 MNA；
- 不读取优化器缓存性能；
- 对每个频段单独判定；
- 保存最差点频率。

---

## 6. 软件架构建议

### 6.1 新增层次

```text
Broadband/
  Models/BroadbandModels.cs
  Analysis/BandPlan.cs
  Analysis/FrequencyGrid.cs
  Analysis/BandMetrics.cs
  Synthesis/BroadbandSeedGenerator.cs
  Synthesis/CascadedMatchingSynthesizer.cs
  Optimization/BroadbandOptimizer.cs
  Optimization/StandardValueSearch.cs
  Validation/BroadbandVerifier.cs
  Engines/BroadbandMatchingEngine.cs
  Reporting/BroadbandReportBuilder.cs
  Controls/BroadbandBandControl.cs
```

### 6.2 复用现有能力

- `CircuitGraph`；
- `CircuitBuilder`；
- `TwoPortNetworkAnalyzer` 的 MNA 核心；
- `NetworkTopologyBuilder`；
- `StandardValueSeries`；
- `AttenuationMatchingCircuitVerifier` 的被动性/元件检查逻辑；
- Schematic、SPICE、报告和 `.cmddesign` 基础设施。

### 6.3 引擎接口

```csharp
public interface IBroadbandMatchingEngine
{
    BroadbandDesignResult Design(BroadbandDesignRequest request, CancellationToken cancellationToken);
}
```

请求中应包含：

- 端口模型；
- 频段计划；
- 拓扑策略；
- 元件约束；
- 优化预算；
- 目标函数权重。

结果中应包含：

- 候选列表；
- 每个频段的指标；
- 最差点；
- 验证诊断；
- 搜索是否截断；
- 可行性警告。

### 6.4 状态与序列化

`.cmddesign` 快照必须版本化：

```json
{
  "schema": "CircuitModuleDesigner.BroadbandMatching.v1",
  "bandPlan": { ... },
  "ports": { ... },
  "topology": { ... },
  "constraints": { ... },
  "selected": { ... }
}
```

兼容策略：

- 旧单频快照继续按单频读取；
- 新宽带快照不伪装成单频；
- 缺失字段使用默认值，但必须报告“旧快照”；
- 不把扫描范围自动解释成频段目标。

---

## 7. UI/交互细节

### 7.1 不要做什么

- 不在单频页面直接增加“Start/Stop”就宣称宽带；
- 不把每个频点都塞进候选表；
- 不让用户手动填写几十个频点；
- 不用“带宽”自动反推用户目标；
- 不在没有频段目标时显示“宽带优化完成”。

### 7.2 频段编辑器

建议一个紧凑的表格：

| Band | Start | Stop | RL In | RL Out | Max IL | Weight |
|---|---|---|---|---|---|---|
| 1 | 10 MHz | 20 MHz | 20 dB | 15 dB | 1.0 dB | 1.0 |
| 2 | 50 MHz | 60 MHz | 15 dB | – | 2.0 dB | 0.7 |

按钮：

- Add band；
- Remove band；
- 从 VNA 范围填充；
- 从当前扫描范围复制。

### 7.3 优化过程

- `Design` 异步执行；
- 显示阶段：生成初值 → 连续优化 → 标准值 → 验证；
- 支持取消；
- 不阻塞 UI；
- 失败时显示首个硬约束，不弹一长串内部日志。

### 7.4 结果展示

主图：

- 全频段 RL；
- 插入损耗；
- 目标掩模；
- 失败频段高亮。

候选表：

```text
Rank | Topology | Sections | RL worst | IL max | Band pass | Status
```

详情：

- 每频段指标；
- 最差点；
- 元件值；
- 是否标准值；
- 搜索是否截断；
- 器件应力。

Smith 图可以保留，但不要用单频匹配的“圆心”作为唯一成功标准。

---

## 8. 分阶段实施路线

### Phase 0：数据与评估基础

- 引入 `BandSegment`、`BroadbandSpecification`；
- 引入完整 VNA 端口表；
- 实现频段网格和 `BandMetrics`；
- 用现有单频候选评估宽带指标；
- 不改变单频综合。

**验收：** 给定一个候选，能正确报告每频段 RL/IL/最差点；不把单点结果当宽带。

### Phase 1：单节 Pi/T 多频优化

- 用中心/边带单频解作为初值；
- 对 3 个 L/C 做有界连续优化；
- IRLS/minimax；
- 标准值搜索；
- 独立验证。

**验收：** 50→200 Ω、10%–20% 带宽的已知案例通过；单频模式不回归。

### Phase 2：多节级联

- 2–4 节 L/Pi/T；
- 节数/元件数上限；
- 级联布局；
- 元件应力；
- 标准值离散优化。

**验收：** 更宽频段可覆盖；元件数量有惩罚；不会无意义增加节数。

### Phase 3：完整 VNA 与多频段

- 全曲线端口；
- 多频段掩模；
- 数据范围/插值/不外推；
- 报告与 `.cmddesign`；
- 测量来源元数据。

### Phase 4：器件级验证

- 电容耐压/纹波；
- 电感饱和电流/SRF；
- 降额；
- 高功率无效候选拒绝。

> LC 额定值不是宽带算法的前置条件，但它是“工程闭环”的前置条件；没有它，宽带候选最多只能标 Warning。

---

## 9. 测试计划

### 9.1 回归

- 现有 150 项测试全部保留；
- 单频 L/Pi/T 结果不得因宽带扩展改变；
- 单频 `.cmddesign` 可读；
- SPICE 单频行为不变。

### 9.2 算法正确性

- 50→200 Ω、10 MHz 中心、10% 带宽；
- 50→200 Ω、20% 带宽；
- 50→1000 Ω 窄带；
- 高 Q 负载；
- 复阻抗负载；
- 多频段；
- Fano 不可行案例必须失败并给出原因；
- 标准值边界；
- 搜索预算截断必须报告。

### 9.3 一致性

- 内部 MNA vs SPICE 的 L/C 值；
- 优化网格 vs 最终密集验证；
- HTML/JSON 带宽与候选 Sweep 一致；
- 直连/单频/宽带三种模型状态不混淆。

### 9.4 UI 回归

- 单频模式不显示频段页；
- 宽带模式不要求手动填写设计频率；
- 频段超出 VNA 范围时阻止开始；
- 取消优化不留下半成品结果；
- 导出按钮只对通过验证的候选启用。

---

## 10. 风险与取舍

| 风险 | 影响 | 对策 |
|---|---|---|
| 宽带优化收敛慢 | UI 卡顿/超时 | 多起点 + 预算 + 后台取消 |
| 标准值组合爆炸 | 无法穷举 | beam search/分支限界，明确 bounded |
| 单节 Pi/T 自由度不足 | 宽带目标不可达 | 多节、可行性预检 |
| VNA 数据质量差 | 优化过拟合 | 范围校验、插值、禁止外推 |
| 元件模型不足 | 假 Supported | 额定值/应力门槛，Warning |
| UI 复杂 | 用户不会用 | 单频/宽带分叉，渐进披露 |
| 优化器与验证不一致 | 假通过 | 最终只用 CircuitGraph + 密集网格 |

---

## 11. 推荐的第一版范围

第一版不要一次做完整“任意宽带”：

1. 保留单频模式；
2. 新增连续单频段（一个 segment）；
3. 复用单节 Pi/T；
4. 只做连续值 + E 系列；
5. 优化 3–6 个变量；
6. 不做多节、不做多频段、不做有源；
7. 做完整的频段评估、验证和报告。

这样可以先验证：

- 频段目标模型；
- 优化器；
- 标准值搜索；
- UI 工作流；
- 导出链路。

再扩展多节和多频段，风险最低。

---

## 12. 结论

- 当前是**单频综合 + 事后扫描**，不是宽带设计。
- Pi/T 有宽带潜力，但单节三个元件只能覆盖有限带宽。
- 宽带匹配的核心不是“多画几条曲线”，而是：
  - 频段目标模型；
  - 多频点误差函数；
  - 多节/多起点综合；
  - 标准值离散优化；
  - 独立全频段验证。
- 建议按 Phase 0 → 1 → 2 → 3 → 4 分阶段落地，先做单频段、单节 Pi/T 的多频优化，再扩多节和完整 VNA。
