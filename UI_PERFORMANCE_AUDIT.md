# Circuit Module Designer WinForms UI 性能审查

日期：2026-09-08  
范围：主窗体、Filter Design UI、Precision Analog Computing UI、Schematic/Analysis 自绘控件。  
边界：未修改 Filter/Precision Analog 的公式、拓扑、CircuitGraph 或验证数学。

## 1～50 检查结果

| # | 结果 | 检查结论、修改与原因 | 涉及文件 |
|---:|---|---|---|
| 1 | **Found / Fixed** | Precision Analog 的 `ShowResult` 每次 Design 都清空全部结果页。改为固定五个 TabPage，只更新现有内容。清空控制树会产生整窗擦除和重新布局。 | `PrecisionAnalogWizardControl.cs` |
| 2 | **Found / Fixed** | 每次 Design 都创建新的 schematic、analysis、calculations、SPICE、report 控件。静态工具栏、文本框和图纸控件现只初始化一次；operation-specific analysis 控件才按结果替换。大量 GDI/控件创建会卡顿并增加 GC。 | `PrecisionAnalogWizardControl.cs`, `FilterDesignOutputControl.cs` |
| 3 | **Found / Fixed** | Precision Analog 和 Filter 输出均会重建 TabPage。两者均改为复用固定的 Schematic / Analysis / Calculations / SPICE / Report 页。 | `PrecisionAnalogWizardControl.cs`, `FilterDesignOutputControl.cs` |
| 4 | **Found / Fixed** | Filter 向导每次 Next/Back 都 `pageHost.Controls.Clear()` 并新建步骤控件；现缓存各 step，只切换可见性。Precision 结果根控件也不再销毁。 | `FilterWizardControl.cs`, `PrecisionAnalogWizardControl.cs` |
| 5 | **Found / Fixed** | 普通页面刷新曾依赖 remove/add；现仅真正切换顶层模块时修改主 control tree，结果刷新只更新 model/text。Analysis 因操作类型不同仍替换其单一内容控件，这是局部且必要的。 | `MainForm.cs`, 两个 Wizard/Output control |
| 6 | **Found / Fixed** | Filter 每次重新 Design 会 Dispose + new 整个输出 UserControl；现调用 `UpdateResult` 更新同一输出控件。真正切换模块时仍允许释放模块 UI。 | `FilterWizardControl.cs`, `FilterDesignOutputControl.cs` |
| 7 | **Found / Fixed** | Precision operation 参数原来把 Label/Editor 分开逐个显隐，造成 Positive/Negative、Gain K 标签与控件脱离。现每个字段封成一个稳定 pair container，按组显隐并在布局冻结中完成。 | `PrecisionAnalogWizardControl.cs` |
| 8 | **Not Found** | Design 事件只在构造时注册一次；固定页的导出/选择事件也只注册一次。没有在 RenderResult 中重复 `+=`。 | 两个结果控件 |
| 9 | **Found / Fixed** | 批量页面/结果更新缺少完整布局冻结。现对 results、pageHost、analysisHost、模块切换区域使用 `SuspendLayout` / `ResumeLayout(false)`，末尾一次 `PerformLayout`。 | `MainForm.cs`, `FilterWizardControl.cs`, `FilterDesignOutputControl.cs`, `PrecisionAnalogWizardControl.cs` |
| 10 | **Found / Fixed** | 过去只冻结较外层或完全不冻结。现同时冻结实际发生子控件变化的 TabPage/host。 | 两个结果控件 |
| 11 | **Found / Fixed** | Filter `ShowStep` 使用 `ResumeLayout(true)`，会立即递归布局；已改为 false，最后统一布局。 | `FilterWizardControl.cs` |
| 12 | **Not Found** | 没有在绘制/采样循环中逐次设置 Location/Size/Margin。少量 splitter 调整每次 resize 只执行一次。 | 全局审查 |
| 13 | **Not Found** | 主体页面使用 Dock/TableLayout/FlowLayout；绘图中的坐标是 canvas geometry，不是 WinForms 子控件手工排版。 | 主窗体、Wizard、自绘控件 |
| 14 | **Not Found** | 未发现同时 Left+Right Anchor 且运行时持续改 Width 的冲突模式。 | 全局审查 |
| 15 | **Not Found** | 未发现 Dock 控件同时在普通刷新中反复设置 Location/Size。 | 全局审查 |
| 16 | **Found / Fixed** | 旧结果页反复加入 Fill 控件，短暂存在 Dock z-order/覆盖风险。固定 host 现在任一时刻只保留一个 Fill analysis 控件。 | 两个结果控件 |
| 17 | **Found / Fixed** | 动态重建造成 Dock 顺序依赖 Add 时序；固定结果页在初始化时一次性按确定顺序建立。 | 两个结果控件 |
| 18 | **Not Found** | 稳定大区域使用 Percent/Absolute 组合；表单型区域的 AutoSize 行数量有限且必要，未发现大规模动态 AutoSize 表格。 | 全局审查 |
| 19 | **Not Found** | 未发现无界深层 TableLayout 嵌套。结果页通常为 root + toolbar + content 两层。 | 全局审查 |
| 20 | **Found / Fixed** | Precision operation 字段动态变化位于 AutoSize FlowLayout，曾引起顶区高度跳动。首次修正遗漏了对内层 FlowLayout 的最终 `PerformLayout`，导致 Full-Wave/Log/Antilog 保留隐藏字段占位时的旧坐标；现已显式重排内层字段、扩大稳定高度并允许局部滚动。 | `PrecisionAnalogWizardControl.cs` |
| 21 | **Found / Fixed** | 动态 operation 根容器和 GroupBox 原来 AutoSize，会随字段数量改变整个页面高度；已关闭根 operation 区 AutoSize。 | `PrecisionAnalogWizardControl.cs` |
| 22 | **Found / Fixed** | operation 字段使用 GrowAndShrink 会让顶区尺寸跳动；GrowAndShrink 仅保留在小型 label/editor pair，不再作用于大页面根容器。 | `PrecisionAnalogWizardControl.cs` |
| 23 | **Found / Fixed** | Design status 是 AutoSize Label，长错误信息会推开按钮/页面。现放入两列表格的 Fill 单元，固定高度、AutoEllipsis。 | `PrecisionAnalogWizardControl.cs` |
| 24 | **Found / Fixed** | capability/status 长文本过去能改变参数区宽高。现状态区稳定；详细失败内容进入固定 results 内容区。 | `PrecisionAnalogWizardControl.cs` |
| 25 | **Found / Fixed** | 动态字段短暂越界会触发父级尺寸重算；现滚动只局限于固定 operationFields，不再改变整个结果区位置。 | `PrecisionAnalogWizardControl.cs` |
| 26 | **Not Found** | 主 Form 明确使用 `AutoScaleMode.Dpi`，子 UserControl 继承统一缩放策略，没有混用 Font/Dpi/None。 | `MainForm.cs`及全部 UserControl |
| 27 | **Not Found** | 项目 UI 为代码构建，没有 Designer AutoScaleDimensions 与运行时策略冲突。 | 全局审查 |
| 28 | **Not Found** | 未发现运行时将 WinForms 控件坐标再次乘 DPI。`DeviceDpi/96` 只用于绘图笔宽/画布，不用于子控件布局。 | `FilterSpecPlotControl.cs` |
| 29 | **Not Found** | 未使用 `Screen.PrimaryScreen` 或固定 96 DPI 计算页面控件尺寸。 | 全局审查 |
| 30 | **Not Found** | Font 仅在初始化或 `using` 的绘制作用域中创建，不在普通交互中反复修改控件 Font。 | 主窗体、绘图控件 |
| 31 | **Not Found** | 运行期间没有修改父容器 Font，因而不会触发整棵 UI 重新测量。 | 全局审查 |
| 32 | **Found / Fixed** | operation Label/Combo/NumericUpDown 在缩放后可能分离或被规格框裁切。字段 pair 化，并增加 100%/125%/150% 自动布局测试；逐个切换全部7个 operation，验证首字段回到左侧、字段不越过底边、组间及组内不覆盖。 | `PrecisionAnalogWizardControl.cs`, CoreTests `Program.cs` |
| 33 | **Not Found** | MinimumSize 仅用于主窗体和按钮，未发现与 DPI 后实际布局相矛盾的 MaximumSize。 | `MainForm.cs`及按钮定义 |
| 34 | **Not Found** | 主 Form Resize 不会手工移动几十个控件；只在首次显示设置主 splitter。三个内部 split 仅各自设置一次 SplitterDistance。 | `MainForm.cs`, Filter summary/spec/implementation controls |
| 35 | **Found / Fixed** | Schematic/Analysis 已有 double buffering；此次补齐 MainForm、FilterWizard、Filter output、Precision Wizard 的局部双缓冲。 | `MainForm.cs`, 两个 Wizard/Output、自绘控件 |
| 36 | **Found / Fixed** | 主要自绘控件已使用 UserPaint/AllPaintingInWmPaint/OptimizedDoubleBuffer；容器补充合理的 AllPaintingInWmPaint/OptimizedDoubleBuffer。 | Schematic/Analysis controls、主容器 |
| 37 | **Found / Fixed** | 审查中移除了曾尝试加在整个 Precision Wizard 上的 `WS_EX_COMPOSITED`；最终未给应用加该扩展样式。它会让大控件树和 WebBrowser 变慢并产生拖影。 | `PrecisionAnalogWizardControl.cs` |
| 38 | **Not Found** | 没有在 Design 路径调用 `Form.Refresh()`。 | 全局审查 |
| 39 | **Not Found** | 没有在 Design/循环中调用 `Update()`。 | 全局审查 |
| 40 | **Not Found** | analysis 数据先整体生成，自绘控件只在 model 替换后 Invalidate；没有采样循环逐点 Invalidate。 | Analysis controls/services |
| 41 | **Not Found** | 未发现 `Invalidate(true)` 递归重绘全部子控件。 | 全局审查 |
| 42 | **Not Found** | OnPaint 只绘图，不修改 Size/Location/Text/Visible，不 Add 控件。 | 全部自绘控件 |
| 43 | **Not Found** | OnPaint 创建的 Pen/Brush/Font 均由 `using` 释放；未发现未释放 Bitmap/GraphicsPath。现阶段对象数量小，强行缓存会增加 DPI/Font 生命周期风险。 | Schematic/Analysis controls |
| 44 | **Not Found** | 未发现每次 repaint 重缩放 Logo/背景图或生成 schematic bitmap；schematic 直接绘制已布局 document。 | 主窗体、Schematic control |
| 45 | **Found / Fixed** | 两个 Design 都已把纯 engine 计算放在 `Task.Run`，UI 线程只 BuildRequest/Render；增加 `_designing` 防重复进入。没有后台访问 WinForms 控件。 | `FilterWizardControl.cs`, `PrecisionAnalogWizardControl.cs` |
| 46 | **Not Found** | 项目没有 `Application.DoEvents()`。 | 全局审查 |
| 47 | **Found / Fixed** | synthesis/E-series/MNA/analysis sampling 已在后台 core design 执行；报告 HTML 不再每次 Design 在 UI 线程生成，而是首次打开 Report 页才生成。 | 两个 Wizard/Output control |
| 48 | **Not Found** | Analysis 使用预先生成的完整点数组并在 OnPaint 一次绘制，不使用 Chart.Series.Points.Add 的逐点 UI 更新。 | Analysis controls |
| 49 | **Found / Fixed** | Precision 输入表更新使用已有 grid；新增结果渲染不逐行建立表格。TreeView 已使用 BeginUpdate/EndUpdate，DataGridView 未使用昂贵的 AllCells 自动列宽。 | `PrecisionAnalogWizardControl.cs`, `FilterWizardControl.cs` |
| 50 | **Found / Fixed** | Precision operation 状态恢复会连续设置多个 NumericUpDown，原事件会连锁刷新公式/布局；新增 `updatingUi` 门禁并统一完成布局。Filter navigation 原有 `updatingNavigation` 保留。 | `PrecisionAnalogWizardControl.cs`, `FilterWizardControl.cs` |

