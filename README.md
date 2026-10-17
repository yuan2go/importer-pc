# importer-pc

C#/.NET 10工位运行基座与供包独立参考产品：Generic Host、WPF、多视图共享业务、真实SQLite事务/Outbox/原请求恢复、合成观察和受控扩展。旧现场迁移、真实SDK/WCS和现场资格分别验证。

## 当前工程状态

最新核验main **12a40b3**（PR15已合并）。本轮实施 **UI-MAINTAINABILITY-16**：报警/历史/维护操作闭环、窗口职责、适配器公共边界和动作/责任/视图规则、声明式独立扩展工具、统一发布入口及健康续期写放大优化。

当前入口：[16逐项实现与边界](docs/runtime/ui-maintainability-16.md) · [checks/实际证据](docs/runtime/ui-maintainability-16-checks.json) · [首次运行与首条参考作业](docs/runtime/getting-started.md)。版本Storage/MinimumReader=5/5、SIMULATION-v1/扩展契约1；参考包新增不可变1.0.4，原1.0.0～1.0.3/宿主/请求身份保持。旧15结果保留[原报告](docs/runtime/operation-closure-15.md)及[同版候选](https://github.com/yuan2go/importer-pc/releases/tag/operation15-reference-013862f)。

实际WPF截图0；Windows客户端/HID/DPI/双窗/声音/触控/PowerShell/UAC/ACL全部NOT_RUN，[同版后验](docs/runtime/validation/ui-maintainability-16-windows.md)逐项保留。真实SDK/WCS、身份/实体/数量/逐SKU少货与现场按能力BLOCKED，Production缺必需能力NotReady。核心/交叉构建/候选不代替这些验收。

## 开发与运行入口

[统一资料入口](docs/INDEX.md) · [工作约束](AGENTS.md) · [框架设计](docs/design/framework.md) · [决策与批准边界](docs/design/framework-decisions.md) · [受控装配/扩展](docs/design/controlled-extensions.md) · [扩展开发](docs/extensions/developer-guide.md) · [运行、发布与恢复](docs/runtime/operations.md)

```sh
# 使用global.json固定的.NET SDK10.0.402与Python3；按本包影响选择验证
./scripts/build.sh
# 独立合成接收端；桌面/控制台的装配与模式见运行说明
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

现实现Storage/MinimumReader为5/5，参考扩展1.0.4。保留SQL1～5及checksum；旧精确1.0.1/1.0.2不重签或伪兼容。旧Effective经清偿、合法过渡、备份显式升级和新部署生效，合成旅程见15报告；真实旧根/备份盘点仍为独立门禁，不能换seed跳过。部署遵循指定账户、机级资源和包所有权；软件暂停不是安全急停。

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
