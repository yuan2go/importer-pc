# WP-IPC-ARCHITECTURE-QUALITY-CLOSURE-01 · 整改设计

开工main **2126ac2c06a0c352d96afbbf13ec1a719c352738**。2026-10-18。业务代码前形成；用户本包给出的职责、业务拓扑、兼容及交付要求为本次授权，不改变F/D批准。历史入口原字节见[归档](../archive/pre-architecture-quality-closure-01/README.md)。本设计与22/19/20共同生效，冲突处以本次责任收敛为准，不能以降低目标关闭问题。

## 输入、现状与方案取舍

独立审查的仓库外REPORT.md和FINDINGS.json固定同一main，列AQ-01～06；当前取得的是Markdown/JSON和合成探针。原PDF尚未取得，不转换Markdown生成“原始PDF”，该归档项BLOCKED，待原件才能校验及单独提交main。所有问题都要复核源码及当前试验，不继承旧PASS。

形成时依赖PR #23（开工head 966ec6aa17dfe29b96fc9b9113f3037ba586f6a0）修复采集保有，当时尚未合并；其有效成果现已随PR24进入main。继承其实现并标明来源，不重建第二套保有协议；本包分支纳入该依赖，禁止修改对方工作树或替对方合并main。公共契约1.5.0来自该PR，本次新增契约使用新不可变版本。

比较三个方向：仅加守卫不能解决容量和维护闭环；重建通用工作流/服务框架会扩大迁移面；采用现有业务共享层的定向责任闭环。三个Application继续独立声明流程、先决和完成，Platform只提供设施。Sportmaster左右独立且只取WCS格口；另两条主流程各main顺序位。旧Supply/Inspection保持原SQL/协议和运行根。

## AQD-01 容量、归档和历史

业务区分内存工作集、未结责任与完整历史。追加数据库迁移，保留SQL1～3/checksum。受控策略只将已满足本业务终态、已释放原图、无在途/原回执/附加责任的完整原JSON从工作表事务性移入归档表，保留所有请求/响应/测量/审计和唯一Admission/Observation索引；不是删除历史或提高上限。自动收敛有界工作集，不自动删除磁盘历史；维护归档命令要求暂停、维护身份及原因。未结责任本身达上限则明确阻断，不能归档解锁。

准入/恢复/快照使用同一容量事实。历史用有界分页查询，详情按原ID读取；归档身份仍参与去重和跨位冲突判断。备份含同一数据库全部归档，恢复保留去重。磁盘历史外迁/删除需另行批准及真实留存预算，本包不推断客户保留天数。

## AQD-02 原件恢复与阶段隔离

启动在端口构造及候选激活前验证seed、持久Effective/Pending与Application/StationId/Mode/目录身份；恢复在创建目标前比对原身份及目录，不修改原模式。所有重启未结件建立恢复屏障，包括无Stage的WaitingWeight。重量可关联原件但不得自动发送，操作者显式确认原实体后才允许继续；期限不延长，不伪造传感器许可。

每阶段拥有StageFact/Failure及独立接收发生。Attached上传所有失败、Unknown、原能力缺失只更新上传责任，不能改主链状态/格口/PLC事实/主原因。核对可选择阶段；仅原阶段的未清接收阻挡该阶段，主链依赖屏障由业务定义决定。已接受事实不因查询失败倒退，原件缺失不能用另一查询清偿。

过载有稳定发生身份，确认事务写入原marker身份与命令回执后才删除marker及解除内存。提交失败保持阻断；已提交但文件清理中断可在重启根据原身份幂等收敛，新增过载不能被旧确认清掉。

## AQD-03 设备、健康与故障归属

每物理设备一个串行生命周期门，reader与其取图门面共享同一键。采集/查询/重连/停止/释放不得并发替换底层会话；外部等待不持有聚合锁。维护互斥并使用停止联动预算，取消/超时不能声称已停止，提供者未返回时保有原资源及租约。

