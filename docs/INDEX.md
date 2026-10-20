> 当前增量：[Sportmaster审查整改工作包](work-packages/WP-IPC-SPORTMASTER-AUDIT-REMEDIATION-01.md)与[整改及R-02分析](runtime/sportmaster-audit-remediation-01.md)。继承已合并PR57/58；原视觉任务和历史证据保留各自身份。

# 已合并实现基线：Sportmaster 原生视觉产品化

[本轮工作包](work-packages/WP-IPC-SPORTMASTER-VISUAL-PRODUCTIZATION-01.md) · [编码前基准与替代关系](design/sportmaster-visual-productization-01.md) · [运行及本轮证据](runtime/sportmaster-visual-productization-01.md)。Sportmaster新UI为本轮实现后的缺省；旧版本/旧候选策略和原始验证保持原身份。以下商业化、架构、安装和恢复规范仍有效，历史发行不代表本轮源码已发布。


# 统一资料与实施入口

**继承的商业交付：[Sportmaster 首个商业交付组合](work-packages/WP-IPC-SPORTMASTER-COMMERCIAL-DELIVERY-01.md)。** 起点 main `939462bee40c9eebe8eb2aa60860217afd95fcdd`，先提交设计，再实施。

> 已发布的历史 Windows 介质：[三业务下载与精确来源](runtime/windows-no-test-release-20261020.md)，运行源码 `939462bee40c9eebe8eb2aa60860217afd95fcdd`，原任务未运行测试/安装。既有[统一发行 cfe6c0e](runtime/unified-release-main-20261020.md)保持旧来源；两者不覆盖本次 Sportmaster 增量。

继承商业交付组合的冻结介质和逐项结果见[Sportmaster 交付结果](runtime/sportmaster-commercial-delivery-01-results.md)，操作见[同版交付手册](runtime/sportmaster-commercial-delivery-01-operations.md)。后继UI实现与本轮介质以页首视觉产品化交付记录为准。

## 1. 当前阅读顺序

1. [AGENTS](../AGENTS.md)、[本轮基准与清理](design/sportmaster-visual-productization-01.md)、[本轮运行与证据](runtime/sportmaster-visual-productization-01.md)，再读继承的[商业设计](design/sportmaster-commercial-delivery-01.md)、[必要验收](runtime/validation/sportmaster-commercial-delivery-01.md)。
2. [框架](design/framework.md)、[F 决策](design/framework-decisions.md)、[19 职责](design/application-capability-19.md)、[20 可靠性](design/capability-reliability-20.md)、[22 独立 Application](design/application-architecture-22.md)。
3. 继承 [COMM-00～10](work-packages/WP-IPC-COMMERCIAL-PRODUCTIZATION-01.md) 的设计/实现/验收及 [PR47/53 最终证据](runtime/evidence/pr47-main-integration/README.md)，再读下表相关指南和代码。
4. [D 决策](readiness/decision-pack.md)、[RV 状态](reviews/STATUS.md)、[公开范围](baseline/PUBLICATION.md)及 [manifest](manifest.json)。原 D/RV 不倒写，UI 修改先读第4节全部实物与完整交接。

旧“当前实施/正在不测试发布/最终 main 没有出包”按各自时间保留于[清理前归档](archive/pre-sportmaster-commercial-delivery-01/README.md)。历史资产只覆盖其声明源码，不能代替后继候选；当前工作不沿用历史免测要求。

## 2. 最新已实现源码与现行操作

实施基座的 main 包含 PR #44 的原生四步安装、独立离线维护/基本配置、启动修复及备份恢复入口，和 PR #45 的全部运行入口身份门禁。指定 Windows 账户非提权日常运行，允许 UAC 过滤/deny-only 的管理员身份；真正提升、错误 SID、系统/服务账户仍拒绝。正常安装、启动与维护不依赖 PowerShell，显式高级脚本单独说明依赖。

