# 性能监控传感器与 UI 复核

日期：2026-10-02。范围：本机只读探测、公开官方/上游资料、网上设计技能筛选。未修改应用代码、未安装驱动、未发布。

## 1. 纠正结论

用户的游戏加加截图证明本机能读到 CPU/GPU 温度和功耗。此前联想 WMI 返回无效结果，只能说明该接口路径不适用，不能说明硬件没有数据。当前实现把七个传感器项直接置空，是首版后端缺失，不是完整监控能力。

截图可见 HWiNFO 标识，本机只读服务检查也发现运行中的 HWiNFO 驱动。这是游戏加加采用 HWiNFO 技术的强线索，尚未确认其 SDK 合同或全部内部实现。未发现独立 HWiNFO 进程；标准共享内存 `Global\\HWiNFO_SENS_SM2` 与非全局同名映射均不存在。内嵌 SDK 不等于开放独立程序共享数据，不能直接复用游戏加加 DLL/驱动。

## 2. 已验证数据路径

| 指标 | 本机结果 | 开发结论 |
|---|---|---|
| NVIDIA GPU 温度 | nvidia-smi 返回 52 °C | NVIDIA 驱动接口可用，不必依赖联想温度接口 |
| GPU 功耗 | 返回 6.90 W | 必须标清驱动报告口径，不能冒充额定 TDP |
| GPU 核心/显存频率 | 返回 285/810 MHz | 可通过厂商接口扩展正式采集 |
| GPU 风扇 | 返回 N/A | 笔记本风扇可能由 EC 管理；不可写成 0 RPM |
| 联想 CPU/GPU 温度 | 上轮 WMI 返回 0 | 无效，不能作为真实温度 |
| CPU 温度与功耗 | 游戏加加截图已显示；DeskBox 后端未实测成功 | 需要硬件采集引擎，不能继续用 PDH 顶替 |

这些数值是一次快照，未做高负载或与游戏加加同步比较。nvidia-smi 用于短探测，正式应用应直接调用 NVML，避免每秒启动进程。先匹配所选 DXGI 适配器身份，再显示 NVML 数据；不能按第一张显卡或只按名称拼接。错误码、缺失驱动、休眠、集显及设备切换均需回退。

## 3. CPU/风扇方案

| 方案 | 优点 | 限制/状态 |
|---|---|---|
| HWiNFO SDK | 官方提供温度、风扇、功耗等集成接口；与截图技术线索一致 | 官方要求联系获取集成与报价，不是可直接随项目分发的开源库；未联系厂商 |
| HWiNFO 独立程序共享内存 | DeskBox 可只消费外部数据，不自行实现硬件访问 | 本机映射不存在；用户需单独运行并开启共享；官方非 Pro 64/ARM64 版本有 12 小时限制 |
| LibreHardwareMonitor v0.9.6 / 上游 | 开源；AMD CPU 源码存在 Tctl/Tdie、CCD、功耗/SMU 路径 | 当前上游 AMD 后端使用 PawnIO；发布包与源码对应性、7945HX、驱动安装、权限、许可证及 AOT 仍须验证。不能宣称一定拿到全部风扇/功耗 |
| 联想 WMI | 不需自建底层驱动 | 已测温度无效、风扇报错，不能作为本机主路径 |

建议：GPU 先用已验证 NVML 路径；CPU 以独立 LibreHardwareMonitor 探测组件验证，成功后设计可选传感器后端。只读采集，不写风扇/超频/电压配置；不整体改 DeskBox 为 requireAdministrator。EC 风扇独立验证，不能错误归属到某块 GPU。软件能读取传感器不等于所有软件都使用同一个公开系统 API。

## 4. UI 问题与改造依据

当前源码：主要文本统一 12 DIP，设备名/状态 11 DIP、标题 12 Semibold；小字号、较小图标和密集间距叠加，缺少参考图的清晰层级。负载是固定 FontIcon，温度图标颜色也固定；还没有根据指标改变扇形和温度状态。程序化 Application.Resources 获取不等同于动态 ThemeResource，后续核对明暗与宿主资源。

图二目标：CPU/GPU 轻量分块，每行图标—标签—数值；标题略大、标签与数字统一 Body；内存/显存以横条承载用量；网络上传下载同栏，IP 保留辅助行。采用系统默认字体和中文回退，以 14 DIP 正文、12 辅助、16 分组标题作为可测提案，不照抄截图物理像素。比例应在当前 150% DPI 实机对照。

动态图标必须与数字来自同一快照：负载扇形面积真实变化；温度警示依据明确阈值；缺失值中性状态，与有效 0 区别；颜色之外提供状态提示。阈值不能由参考图中 103 °C 等示例反推为硬件规则。此轮仅制定规则，尚未实现动态图标。

## 5. 技能筛选与落地

检索并阅读社区 fluent2-design 和 Anthropic frontend-design。前者主要面向 Web Fluent 2、许可证未明确，且部分可访问性说明有误；后者偏创意网页。两者均不能原样作为 WinUI 原生标准。项目新增原创 `.dsh/skills/windows-native-ui/SKILL.md`，以微软官方字体、主题、材质、图标规范为依据；具体来源与筛选记录位于该技能 references。现有 deskbox-ui-design 已链接新技能。

## 6. 下一步规划与验收

1. NVML 原生封装、适配器身份映射，验证 GPU 温度/功耗/频率实际更新和不支持项回退。
2. CPU 独立采集探测：选择固定 LHM 发布版本，核对依赖/驱动许可，实测 7945HX 温度与功耗；涉及安装驱动的具体操作先确认。
3. 快照扩展、数据来源/过期状态、后台生命周期；不让厂商调用阻塞 UI。
4. 使用新技能统一字体/间距，绘制动态负载扇形与温度状态，保持现有 IP 和位置。
5. 同时刻与游戏加加读数对照，记录来源/采样口径；缺失、0、设备变化、休眠、明暗主题、150% DPI、文本缩放及长期成本分开验收。

状态更新（用户确认后的第四轮）：CPU 独立 LHM 0.9.6 宿主与 NVIDIA NVML 正式接入；本机安装官方签名 PawnIO 2.2.0 后，7945HX Tctl/Tdie 和 Package 功耗正常读取。动态扇形、温度变色及可设置阈值已实现；12 种语言键一致。14 项快速检查、Debug 构建及主程序渲染日志通过。风扇转速仍缺可靠来源，普通权限、AOT、长期成本和人工视觉仍待验收，未公开发布。执行详情见 [主流程](../development/2026-10-02-system-monitor.md)。

## 依据

- [HWiNFO SDK](https://www.hwinfo.com/sdk/)：集成接口及询价。
- [HWiNFO Licenses](https://www.hwinfo.com/licenses/)：共享内存与使用范围。
- [NVIDIA NVML](https://docs.nvidia.com/deploy/nvml-api/index.html)：温度、功耗、频率等设备读取；硬件不支持时应处理错误码。
- [LHM AMD CPU 上游源码](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LibreHardwareMonitorLib/Hardware/Cpu/Amd17Cpu.cs)、[发布](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/tag/v0.9.6)：当前路径与发布边界。
- [Windows Typography](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/typography)、[Theme Resources](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources)：字号层级及主题更新。
