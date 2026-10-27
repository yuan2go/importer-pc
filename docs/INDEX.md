> 独立读码服务：[工作包](work-packages/WP-IPC-BARCODE-READER-SERVICE-01.md)、[设计](design/barcode-reader-service-01.md)、[当前实施](runtime/barcode-reader-service-01.md)。当前待协议租约裁决及实际 VM 方案 SDK 核验；不得据设计声明服务已就绪。

> 条码生产者统一接入：[BARCODE-PRODUCER-01](work-packages/WP-IPC-BARCODE-PRODUCER-01.md)。[协议 v1](protocols/barcode-producer-v1.md)、[实现设计](design/barcode-producer-01.md)、[黄金对话](protocols/barcode-producer-v1.golden.json)、[运行说明](runtime/barcode-producer-01.md)、[验证回执](runtime/evidence/barcode-producer-01/README.md)。本型号由 PC 生产者解码，通过 TCP 接入；供包台内置 MVS 扩展限诊断，独立服务复用 MVS 采集不受此扩展用途限制。

> 2026-10-27 单相机作业、显示整理及固定根密码恢复以[STATION-SINGLE-CAMERA-RECOVERY-01](work-packages/WP-IPC-STATION-SINGLE-CAMERA-RECOVERY-01.md)为准：一台就绪相机可服务对应位置；恢复密码只用于重置，原管理授权随重置撤销。旧全连接阻断/G02未定政策保留历史范围；MVS与现场边界继承。

> 作业记录与日志查询当前界面和验证入口见 [QUERY-FILTER-UX-01](runtime/query-filter-ux-01.md)。

> 2026-10-27 查询布局与本地日期时间呈现以 [QUERY-FILTER-UX-01](work-packages/WP-IPC-QUERY-FILTER-UX-01.md) 为准；旧默认 UTC 输入和长时间摘要保留历史范围，查询合同不变。

> 2026-10-27 供包员主页与维护诊断分层按 [OPERATOR-WORKSTATION-01](work-packages/WP-IPC-OPERATOR-WORKSTATION-01.md) 纠偏；本页冲突呈现要求保留历史范围，业务守卫与原验证身份不变。

> 2026-10-27 日志热生效调试模式与实时终端暂停以 [DEBUG-LOG-LIVE-01](work-packages/WP-IPC-DEBUG-LOG-LIVE-01.md) 和[操作说明](runtime/debug-log-live-01.md)为准。作业三组呈现、醒目连接状态、普通启动与日志双视图以 [WORKSTATION-STATUS-LOGS-01](work-packages/WP-IPC-WORKSTATION-STATUS-LOGS-01.md) 为准；本文冲突布局条款保留历史适用范围，字体与业务守卫继承。

> 2026-10-26 RCS组件超时与异常展示：新增1.3.4、组件请求时限及拆包/message投影；明确超时后立即允许新扫码申请，原请求Unknown及记录保留。见[工作包](work-packages/WP-IPC-RCS-TIMEOUT-ERRORS-01.md)与[运行及验证](runtime/rcs-timeout-errors-01.md)。历史接口及验收身份保留。

> 2026-10-27 开发阶段统一当前契约：不再维护旧版本运行兼容；清理范围、原件保护和定向验证以 [CURRENT-CONTRACT-CLEANUP / 独立工作包](work-packages/WP-IPC-RCS-TIMEOUT-INDEPENDENT-REVIEW-02.md) 第0、9节为准。下文及旧版本说明中的迁移/兼容成功目标保留历史适用范围，当前精确装配、未知责任、权限和恢复守卫继续有效。

# 文档索引

2026-10-27 当前自有产品版本统一为1.0.0，见[工作包](work-packages/WP-IPC-UNIFIED-VERSION-1-0-0.md)和[首次安装测试交付](runtime/unified-version-1-0-0.md)。本条替代旧当前版本号，历史证据、外部SDK/协议与数据格式保留原身份。

