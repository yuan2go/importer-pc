# 三Application可靠性、配置和部署 · 20

执行基线main08286542bbd007433d85be2cb8d129b366b007e0（PR19已合并）。[现行增量设计](../design/capability-reliability-20.md)与[19职责设计](../design/application-capability-19.md)共同生效。Supply/Inspection仍用其原运行入口，三个Parcel产品在构建时选择；不可安装后切换主业务。Windows实际运行、UAC/ACL、实体和真实协议验收后置；本指南不授予设备控制权限。

## 构建与首次参考运行

固定global.json的.NET10.0.402、Python3；签包还需OpenSSL。三个选择依次构建，禁止同时写共享bin/obj。

```sh
dotnet build tests/Ipc.Application.Verification -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --reliability-closure
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --capability-closure
dotnet run --project src/Ipc.Assembly -p:Application=WcsToPlc -p:NuGetLockFilePath=packages.wcs-to-plc.lock.json -- --config config/optional/wcs-to-plc --data-root /tmp/ipc20-new-root
```

另两个选择Sportmaster/packages.sportmaster.lock.json、DirectPlc/packages.direct-plc.lock.json。默认配置config/{sportmaster,direct-plc,wcs-to-plc}/business.json；实测/认证/图像参考配置config/optional/。控制台snapshot只读；其余命令沿BusinessCommand JSON，Id、ExpectedRevision、Kind、Owner、Value，客户端自动填原标识及修订。附加上传只让上传独立，采图拒绝仍结束主链，采图未知与称重责任都须核对。

## 原件、重量与图像处置

配置schema2必须显式WeightValidity：UntilDispatch持续校验到未发送阶段提交；AtAssociation只在同实体测量关联时检查新鲜度，后续保留不可变重量。新参考optional使用AtAssociation，默认配置为UntilDispatch；真实现场必须由独立资格记录批准完整策略。schema1原件缺字段仍按UntilDispatch，不回填/重测改原报文。两种策略均校验稳定性、质量、单位、精度、来源设备、实体和等待期限。

WaitingWeight与采集结果分别判断；采图未知不能接下一件，重量期限照常到期。采图拒绝和已处置件不会被迟到重量复活。原采集查询只取原帧，不能拍当前画面。BeforeMain须采图/上传接受，Attached须采图接受后主链才能走，上传可以独立待核对。

超时或持续新鲜失效后：软件暂停，核对原回执；未知必须先查询原身份。无在途/独立回执缺口、采集非Unknown/Conflict、PLC从未尝试且WCS为未发或已明确接受/拒绝时，可填写已检查/移除实体的说明，执行“检查后取消未发送意图”（AbandonUnsubmitted）。保留原操作人、测量、WCS接受/格口、PLC原报文/目标，未发阶段本地Cancelled，作业Abandoned不表示物理完成。有任何未知外部动作时拒绝取消，不能重测或改新目标绕过。

无图、Capture Prepared/Attempts=0的安全取消也持久ImageReleased，释放预留。释放先持久后删除；重启完成清理。未知/冲突采集及上传原件保留；额度不足只拒绝新件。重启Prepared/Attempts=0保留从未尝试原意图并暂停，显式继续/处置；Dispatching或有尝试保持Unknown，不自动重发。

Sportmaster完成条件为有效WCS业务接受与格口（拒绝为终结失败）。DirectPlc/WcsToPlc成功需组件报告物理完成，PLC业务接受只是中间事实。所选PLC必须声明BusinessAcceptance、PhysicalConfirmation，以及Query或CompletesOnSubmit；不能把写入成功改名物理完成。声明仅提交即完成但实际回执未给物理事实时隔离为冲突，不伪成功。现场完成语义及采图失败SOP仍需合同批准。

## 客户端与参数生效

慢核对期间普通写操作互斥，软件暂停及500ms只读刷新使用独立入口；应用CAS和守卫继续执行，暂停不能撤回已发动作。主链、采集、上传、原操作人和阻断原因分别显示。读码Target是稳定DeviceId，在参数表只读；被程序篡改保存也明确拒绝。受控更换需清偿原件、备份、身份/实体映射迁移及新部署，普通参数保存不承担此迁移。

维护步骤：暂停→独立认证维护→读取安装与配置→选择已安装精确提供者→编辑启用/业务绑定和组件schema参数→保存→读回PendingRestart及旧Effective。安装、启用、绑定、健康是四件事，安装不启动组件。保存只产生Pending；原件/附加上传/原回执未清偿则重启激活失败并保留旧Effective。初始化失败也保留候选与失败回执。二进制及参数不会热改在途原件。

参考认证alice/bob与maintenance、证明SIMULATION_ONLY仅为合成测试。真实认证的Simulation标志必须与所选签名提供者Assurance一致；真实身份不伪装模拟，模拟身份不能进入生产。维护还需真实maintenance授权和有效期。密码不进入配置、日志或原请求。

## Windows候选安装（实际验收NOT_RUN）

使用PowerShell7，维护管理员和指定普通运行账户分开。先从获信渠道核对完整ZIP/SHA256SUMS，在全新版本目录解压，例如C:\Program Files\ImporterPc\releases\capability20-独立版本；禁止覆盖19发布或从Downloads提权。候选内含以下实际脚本，无需回源码目录补文件：

