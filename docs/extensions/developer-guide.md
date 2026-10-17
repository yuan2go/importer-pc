> 当前判断见 [PR14后复核](../reviews/audit-reconciliation-01/POST-PR14-ANALYSIS.zh-CN.md)，下一实施为 [15工作包](../work-packages/WP-IPC-OPERATION-CLOSURE-15.md)。14已合并且原子问题分项验证；不同接收发生责任、维护/输入与UI仍有残余。本文有效职责/规则继续成立，历次执行结果只按其来源版本理解；本次更新前原字节见 [归档](../archive/pre-audit-reconciliation-publication-01/README.md)。

# FIX-14 扩展与维护增量

当前 [14报告](../runtime/conformance-fix-14.md)与[操作/恢复规约](../runtime/operations.md)接管本指南下方13历史时态。核心契约仍1；新宿主公共DLL字节/部署指纹不同，新分发参考包为1.0.2，禁止重签1.0.0/1.0.1或删除永久身份。旧精确包若不满足新宿主公共SHA继续拒绝；先以保留的匹配旧宿主/原包核对旧Unknown，再显式迁移，不改原请求Implementation/目标/报文/hash来绕过。原13候选保留。

IntegrationRuntime按实例、目标、部署指纹、epoch/sequence记录带有效期健康；Diagnostic另有Lifecycle/Historical/Port/Health及真实错误。当前requests是Supply实际业务依赖，辅助端口不自动变成主WCS；历史查询失败归原目标并保留原请求责任。Health/Query/Stop都计入在途所有权，取消/慢返回不提前释放包。

每窗ConfigurationDraft是普通维护、组件页的唯一编辑源；导入合法统一JSON后，设备/服务/参数/角色/requests目标及普通字段编辑、预览、保存/重读保留一致。Effective只读，CAS由应用检查。激活绑定组件页本次选中ConfigRevision、维护身份和上下文，不读隐藏维护页列表。非法导入不替换旧草稿，迟到schema读取由选择代次隔离，读取期间禁编辑该表单。扫码消费绑定作业/命令/输入版本，Persisted才清自己的输入；重复A不计量，迟到不能清新B。

共享安装库支持多数据根。运行、扩展修订保存、有责任的备份/恢复副本先登记消费者；卸载/恢复检查所有离线根的Effective/Pending/Unknown及终态冲突、接收围栏责任。不可读和旧库覆盖未知都阻断，不能把固定账户视为单数据根。消费者记录不因退出删除，不提供强制忽略入口。Windows受保护只读围栏由安装器建立，运行账户只在机级租约目录写登记；详见操作规约。旧库LegacyConsumersUnverified需要完整原根/副本盘点与批准迁移，当前不能宣称已无引用。

Storage4新增冲突原件与恢复语义，保留原SQL/checksum和terminal事实。原始回执日志与业务事务失败可恢复，无法接管原件时以预存围栏保守阻断，不能把日志按钮/内存告警当作落盘。已有精确依赖、签名、AssemblyRef和ALC政策不放宽；Production缺能力继续NotReady。

验证入口新增 `--conformance`，复用原Verifier、两审查探针、实际ProductRuntime/独立HTTP/SQLite/来源/发送链。Windows点击、扫码器和ACL仍NOT_RUN，交叉构建不代替它们。

---


# CLOSURE-13 扩展开发、安装与恢复

本指南对应[13工作包](../work-packages/WP-IPC-ARCH-EXTENSION-CLOSURE-13.md)和ExtensionContract1；[设计与职责](../design/controlled-extensions.md)、[实际结果/候选身份](../runtime/arch-extension-closure-13.md)。12指南[原字节](../archive/pre-arch-extension-closure-13/docs/extensions/developer-guide.md.txt)保留。Windows实际操作、普通账户/UAC/ACL、UI/输入/分辨率/DPI、听审与持续旅程全部WINDOWS_NOT_RUN，不能把下列命令写成实机验收。

## 公共契约与独立构建

观察包实现Ipc.Platform.IObservationExtension，Create(ExtensionContext)返回IControlledObservationSource。只构造，StartControlledAsync后才能采集；DeviceId/epoch/sourceEvent/sequence/admission必须真实关联；健康确认来自源，不能靠UI读取续期。观察包不得引用Supply实现或SQLite。

集成包实现Ipc.Supply.IIntegrationExtension，Create(IntegrationExtensionContext)返回IControlledIntegration。公共程序集为Ipc.Supply.Contracts.dll和Ipc.Platform.dll，无业务数据库、UI或根容器。实现Encode/Authority/ValidateResponse及ExecuteAsync/QueryAsync/HealthAsync、StopAsync、DisposeAsync、Diagnostic；以原请求和原Target校验，不从当前配置找替代地址。CancellationToken与有界超时必须穿透网络；请求超时/取消可能已经被接收，保持Unknown并查询原ID。Stop完成代表所有在途任务终止，Dispose失败不得伪称释放。

