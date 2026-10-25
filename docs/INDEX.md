> 当前代码级可靠性深化：[CODE-RELIABILITY-HARDENING-02 风险、证据与必要补测](work-packages/WP-IPC-CODE-RELIABILITY-HARDENING-02.md)、[现行增量设计](design/code-reliability-hardening-02.md)、[运行与升级](runtime/code-reliability-hardening-02.md)、[验证与原始证据](runtime/validation/code-reliability-hardening-02.md)。原开工main为`1dfb676a`，2026-10-25与已合并PR103/main `c425435`的[同步、证据保全及最终集成检查](runtime/validation/pr104-main-integration-01.md)另列；进程强杀、并发、日志、争用与时间边界单独计证，不关闭下述大规模维护FAIL或扩张现场资格。

> 本轮并行成果整合：[工作包与固定来源](work-packages/WP-IPC-PARALLEL-TASK-INTEGRATION-01.md)、[处置、冲突及验证报告](runtime/parallel-task-integration-01.md)。起始远端main `016976c6`已包含PR97/98；旧分支与历史证据身份保留，稳定性资格另计。

> 前阶段Windows 11长期稳定性整改与现场资格：[工作包与风险矩阵](work-packages/WP-IPC-WIN11-24X7-STABILITY-REMEDIATION-01.md)、[输入、故障与运行设计](design/win11-24x7-stability-remediation-01.md)。原[独立审查及证据](reviews/win11-24x7-stability-audit-01/README.md)固定main `c91559f6`，保留历史身份；整改实施与24h/真实设备资格分别记录。当前[运行与到期删除](runtime/win11-24x7-stability-remediation-01.md)、[R01～R12验证及边界](runtime/validation/win11-24x7-stability-remediation-01.md)。

> TYPOGRAPHY-01追加复核：[三组原生对照、选择依据、实际字体来源与续修](runtime/evidence/typography-01/controlled-review/README.md)。继承PR97 `a43ecb8`；旧截图/测试身份保留。

> 当前字体与排版：[TYPOGRAPHY-01 工作包](work-packages/WP-IPC-TYPOGRAPHY-01.md)、[唯一现行规范](design/typography-01.md)、[实现/复现/边界](runtime/typography-01.md)、[原生前后对照与证据](runtime/evidence/typography-01/README.md)。PR97已合并为 `c310705`，PR98已合并为 `016976c`；依赖已在main。[受控对照与验收](design/typography-comparison-01.md)。保留格口优先、空态、回执、参数帮助与查询空间；旧证据仍对应其原SourceCommit。

> 当前默认供包台、参数与查询呈现：[TASK-CONTENT-HIERARCHY-02](work-packages/WP-IPC-TASK-CONTENT-HIERARCHY-02.md)。PR97已以`c310705`合入main；[深度复核证据表](runtime/evidence/task-content-hierarchy-02/deep-review/README.md)保留其真实Application/原回包链、无效输入指引与原生键鼠身份。01及02上一轮的原图/CI不代表后续整合版本、现场或使用者验收。
> 2026-10-24 Windows 11 24×7独立审查：[报告、原始证据与复核](reviews/win11-24x7-stability-audit-01/README.md)、[下一阶段Codex修复提示](reviews/win11-24x7-stability-audit-01/Codex-next-phase.md)。原审查固定main `c91559f6`；本次仅发布审查产物，不代表问题已修复或24小时/实机验收通过。

> PR94/95整合后编译与托管验证：[CI续修记录](runtime/validation/task-context-ci-fix-01.md)。原140项本机UI结果保留原身份；整合后的Linux/Windows及介质身份独立核对。

> 当前双相机通信修复：[工作包](work-packages/WP-IPC-DUAL-CAMERA-COMMUNICATION-FIX-01.md)、[运行与现场只读诊断](runtime/dual-camera-communication-fix-01.md)、[PR93首次配置CI续修](runtime/validation/dual-camera-first-configuration-ci-01.md)。本包替代TCP启动和故障分类操作，历史协议、版本及验收证据保留身份。

