> 2026-10-27 供包员主页与维护诊断分层按 [OPERATOR-WORKSTATION-01](../work-packages/WP-IPC-OPERATOR-WORKSTATION-01.md) 纠偏；本页冲突呈现要求保留历史范围，业务守卫与原验证身份不变。

# 作业状态、管理入口与日志双视图

历史实施、版本和验收原件见[归档](../archive/pre-fresh-baseline-current-guides-01/README.md)；原PASS只属于原SourceCommit。当前仅支持从零安装、当前格式建库；非当前输入保留原件并拒绝，请使用全新数据目录。

## V2对照与修复

左右设备绑定与业务实例原本独立，差距在信息呈现：设备状态为灰色小字，地址和扫描依据在折叠区，空态扫描组消失，RCS状态只在详情中。现在每位固定相机头、扫描、任务三组；共享RCS在顶部状态行，各相机状态在标题旁采用文字与语义颜色。正常格口仍占最高权重，波次/任务/商品编码/返回数量沿同一回执显示。相机地址在常规窗口直接可见；最小高度保留连接状态，把低频地址和扫描时间留在详情。空态仅保留可信占位，不制造任务字段。

下一步说明移到每位固定操作栏，避免长拒绝原因在800×420下把下一步挤走。原原因仍可滚动、选择与复制。保留两位、格口、条码、原请求、拒绝/未知与物理完成的区别；V2的历史帮助/首配导航不恢复为当前功能。

## 状态证据

相机在线只来自有效DeviceState；断线/故障为离线，连接中、缺确认、确认过期和快照失效单独显示。明确延后连接显示待配置，模拟端口标模拟。共享RCS复用现有确认：本地客户端启动不能显示远端在线；有效心跳可以显示在线，但不证明本次取格口已成功。状态呈现不修改Application准入、资格、旧请求或重试行为。

## 管理与参数

桌面新运行入口调用 `WorkstationEntry.BeginRuntimeAsync`，结束同进程首配/维护遗留授权后再创建工作窗。普通状态无需执行退出操作；管理服务报告不可用时原状态读取会撤销本地授权。页面切换/子窗不调用这一入口，不重建业务。

两套宿主都拒绝直接进入未授权的参数/日志页面。顶部管理密码仍控制临时管理模式，退出/到期的任务取消、日志失效、草稿保护和重新验证后续办保持。原授权结束常驻长提示已移除；实际操作失败仍反馈。

管理密码属于受保护的本机管理服务，不是能力目录中的登录组件。目录仅按当前实际用途展示操作员登录，完整候选与业务身份守卫保持。

## 日志使用

2026-10-27追加的[调试模式与实时终端](debug-log-live-01.md)提供热开关、实时跟随和暂停显示；下述固定范围行为继续适用于历史查询，原证据不扩展到该增量。

进入管理模式 → 日志查询 → 查询，使用显示方式选择“列表视图”或“终端视图”。两种视图使用同一固定范围和200条分页，切换不另查、不改变所选行，支持选中复制。终端为只读显示控件，不启动系统命令行。多行原文展开；后台格式化，上限65536字符，达到预算明确提示完整导出。

筛选改变、离页或撤权清空两个视图；迟到查询不得恢复旧内容。分页、上下文、完整导出与原日志授权机制保持。界面状态、操作员回执、生产资格和左右位标签使用中文；外部标识、协议值和原始日志/报文作为证据保留原文，不篡改源记录。

## 复现与交付边界

所有Windows命令先 `. ./scripts/project-environment.ps1`，产物和隔离数据在D盘。

```powershell
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/projects/importer-pc --workstation-status-logs
# 既有回归：--task-hierarchy、--management-mode、--reliability-logs
```

冻结介质入口 `scripts/verify-commercial-wpf.ps1 -WorkstationStatusLogs` 已加入现有Windows CI。原D/RV、SDK、资格、现场条件保持；不自动安装或合并PR。历史同源身份、实际结果、初次失败与截图见[证据](evidence/workstation-status-logs-01/README.md)。
