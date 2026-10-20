# Sportmaster 任务与对象 UI 重设计：实施裁决

日期：2026-10-20。代码复核基准：`e2e69292899c709056ce9ab6159bf01f6f18de6c`；下列源码路径和行号均固定于该提交。执行实现时继续继承最新 main，核对并行变更，不回退代码。**本次提交设计资料和实施计划；新界面尚未移植到 WPF，未新增构建、Windows 或设备验收结果。**

当前目标为 [20261020 完整设计集](../ui/sportmaster-task-redesign-20261020/README.md)：45个命名界面状态、48张高清/紧凑PNG；原交付清单共95文件，包含重复格式、源码、交接、QA及整包，不代表95份独立界面设计。58项字段/状态与39项动作另有迁移映射。设计以36张原图（35项唯一内容）为依据；原字节身份保持，ZIP的分片存储与校验恢复不增加设计数量。最新代码核实通过本裁决补充；HTML及其中计数、示例地址和成功状态只用于模拟。实施顺序见 [UIR-00～06 工作包](../work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)。

## 1. 继承实现，完成新的任务闭环

现有 C#/.NET10/WPF 已具有双位投影、同一 Application 多页面、完整配置账/CAS、加密未完成草稿、受控启动、恢复和原生安装。#62/#63/#64/#66/#67/#68 均已合入基准；诊断 OneWay、单码拒绝分类、classic 最小窗口等修正已存在。新增工作的重点是对象导航、适用表单、错误定位、差异与生效解释、带对象的诊断和跨页作业感知；完成全部任务及异常状态，不能以首页美化或示范表单代替交付。

Sportmaster、DirectPlc、WcsToPlc 保持独立 Application。Sportmaster 左右 reader 请求格口，不下 PLC；登录、称重、采图、上传按真实组合显示，不增加每包确认或强制操作员登录。界面调用应用契约，继承资源所有权、原请求和数据库守卫。已有 `SupplyStationWindow.xaml.cs:25–81` 共享页面/辅助窗口；`src/Ipc.Application.Sportmaster.Desktop/SupplyStationWindow.Diagnostics.cs:30` 已为 OneWay。更改共享 ParcelFlow 控件须保留其他 Application 的必要能力。

## 2. 导航与对象：先回答正在改谁

主导航为“作业 / 记录 / 设备与设置 / 诊断”。设置对象为“配置概览、左读码相机、右读码相机、WCS服务、位置与绑定、运行规则、已安装组件”；首次配置、安装维护、模板/备份、旧格式核对、支持是相应任务入口。记录/设置/诊断继续显示左右位置、当前结果有效性和真实阻断摘要，直接定位相关对象，复用现有投影。

| 用户任务 | 实际对象与裁决 |
|---|---|
| 选择驱动 | 已安装 `CapabilityDescriptor` 的精确实现/版本/包/模块；显示名不是可替换身份。 |
| 配置读码相机 | `Extensions.Instances` 的 observation 实例及参数；实例 ID、稳定 DeviceId、物理身份分别显示。 |
| 建立左右对应 | `Topology.Readers` 与 `Positions[].ReaderDeviceId`；左右由 Application 拥有。运行内稳定 DeviceId 只读，更换走受控迁移，首配可按原流程录入。 |
| 配置 WCS/RCS | Wcs 角色引用类型化 `CapabilityBinding`；Target 属组件协议。不得启用被 Parcel 拒绝的第二套 `Extensions.Services/Ports` 权威。 |
| 配置可选图像 | `CameraInstanceId` 是采图能力，和读码 Reader 不同。不能把“右相机”接到 Camera 或因此新增包裹预览。 |

上下文常驻 Application、站、位置、对象名、实例与编辑来源；技术指纹按需展开。对象切换保留同一草稿，跨对象变更统一汇总。依据：`src/Ipc.Application.ParcelFlow.Desktop/StructuredConfiguration.cs:142–178,206–244`；`CapabilityEditing.cs:41–94`；`src/Ipc.Assembly/ParcelCapabilityFactory.cs:105`。