|任务|现行指南/代码证据|
|---|---|
|安装、首次运行、失败修复|[原生安装和修复](runtime/startup-deployment-02.md)、[首次运行](runtime/getting-started.md)、[Windows 交付维护](runtime/windows-application-delivery.md)|
|独立基础配置与原恢复机制|[启动部署源码/结果](runtime/startup-deployment-02-results.md)、[全部入口身份政策](runtime/runtime-identity-preflight-02.md)|
|Application 配置、资源及维护|[Application 运行](runtime/application-architecture-22.md)、[配置/恢复/容量](runtime/architecture-quality-closure-01.md)、[永久恢复与原失败](runtime/parcel-recovery-p2.md)|
|精确扩展、版本与部署资格|[扩展开发](extensions/developer-guide.md)、[受控装配设计](design/controlled-extensions.md)、[版本和组合证据](runtime/version-compatibility-01.md)|
|Sportmaster 业务与默认原生 UI|[RCS 设计](design/sportmaster-rcs-task.md)/[运行](runtime/sportmaster-rcs-task.md)、[视觉产品化运行](runtime/sportmaster-visual-productization-01.md)、[旧可选UI入口替代说明](runtime/sportmaster-alternate-ui.md)|
|Supply / Inspection 兼容入口|[Supply 首次操作](runtime/supply-getting-started.md)、[Supply 维护](runtime/operations.md)、[共同身份前置门禁](runtime/runtime-identity-preflight-02.md)|
|本次文档发布范围|[文档交付记录](runtime/commercial-productization-01-docs-delivery.md)|

Sportmaster 左右独立读码及取格口，无 PLC、图片展示或长期图库；DirectPlc 完整 UI 延期，WcsToPlc 独立主流程。登录/称重/采图/上传按实际产品组合和批准策略选装。Supply 使用自身 Storage/操作契约。业务/ABI/DB/schema/扩展版本从同一固定介质清单读取，不能仅凭版本号继承其他组合资格。

上表保留实施基座与原来源。COMM-00～10当前实现、复审修正及冻结介质统一见[实施结果](runtime/commercial-productization-01-results.md)与[PR47复审](reviews/pr47-commercial-productization-review.md)，不从旧计划段落推断当前缺失或已验收。

## 3. 源码、发布介质与验收分开

