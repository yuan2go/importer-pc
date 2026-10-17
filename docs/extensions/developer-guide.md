> 当前执行：WP-IPC-ARCH-EXTENSION-CLOSURE-13，基线 main `feed6e9c`。职责、装配、两项P2及类型化设备/集成接入正在实施；本段是执行范围，不代表目标已经完成。Mac核心验证与WPF交叉构建在本轮，Windows/W01～06、实际UI与现场统一延期，状态保持 `WINDOWS_NOT_RUN`；不寻找VM/runner，不扩OAuth。原当前指引见 [归档](../archive/pre-arch-extension-closure-13/README.md)。

# 受控扩展开发、安装与恢复

本指南对应EXTENSION-WINDOWS-DELIVERY-12 / ExtensionContract1。宿主源码和包SHA由12报告登记，插件源码身份独立登记。当前新增的后装能力为受信任托管观察来源；既有manual/replay和SIMULATION-v1继续走同一个Product来源/发送器、Supply契约与事务。厂商SDK、系统驱动、真实WCS和新业务不在本期支持声明内，未支持的模块kind/原生导入/元数据语义明确拒绝。

## 宿主与包的身份

宿主release.json和config/product.json固定核心字节，不因后装重seal。扩展.ipcx是ZIP，包含manifest.json原UTF-8字节、manifest.sig原RSA-PSS/SHA256签名、Files精确文件集合。算法使用.NET/OpenSSL标准实现，RSA至少3072位。manifest含包/发布者/源提交/版本、模块/入口、Runtime/RID/契约及冻结宿主公共契约SHA、依赖、参数schema、SDK或系统驱动前置证据和文件长度/SHA。包无权把自己加入信任。当前无在线吊销查询，界面/回执保存离线策略SHA、核验时间和TEST ONLY标记，不能宣称联网核验。

同一ID/版本不允许绑定另一份manifest；部署每个包只选一个精确版本，安装目录可并存多个版本。依赖必须精确到ID/版本/manifestSHA，部署校验整个闭包、版本冲突和环。签名manifest精确绑定HostContractSha256，不把同一契约主版本视为任意宿主二进制兼容；不同目标构建若公共契约字节不同，分别构建匹配包。托管加载共享宿主Ipc.Platform类型身份；私有程序集只从签名清单及声明的精确依赖上下文解析，不扫描DLL、不改PATH。ALC不是沙箱，可信发布者对进程内代码负责；Unload是协作请求，不能作为停止/释放证据。

SDK前置是vendor-sdk，Windows驱动前置是system-driver；它们不是插件入口。当前缺批准的前置证据解析能力时拒绝激活该包，原生/混合DLL及P/Invoke也拒绝。不执行安装脚本、任意XAML、表达式、MSI或设备控制来“检测”兼容。

## 独立构建与签名

先发布并冻结宿主，再仅引用其Ipc.Platform.dll构建examples/BarcodeExtension。示例不在宿主solution/project引用图中。可用下列本机命令生成**参考测试**签名材料；私钥始终在构建方私有目录，不进Git、客户端或公开制品：

```sh
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out /private/build-only/reference-signing.pem
openssl pkey -in /private/build-only/reference-signing.pem -pubout -out /private/build-only/reference-public.pem
```

独立可信通道提供策略JSON，SchemaVersion=1，Publishers每项为Id、PublicKeyPem、TestOnly。参考包要求TestOnly=true；Production策略中出现测试发布者就拒绝。不要从插件包中导入其自带证书作为信任。生产签名根和代码签名未取得，本候选没有生产签名资格。

```sh
python3 scripts/build-extension.py \
  --contract /frozen-host/app/Ipc.Platform.dll \
  --output /new-output/reference.barcode-1.0.0.ipcx \
  --key /private/build-only/reference-signing.pem \
  --publisher reference-test --trust /independent-policy/trust.Simulation.json
```

脚本使用锁定SDK/空外部依赖锁，不编译宿主；签名前后校验冻结宿主所有Ipc.*.dll，独立插件SourceCommit来自示例目录最后源码提交。签名后再以独立公钥策略验证。升级用新的--version及全新输出，不覆盖旧包；插件源码有变化应先提交该源码，身份文件保留其实际提交。

实现IObservationExtension.Create(ExtensionContext)，返回IControlledObservationSource。Create只构造资源所有者，不偷偷开始采集；StartControlledAsync接受宿主注册epoch，观察保持device/epoch/sourceEvent/sequence/admission身份。健康确认时间来自可靠来源，不从UI读取续期。Stop完成必须表示所有回调/后台任务已终止；不可将超时当作已停止。Dispose幂等，失败仍明确保留所有权。例子的ISyntheticObservationInput仅Simulation工程入口调用，输入真实进入原Observation队列、Supply/SQLite/Outbox和独立loopback；它不是模拟成功返回值。

