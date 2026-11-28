# 平台与业务模块的架构基线

Importer PC 使用 C#、.NET 10、WPF 和 SQLite。构建时由产品和装配根选定业务及其依赖，Windows 工作站持有运行中的业务实例。当前交付 SM；DirectPlc、WcsToPlc、Supply 和 Inspection 保留各自的已有装配。开发范围及持久化安全约束见[工程规则](../../AGENTS.md)。

| 职责 | 实现位置 | 谁作决定 |
| --- | --- | --- |
| 产品、构建与装配 | Ipc.Product、Ipc.Assembly | 选择 Application、端口实现和依赖闭包，构造宿主并绑定资源 |
| 平台与存储 | Ipc.Platform、Ipc.Platform.Storage | 提供机器与用户身份、受保护路径、部署登记、资源所有权、SQLite 写入及恢复机制 |
| 共享供包应用逻辑与具体业务 | Ipc.Application.Contracts、Ipc.Application.ParcelFlow、Ipc.Application.Sportmaster 等 | 应用声明拓扑、角色、准入规则、状态机和业务账，执行命令时核对权限与版本 |
| 设备、协议 | Ipc.Drivers.Hikrobot.Mvs、Ipc.Integrations.SportmasterRcs 等 | 驱动占用及释放真实设备，集成核对协议响应，向 Application 返回可归属的证据 |
| WPF 与运维 | Ipc.Presentation.Wpf、业务 Desktop、Ipc.Operations | 投影带身份和版本的状态，保存本窗草稿；独立运维进程经本机 IPC 访问工作站 |

## 现状与下一阶段的边界

PR 1 已把部署与基座层的产品能力收敛到 `ProductDeploymentCatalog`。编译期装配选定业务 ID，平台、启动安装壳和独立运维据此读取部署声明。运行时仍逐项核验受保护安装登记、介质、程序及数据所有权。声明只供执行时查询，原登记、发布清单和业务账继续使用既有格式与身份。未知产品不具备任何已声明能力。[产品部署声明](../../src/Ipc.Platform/ProductDeploymentProfile.cs) · [部署快捷方式](../../src/Ipc.Platform/DeploymentShortcuts.cs)

PR 2 已把内置驱动和协议组件的选择收敛到装配根。产品能力策略决定外置扩展、模拟读头和热替换的准入，注册表按现有驱动 ID 或实现 ID 路由。工厂核验版本与协议、持有资源租约，并把成功构造的端口交给应用。热替换候选释放失败时保留原对象及租约，停机继续收口。RCS 组件验证实际协议、绑定和冻结报文，发送资源按工位轮转，总并发上限为七，每位最多等待 128 个请求。工作站字段归属见下文。轮次契约留在 PR 4。[装配根](../../src/Ipc.Assembly/ParcelAssembly.cs) · [能力注册契约](../../src/Ipc.Assembly/CapabilityRegistry.cs) · [解耦清点](multi-business-extraction-audit.md)

PR 3 已将桌面身份、默认入口、设置页顺序和字段可见性集中到桌面配置表。部署声明及安装权限仍由平台层核验。工作站与独立运维启动时加载当前 Windows 用户的主题，保存后替换现有窗口的调色板，业务配置版本和设备会话继续由原应用持有。[桌面配置表](../../src/Ipc.Application.ParcelFlow.Desktop/BusinessDesktopProfile.cs) · [主题管理器](../../src/Ipc.Presentation.Wpf/WorkstationThemeManager.cs)

`Workstation.xaml` 负责控件结构、44 DIP 触摸控件和焦点样式。默认浅色和工业深色共享语义资源键及随包 Noto Sans CJK SC 400 / 500 字体。新增 OEM 主题时，在 `Themes/Palettes/` 增加包含完整语义键和对应资源类型的字典，并在产品的 `SupportedThemes` 声明其 ID、名称和 WPF 资源 URI。验收覆盖现场文字颜色、键盘焦点和同一窗口热替换。[共享结构](../../src/Ipc.Presentation.Wpf/Themes/Workstation.xaml) · [浅色调色板](../../src/Ipc.Presentation.Wpf/Themes/Palettes/IndustrialLight.xaml) · [深色调色板](../../src/Ipc.Presentation.Wpf/Themes/Palettes/IndustrialDark.xaml)

通用平台层不得新增 `Id == "sportmaster"` 或等价的业务特判。业务决定能做什么，平台执行有明确输入、身份和失败语义的操作。具体例子是由应用判定本轮是否允许提交，由存储机制原子提交应用给出的身份及事实；由装配确定快捷方式用途，由部署机制核验实际安装、目标和所属用户。引入新的共享能力要有实际消费者，不以规划中的业务替换已有装配。

### 界面语言