先发布冻结宿主，再独立构建；两个示例均不在宿主项目引用图：examples/BarcodeExtension和examples/IntegrationExtension。私钥只放构建者私有目录，不进Git或客户端：

```sh
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out /private/build/reference.pem
openssl pkey -in /private/build/reference.pem -pubout -out /private/build/reference-public.pem
python3 scripts/build-extension.py --contract /frozen/app/Ipc.Platform.dll --kind observation --output /new/reader-1.0.0.ipcx --key /private/build/reference.pem --publisher reference-test --trust /independent/trust.json
python3 scripts/build-extension.py --contract /frozen/app/Ipc.Platform.dll --kind integration --output /new/integration-1.0.0.ipcx --key /private/build/reference.pem --publisher reference-test --trust /independent/trust.json
```

独立政策SchemaVersion=1，Publishers每项为Id/PublicKeyPem/TestOnly；参考发布者TestOnly=true。包内公钥不构成信任，不自动导入。Production拒绝测试根。脚本只引用冻结公共DLL，校验宿主Ipc.*.dll前后SHA，记录每个示例自己的Git source commit和源字节指纹。新版本用--version和新路径，原包不覆盖。

产品装配在首个静态预检前注册公共契约并封存名单，平台不硬编码业务程序集名。依赖类库可用Modules=[]，真实引用仍须闭包完整，不能用空入口蒙混。manifest精确绑定HostContractSha256，集成还绑定PublicContracts.Ipc.Supply.Contracts的SHA。程序集实际AssemblyRef必须满足锁定net10.0框架/明确公共契约/完整签名闭包，版本、token、culture和所有权必须唯一；System.*不自动可信。SDK/系统驱动只作前置证据，未批准resolver时拒绝激活；原生/混合/PInvoke、脚本、任意XAML或命令安装均拒绝。

## 两类多实例及参数

包不等于实例，设备不等于服务。Packages选择完整精确闭包；Instances为观察设备，DeviceBinding.Driver=extension:实例ID且业务拥有Role。Services为外部集成，不携带DeviceId；Ports按业务用途绑定ServiceInstance和Target(Id/Revision/Contract/Endpoint)。供包主端口为requests，必须与StationConfig.Integration一致，另可配置独立辅助命名端口；所有实例各有参数、健康和诊断。

schema为明确有限子集：string/integer/boolean/choice/string-list/secret-reference；分组、单位、长度/范围/集合上限、默认、条件必填、只读、说明、Maintenance权限和Restart生效级别。未知语义拒绝。敏感参数只存credential:名称引用，参考实现不读取生产密码。UI不执行包内XAML/脚本；安装后直接由schema生成表单。

同一PAGE08“组件与部署”可读包/信任/精确依赖、安装计划、设备及服务实例、服务目标、参数有效值与草稿、部署差异/回执、运行诊断、升级/停用/卸载及原安装操作。每窗有自己的草稿和查询代次，页面切换不创建第二业务/来源/连接/发送器。维护身份到期、无权、版本竞争、未激活与失败均由应用守卫裁决。

1. 安装原.ipcx并读取Installed回执；安装不激活。
2. 选精确包加入部署草稿，选模块创建实例。设备填写真实稳定DeviceId和业务Role；服务填写端口与目标身份，不填写虚构设备。
3. 编辑参数，比较有效值；包、实例、参数、角色/端口和站点参数形成唯一StationConfig部署修订。
4. SaveConfig/ActivateConfig经应用CAS、有效维护身份、资源/原请求责任校验。保存Validated不代表生效；PendingActivation要求受控停止/重启。
5. 最终组合取得实际资源与包租约并初始化成功，才写Effective。失败记录ActivationFailed，旧Effective和原业务账保留；完全停止后下一受控启动可回旧组合。

## CLI与Windows运维

CLI与WPF使用同一个ExtensionStore和业务维护契约；不会在提权阶段构造插件。portable开发可显式指定合成store/trust路径，Windows只能使用已由维护主体配置的ProgramData目录与独立信任策略，不开放用户路径覆盖：

