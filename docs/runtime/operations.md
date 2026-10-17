> 当前执行：WP-IPC-ARCH-EXTENSION-CLOSURE-13，基线 main `feed6e9c`。职责、装配、两项P2及类型化设备/集成接入正在实施；本段是执行范围，不代表目标已经完成。Mac核心验证与WPF交叉构建在本轮，Windows/W01～06、实际UI与现场统一延期，状态保持 `WINDOWS_NOT_RUN`；不寻找VM/runner，不扩OAuth。原当前指引见 [归档](../archive/pre-arch-extension-closure-13/README.md)。

## CLOSURE-13 当前运行与候选

当前[13报告](arch-extension-closure-13.md)/[checks](arch-extension-closure-13-checks.json)登记实际源码/包/hash和远端回读；[扩展指南](../extensions/developer-guide.md)包含观察设备与类型化服务实例、公共契约、签名依赖、安装/参数/生效/原请求/卸载。原12报告、原候选和形成时验收不改写。Windows由用户决定延期：本轮只做Mac核心验证与WPF交叉构建，WINDOWS_NOT_RUN；不寻找或开通VM/runner，不扩OAuth。

```sh
# 固定SDK 10.0.402；所有输出使用新目录，先提交实际输入
python3 scripts/package-reference.py --target portable --composition integration-reference --extension-key /private/build/reference.pem --extension-trust /independent/trust.json --output /new/closure13-supply-portable
python3 scripts/package-reference.py --target win-x64 --composition integration-reference --extension-key /private/build/reference.pem --extension-trust /independent/trust.json --output /new/closure13-supply-win-x64
python3 scripts/package-business.py --composition inspection-reference --output /new/closure13-inspection
python3 scripts/package-business.py --composition supply-assembly --output /new/closure13-supply-assembly
```

通用装配使用`dotnet app/Ipc.Assembly.dll --config app/config --data-root /reference/data --view inspection-history --layout compact`（inspection组合）或供包所属页面（supply-assembly组合）。每行snapshot或BusinessCommand JSON；quit受控停止。Inspection只有明确合成模式，持久BeginBatch/RecordSample/CloseBatch、命令去重/CAS、重启查询；不是客户新业务。Supply装配转交同一个ProductRuntime，原Ipc.Product/WPF入口继续可用。未知业务/所属页面/布局拒绝。

新服务实例在Services/Ports中，无DeviceId；供包requests端口目标与原Integration必须一致。管理页、CLI配置命令共用SaveConfig/ActivateConfig。原请求冻结集成实现/参数/目标，改当前包或地址不会改写历史路由。发送停止后，在其它资源仍未终止时允许原身份查询；最终释放须排空整个查询/T3/审计再Dispose。未终止继续持有实际包/设备/站点/库租约。

必要验证选择器：--architecture（13新增风险及两个真实业务组合）、--extensions（12后装与部署旅程）、--baseline（T1/T2/T3/进程恢复）、--followup（配置/权限/双箱旅程）、--review-followup（展示及Product所有权）；只按受影响范围执行，原断言和失败证据保留。smoke.py/smoke-extension.py必须对实际解压的新候选运行，两种插件都在冻结发布宿主后独立构建，宿主前后hash不变。

### 继承的运维细节

以下保留前版本的具体存储/恢复/Windows安装政策；旧版本号、旧输出路径和历史测试结果只作原版本参考，当前候选身份以上方13报告为准。

# 运行、发布与运维

上一版记录：[REVIEW-FOLLOWUP-11](../work-packages/WP-IPC-REVIEW-FOLLOWUP-11.md)，以[11实施与同版报告](review-followup-11.md)为准；[09上一版检查](reference-validation-09.md)及[10逐页映射](ui-baseline-alignment-10.md)保留各自源提交边界。用户已授权合成/参考测试、构建、启动和发布；历史不测试/NOT_RUN见原报告和[纠偏前快照](../archive/pre-reconciliation-01/README.md)。Windows06与真实能力05继续分别记录。