> 当前质量整改：[AQA01-F01～F06工作包](work-packages/WP-IPC-ARCHITECTURE-QUALITY-REMEDIATION-01.md)。[PR92独立复核、边界与CI](reviews/pr92-independent-quality-review.md)、[实现与命令生命周期](runtime/architecture-quality-remediation-01.md)、[验证记录](runtime/validation/architecture-quality-remediation-01.md)。原审查基线0c5d7d2、旧版本及原验收保持身份；相关过期当前入口原字节见[归档](archive/pre-architecture-quality-remediation-01/README.md)。

> 当前经典窗口与参数文案增量：[PARAMETER-SHELL-POLISH-02](work-packages/WP-IPC-PARAMETER-SHELL-POLISH-02.md)。PR88 已合并；不重建管理身份或参数架构。

> 2026-10-23 内部页面与逐页校验续做见[本轮工作包](work-packages/WP-IPC-INTERNAL-PAGES-UI-REFINE-01.md)。替代上轮按整类继续及普通错误中的变量名呈现，查询快照与业务/权限责任保持。

> 2026-10-23参数/首次配置现已授权推送与PR合并；范围、原证据与不测试边界见[本轮发布记录](runtime/publication-parameter-ui-20261023.md)。原稿和4个未跟踪附件继续保留本地。

﻿> 2026-10-23 当前参数与首配细化：[实施及源码映射](runtime/parameter-ui-refine-01.md)、[原生验收/截图及边界](runtime/validation/parameter-ui-refine-01.md)。本地同源测试包不推送、不发布、不自动安装；下列历史安装包和清理结论保持原时点。

