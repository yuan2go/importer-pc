# 统一资料与实施入口

更新日期：2026-10-16；当前实施：WP-IPC-ENGINEERING-CLOSURE-07；执行基准 `6d4063ec54e8167c8710e46befbe66f8b688ef08`（PR #5已合并）。用户授权实施10项工程整改，仍要求不测试；所有运行验证NOT_RUN。

## 当前结论

最新独立判断见 [MAIN-REVIEW-02](reviews/main-static-review-02.md)：10项工程缺口、6组Windows待验、6组真实接口/规则/许可阻塞分别登记；本轮运行验证全部NOT_RUN。下一步先做 [ENGINEERING-CLOSURE-07](work-packages/WP-IPC-ENGINEERING-CLOSURE-07.md)，[Windows验证06](work-packages/WP-IPC-WINDOWS-VALIDATION-06.md) 与 [集成边界05](work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md) 分项推进；07已获本次实施授权；06尚未执行、05真实能力仍受阻，均不构成生产放行。

现有参考实现及上一包验证见 [ALIGNMENT-01](runtime/alignment-01.md)、[检查记录](runtime/alignment-01-checks.json)、[Windows待验](runtime/alignment-01-windows.md)。原检查中的PR OPEN和PASS保留形成时快照；PR #4当前已MERGED，历史运行结果未在本轮重跑。

**独立参考实现已加入。** RUNTIME-03从当时main `8908909` 开始，已建设工位基座、供包应用、多UI、SQLite/Outbox及独立loopback夹具；不是旧现场迁移。[运行/发布](runtime/operations.md)、[实施映射](runtime/implementation.md)、[实际验证与交付](runtime/delivery.md) 是当前实现入口。Windows运行与真实接口/现场状态分项报告，不以核心PASS替代。

完整客户材料归档仍为 **PUBLIC_SUBSET_ONLY**；本轮只公开新的通用框架和实施规范，不上传受限原稿。开发就绪仍有条件：参考闭环可实施，真实业务规则、旧工程迁移、SDK/WCS和实体动作分别受对应证据约束。17个RV不因架构入库自动关闭。

上一包实施范围与验收见 [ARCH-UI-ALIGNMENT-01](work-packages/WP-IPC-ARCH-UI-ALIGNMENT-01.md)，其继承H04的装配、数据链、UI与后台隔离实现继续保留；当前剩余差距以MAIN-REVIEW-02为准。原加固范围与实施基准见 [HARDENING-04](work-packages/WP-IPC-RUNTIME-HARDENING-04.md)。RUNTIME-03交付结果保留为历史验证，不代表本轮结果。

## 当前必读

1. [框架架构与模块设计](design/framework.md)：职责、依赖、装配、数据/状态、配置、设备/外部边界、UI与部署。
2. [框架决策与讨论依据](design/framework-decisions.md)：用户设想和GPT建议分别登记；取舍、新参考技术路线、公开范围和外部依据。
   当前剩余差距和后续执行先读 [独立静态复核](reviews/main-static-review-02.md) 及 [工程收敛07](work-packages/WP-IPC-ENGINEERING-CLOSURE-07.md)；Windows/真实接口分别读06/05，不把未验证功能标为已验收。
3. [WP-IPC-RUNTIME-03](work-packages/WP-IPC-RUNTIME-03.md)：历史S0～S5实施范围、局部阻塞和AC-01～11验收；不要把形成时“无实现”当当前事实。
4. [RUNTIME-03历史Codex提示词](work-packages/WP-IPC-RUNTIME-03-Codex.md)：保留原授权和执行上下文；下一阶段用07/06/05，不重新执行一遍RUNTIME-03。
5. [FRAMEWORK形成时资料检查](design/framework-checks.json)、[基线变更记录](baseline/CHANGELOG.md)、[当前manifest](manifest.json)。本次静态复核未运行资料验证器。

F系列为本轮架构推荐，不等于客户D项批准。C#/.NET10/WPF/SQLite用于无旧工程时的新参考产品，不是已有工程已采用此技术的声明。代码实施中取得不同事实时按ADR记录修订，不凭界面载体推断技术栈。

## 持续有效的业务证据与限制

- [关键决策确认包](readiness/decision-pack.md) 与 [技术基线](readiness/technical-baseline.json)：数量、拆包、实体、SDK、WCS、权限和旧工程仍待相应证据，不复制第二套D登记。
- [历史问题表](reviews/issues.json)、[评审状态](reviews/STATUS.md)、[修订和分层验证](readiness/revision-and-verification.md)：历史事实与新实现验收不能混淆。
- [UI状态](ui/STATUS.md)、[设计tokens](ui/tokens/tokens.json)、[CSS变量](ui/tokens/tokens.css)：作为视觉参考；当前操作约束见框架§10，未证明Windows/DPI/触控或持续操作通过。
- [公开范围](baseline/PUBLICATION.md)、[缺失/未上传](baseline/MISSING.md)：完整客户原件和衍生图像未入公开main；受限阅读与公开发布分开。

## 历史准备与原始资料归档

[BASELINE-01归档工作包](work-packages/WP-IPC-DESIGN-BASELINE-01.md) 的完整归档目标仍未关闭；[归档续作检查](baseline/continuation-checks.json) 及 [冲突清单](baseline/CONFLICTS.md) 保留形成时范围。最新记录表明九张PRD图和七个HTML依赖已由续作取得，但并未公开；新的Canva完整可编辑导出、部分历史截图仍缺，不用旧PPTX冒充新云端稿。

[READINESS-02历史计划](superpowers/plans/2026-10-16-ipc-readiness.md) 及 [当时检查](readiness/checks.json) 保留；PR#1已合并至分析基准，但文件中NOT_PUBLISHED等字段是准备时快照，不是实时发布状态。本轮不倒写历史测试、不把文档合并当作业务通过。

[框架前README/AGENTS/INDEX原件](archive/pre-framework-20261016/README.md) 已归档，解除当前入口“只能归档”的过期指引，但不删除公开与安全约束。完整原稿仍通过获授权资料位置读取；原行号与S/T定位指向原件，不指向改写的导航副本。

资料检查工具见 [tools/README.md](../tools/README.md)。业务实际命令见runtime运行说明；资料工具PASS只能证明其覆盖的资料检查。
