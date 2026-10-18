# Application运行、维护与兼容

本轮P1/P2整改见[结果与固定证据](pr26-recovery-closure-results.md)及[工作包](../work-packages/WP-IPC-PR26-RECOVERY-CLOSURE.md)。普通DB4未结启动先用只读连接检查，拒绝时不打开写库、不checkpoint非空WAL，不写配置确认。原匹配DB4读者仍可清偿；即使实现Hash看似一致，也不自动放行跨DB升级。

本页统一审查02 N01–N07后的实际操作。源码和包身份读取同包ASSEMBLY.json及SHA256SUMS；当前仓库交付见[统一入口](../INDEX.md)。[01工作包](../work-packages/WP-IPC-ARCHITECTURE-QUALITY-CLOSURE-01.md)、[设计及本次差额](../design/architecture-quality-closure-01.md)与[01历史结果](architecture-quality-closure-01-results.md)保留形成时身份。22的三条独立主流程、19/20原请求规则和旧Supply/Inspection继续适用。Windows实机NOT_RUN，真实SDK/协议/现场BLOCKED。软件暂停不是安全急停。

PR24已由独立授权合并：归档前复审head `f862c2ae2ad00ab79ceff5e2013c2a5593d05234`，归档后最终head `f531d3a6882891cfc7870f5dcccfe985009d6be4`，main合并提交 `4afe2afacde9e2acab7648fff3440fa3cc331b6e`。[独立复审与原证据](../reviews/pr24-independent-rereview-f862c2a/README.md)及[R03结果](architecture-quality-closure-01-r03-results.md)为继承来源；本页DB5、旧账确认与独立资料规则为当前操作，不以历史DB4候选或PASS替代。

## 容量及原件

schema3增加可缺省Retention：ResidentHistory=200（1～10000）、MinimumFreeBytes=67108864（技术默认64MiB，允许1MiB～1TiB）。旧配置缺省时按同一显式软件策略读取，不改原FrozenConfiguration。MaximumOperations现在约束不能归档的责任数量；工作集高水位为min(ResidentHistory,MaximumOperations)。默认及optional种子已登记策略，不通过提高上限延迟故障。

满足本业务主链终态、图像已释放、无附加Prepared/Unknown/Conflict、无活动阶段和未清接收发生，才能归档。一次至多256件，事务保存完整原JSON/hash、永久Admission/Observation索引及原响应字节/hash后移出工作表。所有测量、请求、发送次数、回执和审计仍保留；原raw从文件转为同数据库BLOB的持久原字节后才清理冗余spool。删除失败显示待清理并重启续清，不能丢失已提交归档。没有按年龄删除、历史覆盖或长期图片库。

快照只投影当前有界工作集及各位最新结果，完整历史走HistoryAsync(beforeSequence,limit)，limit=1～200，默认50，稳定递减游标。OperationAsync(id)读取原件。CLI命令History（Owner为可选游标、Value为可选页长）、Operation（Owner=原ID）；桌面记录页读取50条/下一页/按需详情。归档旧观察仍去重，跨位身份复用写持续冲突责任。磁盘增长保留全部历史，不推断客户保留天数。维护实际路径是将完整恢复集迁至容量足够的新卷：暂停并认证维护，Backup取得COMPLETED集，停止旧根；以新卷的原Mode/Station/Application目录执行recovery-plan，读取CopyBytes、MinimumFreeBytes、AvailableBytes及RemainingGrowthBytes，再restore。预检不足不建目标；副本hash不符拒绝。按包内原发布、配置、信任和包启动新根，恢复隔离后核对外部事实并显式Resume；旧根仍保留，禁止两个根同时操作设备。重复运行期间记录已归档件数与卷剩余增长字节，按实际增长安排下次扩容；不存在自动删除历史或“永久无限磁盘”承诺。

Capacity统一投影未结/工作集/归档量、磁盘可用及保留阈值。低于磁盘保留或未结数耗尽，准入/Resume/UI同源阻断；维护轮询重新读取容量后可恢复。未结、Unknown、原图/上传及原接收责任不得被归档解锁。暂停、维护认证及检查原因齐备时可显式ArchiveHistory；例行策略本身不授权删除磁盘历史。

## 恢复与阶段

恢复目标严格使用`<new-data-root>/<原Mode>/<原StationId>/<原ApplicationId>`，新目录且完整COMPLETED清单；错身份在创建目标前拒绝。启动比较seed、Effective/Pending和数据身份，构造任何能力前失败，不改原模式、不换空库。