08启动顺序：取得数据根租约→读取最终Effective与待激活修订→核验站点/模式/产品能力→取得最终站点和设备集合租约→创建应用、来源与发送器。seed设备可与Effective不同，实际锁覆盖Effective；不锁seed-B却启动A。非法待激活保留ActivationFailed并使用旧Effective；启源失败记录激活失败，完全停止后下一次受控启动回到旧Effective，不在失败进程里并行启第二套来源。首次成功启动也固定seed为Effective。历史查询全部要求有效模拟身份；无权时Snapshot不查询跨箱最近历史，返回空RecentOperations。当前工位聚合、当前箱及原未决请求/恢复诊断仍可发现；其可见性不授权历史/尝试/回执详情读取。 配置Version是不可复用的修订身份（被拒绝/失败版本也保留，纠正时用新Version）；推广新Effective与旧版Superseded在同一事务。遗留多Effective根据持久ConfigActivation顺序读取，不按草稿插入rowid猜生效顺序，激活审计失败整体回滚保旧。

当前StorageVersion=3、minimum_reader=3；schema1/id2文本保留，追加id3。preflight/backup使用不迁移源库的独立路径；旧format1/2默认启动拒绝，须按07记录显式备份升级。普通Run与升级/回滚均显式选择Release，不使用active-release指针。备份/升级回执和实际新包的操作步骤见07记录；不要从旧发布目录使用旧升级脚本处理新格式。

Windows首次安装需在提升的维护会话执行新包的 `pwsh ./provision-runtime.ps1 -RuntimeAccount 'MACHINE\station-user'`，指定实际运行账户；之后以该账户运行。程序检查ProgramData下维护方拥有的runtime-policy.json、机级leases目录及ACL；缺策略、账户不符或权限不符即拒绝启动业务，不能回退用户级租约。安装脚本的实际Windows执行为NOT_RUN；固定账户/ACL和跨会话行为仍待Windows验证。

本实现是 F-02 独立参考产品。所有设备、身份、数量策略和外部端点为 **SIMULATION**。核心、SQLite 与恢复为真实代码；没有旧现场工程迁移、厂商驱动或真实 WCS。D/RV 的客户与现场状态不改变。

## DELIVERY-12 当前运行与交付

执行原工作包[EXTENSION-WINDOWS-DELIVERY-12](../work-packages/WP-IPC-EXTENSION-WINDOWS-DELIVERY-12.md)，设计增量见[controlled-extensions](../design/controlled-extensions.md)，安装/参数/升级/恢复完整步骤见[扩展指南](../extensions/developer-guide.md)。同版身份、实际结果与AC由[12报告](extension-windows-delivery-12.md)统一记录，旧11/09包及PASS不冒称新包结果。

当前ReleaseVersion=EXTENSION-WINDOWS-DELIVERY-12、Storage/MinimumReader=3/3、SIMULATION-v1、ExtensionContract=1。核心原SQL/checksum不变。包集合/实例参数/设备角色组成唯一StationConfig部署；安装不是激活，默认受控重启。带Extensions字段的配置需要12兼容宿主；旧EXE不能凭Storage同号直接读取新配置。回退包不回滚业务数据库，Unknown仍核对原请求。

普通账户运行，首次提升维护会话保护Program Files/ImporterPc发布目录及父目录、安装指定账户机级策略、经独立离线策略配置Simulation扩展信任；之后组件管理只调用固定受限辅助程序进行OS授权写入。生产信任不包含测试发布者，当前Production缺真实能力继续拒绝。签名/信任/兼容/路径/大小/哈希静态校验早于加载；SDK与Windows驱动前置独立，当前没有真实SDK或系统驱动安装。

开发/参考验证：

```sh
./scripts/build.sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --extensions
# 配置/来源/生命周期改变时的相关继承范围：
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --baseline
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --followup
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --review-followup
```

两种构建组合，输出全新目录、签名私钥不随包：

