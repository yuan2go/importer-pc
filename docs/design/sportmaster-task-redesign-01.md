> 2026-10-21 安装与首次配置当前增量：[用户纠偏与执行边界](startup-experience-02.md)。只替代本范围旧当前交互，历史原件和证据保留。
> 作业主界面当前增量：[信息与操作完善](../runtime/workstation-information-01.md)。仅调整运行信息和按状态出现的操作；原 V2 作业合同、PARAM-UX-02 参数及各历史证据继续保留。

> 参数页面当前修订：[PARAM-UX-02](parameter-settings-experience-02.md) 与 [V3交付](../ui/parameter-settings-v3-20261020/README.md) 替代本文参数版式、底部保存步骤和“维护与迁移”入口。PR74已实现的作业/首配合同及PR76会话修复继续复用；下文旧实施状态保留形成时身份，不要求重复实现。

> 当前实施：[V2 实施与验证记录](../runtime/ui-task-redesign-01.md)。实施基准 main `de2b045`；此前设计、PR73及其他介质验证各守原版本。

> PR73 参数实施补充：原 PARAM-01 参数及必要联动已完成，原提交验证见[实施记录](../runtime/parameter-refactor-01.md)；PR73 发布阶段只做不测试的合并/上传（历史范围）。原 PARAM-01 不自动代表 V2；本版完成范围与新证据见顶部实施记录。

# Sportmaster 任务与对象 UI 重设计：实施裁决

日期：2026-10-20。当前获授权完成 UIR-00～06 实施与必要验证，基准最新 main `de2b045`。本页保留目标裁决；实际完成及差异以顶部实施记录为准。设计入库与PR73发布阶段的不测试限制仅属于原历史任务。

[PARAM-01](parameter-refactor-01.md) 保留已授权参数实施及已有模型；本次用户明确的四类字段归属和 V2 任务结构为后续目标。冲突处按本页受控迁移，不回滚条码筛选、心跳、保存核对或单窗口安装。快照范围及各介质原来源分别见 [参数快照](../runtime/publication-parameter-snapshot-20261020.md)、[参数实施记录](../runtime/parameter-refactor-01.md)与[PR69～72发布说明](../runtime/publication-pr69-pr71-20261020.md)。

当前目标为 [V2 设计入口](../ui/sportmaster-task-redesign-v2-20261020/README.md)及 [V2 交接](../ui/sportmaster-task-redesign-v2-20261020/HANDOFF.md)。V2 采用用户重新明确的三组作业信息、首次启动流程、系统/业务模块/驱动/组件配置归属及新增 RCS 响应字段。界面与 PNG 数量以 V2 索引为准；源码、导出图和格式副本不重复计作页面。此前 [V1 设计集](../ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)保留原字节及 58 项字段/状态、39 项动作的来源证据，其侧栏“首次配置”、旧设置对象平铺及演示端口不再作为现行实现目标。实施顺序仍沿 [UIR-00～06](../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)。

本轮用户提供的成功响应明确包含 `waveNo`、`taskId`、`skuId`、`chuteCode` 及字符串 `quantity`。这些是已确认的目标字段；当前代码缺少相应传递时应补实现，不继续以“Demo 未证实”取消需求。HTML 中的随机值、人工 Dispatch、AGV 放行、共享周转箱及拆包演示没有因此获得 Sportmaster 业务授权。

## 1. 实施前代码基线与继承要求（历史静态对照）

现有 C#/.NET10/WPF 已具有双位投影、同一 Application 多页面、完整配置账/CAS、加密未完成草稿、受控启动、恢复和原生安装。#62/#63/#64/#66/#67/#68 的工作台与布局修正继续继承，#69～72 的增量也已合入。新增工作的重点是把现有参数模型与错误/保存闭环落实到 V2 对象页，补字段归属迁移、三组作业信息、五步首配、带对象诊断及跨页作业感知；完成全部任务及异常状态，不能以首页美化或示范表单代替交付。

Sportmaster、DirectPlc、WcsToPlc 保持独立 Application。Sportmaster 左右 reader 请求格口，不下 PLC；登录、称重、采图、上传按真实组合显示，不增加每包确认或强制操作员登录。界面调用应用契约，继承资源所有权、原请求和数据库守卫。已有 `SupplyStationWindow.xaml.cs` 共享页面/辅助窗口；`src/Ipc.Application.Sportmaster.Desktop/SupplyStationWindow.Diagnostics.cs` 已为 OneWay。更改共享 ParcelFlow 控件须保留其他 Application 的必要能力。

