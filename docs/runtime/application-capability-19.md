# 三Application交付与能力运行 · 19

当前实施基线main `08b8067aa6cba6a148252296c7a5b18a27b3be65`，PR18已合并。[现行设计](../design/application-capability-19.md)及[工作包](../work-packages/WP-IPC-APPLICATION-CAPABILITY-CLOSURE-19.md)为职责/策略唯一入口。17/18报告保持原SHA和历史结果。候选SourceCommit和全文件身份以随包application-release.json、ASSEMBLY.json、SHA256SUMS为准；验收及剩余现场接入仍按分层证据登记。

|所有者/能力|实际实现|资格边界|
|---|---|---|
|Sportmaster|SportmasterFlow选择WCS步骤、解释格口接受/拒绝及展示终态；不创建PLC端口|参考协议已实现；真实WCS待接入|
|DirectPlc|DirectPlcFlow通过共用重量守卫后准备PLC并解释接受/物理完成；不创建WCS|参考协议已实现；真实PLC待接入|
|WcsToPlc|WcsToPlcFlow取得有效格口；接受事实、原格口和PLC意图同一T3；独立步骤及结果解释|参考协议已实现；真实合同/安全联锁待接入|
|公共能力|Platform.CommonCapabilities中的会话、身份、重量、通用采图/上传、健康及能力声明；业务意图/格口/作业留在Application.Contracts|不迁移Supply/Inspection状态/DTO；精确公共契约使用新身份|
|公共机制|ParcelFlow的队列、事务、接收发生/原件、原请求和配置/资源恢复机制|无流程引擎、无万能总线；切页/多窗口不重建设备|
|可选能力|登录、启用秤、采图+前置/附加上传，均经同一配置和Factory绑定|关闭能力不创建业务依赖端口；参考维护身份单独存在|
|提供者管理|原ExtensionStore签名安装、schema、ALC、精确包及资源租约；新增login/camera/upload工厂|Production缺真实输入继续NotReady；无未经授权设备控制|

## 构建及启动

SDK由global.json固定为10.0.402。不更换框架。安装SDK后将其bin放PATH，依次选择Application；同一bin/obj不可并行写不同组合。

```sh
dotnet build tests/Ipc.Application.Verification -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --capability-closure
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --site-capabilities
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll .
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=WcsToPlc -p:NuGetLockFilePath=packages.wcs-to-plc.lock.json -m:1 -nr:false
# 另两个选择：Sportmaster/packages.sportmaster.lock.json；DirectPlc/packages.direct-plc.lock.json
```

默认参考配置在config/{sportmaster,direct-plc,wcs-to-plc}/business.json；开启能力的完整参考配置在config/optional/。后者可将Images.Strategy设为Attached；默认BeforeMain。默认配置补全重量有明确Configured来源，绝不用于代替失败实测。要求秤时Source=Measured并绑定Scale；关闭Source=Disabled且Scale=null。现场组件声明强制实测或图片地址时，冲突配置拒绝。

```sh
dotnet run --project src/Ipc.Assembly -p:Application=WcsToPlc -p:NuGetLockFilePath=packages.wcs-to-plc.lock.json -- --config config/optional/wcs-to-plc --data-root /tmp/ipc19-new-root
# 控制台：snapshot；其余命令为BusinessCommand JSON，稳定Id、ExpectedRevision、Kind、Owner、Value。
# Windows发布后：desktop/Ipc.Desktop.exe --config desktop/config --data-root <指定本地根>
```

构建固定主业务，运行时只选所属页面和standard/compact布局。没有安装后任意切换Application。保留原Supply/Inspection构建与SQL1～5；新场地原SQL1字节不变。

## 客户端操作及生效边界

主屏保留原视觉基线的信息层级、状态/原因和固定操作栏；当前业务、原操作人、图像元数据及独立上传状态分开投影。已读v1.1原HTML、分离CSS/JS、tokens及完整交接；本次WPF更改为代码和交叉构建结果，不能冒称Windows截图或实机验收。

维护页通过“读取安装与配置”显示实际安装包和提供者。实例表分别编辑提供者、启用、业务绑定、稳定实例、协议目标及合同；维护认证提供者由MaintenanceLogin独立绑定，可复用签名login模块；旧Simulation配置缺省仅保留原参考维护边界。参数由签名schema/参考组件声明生成表单，支持choice/boolean/限长和secret-reference（只存凭据引用）。称重与图像策略独立编辑，保存再次校验schema、必需输入、精确包和Application兼容性。无JSON编辑框作为必需操作入口。

