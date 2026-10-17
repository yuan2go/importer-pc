> 当前实施为 [DELIVERY-12](docs/work-packages/WP-IPC-EXTENSION-WINDOWS-DELIVERY-12.md)：受控扩展、统一部署及同版候选交付。见 [12报告](docs/runtime/extension-windows-delivery-12.md) 与 [扩展指南](docs/extensions/developer-guide.md)。Windows参考验收与真实现场资格单列；下文11及更早结果保持其原版本含义。

# importer-pc

C#/.NET 10 工位运行基座与供包**独立参考产品**：Generic Host、WPF、多视图共享业务、实际SQLite事务/Outbox/恢复、合成读码源和独立持久接收账。不是旧现场工程迁移，未接入厂商SDK或真实WCS。

[当前交付与检查](docs/runtime/extension-windows-delivery-12.md) · [运行/发布/维护](docs/runtime/operations.md) · [UI逐页映射](docs/runtime/ui-baseline-alignment-10.md) · [统一入口](docs/INDEX.md) · [框架设计](docs/design/framework.md)

上一版AUDIT-RECONCILIATION-01从main `856e4de`实施08/10/09，最终运行源码`a08082563bc13b2f146e71c4be9136f0469af9db`。应用历史鉴权、最终配置与实际租约覆盖、配置生效身份、v1.1双通道WPF与受控确认/导航已补齐；关联风险基线和持续双箱参考闭环PASS，完整构建及同版portable/win-x64发布成功。ZIP/模块/配置指纹与包SHA见交付报告。开发分支供main PR审查，不自行合并。

REVIEW-FOLLOWUP-11从main `4ae5319`修复PR9两项P2：波次差异固定常显、箱码一次性焦点交接及代次竞争；新增真实Product组合层三故障验证，证明来源未终止时存储/日志与数据/站点/设备租约保留、晚完成后恰一次释放。冻结源码`4edb909e71097a7da61506beaa10ae5db82ec6cb`，定向E11/E07/E08、完整构建与新同版包/解压启动冒烟PASS；旧包与证据保留。

Windows实际WPF/DPI/双窗/扫码/声音/部署行为 **NOT_RUN**（当前无Windows环境）；真实SDK/WCS/实体/数量/权限及现场能力 **BLOCKED**。源码/交叉编译不代表Windows或现场验收。Storage3旧库需原格式备份、绑定身份回执和显式升级；Windows需维护安装的指定账户/机级策略。Production缺必需能力继续NotReady。

```sh
# 使用global.json固定的.NET SDK 10.0.402与Python3
./scripts/build.sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --extensions
# 独立合成接收端，保持运行；桌面/控制台启动见运行说明
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

[实物UI v1.1](docs/ui/ss-ui-vis-001-v1.1/README.md)原源文件保留，PNG为HTML同源导出而非WPF截图。11形成时资料工具单元测试7项PASS；公开总校验存在与原main完全相同的267项源资产问题（PPTX媒体命名及链接标记），详见交付报告，未修改设计原件或放宽工具换PASS。

仓库public；[公开范围](docs/baseline/PUBLICATION.md)持续有效。只交付获授权通用实现及合成证据，不上传客户原稿、私密来源、凭据、生产数据、SDK或字体。完整客户归档仍为PUBLIC_SUBSET_ONLY；D批准未变，RV-01～17仍OPEN。参考验证不表示旧HTML修复或现场放行；[纠偏前版本](docs/archive/pre-reconciliation-01/README.md)和原失败证据保留。