```sh
python3 scripts/package-reference.py --target portable --composition core-reference --output .artifacts/delivery12-core-portable
python3 scripts/package-reference.py --target win-x64 --composition barcode-reference --output .artifacts/delivery12-barcode-win-x64 --extension-key /private/build-only/reference.pem --extension-trust /independent-policy/trust.Simulation.json
```

core-reference不带可选扩展字节；barcode-reference在宿主冻结后独立构建并携带离线签名来源包，不自动信任或激活。composition.lock.json锁定依赖、版本和实际核心/扩展SHA。安装并激活后才是运行组合；独立插件有自己的源码提交。当前包是未验候选，实际Windows11六格矩阵、普通账户/扫码/触控/听审/持续旅程按06逐项取证。仓库Windows runner脚本只提供实际WPF启动/管理入口冒烟，Server/admin会话不能替代完整06。

## 环境与构建

SDK 锁定 `global.json` 的 .NET 10.0.402。NuGet 来源仅 nuget.org；Microsoft.Extensions.Hosting / Microsoft.Data.Sqlite 10.0.13，SQLitePCLRaw.bundle_e_sqlite3 2.1.14，实际传递依赖及内容哈希见各项目的 `packages.lock.json`。首次还原发现默认 2.1.11 原生 SQLite 高危公告，已升级依赖，未屏蔽审计。参考部署基线为 Windows 11 24H2 / build 26100+ x64、WPF，运维脚本检查此基线；这是新参考产品的工程约束，具体现场 OS、DPI、驱动及触控尚未验收。

在仓库根目录，将 SDK 加入 PATH 后：

```sh
./scripts/build.sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --review-followup
```

核心和夹具可在 macOS/Linux 运行；WPF 只能在 Windows 运行。交叉编译成功不证明桌面运行。构建采用单节点 MSBuild；实际本次结果见09报告。

## 开始模拟

先启动独立接收端，保持该进程：

```sh
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /tmp/ipc-simulation-receiver
```

接收端只绑定字面量 `127.0.0.1`，自己的 `receiver.db` 不得与客户端共享；只使用合成数据。`/requests`、`/requests/{id}`、`/statistics` 是本工程协议，绝非真实 WCS 接口。初始需求由合成上下文固定，提交时在接收端事务内原子裁决，两通道不能各自扣减伪造一致。请求ID相同而报文哈希不同返回409。

Windows 启动桌面：

```powershell
dotnet src/Ipc.Desktop/bin/Debug/net10.0-windows/Ipc.Desktop.dll --config src/Ipc.Desktop/bin/Debug/net10.0-windows/config --data-root "$env:LOCALAPPDATA/ImporterPc/data"
```

工程控制页选择模拟角色→工作台输入箱码并校验→某通道“实体入场”→输入条码/包装数量并读码→等待校验→确认供包→业务接受后提供显式模拟“实体离场”“实体复位”→继续同一箱下一件。两通道可重复同码，实体身份不同；同来源观察身份重复不新建请求。不通过定时清屏进入下一件。

超量使用 `SYNTHETIC-KEEP-v1`：扫描**保留件**，单位为合成件，9包装/6需求时保留6、移除3；不同 retained-unit 身份计数，重复身份不增加。该策略、30分钟模拟实体许可、双逻辑位/共享箱、120初始需求和固定角色均为工程选择，不是生产默认。过期或跨进程代次的实体许可须由维护角色提供“核对实体许可”事件；不改变原请求业务内容。生产禁止人工改码/强制成功/自动发车。

相同条码前导零保留；无码/多码/非法输入保留观察与拒绝原因，可在同一实体修正读码。待提交草稿可显式处置；Unknown/在途/已接受效果不能靠取消清除。结束检查要求实体位已复位、无待决请求，建立双通道屏障；不足时可以取消或确认当前检查。只有原关闭回执接受才关闭箱，CloseUnknown 保留箱身份与专属请求。

