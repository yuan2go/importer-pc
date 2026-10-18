# Application 架构收敛与场地业务贯通 · 17

当前工作包：[WP-IPC-APPLICATION-CONVERGENCE-17](../work-packages/WP-IPC-APPLICATION-CONVERGENCE-17.md)。基线 main `6e9c890`；前置文档提交 `54abf1f`。继承 F-02 独立参考工程，不宣称旧现场迁移完成。当前源冻结、运行证据、候选和逐项结果将在本页末尾登记；本节是实际操作指南，不把计划当验证结论。

## 职责与兼容入口

|职责|当前源码/工程|兼容身份与界限|
|---|---|---|
|Platform|Ipc.Platform、Ipc.Platform.Storage|配置修订机制、生命周期/队列/资源/诊断、严格签名安装/ALC、SQLite连接/校验和迁移执行/在线备份；不包含新业务重量或阶段规则。|
|Application|Ipc.Application.Supply、.Supply.Contracts、.Inspection、.Sportmaster、.DirectPlc、.WcsToPlc、.ParcelFlow、.Contracts|各业务拥有校验、状态、仓储SQL、事务/恢复；共享ParcelFlow不引用具体设备/协议/WPF。旧Supply/Inspection的程序集及公共命名空间保留；旧StationConfig在Platform内仅为已发布Supply兼容形状，不是新业务通用配置。|
|所属UI|Ipc.Application.Supply.Desktop目录；三个Ipc.Application.*.Desktop工程；ParcelFlow.Desktop共享窗口/历史/维护|Supply来源代码按条件链接进原Ipc.Desktop宿主保持原类型身份。新业务拥有实际Workstation.xaml及ViewModel；共享外壳只持应用契约。|
|Drivers|Ipc.Drivers|稳定读码来源、重量输入、PLC提交/原查询。原Ipc.Adapters观察类型名作为ABI例外保留。|
|Integrations|Ipc.Integrations|WCS格口与原查询；原loopback/SIMULATION-v1兼容类型仍在原命名空间，Ipc.Adapters仅TypeForwardedTo门面。|
|宿主/装配/合成端|Ipc.Assembly、Ipc.Desktop、Ipc.Product、Ipc.Fixture、Ipc.Reference.Protocol|装配只选择业务/工厂/页面，不判重量/阶段状态；Reference.Protocol是显式合成接收端，不是SDK或现场PLC协议。|

旧Supply数据库格式/最低读者 **5/5**，SQL1～5原文与checksum保留，配置CAS/原请求/接受账/接收责任和历史批准仍按原机制。新场地各有独立`<data-root>/Simulation/<StationId>/<ApplicationId>/<ApplicationId>.db`及业务迁移1；同库业务标识和SQL校验和不符拒绝，不解释为可重建空库。公共契约字节变化使用参考观察/旧WCS包新不可变 **1.0.5**；原1.0.0～1.0.4包/登记/原宿主保留，精确指纹不同仍拒绝。新`Ipc.Application.Contracts`契约1.0.0绑定独立包`reference.parcel-capabilities/1.0.0`，不冒充旧Supply公共合同。

旧请求继续按原发布、原包、合同/参数/目标/实现核对。新场地不得打开旧Supply库。保留旧候选恢复路径；缺原精确能力显示OriginalCapabilityUnavailable，不换ID或使用当前目标。旧公共兼容类型是明确例外，新增业务命名空间全部采用单数Application。

## 选择构建和实际运行

固定SDK10.0.402。当前构建选项为`Application`；旧`BusinessModule`保留为兼容入口。开发构建选择一个业务，WPF工程会真实引用所选页面/VM及Assembly；DirectPlc选装闭包不带Ipc.Integrations.dll。

```sh
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=Sportmaster -m:1 -nr:false
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=DirectPlc -m:1 -nr:false
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=WcsToPlc -m:1 -nr:false
# Windows实际运行，选择和样例目录必须一致：
dotnet src/Ipc.Desktop/bin/Debug/net10.0-windows/Ipc.Desktop.dll --config config/wcs-to-plc --data-root <全新隔离根> --view wcs-to-plc --layout standard
# portable核心参考进程：
dotnet src/Ipc.Assembly/bin/Debug/net10.0/Ipc.Assembly.dll --config config/wcs-to-plc --data-root <全新隔离根>
```

同一源码输出目录一次只构建一个选择；发布工具隔离构建中间件并检查实际闭包，不能把刚构建的另一业务宿主当当前选择。Supply旧ProductHost/桌面默认保持，Inspection原持久批次/采样/关闭与重启接口不变。其运行方式见[Supply运维](operations.md)，旧资料不作为新场地配置来源。