“启用但未绑定”实例保存在同一ParcelConfiguration.CapabilityInstances；实际端口只为业务绑定构造。安装、配置启用、绑定和运行健康不是同一事实。绑定字段和实例清单必须逐字节一致，不能形成第二权威。读码来源沿既有Observation扩展及DeviceId/epoch协议；相机取图是另一能力。

暂停只停止新业务准入与后续发送，不撤销已发送动作，也不赋予维护权限。参考维护主体maintenance，公开合成证明SIMULATION_ONLY，经独立认证组件取得15分钟维护会话。实际客户角色、认证源和授权策略仍需真实合同。登录启用时alice/bob只是合成主体；密码不入库/请求/审计。普通作业会话受SessionLifetimeMs和提供者ExpiresAt双重限制，失效必须重认证；原作业保持接纳时身份快照。未结作业只能原主体重新认证，退出或换班不清原责任。

安装页复用ExtensionStore；Windows普通客户端调用同候选app/Ipc.Assembly.exe的受保护runas帮助程序，子进程重验原SHA、信任、受保护发布及管理员权限，完成后从原安装登记读回。UAC取消/失败保持失败。非Windows仅显式参考store/trust路径。安装不启动插件。所有参数/二进制默认PendingRestart；重启前未结主业务、附加上传或原回执责任阻止激活。旧件冻结原参数/原目标/原实现hash；失败保留旧Effective。维护备份/重连同样需暂停、认证并排空外部责任。

## 重量和图像

称重WaitTimeoutMs是本件等待期限，MaximumAgeMs是实测新鲜度。准入不接受坏秤，重量验证admission/device/id/来源/单位/精度/正值/稳定性/质量/事件时间；下游WCS/PLC及Resume重复使用同一重量守卫。超时不按未称重继续，迟到/重复不覆盖原测量。测量去重行和作业重量同事务，异常退出不能留下已去重未关联缺口。

称重超时可先暂停，记录已检查/移除实体的处置说明，选择“检查后放弃未提交作业”；仅无WCS/PLC意图且无在途/原回执缺口时允许。取消从未尝试的图像意图使用本地Cancelled状态，不伪造外部拒绝。原未知上传仍独立保留；恢复后下一实体使用新admission和新测量。已形成外部意图的过期原件保持阻断，需对应现场权威核对，不能重建请求或自动重测改报文。

接纳时采图请求绑定原AcquisitionIdentity(DeviceId/Epoch/SourceEventId/AdmissionId/ObservedAt)，重量等待期间保留原件；提供者必须按原采集标识取得/查询原图，不能拍当前画面或取最近缓存。参考驱动只生成显式合成PNG，不证明真实SDK已实现这一关联。公共采图接口由调用域选择请求/回执类型，不限定PNG、文件后缀或reference://；临时原件校验关联、安全相对路径、媒体类型、长度和SHA256。上传URI校验absolute http/https/reference且无内嵌凭据；真实地址/认证转换属于对应组件。

BeforeMain：上传接受后才进入依赖主链，未知/拒绝不发WCS/PLC。Attached：上传有独立StageFact，等待/拒绝/未知不覆盖主链状态；主链可继续，恢复仅查询原上传，绝不因无记录自动重传。暂停期间从未尝试的附加上传可显式“继续未尝试附加上传”；已有尝试只能核对。

单件MaximumBytes、总TotalBytes及积压MaximumPending均有限。配额不足拒绝新接纳，不挤掉旧原件。上传终态/本地安全取消后先持久ImageReleased再删除临时文件；重启完成删除，已释放历史无须原文件。未知/冲突/待上传原件仍验证hash并保留；没有长期图片历史需求。原PNG/reference行为只属于参考适配器和独立夹具。

## 原请求与外部等待

T1冻结请求/参数/转换实现hash，T2持久尝试及接收责任后才外呼；外呼释放聚合锁，回执回来后重新读取最新作业并提交T3。快照使用带有效期的健康缓存，不同步等待网络；Pause能在慢请求时持久，不能撤回在途动作。停止排空主worker、附加任务、健康worker和其他在途认证/安装，超时保持原资源/包/DB租约。

ContinuePrepared保留原守卫，只发送从未尝试的原意图；重启Prepared/Dispatching保守转Unknown。查询无记录仍Unknown，组件不声明Query时不调用权威查询。传输、协议确认、业务接受和物理完成分开；WCS接受/原格口/原PLC意图仍同事务。备份是在线SQLite+配置/原回执/未释放原图的集合，恢复隔离暂停且无登录，不回滚外部接收账。