### 最新 main 已有的相关增量

| 当前代码事实 | 复用与后续差距 |
|---|---|
| [ParameterEditorModel](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterEditorModel.cs) 与 [ParameterSettingsControl](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.cs) 已有四分类、已安装描述表单、对象/字段错误及定位。 | 沿同一模型重排：当前预算仍在系统、取格口字段仍在组件，须按 V2 唯一编辑归属迁移；完成对象列表、差异、反馈和适配，不再造第二编辑器。 |
| [StartupConfigurationDraft](../../src/Ipc.Application.ParcelFlow.Desktop/StartupConfigurationDraft.cs) 与日常页面共享参数模型；启动缺配时保留离线编辑路径。 | 复用同源字段、草稿和检查，重组为 V2 首次启动五步；已配置故障进入维护，不反复首配。 |
| [BarcodeFiltering](../../src/Ipc.Application.ParcelFlow/BarcodeFiltering.cs) 已有受限正则筛选、旧版单码兼容、原候选/筛选证据和本地样例测试。 | 在业务模块配置与首配业务步骤中保留入口，呈现唯一选中、零匹配、多码歧义、错误与预算耗尽；不重复实现筛选机制。 |
| [桌面保存核对](../../src/Ipc.Application.ParcelFlow.Desktop/ConfigurationSubmission.cs) 和 [应用保存核对](../../src/Ipc.Application.ParcelFlow/ConfigurationSubmission.cs) 已处理 Unknown、PendingRestart、NotSaved、Conflict。 | 复用规范化候选指纹、原修订和运行所有者核对，把状态与下一步完整呈现；补真实剩余缺口与必要验证。 |
| [RCS 心跳](../runtime/rcs-heartbeat-01.md) 已有默认周期、同实例串行循环、停止/重连和状态证据；海康当前描述含 `bcr-code`。 | 保留心跳及其独立编码来源；HTTP 交付不能当取格口或应用确认，不复制第二个定时器。 |
| [单窗口安装](../runtime/installer-single-window-01.md) 与 [固定桌面入口](../runtime/desktop-shortcut-name-01.md) 已并入。 | 继承一次授权、原责任守卫及普通权限启动，只做必要视觉衔接，不重新实现安装流程或改标旧介质。 |

上述表格保留实施前差距，不作为本版未完成清单。本版结果见实施记录。实施前源码声明海康 1.0.3、RCS 1.2.1、Application.Contracts 1.12.0；心跳报告的 1.0.2/1.2.0/1.11.0 属原冻结产物，保留各自精确身份。

## 2. 导航、首次启动与配置对象

日常主导航为“作业 / 作业记录 / 参数配置 / 诊断与恢复”，帮助及备份等从所属任务进入。**首次配置仅在首次启动尚未完成配置时出现，为独立启动页面；不在主侧栏、设置页签或日常快捷入口中保留“首次配置”。** 未完成退出时沿原加密草稿续办；已配置但发生故障时进入既有维护/恢复，不以重新首配覆盖配置或作业责任。

首配采用五步“业务模块 → 系统配置 → 驱动配置 → 组件配置 → 检查与完成”：先核对已安装 Application 并填写本模块请求规则/处理预算，再配置系统标识/本机目录、两个驱动实例、一个 RCS 组件，最后完整关联检查并保存/受控启动。各步校验当前输入，未验证真实链路不能显示已通过。Sportmaster 安装组合只有该 Application 时直接明确展示，无需虚设流程选择器；确有多 Application 安装入口时选择独立 Application，不把不同业务变成运行中切换的通用流程。完成后进入作业；日常修改从四个配置分类直达对象，不重复首配。

| 配置分类 | 用户看到的对象 | 数据与业务边界 |
|---|---|---|
| 系统配置 | 当前供包台标识、本机显示、目录、记录保留及备份 | 取格口 `zoneId/projectId/stationCode` 按用户要求在此唯一维护；现有心跳 `StationCode` 明确用途。两种外部编码与本地 `StationId` 分开，未获同义证据不相互覆盖。 |
| 业务模块配置 | 当前 Sportmaster 的条码筛选、请求规则、处理队列、全局调用预算和作业容量 | 复用已有 `BarcodeFilter`；`quantity`、`specifiedChute/chuteCode` 及 `queueCapacity/timeoutMs/maximumOperations` 按模块归属编辑。页头明确当前 Application，不提供运行时切换业务的下拉。 |
| 驱动配置 | 已安装且适用的驱动类型 → 该类型下的实例 → 连接、业务映射及高级参数 | 同类型可多实例；实际数量与用途由 Application 声明。Sportmaster 为左右两个读码实例，可共用同一海康驱动；每个实例独立 IP、身份及 `bcrCode`。 |
| 组件配置 | 已安装且适用的组件 → 当前连接实例与参数 | 本轮组件只连接一个；Sportmaster RCS 为一个共享连接服务两路。不开第二连接或“新增组件实例”的无效任务。 |

