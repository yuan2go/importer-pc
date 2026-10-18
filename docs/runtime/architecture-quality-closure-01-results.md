# WP-IPC-ARCHITECTURE-QUALITY-CLOSURE-01 · 交付结果

执行main **2126ac2c06a0c352d96afbbf13ec1a719c352738**；收尾重新fetch及核对，main未移动。开发分支codex/architecture-quality-closure-01。主工作区未跟踪资料及其他工作树未修改。文档治理先提交 **e6582a2**；继承PR #23 head **966ec6aa17dfe29b96fc9b9113f3037ba586f6a0**的采集所有者协议，分支合并 **9226eb9**。没有替该PR合并main。主要整改提交cad129f；升级屏障77e9478/84d6c42、草稿CAS 8ae136f、外部可变数据所有权ee59989。补充资源成功后配置确认54111de、仅Pending含包的备份消费者3d69607。最终业务/UI/随包指南与候选源码固定为 **3d6960756bfd843a1cb78c6648687b45f2da9f1c**；后续提交仅证据和交付文档。e1fcff0、c666efd、ee59989及54111de候选保留形成时身份，当前资格按最终readback读取。

**参考工程整改及定向验证PASS；原始PDF归档BLOCKED；Windows实际NOT_RUN；真实SDK/协议/现场BLOCKED。** 不是旧现场迁移、客户批准或工业现场放行。独立审查原REPORT.md/FINDINGS.json身份见[输入登记](../reviews/architecture-quality-audit-01/input-identity.json)；未取得PDF，未转换Markdown冒充原件，也未直接将业务变更写入main。

[现行运行/维护/兼容](architecture-quality-closure-01.md) · [编码前设计](../design/architecture-quality-closure-01.md) · [工作包](../work-packages/WP-IPC-ARCHITECTURE-QUALITY-CLOSURE-01.md) · [定向证据](evidence/architecture-quality-closure-01/README.md)。

## 整改及固定源码证据

