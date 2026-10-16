# 统一资料与实施入口

更新日期：2026-10-16；本轮设计任务：WP-IPC-FRAMEWORK-01；分析基准 `39747a8c35b748da71bf9acfd56a9c9812b199b4`。

## 当前结论

**框架目标设计已收敛；实现尚未开始。** 当前代码是归档校验工具，没有供包C#工程。下一阶段是工位运行基座、供包参考业务、多UI、本地持久化与恢复的完整实现，而不是再次只交资料准备。

完整客户材料归档仍为 **PUBLIC_SUBSET_ONLY**；本轮只公开新的通用框架和实施规范，不上传受限原稿。开发就绪仍有条件：参考闭环可实施，真实业务规则、旧工程迁移、SDK/WCS和实体动作分别受对应证据约束。17个RV不因架构入库自动关闭。

## 当前必读

1. [框架架构与模块设计](design/framework.md)：职责、依赖、装配、数据/状态、配置、设备/外部边界、UI与部署。
2. [框架决策与讨论依据](design/framework-decisions.md)：用户设想和GPT建议分别登记；取舍、新参考技术路线、公开范围和外部依据。
3. [WP-IPC-RUNTIME-03](work-packages/WP-IPC-RUNTIME-03.md)：设计—实现差距、S0～S5实施范围、局部阻塞和AC-01～11验收。
4. [可直接执行的Codex提示词](work-packages/WP-IPC-RUNTIME-03-Codex.md)：从最新main读取真实文档后实施，在开发分支创建PR。
5. [本轮资料检查](design/framework-checks.json)、[基线变更记录](baseline/CHANGELOG.md)、[当前manifest](manifest.json)。

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

资料检查工具见 [tools/README.md](../tools/README.md)。无真实业务工程前不虚构业务构建命令；工具PASS只能证明其覆盖的资料检查。