即使数据库格式未变，端口构造后、所有来源Start前仍核对需要继续执行的未结原件精确实现指纹；缺原实现即拒绝启动业务，要求匹配原发布核对，不能先在另一位制造混合版本责任。仅剩资料责任且各缺raw均有独立Committed Query依据的已终结Attached作业不再调用旧端口，但继续保有原图、包及归档屏障。任一重启未结责任使软件暂停；主链未结还带RecoveryRequired。重量可按原Admission关联，不能自动首次发送。先显式Resume，再对原Owner以ContinuePrepared提交检查说明；若期限/健康/原件屏障未满足仍拒绝。说明表示操作者检查记录，不假造物理传感器许可。所有原ID/payload/hash/配置/实现/接受事实保留。已经返回且持久的raw仅本地T3恢复。

Attached上传失败/Unknown/无结果/原能力缺失只进入Upload.State/Failure和AuxiliaryReasons。主链Reason、WCS格口、PLC接受/物理完成不被上传覆盖。ReconcileAsync(operationId,stage?)可选择原stage字符串（wcs-slot、plc-submit、image-capture、image-upload）；默认先主链后独立上传。仅同阶段未清原接收阻挡该阶段，另一查询不能代偿缺失原件。维护页“异常与恢复”或CLI的ReceiptResponsibilities（Owner=原OperationId）列出原发生ID/阶段/请求/来源/资料状态；ReconcileReceipt（Owner=原发生ID、Value=检查说明、ExpectedRevision=当前应用Revision）要求暂停和维护认证，仅适用于Awaiting且raw_hash为空、无原文件。只调用冻结原实现/原目标的Query，不Submit。原查询无结果、错误、不支持或冲突仍保留原发生、原图和包责任；有效权威终态只确认上传结果并关联新的Committed Query发生；旧发生保留缺raw资料责任，仍阻原图释放、归档及原包退休。SQL5把已有OutcomeReconciled旧行恢复为Awaiting并建立资料索引；已归档作业因原资料责任重新进入工作集，原归档JSON/hash在后续更新前另存版本。旧代码已释放或删除的图像只能如实保留缺口，不能由新Query、种子或迁移制造原字节。真正原文件仅从完整恢复集按原发生/清单hash恢复后本地T3清偿；缺原字节和合同处置依据时责任持续可见，不提供强制清除。Production须实际提供者支持权威原Query，缺合同仍BLOCKED。

旧版Attached曾把已接受主链写成Blocked/ImageUnknown：启动依据已Committed且hash匹配的主链raw恢复其派生主状态，恢复前完整旧JSON写审计，不改变WCS/PLC原请求/回复。旧DB2/3/4普通启动在写库和SQL5前拒绝未明责任；repair-main-projection仅在原库租约及完整备份下修可证明主投影，保持旧schema，不构成完整升级步骤。缺raw的Attached场景改用下述recover-legacy-receipt：通过逐文件校验的匹配原发布、冻结配置/请求往返及原实现指纹，只启动原上传端口并Health/Query，不启动reader、不Submit。持久化独立Query原件和已证明主状态后，仅在所有外部业务已终结、每个缺raw均有权威依据时显式升级DB5；原发生仍Awaiting、原图/包和资料屏障保留。无结果、冲突、原发布/原图/合同缺失保持隔离，不先写未来迁移标记。

历史仅审计而无作用域的IdentityConflict须按持久Effective实际位恢复为待检查责任，schema1/2必须保护main，不能套用schema3的left/right。未确认冲突阻止Pending拓扑激活；已经错误持久化的legacy范围须保留旧body并追加修复审计，已确认项不重新武装。原审计没有设备/实体范围时保护所有可能位，明确显示资料不足。维护暂停/认证/说明后沿原AcknowledgeIdentityConflict解除；解除记录保留，重启不重复制造阻断。已有明确范围的冲突仍只影响相关位。

原StageFact仅保有至多64条必要事实/最近回复，完整原响应及reply-ID/hash永久持久；StoreRaw与RecoverRaw共享接收发生及不可替换回复身份事务，Request命名空间取原发生。旧DB4及现行DB5启动按原发生顺序验证持久raw/归档BLOB并补齐缺索引，失败完整回滚；已经矛盾的原件与假完成不能改写为成功，以OriginalReplyIdentityConflict拒绝启动，先保全DB/WAL、配置、原raw/归档及精确发布，沿原合同调查处置，不强制清空或重发。超过64次查询仍不能遗忘原身份，也不会单因回复数量锁死权威完成查询。