Windows工作台显示包裹号、阶段结果、格口/重量来源与原因；历史按作业显示两个原阶段和回执，选中后只核对原请求。维护可读取/编辑/严格校验单一草稿、保存Pending、受控重连、备份及过载确认。每窗VM只拥有本窗选择/草稿，业务与端口不因切页、布局或辅助窗口重建。最小窗口900×620，字段区局部滚动，主操作固定；实际DPI/触控/焦点验收NOT_RUN。候选启动时核对application-release.json的实际文件/业务/页面身份；哈希是完整性检查，不代替可信分发。字体/状态色继承v1.1，Supply原实物和新WPF源码来源分别记录；无Windows截图，不用HTML重新导出冒充。

Console每行输入`snapshot`取得完整状态，其他行为使用BusinessCommand JSON。以下为合成示例；Id每次命令不同，实体Owner在同一包裹重扫时保持，换新包裹才用新Owner。ExpectedRevision用于业务命令CAS；StageConfiguration使用配置修订，二者不是同一个版本。

```json
{"Id":"11111111111111111111111111111111","ExpectedRevision":0,"Kind":"EmitReference","Owner":"parcel-one","Value":"000123456789"}
```

Sportmaster自动完成参考WCS格口并展示；DirectPlc不创建WCS端口；WcsToPlc先持久WCS结果，再提交关联PLC命令。`snapshot`内PLC的TransportSucceeded、ProtocolAcknowledged、BusinessAccepted与PhysicalCompleted分别显示。合成端首次只报告业务接受，配置`complete-on-query:true`后显式Reconcile才报告合成物理完成；从未操作真实硬件。

Reconcile的Owner填原Operations.Id；未确认阶段只查原命令，不重发。`Pause/Resume`控制软件入场；`Reconnect`仅暂停下允许且不重发；`AcknowledgeOverload`要求暂停、队列排空和操作员检查。WCS查询恢复后新建但未尝试的PLC意图可显式`ContinuePrepared`；曾尝试或启动中断的意图不能用此命令重发。退出输入`quit`，按源→工作→端口→包→数据库/租约停止。停止超时不释放下游，WPF保留失败并允许再等。

## 重量与配置权威

`Weight.Source=Disabled`：不发送重量。`Configured`：ConfiguredValue为正decimal，Unit=Kg/G、Precision=0～6，值必须符合精度；报文明示配置补全。`Measured`：配置Scale，等待同一AdmissionId/设备的有效稳定测量；测量ID、单位、精度、质量、观察后时间及MaximumAgeMs均校验。无码/多码、不同实体、过期/未来、不稳、超载、零/负值或错误精度不会下发PLC；未知不填0，不使用件数。

实测示例：从原业务样例复制到隔离配置，设置Weight.Source=Measured、ConfiguredValue=null并配置`Kind=weight,Implementation=reference-weight,Version=1.0.0,InstanceId=scale.one,Target=reference://scale/one,Contract=PARCEL-REFERENCE-v1,Parameters={}`。相机读码后，工程参考区“同包裹模拟实测重量”生成明确合成测量，严格关联当前原实体。不是实际电子秤读数。Console的EmitWeight.Value为完整WeightMeasurement的JSON字符串。

Platform.ConfigurationRevisions保存一个Effective/Pending修订封套；Application校验字段/能力及生效屏障。维护先Pause再StageConfiguration，失败不改变Effective或原Pending；保存成功也只PendingRestart。重启先恢复原责任，再验证/激活；未决、未完成PLC或能力失败时ActivationFailed保旧。站/应用/模式/读码所有权身份不能借配置热改。初始business.json只是新根seed，已有根以修订封套为权威，不手改封套绕过生效流程。

## 扩展和真实接入边界

沿用既有.ipcx/RSA-PSS/独立信任/永久ID-version登记/精确AssemblyRef/托管依赖闭包/包租约。新增weight/plc Kind必须签名绑定公共合同；没有关闭P/Invoke/原生拒绝或扩大旧观察ABI例外。完整新声明及工厂见[ParcelCapabilitiesExtension](../../examples/ParcelCapabilitiesExtension/extension.build.json)。读码沿用[BarcodeExtension](../../examples/BarcodeExtension/extension.build.json)。二者独立构建、安装，运行不引用源码工程。

```sh
python3 scripts/build-extension.py --definition examples/ParcelCapabilitiesExtension/extension.build.json --contract <冻结portable/app/Ipc.Platform.dll> --check-host <同源portable/app/Ipc.Assembly.dll> --version 1.0.0 --publisher reference17 --key <外部私钥> --trust <独立TEST_ONLY信任.json> --dotnet <固定dotnet> --output <全新目录>/parcel-1.0.0.ipcx
dotnet <冻结portable/app/Ipc.Assembly.dll> --action extension-install --mode Simulation --file <parcel.ipcx> --sha256 <原包大写SHA256> --extension-root <隔离库> --extension-trust <独立信任.json>
```

