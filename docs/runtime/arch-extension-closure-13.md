> PR12复核补丁正在形成新候选；下文dc5b405及其验证为首轮冻结记录，保持原身份。补丁源修复选中实例重命名及精确版本打包；a1c41bc本地候选因重复构建内容漂移撤销且未上传，修复与失败证据另行追加。实际Windows继续WINDOWS_NOT_RUN。

# CLOSURE-13 实施、验证与候选交付

**本轮授权的核心工程完成，Mac参考验证PASS，WPF交叉构建PASS；Windows实际验收统一延期，WINDOWS_NOT_RUN；真实SDK/WCS/现场资格BLOCKED。** 不以PR状态、交叉构建或HTML图关闭Windows/历史RV。

基线及冻结复核main均为`feed6e9ce79449e48c667c17e2671939a86fb588`，相对分析基准无增量，开工/冻结均无并行开放PR。隔离worktree、分支`codex/arch-extension-closure-13`；原main工作区的未跟踪用户交付物与受限客户资料原样保留，没有进入本分支。用户原始执行要求[逐字保存](../work-packages/Codex-WP-IPC-ARCH-EXTENSION-CLOSURE-13.md)，[工作包及AC映射](../work-packages/WP-IPC-ARCH-EXTENSION-CLOSURE-13.md)明确未收到额外独立附件，不冒称读取未取得文件。

前置提交`2c04d69`在业务修改前保存[原当前指引/manifest原字节](../archive/pre-arch-extension-closure-13/README.md)，更新职责和Windows延期范围；12工作包/报告/checks/原始证据、D/RV、原UI/HTML/tokens均未倒写。P2原审查见[PR11原始回读](evidence/arch-extension-closure-13/pr11-review.json)。Windows延期来自用户决定，本轮没有寻找VM/runner、开通环境或扩大OAuth权限。