工作台保留并排双通道，各自显示当前条码、任务、包装/需求/供给及拆包计数，固定动作绑定本通道owner/revision。当前Item波次不藏在辅助展开；与入场箱波次不一致时，固定结果区同时显示两种原值及差异，不据此编造新的生产接受规则。来源/历史证实可辅助展开；关键核对控件或差异摘要未完整可见时禁止提交/计数，不能通过整体缩字绕过。客户区按1280×680 DIP保护，窗口恢复空间后仍需人工核对并恢复入场；实际可用最小区与DPI尚待Windows验证。

初次有效身份、旧箱明确关闭/拒绝后进入可扫箱状态时，本窗发起一次专用箱码焦点交接并选择既有草稿，下一扫码替换旧输入；300ms投影刷新不重复夺焦点。抽屉、身份入口、过期投影、身份失效、正在执行及区域不足时延后或撤销，执行排队回调时再次检查身份与箱。每窗的草稿、筛选、选区和滚动独立。布局切换只改展示，所有页面和窗口共享唯一业务实例/发送器；低频抽屉背景业务不可操作，全局诊断继续可见。风险确认初始焦点在取消、Tab在窗内循环、关闭归还焦点；Enter不触发高风险动作。Windows实际焦点/扫码/DPI/双窗和提示声听审仍NOT_RUN。

## 设备替换与配置

打包输入 `config/product.reference.json` 选择已实现业务和允许模块；`ReferenceProduct` 的编译注册是执行权威，修改JSON不创造未安装业务或驱动。Platform只检查通用结构/依赖/版本/模式/路径/完整性，双通道和模拟数量规则由Supply拥有。产品清单由显式允许名单、契约主版本、依赖、架构、文件哈希校验；不扫描任意DLL。安装目录须可信且受ACL保护，自带哈希不证明发布者身份。每工位/设备和数据库路径分别持有跨进程文件租约；不删除租约文件。此本机独占不替代真实设备端协议。

三层文件：`product.json` 为装配；`station.json` 为绑定、容量和外部目标；`presentation.json` 为页面/布局。未知字段拒绝。两个读码实现均可绑定任意逻辑通道：`manual` 人工事件源，`replay` 文件源；回放绑定设置 `Driver="replay"`、`ReplayFile="replay.jsonl"`，需维护草稿验证并受控重启。回放每次按钮推进一条，不自动跨过业务提交/物理确认。旧 sourceEventId/admissionId 不能用于新实体。

维护页提供驱动/端点/站点参数表单、逐字段差异/校验/生效时机和失败反馈，JSON仅高级诊断。站点ID/模式固定；后台重复核验权限与状态。维护草稿经历 Validated/Rejected→PendingActivation→重启 Effective/ActivationFailed，无有效规则的草稿保留原因。失败保留旧有效配置。活动箱/未决请求阻止激活；原请求始终保留旧报文、合同、绑定和端点版本，不随配置或换人重投。显示布局不重启业务。初次源码启动先由 seal 工具生成实际程序集哈希；二进制变化后重新执行 build/seal。

Production 模板不注册任何成功夹具或模拟身份/源，不访问生产设备；必需 SDK、合同、策略、身份未取得时，启动诊断并返回 NotReady。它是防伪检查模板，不是生产可用产品。

## 核对与故障

设备或WCS断开、身份失效、许可、人工暂停、配置、过载、存储和Unknown分别阻断。恢复只移除 OperatorPaused。发送前再次核对当前实体许可/代次/作业版本和原绑定；网络等待不占数据库事务或协调锁。

诊断页可为下一个请求选择 Drop、Delay、Reject、QueryMissing、QueryUnsupported。SIMULATION 接收账先提交再丢回执/延迟。Unknown 不自动重发；在“报警与关闭核对”选择原请求，由维护员查询其固定端点。查不到/不支持查询不证明未处理，继续Unknown。人工核对可追加来源和责任证据，**不能无权威回执强制标记成功**。没有查询能力的真实合同仍需批准的人工SOP，不能在参考程序里编造。