选择驱动类型、编辑实例、识别物理设备、建立位置绑定分别表达。对象标题和列表保持 Application、工作站、驱动类型、实例名、左右用途及未保存状态可见；保存摘要点明变化对象。运行内稳定 `DeviceId` 只读，改名不改身份，更换设备走既有受控迁移。`Reader` 是读码观察来源，不能误绑定到可选采图 `Camera`；仍不新增包裹图片预览。

实例数量不能仅从驱动配额决定：海康模块 `MaximumInstances=8` 只是类型上限，Sportmaster 要求两个位置与两个 Reader 一一对应，当前工厂拒绝未绑定 observation 实例。因此正常配置页展示“左位 / 右位，已配置 2/2”，数量说明归属于 Sportmaster；不承诺可以再保存 6 个未绑定备用相机。首次配置按缺失的业务位置补齐，已完整时无泛化“新增实例”主按钮。RCS 模块上限为 1，本轮 UI 和后台均保持单连接。

依据：`src/Ipc.Drivers.Hikrobot/modules.json`、`src/Ipc.Integrations.SportmasterRcs/modules.json`、`src/Ipc.Application.ParcelFlow/ParcelConfigurationRules.cs`、`src/Ipc.Assembly/ParcelCapabilityFactory.cs`。类型/实例编辑仍复用 `StructuredConfiguration.cs`、`CapabilityEditing.cs` 与类型化 `CapabilityBinding`；禁止另造 `Extensions.Services/Ports` 配置权威。未安装、已安装不适用、无可配置参数、实例缺失分别提供原因及相应安装/配置入口。

## 3. 参数归属与 RCS 字段迁移

UI 按人的任务组织字段，底层仍由 Application 持有业务参数、驱动持有硬件参数、Integration 持有协议规则。配置移到另一分类不是复制一份值；组件页只读引用系统的区域/项目/供包台编码、业务模块的请求数量/指定格口规则及驱动实例相机编码，并提供定位入口，不能出现两处都可改的同义字段。统一使用现有 `ParameterField` 元数据与应用级字段描述，必要时补对象/路径/错误码及关联校验。参数描述不是第二运行权威；目前 ApplyLevel 为 Restart、EditRole 为 Maintenance。

### 3.1 用户明确的字段归属