过载确认先将原marker哈希、命令ID/回执和审计同事务提交，再删除marker/解除内存。SQL失败不清责；提交后中断按原哈希收敛。新marker不能被旧确认清掉。跨位身份冲突包含稳定ID、受影响位和原件；重启保留，暂停/维护认证/说明后以AcknowledgeIdentityConflict明确处置，原冲突和作业不删除。

## 设备与UI

物理reader及其采图门面共享设备串行门；采集/查询/重连/Stop不并发替换会话。独立设备不共用该门，WCS等可并发服务调用保持双位独立。维护外呼释放应用聚合锁，快照/软件暂停可用；共享服务重连需要该服务既有调用先排空；维护取得所选资源围栏后，Submit/Query/健康/认证不能进入同一资源，相关Resume/准入关闭。独立设备维护仍允许其他无依赖位受原守卫恢复运行。设备在途采集按预算等待同一串行门；维护预算取消但提供者未返回时仍持有围栏、原任务/资源及租约，不能当成已经停止；迟到返回按MaintenanceIncomplete保留失败审计，回执使用实际软件暂停状态。旧epoch回调与旧维护代次健康不可污染新注册；健康返回InstanceId、确认时间和TTL均校验，投影不续期。

工作台及公共底栏显示当前投递位、条码和完整命令Owner；历史详情单独显示所选Owner及可用命令。历史选择不改变当前位，刷新和双窗保持本窗目标；发送使用显示时的修订，冲突要求刷新。Sportmaster同时显示left/right的reader、入场、作业和WCS格口，模拟输入显式选择位。原UI v1.1 HTML/CSS/JS与交接为信息层级依据，没有复制其旧共享箱/数量/AGV规则。没有新增图片区域或DirectPlc完整UI。Windows交互另验。

## 配置、安装与安全卸载

维护页“完整候选配置”读取全部schema3 JSON，可校验而不保存，也可填维护原因后CAS保存Pending；Effective待清偿屏障及受控重启。旧单Reader表单继续拒绝schema3保存。安装模块/schema、完整候选参数、绑定/激活修订、健康和业务可用分别读取。

StageConfigurationAsync(expectedConfigurationRevision,candidate,reason)要求维护身份与1～1024字原因。配置前写持久审计意图，记录主体/前后修订/原与候选hash；保存、校验拒绝、激活及失败有独立结果。文件已保存但结果审计中断时，启动根据原哈希及权威envelope收敛；不把文件/数据库双写当作单事务。候选包在保存期间受原包使用租约保护，消费者先登记，卸载不能在保存窗口漏读引用。

Parcel CLI离线管理沿机器保护/安装权限：

```sh
dotnet app/Ipc.Assembly.dll --action extension-operations --mode Simulation --extension-root /reference/store --extension-trust /independent/trust.json
dotnet app/Ipc.Assembly.dll --action extension-recover --operation ORIGINAL_OPERATION_ID --mode Simulation --extension-root /reference/store --extension-trust /independent/trust.json
dotnet app/Ipc.Assembly.dll --action extension-uninstall --package '{"Id":"approved.id","Version":"1.6.0","ManifestSha256":"ORIGINAL_UPPERCASE_SHA256"}' --mode Simulation --extension-root /reference/store --extension-trust /independent/trust.json
```

运行维护经IParcelApplication.ManageCapabilityAsync（桌面安装管理/CLI ManageExtension），需暂停/维护认证/外呼排空；Windows调用受保护runas帮助程序再读回原操作/登记。Windows实际UAC/ACL仍NOT_RUN。安装中断按原操作恢复；永久ID/version不覆盖。

