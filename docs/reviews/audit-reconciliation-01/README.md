# 架构与 UI 归并审查：原 PDF 与最新复核

**当前阅读顺序：[PR14 后复核](POST-PR14-ANALYSIS.zh-CN.md) → [当前问题表](current-findings.json) → [下一工作包15](../../work-packages/WP-IPC-OPERATION-CLOSURE-15.md) → [精简 Codex 提示词](../../work-packages/Codex-WP-IPC-OPERATION-CLOSURE-15.md)。**

## 本次提交的 PDF

[WP-IPC-AUDIT-RECONCILIATION-01_8cabf06d_20261017.pdf](WP-IPC-AUDIT-RECONCILIATION-01_8cabf06d_20261017.pdf) 为用户刚收到并要求提交 main 的原文件，27页、309806字节，SHA256：

`7586e7c58f37266eea4a27af453a7a48fad86d14d7f225ba2824755ab457d441`

PDF 固定实现基线为 `8cabf06d3c6d53ba551b65e58a13db6cdcaba16a`。形成时 PR13 尚未合并，建议扩展 FIX14；这些内容原样保留，不倒写为最新结论。本次复核时 main 已为 `0096f3435cc5066a1b50836ef534ec24221a7e6f`，PR13/14 已合并。因此不要直接执行 PDF 中的旧提示词，也不要将旧问题整批当成当前缺陷或已修复。

原 PDF 的“未提交”描述仅指其形成时间；本目录随本次获授权文档提交发布。PDF 内的27页审查本身没有改字节。当前代码结论与剩余工作采用新增复核及15工作包。

## 可读原件与证据

- [归并报告原文](inputs/reconciliation-report-8cabf06d.original.md.txt)、[原问题表](inputs/findings-8cabf06d.original.json)。
- [UI 审查原文](inputs/ui-report-8cabf06d.original.md.txt)、[UI 原问题表](inputs/ui-findings-8cabf06d.original.json)。
- [原建议 FIX14 v1.1](inputs/proposed-FIX14-v1.1-8cabf06d.original.md.txt)、[原建议提示词](inputs/proposed-Codex-FIX14-v1.1-8cabf06d.original.md.txt)。它们是历史方案，不覆盖已执行的14任务定义；其中 UI 补充范围转入15。
- [原件 SHA 清单](historical-inputs.json)、[本轮只读核查证据](../../runtime/evidence/audit-reconciliation-publication-01/README.md)。
- [已入库的项目审查及原始证据](../project-conformance-audit-01/README.md)、[FIX14 实施报告](../../runtime/conformance-fix-14.md)、[FIX14 运行记录](../../runtime/evidence/conformance-fix-14/README.md)。
- [更新入口前的原字节](../../archive/pre-audit-reconciliation-publication-01/README.md)。

本目录任务身份为2026-10-17的独立归并审查；不覆盖2026-10-16已执行的同名历史工作包 `docs/work-packages/WP-IPC-AUDIT-RECONCILIATION-01.md`。采用设计仍为 SS-UI-VIS-001 v1.1；客户原稿公开边界、D/RV、Windows后验和真实集成门禁保持各自身份。
