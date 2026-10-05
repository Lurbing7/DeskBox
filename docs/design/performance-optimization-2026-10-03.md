# DeskBox 性能调研与优化方案

日期：2026-10-03。状态：调研完成，方案待确认，未修改应用代码、未启动实施。

## 结论

优先处理 Dock 的全量刷新、浮出窗口保留策略，以及性能监控中的重复设备发现和 UI 分配。现阶段没有证据支持整体重写、改框架或为了省一个进程把硬件采集直接并入 WinUI 主进程。

当前较多基础优化已经存在，新增 Dock 和监控应接入这些机制，而不是再建设一套缓存、线程池或清理策略。必须保留 Windows 原生材质、通知提示、实时指标正确性、拖放与目录展开体验。

## 调研边界和证据

本轮检查后台采集、图标缓存、Dock 目录刷新、文件卡片虚拟化、内存维护、设置保存及诊断工具；读取当前运行进程和日志，核对微软官方文档。未执行完整构建、测试套件、长期负载测试、堆转储或 ETW 跟踪；未重启当前应用、修改配置或安装工具。

以下“已确认”指源码行为，不等于已证实它占用最多 CPU。收益等级表示优化方向和发生条件，不是实测提升承诺。

### 本机短采样

当前为 Debug 开发实例，样本约 6 秒，当前桌面使用状态未受控，不作为正式性能基线。

| 进程 | CPU：单核心为 100% | 工作集 | 私有内存 | 句柄 | 线程 |
| --- | ---: | ---: | ---: | ---: | ---: |
| DeskBox 主程序 | 2.84% | 138.6 MiB | 247.5 MiB | 2050 | 113 |
| 直接子进程 dotnet，CPU 采集宿主 | 0.26% | 36.6 MiB | 14.1 MiB | 229 | 11 |

本机 32 个逻辑处理器，两者 CPU 合计 3.10% 单核心，按全机容量折算约 0.097%。CPU 未呈现明显异常。线程和句柄数量只有单点，不能据此认定泄漏或线程池异常。