| 请求内容 / 原键 | V2 中文名称与位置 | 规则及迁移处理 |
|---|---|---|
| `zoneId` / `zone-id` | 系统配置 → 业务现场 → 区域 ID | 保留整数原义；现有合同为正整数 Int64。由系统配置唯一维护，生成请求时读取已生效的冻结值。 |
| `projectId` / `project-id` | 系统配置 → 业务现场 → 项目 ID | 同上；用户示例 13421 不是默认。 |
| `stationCode` / `left-station-code`、`right-station-code` | 系统配置 → 业务现场 → 供包台编码 | 当前新要求为整台一值。旧左右值相同才可提出合并映射；不同则同时展示原值及影响，必须明确选择目标值或保留未迁移状态，不能取左值覆盖右值。它不等于本地 `StationId`。 |
| `bcrCode` / `left-bcr-code`、`right-bcr-code` | 驱动配置 → 对应相机实例 → 取格口相机编码 | 左右分别维护并随业务绑定核对；当前仍是 RCS 取格口映射，迁移需保持兼容。不得仅因同名自动替换已声明的心跳 `bcr-code`；切换类型按业务实例保留/校验映射。 |
| `loadCode` | 作业 → 扫描信息 → 当前条码 | 来自该次相机完整候选及业务筛选后准入，不是手工配置。保留原始候选、选中码与筛选状态，保留前导零，不按同码合并不同实体。 |
| `quantity`（请求） | 业务模块配置 → Sportmaster → 请求数量 | 现有协议为正整数 Int32；用户示例 1 不是相机计数或默认。显示来源，不能改称实测包内件数。 |
| `specifiedChute` / `specified-chute` | 业务模块配置 → Sportmaster → 指定格口 | 使用开关；开启时才适用指定格口值。 |
| `chuteCode` / `chute-code`（请求） | 上述开关下的“指定格口” | true 时必填；false 时完整候选省略原 `chute-code` 键，有效请求序列化为 null，禁止把失效旧值带到请求。草稿如保留旧输入供恢复，须明确它当前不参与候选及发送。 |
| Target | 组件配置 → RCS → 连接 → 服务地址 | 明确 HTTP(S) `/rcs/sorter/api`，禁止凭据、query、fragment；保留输入，错误就地定位。localhost 端点仅为用户给出的接口示例，不执行真实请求。 |
| `source`、`locale` | 组件配置 → RCS → 协议 | 保留现有协议文本字段，未声明枚举时不编造固定选项。 |
| `request-id-base`、`request-id-count` | 组件配置 → RCS → 高级请求编号 | 沿现有独占编号区间规则及溢出校验；不提供每包手工填写 `requestId` 的输入。 |
| `version`、`index`、`command` | 协议详情只读 | 现有固定值及命令，非日常配置。不得从用户示例扩成通用协议编辑器。 |
| `StationCode`（现有心跳上下文） | 系统配置 → 心跳供包台编码 | 当前从完整配置读取，1～256 非控制字符且非空白；不等于内部 StationId，也不自动替换取格口左右编码。旧账的软件候选默认值不构成客户编码确认。 |
| `bcr-code`（已安装驱动声明） | 驱动配置 → 对应实例 → 心跳相机编码 | 海康当前真实第六项声明，保留大小写、最长256；RCS 心跳启用时必填，各 Reader 值不同。按选中 Reader 的稳定绑定取值，不从 IP/序列号/左右名称猜测。 |
| `heartbeat-interval-seconds` | 组件配置 → RCS → 周期心跳 | 当前默认5秒，整数1～3600秒，受控重启生效；复用原定时循环和编号隔离。 |

当前 RCS 参数描述共有14项（含新增心跳间隔），另有 Target；取格口仍由 `RcsTaskOptions.Position()` 按原 `PositionId` 读取 `left/right-station-code`、`left/right-bcr-code`。心跳另由 `RcsConfigurationValidation.HeartbeatMapping()` 读取系统 `StationCode` 和已选 Reader 的 `bcr-code`。这两条已存在的用途必须分别保留；同值也只能提出关联建议，旧值不同则展示来源、用途和差异，未经明确业务映射不静默合并。来源：[RCS 参数](../../src/Ipc.Integrations.SportmasterRcs/parameters.json)、[取格口选项](../../src/Ipc.Integrations.SportmasterRcs/RcsTaskOptions.cs)、[心跳映射](../../src/Ipc.Integrations.SportmasterRcs/RcsConfigurationValidation.cs)。

后续按用户目标迁移取格口站级上下文、实例映射、业务规则及候选组装，可以复用兼容投影或受控升级，不限定具体内部实现；始终保持同一完整配置账、完整 CAS、历史可读和一个用途只有一个编辑来源。组件页面对已移出的字段只读引用并可定位；新增真实驱动参数只能来自精确已安装描述，不修改旧签名包或向不声明该键的驱动塞参数。

已冻结原请求继续使用原报文、目标、参数/合同/包版本，不随新系统编码、相机编码或 RCS 地址重写；旧配置原文、映射差异及确认记录保留。配置迁移、历史响应升级与新请求准备分别处理。缺字段保留待填状态；默认、长度、范围及协议可空语义只采用已证实合同，未确认项不得从例值猜测。

### 3.2 海康驱动实例的真实声明与参数

| 字段 | 中文名称 / 控件 | 适用与校验 |
|---|---|---|
| `device.ip` | 相机 IPv4 地址 / 文本 | 必填，最长15；规范 IPv4，拒绝全零/全广播，无默认。修改客户端连接目标，不是写设备 IP。 |
| `device.serial` | 预期序列号 / 文本 | 可选，最长32；空值仍使用实际序列号防重复占用。 |
| `entity.mode` | 实体关联策略 / 枚举 | observe-only / trigger，现有默认前者；只观察不能准入业务。trigger 需要既有设备触发配置和资格证据，不是硬件触发开关。 |
| `code.encoding` | 条码字节编码 / 枚举 | utf-8 / ascii，现有默认 utf-8；严格解码。 |
| `queue.capacity` | 回调队列容量 / 整数 | 1–1024，现有默认128；与 Application 输入队列和作业容量分开。 |
| `bcr-code` | 心跳相机编码 / 文本 | 当前海康描述已声明，最长256；RCS 心跳的上下文校验要求非空且各 Reader 不同。它是外部业务编码，不代替 IP、序列号或 DeviceId。 |