T1原子保存事实/意图/Outbox；T2记录领取与独立Attempt；T3保存响应、业务状态、唯一接受账和审计。Dispatching崩溃恢复Unknown；Prepared须重检许可；接到回执但T3存储失败阻断全站，不能把临时收到的响应当可靠入账。旧响应只更新原记录，矛盾响应保存并阻断；多个Attempt不重复计件。

队列容量可配，发送器仅一个在途网络调用，Outbox有待决数量上限。回调队列满明确返回未接纳、记录计数和独立过载标记；已入队事件排空或故障可诊断。**未被接纳的SDK观察没有逐条持久性保证**，须核对受影响实体。清除过载要求维护依据，不通过普通Resume清除。原生SDK未接入，不声称其资源回调通过。

## HARDENING-04 恢复与请求处置

H04原加固范围与结果见 [HARDENING-04](hardening-04.md)，与RUNTIME-03报告一起保留形成时证据；当前07新增的来源版本、资源清理和维护流程以上方07记录为准。

过载首先关闭源的新输入；在过载之前已接纳的观察继续按业务规则处理和保存，其它许可仍有效。未接纳的观察只保留来源/累计计数等诊断，不承诺逐条持久化。过载标记在实际拒收时同步写入，不再由累计计数定时重写。排空后维护员检查来源与受影响实体；零pending且无未解决运行故障时才能确认过载。UI显示排空/请求/权限禁止原因。确认绑定当前 `OverloadVersion`，新拒收导致版本变化时拒绝旧确认；确认依据和最新来源原因写入SQLite审计后才删除标记。退出不会重建已确认标记；新一轮过载写入新标记，重启重建阻断。普通Resume仍只解除人工暂停。

请求列表分别显示原状态、发送阻断、本地终结禁止原因和下一步。`Prepared`在暂停、过载、原身份过期等情况下可能无法发送。维护员可选择原请求，输入检查依据，执行 `CancelPrepared`。允许条件：仍为Prepared、没有响应，且没有发送尝试，或每次尝试都有已结束的 `NotDelivered` 持久证明。应用在同一协调器内核验/保存状态、Outbox和审计；取消与领取/交付竞争不会复活已终结请求。CLI示例（先snapshot取得当前revision）：

```json
{"Action":"command","Command":{"Id":"unique-local-command","ExpectedRevision":42,"Kind":"CancelPrepared","OwnerId":"original-request-id","Value":"SIMULATION source inspected; local non-delivery evidence reviewed"}}
```

本地终结状态为 `CancelledBeforeDispatch`，原ID、报文、哈希、合同、目标版本和旧操作者均不变。作业或会话进入 `AwaitingDisposition`，另存 `LocalDispositionRequired` 阻断。**此前校验可能产生的预留、实体占位和原接受事实均保留。** 当前版本不提供解除该处置阻断、释放预留、重新授权旧请求或准入新实体的命令；这些需要后续INTEGRATION-BOUNDARY-05取得合同/实体证据。本地终结没有接收端回执，不会新增接受量。异常迟到回执保留为矛盾证据并进入Unknown核对。

T2领取为Dispatching；发送前的最终守卫与过载锁串行裁决，并先保存 `TransportHandoffAuthorized` 审计和 `TransportHandoff` 尝试状态。此后即使实际网络调用尚未开始，也保守视为不能证明未交付。跨进程中断按Unknown核对；不能本地取消或盲重发。发送前守卫失败时记录NotDelivered并回到原Prepared，可恢复对应许可后以同ID再尝试，或受控本地终结。Unknown只能查询原绑定；新登录不重新授权原身份已过期的报文。

活动报警集合与计数只查active=1，不受历史LIMIT影响；已恢复历史独立分页。按原报警ID已读，已读不减少活动计数；恢复匹配对象、来源和代次，新的发生不继承旧已读。请求报警可打开原请求详情及本地终结审计。