开发分支已创建面向main的[PR #12](https://github.com/yuan2go/importer-pc/pull/12)，未合并；报告/manifest提交`10ca8f4`承接冻结宿主dc5b405。

## 冻结身份

|对象|身份|
|---|---|
|宿主与候选输入|`dc5b405f69535df84228199b40ce84048aa153b0`；主体实现`06dc9c8`，导航/输入冻结`701ee0d`，健康兼容回归修复`dc5b405`|
|独立读码源|`6507edb4c10699df3bdeb8f91da3bf31f81b94f9`，examples/BarcodeExtension，版本1.0.0|
|独立类型化集成源|`01038cc5e21d200e4545c0a26594adc05eb62923`，examples/IntegrationExtension，版本1.0.0；首版公共契约/集成源`b1787f1`|
|发布/存储/合同|ARCH-EXTENSION-CLOSURE-13；Storage/MinimumReader=3/3；SIMULATION-v1；ExtensionContract=1|
|portable/win-x64同源指纹|`4dbf95f1a75724bdc85d849fa392ba85c1f5e502f856909a65f69e7784e77d68`，两目标相同|
|宿主公开契约|Ipc.Platform.dll SHA256 `4e8b8eceda06d38aabd74af3d78fde8aedf9769082d05f6964172caa7fbf1afd`；Ipc.Supply.Contracts.dll `917abdb121e9dbf063ec24157bdd3d815171a618685d1198f0cc1df36d423041`|
|制品|[明确未验预发布](https://github.com/yuan2go/importer-pc/releases/tag/closure13-reference-dc5b405)，包/源/独立ipcx/单独测试公钥/清单/回读收据，共12附件；没有私钥|

后续文档、manifest与资料校验工具提交不改变上述宿主/插件/配置/依赖/打包输入，不重新打包冒称同字节。主工程、独立插件和文档提交分别登记。原SQL/checksum与参考报文原件的同字节核对见[兼容输入](evidence/arch-extension-closure-13/compatibility-inputs.json)。

## AC13逐项结果

C1～C13是下节实际命令。`PASS_MAC`仅为明确合成/参考边界；WPF交互均未执行。

|AC|实现路径与提交|验证/证据|结果与遗留|
|---|---|---|---|
|AC13-01|2c04d69；INDEX、framework/decisions、controlled-extensions、前版本归档及13工作包；[开工记录](evidence/arch-extension-closure-13/opening.json)|最新main/PR核验、12原件/P2/UI实物读取；C13归档对账|PASS；历史D/RV不改，公开范围仍PUBLIC_SUBSET_ONLY|
|AC13-02|06dc9c8/dc5b405；Platform/BusinessAssembly、Ipc.Assembly构建注册、Ipc.Inspection、ReferenceProduct；供包页面仅接IStationRuntime/IStationApplication，App为装配根|C1/C2/C9/C11；[发布候选两个业务实际命令/查询/停止](evidence/arch-extension-closure-13/ipc13-published-businesses-smoke.json)，[非供包控制台](evidence/arch-extension-closure-13/inspection-console.log)|PASS_CORE/MAC；Inspection为功能完整验证业务，没有强造第二客户业务或完整UI；通用路径无Supply/left-right/loopback硬编码，旧专用装配保留|
|AC13-03|06dc9c8；ExtensionStore永久身份登记及全历史检查；ManagedAssemblyPolicy、ExtensionPackages、ExtensionLoading、ExtensionPublicContracts|C2/C3；[P2与依赖日志](evidence/arch-extension-closure-13/ipc13-accepted-architecture.log)：卸载/退休/中断/并发后同版本异内容拒绝、原包重装；缺实际引用/假System前缀/版本冲突/歧义拒绝，合法签名类库与运行解析PASS|PASS_MAC；静态检验不执行插件，目标框架/版本/culture/token/公开契约精确核验，不放行System.*；纯类库无需假入口|
|AC13-04|06dc9c8/01038cc；Supply.Contracts/IntegrationExtensions、Product/IntegrationRuntime、独立IntegrationExtension；Reconcile不再固定构造Loopback|C2/C9/C10；[双后装发布宿主](evidence/arch-extension-closure-13/ipc13-published-extensions-smoke.json)、[原请求旅程](evidence/arch-extension-closure-13/integration-journey.json)|PASS_REFERENCE；编码/Authority/验回/HTTP发送/查询/健康/超时/取消/Stop/Dispose/诊断真实连通；服务实例和ServiceState健康均无虚构DeviceId；仅既有本地参考合同，厂商/真实WCS后置|
|AC13-05|06dc9c8/dc5b405；DeploymentExtensions.Services/Ports、ExtensionDeployment、WPF ExtensionsView/MainWindow、IExtensionManagement/IStationRuntime；CLI extension-list/serve同契约|C1/C2/C3/C5；两设备+两服务参数/目标隔离、端口冲突、schema与统一部署CAS；[UI映射](../ui/arch-extension-closure-13-mapping.md)|ENGINEERING_PASS/MAC_PASS，实际UI WINDOWS_NOT_RUN；公共契约不引用DB/业务实现/UI/根容器。旧StationConfig/Contract1/签名/HostContractSha/原SQL保留|
|AC13-06|06dc9c8/dc5b405；StationApplication.Prepare T1冻结IntegrationImplementation、原请求路由/验回；IntegrationRuntime及Product最终查询/T3/审计清理屏障、Sender/Observation模块责任拆分|C2～C7；旧目标查询、新目标/版本不得替代、缺旧能力阻断；取消后接收端已接受仅查原ID；部分构造失败、失败激活保旧、真实Stop及Dispose延迟持有库/站点/设备/包，依赖卸载阻断|PASS_MAC；不改旧ID/报文/hash/合同/目标、不默认重发、不回滚业务库；安装≠激活、保存≠生效，Production继续拒绝缺能力|
|AC13-07|dc5b405最终源码；ArchitectureClosureVerification及保留12/11/08/T1T2T3相关断言|C1～C11；六个受影响选择器全部PASS；冻结宿主后两种插件独立编译/安装/实际链路与宿主哈希不变；候选七项核心冒烟与两业务启动PASS|PASS_MAC；没有以普通dotnet test代替Verifier，没有跳关键恢复；初次失败/撤销候选保留，修复见下节|
|AC13-08|dc5b405冻结候选、后续报告/manifest/工具增量；完整WPF绑定、依赖锁、操作/恢复/扩展说明与新候选|C1/C8～C13；[候选精确组合](evidence/arch-extension-closure-13/candidate-inventory.json)、[11资产完整回读](evidence/arch-extension-closure-13/remote-readback.json)、[第12回读收据](evidence/arch-extension-closure-13/readback-receipt.json)|CANDIDATE_DELIVERED，WPF_BUILD_PASS；实际Windows/现场延期，不覆盖12候选，不改旧验收，不合并PR|

## 实际验证命令与范围

固定SDK10.0.402；下列`dotnet`使用固定SDK10.0.402的本机可执行文件。均从隔离worktree根执行。最终源码dc5b405上的日志以`ipc13-accepted-*`为准；较早PASS/失败均保留形成时身份。

|命令|实际结果/原始日志|
|---|---|
|C1 `dotnet build ImporterPc.slnx -m:1 -nr:false -p:EnableWindowsTargeting=true`|0警告/0错误，含WPF；[构建](evidence/arch-extension-closure-13/ipc13-accepted-build.log)|
|C2 `dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --architecture`|PASS；[日志](evidence/arch-extension-closure-13/ipc13-accepted-architecture.log)、[实际组合/宿主哈希/安装操作](evidence/arch-extension-closure-13/closure-13.json)|
|C3 同上 `--extensions`|PASS，原12安装中断/坏包/修复/并发部署/升级/卸载/实际来源链；[日志](evidence/arch-extension-closure-13/ipc13-accepted-extensions.log)|
|C4 同上 `--baseline`|PASS，原T1/T2/T3、防重、进程中断/恢复及原请求；[日志](evidence/arch-extension-closure-13/ipc13-accepted-baseline.log)|
|C5 同上 `--followup`|PASS，身份/版本/激活/有效配置及持续双箱旅程；[日志](evidence/arch-extension-closure-13/ipc13-accepted-followup.log)|
|C6 同上 `--review-followup`|PASS，展示策略与stop-throws/stop-pending/dispose-pending完整Product所有权及保留期间原请求查询；[日志](evidence/arch-extension-closure-13/ipc13-accepted-review-followup.log)|
|C7 同上 `--closure`|PASS，来源、历史/关闭、通知/清理相关链；[日志](evidence/arch-extension-closure-13/ipc13-accepted-closure.log)|
|C8 `python3 -m unittest discover -s tools -p 'test_*.py' -v`|10 PASS；[日志](evidence/arch-extension-closure-13/ipc13-tools.log)。工具只新增三个真实项目的精确bin/obj生成目录，docs/未知工程/源码断言保留；不放宽历史资产检查|
|C9 `python3 scripts/package-reference.py --target portable\|win-x64 --composition integration-reference --extension-key $PRIVATE_BUILD_KEY --extension-trust $INDEPENDENT_TEST_TRUST --output $NEW_OUTPUT`；`python3 scripts/package-business.py --composition inspection-reference\|supply-assembly --output $NEW_OUTPUT`|四种独立新输出；[构建0](evidence/arch-extension-closure-13/ipc13-accepted-package-0.log)、[1](evidence/arch-extension-closure-13/ipc13-accepted-package-1.log)、[2](evidence/arch-extension-closure-13/ipc13-accepted-package-2.log)、[3](evidence/arch-extension-closure-13/ipc13-accepted-package-3.log)。每次独立build-extension只引用冻结公共契约，宿主哈希前后不变|
|C10 `python3 scripts/smoke.py --product $UNPACKED/app --fixture $UNPACKED/fixture/Ipc.Fixture.dll --dotnet $DOTNET --output $EVIDENCE`；`python3 scripts/smoke-extension.py --release $UNPACKED --dotnet $DOTNET --output $EVIDENCE`|真实解压核心七项PASS；两种签名后装均进入来源→供包→SQLite/Outbox→独立HTTP接收端，唯一接受2、停止成功、宿主全Ipc.*哈希不变；见core/extension smoke JSON。Windows项只按未验解释|
|C11 `dotnet $UNPACKED/app/Ipc.Assembly.dll --config $UNPACKED/app/config --data-root $NEW_ROOT --view inspection-history\|history --layout compact`，逐行真实命令/查询/quit|两个候选实际进程PASS，Inspection BeginBatch/RecordSample/CloseBatch，Supply Login，切视图共享同RuntimeId；[证据](evidence/arch-extension-closure-13/ipc13-published-businesses-smoke.json)。原12签名包在新冻结宿主安装/实际供包/停止PASS；[兼容冒烟](evidence/arch-extension-closure-13/ipc13-legacy12-compatibility-smoke.json)、[原字节下载来源](evidence/arch-extension-closure-13/legacy-custody.json)|
|C12 `gh release download closure13-reference-dc5b405 -D $NEW_READBACK`，逐文件完整SHA/ZIP CRC/内层SHA256SUMS/插件文件核对；另下载回读closure13-READBACK.json|11附件170172418字节全匹配，再新增/下载校验第12回读收据；没有覆写任何包字节。初版CANDIDATE的FileCount包含目录，精确普通文件数以READBACK.VerifiedFileCount及当前inventory为准，勘误保留而非改写旧元数据|
|C13 `python3 tools/verify_archive.py --root $ROOT --scope public`，原main与当前逐项对账|原main与当前均FAIL，267项历史资产错误，无新增/豁免/降低断言；[逐项对账](evidence/arch-extension-closure-13/archive-comparison.json)保留；不能用历史资产失败冒充业务测试失败或反之|

## 候选、安装与运行

[预发布下载](https://github.com/yuan2go/importer-pc/releases/tag/closure13-reference-dc5b405)提供四ZIP、两份独立源码、两个独立ipcx、单独TEST ONLY公钥政策、初始清单/哈希及完整回读。各包内有SHA256SUMS、精确composition.lock、配置和操作说明。普通文件精确数：inspection 44、supply-assembly 84、供包portable 158、Windows 843（均含自身SHA256SUMS）；模块/插件每个精确SHA见inventory及下载收据。

|ZIP|SHA256|
|---|---|
|closure13-dc5b405-inspection.zip|`c8d83ac5de25564b1c09d604804157306689db013b0ff784a3be91bd982855bc`|
|closure13-dc5b405-supply-assembly.zip|`b9d87c6887f711eb2e88fbc659aa9105a7aba804eae948d537c22adc46c649a1`|
|closure13-dc5b405-integration-portable.zip|`c962796996b02a55298ae53ae3f4f886f9c38b909effa85fcaffa5b8df97d06e`|
|closure13-dc5b405-integration-win-x64.zip|`7cfedccd3ef402e43059f2823a4196752845d7aaa40e491b65782591c0a17c3d`|

供包候选沿用station.ps1及[运维入口](operations.md)；纯核心可用Ipc.Product --action serve。同一维护入口/CLI真实管理契约操作，不手改数据库。独立源包附冻结公共契约构建说明；从外部可信渠道配置测试公钥，不从包自带证书信任。Production缺批准SDK/WCS/身份/数量实体策略继续NotReady，不运行模拟成功替代。Windows包自包含，实际WPF运行/普通账户/UAC/ACL/分辨率DPI/扫码/触控/听审/持续性能均需后续统一验收。

## 失败保留与未验范围

初次失败保存在[初次失败原始基线](evidence/arch-extension-closure-13/initial-failures/ipc13-frozen-baseline.log)：新store根不存在时错误拒绝、实际引用缺口负例、测试用版本fixture/命令开关/命名问题、参数字典引用比较误触不可变请求保护、提前关闭保留期查询，以及服务健康分离时旧健康null语义回归，均定位并修复。原请求不可变、完整恢复/所有权、CAS和有效断言没有删除。701ee0d本地候选因最后一项回归撤销、从未上传；[原候选身份](evidence/arch-extension-closure-13/withdrawn-701ee0d.json)保留，dc5b405使用全新路径。没有把旧失败重新标PASS。

实际WPF截图0；[Windows增量场景](validation/closure-13-windows-scenarios.md)及[页面映射](../ui/arch-extension-closure-13-mapping.md)承接原06完整支持矩阵。保护拒绝不算正常适配，未缩字号、未撤守卫、未缩支持范围。历史AC12的OPEN/NOT_RUN不因本轮工程通过而关闭，D/RV与05现场缺口继续原状态。软件暂停不是安全急停。
