> 2026-10-27 用户最新纠偏以[SDK入口与单一相机编码](../work-packages/WP-IPC-CAMERA-SDK-SINGLE-CODE-01.md)为准：每台相机只有一个编码，取格口与心跳共用；SDK需IP/可选序列号，不使用TCP端口。下文旧分开编码说明保留形成时范围，原请求与历史证据不改写。

> 2026-10-27 SDK-BARCODE-02：当前相机接入、参数与诊断规则见[本轮工作包](../work-packages/WP-IPC-HIKROBOT-SDK-BARCODE-02.md)。下文是原阶段记录，不定义当前入口；旧“不改 UI”、旧配置/SDK迁移、兼容与旧包版本条款不适用于本轮。原字节与历史验证已[保留](../archive/pre-hikrobot-sdk-barcode-02/README.md)。

> 2026-10-21 本轮所有权、旧参数继承和真实消费者以[继承增量](parameter-inheritance-02.md)及[迁移表](parameter-legacy-migration-02.csv)为准。原 PARAM-01 实施结果保留；本页旧字段摆放不覆盖本轮硬件驱动/外部服务职责裁决。

> 2026-10-21 当前执行入口：PARAM-UX-02 / WP-IPC-PARAMETER-SETTINGS-02，参数采用 V3，复用已合并 PR78/79。下文“当前、待实施、不测试”均保留原阶段语境；不再定义当前布局或禁止本轮原生验证。

> PR73 参数实施补充：原 PARAM-01 参数及必要联动已完成，原提交验证见[实施记录](../runtime/parameter-refactor-01.md)；本轮只做不测试的合并/上传。V2 字段重排、五步首配、三组作业信息及其余 UI 仍按本页后续目标实施，不据此宣布 V2 完成。

# PARAM-01 · 供包台参数正式重构

状态：**原 PARAM-01 参数及必要联动已在 PR73 完成；V2 新要求继续实施**。2026-10-20 用户另行授权的参数开发保留；当前来源为 `ba80d0ea90d86b24f9d9eebd20d075da34320972`，PR71 冻结范围见[快照记录](../runtime/publication-parameter-snapshot-20261020.md)。本轮 V2 是设计/文档增量，不新增生产实现或测试。不能因快照已合并宣布全包完成，也不能继续说全部 UIR 尚无 WPF 移植。

后续目标统一见 [Sportmaster V2 实施裁决](sportmaster-task-redesign-01.md)、[设计入口](../ui/sportmaster-task-redesign-v2-20261020/README.md)和[UIR工作包](../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)。**用户本轮的四类字段归属、五步独立首配、三组相机信息优先于本快照的临时字段摆放。** 已有参数模型、条码筛选、保存核对、RCS心跳和单窗口安装必须复用；以下明确源码现状与待迁移目标，不建立并列参数权威。V1 原型/48 PNG/58字段状态/39动作只保留原件与形成时证据。

## 实施来源与当前已有代码

快照形成时，开工main为 `6784d08a7a8f5326607dda83e00d21bc2ed71632`；原分支整合PR70并补保存前心跳上下文静态校验，PR69当时并行。该时间点属于原过程记录：截至 ba80，PR69/70/71/72 已合并，原提交、介质与不测试边界见[发布回执](../runtime/publication-pr69-pr71-20261020.md)，不再据原“并行未合并”重复整合或重写成果。

已读到的实现包括 `ParameterEditorModel`/`ParameterSettingsControl` 四分类与字段定位、`StartupConfigurationDraft` 首配共模、`BarcodeFiltering` 受限本地筛选、双方 `ConfigurationSubmission` 保存结果核对、`RcsConfigurationValidation` 心跳上下文静态校验。它们是继续完成本包的基础；[PR73实施记录](../runtime/parameter-refactor-01.md)已补原参数范围的构建、配置恢复和原生合成证据；本轮最终合并树不新增测试，真实链路边界保持。

## 四类参数：现状与 V2 目标

| 分类 | ba80 快照中的摆放 / 已有能力 | V2 目标与迁移边界 |
|---|---|---|
| 系统配置 | `StationCode`、队列/调用预算/作业容量、记录保留和计划备份；本地身份/目录及用户显示偏好已有机制。 | 唯一维护取格口 `zoneId/projectId/stationCode`，保留现有心跳 `StationCode` 的用途和原值，以及本机显示/目录/记录/备份。队列/超时/作业容量编辑移到业务模块。外部编码不替换本地 `StationId`。 |
| 业务模块配置 | Sportmaster 左右共享条码筛选正则、本地多行候选测试；其他 Application 的真实重量/图像策略保留兼容。 | 复用上述筛选，集中维护请求 `quantity`、`specifiedChute/chuteCode` 和 `queueCapacity/timeoutMs/maximumOperations`。只配置当前 Application，不在日常页面切换主业务；Sportmaster不新增称重/采图/上传。 |
| 驱动配置 | Application 固定左右两个Reader，精确安装描述决定参数。海康当前1.0.3描述包含IP/序列号/实体关联/编码/队列及心跳 `bcr-code`。 | 已安装类型→业务要求的实例；各实例独立编辑连接与编码。取格口 `bcrCode` 映射迁到对应实例页，心跳 `bcr-code` 保持真实驱动声明。两用途先明确区分并审阅差异，不凭名称自动替换。身份/数量/DeviceId/用途只读，旧签名包不接受臆造参数。 |
| 组件配置 | 一个逻辑WCS/RCS连接；Target和组件14项声明参数，取格口左右编码/数量/格口规则目前均在此。 | 保留一个RCS连接、endpoint/source/locale/请求编号区间及心跳间隔；已移出的系统/业务模块/实例字段只读引用并可定位。保留协议校验及历史读取；不复制心跳循环或第二套配置。 |