安装器启动时按 Windows 界面语言预选简体中文、English 或 Русский。静默安装用 /quiet --lang ru-RU 等显式指定语言。无效参数返回 87。安装确认提交后，将语言写入安装调用账户的个人偏好。由系统或服务账户发起的安装保留安装结果，由各 Windows 用户自行选择工作站语言。

在供包台设置的「3. 显示与提示音」选择界面语言，使用底部「保存设置」立即应用。首配也可选择，独立运维启动时读取同一 Windows 用户的偏好，并监听该文件的更新。默认语言为 zh-CN，损坏文件保留，界面回退到缺省语言并提示原因。语言与主题分别保存且独立切换，配置修订、设备连接和在途作业继续由原应用持有。

界面文字使用 [三语资源字典](../../src/Ipc.Presentation.Wpf/Localization) 以及动态资源或 UiText 绑定。新增键同时补齐三语及格式参数。条码、格口、故障编号、原始 RCS 回包和日志原文保持原值。使用随包 Noto Sans CJK SC 400 / 500，原生 WPF 验证在 125% DPI 下覆盖西里尔字母、俄语按钮和设置布局。

## 内置适配器接入

1. 为大华、康耐视等读码相机实现 `IControlledObservationSource` 和 `IBuiltInReaderProvider`，在提供者中声明参数、设备身份、组内冲突检查及额外租约。支持局域网发现时一并提供 `ICameraDiscoveryProvider`，明确支持的识码引擎并管理 SDK 所有权。[相机发现契约](../../src/Ipc.Application.Contracts/CameraDiscovery.cs) · [海康提供者](../../src/Ipc.Assembly/BuiltInHikrobotReaderProvider.cs)
2. 电子秤通过 `IControlledWeightSource` 接入，体积相机使用带设备 ID、会话、序号和采集证据的 `IControlledVolumeSource`。实现 `IBuiltInDeviceProvider` 并在需要该端口的产品中注册。当前 SM 的体积接口仅声明契约，应用在正式需要尺寸时再绑定相应角色。[称重端口](../../src/Ipc.Platform/CommonCapabilities.cs) · [体积端口](../../src/Ipc.Platform/VolumeCapabilities.cs)
3. RCS 和 WCS 实现 `IBuiltInIntegrationProvider`，PLC 等现有协议角色通过绑定提供者返回对应类型的端口。接入 MES 时先明确业务所需的操作和可证明结果，再实现适配器与注册声明。产品在 `ParcelAssembly.CreateCapabilityRegistry()` 显式登记提供者及构建依赖。装配工程以 `IncludeHikrobotMvs` 和 `IncludeSportmasterRcs` 开关分别添加项目引用及提供者。SM 默认启用两者，RCS 组件程序集自带参数描述。WcsToPlc 的现行兼容声明使用参考 WCS，其默认产物不含 RCS 组件。[参考提供者](../../src/Ipc.Assembly/BuiltInReferenceProviders.cs) · [RCS 提供者](../../src/Ipc.Assembly/BuiltInSportmasterRcsProvider.cs)

既有驱动 ID、实现 ID、合同、版本、配置和冻结报文继续沿用原值。实现指纹仍从实际程序集及依赖闭包计算，新构建按现行规则核验旧请求的原指纹。

## 一条作业的所有权

1. 驱动提供带会话、次序与采集时间的设备事实。相机采到画面、识读出候选、确认托盘为空分别有独立依据。UI 看见预览不能生成一次业务提交。
2. Application 将输入与本位的放置、到位、作业及配置关联，取得一次提交资格，持久保存请求和原参数。设备或网络等待不能借公共锁占住另一工位的输入。具体边界由[轮次契约](sm-continuous-induction-closure-01.md)定义。
3. 集成按冻结身份执行一次外呼并保存原回包，业务决定能否接受该结果。调用中断、超时及回执落盘失败分别保留责任。新配置、新车、切页均不得使旧请求重新发送。
4. 工作站发布有界快照，UI 按当前身份显示并让操作员查询原记录。运维仅通过声明的观察和控制通道访问工作站，不能从 IPC 断线推定硬件释放或开启第二个业务所有者。[双进程契约](sm-readonly-operations.md)

## 配置与部署

系统、业务、驱动和组件从同一本配置账取得当前修订。保存完整候选并实施 CAS，按资源和业务责任决定可热生效或保留 Pending。恢复已接纳请求时按创建它的原始配置和请求执行。运行数据、配置修订、部署登记、个人偏好分别由所属存储维护。[当前格式](../runtime/version-compatibility-01.md) · [配置保存](../runtime/parameter-inheritance-02.md)

停机、安装、回滚、恢复和清理都要先核验实际所有权。复制或回滚程序不能替换业务库、撤销既有外部效果或覆盖永久编号。恢复已复制的隔离数据，仍须核对原站和原请求，按明确操作启用。[安装生命周期](industrial-lifecycle-01.md) · [正式与测试实例](sm-test-instance-isolation.md)
