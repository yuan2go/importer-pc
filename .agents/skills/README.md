# 项目级 skills

本项目仅保留一套激活中的原生 WPF 审查与整改 skill：[Hallmark-WPF](hallmark-wpf/SKILL.md)。

## 视觉真值与仲裁顺序

所有 UI 审查、原型对齐与 WPF 界面修改必须遵守以下优先级，高优先级覆盖低优先级。

1. **产品与业务底线**：根目录 [AGENTS.md](../../AGENTS.md)、[字体规范](../../docs/design/typography-01.md) 及 [文档索引](../../docs/INDEX.md) 指向的现行状态机、IPC、权限与配置契约。
2. **现行视觉与布局真值**：`docs/ui-v4/` 下的现行交互原型（`importer_pc_ui_v5.html`）与设计令牌（`ThemeTokens.xaml`）。
3. **审查与落地方法**：[Hallmark-WPF](hallmark-wpf/SKILL.md) 与 [WPF 审查方法](../../docs/design/wpf-ui-review.md)。

禁止在 UI 任务中参考 `docs/archive/**` 历史设计稿，也禁止引用 `docs/ui/tokens/tokens.json` 中已废弃的 `colorsAndFonts` 系统字体和等宽字体定义。该文件作为 `Ipc.OperatorTokens.json` 编译期兼容资源保留。

## Hallmark-WPF（本项目唯一激活 UI Skill）

基于 `Nutlope/hallmark 2.0.0` 的轻量 WPF 工业桌面工作流适配。无新增运行、构建或发布依赖。来源与许可见 [UPSTREAM.json](hallmark-wpf/UPSTREAM.json) 和 [LICENSE](hallmark-wpf/LICENSE)。

```text
$hallmark-wpf audit SM 作业页、非模态设置与运维工具；只报告问题，不修改代码。
$hallmark-wpf refine 工作站设置页；按 AGENTS.md 与 docs/ui-v4/ 对齐布局与控件样式，并运行原生验证。
```

`audit / refine / study` 为本地工作模式，默认只读。仅在用户明确要求实施时修改代码。

## 已归档的历史 skills

原引入的 `impeccable` 与 `ui-ux-pro-max` 包含 Web、移动端规范及旧版参数基线（如 `PARAM-UX-02`、`parameter-settings-v3`），现已移至 `docs/archive/skills/`，退出项目级自动发现。禁止在日常 WPF 界面任务中调用或恢复它们。