每设备epoch/sequence围栏保持，旧回调不可更新新注册。健康统一验证实例身份、确认时间及有效期；错误归属于实际调用的端口。当前有效投影不改原来源时间，不读时续期。跨位Admission/Observation内容冲突写独立持久责任，阻受影响位置，暂停且维护检查说明后可确认，保留原件和冲突记录。位内顺序/位间独立/有界队列及失败保租约不改变。

## AQD-04 UI目标与完整配置

当前投递位与历史选择分开。工作台命令只针对正在显示的本位当前作业；历史命令只针对详情所示选择，不隐式回退到隐藏目标。刷新/切页/双窗保留本窗展示身份，服务仍执行CAS和业务守卫。Sportmaster左右结果/状态/格口分别展示并可选择；不新增图片区域或DirectPlc完整UI。

保留原HTML/CSS/JS及完整交接为视觉和信息层级输入，不复刻旧共享箱/数量/AGV规则。完整schema3候选JSON编辑、校验、保存/受控重启贯通；旧schema表单仍禁止覆盖拓扑。安装清单/模块/能力/设备/位/健康/业务可用分开展示。每次配置保存/激活记录维护主体、原因、原/新修订和哈希、阶段/结果；文件权威和DB审计用持久意图及启动收敛处理跨文件中断，不能静默丢审计。

## AQD-05 扩展和兼容

Parcel拥有自己的只读消费者检查：Effective/Pending、未结作业原包、独立回执及备份配置。管理入口复用ExtensionStore永久身份、反向依赖、包租约、安装操作恢复；混合Supply/Parcel消费者由装配选择各自读者，不让Platform解释业务SQL。不认识、缺失、损坏或未来格式消费者均拒绝卸载。备份注册为离线消费者；不凭运行进程已停或绑定已删判定无责任。

公共契约变更须新不可变版本及精确宿主指纹重建签名包；旧包不覆盖、不重签。新库旧程序拒读，升级前完整备份；DB1/2/3均须在迁移标记升级前清偿未结责任；新宿主精确实现变化不能用追加表结构证明旧未结可执行。迁移SQL不重写已结JSON。回退程序只使用升级前匹配备份且先核对外部动作，禁止业务账倒退。schema3保持完整拓扑；原请求目标/合同/实现/payload/hash不变。

参考HTTP业务响应使用ResponseHeadersRead，限量流读取至上限+1，头体共用同一取消预算；无Content-Length也不能先无限缓冲。

## 验证和交付边界

按风险执行真实Application/装配/SQLite定向验证：容量循环/归档重启去重/分页、阶段故障隔离/独立回执、双位慢调用、采集重连互斥/晚回调、持久身份冲突、UI目标、配置CAS/审计中断和扩展安装恢复/消费者卸载。补AQ-01/02/03真实复现的修复检查；必要影响组和构建后只打必要候选/运行参考进程。

每项记固定源码、命令、退出码和原失败证据。Windows实机/交互/UAC/ACL NOT_RUN；真实SDK/WCS/PLC合同/实体/复位/联锁/现场BLOCKED，提供后验步骤。业务和文档在开发分支提交main PR供审查，不合并main；只有取得的原始PDF按用户单独授权归档main。保留REQ/BR/CAPI/D/RV和原历史验收。

补充所有权决策：Application入队时脱离驱动扫码缓冲区；每次外部提交/查询传递独立请求副本，原请求和冻结配置不授予适配器修改权。完整和兼容配置表单的CAS绑定草稿读取时修订及运行实例，不随投影刷新推进。

## AQD-06 激活确认与断进程

收尾只读发现并行审查02 PR25指出同一配置可靠性范围的资源成功前生效窗口。复核当前实现后纳入本包修复，不合并该文档PR，也不把其PDF当成审查01。配置账保留最后确认Effective及完整Pending；新增激活尝试身份/时间，先持久ActivationInProgress再构造/启动资源，全部启动成功后原子确认Effective。进程中断后保留旧Effective、Pending及失败，禁止自动重试候选，须维护者重新校验/保存。运行启动期间候选只供当前构造使用，不发布为已确认配置。资源失败继续完整Stop/Dispose后才允许新Owner；配置恢复不代表资源已释放。