没有更改schema1 SQL或迁移校验和，也没有改写旧请求。新版读取原SIMULATION-v1报文/响应；H04兼容样本来自当时未修改main可执行文件，全部为合成数据；07没有重跑这些样本。新增JSON状态需要新版解释，**不得用旧EXE直接读取已产生AwaitingDisposition的新库**。升级前保留一致备份；回退旧版只使用升级前兼容备份、新数据根和恢复隔离，重新核对外部账及实体，不把schema号相同解释为任意版本可降级。

## 离线发布与部署

已提交独立 `packages.win-x64.lock.json`，使RID还原不改写便携锁。联网构建机执行（PowerShell 7与Python3）：

```powershell
./scripts/publish.ps1 -Mode Simulation -Output .artifacts/release-simulation -ProductDefinition config/product.reference.json
# 防伪模板另生成，使用全新目录：
./scripts/publish.ps1 -Mode Production -Output .artifacts/release-production
```

生成自包含 win-x64 `app`、Simulation专用 `fixture`、配置/锁/运维脚本、SHA256SUMS和ZIP。离线机器运行不需要 SDK 或联网还原；构建机仍需取得锁定NuGet包。安装目录只读，默认数据放 `%LOCALAPPDATA%/ImporterPc/data/{Mode}/{StationId}`。Windows首版只支持维护策略指定的一个运行账户跨登录会话；机级租约固定在 `%ProgramData%/ImporterPc/leases`，不能通过用户偏好或自选根实现第二个账户运行。首次安装策略后才执行下列命令；安装目录可信分发及数据访问权限另由维护方管理，不能随意放网络盘。Production包不带夹具可执行文件。

在解压目录运行：

```powershell
./station.ps1 -Action Fixture
# 另一窗口
./station.ps1 -Action Init
./station.ps1 -Action Run -View workstation -Layout standard
./station.ps1 -Action Export -File 'D:/IpcDiagnostics/diag-new.json'
```

哈希核验不代替可信分发/签名和ACL。真实SDK/字体未随包分发。Windows启动、DPI、触控、物理反馈、吞吐及现场人员验收分项待验。

## 备份、升级、回滚与保留

备份通过 SQLite online backup API，不复制活动主库冒充一致备份。[Microsoft.Data.Sqlite备份依据](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup)。PRAGMA：WAL、foreign_keys、synchronous=FULL、busy_timeout=5000ms、wal_autocheckpoint=1000；schema_history记录迁移校验和。损坏库拒绝启动，不创建替代空库。本地诊断日志为有界队列和5MB×4轮转的元数据日志，业务证据以SQLite为准；数据/日志/导出需要运维容量预算；低水位启动检查不代表持续磁盘容量保证。

退出桌面后（离线运维与桌面竞争同一数据租约）：

```powershell
./station.ps1 -Action Backup -Release 'D:/Ipc/releases/next' -File 'D:/IpcBackups/station-new.db'
./station.ps1 -Action Preflight -Release 'D:/Ipc/releases/next'
./station.ps1 -Action Upgrade -Release 'D:/Ipc/releases/previous' -NextRelease 'D:/Ipc/releases/next' -File 'D:/IpcBackups/before-upgrade.db' -Receipt 'D:/IpcBackups/before-upgrade.upgrade.json'
# 指定选中的发布目录运行，不覆盖旧文件：
./station.ps1 -Action Run -Release 'D:/Ipc/releases/next'
# 必须用新的数据根，原库不删除：
./station.ps1 -Action Rollback -NextRelease 'D:/Ipc/releases/previous' -DataRoot 'D:/Ipc/recovery-new' -File 'D:/IpcBackups/before-upgrade.db' -Receipt 'D:/IpcBackups/before-upgrade.upgrade.json'
```