## 参数schema与实例

这是显式有限schema，不冒称完整JSON Schema。每个字段Key是稳定的参数路径，点号表达嵌套路径；Group以“/”表达分组层级。支持string、integer（InvariantCulture整数/上下界）、boolean、choice、string-list（有界集合）、secret-reference。支持默认、必填、条件必填（RequiredWhenKey/Equals）、只读、说明、单位、长度/集合上限、维护编辑权限及Restart生效级别。当前所有影响来源的参数都要求重启；未知类型、权限、生效级别或未支持的语义拒绝，不静默忽略。schema只生成标准WPF控件，没有脚本/XAML执行。

string-list在编辑器每行一项，存储为有界字符串数组的JSON值；每项也检查长度和控制字符。只读字段必须等于签名默认值。敏感字段只能为credential:名称，持久配置/差异/日志只保存引用，不能输入明文秘密；参考来源不读取生产凭据。真实凭据读取器需与真实身份/SDK集成另行批准，不能靠本例宣称已支持。

包不等于实例。一个模块可有多个实例，每个实例独立参数及稳定DeviceId；DeviceBinding.Driver=extension:实例ID，Role仍由Supply拥有left/right。相同设备资源同时持有模式无关的全局设备租约，不能仅改实例名或模式逃避独占。当前观察实例必须显式绑定一个业务角色，重复角色/设备、未启用实例、缺失包/能力/依赖和实例配额超限均拒绝。

## Windows安装与普通账户运行

从12报告的授权远端下载并核验ZIP SHA和SHA256SUMS。候选未通过Windows验收，首次在隔离参考机使用合成数据，不能用于现场放行。使用Windows11 24H2/build26100+ x64和PowerShell7；日常使用指定普通账户。提升的维护终端执行首次部署（替换实际账户与路径）：

```powershell
$release = 'C:/Program Files/ImporterPc/releases/delivery-12-reference'
./protect-release.ps1 -Release $release -RuntimeAccount 'MACHINE\station-user'
./provision-runtime.ps1 -RuntimeAccount 'MACHINE\station-user'
./provision-extensions.ps1 -Mode Simulation -TrustFile 'D:/independent-policy/trust.Simulation.json'
```

账户名实际采用Windows可解析的MACHINE\station-user格式。安装目录和Program Files/ImporterPc下父目录必须由维护主体拥有，普通账户只读。辅助程序和UI双方检查此边界；不能从用户可写Downloads或临时目录运行提权辅助程序。ProgramData/ImporterPc下的extensions/Simulation与extensions/Production和两份离线信任策略独立；只有SYSTEM/Administrators可写扩展库，普通运行账户只读。机级leases保留既有指定账户写锁权限，并检查ACL/重解析点。新政策不会自动信任包里的公钥。

再回普通账户启动fixture及station.ps1 Init/Run。PAGE-08“组件与部署”或登录/启动页的“组件管理与恢复”可查看包、信任、依赖和操作。选择.ipcx先取得静态计划，再确认精确包/版本/SHA。UI用固定同版Ipc.Product.exe + 参数列表调用OS runas；辅助程序重新验证独立策略、文件和SHA，不加载插件或执行任意命令。UAC取消/权限拒绝保持失败状态，模拟维护身份不能绕过OS。参考托管安装通常只需该受限辅助步骤；没有安装厂商SDK或Windows驱动。

命令行使用同一管理实现，extension-inspect只读，extension-install/recover/uninstall均按OS授权：

```powershell
./app/Ipc.Product.exe --action extension-inspect --mode Simulation --file 'D:/offline/reference.barcode-1.0.0.ipcx'
./app/Ipc.Product.exe --action extension-install --mode Simulation --file 'D:/offline/reference.barcode-1.0.0.ipcx' --sha256 '计划中的64位SHA'
```

没有业务身份或业务未启动时，核心包读取、安装计划和OS授权的安装/恢复仍可达；参数保存/激活需要应用内有效维护身份。普通操作者的UI提示不是提权证明，Windows普通账户、UAC取消/ACL/跨会话结果须看06及12实际证据。

## 经UI形成唯一部署修订