> 当前重测安装包为SM1.11.1：[清理后首次安装修复](runtime/clean-install-bootstrap-01.md)、[实际EXE及原生证据](runtime/evidence/clean-install-bootstrap-01/README.md)。修复保存D盘位置但目录不存在时启动报错；已验证到确认页并取消，尚未正式安装。下述SM1.11.0交付与清理保持历史时点。
>
> 当前管理模式交付SM1.11.0：[PR88](https://github.com/yuan2go/importer-pc/pull/88)、[验证和截图](runtime/validation/management-mode-01.md)。最终包0204fcf曾实际安装并核验；其后按用户要求已卸载并清空旧运行状态，保留海康及D盘存储位置，等待重新测试安装。[当前清理记录](runtime/evidence/management-mode-01/clean-reset-20261022/README.md)、[此前安装证据](runtime/evidence/management-mode-01/installed-d-20261022/README.md) 各守时间范围。
>
> 历史已发布SM1.10.2（PR82～86已合并）：[单EXE下载](https://github.com/yuan2go/importer-pc/releases/download/installer-sm-v1.10.2-c9e482a30d2d/CainiaoSupplyStation_SM_Setup_v1.10.2_win-x64.exe)，冻结源码`c9e482a30d2d`；[前版不测试合并、完整下载/哈希和边界](runtime/publication-pr82-pr86-20261022.md)。原安装/模拟/CI证据各守原来源，安装器未签名。

> 当前安装与首配入口 UI 实施：[INSTALLER-FIRST-RUN-UI-02](work-packages/WP-IPC-INSTALLER-FIRST-RUN-UI-02.md)、[UI规范与预览](ui/installer-first-run-ui-02/README.md)。后续已按用户要求降低密码最低长度至5个字符（允许纯数字）并隐藏安装使用范围，见[增量原生证据](runtime/evidence/installer-first-run-ui-02/followup-password-scope/README.md)。本包替代冲突的旧安装布局与授权提示；下述介质及验证保留各自来源，不代表新 UI 已验收。

# 文档索引 · 作业、管理模式与日志

2026-10-25获授权续做：[PR105输入分页与完整预算整合/受保护重验](runtime/validation/retention-ingress-full-retest-03.md)。继承PR104/main `404e8552`及既有PR105，原FAIL/NOT_RUN身份保留，新结果按实际回执登记。

2026-10-25 当前历史维护整改：[RETENTION-INGRESS-02](work-packages/WP-IPC-RETENTION-INGRESS-02.md)、[实现/预算与复现](runtime/retention-ingress-02.md)、[实际计划、规模与故障恢复结果](runtime/validation/retention-ingress-02.md)。基于 PR103 合并后的 main 修复新增输入分页与完整命令预算，继承 PR102；下述历史失败及候选组件证据保留原身份，新结果独立记录。大规模受保护CLI启动UAC取消，未取得完整规模维护或长期运行资格。

2026-10-24～25长稳维护补证：[本轮工作包](work-packages/WP-IPC-RETENTION-QUALIFICATION-01.md)、[完整原件规模/受保护CLI结果和剩余缺口](runtime/validation/retention-maintenance-qualification-01.md)、[210份文本原件/hash](runtime/evidence/retention-maintenance-qualification-01/FILES.json)。固定运行产品`5bb032d`并继承main/PR102；1千/1万完整维护和隔离恢复通过，60.48万清理超预算FAIL，新增输入分页热点未改业务代码；259.2万未运行，60.48万恢复UAC取消。原查询微基准、CI和失败各守原身份，不替代目标工控机、真实设备或24h/72h验收。

2026-10-24历史清理查询独立复核：[报告和验证边界](runtime/validation/retention-performance-review-01.md)、[原件/hash与仓库复现](runtime/evidence/retention-performance-review-01/README.md)。固定复核main `12a7cbd`；PR100已包含性能修复。本次仅归档，不重改业务代码，不将短测、CI或安装器构建扩张为24h/72h或现场验收。

同日RECHECK-02从main `5bb032d`再次配对到百万审计，并保留首次升级夹具竞态失败及实际回执修复；详见上述报告的再核查段和[新证据清单](runtime/evidence/retention-performance-review-01/recheck-02/FILES.json)。原归档结论保持形成时身份，未重复修改生产清理实现。

当前参数呈现增量：[PARAMETER-UI-REFINE-01](work-packages/WP-IPC-PARAMETER-UI-REFINE-01.md)，替代冲突的首配重复导航、表单/错误和管理入口布局；四类归属、真实字段与保存/授权守卫继承V3。安装与设密修复继续保留。

安装/首配后续实机问题：[窗口比例与首次设密批准修复](runtime/installer-first-run-ui-02-bootstrap-layout.md)。替代此前窗口默认尺寸及首次设密批准调用；原证据不扩张为实际UAC引导验收。

安装与首配UI02新增第9页当前定义见[结果待确认增量](ui/installer-first-run-ui-02/result-unconfirmed-20261022/README.md)，替代未知结果的红色失败呈现；原八页输入、介质及验收保持历史身份。此前五位密码与隐藏使用范围的用户纠偏继续有效。

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

本轮管理模式以最新main `0bf86a0a588b75b95aa1963e8fe88dad976ca803` 开工，PR82～87均已合并。[PR88](https://github.com/yuan2go/importer-pc/pull/88) 提交管理模式纠偏；SM1.11.0最终本地介质固定源码 `0204fcf2fdc8b44d5a4d16b6c5080fa1925cab84`，截图、哈希、验证及本机安装后再清空的时点见[独立证据](runtime/evidence/management-mode-01/README.md)。旧OPS包的 `3b883fe` 开工状态及分支整合身份只适用于前版形成阶段。

[V3设计原件](ui/parameter-settings-v3-20261020/README.md)、[V2设计原件](ui/sportmaster-task-redesign-v2-20261020/README.md)、[V1](ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)与[v1.1](ui/ss-ui-vis-001-v1.1/README.md)保留原字节、数量和历史适用范围。旧支持页、旧导航和原型模拟不是本轮保留要求或生产能力。完整历史入口及发布链接见[本轮清理前索引](archive/pre-operator-admin-logs/docs__INDEX.md.txt)，旧介质始终保留原SourceCommit。

真实SDK/RCS、实体许可、目标工控机、完整安装生命周期和现场分别验收，不以原型、本机合成测试、源码合并或旧介质取代。维护和显示切换不重建业务实例，保存不等于生效，Unknown不重发，软件暂停不是安全急停。
