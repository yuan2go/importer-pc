# importer-pc

C#/.NET 10工位运行基座与供包独立参考产品：Generic Host、WPF、多视图共享业务、真实SQLite事务/Outbox/原请求恢复、合成观察和受控扩展。旧现场迁移、真实SDK/WCS和现场资格分别验证。

## 当前状态与下一步

2026-10-17核查main **0096f3435cc5066a1b50836ef534ec24221a7e6f**，PR13/PR14均已合并。FIX14冻结实现为68cdc36；原同ID冲突、A02实例健康、A03激活及A04原草稿路径、R02/R03核心已有实现与必要验证。最新复核同时发现**接收责任按请求聚合的条件P1残余**，并保留UI/实际操作缺口；恢复能力不能整项宣告关闭。

**当前执行入口：[最新复核](docs/reviews/audit-reconciliation-01/POST-PR14-ANALYSIS.zh-CN.md) · [问题表](docs/reviews/audit-reconciliation-01/current-findings.json) · [OPERATION-CLOSURE-15工作包](docs/work-packages/WP-IPC-OPERATION-CLOSURE-15.md) · [精简Codex提示词](docs/work-packages/Codex-WP-IPC-OPERATION-CLOSURE-15.md)。15为待实施，不是当前能力。**

[用户交付PDF及原字节身份](docs/reviews/audit-reconciliation-01/README.md)固定8cabf06d，只作历史审查基线；原“14待实施/PR13开放”不代表当前状态。[FIX14实施报告](docs/runtime/conformance-fix-14.md)与[原运行记录](docs/runtime/evidence/conformance-fix-14/README.md)保留冻结源和macOS执行身份；本次文档发布没有重跑业务测试/构建或改变业务代码。

主体架构和主工作台继续沿用，优先修接收责任，再完成操作反馈、追溯布局和维护输入旅程。WPF交叉构建通过不能代替实际Windows；客户端截图/输入/DPI/声音/ACL仍NOT_RUN。真实SDK/WCS、实体/数量/权限及现场保持独立门禁，Production缺能力继续NotReady。

## 开发与运行入口

[统一资料入口](docs/INDEX.md) · [工作约束](AGENTS.md) · [框架设计](docs/design/framework.md) · [决策与批准边界](docs/design/framework-decisions.md) · [受控装配/扩展](docs/design/controlled-extensions.md) · [扩展开发](docs/extensions/developer-guide.md) · [运行、发布与恢复](docs/runtime/operations.md)

```sh
# 使用global.json固定的.NET SDK10.0.402与Python3；按本包影响选择验证
./scripts/build.sh
# 独立合成接收端；桌面/控制台的装配与模式见运行说明
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

现实现Storage/MinimumReader为4/4，参考扩展1.0.2。旧格式显式备份升级，旧精确1.0.1包不默认兼容新公共DLL；完整旧Effective迁移旅程见15的UPG01，不能换seed跳过。部署遵循指定账户、机级资源和包所有权；软件暂停不是安全急停。

## UI与历史交付

[v1.1实物设计及完整交接](docs/ui/ss-ui-vis-001-v1.1/README.md)保持原HTML/CSS/JS/tokens和资产；34张PNG是HTML同源导出，非客户端实图。主工作台局部修正，记录列表/详情局部结构调整；[UI当前状态](docs/ui/STATUS.md)分清已实现、待修和未验。

|历史阶段|结果入口|
|---|---|
|14一致性修复，冻结68cdc36、Storage4、参考包1.0.2|[实施报告](docs/runtime/conformance-fix-14.md)、[checks](docs/runtime/conformance-fix-14-checks.json)、[原工作包](docs/work-packages/WP-IPC-CONFORMANCE-FIX-14.md)|
|13架构/扩展收敛，冻结8cb475b、参考包1.0.1|[原报告](docs/runtime/arch-extension-closure-13.md)、[后续复核](docs/runtime/arch-extension-closure-13-recheck.md)|
|12受控后装与候选|[报告](docs/runtime/extension-windows-delivery-12.md)|
|11/08/09/10|[11](docs/runtime/review-followup-11.md)、[09参考验证](docs/runtime/reference-validation-09.md)、[10 UI映射](docs/runtime/ui-baseline-alignment-10.md)|

历史源码、候选和PASS/FAIL/NOT_RUN保留原身份。[本次更新前入口快照](docs/archive/pre-audit-reconciliation-publication-01/README.md)保存原字节；其余历史入口由INDEX保留。

仓库public；[公开范围](docs/baseline/PUBLICATION.md)持续有效，完整客户归档仍PUBLIC_SUBSET_ONLY。D批准与RV状态不因参考实现或文档发布关闭。只提交获授权审查、通用实现及合成证据，不附客户原稿、私密链接、凭据、生产数据、SDK或字体文件。历史源资产校验问题保留，不放松校验器换通过。
