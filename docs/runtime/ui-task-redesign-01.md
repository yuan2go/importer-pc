# Sportmaster V2 · UIR-00～06 实施与运行

实施基准 main `de2b045a6eb66c66cc22a237fb2c4e0f84f98e78`，实现分支 `codex/sportmaster-v2-ui`。本包完成 V2 原生 UI 及必要契约补足，继承 PARAM-01、周期心跳、完整配置 CAS、原请求责任、原生安装及独立维护。精确构建来源、验证结果与介质身份以[本版验收记录](evidence/ui-task-redesign-v2/ACCEPTANCE.md)为准。原型模拟与 Windows/设备验收分别记录，不把新局部验证写成旧 HTML 修复或历史 RV 全部关闭。

## 交付与入口

- [151项逐项迁移表](ui-task-redesign-v2-migration.md)：54项 V2 字段、58项原字段/状态、39项操作保留原编号。
- [同版 WPF 高清界面](../ui/sportmaster-v2-wpf-20261020/README.md)：真实 WPF 控件与合成状态，不是浏览器或设计稿。
- [目标设计](../ui/sportmaster-task-redesign-v2-20261020/README.md)、[实施裁决](../design/sportmaster-task-redesign-01.md)、[完整工作包](../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)保留要求及替代关系。
- [实施前原字节](../archive/pre-ui-task-redesign-implementation-20261020/README.md)保留设计阶段指导；既有历史证据未倒写。

## UIR-00～06 对照

| 包 | 当前实现 | 主要路径与边界 |
|---|---|---|
| UIR-00 | 核对最新 main、工作区和并行 PR；完整查看 V1 48 PNG、V2 67 PNG、两版原型及交接。撤出仅设计/不测试的现行限制，保持有效实现和原件 | INDEX、STATUS、design、工作包、PUBLICATION；历史归档不重新标记为新实现 |
| UIR-01 | 一个业务实例的四导航，左右摘要；四类参数、对象选择、宽屏双列、跨分类搜索、控件和草稿保留 | `ParameterSettingsControl.*`、`SupplyStationWindow.*`、共享 `ShellWindow`。页面脱离视觉树时保持自身 DataContext，避免编辑控件重建 |
| UIR-02 | 真实元数据字段、范围/依赖校验、错误定位；左右站码冲突显式迁移、两种 bcrCode 独立；条码样例有界测试、输入变化令旧结果过期 | `ParameterOwnership`、`ParameterEditorModel`、`CapabilityDraft`。无海康端口字段；未声明能力不补造。关闭指定格口不提交旧值，但保留当前原始草稿 |
| UIR-03 | 单一候选账及编辑来源，完整修改摘要、受控保存、回执核对、Pending/Effective、三选项离开保护、多窗草稿不合并 | 复用 `ConfigurationSubmission` / `PersistentConfigurationDrafts`；`ConfigurationCloseGuard`。保存失败保留输入；未知提交核对原账，不自行重送 |
| UIR-04 | 相机、扫描、RCS任务三组；任务号/SKU/波次/响应数量随同一接受请求整组更新或失效；原责任单独保留 | `SupplyStationViewModel`、`SupplyStationPosition`、`WcsDestinationResult`、RCS解析。窄视口把候选详情放入展开区，保留条码/格口/原责任与操作空间 |
| UIR-05 | 独立五步首配共用模型；记录、诊断、维护、模板差异预览、备份恢复、安装及离线帮助 | `StartupWorkflows`、`StartupConfigurationDraft`、`ProductHelp`、`MaintenancePage`。已配置故障仍进独立维护；不新增部署平台 |
| UIR-06 | 受影响构建、六任务风险验证、原生几何、Windows实际输入、发布启动冒烟及图片溯源 | 结果见本版验收记录。真实设备/SDK/RCS、目标工控机、其他Windows/DPI与现场另验 |

## 参数页的实际操作

顶部显示当前草稿和待重启状态。输入参数名称、英文键或对象名称可搜索；按 Enter 定位第一个结果，Esc 清除搜索，Ctrl+F 回到搜索框。结果落到实际可编辑字段，展开必要的安装详情，不复制另一个编辑副本。分类切换保留无效输入、选择和各对象滚动位置。

系统包含站点标识、数据与记录、计划备份和维护入口。业务模块包含当前 Sportmaster 的条码筛选、请求规则和处理预算。驱动按左右相机组织，取格口映射与驱动心跳编码分别注明。组件显示一个 RCS 连接；系统/业务/驱动中已迁出的字段仅作只读引用并提供跳转。安装版本及稳定设备身份放入可展开详情；更换提供者依旧受精确包和完整候选校验约束。