四类只改变同一完整候选的编辑范围。`RcsTaskOptions.Position()` 当前取格口读取旧 `left/right-station-code`、`left/right-bcr-code`；心跳读取系统 `StationCode` + 已选驱动 `bcr-code`。旧左右取格口站码不同必须显式处理；取格口与心跳码不同也不能静默互换。用户“取格口一台一编码”的目标可以受控迁移，但原请求继续冻结原报文、目标、合同和精确包。具体实现可用现有模型的兼容投影或受控升级，一个用途只保留一个可写来源。

心跳间隔默认5秒、范围1～3600秒；只沿现有受控重启生效。HTTP 2xx只证明本次心跳传输，应用确认未有合同，不代表取格口成功/原请求核对/现场就绪。`Query=false`继续保留。计划备份关闭按 `BackupSchedule=null`，依赖字段只在启用时提交及校验；临时编辑输入不定义生产存储，也不删除原恢复集。精确交互及必要验收统一使用V2交接。

## 准入与兼容决定

驱动只复制有界原始解码候选及完整性事实，业务筛选不得把被截断集合当唯一。可信来源/epoch/sequence/同实体关联先成立；完整候选零匹配拒绝，多有效码歧义拒绝，唯一有效码再走原身份/去重/左右隔离/容量/权限守卫。原始候选、筛选结论、选中码及规则指纹进入原输入/作业证据；无新外呼测试。原请求、原配置和报文继续冻结。

当前配置 `BarcodeFilter=null` 表示未启用筛选，保持无损往返，并显式显示严格单码校验；已有规则后清空会形成无效规则，不能静默关闭筛选。不猜客户正则、不静默写 `.*`。维护人员显式填写规则、完整校验并保存后，待受控重启应用。新增字段为空时不改变旧序列化字节；公共契约/业务/精确扩展版本沿现有兼容清单更新。

## 编辑与失败闭环

四分类只是同一完整草稿的编辑范围；首配与日常共用参数模型/控件/静态验证及配置账。字段错误可定位并保留原文；异步结果不得覆盖更新输入。未改、未保存、校验失败、保存中、待核对、Pending、应用失败、运行未知分别呈现。Stage 回执或异常不证明未落盘：应用按规范化候选指纹与原配置修订核对；未确认前禁止再次 Stage。CAS 冲突保留输入，不推进原草稿基线。保存不应用，Reconnect 不应用 Pending；启动与当前准入证据分别显示。

## 工作包映射与验收

UIR-00：统一 V2 当前入口并保留本参数快照；UIR-01：复用四分类模型、固定实例；UIR-02：迁移新字段归属并保留条码筛选、心跳联合校验；UIR-03：复用保存核对/CAS，补齐真实状态与交互；UIR-04：筛选证据、三组任务信息与旧结果失效；UIR-05：首配同源重组五步；UIR-06：以后代码实现按风险完成必要验证。本轮仅文档合并，无新构建或测试。完整工作包其余范围仍待完成，已并入的单窗口安装与心跳不重复实施。

实际完成、运行方式和外部验证见 [PARAM-01 实施记录](../runtime/parameter-refactor-01.md)。D/RV、历史发行和验收不倒写。

## 用户追加：软件打开与业务就绪分离

缺少参数、无效正则、驱动/组件未安装或未能连接不能阻止客户端显示与参数编辑。启动先显示原生客户端；业务不就绪时保留修改参数/诊断/维护入口，只有作业和外呼受守卫限制。缺少整个 Sportmaster 种子时提供 Application 拥有的固定双位 Production 引导草稿，不悄悄运行模拟。缺少原身份且已有业务数据时不能以新身份覆盖责任，仍可打开界面选择原配置/恢复。

系统必需值均有软件默认：队列64、调用预算1500ms、未清偿作业10000、常驻历史200、写入保留64MiB；心跳用 `StationCode` 的软件候选默认取当前工作站标识（独立字段，可修改），首次新站内部标识station-01；该默认不证明客户取格口编码，也不能自动覆盖旧左右映射。计划备份默认关闭，启用时提供60分钟/10份及当前用户数据目录下的默认备份路径。旧配置明确给出的值不被默认覆盖；原账缺字段的默认显示与待保存候选有区别，保存与应用仍分开。真实IP、实体关联资格、组件取格口编码和业务筛选规则不属于软件级系统默认，不编造客户值。