[PR #44/#45 历史发布记录](runtime/publication-deployment-20261019.md)记录当时政策与两个冻结来源；该文档任务未创建统一组合介质。下表仅为历史已发布资产，当前COMM候选/复审状态由本页顶部实施结果管理。

|已发布对象|精确范围|
|---|---|
|[startup-deployment-reference-e79a8bf](https://github.com/yuan2go/importer-pc/releases/tag/startup-deployment-reference-e79a8bf)|e79a8bfdc65328619c77013fb314eaaef4a35a2f；Sportmaster portable 和三个 win-x64；原生向导及更新的 UAC 政策|
|[runtime-all-entrypoints-reference-16ecb57](https://github.com/yuan2go/importer-pc/releases/tag/runtime-all-entrypoints-reference-16ecb57)|16ecb571cba32188f62a6e6e677030cd589dbf48；Supply/Inspection portable；形成时账户政策，不含原生向导增量|

历史 PASS 仅适用于相应源码/环境/介质。Windows 原生验收未执行部分仍 NOT_RUN，真实 SDK、合同、设备和现场资格仍按能力 BLOCKED；局部结果不构成商用投产批准。详细后验见[本包风险验收](runtime/validation/commercial-productization-01.md)、[启动部署 Windows](runtime/validation/startup-deployment-02-windows.md)、[质量维护 Windows](runtime/validation/architecture-quality-closure-01-windows.md)。

## 4. UI 实物与原生验收

修改 UI 前完整读取 [v1.1 入口](ui/ss-ui-vis-001-v1.1/README.md)、[图册](ui/ss-ui-vis-001-v1.1/GALLERY.md)、[原 HTML](ui/ss-ui-vis-001-v1.1/供包工作站_高保真交互原型.html)、分离 CSS/JS 和[完整交接](ui/ss-ui-vis-001-v1.1/docs/供包工作站_UIUX设计与开发交接说明.md)。

[UI 当前状态](ui/STATUS.md)区分历史模拟设计、当前源码、跨构建和真实截图；34 张 PNG 是同源设计导出。不得把历史共享箱/数量/拆包演示直接套用 Sportmaster 或把模拟 gate 复制为业务逻辑。

[Sportmaster Windows 后验](runtime/validation/sportmaster-alternate-ui-windows.md)、[Windows06](runtime/windows-06.md)/[原工作包](work-packages/WP-IPC-WINDOWS-VALIDATION-06.md)、[真实集成05](runtime/integration-05.md)/[原工作包](work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md)保留未验和外部条件，不因新文档自动关闭。

## 5. 继承任务、审查与历史证据

以下入口用于追溯来源及仍有效的局部规则，不是多套并列的当前实施入口。

|来源|保留入口|
|---|---|
|原生启动/部署与首次运行|[STARTUP-DEPLOYMENT-02 原工作包](work-packages/WP-IPC-STARTUP-DEPLOYMENT-02.md)、[原结果](runtime/startup-deployment-02-results.md)、[首次运行01](work-packages/WP-IPC-WINDOWS-FIRST-RUN-01.md)/[结果](runtime/windows-first-run-01-results.md)|
|SID / 全部入口与维护 P2|[SID01](runtime/runtime-identity-preflight-01.md)、[全部入口02](runtime/runtime-identity-preflight-02.md)、[维护P2工作包](work-packages/WP-IPC-UX-MAINTENANCE-P2-04.md)/[结果](runtime/ux-maintenance-p2-04-results.md)|
|UX 审查/整改与续修|[原审查](reviews/ux-commercial-quality-audit-01/README.md)、[UX闭环02](work-packages/WP-IPC-UX-COMMERCIAL-CLOSURE-02.md)/[结果](runtime/ux-commercial-closure-02-results.md)、[续修03](runtime/ux-commercial-rereview-03-results.md)、[原实施计划](superpowers/plans/2026-10-19-ux-commercial-fix-01.md)|
|商业审查03|[原报告和证据](reviews/commercial-quality-audit-03/README.md)、[交付工作包](work-packages/WP-IPC-COMMERCIAL-AUDIT-03-DELIVERY.md)、[工程结果](runtime/commercial-audit03-delivery-results.md)、[PR34原结果](runtime/commercial-quality-fix-03-results.md)|
|并发整合/PR27～32|[整合报告](runtime/parallel-task-reconciliation-01.md)、[起始清单](runtime/evidence/parallel-task-reconciliation-01/start-inventory.json)、[PR29原复审](reviews/parallel-task-reconciliation-01/pr29-original-review.md)、[PR32原审](reviews/parallel-task-reconciliation-01/pr32-original-review.md)、[PR27原审](reviews/pr27-ui-independent-review.md)|
|版本/RCS/UI|[版本设计](design/version-compatibility-01.md)/[原结果](runtime/version-compatibility-01-results.md)/[续修](runtime/version-compatibility-01-rereview-results.md)、[RCS工作包](work-packages/WP-IPC-SPORTMASTER-RCS-TASK-01.md)/[证据](runtime/evidence/sportmaster-rcs-task/README.md)、[UI工作包](work-packages/WP-IPC-SPORTMASTER-ALTERNATE-UI.md)/[原结果](runtime/sportmaster-alternate-ui-results.md)|
|PR31/32 永久恢复|[PR31原审](reviews/pr31-independent-rereview-f068156/README.md)、[恢复工作包](work-packages/WP-IPC-PR26-RECOVERY-CLOSURE.md)、[32结果及证据](runtime/parcel-recovery-p2.md)|
|审查02 / PR26|[原PDF和复核](reviews/architecture-quality-audit-02/README.md)、[实施结果](runtime/architecture-quality-audit-02-results.md)、[PR26原审](reviews/pr26-independent-78298b5/README.md)|
|质量闭环01 / PR24 / PR23|[设计](design/architecture-quality-closure-01.md)、[PR24原审](reviews/pr24-independent-rereview-f862c2a/README.md)、[R01～R05](runtime/architecture-quality-closure-01-rereview-results.md)、[R03](runtime/architecture-quality-closure-01-r03-results.md)、[采集P2](runtime/acquisition-lifecycle-p2.md)|
|17～22|[17](runtime/application-convergence-17-results.md)、[18](runtime/site-capabilities-18-results.md)、[19](runtime/application-capability-19-results.md)、[20](runtime/capability-reliability-20-results.md)、[备份健康](runtime/backup-health-recovery.md)、[20/21整合](runtime/pr20-21-integration.md)、[22结果](runtime/application-architecture-22-results.md)|
|03～16 / 原资料归档|[14](runtime/conformance-fix-14.md)、[15](runtime/operation-closure-15.md)、[16](runtime/ui-maintainability-16.md)、[13](runtime/arch-extension-closure-13.md)、[12](runtime/extension-windows-delivery-12.md)、[11](runtime/review-followup-11.md)、[09](runtime/reference-validation-09.md)、[03](runtime/implementation.md)、[BASELINE-01](work-packages/WP-IPC-DESIGN-BASELINE-01.md)|

原“不测试发布”任务回执：[部署](runtime/publication-deployment-20261019.md)、[维护P2/SID](runtime/publication-maintenance-20261019.md)、[UX](runtime/publication-ux-20261019.md)、[审查03](runtime/publication-audit03-20261019.md)、[早期发布](runtime/publication-20261019.md)。这些记录中的测试状态只属于原任务，不是后续免做必要测试的理由。

## 6. 资料完整性与归档

[工具说明](../tools/README.md)规定资料校验范围与原资产问题的记账，不通过新增例外掩盖历史失败。manifest 保存全树原条目与本次真实变更；本包文档检查不冒称全量历史资产 PASS。

本次清理前的 README/INDEX/框架和相关运行指南原字节见[商业化整理前归档](archive/pre-commercial-productization-01/README.md)。更早的完整入口、历史细项仍保留于[并发整合前归档](archive/pre-parallel-task-reconciliation-01/README.md)及各阶段 pre-* 目录。原审查01 PDF 缺失、客户全量资料 PUBLIC_SUBSET_ONLY 和原 D/RV 状态分别保留。

> 发行入口增量（PR48）：[原形成时范围](runtime/unified-windows-release-01.md)。本次整合不执行测试/构建，旧冻结结果不授予最终组合资格。

> 历史统一发行工具及中断交付见[结果](runtime/unified-release-01.md)；全部合并裁决见[不测试合并记录](runtime/publication-merge-20261020.md)。此句仅记录该次整合时状态，旧介质/原 PASS 保持原身份。

## 已完成的历史冻结发行回执

[本轮7acbb2c发行结果](runtime/unified-windows-release-01-results.md)含8套实际媒介和完整原始校验；它覆盖开工PR44/45/46，早于运行中另合并的PR47/DB8，不可冒充最新main或用于DB8。并发整合保留后合入的统一发行实现、旧原件及所有现行产品指引；本轮实际续建编排源码仅按hash归档于证据目录，当前操作仍按[现行统一发行](runtime/unified-release-01.md)。Windows实机NOT_RUN、真实能力BLOCKED、商用NOT_APPROVED。

> [PR57 的55025ce历史八包交付](runtime/unified-release-latest-55025ce.md)保留原运行源码与原校验，不包含后继 PR56/58 的商业交付及新视觉成果。本轮只合并，不测试、不出包；[合并裁决与计数](runtime/publication-no-test-20261020-pr57-pr58.md)分别记录当前源码与历史介质。
