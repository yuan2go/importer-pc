# Application运行、维护与兼容

本页统一审查02 N01–N07后的实际操作。源码和包身份读取同包ASSEMBLY.json及SHA256SUMS；当前仓库交付见[统一入口](../INDEX.md)。[01工作包](../work-packages/WP-IPC-ARCHITECTURE-QUALITY-CLOSURE-01.md)、[设计及本次差额](../design/architecture-quality-closure-01.md)与[01历史结果](architecture-quality-closure-01-results.md)保留形成时身份。22的三条独立主流程、19/20原请求规则和旧Supply/Inspection继续适用。Windows实机NOT_RUN，真实SDK/协议/现场BLOCKED。软件暂停不是安全急停。

## 容量及原件

schema3增加可缺省Retention：ResidentHistory=200（1～10000）、MinimumFreeBytes=67108864（技术默认64MiB，允许1MiB～1TiB）。旧配置缺省时按同一显式软件策略读取，不改原FrozenConfiguration。MaximumOperations现在约束不能归档的责任数量；工作集高水位为min(ResidentHistory,MaximumOperations)。默认及optional种子已登记策略，不通过提高上限延迟故障。

满足本业务主链终态、图像已释放、无附加Prepared/Unknown/Conflict、无活动阶段和未清接收发生，才能归档。一次至多256件，事务保存完整原JSON/hash、永久Admission/Observation索引及原响应字节/hash后移出工作表。所有测量、请求、发送次数、回执和审计仍保留；原raw从文件转为同数据库BLOB的持久原字节后才清理冗余spool。删除失败显示待清理并重启续清，不能丢失已提交归档。没有按年龄删除、历史覆盖或长期图片库。

快照只投影当前有界工作集及各位最新结果，完整历史走HistoryAsync(beforeSequence,limit)，limit=1～200，默认50，稳定递减游标。OperationAsync(id)读取原件。CLI命令History（Owner为可选游标、Value为可选页长）、Operation（Owner=原ID）；桌面记录页读取50条/下一页/按需详情。归档旧观察仍去重，跨位身份复用写持续冲突责任。磁盘增长保留全部历史，不推断客户保留天数。维护实际路径是将完整恢复集迁至容量足够的新卷：暂停并认证维护，Backup取得COMPLETED集，停止旧根；以新卷的原Mode/Station/Application目录执行recovery-plan，读取CopyBytes、MinimumFreeBytes、AvailableBytes及RemainingGrowthBytes，再restore。预检不足不建目标；副本hash不符拒绝。按包内原发布、配置、信任和包启动新根，恢复隔离后核对外部事实并显式Resume；旧根仍保留，禁止两个根同时操作设备。重复运行期间记录已归档件数与卷剩余增长字节，按实际增长安排下次扩容；不存在自动删除历史或“永久无限磁盘”承诺。

Capacity统一投影未结/工作集/归档量、磁盘可用及保留阈值。低于磁盘保留或未结数耗尽，准入/Resume/UI同源阻断；维护轮询重新读取容量后可恢复。未结、Unknown、原图/上传及原接收责任不得被归档解锁。暂停、维护认证及检查原因齐备时可显式ArchiveHistory；例行策略本身不授权删除磁盘历史。

## 恢复与阶段

恢复目标严格使用`<new-data-root>/<原Mode>/<原StationId>/<原ApplicationId>`，新目录且完整COMPLETED清单；错身份在创建目标前拒绝。启动比较seed、Effective/Pending和数据身份，构造任何能力前失败，不改原模式、不换空库。

即使数据库格式未变，端口构造后、所有来源Start前仍核对全部未结原件的精确实现指纹；缺原实现即拒绝启动业务，要求匹配原发布清偿，不能先在另一位制造混合版本责任。任一重启未结责任使软件暂停；主链未结还带RecoveryRequired。重量可按原Admission关联，不能自动首次发送。先显式Resume，再对原Owner以ContinuePrepared提交检查说明；若期限/健康/原件屏障未满足仍拒绝。说明表示操作者检查记录，不假造物理传感器许可。所有原ID/payload/hash/配置/实现/接受事实保留。已经返回且持久的raw仅本地T3恢复。

