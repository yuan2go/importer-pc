> [2026-10-19不测试发布与合并回执](runtime/publication-20261019.md)：PR26已合并，8个既有参考产物已上传；Windows/真实集成边界不变。

# 统一资料与实施入口

## 当前工程入口

[首次运行及产品选择](runtime/getting-started.md) · [Application设计](design/application-architecture-22.md) · [Application运行/配置](runtime/application-architecture-22.md) · [恢复/容量/扩展维护](runtime/architecture-quality-closure-01.md) · [扩展开发](extensions/developer-guide.md)。当前main `baac2602` 已继承PR23/24/26；历史N01–N07的源码和证据按原结果读取。新Application使用schema3/Parcel DB5，DB3/4为历史发布；Supply单独适用。恢复P2及custody并发修复见[结果与证据](runtime/parcel-recovery-p2.md)（[先行计划](superpowers/plans/2026-10-19-parcel-recovery-p2.md)），[前版本原字节](archive/pre-parcel-recovery-p2/README.md)保留形成时结论。Windows实际NOT_RUN，真实设备/协议/现场BLOCKED。

[N01–N07交付、PR26复核修复及验证边界](runtime/architecture-quality-audit-02-results.md)。缺raw资料责任、旧发布Query到DB5、旧配置账确认和维护围栏的实际步骤见维护指南；[复核前原字节](archive/pre-pr26-rereview/README.md)保留前次结论，前次PASS不作为全闭环证据。

[N01前操作指引原字节](archive/pre-architecture-quality-02/README.md)。19职责、20可靠性、22拓扑及[质量闭环设计](design/architecture-quality-closure-01.md)按适用范围共同生效；阶段工作包和原候选作为历史来源保留。
PR24已由独立授权合并：归档前复审head `f862c2ae2ad00ab79ceff5e2013c2a5593d05234`，归档后最终head `f531d3a6882891cfc7870f5dcccfe985009d6be4`，main合并提交 `4afe2afacde9e2acab7648fff3440fa3cc331b6e`。[PR24独立原报告与证据](reviews/pr24-independent-rereview-f862c2a/README.md)、[五项整改](runtime/architecture-quality-closure-01-rereview-results.md)及[R03去重前校验](runtime/architecture-quality-closure-01-r03-results.md)保留各自固定源码与未验边界；[R03前入口原字节](archive/pre-pr24-r03-20261018/README.md)与[复审前入口原字节](archive/pre-pr24-rereview-20261018/README.md)保持原身份。采集P2见[工作包](work-packages/WP-IPC-ACQUISITION-LIFECYCLE-P2.md)及[结果](runtime/acquisition-lifecycle-p2.md)，其成果随PR24祖先关系继承；旧OPEN与不合并说明按形成时范围理解。

## 最新独立审查与下一阶段交接

[架构质量审查02：原PDF、最新复核及精简Codex提示词](reviews/architecture-quality-audit-02/README.md)。固定审查main为`2126ac2c06a0c352d96afbbf13ec1a719c352738`；报告保持形成时身份，下一阶段N01–N07按执行时main与并行PR复核后实施。该报告发布任务未实施修复；质量闭环01的实际增量按本包结果和固定源码读取，不据此改写原审查结论或历史验收。

## 继承的整合与历史证据

历史整合入口：[PR20/21整合记录](runtime/pr20-21-integration.md)。开工main `08286542`（PR19已合并）；最终运行源码`d5c8b79`，三项评审修复、交叉恢复整合及定向参考验证完成。PR状态和main合并身份读取GitHub及整合回执；旧报告的OPEN与候选身份按形成时读取。

现行增量共同生效：[能力可靠性20设计](design/capability-reliability-20.md)、[20工作包](work-packages/WP-IPC-CAPABILITY-RELIABILITY-CLOSURE-20.md)/[运行](runtime/capability-reliability-20.md)/[历史结果](runtime/capability-reliability-20-results.md)，以及[备份健康工作包](work-packages/WP-IPC-BACKUP-HEALTH-RECOVERY.md)/[运行及历史结果](runtime/backup-health-recovery.md)。它们补充[19职责设计](design/application-capability-19.md)，不能互相替代。[19运行](runtime/application-capability-19.md)和旧候选保留原身份。原字节分别见[20前归档](archive/pre-capability-reliability-20/README.md)、[备份修复前归档](archive/pre-backup-health-recovery/README.md)及Git基线。Windows实际NOT_RUN，真实设备/协议/现场BLOCKED。

[17实施](runtime/application-convergence-17.md)、[17结果](runtime/application-convergence-17-results.md)、[18实施](runtime/site-capabilities-18.md)、[18结果](runtime/site-capabilities-18-results.md)按其原SHA和形成时范围读取，不作为本轮完成证据。Supply/Inspection兼容机制、D/RV和公开范围继续有效。Windows实际运行NOT_RUN；真实SDK/协议/现场BLOCKED。

## 代码前必读