消费者读者由Parcel拥有。schema1/2/3必须先校验未经All/Bound去重的角色和完整CapabilityInstances，再解析业务/拓扑。库存至多64项、无null、InstanceId唯一（同内容重复也拒绝）；不同角色不得共占身份，角色与库存的一次合法镜像须全部字段一致。逐项校验已知kind、Enabled及精确包Id/Version/ManifestSha256、Module/Implementation闭包，disabled项也不能隐藏引用；无Package不得携带扩展Module/Implementation。Effective、Pending、未结FrozenConfiguration与Factory独立校验入口共用该规则；不加载扩展、不要求历史包仍能启动，不修改原JSON/hash或原包。检查精确迁移checksum、当前Effective/Pending、所有未结业务/图像/回执及已注册离线根；包租约和反向依赖沿ExtensionStore。Backup在开始复制前分别检查Effective和Pending并登记离线消费者（包括只存在于被拒激活Pending的包），不完整或缺失备份保持阻断，COMPLETED后按原hash检查。已有备份中的Effective仍引用原包，因此仅移除当前绑定不足以卸载。装配按数据库标记选择Parcel或Supply各自拥有的只读读者；未知、混合歧义、未来、损坏或无法验证完整配置/冻结绑定的消费者均保守拒绝，不能返回空引用。已知Supply沿自身存储契约读取，不将其业务SQL放入Platform；旧根/备份仍须完整注册并核对原件，不支持跳过未知消费者的任意卸载。

## 升级、回退与验证

Parcel现行数据库5；DB3拓扑、DB4历史/原响应/永久身份/冲突及配置审计属于历史阶段，SQL1～4/checksum逐字节保留，SQL5追加独立资料索引和原归档版本保有。普通旧库启动在迁移前拒绝未明主链/上传/原件；匹配发布清偿或受控旧发布Query证实仅剩资料责任后，先保有完整备份，再按上述专用入口升级。升级不重写原冻结请求、发送次数、目标、hash或合同，不把追加表结构当作精确实现兼容。旧程序拒读DB5。升级前通过匹配发布做完整备份并保存原二进制、配置、信任与包；回退只能使用匹配原发布/升级前备份，并先核对期间外部动作，不能倒退业务账或盲重发。

Application.Contracts仍为ABI1.6.0；PR24能力包1.6.6/1.6.7保留原身份，本次能力、观察及兼容集成示例模板均不提供Version，每次构建显式指定`--version`的新不可变版本；准确发行物版本及manifest/包哈希读取媒介`composition.lock.json`与`.identity.json`，不得把模板或历史示例版本视为当前默认；继承PR23的1.5.0原采集所有者协议。Platform字节变化，观察/兼容集成参考包同样使用显式新版本。所有扩展必须按目标宿主精确公共DLL重新构建/签名；旧1.4/1.5/1.0.9包和原请求不重签或覆盖。

必要构建与定向选择器：

```sh
dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --upgrade-conflict-closure
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --architecture-quality
# signed package按本宿主精确合同独立构建后：
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --quality-extensions /new/selected-version.ipcx /independent/trust.json
```

只根据变更执行AA22/AC19/AC20/BHR/AC17资源组和必要WPF构建/参考包进程，不循环全部历史候选。真实SDK、WCS/PLC字段/地址/握手、身份/原件关联、复位/联锁及现场BLOCKED；[Windows及现场后验](validation/architecture-quality-closure-01-windows.md)不能由合成验证关闭。

驱动入队时复制扫码数组；提交及查询适配器取得原请求的独立副本。外部可变参数不能修改已持久化意图、冻结配置或后续查询依据。配置编辑草稿绑定读取时的运行实例与配置修订，自动刷新不推进草稿CAS；双窗口冲突须重新读取后人工合并。

配置仍只有同一权威envelope。新候选先持久ActivationInProgress及尝试身份/时间，资源构造、Start全部成功后才确认Effective；失败或断进程保留旧确认版、Pending及原尝试，标ActivationFailed，不自动重试，核对后显式restage。旧无成功证据的Effective在Confirmation中保留原envelope Base64、SHA256、当前EffectiveHash、来源和全部启动尝试；失败/中断不虚报已确认，成功Start后为AwaitingMaintenance并保持暂停。没有原备份时不能猜回旧程序已覆盖的配置值；此前确有Activated成功证据的旧账保留其确认。

维护页先“读取待确认原账”，核对原字节/当前输入，填写说明后确认。CLI同样先Pause、AuthorizeMaintenance并读取snapshot，再提交ConfirmConfiguration：Owner必须为读取时`runtimeIdN:configurationRevision:effectiveSha256`，runtimeIdN为RuntimeId去连字符的32位GUID，其他两段来自Configuration.Revision及Confirmation.EffectiveHash；Value为1～1024字检查说明，ExpectedRevision为提交时应用Revision。配置或运行实例变化必须重新读取检查，自动刷新不能推进确认身份。资源当前健康、有效维护会话及外呼排空仍须成立；确认记录维护主体/原因并保留暂停，另行Resume。Pending不因确认丢失，确认旧账后仍需后续受控启动才能激活候选。失败资源必须完整Stop/Dispose，配置恢复不等于释放资源。Supply旧Activate保持自身契约；回退使用匹配完整备份及发布。