## 3. 真实字段优先于模拟表单

统一消费 `ParameterField` 的 Group、Label、Type、Unit、Required/条件必填、Choices、范围、敏感引用及只读语义；目前 ApplyLevel 为 Restart、EditRole 为 Maintenance。字段错误需有对象/路径/错误码，摘要可跳转首错并保留输入；未知错误保留全局原文。数字非法文本不得变0，字符串列表须保留空项和转义，省略默认值及未改参数不能被重排写回。敏感引用按合同遮蔽，不凭空增加账号密码。元数据依据：`src/Ipc.Platform/ExtensionContracts.cs:14–18,68–123`；现有渲染入口 `src/Ipc.Application.ParcelFlow.Desktop/MaintenancePage.xaml.cs:74–99`。

### 海康 reader 的五个实际字段

| 字段 | 当前 schema 与分组 |
|---|---|
| `device.ip` | 连接：必填 IPv4，最长15；无默认，构造器要求规范地址并拒绝全零/全广播。 |
| `device.serial` | 身份：可选预期序列号，最长32；空值仍保留实际序列号防重复占用。 |
| `entity.mode` | 实体关联：observe-only/trigger，默认前者，只观察不能准入业务。trigger 需要设备已有触发模式及实体关联证据，不是设置硬件触发的开关。 |
| `code.encoding` | 解码：utf-8/ascii，默认utf-8；严格解码，不猜测编码。 |
| `queue.capacity` | 驱动容量：整数1–1024，默认128；区别于 Application 输入/作业容量。 |

来源：`src/Ipc.Drivers.Hikrobot/parameters.json:1–53`、`HikrobotReader.cs:44–53,72–85`。该实现没有相机端口、曝光、口令或复位字段；原型9000对它不适用。上述范围只属于该精确驱动，不是现场推荐值。IP属于实例，左右属于拓扑。

### RCS 的真实服务配置

所选 `sportmaster-rcs-task` 有13项参数加 Target，必须完整覆盖：连接目标；项目标识 `source/locale/zone-id/project-id`；请求内容 `quantity/specified-chute/chute-code`；独占编号 `request-id-base/request-id-count`；左右映射 `left/right-station-code`、`left/right-bcr-code`。来源为 `src/Ipc.Integrations.SportmasterRcs/parameters.json:1–121`。Target须为明确HTTP(S)地址且路径为 `/rcs/sorter/api`，不能含凭据/query/fragment；指定格口与格口值成对适用，编号区间不得溢出（`RcsTaskOptions.cs:23–38`）。缺省未声明的字段不编造；quantity是每次实体准入配置数量，不移植成旧共享箱计数。

Application“调用超时”当前为全局预算，不能改称单相机/WCS专用超时；输入队列、未清偿作业容量、常驻历史、磁盘保留、备份策略各守原单位与范围。依据 `src/Ipc.Application.Contracts/ParcelContracts.cs:40`、`ParcelMaintenance.cs:4`、`src/Ipc.Application.ParcelFlow/ParcelConfigurationRules.cs:17–31`。驱动IPv4、RCS URI等构造器语义需补安全前置校验及字段定位；不能为校验启动SDK或发送业务。

## 4. 编辑、保存、应用、准入分别呈现

对象页只是编辑范围，保存仍合并为完整 `ParcelConfiguration`，沿唯一配置账与原CAS；不建立左/右/WCS独立存储。草稿绑定加载时的配置 Revision及RuntimeId，后台刷新不推进；表单与高级JSON只有一个编辑来源。一次完整修订填写一次维护原因。现有保存要求软件暂停、维护认证和排空，界面应提前解释并引向原维护操作，不能删守卫让原型按钮直接成功。普通显示偏好沿独立用户偏好即时保存。