必须使用07新脚本处理旧库，并保持同一DataRoot。升级要求无活动箱/未决请求/占位/恢复隔离，校验两套发布文件后先制作原格式备份和绑定旧/新发布及配置指纹的回执，最后显式迁移；不写发布指针，不自动启动。当前格式3（schema_history id1+2+3，原schema1/id2 SQL不变）；普通启动拒绝隐式升级既有1/2，未知格式/迁移拒绝，不声称任意旧EXE兼容新库。失败保留原文件、备份和回执，先核对实际格式及错误，不把失败等同已回滚。回滚只接受与回执匹配的原格式备份和旧发布/配置；恢复创建新库并持久设置恢复隔离；备份中的Prepared亦转Unknown，可能已被外部处理，不能自动补发。先比对原外部接收账和实体库存，未决全部有权威结果后维护员带依据解除隔离。真实上游无查询/人工SOP时继续阻塞，不能凭“恢复成功”放货。

历史使用类型化查询精确匹配箱/Item/SKU、会话/作业/请求状态、通道和含时区日期范围；可查空箱会话与跨箱关闭链。翻页沿用提交条件与初页父记录上界，既有事实/保留清理仍实时变化；Loading、空、失败/旧结果分别呈现，详情按选择代次隔离迟到结果。模拟保留清理仅维护接口 `PruneHistoryAsync(days,evidence)` 或控制台 `prune-history`，至少保留1天，只删除过期已关闭且无未决的完整会话、保留当前会话、请求引用配置和清理墓碑审计；不提供生产默认保留天数。诊断导出仅元数据和所选对象请求身份/哈希/尝试，不含条码、原报文、端点、角色备注和私密路径。DB/备份本身不脱敏，必须由本机访问控制保护，禁止公开Git。

## 可选控制台

桌面之外可用同一装配验证核心，不能用它替代桌面验收：

```sh
dotnet src/Ipc.Product/bin/Debug/net10.0/Ipc.Product.dll --config src/Ipc.Product/bin/Debug/net10.0/config --data-root /tmp/ipc-client-data --action serve
```

每行一个JSON：`{"Action":"command","Command":{"Id":"唯一命令ID","ExpectedRevision":当前快照版本,"Kind":"Login","Value":"maintenance"}}`。先输入 `snapshot` 取得当前版本。命令/回执由 `Ipc.Supply/Model.cs` 定义；`emit` 输入 Lane/Kind/Codes/Quantity，入场身份由装配生成；`replay` 推进文件；`reconcile` 输入原 RequestId；旧`review-observations`自由文本入口现在拒绝变更；来源核对改用`ReviewObservations`命令，Value为Sources（Code/Scope/Source/SourceEpoch/EvidenceVersion）与Evidence的JSON，绑定原Command.Id/ExpectedRevision。`clear-overload`继续输入Evidence并绑定当前过载版本。`quit` 受控停止。核心离线 `--action backup|preflight|restore|export` 与Windows脚本同一实现。

本机发布/维护冒烟可执行：

```sh
python3 scripts/smoke.py --product .artifacts/native-smoke-final --fixture src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --output .artifacts/smoke-results.json
```

该脚本在新临时目录使用实际进程、已发布核心、SQLite与loopback端点；检查第二进程、备份/预检查/恢复隔离、导出、不可写存储、损坏模块与Production防伪。它不执行Windows EXE，不代替WPF运行验收。

## ALIGNMENT-01 数据、后台与版本

箱波次和Item波次来自独立合成接收夹具的不同语义快照，SKU/任务ID/格口/单位/来源/版本/证据时间跟随持久回执；旧字段缺失显示“未取得”。原SIMULATION-v1请求字节和身份不改写。本箱接受量仅从唯一接受账投影，页面不累加计数。

应用读写由StationDispatcher的单个后台消费者拥有；UI读取不写报警，状态变化/维护脉冲更新报警。投影有独立版本与生成时间，超过3秒/读取失败阻断UI输入，后台命令仍查当前状态。设备健康回调只入队；原观察拒收marker保留H04同步持久性，其文件I/O受磁盘时延影响，真实SDK需单独验证。所有实际SQLite和网络工作均不在UI/设备回调执行。