|问题/范围|实际修改及所有权|参考验收|
|---|---|---|
|AQ-01恢复/部署身份|[ParcelIdentity](../../src/Ipc.Application.ParcelFlow/ParcelIdentity.cs)、[启动](../../src/Ipc.Application.ParcelFlow/ParcelRuntime.cs)、[离线恢复](../../src/Ipc.Application.ParcelFlow/ParcelRecovery.cs)比较seed/Effective/Pending和目录中的Application/Station/Mode；构造设备前拒绝错身份，不改原模式|错Mode及错Station均在创建目标前拒绝；同身份完整归档备份恢复通过|
|AQ-02过载清责|[Overload](../../src/Ipc.Application.ParcelFlow/ParcelOverload.cs)、[Repository](../../src/Ipc.Application.ParcelFlow/ParcelRepository.cs)、[Commands](../../src/Ipc.Application.ParcelFlow/ParcelCommands.cs)先将原marker hash、命令回执/审计同事务提交，之后清文件/内存；新marker不由旧确认清除|真实SQLite写锁SQLITE_BUSY失败仍保留原marker原字节和阻断；提交后中断由重启按原身份续清|
|AQ-03重启WaitingWeight|[恢复](../../src/Ipc.Application.ParcelFlow/ParcelStages.cs)对全部未结责任设暂停和主链RecoveryRequired；原重量可关联，显式检查说明/Resume/Continue才允许首次控制动作|无阶段WaitingWeight重启及关联事务中断后均不自动发送；原Admission/样本保持且单次PLC尝试|
|AQ-04历史容量|[History](../../src/Ipc.Application.ParcelFlow/ParcelHistory.cs)、[StorageCapacity](../../src/Ipc.Application.ParcelFlow/ParcelStorageCapacity.cs)、[统一守卫](../../src/Ipc.Application.ParcelFlow/ParcelGuards.cs)分开未结、工作集与历史；终态事务归档完整JSON及原raw字节，永久身份索引、分页、磁盘保留和受控维护|连续真实主链超过小容量多轮后可继续；工作集有界、分页完整、重启/实际备份恢复去重；高保留阈值按真实磁盘读数关闭准入/Resume/投影|
|AQ-05扩展维护|[Parcel消费者](../../src/Ipc.Application.ParcelFlow/ParcelConsumers.cs)、[Factory](../../src/Ipc.Assembly/ParcelCapabilityFactory.cs)、[CLI](../../src/Ipc.Assembly/ParcelCli.cs)提供原操作恢复/安全卸载/操作清单；候选保存持包租约，备份开始前登记离线消费者|实际签名包中断Published后按原操作恢复；Pending、运行资源、离线备份分别阻卸载；清偿后真正卸载并保留历史及永久身份；仅Pending含包且激活失败的离线备份仍独立阻卸载；不可读消费者不返回空引用|
|AQ-06健康投影|[Health](../../src/Ipc.Application.ParcelFlow/ParcelHealth.cs)校验实际端口身份/时间；[Snapshot](../../src/Ipc.Application.ParcelFlow/ParcelCommands.cs)统一当前TTL有效性且保留确认时间；[真实VM](../../src/Ipc.Application.ParcelFlow.Desktop/ParcelViewModel.cs)消费有效投影|错Instance不能使原WCS就绪；过期保留来源时间但VM显示未就绪|
|主链/Attached隔离|[Stages](../../src/Ipc.Application.ParcelFlow/ParcelStages.cs)让失败/无结果/原能力缺失/原raw缺失归属独立StageFact；按阶段屏障，主链先核对；完整raw和reply-ID/hash持久，阶段保留有限必要事实|上传Unknown不污染主Reason/PLC接受；PLC独立查询到PhysicalCompleted时上传仍Unknown且原图保留；重启缺失上传原raw不改已完成主链；超过64次回复仍有全部原件和单次Submit|
|设备并发与维护|[DeviceLifecycle](../../src/Ipc.Application.ParcelFlow/ParcelDeviceLifecycle.cs)同物理reader/采图共享串行门，服务并发保持；维护外呼释放聚合锁，预算取消不冒称已停；[ObservationSession](../../src/Ipc.Platform/ObservationSession.cs)旧epoch围栏|真实采图在途时重连等待；快照和Pause仍可完成；原采集返回后才换reader epoch；AA22/资源组保持双位交错、Stop失败保租约、迟到回调拒绝|
|跨位身份责任|[Conflicts](../../src/Ipc.Application.ParcelFlow/ParcelConflicts.cs)持久原ID/受影响位/原件；配置守卫和维护说明确认清责|改变内容/位置复用Admission同时阻两位，重启同一冲突ID；授权处置后恢复而不重发原WCS|
|配置审计/完整编辑|[ConfigurationAudit](../../src/Ipc.Application.ParcelFlow/ParcelConfigurationAudit.cs)及Commands持久维护主体、原因、原/候选完整配置、hash、前后修订及阶段结果；envelope/DB跨文件中断以原哈希收敛|校验不保存；保存后审计中断仍有Pending和意图；构造前/构造后/部分Start后断进程保留最后确认Effective、Pending及原尝试，重启不自动重试；显式维护重新保存后资源成功才确认并闭合审计；CAS拒绝旧编辑器；完整及schema2参数草稿固定读取时修订/运行实例，自动刷新后仍拒另一窗口旧草稿覆盖；schema3旧表单继续拒绝覆盖|
|UI命令目标|[Targets](../../src/Ipc.Application.ParcelFlow.Desktop/ParcelTargets.cs)、History/Maintenance及Sportmaster所属页面分开当前位与历史所选Owner；按钮和发送用可见目标及显示时修订|实际VM跨位历史选择/刷新仍分别保持Owner；Sportmaster左右reader/准入/状态/格口独立投影；Windows交互另验|
|可变输入/请求边界|[入队](../../src/Ipc.Application.ParcelFlow/ParcelRuntime.cs)复制驱动扫码数组，[阶段调用](../../src/Ipc.Application.ParcelFlow/ParcelStages.cs)每次Submit/Query传递独立原请求副本；驱动/适配器不能修改Application持有事实|实际左位排队期间驱动复用数组不改原条码；WCS提交及PLC查询修改参数不改冻结配置/原意图；72次持久回复仍可核对完成|
|HTTP正文/取消|[ReferencePeer](../../src/Ipc.Reference.Protocol/ReferencePeer.cs)用SendAsync(ResponseHeadersRead)，限额+1流读取，头体使用同一取消预算|无Content-Length的超额chunked正文拒绝；正文停滞按原caller取消，不缓冲完整大正文或回退成功|

四类职责和三个独立Flow未改成配置万能Application。Sportmaster只取WCS有效格口，DirectPlc及WcsToPlc仍各一个顺序位；新业务不套用旧共享箱/数量/AGV。没有新增微服务、事件总线、通用流程引擎、图片展示或长期图片库。

## 数据、配置与扩展兼容