先检查完整候选，可点击每条错误定位。再查看跨对象修改，打开“保存准备…”填写维护身份与变更原因；软件暂停、授权、原件及资源守卫仍由 Application 检查。底栏“保存修改（待重启）”保持真实准入。需要核对不确定保存时，在保存准备中使用“核对上次保存结果”。常规切页不离开草稿；重读、切编辑来源与关窗按原范围处理。加密保留失败不能放行关闭。

配置/数据目录在运行内只读，来自宿主只读位置投影；宿主未提供时明确提示，不猜当前目录。迁移进入独立维护，保留原目录、完整恢复集与原责任。模板只改变同版白名单策略和精确提供者候选，不带站码、IP、目标、凭据、请求编号、备份目录或业务数据。导入先看差异，原实例参数无损保留，不兼容更换拒绝并保留旧稿。

## 六项商业 UI 质量

| 标准 | 本版可观察表现 |
|---|---|
| 重点清楚 | 当前条码、格口和三组信息分层；新观察、Unknown、拒绝及失效不会沿用旧任务；原责任独立 |
| 组织一致 | 四导航、四类对象、统一字段/错误/搜索；首配复用日常编辑器，操作语义一致 |
| 密度合适 | 作业保留大字号；参数移除重复标题，先显示常用字段，安装细节折叠；对象长名换行，无横向滚动 |
| 细节完整 | 未修改/未保存/校验失败/待重启/未知回执/失效各有状态；保存准备解释授权，空搜索和缺包给出下一步 |
| 操作连贯 | 数据刷新不重建表单、不清输入和选择；慢操作只锁相关提交入口，保留查看、搜索、滚动和编辑 |
| 真实显示可靠 | 实际 WPF 的精确 backing 几何与本机键鼠/缩放分别登记；最小视口保护不放宽，目标工控机验收仍独立 |

## 契约、版本与配置

本版声明：Application.Contracts **1.13.0**，Sportmaster **1.6.0**，Sportmaster RCS **1.3.0**；共享桌面消费者 DirectPlc/WcsToPlc 为 **1.4.3**。RCS 的 `waveNo`、`quantity` 是可空字符串：保留前导零；非字符串非 null 响应拒绝，不推断单位或把请求数量当响应数量。新增空字段省略序列化，旧请求/回执原字节兼容。历史请求固定原报文、合同和目标版本。

Hikrobot ID-MVS 仍为 **1.0.3**（本包未改驱动及其 Platform 契约）。消费新 Application.Contracts 的 reference.parcel-capabilities 声明更新为 **2.2.7**。兼容清单和 composition 已同步；这些版本声明不代表精确签名扩展包已在生产安装。现场须对同版公共契约重新构建、签名和验证扩展闭包，旧包不能仅修改版本号后使用。

`IParcelConfigurationLocationApplication` 仅提供配置/数据目录的只读投影，不成为另一配置或部署权威。Production 缺少必须能力仍拒绝业务。日常页面切换不会创建连接、发送器或心跳循环；RCS 无权威 Query 的限制在诊断中明确显示。心跳 HTTP 交付不等于上游应用确认；软件暂停不替代安全急停。

## 复现与运行

仓库根目录执行（替换为本机 .NET 10 SDK 路径）：

```powershell
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -p:BusinessModule=Sportmaster
dotnet tests/Ipc.Desktop.UiVerification/bin/Debug/net10.0-windows/Ipc.Desktop.UiVerification.dll . --task-redesign
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --parameter-refactor
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --sportmaster-rcs
python scripts/package-application.py --application sportmaster --target win-x64 --output .artifacts/sportmaster-v2-win --dotnet dotnet
```

发行工具要求所有发行输入已提交，使用不可变 Git 源快照并做锁定依赖还原。Windows 介质含 `desktop/Ipc.Desktop.exe`、安装/维护入口、配置与文档；本页随介质为 `SPORTMASTER-V2.md`。首次安装沿 [单窗口安装流程](installer-single-window-01.md)，设备资格和现场参数按 [Hikrobot指南](hikrobot-reader-01.md)及 [RCS指南](sportmaster-rcs-task.md)核对，不能直接使用图中合成数据。已有配置从安装快捷方式运行；故障修复使用维护快捷方式，禁止把手工删除数据当恢复步骤。

## 验证边界

设计原型的 67 图、模拟成功与示例版本未成为生产逻辑。WPF 夹具使用真实 HWND/Dispatcher 和合成端口，1600×900 等是客户区 DIP/backing 尺寸，不自动等于物理屏幕。Windows 本机键鼠记录只证明记录的系统、工作区及缩放；触摸、Win10 22H2 Home/Pro、真实工控机和其他 DPI 未因此通过。真实相机触发实体关联、SDK资格、生产 RCS、计划备份目标权限和现场物理流程须另验。历史 D/RV、原发行 SourceCommit 和原始未公开材料状态保持。