| 状态 | 呈现及可执行结果 |
|---|---|
| 未改 / 未保存 | 显示编辑基线及变更对象数；普通切页保留草稿，无需反复弹窗。撤销只撤本次编辑。 |
| 校验中 / 字段或关联错误 | 固定忙碌反馈，首错可定位；异步结果仅对应原候选，其他输入保留。 |
| 保存中 / 已确认写入前拒绝 | 禁止重复提交；只有明确未进入写入的拒绝才显示“未保存”。CAS冲突保留草稿并比较，不能覆盖他人修订；普通Rejected回执本身不是写入前拒绝的证明。 |
| 保存结果待核对 | Stage落盘后，审计/返回失败既可能抛异常，也可能返回普通Rejected；仅凭回执名不能宣布未保存或允许重试。应用须结合规范化候选指纹和原配置账核对；UI保留输入并显示“保存结果暂时无法确认”，核对已存相同候选后展示其真实状态，不重复提交。 |
| 已保存待重启 | 展示真实Pending及完整变更范围；明确运行内容未因此切换。 |
| 应用中 / 应用失败 | 使用原受控启动尝试、资源释放和失败记录；保留失败候选、旧确认内容及原责任。不能用Reconnect冒充应用或声称自动回退成功。 |
| 运行未知 / 已应用 | 最后确认内容与当前证据分列。Effective字段存在本身不证明启动：Seed/LegacyEnvelope可已有Effective且Confirmation.Required=true。只有匹配内容指纹的成功activation/confirmation才能证明对应构造/启动阶段；仍不证明当前健康、实体许可或允许供包，还需新鲜快照和各位准入事实。 |

依据：`src/Ipc.Platform/ConfigurationRevisions.cs:9–33,57–165`；`src/Ipc.Application.ParcelFlow/ParcelCommands.cs:121–155`、`ParcelRuntime.cs:119–178`、`ParcelHealth.cs:38–56`、`ParcelGuards.cs:7–13,60–65`。Revision是整个配置信封修订，不是原型独立自增的“编辑/保存/运行”三套版本；显示应关联真实内容指纹、RuntimeId和确认时间。不可把业务快照Revision当配置CAS。

离开会覆盖输入的任务、重读或关窗前提供“继续编辑 / 保留加密草稿 / 放弃本次输入”，沿现有无效草稿与多窗关闭保护。恢复上一有效内容是有证据的内容形成新候选，再校验/CAS/重启；恢复默认只使用已声明且适用的默认值，先列本次差异。配置历史查询可补应用拥有的受限只读契约，不直接读SQLite或另建审计平台（`ParcelConfigurationAudit.cs:7–11,32–40`）。

## 5. 连续作业与诊断的真实能力

左右卡固定位置，按Position、Operation、Admission和Request关联异步结果；新观察失败、未准入和原未决作业分别显示，旧格口失效，成功表达“取得格口”，不冒充物理完成。一路异常是否影响另一路由Positions/Availability和共享依赖事实决定；单件Unknown不能直接写成全站WCS断线。保留现有只通知变化、列表选择/滚动和焦点稳定机制（`src/Ipc.Application.Sportmaster.Desktop/SupplyStationViewModel.cs:115–147,170–212`）。

诊断以“对象与影响→已有证据→可检查层→相关参数→适用恢复→返回作业”组织。地址格式、传输可达、设备连接、收到帧/码、业务回应与实体准入各自有时间和来源。没有通用草稿安全连接测试契约：测试不保存、不应用、不发送真实业务，缺某层就显示未支持或未验证。海康独立Probe会取得物理资源，不能在运行旁直接抢占。定向重连已有暂停、维护认证、资源排空/epoch守卫，结束仍暂停，不承诺另一路继续供包（`src/Ipc.Application.ParcelFlow/ParcelDeviceLifecycle.cs:37–98`）。