1. [AGENTS](../AGENTS.md)、[本包](work-packages/WP-IPC-ARCHITECTURE-QUALITY-CLOSURE-01.md)及[整改设计](design/architecture-quality-closure-01.md)、22拓扑及19/20可靠性、最新复核/问题及相关原始证据；从执行时最新main核差异/工作区/并行PR。
2. [框架架构](design/framework.md)、[决策记录](design/framework-decisions.md)、[受控装配与扩展](design/controlled-extensions.md)。职责/事务/身份/生命周期要求继续有效，不能改弱目标消除缺陷。
3. [14任务](work-packages/WP-IPC-CONFORMANCE-FIX-14.md)、[实施](runtime/conformance-fix-14.md)/证据，以及[13任务](work-packages/WP-IPC-ARCH-EXTENSION-CLOSURE-13.md)/[复核](runtime/arch-extension-closure-13-recheck.md)、按影响再读[12任务](work-packages/WP-IPC-EXTENSION-WINDOWS-DELIVERY-12.md)/[交付](runtime/extension-windows-delivery-12.md)。保留后续批准的后装与Windows后验，勿重复实施已闭子项。
4. [Application运行/发布/维护](runtime/architecture-quality-closure-01.md)、[Supply运行维护](runtime/operations.md)、[扩展开发安装恢复](extensions/developer-guide.md)，核实际入口、配置、依赖和源码。
5. [D决策](readiness/decision-pack.md)、[技术基线](readiness/technical-baseline.json)、[RV状态](reviews/STATUS.md)/[原问题](reviews/issues.json)。采用不等于客户批准，局部新PASS不关闭全部旧RV。
6. [公开范围](baseline/PUBLICATION.md)、[资料缺失](baseline/MISSING.md)、[冲突](baseline/CONFLICTS.md)和[manifest](manifest.json)。先保留需改文件原字节，再清理相关过期入口；已正确内容不重复写，未经实现的目标不写成能力。

## UI实物与分层验收

[v1.1入口](ui/ss-ui-vis-001-v1.1/README.md) · [34张状态图](ui/ss-ui-vis-001-v1.1/GALLERY.md) · [原HTML](ui/ss-ui-vis-001-v1.1/供包工作站_高保真交互原型.html) · [完整UI交接](ui/ss-ui-vis-001-v1.1/docs/供包工作站_UIUX设计与开发交接说明.md) · [UI资料状态](ui/STATUS.md)。源文件、PPTX、画板、tokens、分离HTML/CSS/JS及PNG都有实际资料；PNG为同源重新导出，未变成Windows截图。修改页面前实际阅读实物，不照抄原型已知业务缺陷。

[15 UI映射](ui/operation-closure-15-mapping.md) · [15 Windows后验](runtime/validation/operation-closure-15-windows.md) · [13 UI映射](ui/arch-extension-closure-13-mapping.md) · [12 UI映射](ui/extension-delivery-12-mapping.md) · [10 UI实施](runtime/ui-baseline-alignment-10.md) · [Windows06](runtime/windows-06.md) · [06工作包](work-packages/WP-IPC-WINDOWS-VALIDATION-06.md) · [真实集成05](runtime/integration-05.md) · [05工作包](work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md)。旧“当前候选/环境阻塞”只表示形成时状态，15开工前按最新有效要求核对；W01～06和B01～06本身不能作为过期要求删除。

## 历史实施、评审与工作包

|阶段|完整入口|
|---|---|
|11复核跟进|[工作包](work-packages/WP-IPC-REVIEW-FOLLOWUP-11.md)、[报告](runtime/review-followup-11.md)、[checks](runtime/review-followup-11-checks.json)|
|08/09/10纠偏与参考验证|[纠偏工作包](work-packages/WP-IPC-AUDIT-RECONCILIATION-01.md)、[08](work-packages/WP-IPC-ENGINEERING-FOLLOWUP-08.md)、[09](work-packages/WP-IPC-REFERENCE-VALIDATION-09.md)、[10](work-packages/WP-IPC-UI-BASELINE-ALIGNMENT-10.md)、[09报告](runtime/reference-validation-09.md)、[checks](runtime/reference-validation-09-checks.json)|
|07工程闭环|[工作包](work-packages/WP-IPC-ENGINEERING-CLOSURE-07.md)、[报告](runtime/engineering-closure-07.md)|
|03/04及架构UI对齐|[03工作包](work-packages/WP-IPC-RUNTIME-03.md)、[实施](runtime/implementation.md)、[交付](runtime/delivery.md)、[04](work-packages/WP-IPC-RUNTIME-HARDENING-04.md)、[04结果](runtime/hardening-04.md)、[对齐包](work-packages/WP-IPC-ARCH-UI-ALIGNMENT-01.md)、[结果](runtime/alignment-01.md)|
|旧独立复核|[02](reviews/main-static-review-02.md)、[03](reviews/main-static-review-03.md)、[03 checks](reviews/main-static-review-03-checks.json)|
|资料与准备阶段|[BASELINE-01](work-packages/WP-IPC-DESIGN-BASELINE-01.md)、[READINESS计划](superpowers/plans/2026-10-16-ipc-readiness.md)、[修订与验证建议](readiness/revision-and-verification.md)|

历史PR状态以其记录时间理解，实时状态读取GitHub；不把历史OPEN_AT_CREATION当作仍未合并。历史“不测试”仅限原任务，当前必要验证按本次质量闭环变更风险执行；旧候选/旧PASS不代替新SHA证据。完整客户归档继续PUBLIC_SUBSET_ONLY。

## 原字节归档与资料检查

[15前原字节](archive/pre-operation-closure-15/README.md) · [上轮入口前版本](archive/pre-audit-reconciliation-publication-01/README.md) · [14实施前](archive/pre-conformance-fix-14/README.md) · [原项目审查发布前](archive/pre-conformance-audit-01/README.md) · [13前](archive/pre-arch-extension-closure-13/README.md) · [12前](archive/pre-extension-delivery-12/README.md) · [11前](archive/pre-review-followup-11/README.md) · [纠偏前](archive/pre-reconciliation-01/README.md) · [框架前](archive/pre-framework-20261016/README.md)。归档保存形成时文件身份，不作为当前命令；不复制整仓或清空旧证据来减少文件。

[资料校验工具](../tools/README.md)只证明它覆盖的资料/链接/身份，不代表业务或现场通过。已知源资产问题与本次新增材料校验分开，保持原件和失败记录。

22历史交付：[22结果与候选身份](runtime/application-architecture-22-results.md)。