## Design 调用链与诊断

两条调用链现在都是：

`Button_Click → UI thread BuildRequest → Task.Run(CoreDesign + independent verification + analysis data) → UI thread UpdateResult → update existing schematic/analysis/calculations/netlist controls → lazy report`

Debug 构建会输出：

```text
[UI PERF] Precision Analog Computing / Filter Design
BuildRequest             ... ms
CoreDesign               ... ms
Verification             included in CoreDesign
RenderSummary            included in RenderResult
RenderSchematic          ... ms
RenderAnalysis           ... ms
RenderCalculations       ... ms
RenderNetlist            ... ms
RenderReport             lazy; measured when selected
Total                    ... ms
```

正式 UI 不显示这些诊断。验证前的代码没有 Stopwatch，因此不存在可信的“修改前毫秒数”；本报告不伪造数据。结构基线是每次 Design 重建 4～5 个 TabPage、WebBrowser 和全部结果控件；修改后每次 Design 重建 **0 个 TabPage、0 个结果根 UserControl、0 个 WebBrowser**，只有 operation-specific Analysis 内容在类型变化时替换。修改后的实际毫秒数可直接从 Visual Studio Debug Output 的 `[UI PERF]` 读取。

## 根因 Top 5

1. Precision `ShowResult` 每次清空并重建全部 TabPage 和 WebBrowser。
2. Precision operation 的 Label 与输入控件分开显隐，AutoSize 重排时发生脱离、覆盖和控件“消失”。
3. Filter Next/Back 每次销毁并重建步骤页，Design 后又重建完整输出控件。
4. 报告 HTML/WebBrowser 在每次 Design 中同步创建，即使用户没有打开 Report。
5. 长状态 Label 与动态 operation GroupBox 使用根级 AutoSize，使页面尺寸随文本和字段数量跳动。