## PR73 实际字段盘点（当前实现，V2重排之前）

以下保留PR73的真实字段、默认和校验，不代表V2目标字段归属已经迁移；V2目标以上文为准。


所有可编辑运行字段均通过同一完整候选、静态校验、权限/审计/CAS 保存，受控重启生效；没有单独热写设备。下表不是安装描述的新权威。

| 分类 / 字段 | 编辑与默认 | 静态校验 / 作用 |
|---|---|---|
| 系统 StationCode | 可编辑；默认当前 StationId | 1～256非控制字符；RCS心跳联合校验 |
| 系统 QueueCapacity | 可编辑；64 | 1～4096；软件入口队列 |
| 系统 TimeoutMs | 可编辑；1500 | 100～30000ms |
| 系统 MaximumOperations | 可编辑；10000 | 1～100000；未清偿容量 |
| 系统 Retention.ResidentHistory | 可编辑；200 | 1～10000；常驻历史 |
| 系统 Retention.MinimumFreeBytes | MiB表单；64MiB | 1～1048576MiB；未改原字节值无损保留 |
| 系统 BackupSchedule | 启用可编辑；默认关闭 | 原后台权限及暂停守卫 |
| 系统 BackupSchedule.Directory | 可编辑；当前用户 ImporterPc/backups/ApplicationId | 绝对路径及原完整候选目录约束 |
| 系统 BackupSchedule.IntervalMinutes | 可编辑；60 | 15～10080分钟 |
| 系统 BackupSchedule.MaximumSets | 可编辑；10 | 1～1000份 |
| 系统 SessionLifetimeMs | 既有高级完整候选保留；28800000 | 100～86400000ms；仅已有登录策略适用，不新增强制登录 |
| 业务 BarcodeFilter.Pattern | Sportmaster左右共用；新首配无生产默认 | 1～2048字符、语法/整串/预算；旧null严格单码兼容 |
| 业务 Weight / WeightValidity / Images | Sportmaster保持原不适用值 | 其他Application按已有能力显示重量、图像策略；旧字段无损往返 |
| 固定 SchemaVersion / ApplicationId / StationId / Mode /目录 | 当前身份与责任只读；新站由Application默认 | 迁移沿原受控入口，不作为普通参数修改 |
| 固定 Topology.Readers / Positions / ReaderDeviceId / InstanceId /用途 /数量 | Sportmaster双位固定、稳定身份只读 | 禁止扁平化/重复设备/改变责任；旧请求原绑定 |
| 用户显示偏好 /布局选择 | 原用户作用域 | 不加入运行配置修订 |
| 部署 Extensions / CapabilityInstances /角色绑定 | 原安装目录与完整候选 | 精确包/版本/模块/描述；无第二登记权威 |

驱动样例为 hikrobot.idmvs-reader 1.0.3，左右独立两份；下列真实参数均可编辑。组件样例 sportmaster.rcs-task 1.2.1，一个逻辑连接左右共用。缺精确依赖显示不可用，不用此文档代替安装描述。

| 驱动字段 | 默认 / 必填与静态校验 |
|---|---|
| `device.ip` | 必填；最多15字符；规范IPv4；左右不得同IP |
| `device.serial` | 最多32字符 |
| `entity.mode` | 默认 `observe-only`；observe-only / trigger；trigger实际资格留给部署/SDK启动；observe-only不准入 |
| `code.encoding` | 默认 `utf-8`；utf-8 / ascii |
| `queue.capacity` | 默认 `128`；1～1024 |
| `bcr-code` | 最多256字符；RCS心跳必填且各Reader不重复；区分大小写 |

| 组件字段 | 默认 / 必填与静态校验 |
|---|---|
| `source` | 必填；最多64字符 |
| `locale` | 必填；最多64字符 |
| `zone-id` | 必填；1～9223372036854775807；最多32字符 |
| `project-id` | 必填；1～9223372036854775807；最多32字符 |
| `quantity` | 必填；1～2147483647；最多10字符 |
| `request-id-base` | 必填；0～9223372036854775806；最多32字符 |
| `request-id-count` | 必填；1～9223372036854775807；最多32字符；base+count不得溢出；原持久编号责任不变 |
| `left-station-code` | 必填；最多256字符 |
| `left-bcr-code` | 必填；最多256字符 |
| `right-station-code` | 必填；最多256字符 |
| `right-bcr-code` | 必填；最多256字符 |
| `specified-chute` | 必填；false / true |
| `chute-code` | specified-chute=true时必填；false时必须不提供；最多64字符 |
| `heartbeat-interval-seconds` | 默认 `5`；1～3600；最多4字符 |

组件 Target 可编辑，须为显式 HTTP(S) `/rcs/sorter/api`，禁止凭据/query/fragment。RCS文本拒绝空白/控制字符；原左右取格口编码与系统 StationCode、驱动 bcr-code 独立保存，不互相覆盖。其他已安装版本只呈现其实际描述，未声明参数不补造。
