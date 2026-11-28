# 多业务架构解耦摸底报告

PR 1 已完成部署与基座声明，PR 2 已完成内置驱动和组件注册，PR 3 已完成共享桌面字段与主题解耦。桌面国际化见[界面语言](framework.md#界面语言)。后续业务核心工作留待拆分 `ParcelFlow` 的到位、提交与原请求收口。[架构基线](framework.md) · [连续供包契约](sm-continuous-induction-closure-01.md)

## 统计口径

以下是 Stage 2 提交 `0bf67b21` 的历史基线。扫描 Git 跟踪的 533 份 C# 和 24 份 XAML。下表按 C# 语法树统计值恰为 `sportmaster` 的字符串字面量，同一文件不同编译分支取并集，相同源码位置只计一次。比较列只计直接的 `==` 和 `!=`，两侧操作数都识别。模式列包括属性模式、`is` 白名单和 `switch` 名称映射。最后一列包括调用参数、默认参数、常量、白名单数组及导出字段。

| 共享工程 | 命中文件 | 字面量 | 直接比较 | 常量模式 | 其他 |
| --- | ---: | ---: | ---: | ---: | ---: |
| Ipc.Application.ParcelFlow | 47 | 148 | 144 | 2 | 2 |
| Ipc.Application.ParcelFlow.Desktop | 27 | 116 | 103 | 4 | 9 |
| Ipc.Platform | 23 | 66 | 49 | 6 | 11 |
| Ipc.Desktop | 5 | 25 | 24 | 0 | 1 |
| Ipc.Assembly | 5 | 16 | 15 | 0 | 1 |
| Ipc.Operations | 2 | 5 | 2 | 0 | 3 |
| **六个工程** | **109** | **376** | **337** | **12** | **27** |

该基线的全 `src/` 共 119 个文件、393 处。其余 17 处分布在 Application.Contracts（3 文件 / 5 处）、Sportmaster（1 / 1）、Sportmaster.Desktop（3 / 8）、Integrations.SportmasterRcs（2 / 2）和 Operations.Desktop（1 / 1）。Contracts 中的热生效比较、待配置连接和字段归属会影响拆分。业务模块声明自己的 ID，以及运维程序校验所读安装的 ID，均需保留身份语义。[配置生效](../../src/Ipc.Application.Contracts/ConfigurationActivation.cs) · [待配置连接](../../src/Ipc.Application.Contracts/ParcelConnectionSetup.cs) · [字段归属](../../src/Ipc.Application.Contracts/SmParameterAudience.cs)

PR 1 在主线基线 `aa1789872c0afe892612cc93074ba79f085f137e` 上完成。本表用 `git grep -o -F '"sportmaster"'` 统计 PR 1 涉及的四个工程。

| 工程 | 基线 | PR 1 完成后 | 余项 |
| --- | ---: | ---: | --- |
| Ipc.Platform | 66 | 1 | 内置产品注册表 |
| Ipc.Operations | 5 | 0 | 从受保护登记和宿主身份取得产品 ID |
| Ipc.Desktop | 25 | 1 | `ParcelApp` 编译期选择 |
| Ipc.Assembly | 16 | 4 | `ParcelAssembly` 编译期选择 1 处，`ParcelCapabilityFactory` 3 处留给 PR 2 |
| 合计 | 112 | 6 | 部署与启动服务中的字面量特判为 0 |

PR 2 以 PR 1 合并提交 `1255586fc8e44d0e20e0f9968b14948d6ea66515` 为基线。

| 核查项 | 基线 | PR 2 完成后 |
| --- | ---: | ---: |
| `Ipc.Assembly` 中 `"sportmaster"` 字面量 | 4 | 1 |
| `ParcelCapabilityFactory.cs` 中 `"sportmaster"` 字面量 | 3 | 0 |
| `ParcelCapabilityFactory.cs` 中 `#if SPORTMASTER` | 14 | 0 |
| `ParcelCapabilityFactory.cs` 中 `#if !DIRECTPLC` | 4 | 0 |
| `ParcelCapabilityFactory.cs` 中 `#if DIRECTPLC` | 1 | 0 |
| `Ipc.Integrations.SportmasterRcs` 中 `"sportmaster"` 业务 ID 比较 | 2 | 0 |
| WcsToPlc 装配链接的 RCS 源文件 | 7 | 0 |

编译期产品选择保留在装配根。`ISmHeartbeatControl`、`sportmaster-rcs` 和持久化角色名继续表达协议与存量数据身份。注册和扩展能力仍按现有精确标识、资格、租约和程序集指纹核验。RCS 组件按实际绑定和有效冻结报文核验，不再依赖宿主的业务 ID。发送资源按工位轮转，保留七个目的调用并发、每位 128 个等待上限和现有左右等待数。新增相机或协议组件时实现对应 Provider，在装配根注册并用组件开关声明项目引用。[能力注册契约](../../src/Ipc.Assembly/CapabilityRegistry.cs) · [装配根](../../src/Ipc.Assembly/ParcelAssembly.cs) · [DB14 格式校验](../../src/Ipc.Application.ParcelFlow/ParcelStorageFormat.cs)

PR 3 以 PR 2 合并提交 `35f55438ca2248a8190b49b41c77654591e02d11` 为基线。

| 核查项 | 基线 | PR 3 完成后 |
| --- | ---: | ---: |
| `Ipc.Application.ParcelFlow.Desktop` C# 中的 `"sportmaster"` 字面量 | 116（27 文件） | 1（集中注册表） |
| 历史页 XAML 中的 `"sportmaster"` 条件 | 3 | 0 |
| `ProductDeploymentProfile` 中暂存的桌面属性 | 5 | 0 |

桌面配置表声明首配步序、设置布局和字段投影，未知标识保守回退。工作站和运维使用同一套 WPF 模板，通过调色板加载浅色与深色。OEM 主题提供完整语义键的资源字典，再在该业务的 `SupportedThemes` 声明。主题保存限于用户偏好文件。[桌面配置表](../../src/Ipc.Application.ParcelFlow.Desktop/BusinessDesktopProfile.cs) · [主题管理器](../../src/Ipc.Presentation.Wpf/WorkstationThemeManager.cs)

## 七个候选边界

表中接口名仅供后续 PR 讨论。已有契约能承载的变化优先落到现有类型。多个边界共享同一次提交的地方，调用位置、锁和事务需一起审查。

| 边界及候选入口 | 由业务决定 | 继续由原所有者保证 |
| --- | --- | --- |
| 拓扑 · `IStationTopologyPolicy` | 读头角色、本位启用条件、允许的能力及依赖。沿用 `FlowDefinition.PositionRoles` 和 `IParcelCapabilityFactory`，装配根选择实现 | 工厂核验实际序列号、SDK 生命周期和可释放性。运行时持有同一份设备会话 |
| 轮次 · `IArrivalBoundaryPolicy` | SM 的到位代次、可信空盘、人工取下、相机与人工输入竞争，以及单工位空闲校准的准入 | Application 在原 gate 和事务内结束本轮、撤下操作资格并保留未确认发送围栏。驱动只提供带时间与身份的帧 |
| 协议 · `IBusinessIntegrationAdapter` | 内置 RCS 的成员、车号与回包归属、精确期限、哪些事实能证明已绑定或取消。优先使用已有 WCS 与心跳端口 | 原请求冻结的编号、报文、目标、调用和回执只进原账。不自动重发。批准协议结果不等于完成物理动作 |
| 参数 · `IParameterAudiencePolicy` | 字段归属、可见性、默认值和候选可热替换的范围。首配与日常编辑读同份声明 | Application 保存完整候选并核验 CAS 与实际责任。Windows 用户偏好沿原分区保存。WPF 持有未保存草稿 |
| 工作区 · `IOperationsWorkspaceProvider` | 已装业务的视图、命令清单、日志展示和快照投影 | 工作站持有设备和原作业。运维进程验证安装、RuntimeId、会话和 Windows SID，执行时复核授权 |
| 部署 · 产品部署声明 | `IDeploymentShortcutPolicy` 只描述系统入口和受控自启。产品另行声明支持的维护动作，同版本覆盖和回滚资格须以原登记为依据 | 平台验证介质、ACL、实例、原记录与有限路径，持锁提交。产品无法提供任意可执行路径或扩大清理范围 |
| 历史与恢复 · `FlowDefinition` 的终态与保管规则 | SM 的格口分配何时结案、已离位 Unknown 的历史口径、按班次统计和可清理记录 | DB14 格式校验、WAL 一致性、编号高水位、恢复引用和备份集归属仍由存储维护。保管与释放发送围栏是两个判断 |

三个入口决定了拆分的先后顺序。共享设置页 `ParameterSettingsControl.Policy` 的 21 处判断集中在 `BuildSystem`（15）和 `BuildBusiness`（6），适合先迁移字段归属。作业入口 `ParcelCommands` 的 17 处中有 11 处在 `SnapshotAsync`。将快照投影交给工作区之后，命令执行仍需在 Application 核对修订。运行时 `ParcelRuntime` 的 21 处中有 12 处在 `StartCoreAsync`，其余跨越入队、可信观察、保存和原请求准备，应分别验证停机重开及原请求归属。

## 四个已有非 SM 模块

按 Stage 2 源树的物理行统计，含注释和空行。范围为同名 Application、Contracts 和 Desktop 目录，排除共享依赖、项目配置、锁文件及构建产物。Supply.Desktop 源码由 Ipc.Desktop 通过 MSBuild 链接编译，统计一次。

| 模块 | C# 文件 / 行 | XAML 文件 / 行 | 实际接入与建议 |
| --- | ---: | ---: | --- |
| DirectPlc | 3 / 60 | 1 / 8 | 单一 PLC 阶段，应用 34 行，界面 C# 26 行。继承 `FlowDefinition`，复用 `ParcelRuntime` 和 `ShellWindow`。按 PLC 物理完成或业务拒绝结案。适合作为最小 Parcel 流程样板。当前作业台含模拟输入，不能据此推断现场 PLC 已验收。[应用](../../src/Ipc.Application.DirectPlc/Application.cs) · [桌面壳](../../src/Ipc.Application.DirectPlc.Desktop/Presentation.cs) |
| WcsToPlc | 3 / 79 | 1 / 8 | 应用 53 行，界面 C# 26 行。先取得 WCS 的有效格口，再准备 PLC。可从持久回执重建主状态。适合作为多阶段依赖、拒绝和恢复样板。默认及可选配置的现行兼容集合使用 `reference-integration`，所以 WcsToPlc 关闭 `IncludeSportmasterRcs`，产物中没有 RCS 源码或组件程序集。SM 通过项目引用交付独立 RCS 程序集，由组件嵌入参数描述。[应用](../../src/Ipc.Application.WcsToPlc/Application.cs) · [兼容声明](../../config/compatibility/wcs-to-plc.json) · [装配条件](../../src/Ipc.Assembly/Ipc.Assembly.csproj) |
| Supply | 46 / 4,848 | 3 / 168 | 有自己的 `ProductRuntime`、`StationApplication`、`SupplyStore`、配置与桌面。应用、契约、桌面分别有 13、11、22 个 C# 文件。平台端口有复用，业务账和命令处理独立于 ParcelFlow。暂保留整套参考实现。未来可提取能力消费者检查，单独评审 UI 或账本重构。Parcel 装配仍借用它读取已登记扩展包的消费者，直接归档会破坏现有调用。[供包宿主](../../src/Ipc.Assembly/SupplyAssembly.cs) · [业务账](../../src/Ipc.Application.Supply/Store.cs) · [两种消费者格式](../../src/Ipc.Assembly/ExtensionConsumerReaders.cs) |
| Inspection | 2 / 98 | 0 / 0 | 直接实现 `IBusinessRuntime` 和 `IBusinessApplication`，仅接受 Simulation，使用 schema1 配置和 `inspection.json` 账本。Assembly 可选，桌面构建选项未接入。适合作为平台契约的独立验证对象，保留暂停开发状态。只有明确现场产品需求后才评估生产存储与 UI。[运行时](../../src/Ipc.Application.Inspection/InspectionRuntime.cs) · [装配](../../src/Ipc.Assembly/InspectionAssembly.cs) |

## 四个增量 PR

各步均保留编译期选择并单独合并。每个 PR 都运行锁定依赖的 SM Desktop 构建、完整 Core 回归和文档门禁。触及的业务装配单独构建。下面列出每步最能揭示行为变化的附加对照。

1. **部署与基座声明，已完成。** 平台集中声明产品部署能力，启动、安装和独立运维核验同一产品 ID。保留原登记键、受保护路径、快捷方式、恢复凭据和正式／测试实例的权限边界。对照 `--shortcut-lifecycle`、`--installation-resource-boundary`、离线部署回归及 Operations.Verification。[产品部署声明](../../src/Ipc.Platform/ProductDeploymentProfile.cs)
2. **集中装配与能力选择，已完成。** 装配根按产品注册读头、相机发现、协议和可选硬件提供者。三套业务分别编译，保持 SM 内置 MVID/MVS 和 RCS 的依赖闭包。对照注册表用例、`--sm-built-in-only`、`--sm-camera-reconfiguration`、`--version-compatibility` 和驱动回归。
3. **迁移字段与页面选择。** 从共享设置页和偏好草稿移出 SM 字段归属，由已选业务向工作站和运维视图传入声明。对照旧配置、完整候选和冻结请求的序列化结果、并发保存与偏好部分保存失败后的重试。运行 `--sm-configuration-preferences`、`--sm-parameters` 及 WPF 字体门禁。
4. **抽出轮次与协议决策。** 将基于原状态和证据的判断迁到 SM 模块，保留原调用点的提交事务、锁顺序、I/O 和回执持久化。对照同车下一件、A→B→A、左右位并行、取件后迟到回包、原生预览及单工位校准。重开应用仍须核对原请求、Unknown 和原编号。

核心入口按实际领域分组。[平台](../../tests/Ipc.Core.Tests/PlatformRegression.cs) · [SM](../../tests/Ipc.Core.Tests/SportmasterRegression.cs) · [驱动](../../tests/Ipc.Core.Tests/DriversRegression.cs) · [部署](../../tests/Ipc.Core.Tests/DeploymentRegression.cs) · [工业 CI](../../.github/workflows/industrial-core.yml) · [SM 交付 CI](../../.github/workflows/sportmaster-commercial.yml)

## 逐文件解耦地图

每个命中文件只列一次。职责列说明迁移时需要一起审查的判断，跨边界处同时列出。入口列给出一至两个可直接搜索的成员，次数按该文件全部字面量计算。可用 `git grep -n -F '"sportmaster"' -- src` 定位原文，再按上述语法口径排除注释和项目外引用。

### Ipc.Application.ParcelFlow

| 源文件 | 次数 | 入口举例 | 边界与职责 |
| --- | ---: | --- | --- |
| [ConfigurationSubmission.cs](../../src/Ipc.Application.ParcelFlow/ConfigurationSubmission.cs) | 1 | `TestBarcodeFilterAsync` | 参数 · 草稿试算的缺省条码筛选，与首配试算共用业务规则 |
| [ParcelAutomaticCameraReads.cs](../../src/Ipc.Application.ParcelFlow/ParcelAutomaticCameraReads.cs) | 3 | `RecordCapabilityHealth`、`AutomaticCameraReadBlock` | 轮次 / 协议 · 根据本位到位、RCS 健康和来源会话决定是否允许生产识码 |
| [ParcelBackupSets.cs](../../src/Ipc.Application.ParcelFlow/ParcelBackupSets.cs) | 2 | `DeleteBackupSetAsync`、`RequireBackupSetBoundary` | 部署 / 历史与恢复 · 删除前锁定恢复引用，核对备份集和关联安装边界 |
| [ParcelCameraInspection.cs](../../src/Ipc.Application.ParcelFlow/ParcelCameraInspection.cs) | 2 | `InitializeCameraInspection`、`BeginCameraInspectionAsync` | 轮次 · 相机检查与校准的会话登记和准入 |
| [ParcelCameraReadFeedback.cs](../../src/Ipc.Application.ParcelFlow/ParcelCameraReadFeedback.cs) | 1 | `RegisterCameraReadFeedback` | 轮次 · 把读码反馈绑定到原相机会话 |
| [ParcelCapabilityInventory.cs](../../src/Ipc.Application.ParcelFlow/ParcelCapabilityInventory.cs) | 1 | `ValidateCapabilityInventory` | 拓扑 · 处理待配置的内置 RCS 能力 |
| [ParcelCommands.cs](../../src/Ipc.Application.ParcelFlow/ParcelCommands.cs) | 17 | `SnapshotAsync`、`ExecuteCommandAsync` | 轮次 / 参数 / 工作区 · 快照、命令路由、单工位校准、配置保存与停止跨多个入口，共用原运行实例 |
| [ParcelConfigurationAudit.cs](../../src/Ipc.Application.ParcelFlow/ParcelConfigurationAudit.cs) | 1 | `DiscoverCamerasAsync` | 拓扑 · 保存相机草稿前按所选引擎枚举设备 |
| [ParcelConfigurationRules.cs](../../src/Ipc.Application.ParcelFlow/ParcelConfigurationRules.cs) | 5 | `Validate` | 拓扑 / 参数 · 校验运行策略、角色及可配置的连接 |
| [ParcelConsumers.cs](../../src/Ipc.Application.ParcelFlow/ParcelConsumers.cs) | 3 | `InspectRequiredPackages`、`ValidateConsumerConfiguration` | 协议 / 历史与恢复 · 冻结配置的组件依赖与已完成原作业的消费责任 |
| [ParcelGuards.cs](../../src/Ipc.Application.ParcelFlow/ParcelGuards.cs) | 8 | `CameraPositionBlock`、`CameraAdmissionBlock` | 轮次 / 拓扑 · 本位相机及车辆是否具备提交资格。仍需核对公共资源 |
| [ParcelHealth.cs](../../src/Ipc.Application.ParcelFlow/ParcelHealth.cs) | 1 | `CheckRcsHeartbeatAsync` | 协议 · 手动心跳的准入与当前 WCS 端口 |
| [ParcelHistory.cs](../../src/Ipc.Application.ParcelFlow/ParcelHistory.cs) | 5 | `UnresolvedCapacity`、`CapacityAvailable` | 历史与恢复 · 容量按当前轮次计算，结案归档按原责任判定 |
| [ParcelInitialConfiguration.cs](../../src/Ipc.Application.ParcelFlow/ParcelInitialConfiguration.cs) | 3 | `InitialRepairResponsibilitiesClear`、`TestBarcodeFilterAsync` | 参数 / 历史与恢复 · 首配试算、检查和原作业责任核验 |
| [ParcelInputs.cs](../../src/Ipc.Application.ParcelFlow/ParcelInputs.cs) | 3 | `RecordInput`、`LatestWorkCardOperation` | 轮次 / 工作区 · 输入记录归属本轮，工作卡按当前件或上一件投影 |
| [ParcelLiveDevices.cs](../../src/Ipc.Application.ParcelFlow/ParcelLiveDevices.cs) | 2 | `ApplyDevicesLiveAsync` | 参数 / 轮次 · 热替换时注册相机检查、恢复受控识码 |
| [ParcelMaintenance.cs](../../src/Ipc.Application.ParcelFlow/ParcelMaintenance.cs) | 2 | `InspectMaintenanceCore` | 历史与恢复 · 离线检查区分已历史保管的 Unknown 与待办责任 |
| [ParcelManagementWorkspace.cs](../../src/Ipc.Application.ParcelFlow/ParcelManagementWorkspace.cs) | 1 | `SetManagementWorkspaceAsync` | 工作区 / 轮次 · 管理窗口的暂停范围、调试所有权和退出恢复 |
| [ParcelManualInputReadiness.cs](../../src/Ipc.Application.ParcelFlow/ParcelManualInputReadiness.cs) | 2 | `IsManualParcel`、`ManualInputReasons` | 轮次 · 人工输入按作业来源、投递位及到位状态准入 |
| [ParcelManualRcsDebug.cs](../../src/Ipc.Application.ParcelFlow/ParcelManualRcsDebug.cs) | 1 | `PrepareManualRcsRequestAsync` | 协议 · 独立调试取格口请求的准备与身份 |
| [ParcelObsoleteSmOriginals.cs](../../src/Ipc.Application.ParcelFlow/ParcelObsoleteSmOriginals.cs) | 1 | `ClassifyIncompatibleOriginals` | 协议 / 历史与恢复 · 按冻结配置和指纹识别无法续办的旧请求 |
| [ParcelOperatorInput.cs](../../src/Ipc.Application.ParcelFlow/ParcelOperatorInput.cs) | 1 | `SubmitParcelBarcodeAsync` | 轮次 · PDA 与键盘提交竞争本轮唯一提交权 |
| [ParcelOperatorProjection.cs](../../src/Ipc.Application.ParcelFlow/ParcelOperatorProjection.cs) | 1 | `WorkstationConfiguration` | 参数 / 工作区 · 供包员配置视图的可见字段 |
| [ParcelPictures.cs](../../src/Ipc.Application.ParcelFlow/ParcelPictures.cs) | 3 | `InitializeParcelPictures`、`ReadParcelPictureCoreAsync` | 工作区 / 轮次 · 为本业务保存和读取可归属的原图 |
| [ParcelPlacement.cs](../../src/Ipc.Application.ParcelFlow/ParcelPlacement.cs) | 2 | `PlacementRemovalOperation`、`ConfirmPlacementRemoval` | 轮次 · 按本位、本轮和版本确认取件 |
| [ParcelPositionControl.cs](../../src/Ipc.Application.ParcelFlow/ParcelPositionControl.cs) | 7 | `ReaderSupplyActive`、`PositionSupplyEnabled` | 拓扑 / 轮次 · 按位启停、读头成员和本位历史 |
| [ParcelProductMaintenance.cs](../../src/Ipc.Application.ParcelFlow/ParcelProductMaintenance.cs) | 1 | `Preview` | 部署 · 隔离恢复标记对回滚与清理预检的影响 |
| [ParcelReceiptCustody.cs](../../src/Ipc.Application.ParcelFlow/ParcelReceiptCustody.cs) | 1 | `ExternalWorkComplete` | 历史与恢复 / 协议 · 按业务终态核对外部执行及回执责任 |
| [ParcelRestoreHandoff.cs](../../src/Ipc.Application.ParcelFlow/ParcelRestoreHandoff.cs) | 4 | `AdoptRestoreOwnedAsync` | 部署 / 历史与恢复 · 恢复接管时核对受保护安装与保管责任 |
| [ParcelRestoreOperations.cs](../../src/Ipc.Application.ParcelFlow/ParcelRestoreOperations.cs) | 2 | `OriginalRestoreIntents`、`ReadRestoreOperationsAsync` | 部署 · 从部署原记录读取恢复意图 |
| [ParcelRuntime.cs](../../src/Ipc.Application.ParcelFlow/ParcelRuntime.cs) | 21 | `StartCoreAsync`、`Enqueue` | 拓扑 / 轮次 / 协议 · 启动、输入交付、可信观察、原请求准备和保存分支汇集处，应按事务逐个迁出 |
| [ParcelSmArrival.cs](../../src/Ipc.Application.ParcelFlow/ParcelSmArrival.cs) | 7 | `PrepareSmHeartbeatMaintenance`、`CompleteSmHeartbeatMaintenance` | 协议 / 轮次 · 把心跳成员与有效车号映射到本位到位代次，维护发送资格 |
| [ParcelSmRounds.cs](../../src/Ipc.Application.ParcelFlow/ParcelSmRounds.cs) | 3 | `MaintainSmRounds`、`RestoreSmRounds` | 轮次 · 创建、恢复、结束本位轮次及查询历史 |
| [ParcelSmUnknownHistory.cs](../../src/Ipc.Application.ParcelFlow/ParcelSmUnknownHistory.cs) | 2 | `RetainReleasedSmUnknowns`、`IsSmUnknownHistoryCandidate` | 历史与恢复 / 协议 · 已离位 Unknown 的保管分类与运行集合移出 |
| [ParcelSnapshotCache.cs](../../src/Ipc.Application.ParcelFlow/ParcelSnapshotCache.cs) | 1 | `RefreshArchiveCountAsync` | 历史与恢复 · 按当前业务口径投影已归档数量 |
| [ParcelStages.cs](../../src/Ipc.Application.ParcelFlow/ParcelStages.cs) | 5 | `ExecuteStageAsync`、`LogRcsResult` | 协议 / 历史与恢复 · WCS 调用资源、结果记录及本地证据恢复 |
| [ParcelStartupMaintenance.cs](../../src/Ipc.Application.ParcelFlow/ParcelStartupMaintenance.cs) | 3 | `ReadState`、`RestoreAsync` | 部署 / 历史与恢复 · 离线启动态、备份配置和隔离恢复的业务身份 |
| [ParcelStateJournal.cs](../../src/Ipc.Application.ParcelFlow/ParcelStateJournal.cs) | 3 | `WriteStationState` | 工作区 · 工作站状态日志的采集条件 |
| [ParcelStorageFormat.cs](../../src/Ipc.Application.ParcelFlow/ParcelStorageFormat.cs) | 1 | `RequireCurrentStorage` | 历史与恢复 · DB14 原始业务标识白名单、版本、基线摘要与表结构校验 |
| [ParcelTerminalRecovery.cs](../../src/Ipc.Application.ParcelFlow/ParcelTerminalRecovery.cs) | 3 | `HasTerminalRecoveryBasis` | 历史与恢复 / 协议 · 已分配格口能否作为原作业终态的恢复依据 |
| [ParcelUnadmittedPictures.cs](../../src/Ipc.Application.ParcelFlow/ParcelUnadmittedPictures.cs) | 3 | `ContinuingBlockedObservation`、`DisplayedInput` | 轮次 / 工作区 · 拒绝期间的连续观察、图片与原输入的归属 |
| [StationAutomaticCleanup.cs](../../src/Ipc.Application.ParcelFlow/StationAutomaticCleanup.cs) | 1 | `TryAutomaticCleanup` | 历史与恢复 · 自动清理的业务策略及准入 |
| [StationDiagnosticPackage.cs](../../src/Ipc.Application.ParcelFlow/StationDiagnosticPackage.cs) | 3 | `ReadDiagnosticProductInformationAsync`、`ExportDiagnosticOverviewAsync` | 工作区 · 普通用户可见的产品信息、诊断摘要及导出 |
| [StationExceptions.cs](../../src/Ipc.Application.ParcelFlow/StationExceptions.cs) | 1 | `ReadExceptionsAsync` | 工作区 / 历史与恢复 · 业务异常查询能力 |
| [StationFactProjection.cs](../../src/Ipc.Application.ParcelFlow/StationFactProjection.cs) | 1 | `ProjectStationRequestCore` | 工作区 / 协议 · 从原请求投影运营事实，排除调试请求 |
| [StationSessionStatistics.cs](../../src/Ipc.Application.ParcelFlow/StationSessionStatistics.cs) | 1 | `ReadSessionStatisticsAsync` | 工作区 / 历史与恢复 · 班次统计查询能力 |
| [StationStatistics.cs](../../src/Ipc.Application.ParcelFlow/StationStatistics.cs) | 2 | `ReadStatisticsAsync`、`SaveOperationsPolicyAsync` | 工作区 / 参数 · 运营统计与统计策略保存 |

### Ipc.Application.ParcelFlow.Desktop（PR 3 前逐文件基线）

| 源文件 | 次数 | 入口举例 | 边界与职责 |
| --- | ---: | --- | --- |
| [BackupSetsWindow.cs](../../src/Ipc.Application.ParcelFlow.Desktop/BackupSetsWindow.cs) | 1 | `BackupSetsWindow` | 部署 / 工作区 · 备份集选择窗口的缺省应用参数 |
| [CapabilityEditing.cs](../../src/Ipc.Application.ParcelFlow.Desktop/CapabilityEditing.cs) | 2 | `BuildReader`、`LoadCapabilitiesAsync` | 拓扑 / 参数 · 读头绑定的行标识、可编辑的设备能力 |
| [ChineseInterfaceText.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ChineseInterfaceText.cs) | 1 | `BusinessName` | 工作区 · 应用标识到界面名称的映射 |
| [ConfigurationActivationPresentation.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ConfigurationActivationPresentation.cs) | 1 | `ConfigurationActivationOutcome` | 参数 · 热生效失败时按业务提供处置说明 |
| [ConfigurationReviewPresentation.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ConfigurationReviewPresentation.cs) | 5 | `HasPreferenceSaveFailure`、`ConfigurationActivationGuidance` | 参数 · 展示偏好保存状态、签名及生效方式 |
| [ConfigurationSaveReviewWindow.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ConfigurationSaveReviewWindow.cs) | 1 | `UpdateState` | 参数 · 保存确认按钮与自动生效反馈 |
| [FaultGuidance.cs](../../src/Ipc.Application.ParcelFlow.Desktop/FaultGuidance.cs) | 2 | `NextAction` | 工作区 · 按业务解释 Unknown 等故障的操作后果 |
| [HistoryPage.xaml.cs](../../src/Ipc.Application.ParcelFlow.Desktop/HistoryPage.xaml.cs) | 1 | `FitQueryViewport` | 工作区 · 按产品及可用高度安排历史页标题 |
| [HistoryTasks.cs](../../src/Ipc.Application.ParcelFlow.Desktop/HistoryTasks.cs) | 1 | `ArrangeTaskFilters` | 工作区 · 作业查询筛选项布局 |
| [MaintenancePage.xaml.cs](../../src/Ipc.Application.ParcelFlow.Desktop/MaintenancePage.xaml.cs) | 1 | `AdvancedToggle_SubmenuOpened` | 工作区 / 参数 · 高级菜单及可见的设置入口 |
| [ParameterEditorModel.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterEditorModel.cs) | 9 | `Readers`、`Components` | 参数 / 拓扑 · 按业务创建相机草稿、筛选组件并校验字段 |
| [ParameterRuntimePresentation.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterRuntimePresentation.cs) | 2 | `ParameterConnection` | 参数 · 连接设置与采集方式的说明 |
| [ParameterSettingsControl.Capabilities.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.Capabilities.cs) | 9 | `BuildCapabilities`、`Capability` | 拓扑 / 参数 · 相机、角色和能力编辑控件 |
| [ParameterSettingsControl.FirstReadiness.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.FirstReadiness.cs) | 1 | `UsesFirstReadiness` | 参数 · 首配完成页是否展示就绪检查 |
| [ParameterSettingsControl.Navigation.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.Navigation.cs) | 3 | `OrderedPages`、`FirstCategories` | 参数 · 字段分组、导航顺序与首配步骤 |
| [ParameterSettingsControl.Policy.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.Policy.cs) | 21 | `BuildSystem`、`BuildBusiness` | 参数 · 系统和业务字段、缺省说明及显示条件 |
| [ParameterSettingsControl.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.cs) | 5 | `SectionLayout`、`ShowPage` | 参数 · 设置页布局及区段选择 |
| [ParcelPresentation.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ParcelPresentation.cs) | 1 | `SetViewport` | 工作区 · 根据工作站布局设置视口恢复状态 |
| [ParcelTargets.cs](../../src/Ipc.Application.ParcelFlow.Desktop/ParcelTargets.cs) | 2 | `CapacityState`、`AppendHistory` | 工作区 / 历史与恢复 · 作业容量与历史记录的显示口径 |
| [PersistentConfigurationDrafts.cs](../../src/Ipc.Application.ParcelFlow.Desktop/PersistentConfigurationDrafts.cs) | 3 | `SaveIncompleteEditorAsync`、`ContinueIncompleteEditorAsync` | 参数 · 未完成草稿保存、续办时带回用户偏好 |
| [SmDisplayPreferenceDraft.cs](../../src/Ipc.Application.ParcelFlow.Desktop/SmDisplayPreferenceDraft.cs) | 7 | `SmDisplayPreferenceDraft`、`SaveOne` | 参数 · 个人窗口、节拍、音量等偏好的持久化分区键 |
| [StartupConfigurationDraft.cs](../../src/Ipc.Application.ParcelFlow.Desktop/StartupConfigurationDraft.cs) | 8 | `StartupConfigurationDraft`、`CreateParameterEditor` | 参数 / 拓扑 · 首配候选、读头草稿及字段恢复 |
| [StartupWorkflowWindow.cs](../../src/Ipc.Application.ParcelFlow.Desktop/StartupWorkflowWindow.cs) | 2 | `StartupWorkflowWindow` | 工作区 · 启动窗口的业务缺省参数和窗口偏好 |
| [StationDisplayPreferences.cs](../../src/Ipc.Application.ParcelFlow.Desktop/StationDisplayPreferences.cs) | 2 | `HasUnsavedDisplayPreferences` | 参数 · 日常设置的未保存状态，与业务配置修订分开 |
| [StructuredConfiguration.cs](../../src/Ipc.Application.ParcelFlow.Desktop/StructuredConfiguration.cs) | 15 | `ApplyParameters`、`From` | 参数 / 拓扑 · 角色、参数、图片策略和完整候选的往返投影 |
| [WindowOpeningPreference.cs](../../src/Ipc.Application.ParcelFlow.Desktop/WindowOpeningPreference.cs) | 3 | `Attach`、`PreferenceChanged` | 工作区 / 参数 · 全屏窗口恢复与偏好变更 |
| [WindowPreferenceDraft.cs](../../src/Ipc.Application.ParcelFlow.Desktop/WindowPreferenceDraft.cs) | 7 | `HasUnsavedWindowPreference`、`WindowModeDraft` | 参数 · 个人窗口方式的装载、草稿与保存 |

### PR 2 后的装配与平台字面量

| 源文件 | 次数 | 入口举例 | 边界与职责 |
| --- | ---: | --- | --- |
| [ProductDeploymentProfile.cs](../../src/Ipc.Platform/ProductDeploymentProfile.cs) | 1 | `ProductDeploymentCatalog` | 已声明产品 ID 和部署能力 |
| [ParcelApp.xaml.cs](../../src/Ipc.Desktop/ParcelApp.xaml.cs) | 1 | `ApplicationId` | 编译期工作站业务标识及窗口装配 |
| [ParcelAssembly.cs](../../src/Ipc.Assembly/ParcelAssembly.cs) | 1 | `InstalledBusiness.Create` | 编译期将产品标识绑定到实际 Flow 和运行时工厂 |
