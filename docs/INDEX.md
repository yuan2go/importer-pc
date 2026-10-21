> 2026-10-22 当前已合并PR82～86：[SM1.10.2单EXE下载](https://github.com/yuan2go/importer-pc/releases/download/installer-sm-v1.10.2-c9e482a30d2d/CainiaoSupplyStation_SM_Setup_v1.10.2_win-x64.exe)，冻结源码`c9e482a30d2d`；[本轮不测试合并、完整下载/哈希和边界](runtime/publication-pr82-pr86-20261022.md)。原安装/模拟/CI证据各守原来源，安装器未签名。

# 文档索引 · 作业、管理模式与日志

当前纠偏实施为[MANAGEMENT-MODE-01](work-packages/WP-IPC-MANAGEMENT-MODE-01.md)：管理能力是防误改的管理模式，不是管理员账号。操作员身份独立，普通模式只使用作业/记录，管理模式开放参数和日志。正常读码自动取格口，帮助支持保持删除。旧1.10.2发行链接及证据为历史已交付版本，不代表模式增量已发布。

| 阅读目的 | 当前入口 |
|---|---|
| 当前模式语义与权限边界 | [管理模式工作包](work-packages/WP-IPC-MANAGEMENT-MODE-01.md)、[操作与实现](runtime/management-mode-01.md) |
| 1.10.2历史账号/日志实现及纠偏替代关系 | [前版实现](runtime/operator-administrator-logs-01.md) |
| 管理模式验证、截图及未验边界 | [本轮记录](runtime/validation/management-mode-01.md)；[前版1.10.2](runtime/validation/operator-administrator-logs-01.md) |
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