Attached上传失败/Unknown/无结果/原能力缺失只进入Upload.State/Failure和AuxiliaryReasons。主链Reason、WCS格口、PLC接受/物理完成不被上传覆盖。ReconcileAsync(operationId,stage?)可选择原stage字符串（wcs-slot、plc-submit、image-capture、image-upload）；默认先主链后独立上传。仅同阶段未清原接收阻挡该阶段，另一查询不能代偿缺失原件。维护页“异常与恢复”或CLI的ReceiptResponsibilities（Owner=原OperationId）列出原发生ID/阶段/请求/来源/资料状态；ReconcileReceipt（Owner=原发生ID、Value=检查说明、ExpectedRevision=当前应用Revision）要求暂停和维护认证，仅适用于Awaiting且raw_hash为空、无原文件。只调用冻结原实现/原目标的Query，不Submit。原查询无结果、错误、不支持或冲突仍保留原发生、原图和包责任；有效权威终态才把旧发生标为OutcomeReconciled，并在同一T3审计关联新的Committed Query发生。旧ID/Source/空raw_hash及缺口证据永久保留，不能将新raw称为丢失旧raw；RawDurable原件已丢失仍须恢复原字节，不能使用该命令清除。Production须实际提供者支持权威原Query，缺合同仍BLOCKED。

旧版Attached曾把已接受主链写成Blocked/ImageUnknown：启动依据已Committed且hash匹配的主链raw恢复其派生主状态，恢复前完整旧JSON写审计，不改变WCS/PLC原请求/回复。旧DB2/3未结仍不自动升级；可离线运行repair-main-projection，在原库租约下验证原DB hash、先保存完整恢复集，再仅修复可证明的主投影，保持原schema/未结屏障。无原实现/旧raw仍须匹配发布和原件，不能借投影修复放行升级。

历史仅审计而无作用域的IdentityConflict会恢复成显式待检查责任；原审计没有设备/实体范围时保护所有可能位，明确显示资料不足。维护暂停/认证/说明后沿原AcknowledgeIdentityConflict解除；解除记录保留，重启不重复制造阻断。已有明确范围的冲突仍只影响相关位。

原StageFact仅保有至多64条必要事实/最近回复，完整原响应及reply-ID/hash永久持久；达到历史回复数不会永久锁死后续权威完成查询。

过载确认先将原marker哈希、命令ID/回执和审计同事务提交，再删除marker/解除内存。SQL失败不清责；提交后中断按原哈希收敛。新marker不能被旧确认清掉。跨位身份冲突包含稳定ID、受影响位和原件；重启保留，暂停/维护认证/说明后以AcknowledgeIdentityConflict明确处置，原冲突和作业不删除。

## 设备与UI

物理reader及其采图门面共享设备串行门；采集/查询/重连/Stop不并发替换会话。独立设备不共用该门，WCS等可并发服务调用保持双位独立。维护外呼释放应用聚合锁，快照/软件暂停可用；全局服务重连需要现有服务调用排空。维护预算取消但提供者未返回时仍持有原任务/资源，不能当成已经停止。旧epoch回调与旧维护代次健康不可污染新注册；健康返回InstanceId、确认时间和TTL均校验，投影不续期。

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

消费者读者由Parcel拥有，检查精确迁移checksum、当前Effective/Pending、所有未结业务/图像/回执及已注册离线根；包租约和反向依赖沿ExtensionStore。Backup在开始复制前分别检查Effective和Pending并登记离线消费者（包括只存在于被拒激活Pending的包），不完整或缺失备份保持阻断，COMPLETED后按原hash检查。已有备份中的Effective仍引用原包，因此仅移除当前绑定不足以卸载。未知/Supply/未来/损坏消费者均保守拒绝，不能返回空引用；Supply兼容主程序仍使用自己的读者。混合旧根的统一管理读者尚未交付，必须保留原包并使用匹配维护发布盘点，不能声称支持跨业务任意卸载。

## 升级、回退与验证

Parcel数据库4，SQL1～3/checksum逐字节保留，新增历史/原响应保有/身份冲突/过载确认/配置审计设施。DB3→4的迁移SQL不重写旧业务JSON；正常重启恢复追加恢复标记，原冻结请求及hash不变；DB1/2/3均在迁移标记升级前拒绝未结，先以匹配旧发布清偿并备份，不能以追加表结构冒充精确实现兼容。旧程序拒读DB4。升级前通过匹配发布做完整备份并保存原二进制、配置、信任与包；回退只能使用匹配原发布/升级前备份，并先核对期间外部动作，不能倒退业务账或盲重发。