来源：[海康当前参数描述](../../src/Ipc.Drivers.Hikrobot/parameters.json)、`HikrobotReader.cs`。前五项连接/采集参数继续继承，第六项 `bcr-code` 是已声明的心跳业务编码，不能继续写成“驱动只有五字段”或禁止使用真实声明。当前驱动仍没有端口、曝光、口令或复位参数；V1 的9000端口不使用。实例页把取格口映射和心跳编码按用途分组、显示冲突及来源，避免操作员误改同名字段；后续只有合同确认同义后才可合并编辑。未安装或不适用参数不混入当前表单。

业务模块配置中的 Application 调用超时仍是全局预算，输入队列和未清偿作业容量也归当前模块；系统配置保留常驻历史、磁盘保留及备份策略。各字段保留原单位、含义和范围。依据 `src/Ipc.Application.Contracts/ParcelContracts.cs`、`ParcelMaintenance.cs`、`src/Ipc.Application.ParcelFlow/ParcelConfigurationRules.cs`。非法数值文本不变0，未知字段不丢失，省略默认和字符串列表原意不被重排改写。

计划备份关闭的真实候选为 `BackupSchedule=null`；仅启用时校验并提交绝对目录、周期和保留份数。依赖值只在保存前的编辑副本中暂留以便撤销/重新启用，成功保存后不作为已保存依赖复活，失败仍保留本次输入。原型平面内存字段只用于交互演示，不定义新的生产存储格式；关闭计划只取消后续调度，不删除已有恢复集。依据 `src/Ipc.Application.ParcelFlow.Desktop/StructuredConfiguration.cs` 的 `ConfigurationPolicyDraft.From/BuildBackup()`。

### 3.3 已有业务条码筛选与软件默认

当前 `BarcodeFilter` 为 Sportmaster 左右共享规则。保留“本地候选样例，每行一个、同一次观察”和“测试候选筛选”入口，测试不保存、不应用、不外呼。复用已有规则编译与筛选：正则1～2048字符、整串匹配、CultureInvariant、单次25ms/总计100ms预算，最多300项完整候选；零匹配、多匹配（包括重复相同码）和完整性不足都不能选第一项继续。规则改变后的异步样例结果标为过期，其他输入保留。原 `BarcodeFilter=null` 才保留严格单原始码兼容；已存在规则时清空不自动退回兼容或生成 `.*`。来源：[筛选实现](../../src/Ipc.Application.ParcelFlow/BarcodeFiltering.cs)、[参数控件](../../src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.cs)、[完整候选组装](../../src/Ipc.Application.ParcelFlow.Desktop/StructuredConfiguration.cs)。

工作台扫描区区分原始候选、业务选中码与筛选失败，取得唯一合法码后仍继续原实体/来源/容量等准入守卫；筛选通过不等于业务请求成功。记录保留规则指纹和原候选，不按新规则重解释旧作业。首配业务步骤与日常业务模块页共用这一模型，不复制另一个正则验证器。

新代码已有软件级默认：[SystemParameterDefaults](../../src/Ipc.Application.Contracts/SystemParameterDefaults.cs) 中队列64、调用预算1500ms、未清偿作业10000、常驻历史200、写入保留64MiB；新站内部标识为station-01。计划备份默认关闭，启用编辑候选已有60分钟/10份和本机用户数据目录路径。迁移分类不删除这些已声明默认，不覆盖旧配置显式值；心跳 StationCode 的软件候选默认取本地标识，但不能据此替换用户尚未确认的取格口编码。真实 IP、业务筛选规则及外部客户编码仍不从界面示例生成。

### 3.4 三组作业信息与响应字段

| 固定区域 | 显示内容与来源 | 更新规则 |
|---|---|---|
| 相机信息 | 左右位置、实例名、相机连接状态及可查看的身份/编码 | 保留用户认可的设备头部；设备状态更新不清空扫描或冒充业务成功。 |
| 扫描信息 | 相机本次完整候选、Application筛选状态与SelectedCode、读取时间/来源；异常观察单独表达 | 候选和业务筛选分阶段更新。突出当前有效选中码，原候选与筛选依据按需展开；新件清出旧任务，未准入观察与原未决责任分开。 |
| 任务信息 | RCS `chuteCode` 大字号；`waveNo`、`taskId`、`skuId` 明确标签，返回数量单独显示 | 只接受匹配本次原请求/实体/位置的有效响应；同一份结果原子更新。处理、拒绝、超时、失效时绝不残留上一件任务字段。 |

