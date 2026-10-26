# 调试模式与实时终端

在管理模式进入“日志查询”。勾选“调试模式”后立即输出详细日志，取消后恢复常规日志；操作完成文案来自当前运行实例的实际状态。无需保存业务参数或暂停供包。设置只作用于当前运行实例，多窗口共享；退出管理不关闭已经开启的记录，重启默认关闭。旧服务未提供可选运行诊断契约时隐藏此开关，不显示虚假能力。

日常输入处理结果、作业状态、异常和命令审计继续记录。调试模式增加扫码来源/实体/事件/序列/时间、候选与筛选依据、准入原因、原请求/配置/合同/目标身份、发送次数/超时/耗时、响应码/正文/业务接受事实及异常堆栈。连接变化立即记录，稳定连接确认每5秒采样；不以调试记录或HTTP成功替代业务接受和物理完成。

选择“终端视图”，勾选“实时跟随”即可每秒读取当前实例已经落盘的新记录。点击“暂停显示”，画面与选区保持，后台继续记录；“继续显示”按原游标补读。页面退出或管理授权失效后停止读取并清空受限内容，迟到结果不能恢复旧画面。实时读取异常明确暂停显示，用户可继续显示重试。

实时跟随应用当前关键词、级别和来源；时间范围用于历史查询。取消实时跟随恢复固定历史页，修改筛选或重新查询清空旧内容。“列表视图”和未跟随的“终端视图”共用同一固定历史快照；分页、上下文和导出保持原授权与固定范围。正在实时跟随时禁用固定范围分页/导出，取消跟随后即可查询导出。

实时缓存最多512条且最多1Mi字符；首次最多显示最近200条，每次补读最多200条。暂停过久导致缓存轮转会明确告知缺失条数，按暂停时段查历史；历史文件也可能已按原轮转/保留策略清理，不能承诺无限保存。终端只保留65536字符；每条实时显示也有预算，原文件查询/导出仍以实际文件为准。

详细日志走独立有界队列，预留常规记录容量；写入压力过大时丢弃可选调试记录，显示计数并追加告警。原4个各约5MB轮转文件与保留天数继续使用。诊断报文片段最多8192字符、原始候选最多16个各256字符，显式标明原长度与省略；原请求和回执账不因此截断。诊断日志/界面/导出对敏感字段及嵌套JSON遮蔽；外部协议字段和原始异常作为证据保留其语言，界面操作文案统一中文。

Windows命令先执行`. ./scripts/project-environment.ps1`：

```powershell
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/projects/importer-pc --debug-log-live
# 冻结交付：scripts/verify-commercial-wpf.ps1 -Media <介质> -Output <新的D盘目录> -DebugLogLive
```

契约为可选`IRuntimeDiagnosticsControl`，原`IWorkstationAdministration`和配置格式不变；实现为`ParcelRuntimeDiagnostics.cs`、`RuntimeJournal.Tail.cs`、`SoftwareLogRuntime.cs`。不增加运行依赖；继续使用原运行目录和保留配置。[工作包](../work-packages/WP-IPC-DEBUG-LOG-LIVE-01.md)与[本次证据](evidence/debug-log-live-01/README.md)区分源、构建、模拟和真实现场。旧证据保持原身份。
