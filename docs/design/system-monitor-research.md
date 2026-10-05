# 性能监控调研

## 现有产品参考补充

[游戏加加](https://gamepp.com/features.html) 提供 CPU/GPU 占用、频率、温度、风扇、功耗及多显卡桌面监控。官网未公开采集实现，不能由界面推断通用系统 API 能覆盖所有参数。[AIDA64 SensorPanel](https://www.aida64.com/user-manual/sensorpanel) 采用传感器与可配置面板分离；[HWiNFO 共享内存](https://www.hwinfo.com/forum/threads/shared-memory-support.18/) 可供第三方消费，但接入前需要核实授权和限制。

本项目借鉴采集与展示分离、设备明确、单位一致和不可用值显式表示。首版原生基础计数器不依赖外部监控程序；后续评估 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 的硬件、权限和许可。本版不承诺游戏加加全部传感器覆盖。

日期：2026-10-02。结论：无需提权的基础监控可实现；参考图全部传感器指标不能保证在所有硬件上直接读取。首版建议先交付 CPU、内存、GPU、显存和网络的基础指标，温度、风扇、功耗作为后续可选传感器扩展。

## 项目接入与本机验证

项目为 .NET 10 / WinUI 3，已预留 `WidgetKind.SystemMonitor`，但 factory 为占位 provider、registry 未实现。可沿用现有功能格子入口、共享 ContentWidgetWindow / WidgetShell 和 IWidgetContent 生命周期，无需建立新窗口架构。

本次只读短探测结果：GetSystemTimes 两次采样差值有效；GlobalMemoryStatusEx 返回有效物理内存；GPU 性能类存在引擎与适配器内存实例，专用显存用量可读；活动网卡的累计字节可读。本次还发现多 GPU 实例及大量活动网卡，必须设计选择与去重策略。没有安装驱动、引入依赖、提权或改动应用。

这些结果只证明当前机器能访问相关接口，不代表已经验证 PDH 正式采集、GPU 计算口径、显存总容量、长期成本、所有硬件或 ARM64。未发现已有 LibreHardwareMonitor / OpenHardwareMonitor WMI 命名空间；未验证温度、风扇或功耗读取。

## 数据来源与边界

| 指标 | 首版来源与口径 | 限制 |
|---|---|---|
| CPU 占用 | PDH Processor 总忙碌时间；GetSystemTimes 差值作为受限回退 | 不承诺与任务管理器“处理器效用”完全相等；GetSystemTimes 在超过 64 逻辑处理器时有处理器组限制 |
| CPU 频率 | Windows Processor Information 频率计数器；明确标为系统报告值 | 不宣称每核真实有效频率；固定或不可用数据须说明，不能假装瞬时频率 |
| 内存 | GlobalMemoryStatusEx：总物理内存减可用物理内存 | 不混用提交量或虚拟内存 |
| GPU 占用 | PDH GPU Engine：按适配器与引擎实例聚合同一引擎各进程，再取最忙引擎 | 不把所有引擎相加；计数器缺失显示不可用；不能假设恒定 3D 引擎代表全部负载 |
| 显存 | GPU Adapter Memory 专用用量；DXGI 匹配同一适配器的专用容量 | 共享内存不能冒充专用显存；无法可靠匹配或集显无专用容量时不给伪造百分比 |
| 网络 | 单个已选网卡累计收发字节差 / 实际经过时间 | 默认选有可用路由的活动接口；允许手选；不汇总所有活动接口，避免重复统计 |
| 温度、风扇、功耗、GPU 时钟 | 首版明确不可用，后续可选传感器后端 | 依赖厂商接口、硬件、驱动和权限；CPU 风扇通常来自主板，不可擅自归属 |

PDH 使用 PdhAddEnglishCounterW 避免中文 Windows 计数器名称本地化问题。正式实现需处理冷启动、实例消失、无数据和非有限值，不能只检查“添加计数器成功”。

## 传感器方案比较

建议首版不新增监控 NuGet 或内核驱动，使用 Windows 原生 API 和 .NET NetworkInterface。运行时不通过 PowerShell 或子进程定时查询 WMI。

LibreHardwareMonitor 官方库可支持多类传感器，项目当前列出 .NET 10 支持，采用 MPL-2.0，且有第三方许可说明；部分传感器需要管理员权限。采用前仍须核对实际发布包、依赖、架构、AOT、驱动和分发兼容性。因此不把整款 DeskBox 改为 requireAdministrator，也不把缺失传感器当作“软件安装后必定解决”。需要完整传感器时，应另行评估可选隔离后端及明确授权。

## 官方与上游依据

- [GetSystemTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes)
- [GlobalMemoryStatusEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-globalmemorystatusex)
- [GPU 与任务管理器统计口径](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/)
- [PdhAddEnglishCounterW](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhaddenglishcounterw)
- [Windows 报告的 CPU 频率语义](https://learn.microsoft.com/en-us/windows/win32/power/processor-power-information-str)
- [LibreHardwareMonitor 官方项目与权限说明](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)

## 建议验收范围

先验收无提权基础版，传感器扩展不作为首版完成条件。如果温度、功耗和风扇必须首版真实显示，则需要先完成传感器后端实测，不能按本方案直接进入基础版开发。