旧无尝试字段配置可读；新字段可缺省。保留原Activate供Supply兼容，Parcel采用两阶段入口；旧Activate只识别PendingRestart，因此不会重试ActivationInProgress。不得用旧程序原地回退活动尝试，仍走完整备份/原发布恢复。最终候选须在本修复后新建精确包，先前产物身份保留。

AQD-05补差：实际签名包复现仅Pending含包、重启激活被旧件屏障拒绝时，未初始化Store的Effective不能作为备份登记依据。备份复制前分别登记Effective和Pending，备份读者继续核对原COMPLETED及配置hash；清偿live后该离线Pending仍必须阻卸载。

## AQD-07 PR24独立复审整改

以下为PR24形成时设计及执行记录，保留原基线、任务范围和证据；其数据库4与旧样例版本仅描述该阶段。现行PR26操作使用DB5及下文独立资料/旧账确认规则，合并身份见本节末尾。

2026-10-18用户已授权修复五项并更新同一PR，不合并main。本轮基线PR a2e987a8d15eb397ad1a40cf1a303bdad4ec397b、main b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d；原审查探针和输出保持原身份，前轮3d69607候选及PASS不改写为新实现资格。

选择复用现有事务、业务读者及维护机制：不提高回复上限、不清原raw/未知责任、不把业务SQL放入Platform，也不因混合消费者加载SDK或业务运行实例。

1. R01：StoreRaw与RecoverRaw共享“发生元数据+回复ID/hash”事务。恢复同ID不同内容时保留最早已知hash，让正常ApplyReply转Conflict；旧DB4已经恢复却未建索引的原件，启动从全部持久raw/归档BLOB按原发生顺序补齐校验，冲突原件拒绝启动，不依赖有界Stage.Replies，不改SQL1～4或原响应字节。
2. R02：Attached Dispatching→Unknown仅更新Upload.Failure，保持主State及Reason；Prepared从未发送仍保留显式继续门禁。
3. R03：离线读者先验证Effective、Pending、未结FrozenConfiguration版本/核心身份和包绑定闭包；未知枚举、未来schema、悬挂包/模块/reader或矛盾实例均保守拒绝，校验不启动端口、不要求包仍安装，不以零引用替代不确定性。
4. R04：装配根按只读数据库格式身份选择Supply或Parcel各自既有读者；混合/歧义/未知格式拒绝。Supply与Parcel管理入口都用该分派。为离线维护显式包含对方读者依赖，不创建对方Application实例、不引入对方主流程、不执行扩展代码；数据库语义继续由各业务拥有。
5. R05：维护登记使用规范化物理设备键及服务实例键。登记先于释放聚合锁；依赖该资源的准入、首次发送、继续上传及查询均拒绝/保留原意图，其他位/服务仍可运行。设备维护等待原采集，服务维护先检查原外呼排空；预算取消但提供者未返回时仍保持围栏/租约。完成后清除围栏并要求当前健康，回执准确记录实际暂停状态，不谎称全局仍暂停。

### PR24五项整改执行记录（历史）

目标：闭合R01～R05并用真实Application/Factory/SQLite及精确签名包定向验证。技术栈仍.NET10.0.402/SQLite；业务分支codex/architecture-quality-closure-01，保留数据库4、Supply5、Application.Contracts1.6.0语义及原不可变包。

