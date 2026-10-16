# 统一资料与实施入口

更新日期：2026-10-16；当前执行 **WP-IPC-AUDIT-RECONCILIATION-01**，基准main `856e4de73f09a1c4f12af798c4d21e8631a6a86f`。本次用户已授权08/10实施及09合成/参考环境必要测试、构建、启动和发布；未授权生产WCS写入或真实设备动作。执行映射见[本次计划](superpowers/plans/2026-10-16-ipc-audit-reconciliation.md)，历史入口/报告/工作包的原字节见[纠偏前快照](archive/pre-reconciliation-01/README.md)。下列旧“本轮不要测试/尚未执行”描述是形成时记录，当前执行和最终结果以本次报告为准。

## 已入库的最终交付UI：Codex页面开发必读

[SS-UI-VIS-001 v1.1完整设计入口](ui/ss-ui-vis-001-v1.1/README.md) · [34张独立状态图](ui/ss-ui-vis-001-v1.1/GALLERY.md) · [原始交互HTML](ui/ss-ui-vis-001-v1.1/供包工作站_高保真交互原型.html) · [完整UI规范与交接](ui/ss-ui-vis-001-v1.1/docs/供包工作站_UIUX设计与开发交接说明.md)。PPTX、画板JSON、tokens及分离HTML/CSS/JS均在该目录，不仅是链接或摘要。11份源文件原字节保留，PNG为同源重新导出、非Windows运行截图；版本与边界见目录README。

该组生成成果依据用户本次明确上传要求发布。下文历史“所有衍生图均未入库”仅对本组以外材料有效；完整客户资料归档仍为部分完成。设计保存不自动关闭D/RV，也不能覆盖实际应用安全约束。

## 当前结论

[独立复核03](reviews/main-static-review-03.md) 与[执行边界](reviews/main-static-review-03-checks.json) 为当前入口：10项中8项的指定整改路径静态补齐；**E-06历史旧入口权限、E-10最终生效配置与实际租约集合仍有工程缺口**，均可不等SDK/WCS整改。源码静态符合性不代表运行PASS，10项运行验收均NOT_RUN。

下一阶段由[工程增量08](work-packages/WP-IPC-ENGINEERING-FOLLOWUP-08.md)收敛这两个缺口；[参考验证09](work-packages/WP-IPC-REFERENCE-VALIDATION-09.md)承接旧验证预期对齐、07/08风险覆盖及必要构建/同版交付，须明确恢复验证授权后执行。另有[UI基线落实10](work-packages/WP-IPC-UI-BASELINE-ALIGNMENT-10.md)处理已入库实物与当前WPF的差异；08/09/10本轮均未执行。[Windows06](work-packages/WP-IPC-WINDOWS-VALIDATION-06.md) W-01～06继续NOT_RUN，[集成05](work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md) B-01～06继续BLOCKED；不能混合为一个“待联调”。

[07实施记录](runtime/engineering-closure-07.md)保留形成时“10项已实现、运行未验”的交付范围，PR #6当前已MERGED；当前闭环判断以上述03为准。[复核02](reviews/main-static-review-02.md)保留原问题形成时证据。最后业务代码源提交`493416f`，待构建元数据ENGINEERING-CLOSURE-07 / Storage3 / SIMULATION-v1；没有新包、包SHA或Windows截图。历史D/RV不改写。

现有参考实现及上一包验证见 [ALIGNMENT-01](runtime/alignment-01.md)、[检查记录](runtime/alignment-01-checks.json)、[Windows待验](runtime/alignment-01-windows.md)。原检查中的PR OPEN和PASS保留形成时快照；PR #4当前已MERGED，历史运行结果未在本轮重跑。

**独立参考实现已加入。** RUNTIME-03从当时main `8908909` 开始，已建设工位基座、供包应用、多UI、SQLite/Outbox及独立loopback夹具；不是旧现场迁移。[运行/发布](runtime/operations.md)、[实施映射](runtime/implementation.md)、[实际验证与交付](runtime/delivery.md) 是当前实现入口。Windows运行与真实接口/现场状态分项报告，不以核心PASS替代。

完整客户材料归档仍为 **PUBLIC_SUBSET_ONLY**；本轮只公开新的通用复核与工作包，不上传受限原稿。开发就绪仍有条件：参考闭环可实施，真实业务规则、旧工程迁移、SDK/WCS和实体动作分别受对应证据约束。17个RV不因架构入库自动关闭。

