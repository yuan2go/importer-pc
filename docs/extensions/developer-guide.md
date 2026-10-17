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
