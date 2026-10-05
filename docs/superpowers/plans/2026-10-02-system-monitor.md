# 性能监控卡片开发规划

状态：用户已授权执行；首版实现完成，本地 Debug 验证与展示中。关联：[调研](../../design/system-monitor-research.md)、[验收清单](../../design/system-monitor-acceptance.md)、[执行记录](../../development/2026-10-02-system-monitor.md)。

> 执行方式：使用 superpowers:executing-plans 在当前任务逐项实施。用户与 AGENTS.md 的范围、构建及验证要求优先；不自动提交、不运行完整测试、不创建额外工作树。

**目标：** 交付无需管理员权限、有真实基础读数和明确能力边界的性能监控卡片。

**架构：** 原生采集后端生成不可变快照，独立内容/provider 展示快照，共享宿主处理外壳与生命周期。采集在后台串行运行，UI 只更新现有控件。无新增外部依赖、驱动或监控子进程。

**技术：** .NET 10、WinUI 3、LibraryImport/PInvoke、PDH、DXGI、GlobalMemoryStatusEx、NetworkInterface。正式 interop 遵循项目已有约定，保留 x64/ARM64 和 AOT 约束，未运行的平台不得宣称验证通过。

## 第一版范围

CPU 占用与可用的系统报告频率；GPU 占用与专用显存；物理内存；单网卡上传/下载速率。CPU/GPU 分区、内存/显存用量条、网络底栏，复用原生主题控件。温度、风扇、功耗及不可用频率显示“—”并解释原因；不使用示例值。最终全局 UI 统一仍在监控完成后进行。

默认 1 秒采样，可设 1/2/5 秒；设置提供 GPU 和网卡选择、不可用指标行显示开关。首版为单例功能格子，不添加历史曲线、日志上传、硬件控制或自动提权。

## 任务 1：指标模型与计算

- [ ] 新建 Models/SystemMonitorSnapshot.cs：时间戳、指标值与可用状态、设备稳定标识；空值与有效 0 分开。
- [ ] 新建 Services/SystemMonitorMetrics.cs：计数器差分、CPU 百分比、网速单位、GPU 引擎聚合、显存容量匹配规则。
- [ ] 新建 tests/DeskBox.Tests/SystemMonitorMetricsTests.cs：重置/倒退计数器、冷启动、实际时间间隔、GPU 多进程多引擎、无效样本及容量缺失。
- [ ] 运行预计 30 秒内的聚焦计算验证；检查百分比、单位和缺失数据，不扩大为完整套件。

## 任务 2：原生采集与生命周期

- [ ] 新建 Services/SystemMonitorNative.cs、SystemMonitorPdhQuery.cs、SystemMonitorSampler.cs，各自负责 interop、句柄和串行采样。
- [ ] 原生 API 验证 GPU/频率计数器与 DXGI LUID 匹配；缺失时降级，不能用错误适配器容量代替。
- [ ] 初次采样显示采样中；网络重置/设备消失重建基线，不产生峰值；GPU 实例变化按有界周期重新枚举。
- [ ] 使用取消、释放和不重叠循环；隐藏、停用、关闭时停止采集，恢复后重新建立差分基线。检查实际宿主隐藏通知，不仅依赖激活/失活。
- [ ] 在本机验证正式后端的真实数值、权限及资源释放；记录未覆盖的多硬件/ARM64情形。

## 任务 3：卡片、设置与入口

- [ ] 新建 Controls/WidgetContents/SystemMonitorWidgetContent.cs 及 Services/SystemMonitorWidgetContentProvider.cs，复用 IWidgetContent 与共享窗口。
- [ ] 使用原生 TextBlock、ProgressBar、主题资源和系统字体；数值右对齐，单位明确，窗口窄时仍能读，菜单反馈能力状态。
- [ ] 新建 Views/SettingsSections/SystemMonitorWidgetSettingsSection.cs；配置沿用 WidgetConfig.Metadata，集中解析并校验，不另建无关存储模块。
- [ ] 接通 WidgetContentFactory、WidgetRegistry、WidgetManager.FeatureWidgets 和设置页延迟分区/菜单；单例、默认尺寸及恢复逻辑保持一致。
- [ ] 添加全部现有语言文案，更新受影响的 SystemMonitor 占位契约断言；保留其他未提交改动。

## 任务 4：交付与验收

- [ ] 静态审查线程、句柄、取消、异常和本地化，git diff --check。
- [ ] 停止仓库内 DeskBox，构建标准 Debug 并重新启动，确认唯一预期路径和初始化成功。
- [ ] 验证设置保存、采样频率、GPU/网卡切换、缺失行、隐藏恢复与关闭；按验收清单记录通过与未验证项。
- [ ] 测量启用/停用监控的额外 CPU、内存、句柄和线程变化；短测不冒充长期稳定性验证。
- [ ] 更新 PROJECT_MEMORY.md、Design 和验收记录；给用户展示真实监控。功能通过后再进入整体 UI 统一阶段。

## 受阻策略

原生计数器缺失不影响其他指标。GPU 引擎或容量无法可靠归属时标不可用并报告；不悄悄改成传感器库、WMI 循环或提权方案。若基础范围不能达到调研承诺，先记录具体阻碍并调整方案。