## 审查02实际维护命令

以下为选中Application的Assembly入口。先停止旧应用并取得原目录所有权，核对数据库及非空WAL的SHA256，保留完整匹配原发布、原配置/信任/包/图像/回执；备份目标必须全新且与原目录分离。recover-legacy-receipt是旧Attached缺raw到DB5的窄维护入口；原发布目录应包含原application-release.json及其Files完整文件（通常为原媒介app目录）。工具不接纳新件。Awaiting分支只Query冻结原上传请求；旧DB4 OutcomeReconciled分支只验证已经持久的独立Query、主链raw及全部归档/未结责任，不调用新的Query，先建立完整备份和包含主体/原因/原发生/备份身份的迁移意图审计，再受控写SQL5。任一旧Query原件、hash、owner或终态依据不完整则保留DB4，不能由已归档或历史ImageReleased掩盖。恢复进程中断后可用当前DB/WAL新指纹及新备份目标重试，先本地完成已持久Query再决定是否需要外查。repair-main-projection仅修主投影，不能代替该升级流程。

```sh
# 如原库有非空-wal，为recover-legacy-receipt增加 --wal-sha256 ORIGINAL_UPPERCASE_WAL_SHA256
dotnet app/Ipc.Assembly.dll --action recover-legacy-receipt --file /old/Simulation/reference-17/sportmaster/sportmaster.db --sha256 ORIGINAL_UPPERCASE_DB_SHA256 --destination /backup/before-original-query.db --occurrence ORIGINAL_AWAITING_OCCURRENCE_ID --original-release /frozen/original/app --reason 'Checked original main raw and frozen uploader before query'
dotnet app/Ipc.Assembly.dll --action repair-main-projection --file /old/Simulation/reference-17/sportmaster/sportmaster.db --sha256 ORIGINAL_UPPERCASE_DB_SHA256 --destination /backup/before-repair.db --reason 'Checked original main receipt custody'
dotnet app/Ipc.Assembly.dll --action recovery-plan --file /backup/full.db --destination /larger-volume/runtime/Simulation/reference-17/sportmaster
dotnet app/Ipc.Assembly.dll --action restore --file /backup/full.db --destination /larger-volume/runtime/Simulation/reference-17/sportmaster
```

含扩展的离线修复须在原Application目录保有extension-store.json（Windows使用已配置机器策略），备份Effective与Pending均登记；未配置/未知消费者拒绝，不返回空引用。旧主结果恢复不代表旧实现升级已完成。recover-legacy-receipt成功输出UpgradedCustodyRetained及DatabaseVersion=5，仍需核对原Awaiting/raw_hash=null、Query关联、发送次数和完整备份；原raw资料责任持续阻止图像释放、归档及原包退休。新运行输入需按旧账确认流程维护确认；若需改新精确实现，沿完整候选/CAS和受控重启，仅在外部业务已被证明终结后过渡。

## 标准发行的可选Driver/Component集合

核心闭包沿compositions.json；可重复指定已签名的--extension-package，集合实际决定offline-extensions目录和composition.lock.json。构建不会复制信任策略或私钥。所有选择包依赖必须完整且精确；使用目标Release的静态检查器验证签名/真实AssemblyRef/公共DLL，Windows跨目标检查器也须逐字节匹配。任一缺依赖/不匹配/输入变化即停止，不产成功ZIP。内置参考实现仍是核心；无需把每类端口拆成独立DLL。

```sh
python3 scripts/package-application.py --application sportmaster --target portable --output /new/selected --dotnet /pinned/dotnet --extension-package /signed/driver.ipcx --extension-package /signed/component.ipcx --extension-trust /independent/trust.json
```

解压后按SHA256SUMS及composition.lock.json核选中包；按EXTENSIONS.md inspect/install，保留原操作ID，编辑完整候选绑定，校验和StageConfiguration，受控重启后查Effective及健康。选装媒介不会隐式安装/激活。不同集合各用新不可变输出；运行/安装/原操作恢复/安全卸载沿同一Store和消费者政策。实际Windows及真实SDK/现场仍未验。