当前构建与测试入口修复见[BUILD-TEST-FOLLOWUP-01](work-packages/WP-IPC-BUILD-TEST-FOLLOWUP-01.md)：Supply默认构建、无参数Application验证及station平台/存储规则。

当前唯一规则：[从零安装工作包](work-packages/WP-IPC-FRESH-BASELINE-01.md)、[运行入门](runtime/getting-started.md)、[精确格式规则](runtime/version-compatibility-01.md)、[基线结构等价证据](verification/fresh-baseline-01/README.md)。此前指南的迁移步骤已移入[首批原字节归档](archive/pre-fresh-baseline-01/README.md)与[随包指南原字节归档](archive/pre-fresh-baseline-current-guides-01/README.md)，不用于当前运行。

2026-10-26 首次安装计划任务缺失及修复失败状态：见[工作包](work-packages/WP-IPC-INSTALLER-TASK-LOOKUP-01.md)和[实施及定向验证](runtime/installer-task-lookup-01.md)。保留原安装登记，沿原系统入口修复，不重放安装。

本索引区分当前规则、进行中的整合和历史证据。2026-10-26 起点 main 为 `a8cd4314`；最新整合状态只在[本轮工作包](work-packages/WP-IPC-INTEGRATION-20261026.md)记录，旧 PASS 不随合并迁移。

## 当前规则与操作

| 阅读目的 | 当前入口 |
|---|---|
| 本轮意图台账、时间线与执行顺序 | [INTEGRATION-20261026](work-packages/WP-IPC-INTEGRATION-20261026.md) |
| 架构与职责 | [框架](design/framework.md)、[F决策](design/framework-decisions.md)、[独立Application](design/application-architecture-22.md) |
| 当前合同、旧输入拒绝与原件保全 | [当前合同收口](runtime/current-contract-cleanup-01.md)、[工作包K01～K15](work-packages/WP-IPC-RCS-TIMEOUT-INDEPENDENT-REVIEW-02.md) |
| 管理模式与操作员权限 | [管理模式](runtime/management-mode-01.md)、[工作包](work-packages/WP-IPC-MANAGEMENT-MODE-01.md) |
| 参数与首次启动 | [用户纠偏](work-packages/WP-IPC-PARAMETER-GUIDANCE-STARTUP-01.md)、[延后映射/恢复](runtime/validation/deferred-heartbeat-01.md)、[参数共模](design/parameter-refactor-01.md) |
| 参数设计 | [PARAM-UX-02](design/parameter-settings-experience-02.md)、[参数V3](ui/parameter-settings-v3-20261020/README.md)、[字段继承](design/parameter-inheritance-02.md) |
| 作业与首次启动设计 | [V2交接](ui/sportmaster-task-redesign-v2-20261020/HANDOFF.md)、[任务裁决](design/sportmaster-task-redesign-01.md)、[任务层级](work-packages/WP-IPC-TASK-CONTENT-HIERARCHY-02.md)、[字体](design/typography-01.md) |
| 当前可靠性与输入责任 | [可靠性02](runtime/code-reliability-hardening-02.md)、[长稳运行](runtime/win11-24x7-stability-remediation-01.md) |
| 历史维护 | [输入分页/完整预算](runtime/retention-ingress-02.md)、[完整维护最新证据](runtime/validation/retention-ingress-full-retest-03.md) |
| 构建、部署与版本 | [运行入门](runtime/getting-started.md)、[Windows交付](runtime/windows-application-delivery.md)、[版本兼容](runtime/version-compatibility-01.md)、[D盘环境](runtime/d-drive-storage-20261021.md) |
| 真实接口边界 | [RCS](runtime/sportmaster-rcs-task.md)、[心跳](runtime/rcs-heartbeat-01.md)、[条码生产者](work-packages/WP-IPC-BARCODE-PRODUCER-01.md)、[MVS诊断范围](runtime/hikrobot-reader-01.md) |
| 外部决定与公开范围 | [D决定](readiness/decision-pack.md)、[RV状态](reviews/STATUS.md)、[公开范围](baseline/PUBLICATION.md) |