ALIGNMENT-01原版本为Storage2；11形成时元数据为REVIEW-FOLLOWUP-11 / Storage3 / SIMULATION-v1，旧EXE因未知id3拒绝当前库，回退必须原格式备份+匹配旧EXE/配置+新根+恢复隔离。桌面内全库备份需要有效维护身份；离线运维使用数据租约和本机账户权限。

`--alignment`保留H04+A01的兼容范围；07/08及持续参考旅程使用明确的新选择器。旧迁移断言已按显式升级及3:3对齐，原SQL/checksum不变。上一版分项运行结果见09，当前11结果见11报告；Windows按06绑定当前同版包，不能复用07或旧包截图。

## 同版包与验证选择

`--reconciliation`（也为默认） =H04+A01+07+08/持续参考旅程+11展示/组合所有权+原AC/进程中断基线；`--hardening`仅H04，`--alignment`=H04+A01，`--data`仅A01，`--baseline`仅原AC/进程，`--closure`仅07，`--followup`仅08及双箱旅程。按本包风险运行必要基线和定向选择器；已有适用基线时不循环运行无关全量。

```sh
# 先提交所有运行源码、配置及打包脚本；输出目录必须全新
python3 scripts/package-reference.py --target portable --output .artifacts/review-11-portable
python3 scripts/package-reference.py --target win-x64 --output .artifacts/review-11-win-x64
python3 scripts/smoke.py --product .artifacts/review-11-portable/app --fixture .artifacts/review-11-portable/fixture/Ipc.Fixture.dll --output .artifacts/review-11-smoke.json
```

portable依赖已安装.NET10运行时；win-x64自包含包需实际Windows执行。两个包的SourceCommit/SourceCodeSha256及Release/Storage/Contract必须一致；模块版本/合同来自同一装配定义，各目标的二进制与配置指纹分别记录，RID导致的字节差异不冒称同SHA。`SHA256SUMS`核对每个包文件，ZIP SHA在11报告记录。包SHA识别字节，不是发布者签名或ACL证明。Windows上的`publish.ps1`继续可用；本次Python封装用于macOS交叉发布，不替代PowerShell行为验收。业务守卫和Production拒绝规则不因包生成而放宽。

## REVIEW-FOLLOWUP-11 定向验证与同版交付

新增实际选择器`--review-followup`执行桌面实际共用的纯展示策略（源码链接编入现有Verifier）、波次XAML结构约束及Product组合所有权三类故障；这不是WPF运行。`--closure`保留E07来源/健康/提示/清理回归，`--followup`覆盖E08权限/配置/双箱Unknown旅程，不重复未改的迁移及全部AC基线。完整`--reconciliation`也纳入新增E11；未知/多个选择器仍拒绝。

Product测试只通过验证友元可用的内部来源工厂进入；公开构造、Host/CLI和配置仍用注册工厂，Production先做原能力校验。Stop抛错、Stop任务未终止、Dispose任务未终止分别核验真实Product持有应用/store/journal/data与站点/设备租约；独立来源继续清理，未终止任务不重复启动，晚完成后恰一次释放。测试通过原请求查询在保留期间写入真实SQLite和日志，并检查另一运行实例不能取得资源；它不证明真实SDK回调停止合同。

新的ReleaseVersion为`REVIEW-FOLLOWUP-11`，Storage/MinimumReader继续3/3、Contract仍SIMULATION-v1。从报告指定SourceCommit以全新输出生成portable和win-x64包，不覆盖a080825旧包：

```sh
python3 scripts/package-reference.py --target portable --output .artifacts/review-11-portable
python3 scripts/package-reference.py --target win-x64 --output .artifacts/review-11-win-x64
python3 scripts/smoke.py --product <新解压目录>/app --fixture <新解压目录>/fixture/Ipc.Fixture.dll --output <新结果.json>
```

SourceCommit、源码/配置/模块指纹、实际ZIP SHA及运行结果以11报告为准；无Windows时win-x64只有交叉发布与字节核验，W01～06 NOT_RUN、B01～06 BLOCKED。
