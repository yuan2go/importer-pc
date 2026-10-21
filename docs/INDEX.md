# 文档索引 · 现场作业、软件管理员与日志

当前实施为 [OPS-ADMIN-LOGS-01](work-packages/WP-IPC-OPERATOR-ADMIN-LOGS-01.md)。普通操作员只使用作业和作业记录；软件管理员增加参数设置、日志查询，并按需诊断恢复。正常读码后自动取得目标格口，无逐包点击。帮助与支持功能已从产品范围删除。

| 阅读目的 | 当前入口 |
|---|---|
| 本轮功能取舍、合并来源与权限边界 | [统一工作包](work-packages/WP-IPC-OPERATOR-ADMIN-LOGS-01.md) |
| 账号、参数权限、日志与部署兼容 | [当前操作与实现](runtime/operator-administrator-logs-01.md) |
| 本轮验证、截图及未验边界 | [同版交付记录](runtime/validation/operator-administrator-logs-01.md) |
| 本机全部项目文件、安装与数据的D盘迁移 | [D盘存储与运行](runtime/d-drive-storage-20261021.md) |
| 框架与业务所有权 | [框架](design/framework.md)、[决策](design/framework-decisions.md)、[独立Application](design/application-architecture-22.md) |
| 参数字段、完整候选及唯一账 | [参数继承](design/parameter-inheritance-02.md)、[93项迁移](design/parameter-legacy-migration-02.csv)、[PARAM-UX-02](design/parameter-settings-experience-02.md)、[V3基线](ui/parameter-settings-v3-20261020/README.md) |
| 作业与首配合同 | [V2交接](ui/sportmaster-task-redesign-v2-20261020/HANDOFF.md)、[任务裁决](design/sportmaster-task-redesign-01.md)、[首配/TCP增量](runtime/startup-experience-03.md)；导航和权限由本轮替代 |
| 实际构建与安装 | [运行入门](runtime/getting-started.md)、[Windows交付](runtime/windows-application-delivery.md)、[版本兼容](runtime/version-compatibility-01.md) |
| 真实接口边界 | [RCS](runtime/sportmaster-rcs-task.md)、[心跳](runtime/rcs-heartbeat-01.md)、[海康Reader](runtime/hikrobot-reader-01.md) |
| 外部批准与历史评审 | [D决定](readiness/decision-pack.md)、[RV状态](reviews/STATUS.md)、[公开范围](baseline/PUBLICATION.md) |

开工 main 为 `3b883fe`。PR82～85均未合并；本分支整合85 `d52ade7`与84 `587fbe8`，继承适用的参数、中文、首配、TCP和作业信息成果。组合源码通过本轮验证前不继承它们的PASS；main和并行PR实际状态以交付记录为准。

[V3设计原件](ui/parameter-settings-v3-20261020/README.md)、[V2设计原件](ui/sportmaster-task-redesign-v2-20261020/README.md)、[V1](ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)与[v1.1](ui/ss-ui-vis-001-v1.1/README.md)保留原字节、数量和历史适用范围。旧支持页、旧导航和原型模拟不是本轮保留要求或生产能力。完整历史入口及发布链接见[本轮清理前索引](archive/pre-operator-admin-logs/docs__INDEX.md.txt)，旧介质始终保留原SourceCommit。

真实SDK/RCS、实体许可、目标工控机、完整安装生命周期和现场分别验收，不以原型、本机合成测试、源码合并或旧介质取代。维护和显示切换不重建业务实例，保存不等于生效，Unknown不重发，软件暂停不是安全急停。
