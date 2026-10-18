# importer-pc

C#/.NET 10工位运行基座及独立Application/供包参考产品：WPF、多视图共享业务、真实SQLite事务/Outbox/原请求恢复、合成观察和受控扩展。旧现场迁移、真实SDK/WCS和现场资格分别验证。

## 当前工程状态

三个独立Application按[架构22](docs/design/application-architecture-22.md)选装，Parcel当前数据库4、配置schema3，Application.Contracts ABI1.6.0，保留PR23采集所有权和PR24归档、双位、完整配置与两阶段激活。执行main为b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d；PR23/24仍开放，本分支继承PR24固定a2e987a，不代为合并。N01–N07针对[独立审查02](docs/reviews/architecture-quality-audit-02/README.md)，与审查01 AQ编号和缺PDF记录分开。

先按[首次运行](docs/runtime/getting-started.md)选产品。新Application使用Assembly/ParcelConfiguration；[维护指南](docs/runtime/architecture-quality-closure-01.md)统一配置确认、恢复、容量及扩展路线；Supply的Product/StationConfig、箱/数量规则仅在[Supply指南](docs/runtime/operations.md)适用。Windows实际NOT_RUN，真实SDK/协议/现场BLOCKED。
## 继承的PR20/21整合基线

当前工程入口：[PR20/21协调整合](docs/runtime/pr20-21-integration.md)。最终运行/打包源码 **d5c8b7981ba8e4c8b9daf22df1cb686243815f3b**；三个评审问题及Create二次证据比较已修复，交叉恢复/资源/迁移、签名资格与5条解包进程旅程完成定向参考验证。Windows实际NOT_RUN，真实SDK/设备/WCS/PLC/现场BLOCKED。

[PR20](https://github.com/yuan2go/importer-pc/pull/20)已正常合并为`fa1d25211be454cd3cf49da40eb7d8d92e323652`；[PR21](https://github.com/yuan2go/importer-pc/pull/21)已纳入该main并保持经验证运行树，最终合并状态及提交读取GitHub和整合回执。能力可靠性与备份/健康设计共同生效：[20增量设计](docs/design/capability-reliability-20.md)、[备份恢复工作包](docs/work-packages/WP-IPC-BACKUP-HEALTH-RECOVERY.md)，补充19职责。[既有运行/部署](docs/runtime/capability-reliability-20.md)继续适用；旧报告及a147eac候选保持形成时身份。[整合前入口原字节](docs/archive/pre-pr20-21-integration/README.md)。

[19结果](docs/runtime/application-capability-19-results.md)中的c213aad候选与PASS保持形成时范围，本轮复核已发现组合可靠性、客户端和打包缺口，不能由旧PASS认定已关闭。Windows实际客户端/交互/UAC/ACL NOT_RUN；真实SDK/协议/设备/现场按所选提供者的证据分别准入，现有参考提供者不能满足Production。

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

历史22交付：[22结果与候选身份](docs/runtime/application-architecture-22-results.md)；本次继承[PR24结果](docs/runtime/architecture-quality-closure-01-results.md)，新验证见[本次N01–N07交付](docs/runtime/architecture-quality-audit-02-results.md)。