参数V3替代V2参数版式，作业/首配合同仍继承V2及后续用户纠偏。管理能力是管理模式，操作员身份独立；帮助与支持页面已删除。仓ID、生产线ID、统一供包台编号、7天新站默认、本机窗口偏好及相机/RCS独立延后按当前纠偏执行。配置保存不等于生效；待配置拒绝业务，Unknown不重发。

## 历史证据与发布

以下材料保留形成时事实，不能作为当前实现入口或将结果转记到新版本：

- [清理前完整索引与README原字节](archive/pre-integration-20261026/README.md)，其中保存全部既有发布、设计与回执链接。
- [SM1.11.1重测介质](runtime/clean-install-bootstrap-01.md)、[SM1.11.0管理模式验证](runtime/validation/management-mode-01.md)、[SM1.10.2发布](runtime/publication-pr82-pr86-20261022.md)、[SM1.6.1发布](runtime/publication-login-session-20261020.md)。这些均为历史介质，不代表最新main。
- [旧软件管理员/日志实现](runtime/operator-administrator-logs-01.md)、[V1](ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)、[v1.1](ui/ss-ui-vis-001-v1.1/README.md)保留原编号、原件与验收范围。
- [原长稳独立审查](reviews/win11-24x7-stability-audit-01/README.md)、[原维护失败](runtime/validation/retention-maintenance-qualification-01.md)、[原查询性能复核](runtime/validation/retention-performance-review-01.md)、[PR104精确源验证](runtime/validation/pr104-main-integration-01.md)。原FAIL不因后续修复删除。

真实SDK/RCS、目标工控机、系统安装生命周期、24h/72h和现场分别验收。本轮不生成发布介质、不宣布验收通过。

P0基座已合入main：[运行说明](runtime/p0-stability-01.md)、[历史问题卡](runtime/validation/p0-stability-01.md)、[Unicode续修](runtime/validation/pr110-unicode-shortcuts-01.md)。原问题卡不作为本轮合并验证。

- [2026-10-26整合执行记录](runtime/integration-20261026.md)：本轮真实执行与输入哈希，持续更新。

- [Unicode快捷方式生命周期](runtime/unicode-shortcut-compatibility-01.md)：继承PR110组件身份/监督预算修复，补齐一次释放及路径冲突；[原独立证据](runtime/validation/unicode-shortcut-compatibility-01.md)保持原源。

- [时间与存储故障分级](runtime/time-storage-fault-01.md)：单调节拍、原请求回拨证据、本地事务分级及恢复先暂停；[UTC清单](runtime/time-storage-fault-01-utc-inventory.md)和[原独立证据](runtime/validation/time-storage-fault-01.md)各守原身份。

- [卡顿、容量及WAL维护](runtime/latency-capacity-01.md)：共享投影、后台详情、可取消备份与分级预警；[原测量与反向样本](runtime/validation/latency-capacity-01.md)不转记组合PASS。

生命周期当前入口：[操作指南](runtime/lifecycle-ux-current-guidance-01.md)、[七包实现](runtime/lifecycle-ux-implementation-01.md)、[工作包](work-packages/WP-IPC-LIFECYCLE-UX-OPTIMIZATION-01.md)。原独立验证见[历史报告](runtime/validation/lifecycle-ux-optimization-01.md)及[批量预检历史](runtime/validation/lifecycle-batch-preflight-01.md)，不转记组合源。

原本地稳定性补修当前承接：[F01～F03工作包](work-packages/WP-IPC-STABILITY-AUDIT-FIXES-02.md)、[设计](design/stability-audit-fixes-02.md)、[当前源与验证](runtime/stability-audit-integration-20261026.md)。原未公开回执保持本地历史身份。