- [x] 回执/Attached：修改ParcelRepository.StoreRaw/RecoverRaw/ValidateRawInventory及ParcelStages恢复原因；新增ReviewClosure回执落盘中断、65次后续回复/同ID篡改Conflict、旧DB4索引补齐/矛盾原件、Attached已完成主链重启断言。发生和身份均在事务内提交，Request命名空间由原接收发生而非外部返回字段确定，永不重发Submit。
- [x] 维护围栏：ParcelDeviceLifecycle新增资源占用查询，ParcelGuards、ParcelStages及健康投影使用同一围栏；以延迟WCS重连和左右独立设备维护验证Resume/已准备意图/新件/原Query被正确隔离、无关位继续、停止和超时保有资源。
- [x] 离线维护：ParcelConsumers验证完整配置及冻结责任；Ipc.Assembly/ExtensionConsumerReaders显式分派并接到Factory/CLI/Product入口；测试未来Pending、悬挂绑定/冻结包、未知根、真实混合Supply/Parcel数据库以及仅Pending离线备份。所有异常保留包和消费者登记。
- [x] 交付：锁文件按实际新增维护依赖更新，不升级NuGet；新精确宿主单独构建不可变签名样例，真实安装/恢复/卸载旅程；必要核心/WPF及旧产品编译、选装依赖和参考进程冒烟。新候选不覆盖原产物；Windows/SDK/现场及原审查01 PDF缺件继续保留。
- [x] 按固定源码登记必要命令、宿主/包/候选hash及原失败；资料工具与既有267项错误集核差，提交推送同PR24供复审，不合并main。

关键命令：`dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false`；`dotnet Ipc.Application.Verification.dll . --review-closure`（本轮定向入口）；受影响`--architecture-quality`、`--application-architecture`及签名`--quality-extensions`，以实际风险决定。每步先保留复现失败，再记录修复后结果；不运行无差别全量测试。


本轮固定实现与候选7850fcc02b04d1b572fcb89215945ee0f60be28c，实际定向结果及失败保留见[复审结果](../runtime/architecture-quality-closure-01-rereview-results.md)。形成时结论为参考验证通过、待独立复审；后续独立复审与合并身份见本节末尾；不变更D/RV或Windows/现场状态。


## AQD-08 R03兼容库存校验闭环

2026-10-18用户明确授权修复独立复审剩余R03，不合并main。基线PR24 head **3f6f762d31655ef0fe8d6562ed413beed682e2f9**、main **b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d**，开放PR23/26保持独立。schema1/2的Effective/Pending/未结FrozenConfiguration可携带同InstanceId不同包的矛盾库存，All去重后返回零引用，不能以schema3测试关闭此问题。原独立审查、原件与入口保持身份，见pre-pr24-r03归档。

已获本轮用户确认的方案：在Parcel共用配置规则中先校验未去重的角色与完整CapabilityInstances，再进入拓扑/业务解析。仅在离线读者加特例会留下其他入口不一致；修改All或自动取首项会放宽歧义/改变公共契约，因此不采用。三个Application及schema1/2/3统一规则；Factory的独立校验入口也在Bound/DistinctBy前调用同一校验。

规则：库存至多64项、非null且InstanceId唯一（相同内容的重复库存也拒绝）；不同角色不能占同一身份。角色与库存中的一次合法镜像必须保持全部绑定内容一致，包含kind/实现/version/目标/合同/参数/Enabled/包/模块。原始每项都检查已知能力kind、精确绑定身份及包引用；包ID/version/manifest hash合法、声明集合唯一、Package/Module/Implementation/Version相符；无Package不能携带扩展Module/Implementation，disabled项同样不能隐藏引用。先验证这些事实再做All/Bound去重，不加载扩展/SDK，也不以包当前可启动作为历史读者前提。

没有新的数据库/配置/公共契约/扩展协议版本；不修改FrozenConfiguration或其hash、原SQL、原包或旧请求。非法原件保留并明确阻卸载，合法旧schema与合法角色/库存镜像继续读取。现有旧候选与1.6.8/1.6.9精确包维持原源码/二进制身份，不能标成本轮候选；本轮不重复打包整个产品。

### PR24 R03执行记录（历史）

目标：闭合剩余R03，并保持合法兼容配置的完整引用与只读证据。技术栈.NET10.0.402/SQLite，分支codex/architecture-quality-closure-01；沿现有计划逐项执行，无需新业务实例或通用框架。