`taskId`、`skuId` 已由 `RcsDestinationProtocol.cs` 解析到 `WcsDestinationResult`，随 `StageReply.Destination` 进入作业快照；`ParcelFlow.Desktop/ParcelViewModel.cs` 已有文字摘要，Sportmaster `SupplyStationViewModel.cs` 引用摘要，但尚未按上述三组结构化展示。不要把已有传递能力重复列成缺失，也不要用本地请求号替代 RCS 任务号。

`waveNo` 和响应 `quantity` 尚未出现在当前 `WcsDestinationResult`（`src/Ipc.Application.Contracts/WcsDestination.cs`）与解析器，需补兼容 DTO/解析、持久化回复读取、投影及历史详情。新增可缺省属性须核对 Wire 序列化与哈希兼容，旧回执/冻结请求的原字节和校验值不重写，不能因默认 null 重新序列化而制造历史身份变化。用户成功样例中请求 quantity 是 JSON 整数、响应 quantity 是 JSON 字符串；二者分别保留，不能静默强转、回填请求值或据此发明数量差异处置。波次号、任务号和 SKU 均按字符串展示；旧记录缺新增字段时明确“历史记录未包含”，新回复缺值/类型不符按协议规则反馈，不能拼造默认。用户尚未提供错误响应扩展，不自行改写既有拒绝、Unknown 或 NEED_UNPACK 的业务合同。

## 4. 编辑、保存、应用、准入分别呈现

对象页只是编辑范围，保存仍合并为完整 `ParcelConfiguration`，沿唯一配置账与原CAS；不建立左/右/WCS独立存储。草稿绑定加载时的配置 Revision及RuntimeId，后台刷新不推进；表单与高级JSON只有一个编辑来源。一次完整修订填写一次维护原因。现有保存要求软件暂停、维护认证和排空，界面应提前解释并引向原维护操作，不能删守卫让原型按钮直接成功。普通显示偏好沿独立用户偏好即时保存。

PARAM-01 已实现 `ParameterEditorModel.ValidateFields/Report` 和双方 `ConfigurationSubmission.cs`：在应用层规范化候选、核对原 RuntimeId/配置修订/磁盘账与运行所有者，区分 PendingRestart、NotSaved、Conflict、Unknown；未知时阻止重复 Stage。复用现有接口与状态，不重复补一套保存服务。下一步完成其准确中文、对象差异、未知/冲突下一步与异步输入保护，并按最终实现做必要验证；静态读到方法不代表这些任务已验收。

| 状态 | 呈现及可执行结果 |
|---|---|
| 未改 / 未保存 | 显示编辑基线及变更对象数；普通切页保留草稿，无需反复弹窗。撤销只撤本次编辑。 |
| 校验中 / 字段或关联错误 | 固定忙碌反馈，首错可定位；异步结果仅对应原候选，其他输入保留。 |
| 保存中 / 已确认写入前拒绝 | 禁止重复提交；只有明确未进入写入的拒绝才显示“未保存”。CAS冲突保留草稿并比较，不能覆盖他人修订；普通Rejected回执本身不是写入前拒绝的证明。 |
| 保存结果待核对 | Stage落盘后，审计/返回失败既可能抛异常，也可能返回普通Rejected；仅凭回执名不能宣布未保存或允许重试。应用须结合规范化候选指纹和原配置账核对；UI保留输入并显示“保存结果暂时无法确认”，核对已存相同候选后展示其真实状态，不重复提交。 |
| 已保存待重启 | 展示真实Pending及完整变更范围；明确运行内容未因此切换。 |
| 应用中 / 应用失败 | 使用原受控启动尝试、资源释放和失败记录；保留失败候选、旧确认内容及原责任。不能用Reconnect冒充应用或声称自动回退成功。 |
| 运行未知 / 已应用 | 最后确认内容与当前证据分列。Effective字段存在本身不证明启动：Seed/LegacyEnvelope可已有Effective且Confirmation.Required=true。只有匹配内容指纹的成功activation/confirmation才能证明对应构造/启动阶段；仍不证明当前健康、实体许可或允许供包，还需新鲜快照和各位准入事实。 |