Parcel数据库4追加设施；[SQL1～3原字节/checksum核对](evidence/architecture-quality-closure-01/migration-custody.json)全部相同。DB1/2/3均在迁移标记升级前拒绝未结。DB3→4会同时改变精确实现资格，必须先由匹配旧发布清偿再备份升级；不能只因SQL追加就放行。即使格式相同，所有来源Start前仍验证未结原件精确实现，缺原实现拒绝新宿主启动，避免跨位混合版本责任。迁移SQL不改已结业务JSON，原请求/目标/合同/payload/hash保持。新备份含同一库的归档JSON、原raw BLOB及永久去重，活跃原图/回执继续完整保有。旧程序拒读新库，升级前完整备份和匹配旧发布必须保留；回退前核对外部副作用，不回滚业务事实。Supply存储5/旧SQL与Inspection业务未重写。

schema3增加可缺省Retention，默认200条工作集高水位/64MiB技术保留，MaximumOperations约束不可归档责任；不指定客户保留天数。长期磁盘历史外迁/删除仍需实际预算和批准，软件不删除Unknown或备份消费者来续跑。

配置账新增可缺省Activation尝试身份、源时间及结果；旧配置可读。Parcel两阶段确认保持旧Effective，失败候选不自动重试；Supply原Activate入口保持。配置回退不释放资源，仍须完整Stop/Dispose；降版只走匹配备份/发布，不能原地忽略活动尝试。

Application.Contracts ABI1.6.0、能力示例新不可变1.6.6继承PR23原采集Retain/Release；观察及兼容集成模板1.0.10。精确合同变化须新包，不覆盖1.4/1.5/1.0.9原包。早期冻结Debug1.6.0及Release1.6.1签名证据保持原身份；升级屏障中间候选1.6.2/1.6.3保留；ee59989 Debug1.6.4/Release1.6.5保留；当前3d69607 Debug宿主另建1.6.6、Release宿主另建1.6.7，不复用旧精确包。能力包自身构建输入最近提交54111de，宿主/候选3d69607，按identity文件分别保留，不能把二者混写。全部是TestOnly Simulation。

包、模块、能力、服务/设备实例及投递位分别读取；安装不等于绑定/激活/健康/业务可用。未知、未来、损坏及本宿主无适用读者的Supply消费者均明确阻卸载。没有伪称跨业务混合旧根的统一消费者读者已交付；混合旧根需匹配维护发布盘点和后续适配。

## 必要验证及实际候选

最终命令、返回码及逐日志hash见[checks](evidence/architecture-quality-closure-01/checks.json)、[测试宿主身份](evidence/architecture-quality-closure-01/test-host-delivery.json)及[最终源码逐文件hash](evidence/architecture-quality-closure-01/runtime-source-files-final.json)。原cad/e1运行完整保留于checks-before-final.original.json与frozen-quality-run.json，不替换其来源。固定SDK10.0.402，核心及最终Sportmaster WPF交叉构建均0警告/0错误。

- 新AQ实际Application/Factory/SQLite定向组PASS；含工作集/归档/分页/原raw、重启/备份去重、身份冲突、SQLITE_BUSY、配置审计、UI Owner、Attached、72个持久接收发生及采集重连/HTTP边界。
- 最终3d69607的AQ、AA22、BHR及AC17资源/恢复组PASS；新草稿CAS及可变边界探针先失败后通过。AC19、修正后的AC20在早期cad/e1源码的结果按原scope保留，未宣称全部重跑最终源码；最终签名能力组另覆盖当前完整主链。PR23采集28项定向组在继承时通过。公共Platform改动后另对旧Supply产品与Inspection核心做锁定依赖定向构建，均0警告/0错误；其实际业务运行及全部历史验收NOT_RUN。没有无差别重跑全部历史候选或假称重新做旧现场验收。
- 最终Debug1.6.6真签名/ALC/真实Application七模块能力执行及安装恢复/卸载PASS；签名测试采用独立宿主，替换为冻结checker同字节公共合同/核心DLL后校验hash一致，原测试输出保留。新包实际中断恢复和离线备份阻断独立验证。最终Release1.6.7解包CLI的安装、原操作恢复、安全卸载及操作读回PASS，适用新隔离空消费者store。
- 两个portable包各独立解压，逐项核对97条SHA256SUMS。Sportmaster真实进程运行左右位；WcsToPlc两条独立HTTP接收进程旅程分别验证Attached与BeforeMain、登录/同件重量、真实PNG字节、回复丢失、进程重启原查询、显式检查/继续、接收端每请求一次。Attached旅程到参考PhysicalCompleted；BeforeMain旅程到PlcAccepted，未将它当物理完成。
- 随包指南2项测试PASS。资料校验保留原资产错误集合，不靠豁免降低标准；与继承基线完全相同的267项历史资产错误，新增0；差集见[记录](evidence/architecture-quality-closure-01/archive-error-comparison.json)，校验规则未放宽。

