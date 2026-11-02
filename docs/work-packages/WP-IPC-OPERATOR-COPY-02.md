# OPERATOR-COPY-02 · 全软件文案工业化（第二轮）

2026-11-02。用户要求“对整个软件中所有展示出来的文案重新设计优化，作为商业化工业软件需要足够专业，方便操作工和管理员使用”。基线 main `56bf77f`（含 PR171～173：相机链路闭环、统计与班次、异常处理、维护中心、首配五步）。分支 `codex/operator-copy-industrial-03`。

OPERATOR-COPY-01（PR168）已处理作业页。本包覆盖其余全部界面。只改展示文案，不改业务状态机、受理、RCS 请求、格口未确认不重发、原请求责任、配置账、权限和维护守卫。合同、审计、草稿、日志原文和 Application 层原因保持原值。

## 规则

沿用 [OPERATOR-COPY-01](WP-IPC-OPERATOR-COPY-01.md) 的“状态 / 原因 / 下一步”三层写法，并增加：

| 规则 | 说明 |
|---|---|
| 按钮 | 动词 + 对象，2～8 字；会弹窗的加“…”。不用“只读核对原…”这类长句 |
| 报错 | 发生了什么 + 影响什么 + 怎么处理 |
| 一个概念一个名字 | 表单、审阅、日志、报错用同一叫法（见术语表） |
| 不解释实现 | 界面不出现 CAS、Effective、候选、修订、回执、责任、冻结、同源介质、原根、退役 |
| 安全语句不弱化 | “软件暂停不是安全急停”“勿重复上件”“不会重发”保留原意 |

## 术语表

| 原用语 | 现用语 |
|---|---|
| 准入 / 未准入 | 受理 / 未受理 |
| 原件、原作业、原责任、未决输入 | 作业、未处理完的作业、未处理的扫码 |
| 回执 | RCS 返回 / 保存结果 / 操作结果 |
| 待核对（作业） / 待核对（维护） | 格口未确认 / 结果未确认 |
| 候选、完整候选 | 待保存的设置、完整配置 |
| Effective、权威配置 | 当前设置、已保存的配置 |
| 待生效修订、Pending | 已保存，重启后生效 |
| 受控重连 / 受控修复 | 手动重连 / 修复 |
| 快照失效 | 状态过期 |
| 隔离、实机隔离 | 调试、实机调试 |
| 冻结请求 | 待发送的请求 |
| 同源介质、同版安装器 | 原安装包 |
| 受保护部署登记、系统入口 | 安装登记、开始菜单和程序入口 |
| 退役 | 已卸载 |
| 原根 / 新根、隔离目录 | 原目录 / 新目录 |
| 已结历史到期维护 | 清理过期记录 |
| 能力库存、能力实例 | 已安装组件 |
| 新入场 | 供包 |

## 覆盖范围（约 780 条，91 个源码与测试文件）

| 区域 | 主要文件 | 示例（原 → 现） |
|---|---|---|
| 参数页、保存审阅、高级工具 | `ParameterSettingsControl.*`、`StructuredConfiguration`、`ConfigurationReviewPresentation`、`ConfigurationSaveReviewWindow`、`ConfigurationSubmission`、`MaintenancePage` | 保存更改… → 保存设置…；暂停新入场 → 暂停供包；能力库存与参数 → 已安装组件与参数；完整候选校验 → 配置检查 |
| 日志 | `SoftwareLogPage`、`SoftwareLogText`、`SoftwareLogRuntime` | 导出全部匹配… → 导出筛选结果…；查看原始上下文 → 查看前后日志；已应用范围 → 查询范围 |
| 作业记录、交接 | `HistoryPage`、`HistoryEditing`、`ParcelTargets`、`RequestHandover`、`ParcelDetailsProjection` | 核对所选原请求 → 查询所选作业的结果；原请求交接 · 保留原责任 → 交接给维护人员 |
| 诊断、异常处理、统计、维护中心 | `OperatorWorkspacePresentation`、`SupplyStationWindow.Diagnostics/ProductOperations`、`StationStatisticsControl`、`FaultGuidance` | 连接仍未恢复？查看受控重连 → 连接没有恢复？手动重连；处理待核对作业 → 处理格口未确认的包裹；心跳收到HTTP回应，业务结果另行核对 → 心跳正常（只表示 RCS 可连接） |
| 调试 | `StationDebugControl`、`StationDebugEvidenceControl`、`StationCameraPreviewControl` | 实机隔离已生效 → 实机调试已生效；本轮冻结请求内容 → 本轮待发送的请求内容 |
| 管理模式、图片 | `ManagementModeWindow`、`ManagementPasswordRecoveryWindow`、`ParcelPictureWindow` | 管理模式服务不可用，请使用同版安装器修复。 → 管理模式服务不可用，请用原安装包修复软件。 |
| 首配、启动修复、离线维护 | `Ipc.Desktop/StartupWorkflows`、`StartupWorkflowWindow.Maintenance`、`MaintenanceCenterWindow`、`ParcelApp` | 保存待生效修订… → 保存配置（下次启动生效）…；接管已恢复的原机目录… → 换机：接管已恢复的数据目录… |
| 安装、卸载、历史清理 | `StartupSetupWindow`、`ProductMaintenanceWindow`、`HistoryRetentionWindow` | 只读核对本次安装 → 查看本次安装结果；从同源介质继续原清单… → 用原安装包继续清理…；已结历史到期维护 → 清理过期记录 |

## 实现方式与守卫

- 只替换完整字符串字面量（含引号精确匹配），同一映射同步应用到测试断言；替换前检查 `Contains/StartsWith/EndsWith/IndexOf/Replace/Split` 是否以该文字做判断。
- 以文字做按钮查找的测试同步改名；按钮改名后与同窗口其他按钮冲突的，改用更具体的名字（诊断页“返回该设备设置”）。
- 保留不改：`ParameterRuntimePresentation` 中含“受控重连”的长句（`DualCameraDiagnostics` 以此断言）、RCS 超时合同文字、日志原文、CSV 列名、Application 层原因和审计文字。
- `StartupWorkflows` 中以按钮文字判断可见性的标签（下一步、上一步、检查配置、保存配置、完成、重新读取配置…）未改。

## 剩余

- Application 层原因（约 200 条，`Ipc.Application.ParcelFlow`）会出现在阻断原因和弹窗里，仍有“原请求”“阻断”等用语。它们同时进入审计和日志，需改为“原因代码 + 展示映射”后再统一替换，本包不动。
- 暂停的 Supply 业务（`Ipc.Application.Supply.Desktop`）不处理。

## 验证边界

- 已用脚本核对：替换前的测试字面量在源码中仍可找到；新文案不含会破坏 XAML 的字符；插值字符串花括号数量不变；修改过的 XAML 均能按 XML 解析。
- 当前云端环境无 .NET SDK，`dotnet build`、Application.Verification、Desktop.UiVerification 均为 **NOT_RUN**。合入后须在 Windows 本机执行三项 Release 构建与两套验证，并截图复核 1366×768、800×420 下参数页、日志页、维护中心与安装向导的换行和截断。
