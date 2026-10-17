# importer-pc

C#/.NET 10工位运行基座与供包独立参考产品：Generic Host、WPF、多视图共享业务、真实SQLite事务/Outbox/原请求恢复、合成观察和受控扩展。旧现场迁移、真实SDK/WCS和现场资格分别验证。

## 当前状态与下一步

从执行时main **5548b3f** 建独立开发分支，OPERATION-CLOSURE-15工程实现与必要验证已交付：每次接收独立责任/T3/重启恢复、箱级结果和当前守卫、忙态输入隔离、维护草稿/异步选择、追溯列表/详情/筛选，以及旧1.0.1实际部署到新版本的合成升级旅程。冻结源码 **013862fc2d1769605624f8e10df5cc5b448a7954**。

**当前入口：[15逐项报告](docs/runtime/operation-closure-15.md) · [checks/25问题与AC矩阵](docs/runtime/operation-closure-15-checks.json) · [原始证据](docs/runtime/evidence/operation-closure-15/README.md) · [同版参考候选](https://github.com/yuan2go/importer-pc/releases/tag/operation15-reference-013862f)。** [15任务](docs/work-packages/WP-IPC-OPERATION-CLOSURE-15.md)和[PR14后复核](docs/reviews/audit-reconciliation-01/POST-PR14-ANALYSIS.zh-CN.md)保留形成时内容；[原PDF](docs/reviews/audit-reconciliation-01/README.md)固定8cab，不改写为新验证。

实际WPF截图0；Windows客户端行为/输入/DPI/声音/ACL全部NOT_RUN，[后验清单](docs/runtime/validation/operation-closure-15-windows.md)逐项保留。真实SDK/WCS、实体/数量/权限与现场独立BLOCKED；Production缺能力继续NotReady。核心策略、交叉构建和候选校验不代替上述验收。

## 开发与运行入口

[统一资料入口](docs/INDEX.md) · [工作约束](AGENTS.md) · [框架设计](docs/design/framework.md) · [决策与批准边界](docs/design/framework-decisions.md) · [受控装配/扩展](docs/design/controlled-extensions.md) · [扩展开发](docs/extensions/developer-guide.md) · [运行、发布与恢复](docs/runtime/operations.md)

```sh
# 使用global.json固定的.NET SDK10.0.402与Python3；按本包影响选择验证
./scripts/build.sh
# 独立合成接收端；桌面/控制台的装配与模式见运行说明
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

现实现Storage/MinimumReader为5/5，参考扩展1.0.3。保留旧SQL1～4并追加5；旧精确1.0.1/1.0.2不重签或伪兼容。旧Effective经清偿、合法过渡、备份显式升级和新部署生效，合成旅程见15报告；真实旧根/备份盘点仍为独立门禁，不能换seed跳过。部署遵循指定账户、机级资源和包所有权；软件暂停不是安全急停。

## UI与历史交付

[v1.1实物设计及完整交接](docs/ui/ss-ui-vis-001-v1.1/README.md)保持原HTML/CSS/JS/tokens和资产；34张PNG是HTML同源导出，非客户端实图。主工作台局部修正，记录列表/详情局部结构调整；[UI当前状态](docs/ui/STATUS.md)分清已实现、待修和未验。

|历史阶段|结果入口|
|---|---|
|14一致性修复，冻结68cdc36、Storage4、参考包1.0.2|[实施报告](docs/runtime/conformance-fix-14.md)、[checks](docs/runtime/conformance-fix-14-checks.json)、[原工作包](docs/work-packages/WP-IPC-CONFORMANCE-FIX-14.md)|
|13架构/扩展收敛，冻结8cb475b、参考包1.0.1|[原报告](docs/runtime/arch-extension-closure-13.md)、[后续复核](docs/runtime/arch-extension-closure-13-recheck.md)|
|12受控后装与候选|[报告](docs/runtime/extension-windows-delivery-12.md)|
|11/08/09/10|[11](docs/runtime/review-followup-11.md)、[09参考验证](docs/runtime/reference-validation-09.md)、[10 UI映射](docs/runtime/ui-baseline-alignment-10.md)|

历史源码、候选和PASS/FAIL/NOT_RUN保留原身份。[本次更新前入口快照](docs/archive/pre-operation-closure-15/README.md)保存原字节；其余历史入口由INDEX保留。

仓库public；[公开范围](docs/baseline/PUBLICATION.md)持续有效，完整客户归档仍PUBLIC_SUBSET_ONLY。D批准与RV状态不因参考实现或文档发布关闭。只提交获授权审查、通用实现及合成证据，不附客户原稿、私密链接、凭据、生产数据、SDK或字体文件。历史源资产校验问题保留，不放松校验器换通过。