- [x] 先保存独立复审原报告/合成探针和旧入口字节；修正README/INDEX/指南的当前状态，提交文档治理。
- [x] 新增定向--inventory-closure：schema1/2（及schema3控制组）分别覆盖Effective/Pending/未结Frozen；同ID相同/不同内容、角色镜像矛盾、悬挂包/模块/版本/未知kind必须拒绝且DB/config原bytes不改。合法旧schema/完整镜像/精确包引用须仍可读并保有未结包。先记录修复前失败。
- [x] 在ParcelConfigurationRules新增统一原库存校验，先于All/ValidateTopology；删除schema3专属重复实现与离线重复绑定校验，Factory入口复用；保持reader专属校验与真实安装资格机制。
- [x] 只做受影响定向验证：--inventory-closure、--review-closure，必要真实Assembly/Product构建；按风险核配置验证和卸载原入口，不无差别全量测试/重打旧候选。
- [x] 固定源码、命令/返回码、原失败与只读hash证据；更新同一PR供复审，核main未变，Windows/SDK/现场及原PDF缺件继续NOT_RUN/BLOCKED。

固定源码445e1f62bf01fe21e5f13576d2f483292571b709；执行及同PR交付结果见[本轮记录](../runtime/architecture-quality-closure-01-r03-results.md)。形成时参考验证完成；后续独立复审已归档，Windows/现场资格仍另行判定。

2026-10-19归档/合并授权：独立复审固定f862c2ae2ad00ab79ceff5e2013c2a5593d05234，限定参考范围无剩余代码阻断。原报告及证据见[归档](../reviews/pr24-independent-rereview-f862c2a/README.md)。用户授权仅归档及精确head合并，不重复测试/打包，不改业务/测试/配置/构建输入，不关闭Windows/真实SDK/协议/现场或PDF缺件门禁。前述“不合并”保留各次任务形成时范围。

PR24已由独立授权合并：归档前复审head `f862c2ae2ad00ab79ceff5e2013c2a5593d05234`，归档后最终head `f531d3a6882891cfc7870f5dcccfe985009d6be4`，main合并提交 `4afe2afacde9e2acab7648fff3440fa3cc331b6e`。完整[独立复审归档](../reviews/pr24-independent-rereview-f862c2a/README.md)及历史失败不改；以下PR26差额继续使用DB5及独立资料责任，不以PR24当时DB4或历史包版本作为当前发行指令。

## 审查02 N01–N07差额设计（本次实施范围）

审查02固定2126ac2，执行main b2dc55b，继承PR24 a2e987a（含PR23 966ec6a），两PR开工均OPEN。审查01 AQ与其缺PDF记录保留原身份；审查02原PDF已在main，hash dce6b0e235c44e414cc14a976dbd3cea0d24f634aab1276be894b6b69b62283d。本节是具体差额设计，不替换原始审查或D/RV。

- N01/A09：整合README/INDEX、现有22运行、扩展和UI STATUS；Supply操作单独标识，随包按产品选择。替代操作原字节见pre-architecture-quality-02，设计22/19/20仍有效内容不归档删除。
- N02/A01：复用已有Begin/Confirm、Interrupted尝试和原三断进程测试；不增加配置账。核部分资源失败排空及显式restage。旧无尝试配置只作为历史输入，成功Start才有当前运行确认，不能声称已找回旧程序遗失值。
- N03/A02/A04：修Attached重启主Reason；依据持久且验hash的主链原回复恢复旧Blocked投影，恢复前完整JSON写原审计。原请求/原回复不改。维护暂停及认证后，允许对缺raw的原Awaiting执行原Query；查询产生自己的原件和T3，只有有效权威终态才关联独立Query的结果依据；旧raw缺口仍作为独立资料责任保有。原发生ID/Source/空raw_hash保留，审计保存新查询发生及检查说明，绝不制造旧raw或清RawDurable丢件。Query无结果/冲突/无原能力保持隔离。旧DB升级屏障和匹配旧发布清偿继续执行，离线局部投影修复不授权控制动作。
- N04/A03：复用DB4工作集/事务归档/永久去重/分页/raw和备份。补完整恢复集搬迁至新卷的容量预检和实际恢复入口，按实际字节加配置磁盘保留计算，保留原集、不自动删历史；不设客户保留期。新卷恢复仍隔离，旧根停止后才能启用。
- N05/A05/A06：继承双位目标与完整编辑器。使用两个实际ParcelViewModel同时加载/编辑/自动刷新/保存验证CAS和另一位/实例不丢；Windows双窗交互单独NOT_RUN。
- N06/A07/A08：继承Store恢复/卸载与Pending备份测试；包选装采用compositions闭包加重复--extension-package输入，校验完整依赖/目标精确公共DLL，实际只复制所选.ipcx及身份锁，不夹带私钥/信任策略。随包提供安装指引，安装/绑定/激活沿原入口；Release签名旅程核验宿主匹配。
- N07：沿打包工作复用产品映射/指南集合，沿恢复工作将主结果解释留给各Flow；不全量改名、不新建恢复/插件框架。未改变公共契约就保留ABI，公共字节变化则新不可变包。

