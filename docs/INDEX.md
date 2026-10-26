# 文档索引

本索引区分当前规则、进行中的整合和历史证据。2026-10-26 起点 main 为 `a8cd4314`；最新整合状态只在[本轮工作包](work-packages/WP-IPC-INTEGRATION-20261026.md)记录，旧 PASS 不随合并迁移。

## 当前规则与操作

| 阅读目的 | 当前入口 |
|---|---|
| 本轮意图台账、时间线与执行顺序 | [INTEGRATION-20261026](work-packages/WP-IPC-INTEGRATION-20261026.md) |
| 架构与职责 | [框架](design/framework.md)、[F决策](design/framework-decisions.md)、[独立Application](design/application-architecture-22.md) |
| 管理模式与操作员权限 | [管理模式](runtime/management-mode-01.md)、[工作包](work-packages/WP-IPC-MANAGEMENT-MODE-01.md) |
| 参数与首次启动 | [用户纠偏](work-packages/WP-IPC-PARAMETER-GUIDANCE-STARTUP-01.md)、[延后映射/恢复](runtime/validation/deferred-heartbeat-01.md)、[参数共模](design/parameter-refactor-01.md) |
| 参数设计 | [PARAM-UX-02](design/parameter-settings-experience-02.md)、[参数V3](ui/parameter-settings-v3-20261020/README.md)、[字段继承](design/parameter-inheritance-02.md) |
| 作业与首次启动设计 | [V2交接](ui/sportmaster-task-redesign-v2-20261020/HANDOFF.md)、[任务裁决](design/sportmaster-task-redesign-01.md)、[任务层级](work-packages/WP-IPC-TASK-CONTENT-HIERARCHY-02.md)、[字体](design/typography-01.md) |
| 当前可靠性与输入责任 | [可靠性02](runtime/code-reliability-hardening-02.md)、[长稳运行](runtime/win11-24x7-stability-remediation-01.md) |
| 历史维护 | [输入分页/完整预算](runtime/retention-ingress-02.md)、[完整维护最新证据](runtime/validation/retention-ingress-full-retest-03.md) |
| 构建、部署与版本 | [运行入门](runtime/getting-started.md)、[Windows交付](runtime/windows-application-delivery.md)、[版本兼容](runtime/version-compatibility-01.md)、[D盘环境](runtime/d-drive-storage-20261021.md) |
| 真实接口边界 | [RCS](runtime/sportmaster-rcs-task.md)、[心跳](runtime/rcs-heartbeat-01.md)、[Reader](runtime/hikrobot-reader-01.md)、[双相机](runtime/dual-camera-communication-fix-01.md) |
| 外部决定与公开范围 | [D决定](readiness/decision-pack.md)、[RV状态](reviews/STATUS.md)、[公开范围](baseline/PUBLICATION.md) |

参数V3替代V2参数版式，作业/首配合同仍继承V2及后续用户纠偏。管理能力是管理模式，操作员身份独立；帮助与支持页面已删除。仓ID、生产线ID、统一供包台编号、7天新站默认、本机窗口偏好及相机/RCS独立延后按当前纠偏执行。配置保存不等于生效；待配置拒绝业务，Unknown不重发。

## 本轮待整合来源

[PR108 生命周期七包](https://github.com/yuan2go/importer-pc/pull/108)、[PR110 P0](https://github.com/yuan2go/importer-pc/pull/110)、[PR111 卡顿容量](https://github.com/yuan2go/importer-pc/pull/111)、[PR112 时间存储](https://github.com/yuan2go/importer-pc/pull/112)、[PR113 快捷方式](https://github.com/yuan2go/importer-pc/pull/113)。状态以本轮工作包和远端PR为准，不将分支实现称作已在main生效。

## 历史证据与发布

以下材料保留形成时事实，不能作为当前实现入口或将结果转记到新版本：

- [清理前完整索引与README原字节](archive/pre-integration-20261026/README.md)，其中保存全部既有发布、设计与回执链接。
- [SM1.11.1重测介质](runtime/clean-install-bootstrap-01.md)、[SM1.11.0管理模式验证](runtime/validation/management-mode-01.md)、[SM1.10.2发布](runtime/publication-pr82-pr86-20261022.md)、[SM1.6.1发布](runtime/publication-login-session-20261020.md)。这些均为历史介质，不代表最新main。
- [旧软件管理员/日志实现](runtime/operator-administrator-logs-01.md)、[V1](ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)、[v1.1](ui/ss-ui-vis-001-v1.1/README.md)保留原编号、原件与验收范围。
- [原长稳独立审查](reviews/win11-24x7-stability-audit-01/README.md)、[原维护失败](runtime/validation/retention-maintenance-qualification-01.md)、[原查询性能复核](runtime/validation/retention-performance-review-01.md)、[PR104精确源验证](runtime/validation/pr104-main-integration-01.md)。原FAIL不因后续修复删除。

真实SDK/RCS、目标工控机、系统安装生命周期、24h/72h和现场分别验收。本轮不生成发布介质、不宣布验收通过。

P0基座正在同步本轮文档清理：[运行说明](runtime/p0-stability-01.md)、[历史问题卡](runtime/validation/p0-stability-01.md)、[Unicode续修](runtime/validation/pr110-unicode-shortcuts-01.md)。原问题卡不作为本轮合并验证。

- [2026-10-26整合执行记录](runtime/integration-20261026.md)：本轮真实执行与输入哈希，持续更新。

- [Unicode快捷方式生命周期](runtime/unicode-shortcut-compatibility-01.md)：继承PR110组件身份/监督预算修复，补齐一次释放及路径冲突；[原独立证据](runtime/validation/unicode-shortcut-compatibility-01.md)保持原源。
