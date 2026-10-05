# 开机启动修复与优先级对齐

## 调研报告

用户重启后 DeskBox 未运行。检查发现当前用户 Run 启动项存在，指向有效的标准 Debug 文件；应用设置 autoStart=true、模式 Standard，没有 DeskBox 登录任务。Windows StartupApproved 未发现 DeskBox 显式禁用项；日志止于上次关机，没有本次登录启动记录，事件日志未找到相关崩溃证据。因此只能确认本次未成功启动，无法断言确切系统原因。用原 Run 参数手动启动成功。

Nexus 通过当前用户 Run 启动，实际进程 PriorityClass=Normal。项目支持 ScheduledTask 模式，任务 Priority=4，对应 Normal，交互用户、LeastPrivilege、无电池限制、无运行时限、失败一分钟重试三次。

## 问题澄清

用户已授权配置启动并对齐 Nexus 优先级。对齐进程优先级为 Normal；Windows Run 项无明确启动先后保证，不承诺与 Nexus 同时启动。本次配置当前开发程序，不生成安装包或切换旧发布目录。

## 开发规划

备份配置与现有 DeskBox 启动命令；停止仓库实例，使用项目既有任务名称和参数注册登录任务；验证后清理仅本应用的旧 Run 项，切换设置模式，实际运行任务并检查唯一程序路径与优先级。失败保留或恢复旧启动项，不修改 Nexus。

## 配置执行

已完成。备份后切换设置为 ScheduledTask，注册项目标准每用户登录任务：InteractiveToken、LeastPrivilege、Priority=4、IgnoreNew、执行时限为零、电池允许、失败一分钟重试三次。读取并验证任务后移除本应用旧 Run 项，再通过 Start-ScheduledTask 启动。未修改产品代码，无需构建；未修改 Nexus。

## 验收清单

- [x] 任务启用、正确用户与路径，电池供电不阻止执行。
- [x] 任务实际启动唯一标准 Debug 实例，优先级 Normal；Nexus 与 DeskBox 都是 PriorityClass.Normal（32）。
- [x] 设置模式与实际注册一致，无重复 Run 项。
- [x] 日志启动完成，CPU/GPU 温度与基础监控正常渲染。
- [ ] 下次真实重启仍需实机确认；未代替用户重启电脑。

任务 LastTaskResult=267009（0x41301，任务正在运行），符合持续运行的桌面程序，不是启动失败。静态差异检查通过；未运行测试和构建，因为仅变更本机注册及设置。

## 上线与收尾

仅本机配置，未公开发布；配置备份保存在忽略的本地证据目录。回退为删除本次任务、恢复备份设置和 DeskBox Run 命令，不影响其他软件。