采用现有设施定向修补。仅加阻断不能解决恢复，另建通用框架会扩大责任；本方案补既有对象的完成路径。验证以已有architecture-quality、AA22、资源/扩展组和必要打包启动为主，原失败输出保留，Windows/SDK/协议/现场分层。

## PR26独立复核修复设计（业务代码前）

本轮开工main b2dc55b，PR26原头592d2bdd；PR24后续3f6f762的b82b8d0/7850fcc及最终R03原库存校验已评审继承，永久回复身份、严格消费者读者及维护围栏保持其原证据身份。PR24已由独立授权以f531d3a合并为main 4afe2af，本任务同步该main，不代合并其他PR。原审查5项P1/2项P2及旧操作字节见pre-pr26-rereview归档和本次证据输入；前次PASS仅表示当时断言，不证明新缺口关闭。

- 原结果与原资料分开：Query只确认业务结果，不清原Awaiting。追加Parcel DB5资料索引，保留SQL1～4、原发生、原raw hash和查询关联；OutcomeReconciled旧行恢复为缺raw资料责任，已释放历史如实登记资料缺口，不能制造原图。未清资料持续保有原图、原包、容量与归档守卫；原目标权威Query无结果不变更原事实。真正原文件由完整恢复集恢复后沿本地T3清偿；没有原字节或合同处置依据则持续可见，禁止通用清除。
- 旧DB2/3 Attached主状态污染：离线取得原应用所有权及完整备份。使用经逐文件校验的匹配原发布，只启动原上传端口并Query冻结原请求，验证原实现指纹和原结果；不Start reader、不Submit、不换配置。主结果依据原Committed主raw恢复。权威上传终态与独立Query原件提交后，允许显式将只剩资料责任的旧库升级DB5，保持资料索引、原图/包引用和隔离；普通启动仍拒绝未明旧业务。新配置只有业务已终结且每个缺raw均有可信Query依据才可显式过渡，原资料责任不因此归档/消失，旧未知外部动作仍需匹配发布核对。
- 旧无确认配置账：在同一envelope保留原JSON/hash，标记确认未知。资源启动失败/中断持久记录；成功启动仅开放暂停维护入口，需维护身份、当前CAS和说明显式确认。新种子首次启动及新候选只有资源成功才确认；旧账遗失值不由seed猜补。Supply原路径保持。
- 维护围栏沿PR24：按所选资源跟踪在途外呼，重连期间禁止该资源新Query/Submit/认证及相关位接纳；不相关位独立。取消未返回仍保有资源，维护epoch阻旧健康污染。
- 当前指南与模板不再把已占用历史版本作为可重复构建默认。示例构建必须显式指定新不可变版本，发行物以composition.lock/identity和目标公共DLL为准；最终新候选使用新版本，不覆盖1.7.0/1.7.2、1.0.10/1.0.12及PR24身份。

验证顺序：先保存前次原始反例及针对新增缺口的RED，再继承PR24修复，完成DB4缺raw独立责任/本地原件恢复、真实旧发布Query到显式升级、旧账确认失败/中断/CAS、原回复65次淘汰与维护并发定向验证。必要AA22/资源/扩展、WPF构建及新选装Release旅程；Windows实机NOT_RUN、真实SDK/协议/现场BLOCKED。不得为PASS删除有效守卫或原断言，错误旧断言保留原版本并改为验证独立责任。