安装不激活。portable参考环境的config目录extension-store.json明确独立Root/TrustFile绝对路径；Windows拒绝该覆盖文件和CLI自选路径，复用ProgramData独立信任、指定运行账户与机级租约，安装要求提升且来自受保护Program Files发布目录。包内provision-runtime.ps1/provision-extensions.ps1供既定Windows后验配置，实际ACL/UAC未验；同一业务修订的Extensions.Packages列精确包，Reader使用原观察实例，Scale/Wcs/Plc分别使用Package/Module/InstanceId和严格参数，不假造设备观察。Scale的Kind=weight/Module=parcel-weight，PLC为plc/parcel-plc，WCS为integration/parcel-integration；扩展绑定Implementation为`extension:<PackageId>/<ModuleId>`、Version必须等于原Package.Version；各参数见各schema，`peer-root`为独立合成接收账绝对目录。WCS/PLC的身份、合同、目标和全部参数随阶段冻结。安装静态/运行校验与原政策一致；包使用和离线consumer登记保护旧责任。旧Supply工具遇到不识别的新业务消费者拒绝卸载，不能删登记绕过。

当前真实SDK、型号/固件/位数、PLC地址/字节序/命令握手/回执查询和安全控制权、WCS版本合同与认证/格口/幂等范围、秤关联/校准/质量/时间源均未取得。所有Production启动明确NotReady，原生SDK/系统驱动prerequisite仍拒绝，未虚构任何厂商方法或地址。显式参考端使用独立持久接收账，校验原请求、重复身份/报文、业务接受与查询，注入lose-after-commit在落账后丢回执；不是返回成功的占位接口，也不证明现场能力。

## 恢复、备份与必要验证

T1意图、T2尝试、原回执发生、原件Flush、T3及下游意图各有持久责任。原件缺失/损坏/陌生.new文件阻断；原件已持久但T3失败，重启只本地重放原回执。WCS成功PLC未知保存原格口/原命令；查不到不证明未动作。不同/矛盾回执保留并隔离，终态不被较弱信息覆盖。程序升级缺原实现hash明确阻断；使用原发布/原精确包恢复。

Backup命令.Value为新绝对数据库备份路径，同时生成`.recovery/`，含原配置/原回执与最后写入的COMPLETED.json；缺完成清单不算完整备份。暂停及应用gate保证一致性，SQLite使用online backup覆盖WAL。文件含业务数据，保留受限，不上传真实数据。离线恢复要求全新`.../Simulation/<原站>/<原ApplicationId>`目录：

```sh
dotnet <匹配原发布>/Ipc.Assembly.dll --action restore --mode Simulation --file <原备份.db> --destination <全新根>/Simulation/<原站>/<原ApplicationId>
```

恢复核验清单/原字节/配置身份，仅复制到新根；中断保留RESTORE-INCOMPLETE并拒启动。恢复后保持软件暂停；先核对外部事实再恢复，备份不能撤回外部动作。独立合成接收账不随客户端备份倒退。

解压portable候选后使用`python3 scripts/smoke-application.py --package <解压根> --dotnet <固定dotnet> --output <新证据.json>`，实际启动、作业、重启原身份和停止。

定向验证：`dotnet run --project tests/Ipc.Application.Verification -- .`覆盖三链、重量错误关联、PLC未知重启原查询、配置保旧、T3原件恢复、回调背压及Stop租约；旧Supply原请求/接收责任/配置/资源和Inspection按受影响原选择器复验。Windows后验：[17逐项清单](validation/application-convergence-17-windows.md)。不运行无关全量或为数量新增镜像测试。

发布：源码/随包指南提交后，使用新目录构建冻结候选：

```sh
python3 scripts/package-application.py --application sportmaster --target portable --dotnet <固定dotnet> --output <新目录>/sportmaster-portable
python3 scripts/package-business.py --composition wcs-to-plc-reference --target win-x64 --dotnet <固定dotnet> --output <新目录>/wcs-to-plc-win-x64
```

PowerShell `publish.ps1 -Composition direct-plc-reference -Mode Simulation -Output <新目录>`走同一打包权威。Windows候选同时包含portable装配入口及真正所选WPF桌面/依赖；portable只验证核心。ASSEMBLY.json/SHA256SUMS/ZIP hash绑定源SHA和实际二进制。发布不改变仓库可见性，不上传SDK/字体/客户材料；D/RV不因新工程验证关闭。
