# importer-pc

C#/.NET 10工位运行基座与供包独立参考产品：Generic Host、WPF、多视图共享业务、真实SQLite事务/Outbox/原请求恢复、合成观察和受控扩展。旧现场迁移、真实SDK/WCS和现场资格分别验证。

## 当前工程状态

已完成 **WP-IPC-APPLICATION-CAPABILITY-CLOSURE-19** 的实施与参考资格验证。开工main **08b8067aa6cba6a148252296c7a5b18a27b3be65**（PR18已合并）；最终运行/六候选源码 **c213aad54f5d48687efc0b56428991b6bf4f3537**。

[PR #19（OPEN，未合并）](https://github.com/yuan2go/importer-pc/pull/19) · [19结果及能力矩阵](docs/runtime/application-capability-19-results.md) · [现行职责设计](docs/design/application-capability-19.md) · [配置/客户端/部署/恢复](docs/runtime/application-capability-19.md) · [证据及完整身份](docs/runtime/evidence/application-capability-19/README.md)。三Application独立编排、公共会话/能力、称重守卫、两类上传与有界原图、签名提供者及结构化客户端已实施；三个portable解压9条进程旅程、精确Release签包激活、三个Windows候选交叉构建/解压通过。

**Windows实际客户端/交互/截图/ACL/UAC NOT_RUN；真实SDK/协议/设备/现场BLOCKED，Production继续NotReady。** Supply/Inspection行为与原SQL、D/RV和公开边界保持。17/18结果按其原SHA与形成时范围保留。

## 开发与运行入口

[统一资料入口](docs/INDEX.md) · [工作约束](AGENTS.md) · [框架设计](docs/design/framework.md) · [决策与批准边界](docs/design/framework-decisions.md) · [受控装配/扩展](docs/design/controlled-extensions.md) · [扩展开发](docs/extensions/developer-guide.md) · [运行、发布与恢复](docs/runtime/operations.md)

```sh
# 使用global.json固定的.NET SDK10.0.402与Python3；按本包影响选择验证
./scripts/build.sh
# 独立合成接收端；桌面/控制台的装配与模式见运行说明
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

Supply兼容Storage/MinimumReader为5/5；17冻结参考读码1.0.5与场地能力包1.0.0属于其原发布；19使用Application.Contracts及场地能力示例1.2.0、新观察/集成参考包1.0.6精确身份，旧签名包不覆盖。保留SQL1～5及checksum；旧精确1.0.1/1.0.2不重签或伪兼容。旧Effective经清偿、合法过渡、备份显式升级和新部署生效，合成旅程见15报告；真实旧根/备份盘点仍为独立门禁，不能换seed跳过。部署遵循指定账户、机级资源和包所有权；软件暂停不是安全急停。

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
