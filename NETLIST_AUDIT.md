# Circuit Module Designer 网表导出审查

审查日期：2026-09-09  
范围：文本 SPICE 网表（`.cir` / `.net`）、LTspice、Multisim；不包含 LTspice `.asc` 原理图格式。

## 结论

当前成功设计生成的文本网表已经可以作为 `.cir` 或 `.net` 由 LTspice 打开。R/C/L、独立源、有限增益 VCVS 运放抽象、带显式 `.model` 的二极管以及 AC/DC/TRAN/OP 指令均采用 LTspice 可识别的文本语法。它不是 `.asc`，也不会保存图形坐标。

“可以打开并运行”不等于“等效于某个真实运放/二极管”：默认运放策略是有限开环增益 E 源，不模拟电源限幅、GBW、slew rate、输入/输出阻抗；Precision Analog 的二极管使用设计请求中的 nominal Shockley `Is`、`n`、温度生成显式模型，不代表具体厂商器件精度。需要真实运放时必须选择外部 `.SUBCKT`，并保证 X 实例引脚顺序与模型声明完全一致。

本机常见安装路径和命令中未发现 LTspice executable，因此本次完成了生成文本、结构验证和 golden regression，没有伪称做过本机 LTspice 求解器运行。

## 审查前结构与问题

- Filter 使用 `Core/Circuit/Spice/CircuitSpiceExporter`，直接读取 `CircuitGraph`，但同时硬编码输入源、外部负载、AC sweep 和 Multisim 文案。
- Precision Analog 使用独立的 `PrecisionAnalogSpiceExportService` 再实现一次 R、运放、节点清理和数字格式化；它只支持电阻和运放，遇到 C/L/Diode 会失败，并把所有操作强制导出为 DC sweep。
- 两套实现的公共器件序列化、节点名和数字格式存在重复，后续容易发生同一 CircuitGraph 导出不同含义。
- 二极管没有输出模型名和 `.MODEL`；抽象运放虽然输出的是有效 E 源，而不是裸 `U1`，但模型策略没有形成显式选项。
- 原 options 强制要求 AC 起止频率，无法表达“纯网表”、DC、瞬态或工作点分析。

未发现 exporter 重新计算 Filter 或 Precision Analog 的电阻、电容、电感值。模块 service 只负责从 design result 补充激励源、分析范围、外部 termination 和模型 metadata；所有设计器件及其连接仍来自唯一的 `CircuitGraph`。

## 最终结构

```text
FilterDesignResult / AnalogComputeDesignResult
                |
                | CircuitGraph（唯一设计电路数据源）
                | export metadata（源、分析、模型、外部负载）
                v
CircuitGraphSpiceSerializer
                |
                v
SpiceNetlistDocument
        |                       |
        v                       v
LtspiceNetlistExporter   MultisimNetlistExporter
```

- `CircuitGraphSpiceSerializer` 是 R/C/L/Diode/OpAmp 唯一正式映射。
- `SpiceText` 是节点、器件标识符和 invariant scientific number 的唯一格式化实现。
- `CircuitSpiceExporter` 保留为通用/向后兼容 façade；LTspice 与 Multisim adapter 只设置 dialect，不复制器件循环。
- Filter 的 AC profile 仍由 Filter service 选择，但不重算或改变 CircuitGraph。
- Precision Analog service 只添加 VCC/VEE/input sources、合适的 DC 或 AC profile、nominal diode model 和温度；不再序列化器件。

## LTspice 官方格式依据

采用 Analog Devices 的 LTspice 26 Quick Reference 和官方模型导入资料作为依据：

