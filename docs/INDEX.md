# 统一资料与实施入口

## 本轮有效入口

[UX审查WP-01～07工程闭环](work-packages/WP-IPC-UX-COMMERCIAL-CLOSURE-02.md)为当前执行入口，[结果/身份/剩余](runtime/ux-commercial-closure-02-results.md)、[可取得参考候选](https://github.com/yuan2go/importer-pc/releases/tag/ux-reference-f1a655c)。开发分支显式依赖PR37固定2d60fdc，远端PR37未合并；main 1.2.8、PR37本地1.3.0和本轮候选分别记录。原审查及下列审查03记录按形成时身份保留。

## 最新独立审查与实施交接

[UX商用质量审查01原PDF与发布时复核](reviews/ux-commercial-quality-audit-01/README.md) · [下一阶段Codex提示词](reviews/ux-commercial-quality-audit-01/Codex-next-phase.md)。原报告固定main为`8f886f44bec01779318ec078a569500ab1beab56`；发布时开放PR37已有UX整改，按实际行为继承并补差额，不能将其本地1.3.0候选当作main已交付。原报告发布仅归档交接；本轮工程结果另记，原结论不倒写。

[审查03不测试发布与合并记录](runtime/publication-audit03-20261019.md) · [PR34原整改报告](runtime/commercial-quality-fix-03-results.md)。审查03历史实现为PR35的1.2.8/ABI1.8/DB7，原报告中的OPEN及旧候选按形成时身份解释。

[审查03工程交付结果](runtime/commercial-audit03-delivery-results.md) · [PR #35](https://github.com/yuan2go/importer-pc/pull/35) · [最终产物hash与证据](runtime/evidence/commercial-audit03-delivery/corrected-565abcc/ARTIFACTS.json)。00/01/02及03源码、05媒介/维护和06参考故障路径已落实；真实组合、Windows原生与目标负载/现场门禁按逐包剩余读取。

[商业质量审查03原报告与证据](reviews/commercial-quality-audit-03/README.md) · [下一阶段Codex提示词](reviews/commercial-quality-audit-03/Codex-next-phase.md)。固定审查main为ec698bd；PR34的形成时覆盖与剩余按原发布交接读取；本轮合并不倒写原审查结论。审查03形成时实施入口为[审查03交付工作包](work-packages/WP-IPC-COMMERCIAL-AUDIT-03-DELIVERY.md)，其中列明00～06的执行依赖、支持组合及待确认输入。原审查与报告发布按形成时身份保留。

## 当前有效阅读顺序

1. [AGENTS](../AGENTS.md)、[本轮UX闭环工作包](work-packages/WP-IPC-UX-COMMERCIAL-CLOSURE-02.md)，继承PR37的[原工作包](work-packages/WP-IPC-UX-COMMERCIAL-FIX-01.md)/[原实施计划](superpowers/plans/2026-10-19-ux-commercial-fix-01.md)及[审查03交付工作包](work-packages/WP-IPC-COMMERCIAL-AUDIT-03-DELIVERY.md)；既有组合、来源PR及历史验证见[整合报告](runtime/parallel-task-reconciliation-01.md)。旧任务的合并授权只适用于其原PR和固定head。
2. [框架](design/framework.md)、[F决策](design/framework-decisions.md)、[受控装配/扩展](design/controlled-extensions.md)、[19职责](design/application-capability-19.md)、[20可靠性](design/capability-reliability-20.md)、[22拓扑](design/application-architecture-22.md)和[质量/恢复设计](design/architecture-quality-closure-01.md)。仍是三个独立Application，无通用主流程、第二配置账或第二扩展设施。
3. [首次运行](runtime/getting-started.md)、[Windows交付与故障SOP](runtime/windows-application-delivery.md)、[Application配置](runtime/application-architecture-22.md)、[恢复/容量/扩展维护](runtime/architecture-quality-closure-01.md)、[版本与组合证据](runtime/version-compatibility-01.md)、[扩展开发安装恢复](extensions/developer-guide.md)。本分支业务1.3.1、Contracts1.9.0、Parcel DB7；schema1/2保留main兼容位，schema3显式双位。Supply Storage5仅按[Supply运行维护](runtime/operations.md)操作。
4. [RCS设计](design/sportmaster-rcs-task.md)/[运行](runtime/sportmaster-rcs-task.md)、[Sportmaster可选UI](runtime/sportmaster-alternate-ui.md)、[永久恢复P2及原失败](runtime/parcel-recovery-p2.md)。原请求/版本/目标/合同/raw不因新部署改变，Unknown不自动重发。
5. [D决策](readiness/decision-pack.md)、[技术基线](readiness/technical-baseline.json)、[RV状态](reviews/STATUS.md)/[原问题](reviews/issues.json)、[公开范围](baseline/PUBLICATION.md)、[缺失](baseline/MISSING.md)/[冲突](baseline/CONFLICTS.md)和[manifest](manifest.json)。客户全量资料仍PUBLIC_SUBSET_ONLY，原审查01 PDF未取得。

## 实物UI与验收

[v1.1入口](ui/ss-ui-vis-001-v1.1/README.md) · [34张图册](ui/ss-ui-vis-001-v1.1/GALLERY.md) · [原HTML](ui/ss-ui-vis-001-v1.1/供包工作站_高保真交互原型.html) · [完整交接](ui/ss-ui-vis-001-v1.1/docs/供包工作站_UIUX设计与开发交接说明.md) · [UI状态](ui/STATUS.md)。原HTML为模拟设计，PNG是同源导出，不是WPF运行图；不得据原型推导客户规则。

[新UI Windows后验](runtime/validation/sportmaster-alternate-ui-windows.md) · [质量Windows后验](runtime/validation/architecture-quality-closure-01-windows.md) · [Windows06](runtime/windows-06.md)/[工作包](work-packages/WP-IPC-WINDOWS-VALIDATION-06.md) · [真实集成05](runtime/integration-05.md)/[工作包](work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md)。Windows实机NOT_RUN；真实SDK/协议/设备/现场BLOCKED，参考测试不授予Production。

## 来源任务、原审查与历史证据

|来源|保留入口及适用范围|
|---|---|
|审查03 / WP-A03-00～06|[31页原PDF、67文件证据包及发布时复核](reviews/commercial-quality-audit-03/README.md)，固定ec698bd；与PR34的CQ03编号逐项映射，原审查只读结论不倒写。|
|本轮全部任务|[起始清单](runtime/evidence/parallel-task-reconciliation-01/start-inventory.json)、[整合报告](runtime/parallel-task-reconciliation-01.md)、[PR29原复审](reviews/parallel-task-reconciliation-01/pr29-original-review.md)、[PR32原审](reviews/parallel-task-reconciliation-01/pr32-original-review.md)、[PR27原审](reviews/pr27-ui-independent-review.md)|
|PR29版本|[设计](design/version-compatibility-01.md)、[原结果](runtime/version-compatibility-01-results.md)、[原P1/P2修复](runtime/version-compatibility-01-rereview-results.md)，候选/原件按原SHA|
|PR30 RCS|[工作包](work-packages/WP-IPC-SPORTMASTER-RCS-TASK-01.md)、[原证据](runtime/evidence/sportmaster-rcs-task/README.md)|
|PR27 UI|[工作包](work-packages/WP-IPC-SPORTMASTER-ALTERNATE-UI.md)、[原结果及候选](runtime/sportmaster-alternate-ui-results.md)|
|PR31/32恢复|[PR31原审](reviews/pr31-independent-rereview-f068156/README.md)、[31工作包](work-packages/WP-IPC-PR26-RECOVERY-CLOSURE.md)、[32结果/证据](runtime/parcel-recovery-p2.md)|
|审查02 N01–N07 / PR26|[原PDF及复核](reviews/architecture-quality-audit-02/README.md)、[实施结果](runtime/architecture-quality-audit-02-results.md)、[PR26原审](reviews/pr26-independent-78298b5/README.md)|
|质量闭环01 / PR24 / PR23|[设计](design/architecture-quality-closure-01.md)、[PR24原审](reviews/pr24-independent-rereview-f862c2a/README.md)、[R01–R05](runtime/architecture-quality-closure-01-rereview-results.md)、[R03](runtime/architecture-quality-closure-01-r03-results.md)、[采集P2](runtime/acquisition-lifecycle-p2.md)|
|17–22|[17](runtime/application-convergence-17-results.md)、[18](runtime/site-capabilities-18-results.md)、[19](runtime/application-capability-19-results.md)、[20](runtime/capability-reliability-20-results.md)、[备份健康](runtime/backup-health-recovery.md)、[20/21整合](runtime/pr20-21-integration.md)、[22结果](runtime/application-architecture-22-results.md)|
|03–16 / 资料归档|[14](runtime/conformance-fix-14.md)、[15](runtime/operation-closure-15.md)、[16](runtime/ui-maintainability-16.md)、[13](runtime/arch-extension-closure-13.md)、[12](runtime/extension-windows-delivery-12.md)、[11](runtime/review-followup-11.md)、[09](runtime/reference-validation-09.md)、[03](runtime/implementation.md)、[BASELINE-01](work-packages/WP-IPC-DESIGN-BASELINE-01.md)|
|不测试发布|[2026-10-19原发布回执](runtime/publication-20261019.md)，NOT_RUN仅属原任务|

旧OPEN/不合并/当前候选/PASS均按各自形成时间和SHA读取；本轮新证据不倒写旧结论。所有原入口链接与历史细项保存在[整合前原字节](archive/pre-parallel-task-reconciliation-01/README.md)，各阶段pre-*归档继续保有。新资料校验按[工具说明](../tools/README.md)执行，不新增例外掩盖原设计资产失败。