```sh
dotnet app/Ipc.Product.dll --action extension-list --mode Simulation --extension-root /reference/store --extension-trust /independent/trust.json
dotnet app/Ipc.Product.dll --action extension-inspect --mode Simulation --file /offline/reference.ipcx --extension-root /reference/store --extension-trust /independent/trust.json
dotnet app/Ipc.Product.dll --action extension-install --mode Simulation --file /offline/reference.ipcx --sha256 ORIGINAL_PACKAGE_SHA --extension-root /reference/store --extension-trust /independent/trust.json
```

实际业务控制台--action serve接受snapshot、extensions、integration-diagnostics，以及原JSON command SaveConfig/ActivateConfig/原请求reconcile。先snapshot取ExpectedRevision；维护登录与应用守卫不可绕过。无需UI手工编辑数据库。

Windows候选在Program Files/ImporterPc的受保护发布目录，经同版protect-release.ps1、provision-runtime.ps1和provision-extensions.ps1配置指定普通运行账户、机级租约及只读包库。WPF调用同版受限Ipc.Product.exe OS runas辅助程序，辅助程序重新验信任、原SHA、路径和计划；UAC取消/ACL拒绝不得伪称成功。不要从Downloads可写目录提升。生产签名根/SDK/WCS及批准策略未取得，继续BLOCKED。原12指南中的Windows具体步骤仍可参照，但没有本轮实机PASS。

## 恢复、原请求和卸载

同ID/version永远绑定同一manifest，跨Uninstalled/retired/中断/并发/修复保持。原精确包可重装；更换签名内容必须新版本，不删登记、日志或identities目录。安装Prepared/FilesVerified/Published/Registered原操作ID和原包SHA可恢复，UninstallRetired中断也用原操作恢复；损坏原字节移入damaged保留，修复需要独占包租约。

新请求首次T1持久化精确实现/包/模块/实例/部署/参数指纹/合同SHA/目标/超时及参考业务策略。历史查询/回执验证使用原身份，缺旧能力精确阻断；不换目标、不换ID、不改原报文/hash、不默认重发。旧Implementation缺失的SIMULATION-v1只走明确原Binding/原payload兼容适配。12公开观察包仅对登记指纹允许Contract1兼容，不删除HostContractSha256或改主版本冒充兼容。

先在统一部署停用实例、移除绑定及包并受控重启。卸载重新检查反向依赖、Effective/Pending、所有未决原请求及实际包租约；不是UI分页快照。Stop/Dispose或查询/T3/审计未终止时保留应用/库/日志和全部租约，不能并行创建第二所有者。成功只退休文件，保留登记、审计、业务事实。旧SQL/checksum1/2/3不改，不回滚station.db隐藏外部接受；回退核心仍按备份/匹配旧发布/新根/恢复隔离的原流程。

## 验证与候选

--architecture实际覆盖P2身份失败窗口、真实AssemblyRef负例与合法依赖、两个独立包、两类多实例、冻结原请求/改目标/缺旧能力、取消、失败激活、Stop所有权和两个真实业务组合。--extensions保留12中断安装/修复/卸载/升级与来源恢复旅程；--baseline、--followup、--review-followup保护相关T1/T2/T3、双箱及所有权。具体源码、命令与结果只以13报告/checks为准。

package-reference.py integration-reference交付完整供包portable或win-x64未验候选，含两类独立签名包及单独TEST ONLY公钥策略；package-business.py提供inspection-reference和supply-assembly真实裁剪组合。锁、SourceCommit、Release、Storage/MinimumReader、契约、每个独立插件身份、精确文件SHA、ZIP SHA和远端回读均分别记录。Windows包是候选，不能当已验最终版或现场资格。

## PR12补丁的包内容身份

组合中的`reference.barcode`和`reference.integration`已声明补丁版本1.0.1。`package-reference.py`按组合精确版本传给独立构建器，`smoke-extension.py`按发布的`composition.lock.json`选择原包，不扫描最新版。插件源提交仍分别取各自examples目录的最后提交；宿主提交及公共契约SHA另记。换宿主导致manifest公共契约指纹变化时必须使用新包版本；已发布1.0.0及永久ID/version登记不可改写。Production批准与实际Windows接入状态不随参考包版本改变。

重复构建证明：`python3 scripts/verify-extension-reproducibility.py --contract-directory <portable/app> --contract-directory <win-x64/app> --key <external-private-key> --trust <independent-test-policy> --publisher reference-test --version 1.0.1 --dotnet <pinned-dotnet> --output <fresh-evidence.json>`。两种扩展对每个冻结宿主各构建两次，必须有相同原始manifest和payload，宿主字节不变；RSA-PSS签名随机性不要求ZIP原字节相同。macOS临时目录先解析物理路径并映射整个构建根，避免随机/private/var路径进入托管DLL。新输出不可覆盖旧证据；私钥不会进入输出。