```powershell
# 由提升的PowerShell7执行；账户和路径由部署负责人填写
.\provision-runtime.ps1 -RuntimeAccount 'MACHINE\station-runtime'
.\provision-extensions.ps1 -Mode Simulation -TrustFile 'D:\approved\extension-trust.Simulation.json'
.\protect-release.ps1 -Release 'C:\Program Files\ImporterPc' -RuntimeAccount 'MACHINE\station-runtime'
# 切回指定普通运行账户
& 'C:\Program Files\ImporterPc\releases\capability20-独立版本\desktop\Ipc.Desktop.exe' --config 'C:\Program Files\ImporterPc\releases\capability20-独立版本\desktop\config' --data-root 'D:\ImporterPc\data'
```

protect-release覆盖ImporterPc父目录及发布闭包，运行账户只有读/执行；不要给运行账户发布目录写权。新数据根由维护者事先创建并仅授予指定账户所需权限；本脚本不自动放宽数据盘ACL。机级策略、独立信任或指定账户不符会拒绝。客户端从同候选app/Ipc.Assembly.exe受保护runas安装，重新验证包SHA和发布闭包；UAC取消/错误只显示失败，不记安装成功。Windows ACL/UAC、指定账户、DPI/HID与截图都需真实环境另验。

## 签名组件与真实接入资格

参考内置参数才受reference://目标、127.0.0.1/parcel-reference/限制。签名组件schema和自身协议负责地址、字段、单位、编码、认证及回执映射，宿主不替它规定现场报文。继续用.ipcx签名、原ExtensionStore、ALC精确加载、包/设备/数据租约及Stop屏障。SDK/native直接P/Invoke及任意原生DLL加载仍未获批准；可用受控托管组件或已批准桥接，但缺厂商协议/SDK资料的具体适配仍BLOCKED。

新Application.Contracts=1.3.0；参考能力包1.3.0、观察/集成参考包1.0.7。所有示例Assurance.Simulation=true。旧精确包/SQL不覆盖、不重签；需要新不可变版本。旧未决用匹配旧发布清偿后再升级。Parcel SQL追加迁移2作为新语义读取门槛，SQL1及Supply SQL1～5/checksum不改；旧宿主拒绝未来迁移。升级前用匹配旧发布创建一致备份，旧件FrozenConfiguration及原请求仍按旧语义读取；不能回滚新DB让旧程序误读。

真实提供者须签名声明Assurance={SchemaVersion:1,Simulation:false,ProtocolEvidence:批准协议证据文件的SHA256}、ContractIdentity及Requirements。Production要求真实读码、维护认证、schema2策略和所有已绑定能力可满足交付条件。独立签名资格绑定**完整规范配置hash、宿主/业务契约hash、有效期、批准证据文件hash，以及每个SDK/system-driver前置的精确包/声明/实际文件hash**；测试发布者不能签生产资格。不是删除门禁，也不是所有真实提供者永久NotReady。

先在冻结的所选宿主执行以下只读动作，不创建端口、不启动设备：

```sh
dotnet app/Ipc.Assembly.dll --action deployment-identity --file /approved/config/business.json --data-root /approved/data
dotnet app/Ipc.Assembly.dll --action deployment-check --file /approved/config/business.json --data-root /approved/data
```

identity返回规范配置、ConfigurationHash、HostContractSha256及ApplicationContractSha256；IdentityOnly不表示资格通过。check重验签包/schema/独立证据/完成能力；PreflightPassed仍不是健康、接口或现场验收。资格文件名deployment-qualification.<ConfigurationHash>.json，放在独立信任文件同目录。Windows为ProgramData/ImporterPc，与受保护extension-trust.Production.json同目录；批准文件、SDK/桥接文件及其父目录须维护者保护，非链接且不可由普通账户写。运行中持有文件只读句柄，直至所有端口停止/释放；到期阻止新准入和发送，原件核对仍按原请求。

审核者创建DeploymentQualification schema1文档：Mode、ConfigurationHash、HostContractSha256、ApplicationContractSha256、IssuedAt、ExpiresAt、非空ApprovalEvidence数组（Path/Sha256）、Prerequisites数组（Package、Declaration及非空Files）。Declaration逐字匹配签名manifest的Kind/Identity/Version/Evidence。即使无SDK，也须明确Prerequisites=[]；不能造批准证据。将审批文档及密钥留在授权私有位置，离线签名：

```sh
python3 scripts/sign-deployment-qualification.py --document /private/reviewed-decision.json --publisher approved-publisher --key /private/approver.pem --trust /independent/trust.json --output /new/deployment-qualification.HASH.json
```

工具只签已有审查决定，校验独立公钥且拒绝覆盖，不生成批准人/现场证据。维护者部署签名文档与实际证据，按上述deployment-check读回。安装CLI extension-check/inspect/install均接受所选Mode并执行原签名、测试根隔离和Windows提升检查；安装不授予生产运行资格。Production恢复也仅允许原Application/Mode到新隔离目录，启动保持暂停并重新走精确部署资格，不回滚外部接收账。

## 构建与解包验证

先提交源码、配置、脚本及随包指南，再选择全新输出：

```sh
python3 scripts/package-application.py --application wcs-to-plc --target win-x64 --dotnet /pinned/dotnet --output /new/candidate
# 以新目录解压ZIP，逐文件核对SHA256SUMS和application-release.json
python3 scripts/package-application.py --application wcs-to-plc --target portable --dotnet /pinned/dotnet --output /new/portable
python3 scripts/smoke-application.py --package /new/unpacked-portable --dotnet /pinned/dotnet --output /new/smoke.json
```

完整指南与protect-release/provision脚本都纳入SHA256SUMS。portable不含Windows二进制，Windows候选含所选真实WPF工程；交叉构建/解包不能代替Windows执行。真实认证、秤实体/校准、原帧关联、WCS/PLC/上传协议和控制权限、完成语义、现场节拍与SOP分别需要授权资料及验收，不从合成PASS推定生产就绪。
