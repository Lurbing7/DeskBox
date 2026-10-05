# 撤下标签分类功能

## 调研与澄清

用户明确要求撤下所有本轮标签分类修改，保留性能监控。标签类型枚举及占位实现原本已存在；恢复这些基线，移除本轮分类实现、设置入口、原生拖放接入、专用测试、本地化和专用设计/原型。文件卡片网格、性能监控、开机启动及共享 UI/文档技能保留。分类目录和原始快捷方式属于用户数据，保留并备份，不自动删除或迁移。

## 开发规划

备份当前差异及分类文件到忽略目录；按 HEAD 对照选择性撤销标签部分，不整体回退混合文件。撤销注册后旧标签配置不会恢复窗口。构建标准 Debug 并重启，验证监控与原文件卡片；不改发布产物。

## 代码开发

已完成。选择性恢复 Tags 原有占位 descriptor/provider/registry，撤销单例窗口、功能开关、设置模板、菜单、尺寸吸附及拖放增量，移除标签专用实现、测试、设计稿及 HTML 原型。12 种语言恢复标签占位文本并移除本轮 Tags.* 增量；27 个 Monitor.* 文本保留。JSON source-generation 清单恢复标签加入前的基线。共享设计技能去除失效设计稿链接并同步运行副本。

## 验收清单

- [x] 标签实现和入口移除，原有占位类型保留；运行日志明确跳过旧 Tags 配置。
- [x] 性能监控和文件网格修改保留；日志确认 CPU/GPU 温度及基础指标渲染。
- [x] 未操作原应用文件，分类数据字节与备份一致。
- [x] 静态差异检查、Debug 构建（0 错误、22 个既有警告）、唯一标准实例启动。
- [x] 完整测试未运行；建议 `dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore --verbosity:minimal -p:Platform=x64 --filter "FullyQualifiedName~WidgetContentFactoryTests|FullyQualifiedName~WidgetRegistryTests|FullyQualifiedName~FeatureWidgetKindContractTests|FullyQualifiedName~JsonSerializationBaselineContractTests|FullyQualifiedName~SystemMonitorMetricsTests"`。

## 上线与收尾

仅本地撤销，未公开发布。备份保留在本地忽略目录，可恢复撤销前代码及用户数据。

旧分类配置保留在用户数据中但不再恢复窗口；不自动迁移应用引用为文件卡片或删除分类目录。当前唯一程序路径为 `src/DeskBox/bin/Debug/net10.0-windows10.0.22621.0/DeskBox.exe`，通过既有登录任务启动。实际 UI/拖放及完整测试未运行，源码撤销和监控运行验证不替代这些验收。