依据：`src/Ipc.Platform/ConfigurationRevisions.cs`；`src/Ipc.Application.ParcelFlow/ParcelCommands.cs`、`ParcelRuntime.cs`、`ParcelHealth.cs`、`ParcelGuards.cs`。Revision是整个配置信封修订，不是原型独立自增的“编辑/保存/运行”三套版本；显示应关联真实内容指纹、RuntimeId和确认时间。不可把业务快照Revision当配置CAS。

离开会覆盖输入的任务、重读或关窗前提供“继续编辑 / 保留加密草稿 / 放弃本次输入”，沿现有无效草稿与多窗关闭保护。恢复上一有效内容是有证据的内容形成新候选，再校验/CAS/重启；恢复默认只使用已声明且适用的默认值，先列本次差异。配置历史查询可补应用拥有的受限只读契约，不直接读SQLite或另建审计平台（`ParcelConfigurationAudit.cs`）。

## 5. 连续作业与诊断的真实能力

左右卡固定位置，按Position、Operation、Admission和Request关联异步结果；新观察失败、未准入和原未决作业分别显示，旧格口失效，成功表达“取得格口”，不冒充物理完成。一路异常是否影响另一路由Positions/Availability和共享依赖事实决定；单件Unknown不能直接写成全站WCS断线。保留现有只通知变化、列表选择/滚动和焦点稳定机制（`src/Ipc.Application.Sportmaster.Desktop/SupplyStationViewModel.cs`）。

按当前 schema 3 合同，恢复接收要求 `CapacityAvailable && SharedReady() && positions.Keys.Any(PositionReady)`，不是两位均就绪。共享条件与容量满足时，未知原件继续阻断其所属位置，健康另一位可以具备恢复资格；共享服务失败、容量耗尽或全站前提未满足仍不能恢复。按钮状态取当前 Application Availability，提交时沿 `CanResume()` 再核验；恢复软件接收不会清偿未知原件，也不代表两位同时可准入。依据 `src/Ipc.Application.ParcelFlow/ParcelGuards.cs`、`ParcelCommands.cs`。不移植原型的简化内存守卫。

诊断以“对象与影响→已有证据→可检查层→相关参数→适用恢复→返回作业”组织。地址格式、传输可达、设备连接、收到帧/码、业务回应与实体准入各自有时间和来源。没有通用草稿安全连接测试契约：测试不保存、不应用、不发送真实业务，缺某层就显示未支持或未验证。海康独立Probe会取得物理资源，不能在运行旁直接抢占。定向重连已有暂停、维护认证、资源排空/epoch守卫，结束仍暂停，不承诺另一路继续供包（`src/Ipc.Application.ParcelFlow/ParcelDeviceLifecycle.cs`）。

当前 RCS Health 仍不证明远端应用成功，但已附带周期心跳等待、HTTP 交付或失败证据；不能继续写成“完全没有远端通信证据”。诊断分别显示客户端生命周期、最近心跳 HTTP 状态与时间、应用确认未获合同、取格口本次结果。心跳故障按现有健康/准入守卫处理；下一次2xx只能解除心跳传输故障，不清偿 Unknown，不替代 Query。重连沿原停止/排空再启动且只创建一个心跳循环，不应用 Pending，也不重发旧心跳或原业务。

`Requirements.Query=false`、QueryAsync返回null；“核对原请求”不得接POST，应说明“当前协议未提供结果查询”，保留原件与责任及实际适用处置。心跳默认5秒、可配置1～3600秒，沿原请求编号隔离和单次有界取消；不把真实周期心跳当草稿连接测试，也不让页面切换新建定时器。NEED_UNPACK沿原检查移除任务，不新增拆包提交。依据：[心跳实现](../../src/Ipc.Integrations.SportmasterRcs/RcsHeartbeat.cs)、[RCS服务](../../src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs)、[原心跳验证边界](../runtime/rcs-heartbeat-01.md)、`ParcelStages.cs`。

## 6. 首配、维护与安装完整衔接

首配改为首次启动的独立全窗流程，复用已并入的 `StartupConfigurationDraft`/`ParameterSettingsControl`、原目录/设备/策略/离线分层检查、加密草稿、保存待启动和修复；完成后不保留日常导航入口。软件能打开与业务就绪分开：缺参数、无效规则、缺驱动/组件或连接失败保留参数/诊断/维护入口，业务准入和外呼仍受守卫。已有身份/原责任不能用新站种子覆盖。业务模块及安装清单决定适用驱动/组件和实例数，左右两相机及单 RCS 连接在首配中完成。后续维护直接进入系统、业务模块、驱动或组件对象，不反复走向导。模板导入先显示适用对象与差异，再改草稿：现有安全模板仅允许queue/timeout/operations/resident/reserveMiB、非Backup策略和精确provider选择；排除站/设备/IP/目标/凭据/编号/业务数据及备份路径。不能按模拟模板迁移相机地址（`src/Ipc.Application.ParcelFlow.Desktop/StartupSiteTemplates.cs`）。完整备份与隔离恢复为独立任务，保留原目录、完整恢复集和责任屏障。