上一包实施范围与验收见 [ARCH-UI-ALIGNMENT-01](work-packages/WP-IPC-ARCH-UI-ALIGNMENT-01.md)，其继承H04的装配、数据链、UI与后台隔离实现继续保留；当前闭环判断以03复核为准，07实施与历史复核定位保留。原加固范围与实施基准见 [HARDENING-04](work-packages/WP-IPC-RUNTIME-HARDENING-04.md)。RUNTIME-03交付结果保留为历史验证，不代表本轮结果。

## 当前必读

1. [框架架构与模块设计](design/framework.md)：职责、依赖、装配、数据/状态、配置、设备/外部边界、UI与部署。
2. [框架决策与讨论依据](design/framework-decisions.md)：用户设想和GPT建议分别登记；取舍、新参考技术路线、公开范围和外部依据。
   当前剩余差距先读 [独立复核03](reviews/main-static-review-03.md)、[工程增量08](work-packages/WP-IPC-ENGINEERING-FOLLOWUP-08.md) 和 [参考验证09](work-packages/WP-IPC-REFERENCE-VALIDATION-09.md)；07是已合并的实施记录，Windows/真实接口分别读06/05。
3. [WP-IPC-RUNTIME-03](work-packages/WP-IPC-RUNTIME-03.md)：历史S0～S5实施范围、局部阻塞和AC-01～11验收；不要把形成时“无实现”当当前事实。
4. [RUNTIME-03历史Codex提示词](work-packages/WP-IPC-RUNTIME-03-Codex.md)：保留原授权和执行上下文；下一阶段用08/10/09/06/05，不重新执行一遍RUNTIME-03。
5. [FRAMEWORK形成时资料检查](design/framework-checks.json)、[基线变更记录](baseline/CHANGELOG.md)、[当前manifest](manifest.json)。本次静态复核未运行资料验证器。

F系列为本轮架构推荐，不等于客户D项批准。C#/.NET10/WPF/SQLite用于无旧工程时的新参考产品，不是已有工程已采用此技术的声明。代码实施中取得不同事实时按ADR记录修订，不凭界面载体推断技术栈。

## 持续有效的业务证据与限制

- [关键决策确认包](readiness/decision-pack.md) 与 [技术基线](readiness/technical-baseline.json)：数量、拆包、实体、SDK、WCS、权限和旧工程仍待相应证据，不复制第二套D登记。
- [历史问题表](reviews/issues.json)、[评审状态](reviews/STATUS.md)、[修订和分层验证](readiness/revision-and-verification.md)：历史事实与新实现验收不能混淆。
- [UI状态](ui/STATUS.md)、[设计tokens](ui/tokens/tokens.json)、[CSS变量](ui/tokens/tokens.css)：作为视觉参考；当前操作约束见框架§10，未证明Windows/DPI/触控或持续操作通过。
- [公开范围](baseline/PUBLICATION.md)、[缺失/未上传](baseline/MISSING.md)：客户原件和本组以外受限资产仍未公开；v1.1限定发布见上方实物入口，不扩大为全部材料许可。

## 历史准备与原始资料归档

[BASELINE-01归档工作包](work-packages/WP-IPC-DESIGN-BASELINE-01.md) 的完整归档目标仍未关闭；[归档续作检查](baseline/continuation-checks.json) 及 [冲突清单](baseline/CONFLICTS.md) 保留形成时范围。最新记录表明九张PRD图和七个HTML依赖已由续作取得，但并未公开；新的Canva完整可编辑导出、部分历史截图仍缺，不用旧PPTX冒充新云端稿。

[READINESS-02历史计划](superpowers/plans/2026-10-16-ipc-readiness.md) 及 [当时检查](readiness/checks.json) 保留；PR#1已合并至分析基准，但文件中NOT_PUBLISHED等字段是准备时快照，不是实时发布状态。本轮不倒写历史测试、不把文档合并当作业务通过。

[框架前README/AGENTS/INDEX原件](archive/pre-framework-20261016/README.md) 已归档，解除当前入口“只能归档”的过期指引，但不删除公开与安全约束。完整原稿仍通过获授权资料位置读取；原行号与S/T定位指向原件，不指向改写的导航副本。

资料检查工具见 [tools/README.md](../tools/README.md)。业务实际命令见runtime运行说明；资料工具PASS只能证明其覆盖的资料检查。