Application.Contracts仍为ABI1.6.0；PR24能力包1.6.6/1.6.7保留原身份，本次能力示例新不可变1.7.0；继承PR23的1.5.0原采集所有者协议。Platform字节变化，观察/兼容集成参考包采用新1.0.10。所有扩展必须按目标宿主精确公共DLL重新构建/签名；旧1.4/1.5/1.0.9包和原请求不重签或覆盖。

必要构建与定向选择器：

```sh
dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --architecture-quality
# signed package按本宿主精确合同独立构建后：
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --quality-extensions /new/1.6.6.ipcx /independent/trust.json
```

只根据变更执行AA22/AC19/AC20/BHR/AC17资源组和必要WPF构建/参考包进程，不循环全部历史候选。真实SDK、WCS/PLC字段/地址/握手、身份/原件关联、复位/联锁及现场BLOCKED；[Windows及现场后验](validation/architecture-quality-closure-01-windows.md)不能由合成验证关闭。

驱动入队时复制扫码数组；提交及查询适配器取得原请求的独立副本。外部可变参数不能修改已持久化意图、冻结配置或后续查询依据。配置编辑草稿绑定读取时的运行实例与配置修订，自动刷新不推进草稿CAS；双窗口冲突须重新读取后人工合并。

配置账新增可缺省Activation尝试身份/源时间/结果，旧Effective在资源构造和Start全部成功前不变，Pending保持完整候选。突然退出后标记ActivationFailed，保留旧确认版、原候选和原尝试；不自动重试，维护者核对后使用当前CAS重新保存。失败资源仍必须Stop/Dispose释放，配置回退不等于资源释放。Supply旧Activate路径保持；旧版本不支持原地恢复活动尝试，应使用匹配完整备份和发布。

## 审查02实际维护命令

以下为选中Application的Assembly入口，路径与hash由维护者从原文件核对。repair仅离线修主投影，不能清旧上传/回执责任或升级未结；备份用全新目标。

```sh
dotnet app/Ipc.Assembly.dll --action repair-main-projection --file /old/Simulation/reference-17/sportmaster/sportmaster.db --sha256 ORIGINAL_UPPERCASE_DB_SHA256 --destination /backup/before-repair.db --reason 'Checked original main receipt custody'
dotnet app/Ipc.Assembly.dll --action recovery-plan --file /backup/full.db --destination /larger-volume/runtime/Simulation/reference-17/sportmaster
dotnet app/Ipc.Assembly.dll --action restore --file /backup/full.db --destination /larger-volume/runtime/Simulation/reference-17/sportmaster
```

含扩展的离线修复须在原Application目录保有extension-store.json（Windows使用已配置机器策略），备份Effective与Pending均登记；未配置/未知消费者拒绝，不返回空引用。旧主结果恢复不代表旧实现升级已完成。

## 标准发行的可选Driver/Component集合

核心闭包沿compositions.json；可重复指定已签名的--extension-package，集合实际决定offline-extensions目录和composition.lock.json。构建不会复制信任策略或私钥。所有选择包依赖必须完整且精确；使用目标Release的静态检查器验证签名/真实AssemblyRef/公共DLL，Windows跨目标检查器也须逐字节匹配。任一缺依赖/不匹配/输入变化即停止，不产成功ZIP。内置参考实现仍是核心；无需把每类端口拆成独立DLL。

```sh
python3 scripts/package-application.py --application sportmaster --target portable --output /new/selected --dotnet /pinned/dotnet --extension-package /signed/driver.ipcx --extension-package /signed/component.ipcx --extension-trust /independent/trust.json
```

解压后按SHA256SUMS及composition.lock.json核选中包；按EXTENSIONS.md inspect/install，保留原操作ID，编辑完整候选绑定，校验和StageConfiguration，受控重启后查Effective及健康。选装媒介不会隐式安装/激活。不同集合各用新不可变输出；运行/安装/原操作恢复/安全卸载沿同一Store和消费者政策。实际Windows及真实SDK/现场仍未验。
