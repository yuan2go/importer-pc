# 统一资料与实施入口

## 当前代码、审查与下一实施包

2026-10-17核查main **0096f3435cc5066a1b50836ef534ec24221a7e6f**；PR13/14已合并，FIX14冻结实现68cdc36。原A01同ID冲突、A02/A03/A04原路径及R02/R03核心有实现/验证；接收责任聚合残余、维护/输入与UI问题尚待收口。**下一包为OPERATION-CLOSURE-15，PLANNED / NOT_IMPLEMENTED。**

|入口|阅读目的|
|---|---|
|[最新复核](reviews/audit-reconciliation-01/POST-PR14-ANALYSIS.zh-CN.md)、[当前问题](reviews/audit-reconciliation-01/current-findings.json)|继承和关闭哪些子项、新增接收责任/操作残余、模块继续/暂停边界。|
|[原PDF与归并报告](reviews/audit-reconciliation-01/README.md)|8cab固定审查原字节；不直接沿用旧“14待实施”提示词。|
|[15完整工作包](work-packages/WP-IPC-OPERATION-CLOSURE-15.md)、[精简Codex提示词](work-packages/Codex-WP-IPC-OPERATION-CLOSURE-15.md)|下一步结果、范围、依赖、自主/确认项、功能/UI/必要测试及交付。|
|[FIX14实施](runtime/conformance-fix-14.md)、[checks](runtime/conformance-fix-14-checks.json)、[证据](runtime/evidence/conformance-fix-14/README.md)|冻结源68cdc36的真实核心验证、原FAIL与复验、候选/兼容及Windows边界。|
|[14原任务](work-packages/WP-IPC-CONFORMANCE-FIX-14.md)、[原项目审查](reviews/project-conformance-audit-01/README.md)|原AC14-01～07与形成时8cab证据，作为历史及受影响回归依据。|
|[本轮核验](runtime/evidence/audit-reconciliation-publication-01/README.md)、[前置归档](archive/pre-audit-reconciliation-publication-01/README.md)|输入身份、原PDF、日志SHA、文档校验、原字节/替代关系。|

Windows实际WPF、DPI、HID、声音和部署沿13批准后验，保持NOT_RUN；真实SDK/WCS/实体/数量/身份及现场按独立门禁。Linux/Mac运行、交叉构建、Windows实际验收分开。当前源/配置/测试未因本次文档发布改变，15目标不能写成现有能力。

## 代码前必读

1. [AGENTS](../AGENTS.md)、15工作包、最新复核/问题及相关原始证据；从执行时最新main核差异/工作区/并行PR。
2. [框架架构](design/framework.md)、[决策记录](design/framework-decisions.md)、[受控装配与扩展](design/controlled-extensions.md)。职责/事务/身份/生命周期要求继续有效，不能改弱目标消除缺陷。
3. [14任务](work-packages/WP-IPC-CONFORMANCE-FIX-14.md)、[实施](runtime/conformance-fix-14.md)/证据，以及[13任务](work-packages/WP-IPC-ARCH-EXTENSION-CLOSURE-13.md)/[复核](runtime/arch-extension-closure-13-recheck.md)、按影响再读[12任务](work-packages/WP-IPC-EXTENSION-WINDOWS-DELIVERY-12.md)/[交付](runtime/extension-windows-delivery-12.md)。保留后续批准的后装与Windows后验，勿重复实施已闭子项。
4. [运行/发布/维护](runtime/operations.md)、[扩展开发安装恢复](extensions/developer-guide.md)，核实际入口、配置、依赖和源码。
5. [D决策](readiness/decision-pack.md)、[技术基线](readiness/technical-baseline.json)、[RV状态](reviews/STATUS.md)/[原问题](reviews/issues.json)。采用不等于客户批准，局部新PASS不关闭全部旧RV。
6. [公开范围](baseline/PUBLICATION.md)、[资料缺失](baseline/MISSING.md)、[冲突](baseline/CONFLICTS.md)和[manifest](manifest.json)。先保留需改文件原字节，再清理相关过期入口；已正确内容不重复写，未经实现的目标不写成能力。

## UI实物与分层验收