|最终候选（3d6960756bfd843a1cb78c6648687b45f2da9f1c）|ZIP SHA-256|
|---|---|
|sportmaster-portable-delivery|a7062195bccace443d726146105bbad7cb7e5c1fa9b2552ea60c453d59f5f785|
|wcs-to-plc-portable-delivery|37ccbfc498a5e509e97d39ded84549e8b9304105883aeb1d30a614df615c1c7f|

候选在本机Codex artifacts的ipc-architecture-quality-closure-01目录，未发布为验收Release、未提交二进制或私钥。默认运行`dotnet app/Ipc.Assembly.dll --config app/config --data-root <new-isolated-root>`，完整输入、维护与升级见同包QUALITY-CLOSURE-01.md。最终候选身份见[candidate-readback](evidence/architecture-quality-closure-01/candidate-readback.json)，早期e1/c666独立原记录保留。跨版本实际进程[结果](evidence/architecture-quality-closure-01/delivery-cross-release.json)证明新版在来源Start前拒接旧版未结；匹配旧发布以原请求核对到PhysicalCompleted、接收端执行1次后，新版才能启动且保持暂停。DirectPlc真实Application及签名端口已定向验证，本轮没有重复打其portable包或开发延期完整UI。

## 历史和未验证边界

原编译/定向失败日志全部保留。补充跨版本探针初版误发送`stop`而非入口`quit`导致隔离进程超时，修正探针后通过，没有改产品停止语义。Release CLI探针初版传小写hash被精确计划身份拒绝，改用原Plan的大写hash后通过，未放宽校验。独立构建目录导致公共DLL字节不同的检查阻止直接混用，最终签名测试使用独立同字节宿主。资源确认新阶段使配置确认修订从2变3，旧固定2的断言首轮失败后改为同时核对修订3和Activated尝试，主体/原因/原候选hash断言保留。仅Pending包探针初版遗漏合法绑定被正确拒绝，修正输入后复现单一漏登记并修复。旧AC19重启后自动重量续跑、AA22重启直接右位准入两条断言按本次更严格的人工恢复屏障更新，保留原件/单次执行/位隔离断言。AC20旧库夹具补齐SQL4新增设施的剥离后才确为schema1；没有删除迁移拒绝/checksum断言。新测试曾错误地把所有reader的正常“就绪”也视为WCS错身份失败，已改为核对确切WCS实例。签名旅程遗漏受控激活后的Resume曾失败，修正旅程显式恢复，未放宽业务暂停。

原UI v1.1 HTML/CSS/JS、完整交接及同源PNG保持[原身份](evidence/architecture-quality-closure-01/ui-baseline-custody.json)。新增WPF双位/历史/维护是参考实现，本轮没有Windows截图或像素等价证明，也未说旧HTML/RV已修复。

- **BLOCKED：原始审查PDF。** 已搜索指定审查根、Downloads、Codex artifacts并请求原件路径/链接，未取得。Markdown/JSON仅登记输入hash；要求归档main的PDF项未完成。
- **NOT_RUN：** Windows实际WPF/多窗/HID/DPI/触控/声音、PowerShell/UAC/ACL/安装；真实节拍长时负载、故障介质/断电和旧站全量迁移。
- **BLOCKED：** 厂商SDK、真实身份/WCS/PLC合同/地址/握手、实体采集/测量关联、离场/复位/安全联锁及现场资格。Production仍拒绝缺必需能力/独立资格，不静默模拟。
- **后续：** 按[Windows与现场清单](validation/architecture-quality-closure-01-windows.md)收集逐项原证据；取得PDF后单独完成原件归档；有混合旧消费者时安排统一维护读者；按现场预算执行历史外迁和长期性能工作包。D/RV和历史批准不改，不填写假批准人。

已创建并读回[PR #24](https://github.com/yuan2go/importer-pc/pull/24)，OPEN，创建时HEAD90123e2，未合并main。回执见[publication-receipt](evidence/architecture-quality-closure-01/publication-receipt.json)。随后收尾补齐升级屏障、草稿CAS、可变边界、配置激活及Pending备份；另只读核对并行[PR25](https://github.com/yuan2go/importer-pc/pull/25)，未改其工作树/分支，审查02 PDF不冒充审查01。仍更新同一PR且不合并main；最终源码、候选和读回身份在本报告及readback登记。