- [LTspice 26 SPICE Quick Reference](https://www.analog.com/media/en/news-marketing-collateral/solutions-bulletins-brochures/ltspice-keyboard-shortcuts-ink-saver.pdf)：列出 R/C/L/D/E/I/V/X 元件行类型、`.ac/.dc/.op/.tran`、`.model/.subckt/.include/.lib/.param/.end`，并明确一次仿真只有一个 active analysis directive。
- [LTspice World Circuit / Help overview](https://ltspice.analog.com/LTspiceSafari.pdf)：文本电路由器件、节点、模型和 SPICE 命令组成，`.net/.cir/.sp` 可作为文本网表打开；`.asc` 是另一种原理图文件。
- [LTspice: Importing Third-Party Models](https://www.analog.com/en/resources/technical-articles/ltspice-how-to-import-third-party-models.html)：本征器件使用 `.MODEL`，组合模型使用 `.SUBCKT`，并通过模型名/库引用关联。
- [Using an Intrinsic Symbol for a Third-Party Model](https://www.analog.com/en/resources/technical-articles/ltspice-using-an-intrinsic-symbol-for-a-third-party-model.html)：subcircuit 的端口顺序必须与调用端保持一致。
- [ADI EngineerZone diode model clarification](https://ez.analog.com/design-tools-and-calculators/ltspice/f/q-a/604438/unable-to-create-a-standalone-diode-model-in-the-ltspice)：D 实例引用模型名，`.model` 是独立 directive，不能把模型定义伪装成器件行的一部分。

关键规则落实如下：

- 地统一为节点 `0`，CircuitGraph 的 `GND` 在导出边界转换。
- R/C/L 为 `name node+ node- value`；D 为 `name anode cathode model-name`；E 为电压控制电压源；V/I 为独立源；X 为子电路调用。
- 第一行是 title；`*` 行是注释；末尾始终输出 `.END`。
- `.AC/.DC/.TRAN/.OP` 是 profile，可不指定；指定时最多一个 active analysis。
- `.INCLUDE`、`.LIB`、`.PARAM`、`.MODEL`、`.SUBCKT/.ENDS` 有独立 representation。
- 文件路径加双引号；节点中的特殊字符使用可逆式十六进制转义，避免简单替换造成常见碰撞。
- 所有数值使用 `InvariantCulture` 科学计数法。没有使用 `M` 后缀：LTspice 中 `M/m` 均是 `10^-3`，`Meg` 才是 `10^6`。
- 独立源允许负值和零值；CircuitGraph 的 R/C/L 继续要求有限正值，导出器不会用负数或零元件掩盖上游错误。

## 兼容矩阵

| 内容 | LTspice | Generic SPICE | Multisim | 处理 |
|---|---|---|---|---|
| R/C/L | 兼容 | 兼容 | 保留兼容 | 公共 serializer |
| Ground | 兼容 | 兼容 | 兼容 | `GND` → `0` |
| 独立 V/I source | 兼容 | 兼容 | 兼容 | export metadata，不伪造 CircuitGraph 元件 |
| E controlled source | 兼容 | 兼容 | 保留兼容 | 默认抽象运放 |
| F/G/H controlled source | 未导出 | 未导出 | 未导出 | CircuitGraph 当前无对应器件类型 |
| Diode | 兼容 | 兼容 | 标准 SPICE 兼容 | 必须提供显式 `.MODEL` |
| Abstract OpAmp | 可运行但仅抽象 | 通用 E 源 | 保留原行为 | 不输出裸 `U1` |
| External OpAmp | 兼容 | 取决于模型 | 取决于模型 | `X...` + `.INCLUDE`/embedded `.SUBCKT`，显式 pin order |
| `.AC/.DC/.TRAN/.OP` | 兼容 | 兼容 | 常用格式兼容 | operation-specific，可为空，最多一个 |
| `.MODEL/.SUBCKT/.INCLUDE/.LIB/.PARAM` | 兼容 | 标准形式 | 依目标模型兼容性 | 公共 representation |
| `.PRINT` | LTspice 文本网表示例可用 | 常见 SPICE | 保留旧行为 | 与 active analysis domain 一致 |
| `.cir/.net` | 直接文本网表 | 是 | 可导入 | Save dialog 支持两者 |
| `.asc` | 不支持 | 不适用 | 不适用 | 明确不在本任务范围 |

Multisim adapter 与 LTspice adapter 共享全部器件序列化。当前没有发现值得复制完整 exporter 的格式差异；软件专用差异只允许留在 adapter/profile 层。某个厂商加密或专有 subcircuit 能否跨模拟器运行，不由通用语法保证。

## OpAmp 与 Diode 策略

### OpAmp

1. `FiniteGainVcvs`（默认）：输出 `E_<reference> OUT 0 IN+ IN- Aol`。这是有效 SPICE/LTspice controlled-source 语法，但明确标注不使用供电端作 swing limit。
2. `ExternalSubcircuit`：输出 `X_<reference> ... modelName`。调用 pin order 必须显式配置，可配 `.INCLUDE` 或 embedded `.SUBCKT`。
3. `Unsupported`：CircuitGraph 含 OpAmp 时立即给出明确异常，不生成看似可用的假行。

### Diode

- CircuitGraph 只保存 A/K 拓扑，不塞入 vendor model。
- 导出含 D 元件时必须提供 `SpiceDiodeModelDefinition`，否则明确失败。
- Precision Analog 从 result 的 `Is`、`n`、temperature 生成 `.MODEL CMD_DIODE D(...)` 和 `.TEMP`。
- 该 nominal Shockley 模型不包含真实器件寄生、反向恢复、批次离散或精密匹配保证。

## 分析指令策略

- 纯 CircuitGraph 可导出为没有 analysis directive 的合法电路描述。
- Filter service 显式请求 AC source + `.AC`，没有把该 profile 强加给其他模块。
- Precision Integrator/Differentiator 输出 AC profile；静态 affine、rectifier、log/antilog、limiter 输出 DC sweep。
- serializer 拒绝同时存在多个 active analyses，防止生成 LTspice 26 Quick Reference 明确不允许的运行配置。
- AC source、DC source、分析范围属于测试台 metadata，不属于设计出的 CircuitGraph 元件。

## 修复与测试

新增/增强的自动化覆盖：

- R/C/L 串并联节点与特殊节点名。
- 独立电压源、独立电流源、正/负 rail source、VIN/VOUT/GND。
- 抽象运放 E-source；external `.SUBCKT` 的 X 调用、供电节点和 pin order。
- 二极管实例、显式 `.MODEL`、temperature。
- AC、DC、TRAN、OP profiles；`.INCLUDE/.LIB/.PARAM`。
- p/n/u/m/k/Meg 数量级对应的科学计数输出，并在 `de-DE` culture 下确认不会出现逗号小数。
- LTspice/generic golden text、Multisim 原有回归、Filter 和 Precision Analog 导出回归。
- Precision nonlinear operation 现在可导出 diode DC netlist；Integrator/Differentiator 可导出 C/R/OpAmp AC netlist，不再显示 placeholder。

人工审查样例位于 `NetlistSamples/LTspice/`。它们均为文本 `.cir`，不是 `.asc`。

最终验证结果：

- `dotnet build CircuitModuleDesigner.csproj --no-restore`：通过，0 errors，0 warnings。
- 测试 executable：131/131 通过（包含全部既有 Filter、Precision Analog 与新增 SPICE regression）。
- `dotnet test ... --no-build`：命令成功；该项目的实际断言由上述自执行 test harness 输出。
- `git diff --check`：通过，仅 Git 提示工作区未来可能进行 LF→CRLF 转换，没有 whitespace error。

## 仍未支持

- LTspice `.asc` 原理图坐标文件。
- CircuitGraph 尚不存在的 F/G/H source、MOSFET、BJT、switch、transmission line 等元件。
- 自动选择具体厂商运放/二极管、自动下载模型或猜测任意 `.SUBCKT` pin order。
- 对厂商 proprietary/encrypted macromodel 的跨 Multisim 兼容承诺。
- 调用本机 LTspice 做求解器级 CI（本机未安装；当前为 serializer/golden 级验证）。

这些限制均不会输出 fake Supported：没有公共映射或必需模型时，serializer 会明确拒绝导出。