## 修改文件

- `Controls/UiUpdateUtilities.cs`
- `Forms/MainForm.cs`
- `Modules/Filters/FilterDesign/Controls/FilterWizardControl.cs`
- `Modules/Filters/FilterDesign/Controls/FilterDesignOutputControl.cs`
- `Modules/PrecisionAnalogComputing/Controls/PrecisionAnalogWizardControl.cs`
- `Tests/CircuitModuleDesigner.CoreTests/Program.cs`

## 验收结果

- 同一 Precision result 连续 Render 20 次：TabPage 实例和数量保持不变。
- Half-Wave / Full-Wave 的 Positive/Negative 选择控件在来回切换后仍存在。
- 100% / 125% / 150% 缩放：整流的 Polarity、Gain K、Vin Min、Vin Max 四组字段存在、非零尺寸、彼此不覆盖，组内 Label 与 editor 不覆盖。
- 全部 126 项 calculation/synthesis/UI regression 通过。
- 构建 0 errors、0 warnings。
- 未改变 schematic document、analysis 数学数据或电路绘制结果。

## 已知剩余问题

没有已知的确定性控件漂移或结果页重建问题。真正切换顶层模块时仍会 detach/add 模块 UserControl，这是模块切换所必需；operation-specific Analysis 在结果类型改变时也会替换单个绘图控件。WebBrowser 第一次打开 Report 页仍有其原生初始化成本，但已从 Design 主路径移除，不再造成点击 Design 时整窗闪烁。