当前 RCS Health仅本地客户端生命周期，Reconnect只重新初始化本地客户端；显示“客户端已启动，上游未确认”。`Requirements.Query=false`，QueryAsync返回null，原型“核对原请求”不得接POST；应在按钮前说明“当前协议未提供结果查询”，保留原件和责任并提供实际适用处置。业务防重已在应用守卫执行，补前端能力说明即可。NEED_UNPACK沿现有检查移除任务，不新增拆包提交。依据：`src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs:39–65,67–107`、`modules.json:9–16`；`src/Ipc.Application.ParcelFlow/ParcelStages.cs:16–21,195–216`。

## 6. 首配、维护与安装完整衔接

首配复用原目录/设备/策略/离线分层检查、加密草稿、保存待启动和修复；后续维护直接定位对象，不反复走向导。模板导入先显示适用对象与差异，再改草稿：现有安全模板仅允许queue/timeout/operations/resident/reserveMiB、非Backup策略和精确provider选择；排除站/设备/IP/目标/凭据/编号/业务数据及备份路径。不能按模拟模板迁移相机地址（`src/Ipc.Application.ParcelFlow.Desktop/StartupSiteTemplates.cs:9–18`）。完整备份与隔离恢复为独立任务，保留原目录、完整恢复集和责任屏障。

记录保留固定查询/选中原件及导出边界；支持摘要复用白名单预览：范围最多7天、最多256事件，预览5分钟内可导出同一份，注明截断/过期，非采集最近5分钟（`src/Ipc.Application.ParcelFlow/ParcelProductSupport.cs:13,25,46,62`）。

继承“菜鸟供包台”名称/图标、安装目录和两个快捷方式选项、原helper/UAC、并排升级及BUILD-INFO。最新已发布英文EXE是SM业务1.4.1、运行源码7c384540，41项是该原介质的本机Simulation实际升级证据；新UI不得重标旧EXE或借用其PASS。正常人工单EXE安装继续为主入口，不重建安装器或强加批量部署。来源：[现行安装发行指南](../runtime/installer-release-info-01.md)，冻结源码中的第21–45行。

## 7. 视觉、清理与验收裁决

采用新设计的克制浅色、稳定双位、对象表单与统一控件；中文/数字层级、间距、焦点、禁用原因、长内容和忙碌状态按原交接完整实现。1600×900逻辑为主，1366×768补查；现有正常作业下限800×420 DIP、1280/680紧凑阈值及不足空间暂停保护仍继承，不能抬高下限或删严格几何断言。800×420应验证当前条码/格口仍在本位视口、原责任与主要操作可达；表单允许纵向滚动。依据 `src/Ipc.Application.ParcelFlow.Desktop/ParcelPresentation.cs:74–81`；`src/Ipc.Application.Sportmaster.Desktop/SupplyStationWindow.xaml.cs:85–86`；`tests/Ipc.Desktop.UiVerification/UxCommercialClosure.cs:288–359`。

编码前将AGENTS/INDEX/README/UI STATUS的旧v1.1当前布局要求、旧视觉产品化“本轮”及旧安装现行说法撤出现行入口，指本设计、工作包和最新发行；在既有pre-*方式保留原字节，归档指令用.txt。原设计95文件、旧图集、REQ/BR/CAPI/D/RV、失败、PDF和旧介质身份不倒写。manifest新增本次设计交付状态，不将当前7c已验证发行改成新UI已验。

实现完成必须通过六核心任务及相关失败场景，提供同版WPF大图、交互记录、字段/动作去向与运行说明。原型QA、原生合成视觉树、Windows实际输入、真实设备/服务/现场分别登记。既有233项WPF组合和41项升级均属原源码环境；真实海康现有记录仍0帧/0码，目标系统/DPI/输入、trigger实体关系、客户RCS及现场资格按实际未验部分列明。实现不能因缺远端合同停在半张界面，也不能用假成功补齐协议能力。