1. 在包列表选择已安装精确版本，加入部署草稿（自动解析精确依赖闭包）。安装状态只是Installed。
2. 在实例页选模块，填写唯一实例ID、稳定设备ID及left/right，编辑schema生成的完整参数，写入本窗草稿。两个实例独立，右侧显示当前有效值，敏感项仅显示引用已设置。
3. 统一部署页填写新的不可复用版本，预览差异后保存；站点参数来自同一维护表单。包集合、实例、参数和DeviceBinding全都进入原StationConfig/ConfigRevision，而非第二个可生效配置账。
4. 后台校验结构/schema、实例/跨绑定/资源、产品能力，保存Validated或Rejected及原因。CAS竞争返回Conflict；过期身份/迟到读取不能覆盖新身份结果。保存不代表激活。
5. 关闭活动箱、核对Unknown与实体责任后，选择Validated修订申请激活。后台再查权限、原版本、原请求、隔离、占位、依赖和签名，标PendingActivation并阻断。受控退出/重启；来源Stop或Dispose未终止时继续保留全部依赖/租约，不能启动第二所有者。
6. 重启在数据租约下选择最终配置，再取得最终设备/包租约，重验包、建立来源/代次和发送器。DeploymentPhase审计记录部署指纹/验证/资源/模块阶段；只有组合初始化成功，原事务才将新修订Effective、旧修订Superseded。业务就绪仍由真实健康/身份/实体/Unknown守卫决定。

界面草稿每窗独立。只开抽屉或第二窗不会复制业务实例。新箱焦点沿用ticket；窗口区域不足由应用拥有每窗保护标记，首次Loaded前测得不足也会提交，且不依赖已登录。另一窗口不能Resume仍在不足区的窗口。扩大/关闭窗口只解除对应区域条件，持久人工暂停必须显式恢复；其它原因保留。

## 升级、失败与卸载

新版本先安装另一目录，再以新部署修订选择；旧包/旧Effective/原业务账保留。启动失败记录ActivationFailed，核心管理可见；完全停止后下一受控启动使用旧Effective。不能在未终止进程里并行启动旧来源。切回旧包仍要通过当前schema、契约、包/配置及资源校验；不支持的配置迁移明确拒绝。对本期托管观察扩展，回退不回滚station.db，不修改原请求字节/目标/合同，不补发Unknown。

安装日志阶段Prepared→FilesVerified→Published→Registered绑定原操作ID和包SHA。incoming受控原包、staging、版本目录和登记分开；实际进程中断后以原ID恢复。部分暂存保留为interrupted，不先执行代码。损坏的同身份包可用原签名包修复：取得独占包租约后将坏字节保留到damaged，再静态重装；仍在使用时拒绝。不要换ID盲重装或手删日志/租约。

先在新部署草稿停用/移除实例并解除绑定、移出包，受控重启生效。卸载检查反向包依赖、Effective/Pending和原未决请求需求；原请求未决时保守保留所有相关历史配置包引用，查询不受UI分页100条限制。实际使用/未终止插件的包租约也阻止卸载。包使用锁在安装方拥有、运行账户只读的usage目录，多个工位可共享不可变包；卸载/修复取独占读句柄，不在提升进程中写入运行账户可替换的租约文件。配置保存/激活持有包读租约直至原事务提交；卸载先取得独占包租约，再重新读取DB引用，防止以旧快照漏掉并发Pending。成功将文件原子移至retired，保留登记、操作日志和所有业务事实；UninstallRetired阶段中断可用同一原操作恢复。需要后续清理容量时必须另行审查保留政策，不在本包自动删审计。

历史Storage SQL/checksum1/2/3未改。带Extensions字段的新部署需要12兼容宿主，不能因Storage仍3就让旧EXE直接解释新配置；旧严格DTO会拒绝新字段。核心二进制回退仍遵守既有备份+匹配发布+新根+恢复隔离流程，不用业务数据库回滚隐藏外部接受事实。

## 验证与交付边界

`--extensions`实际独立构建来源、验证签名/路径/RID/配额/依赖/篡改、多个中断点、真实进程终止后原ID恢复、实例参数隔离、配置竞争、Unknown原请求核对、升级成功/启动失败保旧、停用卸载、真实延迟Stop及构造失败时的所有权；不代替WPF。

两个构建组合由config/compositions.json锁定：core-reference不带可选插件；barcode-reference额外独立构建并携带签名离线插件及单独TEST ONLY公钥策略（仍须OS显式配置，不自动信任/激活）。composition.lock.json记录依赖闭包、核心字节和可选包SHA；SHA256SUMS覆盖精确产物集合。

Windows可执行入口是随包windows-reference-validation.ps1，记录包回读、环境、实际WPF进程/UIAutomation与屏幕截图。它的runner管理入口冒烟不等于06六组验收；Windows Server/admin会话不算Windows11/普通账户资格。每格分辨率/缩放、跨屏、多窗、扫码器、触控、听审、持续旅程及维护/故障按06独立保留证据；缺项继续OPEN/NOT_RUN。远端同版候选和下载回读SHA见12报告，不从工作流文件存在推定运行PASS。