工作集是驻留物理页；私有内存是不同口径，不能等同于当前实际占用 RAM。多个进程工作集相加也不能当作不重复的物理内存总量。工作集裁剪可能降低数字，但再次访问移出的页会产生缺页，需同时测恢复响应。[微软工作集说明](https://learn.microsoft.com/en-us/windows/win32/memory/working-set)

### 已有优化，应该保留

- [IconHelper](../../src/DeskBox/Helpers/IconHelper.cs)：图标字节、解码位图和缩略图分层缓存，条目数与估算字节上限；Shell 图标和缩略图并发受限。缓存上限是预算，不是当前真实占用。
- [BoundedBackgroundWorkScheduler](../../src/DeskBox/Helpers/BoundedBackgroundWorkScheduler.cs)：Shell 原生调用超时后仍占用槽位直到真实结束，防止不可取消调用无限堆积。不能用无限 Task.Run 替代。
- [FileSurfaceContent.xaml](../../src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml)：GridView 使用 ItemsWrapGrid；RenderedItems 与渲染窗口限制首次展示的数据量，已有虚拟化，不建议重写为普通 StackPanel。[微软 ListView/GridView 优化指南](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview)
- [FolderWatcherService](../../src/DeskBox/Services/FolderWatcherService.cs)、[WidgetViewModel.SortingAndWatchers](../../src/DeskBox/ViewModels/WidgetViewModel.SortingAndWatchers.cs)：变更批处理、防抖、代次校验、重新连接；文件卡片并非所有事件都全量刷新。
- [SystemMonitorWidgetContent](../../src/DeskBox/Controls/WidgetContents/SystemMonitorWidgetContent.cs)：窗口隐藏或收起时停止采集；采样循环串行，重启等待旧循环退出。
- [PerformanceSettingsPolicy](../../src/DeskBox/Services/PerformanceSettingsPolicy.cs)：已有节能预设、缓存预算、隐藏清理和临时窗口释放设置。
- [SettingsService](../../src/DeskBox/Services/SettingsService.cs)：保存已有防抖和代次管理，不能直接归类为“每次点击都同步写盘”。
- [PerformanceLogger](../../src/DeskBox/Services/PerformanceLogger.cs)：已有可选耗时和缓存诊断；内存诊断默认并不持续写高频日志。

## 优化清单

| 顺序 | 优化项 | 源码依据及结论 | 主要收益 | 风险 |
| --- | --- | --- | --- | --- |
| 0 | 建立可重复基线 | 当前只有 Debug 短采样，缺少调用栈和场景归因 | 避免优化无关路径，能够量化结果 | 低 |
| 1 | Dock 增量刷新与单次刷新合并 | DockWidgetContent.RefreshAsync 每次扫描主目录和全部一级目录，清空主栏并重建按钮；代次仅阻止旧结果应用，不阻止旧扫描继续执行 | 文件变化和批量拖入时减少 I/O、控件重建、闪动 | 中 |
| 2 | Dock 临时面板按预算释放 | HidePopover 只将窗口移到屏幕外；Dock 目录和设置宿主目前仅在 Dispose 时 Destroy，最后的内容树仍保留 | 打开目录/设置后的常驻内存、句柄和原生资源 | 中，需避免重现窗口闪烁和泄漏 |
| 3 | 监控设备发现与实时采样分离 | 每轮枚举网卡并读取 IP/网关；NVML 每轮重新映射 DXGI 设备地址、枚举 GPU、查询导出函数 | 减少后台重复原生查询和对象分配 | 中，必须正确处理设备变化 |
| 4 | 监控 UI 更新合并和对象复用 | 每个快照排队 Render；图标 Update 新建画刷、AccessibilitySettings，负载图标重建几何 | 降低 UI 分配、队列积压和重复布局 | 低至中，当前频率不高，不夸大收益 |
| 5 | Dock 只实例化可见项与复用布局定时器 | 主栏先为所有项创建按钮/图标，再隐藏溢出；目录 StackPanel 创建全部子项；布局请求完成后下次新建 DispatcherTimer | 大型 Dock/目录的打开峰值和渲染树大小 | 中；当前少量项收益有限 |
| 6 | 核对图标请求同键并发去重 | s_bitmapImageCache.GetOrAdd 的工厂直接启动异步加载；并发工厂允许执行多次 | 并发同图标请求时减少重复解码和 Shell 请求 | 中，是否实际发生需加计数验证 |
| 7 | 校准回收、工作集裁剪和轮询 | 已有回收冷却/交互保护；深清理强制两次全代 GC；静默裁剪以 2 秒 UI 定时器检查，即使禁用也会唤醒并早退 | 更低后台唤醒、减少恢复时缺页和偶发停顿 | 中至高，不能直接删掉既有回收边界 |
| 8 | PDH 缓冲区复用和热点字符串处理 | ReadArray 每次探测长度、AllocHGlobal、生成列表/名称，使用后释放 | GPU 实例较多时减少原生分配和托管分配 | 中，必须经剖析证明值得做 |
| 9 | 文件卡片布局回调收敛 | RenderWindow 同时订阅 LayoutUpdated、SizeChanged、滚动与数据变更；已有早退和渲染窗口 | 大目录、调整大小时减少重复检查 | 中，需避免内容无法继续展开的旧问题 |

### 1. Dock：增量刷新

涉及 [DockWidgetContent.cs](../../src/DeskBox/Controls/WidgetContents/DockWidgetContent.cs)、[DockDirectory.cs](../../src/DeskBox/Services/DockDirectory.cs)。

保留文件变化路径和变更种类，区分主栏变化、某个目录变化、图标内容变化。主栏路径和名称未变时复用按钮，不清空整个 _row。某个一级目录内容改变，只更新该目录缓存、徽标和已打开的目录面板。

采用“最多一个扫描正在执行，加一个待处理变更集合”。新事件到来合并变更；扫描结束后再处理新集合，而不是只丢弃已经做完的旧结果。本地可取消工作使用 token，原生不可取消工作仍遵守现有并发限制。

当前 IncludeSubdirectories=true 会把更深处文件变化也纳入主栏重建。按实际展示深度过滤；外部目录快捷方式的目标监听另行设计，不能为省资源丢失目录内容更新。应用通知依赖目录子项索引，因此“完全不扫描未打开目录”不直接采用，应按变更维护索引。

验收：单个目录变更不重建未变化主栏按钮；批量变更无重叠扫描，最后状态与磁盘一致；目录保持打开、注意力徽标和拖放结果正确。

### 2. Dock：临时窗口生命周期

涉及 [DockWidgetContent.Placement.cs](../../src/DeskBox/Controls/WidgetContents/DockWidgetContent.Placement.cs)、[StackPopoverHostWindow.cs](../../src/DeskBox/Views/StackPopoverHostWindow.cs)。

文件卡片已有临时宿主释放路径，Dock 应优先接入 PerformanceSettingsPolicy.TransientWindowReleaseDelaySeconds。同一时刻只有一个目录面板，近期反复点击复用窗口；长时间关闭后再拆除内容和释放宿主。隐藏主卡片或关闭应用时同步处理附属窗口。

保留短期停车策略，因为现有代码记录了 Show/Hide、弹出窗口岛和旧画面呈现相关问题。不能仅看到“窗口仍存在”就将所有面板改为每次 Close/Create；也不能未经测量断言停车窗口持续占用大量 GPU。

验收：100 次打开/关闭后，按配置等待释放，句柄与私有内存回到合理稳定区间；再次打开没有旧内容、明显卡顿、主栏覆盖或弹层异常；退出无残留窗口。

### 3. 监控：设备发现缓存

涉及 [SystemMonitorSampler](../../src/DeskBox/Services/SystemMonitorSampler.cs)、[SystemMonitorNvml](../../src/DeskBox/Platform/SystemMonitorNvml.cs)。

网卡列表、当前选择、IP 和网关归入设备发现缓存，网络变化事件使缓存失效，同时低频定时兜底。收发字节计数仍按采样周期读取，切换网卡/休眠恢复后重新预热，避免流量尖峰。GPU 列表已有每 30 次采样刷新，应改为经过时间的周期，避免设置采样间隔改变设备发现频率。

NVML 的 LUID→PCI→设备句柄映射和可用导出函数可在初始化、选择变化或查询失效时重建，稳定阶段仅查询温度、频率、功耗。不得按设备名称猜测匹配；保留相同型号多 GPU 的歧义保护。

CPU 采集宿主当前固定 2 秒更新，主卡片隐藏会结束循环并释放宿主，重新显示会再启动。先测快速显示/隐藏的启动成本；如果值得优化，再设计有期限的暂停/恢复协议。不能仅把隐藏后退出改为永久运行，否则可能省启动成本却增加后台开销。暂不将其合并进主进程：隔离 LHM 依赖与 AOT/WinUI 的既有理由仍成立。

验收：稳定网络下不每轮枚举设备；断连、VPN、切换网卡、休眠和 GPU 变化能更新；设备异常后不显示另一设备的值；数据更新时间与缺失值语义保持正确。

### 4. 监控 UI：更新最新快照，复用图形

涉及 [SystemMonitorWidgetContent](../../src/DeskBox/Controls/WidgetContents/SystemMonitorWidgetContent.cs)、[SystemMonitorMetricIcon](../../src/DeskBox/Controls/WidgetContents/SystemMonitorMetricIcon.cs)。

Dispatcher 只保留一个待处理渲染任务，任务执行时读取最新快照。按显示文本、可见状态、警示状态和可观察图形变化判断是否写 UI；避免反复设置相同 tooltip、设备名与值。

复用画刷、圆弧、扇形几何与无障碍设置对象；高对比度/主题变化通过事件更新缓存。保留完整采样精度，只有绘制允许按显示分辨率跳过重复工作，不能把数据本身四舍五入或漏掉阈值跨越。

当前更新约为秒级，这一项主要降低分配和交互期间的队列压力，暂无证据说明它是最大的 CPU 瓶颈。

验收：UI 阻塞后不会逐个追赶旧快照；每轮未变化数值不新增几何；0%、100%、警示阈值、失效数据、高对比度仍准确。

### 5–9. 其余优化的实施限制

- 主栏不出现滚动条这一要求保留；超过屏幕上限的项仅保留轻量数据，经“更多”访问时再创建必要 UI。目录需要横向滚动，可评估原生虚拟化控件，注意键盘与拖放落点。
- 图标并发去重只在复现同键多次启动后调整为惰性任务或锁内注册；不能破坏失败重试、UI Dispatcher、缓存淘汰和超时占槽。[微软 GetOrAdd 文档说明工厂可能并发执行多次](https://learn.microsoft.com/en-us/dotnet/api/system.collections.concurrent.concurrentdictionary-2.getoradd?view=net-10.0)。这是 API 允许的行为，不代表本项目每次都会重复加载。
- 诊断关闭时 IconHelper.GetIconAsync 仍先构造 path 详情字符串，可顺带在该热点的优化任务中跳过；低收益项，不单独大改日志系统。
- 完整 GC 已有 120 秒冷却，并从后台运行；后台调用不意味着阻塞式全代 GC 完全不影响应用线程。先记录释放字节、GC 暂停和恢复耗时，再决定阈值。微软建议优先让 GC 自行选择回收时机，人工回收应有针对性。[人工回收指南](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/induced)
- 定时器统一调度只做有证据的合并；禁用功能时停止对应轮询优于不断早退，不建议为几个定时器搭建新架构。
- PDH 缓冲只在容量不足时扩容，并保持长度、计数和返回状态检查；Dispose 释放。动态 GPU Engine 实例仍需正确枚举，不能为了缩小缓冲直接丢实例。
- 文件卡片 LayoutUpdated 已有 CanGrowRenderWindow 早退，现有常规小目录不一定受影响。先统计调用次数与耗时；优化时保留首屏填充、批量导入和尺寸变化路径。

## 分阶段开发规划

| 阶段 | 范围 | 输出与验收 | 回退 |
| --- | --- | --- | --- |
| A：建立基线 | 主程序与全部相关采集/代理进程；先测当前 Debug，再经授权测试同架构 Release | CPU、私有内存、工作集、分配率、GC 暂停、句柄、采样耗时、目录打开延迟；保留场景与设置 | 不改变用户配置，诊断日志和跟踪仅保存本地 |
| B：Dock 优化 | 清单 1、2，布局定时器复用 | 单次刷新、未变控件复用、长期关闭释放；拖放、通知和展开无回归 | 每项独立提交，不改用户目录结构 |
| C：监控优化 | 清单 3、4 | 稳定阶段减少发现调用；最新快照合并；更少 UI 分配且指标不丢失 | 保留原发现和采样路径可恢复 |
| D：缓存与容量 | 清单 5、6；只做已复现热点 | 大量项目/同键并发测试、内存曲线稳定 | 单独回退去重/虚拟化改动 |
| E：深层调优 | 清单 7–9，经测量决定 | 清理恢复响应、原生分配和大目录布局的 A/B 对比 | 保留现有性能预设与回收保护 |

建议先批准 A、B、C，D、E 根据结果再决定。每个开发阶段建立文档先行流程并引用本报告；本报告不代表公开发布授权。

## 验证与验收方案

正式基线建议固定卡片数、项目数、DPI、透明度、采样间隔及供电模式；启动后预热，正常可见和隐藏状态分别采样至少 2 分钟、重复 3 次，比较中位数和尾部延迟。批量变化和窗口循环作为独立场景，不混入闲置 CPU。

| 场景 | 检查项 |
| --- | --- |
| 原有应用栏、监控、Dock 都可见 | 主程序和采集宿主 CPU、私有内存、工作集、UI 更新/采样次数 |
| 全部隐藏/监控收起 | 实际无采集，停止/暂停协议正确，后台唤醒降低 |
| 100 次目录展开、关闭与设置开关 | 无句柄/内存持续单调增长，按策略释放，重新打开延迟不恶化 |
| 5、50、200 个主栏/目录项 | 首屏响应、图标任务数、更多菜单和横向滚动 |
| 批量拖入/新增、重命名、删除 | 扫描并发上限 1，合并后最终状态准确，未变按钮复用 |
| 网卡切换、VPN、睡眠恢复 | IP/设备列表正确、网络速率重新预热、过期传感器读数失效 |
| 1000 项文件目录及持续滚动 | 虚拟化有效，主线程布局与图标峰值稳定，仍可访问全部内容 |
| 深浅主题、高对比度、文字缩放 | 不以性能优化破坏原生视觉与可访问性 |

验收以相同场景 A/B 对比为准；不能因为单点 working set 下降就判定通过。第一阶段先建立数值基线，再确定 CPU、分配率、延迟和稳定内存的目标，不承诺未经测量的百分比或绝对内存值。

本机可发现 wpr.exe，未在 PATH 发现 dotnet-counters/dotnet-trace；本轮未安装，也未发起系统跟踪。实施阶段可先复用 DESKBOX_PERF_LOG=1 的现有诊断；对 .NET 主程序和 SensorHost 分别采样。托管工具分析 GC 和调用栈，Windows 原生线程/合成器分析按需使用 WPR/WPA，不能把托管堆直接当成全部原生 UI 内存。[dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)、[dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace)

已有工具时可使用：

```powershell
# PID 使用本次实际运行实例；两种采集不要默认同时运行。
dotnet-counters collect --process-id <PID> --counters System.Runtime --refresh-interval 1 --duration 00:02:00 --format json --output .artifacts/perf-baseline.json
dotnet-trace collect --process-id <PID> --duration 00:00:30 --output .artifacts/perf-baseline.nettrace
```

命令仅为实施建议，本轮未执行。工具版本不同先核对 --help；跟踪可能包含本机路径，不提交原始跟踪或日志到远程仓库。

## 当前交付状态

源码调研、短采样和优化方案已完成；正式基线、优化实现与功能回归尚未开始。本轮只新增此报告，应用和配置保持当前状态。
