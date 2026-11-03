# RCS 参数生效说明与兼容性

2026-11-03，核查起点 main `1dff45ae`，已包含 PR #192（`74bca0e0`）。本次只修正文案并补充两个参数的合成回归，不改变生效判断、资源替换、请求或部署守卫。

## 生效行为

已配置的 SM 内置 MVS 相机、内置 RCS 的参数修改，可与条码规则一起即时生效，只重连变化的设备。`ConfigurationActivation.LiveDevices` 审核完整候选；设备实例、驱动、启停状态不能改变，不能混入其他设置，也不能已有 Pending。

`ParcelRuntime.ApplyDevicesLiveAsync` 执行前再次检查配置版本、确认状态、原请求责任、观察队列、维护、调试读取及组件实现指纹。不满足条件就走原受控重启流程；重启仍受原责任约束。校验失败、编号区间冲突或旧设备无法停止可能直接拒绝保存，不能将回退理解成一定保存成功。以保存回执为准。

`request-timeout-ms` 和 `heartbeat-interval-seconds` 都是 RCS 参数，不能再无条件说明“重启后生效”。共享描述文件也用于 WcsToPlc，因此即时生效说明明确限于 SM。手动“重连”只使用 Effective，不会激活 Pending。首次启用、停用设备及待配置变为已配置仍需受控重启。

## 读取与身份链

| 路径 | 实际用途与影响 |
|---|---|
| `Ipc.Assembly.csproj` → `Ipc.BuiltInRcs.Parameters.json` → `ParcelCapabilityFactory.ReferenceProviders()` | 将 JSON 嵌为资源，再反序列化为 `ParameterField[]`，供目录和参数表单使用。当前配置保存的是 `CapabilityBinding` 和参数值，不保存描述字段。 |
| `ParcelCapabilityFactory.ValidateDeployment()` → `RcsTaskOptions` / `RcsConfigurationValidation` / `ExtensionSchema` | 校验参数、完整候选、心跳映射和当前版本/合同。仅改 Description 不改键、类型、默认值、上下限或必填约束。当前 SM 拒绝外置扩展，不需要配置资格签名。 |
| `BuiltInFingerprint()` → Ipc 程序集依赖闭包文件哈希 | **嵌入文字会改变新构建的二进制指纹**，不能宣称“文案不参与指纹”。`ParcelLiveDevices` 只允许同一正在运行的实现做热替换。 |
| 冻结作业/请求 → `ParcelRuntime.StartAsync` / `ExecuteStageAsync` / `OriginalParcelQuery` | 原实现哈希继续冻结。仍需执行的原责任遇到不同二进制会拒绝启动/执行；旧请求必须用匹配原版本处理，不改写哈希、不迁移 Unknown、不自动重发。已结历史保留原身份。 |
| `BusinessCompatibility.Fingerprint()` → `DeploymentRuntimeCompatibility` | 使用业务程序集内的 `config/compatibility/sportmaster.json`，并校验配置/数据格式、管理协议和依赖库存。本次不修改这些声明、组件 `1.0.0` 或 `SPORTMASTER-RCS-SORTER-v1`；不新增配置或数据库迁移。 |
| `ApplicationReleaseValidation` | 安装库存逐文件验证精确字节。新版本必须按正常发布重新生成库存及适用的代码签名；不能在已安装 DLL 或旧发布中直接替换文字。本次不构建/发布安装介质、不修改当前部署。 |
| 历史 `extension.build.json` → `scripts/extension_build.py` → `ExtensionPackages.VerifyMetadata/Inspect` | 旧外置扩展构建将 JSON 解码进 manifest.Modules.Parameters，manifest 的全部字节受 RSA-PSS/SHA256 签名和 ManifestSha256 约束，源快照也包含 JSON。重建会是不同的包身份。SM 已停用此路径；本次不重建、重签或覆盖任何旧包，不更改历史配置中的精确包引用。 |

因此可以作为正常源码修订交付，无需组件逻辑身份或数据迁移，也不改变程序回退兼容规则。**不能据此直接替换现场程序**：有未结原责任时保留匹配版本，先按既有流程清责；本次没有为新字节授予旧请求执行资格。若将来需要跨实现执行旧请求，必须单独提出方案并取得批准。

## 本次验证

本机 Windows / .NET 10，先执行 `. ./scripts/project-environment.ps1`，数据、缓存和日志均在 D 盘。

- SM 装配根基线构建、修改后的 SM Desktop 和 Application.Verification 构建通过，零警告、零错误。
- 对比 main `1dff45ae` 与本分支 JSON：12 个参数的所有非 Description 字段逐项相同，仅超时与心跳两处 Description 改变；业务兼容声明字节相同，SHA256 为 `660B04A528A547A6DB1AE31499CF34DAB258ABC77EC1D9FDA3C79D2E9107A2D3`。
- 实际 SM 装配程序集 SHA256 从 `AC3A3ACFCAE720E952DD3B8D9E6F350EA19A44F70602F53C65CC04C6BC690EFC` 变为 `288A12BA107C30FC9B21275DED45778BDCEBBEBA75C60C5DB9FE0F723522C186`，印证嵌入文案会改变二进制身份；这些是本地开发构建，不是发布身份。
- `--live-devices-30`：23 项通过，新增两个具体 RCS 参数的 Effective、无 Pending、只重建 RCS、组件版本/合同不变检查；原有混合修改/Pending/Unknown 不替换、不重发断言保留。
- CI 使用的 MSTest 入口定向运行通过：`dotnet test tests/Ipc.Core.Tests --filter 'Name~live-devices-30'`，实际运行 1 个数据用例，执行上述 23 项断言。
- `--configuration-confirmation`、`--rcs-heartbeat`、`--sportmaster-rcs`、`--storage-format-guards` 全部通过，包含原请求/回包、编号区间、受控重启、配置原件保留等既有检查。
- `--version-compatibility`：本分支首次运行在 WcsToPlc 的维护前置守卫被拒绝，未到该项兼容断言；SM 和 DirectPlc 部分已通过。独立原 main 工作树完整通过，本分支不改源码复跑也完整通过。保留首次失败和两次完整结果；该维护时序不稳定未在本次修复，也不宣称首次全绿。

复现应用验证：`dotnet run --project tests/Ipc.Application.Verification -- . --live-devices-30`，其余套件将最后一个选择器替换为上列对应值。原始本机日志在 Git 外 `.artifacts/rcs-description-compat/`，不上传二进制、合成数据库或本机管理凭据。

Windows 原生页面人工操作、受保护安装升级、真实海康 SDK/相机、客户 RCS 和现场验收未运行；合成 SDK、SQLite 和环回 HTTP 不构成现场验收。
