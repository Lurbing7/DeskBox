# Dock 与任务栏闪烁调研

## 结论与实测

可实施独立 Dock 内容，复用 DeskBox 窗口、原生材质、生命周期和文件启动服务，不增加常驻进程。目录作为分类，不恢复 Tags 模型。Windows RegisterShellHookWindow 提供 HSHELL_FLASH、窗口激活与销毁事件；本机独立测试成功注册并收到测试窗口 FlashWindowEx 产生的多次闪烁事件。此结论不代表微信所有状态已验证。

## 接口边界

事件给出请求关注的窗口句柄，不提供消息正文、未读数量或微信会话。监听开始前已发生的闪烁没有通用历史查询接口。按进程关联提示，激活对应应用或手动清除提示；重复闪烁不累加成消息数量。微信退出到托盘、提升权限、Explorer 重启、会话切换需实测。微软注明该接口不面向一般用途，未来版本可能变化，失败须明确显示而不影响启动功能。

Dock 第一版采用屏幕底部悬浮窗口，不注册 AppBar 占据工作区，避免与系统任务栏自动隐藏及 Nexus 抢占同一边缘。点击文件夹在按钮上方展开横向应用行，超出可滚动；点击外部由原生 Flyout 关闭，文件夹直接对应目录。窗口激活受 SetForegroundWindow 限制，不强制抢焦点。图标加载使用项目图标服务，内容扫描限制根目录与展开的一层。

## 资源与替换 Nexus

事件监听替代扫描任务栏颜色；不读取聊天数据库、不注入应用、不要求新驱动。复用现有进程但不承诺一定比 Nexus 低内存，须以同配置测量确认。Nexus 保留运行，自启及系统任务栏状态不变，体验验收后再考虑替换。

## 官方依据

- [RegisterShellHookWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registershellhookwindow)
- [FlashWindowEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-flashwindowex)
- [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
- [Application Desktop Toolbars](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars)

检索及测试日期：2026-10-03。Dock 可开发，微信、实际文件夹交互和长期资源成本作为验收项保留。
