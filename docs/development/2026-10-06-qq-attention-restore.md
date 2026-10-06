# QQ 关注入口恢复：开发流程

日期：2026-10-06；阶段：代码、针对性验证与用户实机验收完成，进入发布流程。

## 1. 调研报告

用户确认微信关注入口已正常，QQ 点击却显示停滞页面。10:27 点击日志确认 QQ 隐藏的 `Chrome_WidgetWin_1` 接收 `SC_RESTORE` 后变为可见且前台验证成功，随后用户报告页面卡住。当前通用恢复仅验证 Win32 可见和前台状态，没有经过 QQ 自身的窗口显示与页面更新逻辑，不能视为完整成功。

本机 QQNT 9.9.35-52892 的闪烁窗口与唯一注册托盘窗口同进程，托盘类名精确为 `Electron_NotifyIconHostWindow`；只读 Shell 查询确认图标 ID 为 3。只读核对其窗口处理函数，回调为 `WM_APP+1`，通过 wParam 匹配图标 ID，lParam 使用传统鼠标事件；与微信的版本 4 坐标/事件打包不同，不能直接复用微信回调。

2026-10-06 核对官方源码：

- [Electron v32.2.0 托盘宿主](https://github.com/electron/electron/blob/v32.2.0/shell/browser/ui/win/notify_icon_host.cc)：精确宿主类名、`WM_APP+1`、wParam 选择图标、lParam 鼠标事件。
- [Electron 托盘事件分发](https://github.com/electron/electron/blob/v32.2.0/shell/browser/ui/win/notify_icon.cc)：托盘回调转入应用 click/double-click 事件；此为协议参考，本机定制差异另行核对。

## 2. 问题澄清

修复 QQ 当前通知窗口的打开和更新，不新启 QQ、不修改 QQ/任务栏设置，不读取聊天内容。保留已由用户确认的微信逻辑。成功条件：应用自身恢复路径收到正确请求，随后验证 QQ 前台窗口；真实 QQ 最新消息与可交互状态由用户验收。

## 3. 开发规划

1. 确认 QQ 实际托盘事件和唯一注册入口，限定同进程、精确类名，不尝试任意事件轮番发送。
2. 为 QQ 通知接入应用托盘恢复；失败保留提醒，不再退回会产生停滞页面的 `SC_RESTORE`；其他应用及微信路径不改动。
3. 用独立注册托盘夹具验证真实回调路由、图标 ID 与未知/歧义入口拒绝；保留微信回归。
4. 完成快速检查，按项目要求停止仓库 DeskBox、构建并重启标准 Debug；公开发布不在本轮范围。

## 4. 代码开发

修复前用本地独立测试进程注册 Electron 托盘和隐藏应用窗口，执行原生产 `ActivateNotifiedWindowAsync`：`reported=True, shown=True, application-activation=0, forced-restore=1`，复现“原生可见/前台成功但未调用应用恢复”。测试进程名为 QQ，仅用于验证原生产分支，不操作真实 QQ。

已确认本机回调接收端与官方 Electron 协议一致；先按托盘单击 `WM_LBUTTONDOWN` 接入，用户原托盘单/双击行为的可选澄清等待回复。枚举只查询图标 ID 3–255，要求同进程精确宿主内唯一注册图标；超出范围、未注册或多图标歧义直接失败，不猜测 ID、不向所有图标广播。不读取安装包内的加密业务代码。

已新增 `DockQqTrayActivation`，发送前再次核对进程、类名和 Shell 注册信息；限时发送一次正确协议的托盘单击。`DockAttentionListener` 仅为 QQ 增加应用托盘路径，保留用户点击时目标进程前台授权与原有前台验证；QQ 失败直接返回，不再走 `SC_RESTORE`。微信恢复实现不变。

完整生产路径本地回归：修正后 `reported=True, shown=True, application-activation=1, forced-restore=0`。真实 QQ 只读探测确认 `registered-icons=1, id=3`，没有自动点击真实 QQ。

已增加 QQ 注册托盘夹具和 11 项测试，覆盖 ID 3/19 的实际回调、只读不发送、未注册/歧义入口拒绝及进程/类名边界。初次执行两项失败（`UnregisteredTrayWindowIsNotClicked`、`AmbiguousTrayIconsAreNotClicked`）：夹具从旧测试改写时仍无条件注册一个图标，未按测试参数建立相应场景；已修正夹具注册条件并复验，生产代码未为通过测试而放宽限制。与现有 12 项微信回归共同使用本地隔离项目编译同一份生产源码和测试，23 项全部通过，执行 191ms；未运行全项目测试套件。

## 5. 验收清单

- [x] 日志复现通用恢复显示成功、实际页面异常的差异。
- [x] 本机唯一同进程 Electron 托盘入口及回调协议只读核对。
- [x] QQ 已接入托盘单击协议及真实入口只读核对；真实 QQ 单击打开语义待下项实机确认。
- [x] 生产监听器完整路径夹具由失败转为通过；QQ 与微信 23 项针对性测试、代码审查及 `git diff --check` 通过。
- [x] 标准 Debug 构建 0 错误、22 条现有警告，41.23 秒；停止旧仓库实例后构建。
- [x] 10:55 重启单个标准 Debug（PID 55316），路径 `src/DeskBox/bin/Debug/net10.0-windows10.0.22621.0/DeskBox.exe`；两屏关注监听均可用，启动 0 降级、0 失败。
- [x] 用户确认「现在正常了，暂时没有发现其他的问题」，QQ 通知恢复实机验收通过。

## 6. 上线与收尾

用户已确认真实 QQ 交互正常，并明确要求发布。按当前单击协议验收通过，无须等待原托盘手势的可选澄清；发布进度见 [preview.2 发布流程](2026-10-06-preview2-release.md)。回退仅撤销本轮 QQ 恢复接入。

建议实际验证：QQ 收到新消息且窗口关闭到托盘后，单击 Dock 左侧通知，核对最新消息、会话切换和输入响应；重复一次，确认不出现停滞页面。若系统托盘原本只响应双击，则根据用户澄清调整这一事件，不叠加多个试探回调。

完整项目中的针对性复验命令（本轮已用隔离项目验证同一份托盘源码和测试，未执行此项目依赖构建）：

```powershell
dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore --verbosity:quiet -p:Platform=x64 --filter "FullyQualifiedName~DockQqTrayActivationTests|FullyQualifiedName~DockWeChatTrayActivationTests"
```

回退仅移除监听器的 QQ 托盘分支及 `DockQqTrayActivation`，保留已实机确认的微信恢复和此前豆包身份修复，不改 QQ、任务栏或 Dock 配置。