## 备份路径与健康预算修复

PR19合并后的增量见[本轮工作包](../work-packages/WP-IPC-BACKUP-HEALTH-RECOVERY.md)。`ParcelRuntime.BackupAsync`保持暂停、维护认证和外部排空守卫，目标必须在运行目录之外且全新。`.recovery/COMPLETED.json`的Files键统一为`/`；仅本机文件枚举转换分隔符，输入清单的反斜杠不自动改写。配置、原回执和未释放图像在受限目录内枚举；排除实时租约，检查链接、大小写别名、Windows保留名、重复JSON键、完整集合及SHA256，复制后Flush并再次验hash。缺完成标记或失败残件保持不可恢复，不删除原数据或自动重做备份。

恢复只进入全新同Application隔离目录。校验在创建目标前完成；复制中断保留RESTORE-INCOMPLETE，成功才发布RESTORED-ISOLATED；重启暂停、不复活登录、不重传未知请求。以前含反斜杠的非规范清单仍明确拒绝，保留原件；不能手改原清单/原图或当成已核验恢复。对旧有效发布清偿未决后，使用修复版生成新备份，不能改旧请求实施hash来强行升级。清单hash不是签名，备份需授权受控保存。

启动、后台巡检和每阶段健康探测分别使用TimeoutMs预算；阶段通过健康守卫后，发送/查询有独立TimeoutMs预算。预算耗尽返回NotReady，真正启动取消/停机取消继续传播；发送前停机不产生发送尝试，发送后取消保留Unknown和原身份。对已接受PLC的确认查询失败不擦除既有接受事实，实体完成仍需独立证据。ReferencePeer的健康响应按固定标记长度有限读取；旧Supply loopback自身HTTP超时报不可用，外部取消继续传播。多页面/快照仍不承担网络探测，不改变Production拒绝或SDK/现场资格。

固定SDK下的新增定向验证：

```sh
dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --backup-health-recovery
```

Windows必须另跑同一实际采图备份/恢复旅程、ACL/重解析点和普通账户验证；本次macOS验证不构成Windows验收。符号链接负例需要测试账户有创建链接权限。旧签名包仍按精确公共契约和原实现绑定检查，本轮不覆写包或迁移SQL。

## 新签名提供者

示例examples/ParcelCapabilitiesExtension新增login/camera/upload工厂和strict-wcs输入要求，继续使用原build-extension.py和.ipcx格式。新Application.Contracts=1.2.0，示例包使用新不可变1.2.0；Platform精确指纹变化也要求新观察/集成参考包1.0.6。旧包不覆盖、不重签为兼容；旧未决用匹配旧发布清偿。

```sh
python3 scripts/build-extension.py --definition examples/ParcelCapabilitiesExtension/extension.build.json --contract <冻结宿主>/Ipc.Platform.dll --check-host <冻结宿主>/Ipc.Assembly.dll --output <新路径>/capabilities-1.2.0.ipcx --version 1.2.0 --publisher <获准发布者> --key <私钥路径> --trust <独立信任文件> --dotnet <固定dotnet>
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --signed-capabilities <签名包> <独立信任文件>
```

Module.Kind为login/camera/upload时分别实现ILoginExtension/ICameraExtension/IUploadExtension；合同版本、Requirements(实测、图片地址、查询、物理确认)及参数schema属于签名模块。通过安装不等于获得生产SDK/权限。真实提供者仍须SDK、现场字段/单位/编码/认证、状态/握手样例、查询证明、实体关联和原件生命周期验证。

## 打包与验证

先提交源码和随包指南，输出使用全新路径。每个Application重复三次：

```sh
python3 scripts/package-application.py --application sportmaster --target portable --dotnet <固定dotnet> --output <全新路径>/sportmaster
# 真正解压sportmaster.zip后执行；另两个选择direct-plc/wcs-to-plc
python3 scripts/smoke-application.py --package <解压根> --dotnet <固定dotnet> --output <默认链证据.json>
python3 scripts/smoke-application.py --package <解压根> --dotnet <固定dotnet> --optional-features --peer-fixture src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --output <前置上传证据.json>
python3 scripts/smoke-application.py --package <解压根> --dotnet <固定dotnet> --optional-features --attached-upload --peer-fixture src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --output <附加上传证据.json>
```

Windows实际客户端/交互/DPI/HID/截图/UAC/ACL：NOT_RUN。真实SDK/相机采集关联/秤实体及校准/WCS合同/PLC地址握手控制权/上传认证/现场节拍：BLOCKED。Production继续拒绝；D/RV不因参考验证关闭。