记录保留固定查询/选中原件及导出边界；支持摘要复用白名单预览：范围最多7天、最多256事件，预览5分钟内可导出同一份，注明截断/过期，非采集最近5分钟（`src/Ipc.Application.ParcelFlow/ParcelProductSupport.cs`）。

继承“菜鸟供包台”名称/图标、目录和两个快捷方式、单窗口自动准备/选项/摘要/完成、安装一次UAC、普通权限启动、并排升级及BUILD-INFO。PR69 的单窗口与固定桌面名称已合入，不重复重做安装流程。

已上传单窗口安装器仍为 SM1.4.1，SourceCommit=`27b0d8f6cc0281fd82ee68fcda350233cb0a45ff`；原58项是该介质和原Windows11主机Simulation范围，尚不包含后续固定桌面名称、心跳或参数快照。心跳portable CLI/签名夹具来自 `3f8a58ba32887df07f6a7817b64afa693f903c50`，不是WPF安装包。原7c/41项发行保留历史身份，最终ba80合并树没有因此取得新构建或验收。来源：[单窗口安装](../runtime/installer-single-window-01.md)、[桌面名称](../runtime/desktop-shortcut-name-01.md)、[已上传介质与不测试发布回执](../runtime/publication-pr69-pr71-20261020.md)。正常人工单EXE安装继续为主入口，不重建安装器或强加批量部署。

## 7. 视觉、清理与验收裁决

用户追加的“商业软件质感”按六项可观察结果实施：重点清楚、组织一致、密度合适、细节完整、操作连贯、真实显示可靠。作业页首眼可识别当前包裹/格口/异常，参数页按准确输入组织；相似操作同位、同名、同反馈，全部适用状态明确。刷新保留焦点/选择/输入，耗时操作不阻塞整个UI；目标工控机分辨率和Windows缩放下无意外截断、遮挡或关键内容缺失。各项完成标准与必要证据统一见[工作包的六项质量要求](../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md#商业软件质感六项可观察完成标准)。原生实际交互和目标环境检查分别验证连贯性与显示可靠性，静态设计图不替代这些证据。

采用 V2 的克制浅色、稳定双位三组信息、分类对象表单与统一控件；中文/数字层级、间距、焦点、禁用原因、长内容和忙碌状态按 V2 交接及样式源码实现。1600×900逻辑为主，1366×768补查；现有正常作业下限800×420 DIP、1280/680紧凑阈值及不足空间暂停保护仍继承，不能抬高下限或删严格几何断言。800×420应验证当前条码/格口仍在本位视口、原责任与主要操作可达；表单允许纵向滚动。依据 `src/Ipc.Application.ParcelFlow.Desktop/ParcelPresentation.cs`；`src/Ipc.Application.Sportmaster.Desktop/SupplyStationWindow.xaml.cs`；`tests/Ipc.Desktop.UiVerification/UxCommercialClosure.cs`。

编码前将 AGENTS/INDEX/README/UI STATUS 中 V1 侧栏首配、七对象平铺、旧字段归属及更早 v1.1 页面主定义撤出现行入口，统一指向 V2、本裁决、工作包和最新发行；在既有pre-*方式保留原字节，归档指令用.txt。原设计95文件、旧图集、REQ/BR/CAPI/D/RV、失败、PDF和旧介质身份不倒写。manifest记录本次设计材料身份，不将27b0d8f安装器、3f8a58ba心跳或原7c的证据改成新V2或最终main已验。

实现完成必须通过六核心任务及相关失败场景，提供同版WPF大图、交互记录、字段/动作去向与运行说明。原型QA、原生合成视觉树、Windows实际输入、真实设备/服务/现场分别登记。既有233项WPF组合、原7c的41项升级、27b0d8f的58项安装及3f8a58ba的心跳验证均守各自冻结源码、夹具与环境；不得扩张到未验参数快照或V2。目标工控机、Win10 22H2 Home/Pro及其他目标CPU、DPI/输入、精确SDK/trigger实体关系、真实客户RCS和现场资格按实际未验部分列明。实现不能因缺远端合同停在半张界面，也不能用假成功补齐协议能力。

