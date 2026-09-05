# Filter Design v1 五期人工测试说明

## 有源 + 无源综合详测（本轮推荐）

完整的 48 点综合测试表见：

`Filter_Active_Passive_48点详尽人工测试.csv`

其中 1～24 为 Active，25～48 为 Passive。测试范围包括：

- Sallen-Key、MFB、Bainter，以及明确不支持组合的 capability 状态。
- Butterworth、Chebyshev-I、Bessel、Gaussian、Linear Phase。
- LP、HP、BP、BS，自动/指定阶数，A0=0.5/1/2，E24/E96。
- Passive RC 的一阶 LP/HP、Two-Corner BP、Twin-T，以及理想源/有限 Rs、RL。
- Passive LC 的 50/75/600/1kΩ、Matched/Prototype-Native/Custom、Series/Shunt/Auto。
- Chebyshev 偶数阶终接、Bessel LP/HP/BP/BS、Gaussian/Linear Phase 正实可实现性和窄带元件范围。

表格左侧是已完整填写的输入；不适用项统一为“无”，右侧结果列不预填答案。每次从 Excel 复制一整行，在 Filter Wizard 中点击 `Test Row...`，运行后软件会返回相同的 57 列，可整行粘贴回去。返回内容包括实际设计状态、`CapabilityLevel`、`CapabilityReasonCode`、实际阶数、边缘幅度、四种 Passive response、Analysis/Report/CIR 检查及警告数量。

该 CSV 使用 UTF-8 BOM 和 Windows CRLF，可直接由 Excel 打开；不要另存为 ANSI，以免中文乱码。

## Passive Filter 集中复测

Passive Filter 阻抗与综合重构后的 20 点集中测试见：

`Passive_Filter_20点集中人工测试.csv`

该表独立于下面原有五期测试，不要求重新完成原来的 80 点。只填写表格右侧留空的实测字段；输入中不适用的项目已经填写“无”。幅度一律填写软件 Analysis 光标显示的 dB 数值，状态字段填写界面显示的原始枚举值，不需要自行判断正确答案。

快捷测试方法：

1. 在 Excel 中选中一个测试点的整行并复制。
2. 在 Filter Wizard 底部点击 `Test Row...`。
3. 点击 `Paste Excel Row`，再点击 `Run Row`。
4. 软件会运行正式综合和 CircuitGraph/MNA 验证，并输出同样 51 列的结果行。
5. 点击 `Copy Result Row`，整行粘贴回 Excel 即可。

该入口接受 Excel 的制表符行，也接受 CSV 的逗号行；一次处理一个测试点，因此无需重复操作各个下拉框。

## 总体安排

| 期次 | 数量 | 重点 |
|---|---:|---|
| 第 1 期 | 20 | 12 个 LP/HP 基础类型，4 个 BP，4 个 BS；覆盖主要响应和有源拓扑 |
| 第 2 期 | 20 | 12 个 LP/HP，4 个 BP，4 个 BS；改变 A0、指定阶数和频率范围 |
| 第 3 期 | 20 | 12 个 LP/HP，4 个 BP，4 个 BS；覆盖自动阶数、拓扑和 Passive LC |
| 第 4 期 | 10 | E96 常规条件下 selected-value 电路的 fp/fs 超限检查 |
| 第 5 期 | 10 | E96 较窄过渡带、较高阶和极端频率压力检查 |

前三期仍各 20 点，总测试数量仍为 80，不因加入带通、带阻而增加工作量。

表中的实测值全部是幅度 dB，不是相位 degree。本轮不要求填写中心频率相位，也不要求填写任何相位数据。

## 固定设置

- 第 1～3 期：开启标准值优化，R/C/L 使用 E24。
- 第 4～5 期：开启标准值优化，R/C/L 使用 E96。
- Active 和 Passive RC：R 范围固定为 1 kΩ～100 kΩ，C 范围固定为 100 pF～1 µF。
- Passive LC：使用 `DoublyTerminated`，Rs=50 Ω、RL=50 Ω，C 范围固定为 1 pF～10 mF，L 范围固定为 100 nH～10 H。
- 容差保持界面默认值。

## 填写方式

低通、高通测试点只填写：

- `设计成功_0或1`
- `实测_fp_dB`
- `实测_fs_dB`
- 有问题时填写 `异常备注`

带通、带阻测试点只填写：

- `设计成功_0或1`
- `实测_fp1_dB`
- `实测_fs1_dB`
- `实测_fs2_dB`
- `实测_fp2_dB`
- 有问题时填写 `异常备注`

不适用于该响应的输入格和实测格已经填为“无”。四个带边缘的频率顺序为：

- BandPass：`fs1 < fp1 < fp2 < fs2`
- BandStop：`fp1 < fs1 < fs2 < fp2`

第 4～5 期另填写：

- `fp超限_0或1`：实测 fp dB 低于 `fp允许下限_dB` 时填 1，否则填 0。
- `fs超限_0或1`：实测 fs dB 高于 `fs允许上限_dB` 时填 1，否则填 0。

如果设计没有生成，只填写 `设计成功_0或1=0` 和异常提示原文，其余实测栏留空，不要猜测数值。
