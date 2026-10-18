# importer-pc

C#/.NET 10工位运行基座与供包独立参考产品：Generic Host、WPF、多视图共享业务、真实SQLite事务/Outbox/原请求恢复、合成观察和受控扩展。旧现场迁移、真实SDK/WCS和现场资格分别验证。

## 当前工程状态

执行基线main **6e9c890**（PR16已合并）。当前交付 **Application架构收敛与场地业务贯通17**：Platform/Application/Drivers/Integrations职责收敛，三个独立Application与实际WPF装配，重量/原阶段恢复，受控扩展和可运行参考候选。

[17实际完成矩阵](docs/runtime/application-convergence-17-results.md) · [运行/配置/扩展/恢复](docs/runtime/application-convergence-17.md) · [checks与证据](docs/runtime/application-convergence-17-checks.json) · [六个候选与独立扩展源](https://github.com/yuan2go/importer-pc/releases/tag/application17-reference-f184a6b)。Supply/Inspection原行为与旧公共身份保留，旧Supply 5/5 SQL及历史请求不改写。

三条完整参考链、必要核心/恢复/资源/配置验证、独立签名扩展安装旅程、WPF交叉构建及portable候选解压启动/重启/停止PASS。**Windows实际客户端截图0，交互/ACL/UAC NOT_RUN；真实SDK/读码/秤/PLC/WCS与现场BLOCKED；Production NotReady**，见[17后验清单](docs/runtime/validation/application-convergence-17-windows.md)。原D/RV和资料公开边界不变。16与更早报告保留形成时身份。

## 开发与运行入口

[统一资料入口](docs/INDEX.md) · [工作约束](AGENTS.md) · [框架设计](docs/design/framework.md) · [决策与批准边界](docs/design/framework-decisions.md) · [受控装配/扩展](docs/design/controlled-extensions.md) · [扩展开发](docs/extensions/developer-guide.md) · [运行、发布与恢复](docs/runtime/operations.md)

```sh
# 使用global.json固定的.NET SDK10.0.402与Python3；按本包影响选择验证
./scripts/build.sh
# 独立合成接收端；桌面/控制台的装配与模式见运行说明
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

Supply兼容Storage/MinimumReader为5/5；本轮参考读码1.0.5与场地能力包1.0.0均使用新不可变身份。保留SQL1～5及checksum；旧精确1.0.1/1.0.2不重签或伪兼容。旧Effective经清偿、合法过渡、备份显式升级和新部署生效，合成旅程见15报告；真实旧根/备份盘点仍为独立门禁，不能换seed跳过。部署遵循指定账户、机级资源和包所有权；软件暂停不是安全急停。

## UI与历史交付

[v1.1实物设计及完整交接](docs/ui/ss-ui-vis-001-v1.1/README.md)保持原HTML/CSS/JS/tokens和资产；34张PNG是HTML同源导出，非客户端实图。主工作台局部修正，记录列表/详情局部结构调整；[UI当前状态](docs/ui/STATUS.md)分清已实现、待修和未验。

|历史阶段|结果入口|
|---|---|
|14一致性修复，冻结68cdc36、Storage4、参考包1.0.2|[实施报告](docs/runtime/conformance-fix-14.md)、[checks](docs/runtime/conformance-fix-14-checks.json)、[原工作包](docs/work-packages/WP-IPC-CONFORMANCE-FIX-14.md)|
|13架构/扩展收敛，冻结8cb475b、参考包1.0.1|[原报告](docs/runtime/arch-extension-closure-13.md)、[后续复核](docs/runtime/arch-extension-closure-13-recheck.md)|
|12受控后装与候选|[报告](docs/runtime/extension-windows-delivery-12.md)|
|11/08/09/10|[11](docs/runtime/review-followup-11.md)、[09参考验证](docs/runtime/reference-validation-09.md)、[10 UI映射](docs/runtime/ui-baseline-alignment-10.md)|

历史源码、候选和PASS/FAIL/NOT_RUN保留原身份。[本次更新前入口快照](docs/archive/pre-ui-maintainability-16/README.md)保存原字节；其余历史入口由INDEX保留。

仓库public；[公开范围](docs/baseline/PUBLICATION.md)持续有效，完整客户归档仍PUBLIC_SUBSET_ONLY。D批准与RV状态不因参考实现或文档发布关闭。只提交获授权审查、通用实现及合成证据，不附客户原稿、私密链接、凭据、生产数据、SDK或字体文件。历史源资产校验问题保留，不放松校验器换通过。
