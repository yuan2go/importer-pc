# WP-IPC-PROJECT-CONFORMANCE-AUDIT-01

2026-10-17 独立审查，固定业务源码 **`8cabf06d3c6d53ba551b65e58a13db6cdcaba16a`**。本目录是已完成审查的发布，不是修复提交。

## 结论与材料

main 已有实际装配、SQLite/Outbox、原请求恢复及受控扩展；四项确定缺陷仍待修复，三个条件风险分别保留其适用前提。Linux 隔离构建、必要选择器与两个探针有原始记录；Windows 实际交互、真实 SDK/WCS/设备及现场验收未运行。

|材料|内容|
|---|---|
|[完整报告](report.md)|公开Markdown呈现版；正文不变，仅追加19个固定源码的引用定义。结论、矩阵、架构/业务链、A/R/G及验证完整保留；[原字节文本](report.original.md.txt)另存。|
|[23页PDF](WP-IPC-PROJECT-CONFORMANCE-AUDIT-01_8cabf06d_20261017.pdf)|与上一轮交付的 PDF 字节一致。|
|[问题清单](issues.json)|原字节结构化 A/R 清单；不覆盖历史 RV issues。|
|[审查结果摘要](checks.json)|固定 SHA、验证层级、确定缺陷和条件风险的机器可读摘要。|
|[原始证据入口](../../runtime/evidence/project-conformance-audit-01/README.md)|构建/选择器/失败/复验日志、覆盖记录、远端基线、两探针源码与原始合成数据库。|
|[下一步分析](next-step-analysis.md)|修复顺序、文档清理对象、条件风险与兼容性边界。|
|[修复工作包14](../../work-packages/WP-IPC-CONFORMANCE-FIX-14.md)|AC14 结果约束及必要验证；状态 PLANNED / NOT_IMPLEMENTED。|
|[Codex执行提示词](../../work-packages/Codex-WP-IPC-CONFORMANCE-FIX-14.md)|直接复制给执行 Codex；具体拆分及实现方式自主选择。|

## 版本与公开范围

提交准备时再次远端核验 main 仍为上述 SHA、打开 PR 为0，见[发布基线](../../runtime/evidence/project-conformance-audit-01/metadata/publication-baseline.json)。文档发布本身会产生新提交；它不改变原审查和原运行的源码身份。修复执行时仍必须读取届时最新 main 和并行 PR。

原报告正文、原字节文本、PDF、问题清单和入库的原始运行日志没有修改。公开Markdown版仅追加引用定义，使连续的S编号能被GitHub和既有资料校验器正确识别；“未提交”“未创建修复PR”等叙述指原只读审查时点。本次仅新增审查/交接文档，整理 README、INDEX 的当前入口和 manifest。原入口与清单已按原字节[归档](../../archive/pre-conformance-audit-01/README.md)。业务源码、测试断言、原设计及历史验收不改。

完整原证据 ZIP 已随原审查交付并继续保留。本次 Git 提交收录审阅/复现所需的文本证据和五个原始 SQLite 文件的压缩副本，未重复提交生成的部署二进制、缓存和全部临时运行目录。两份阅读覆盖记录仅移除非公开文件的路由 ID，读取范围不变；原/公开字节哈希和完整原包目录均见[公开来源登记](../../runtime/evidence/project-conformance-audit-01/publication-provenance.json)。客户原稿、私密链接、凭据、生产数据和厂商 SDK 未加入仓库。

原报告问题状态固定在审查时点。后续关闭必须另附新 SHA、实现路径、必要验证与未验边界，不回写本报告或旧 PASS/FAIL/NOT_RUN。

文字澄清：原报告§12.3“不复制客户原文、名称”的措辞过宽；报告确实列出来源文档标题和必要需求摘要。准确的公开边界是未附客户原稿全文、私密访问链接、客户组织/人员标识及原图片。本次授权发布该审查报告及结果，不扩大为原始客户资料整体公开许可；原报告/PDF字节保持不变。
