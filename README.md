> FIX-14当前交付：源码 `68cdc36` 完成A-01～04及适用R项的核心修复/必要验证，Storage4，WPF交叉构建PASS；[逐A/R与AC14报告](docs/runtime/conformance-fix-14.md)。Windows沿13批准后验，NOT_RUN；真实SDK/WCS/现场仍BLOCKED。以下8cabf06d审查结论及13资料保留其原版本含义，不能冒称main已包含本分支修复。

# importer-pc

C#/.NET 10工位运行基座与供包独立参考产品：Generic Host、WPF、多视图共享业务、真实SQLite事务/Outbox/原请求恢复、合成观察和受控扩展。旧现场工程迁移、真实SDK/WCS和现场资格分别验证。

## 当前状态与下一步

2026-10-17独立审查固定main **`8cabf06d3c6d53ba551b65e58a13db6cdcaba16a`**（PR12已合并）。必要Linux隔离构建/模拟验证通过，同时复现A-01/A-02，并静态确认A-03/A-04；完整恢复、多实例健康及维护UI仍有缺口。Windows实际验收继承13明确的后置决定，保持 **WINDOWS_NOT_RUN**；真实SDK/WCS/实体/数量/权限/现场仍按对应门禁处理。

[独立审查、PDF和问题](docs/reviews/project-conformance-audit-01/README.md) · [原始验证证据](docs/runtime/evidence/project-conformance-audit-01/README.md) · [下一步分析](docs/reviews/project-conformance-audit-01/next-step-analysis.md) · [WP-IPC-CONFORMANCE-FIX-14](docs/work-packages/WP-IPC-CONFORMANCE-FIX-14.md) · [Codex提示词](docs/work-packages/Codex-WP-IPC-CONFORMANCE-FIX-14.md)

**14为待实施工作包，未修复代码。** 本次提交审查材料和执行交接，不把文档入库当作工程缺陷已关闭。修复必须从执行时最新main核对差异，先读有效文档、归档过期当前指引，再按AC14完成工程及必要验证。原报告/失败记录/历史验收保持原身份。

## 开发与运行入口

[统一资料入口](docs/INDEX.md) · [工作约束](AGENTS.md) · [框架设计](docs/design/framework.md) · [决策与批准边界](docs/design/framework-decisions.md) · [受控装配/扩展](docs/design/controlled-extensions.md) · [扩展开发](docs/extensions/developer-guide.md) · [运行、发布与恢复](docs/runtime/operations.md)

```sh
# 使用global.json固定的.NET SDK 10.0.402与Python3
./scripts/build.sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --extensions
# 独立合成接收端；桌面/控制台的装配与模式见运行说明
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

上方为既有运行入口，不表示文档发布时重新执行。具体修复仅运行受影响验证；跨平台WPF构建不能替代Windows操作。Production缺必需真实能力继续NotReady。Storage3旧库使用原格式备份、身份回执和显式升级；部署遵循既有指定账户与机级资源/包所有权约束。

## UI与历史交付

[v1.1实物设计及完整交接](docs/ui/ss-ui-vis-001-v1.1/README.md)保留原HTML/CSS/JS/tokens和资产；34张PNG为HTML同源导出，不能作为WPF截图。最新已合并13的[复核记录](docs/runtime/arch-extension-closure-13-recheck.md)、[checks](docs/runtime/arch-extension-closure-13-recheck-checks.json)和[UI映射](docs/ui/arch-extension-closure-13-mapping.md)保留其实际源码、平台与验证范围；本次A/R结论另行追加。

|历史阶段|结果入口|
|---|---|
|13架构/扩展收敛，最终宿主8cb475b、插件1.0.1|[原报告](docs/runtime/arch-extension-closure-13.md)、[后续复核](docs/runtime/arch-extension-closure-13-recheck.md)|
|12受控后装与候选交付|[报告](docs/runtime/extension-windows-delivery-12.md)|
|11独立复核跟进|[报告](docs/runtime/review-followup-11.md)|
|08/09/10纠偏、参考验证及UI实现|[参考验证](docs/runtime/reference-validation-09.md)、[UI映射](docs/runtime/ui-baseline-alignment-10.md)|

旧源码、候选与PASS/FAIL/NOT_RUN不迁移成新版本验收。发布前README/INDEX/manifest的原字节见[快照](docs/archive/pre-conformance-audit-01/README.md)。其他历史入口由INDEX保留。

仓库public；[公开范围](docs/baseline/PUBLICATION.md)持续有效，完整客户归档仍为PUBLIC_SUBSET_ONLY。D批准和历史RV-01～17状态不因参考实现或新审查而关闭。只发布本次获授权审查、通用实现与合成证据，不附客户原稿、私密链接、凭据、生产数据或厂商SDK。资料校验的历史源资产错误另有记录，不删除原设计或放松校验器换通过。