[v1.1入口](ui/ss-ui-vis-001-v1.1/README.md) · [34张状态图](ui/ss-ui-vis-001-v1.1/GALLERY.md) · [原HTML](ui/ss-ui-vis-001-v1.1/供包工作站_高保真交互原型.html) · [完整UI交接](ui/ss-ui-vis-001-v1.1/docs/供包工作站_UIUX设计与开发交接说明.md) · [UI资料状态](ui/STATUS.md)。源文件、PPTX、画板、tokens、分离HTML/CSS/JS及PNG都有实际资料；PNG为同源重新导出，未变成Windows截图。修改页面前实际阅读实物，不照抄原型已知业务缺陷。

[13 UI映射](ui/arch-extension-closure-13-mapping.md) · [12 UI映射](ui/extension-delivery-12-mapping.md) · [10 UI实施](runtime/ui-baseline-alignment-10.md) · [Windows06](runtime/windows-06.md) · [06工作包](work-packages/WP-IPC-WINDOWS-VALIDATION-06.md) · [真实集成05](runtime/integration-05.md) · [05工作包](work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md)。旧“当前候选/环境阻塞”只表示形成时状态，15开工前按最新有效要求核对；W01～06和B01～06本身不能作为过期要求删除。

## 历史实施、评审与工作包

|阶段|完整入口|
|---|---|
|11复核跟进|[工作包](work-packages/WP-IPC-REVIEW-FOLLOWUP-11.md)、[报告](runtime/review-followup-11.md)、[checks](runtime/review-followup-11-checks.json)|
|08/09/10纠偏与参考验证|[纠偏工作包](work-packages/WP-IPC-AUDIT-RECONCILIATION-01.md)、[08](work-packages/WP-IPC-ENGINEERING-FOLLOWUP-08.md)、[09](work-packages/WP-IPC-REFERENCE-VALIDATION-09.md)、[10](work-packages/WP-IPC-UI-BASELINE-ALIGNMENT-10.md)、[09报告](runtime/reference-validation-09.md)、[checks](runtime/reference-validation-09-checks.json)|
|07工程闭环|[工作包](work-packages/WP-IPC-ENGINEERING-CLOSURE-07.md)、[报告](runtime/engineering-closure-07.md)|
|03/04及架构UI对齐|[03工作包](work-packages/WP-IPC-RUNTIME-03.md)、[实施](runtime/implementation.md)、[交付](runtime/delivery.md)、[04](work-packages/WP-IPC-RUNTIME-HARDENING-04.md)、[04结果](runtime/hardening-04.md)、[对齐包](work-packages/WP-IPC-ARCH-UI-ALIGNMENT-01.md)、[结果](runtime/alignment-01.md)|
|旧独立复核|[02](reviews/main-static-review-02.md)、[03](reviews/main-static-review-03.md)、[03 checks](reviews/main-static-review-03-checks.json)|
|资料与准备阶段|[BASELINE-01](work-packages/WP-IPC-DESIGN-BASELINE-01.md)、[READINESS计划](superpowers/plans/2026-10-16-ipc-readiness.md)、[修订与验证建议](readiness/revision-and-verification.md)|

历史PR状态以其记录时间理解，实时状态读取GitHub；不把历史OPEN_AT_CREATION当作仍未合并。历史“不测试”仅限原任务，当前必要验证按15执行；旧候选/旧PASS不代替新SHA证据。完整客户归档继续PUBLIC_SUBSET_ONLY。

## 原字节归档与资料检查

[本次入口前版本](archive/pre-audit-reconciliation-publication-01/README.md) · [14实施前](archive/pre-conformance-fix-14/README.md) · [原项目审查发布前](archive/pre-conformance-audit-01/README.md) · [13前](archive/pre-arch-extension-closure-13/README.md) · [12前](archive/pre-extension-delivery-12/README.md) · [11前](archive/pre-review-followup-11/README.md) · [纠偏前](archive/pre-reconciliation-01/README.md) · [框架前](archive/pre-framework-20261016/README.md)。归档保存形成时文件身份，不作为当前命令；不复制整仓或清空旧证据来减少文件。

[资料校验工具](../tools/README.md)只证明它覆盖的资料/链接/身份，不代表业务或现场通过。已知源资产问题与本次新增材料校验分开，保持原件和失败记录。
