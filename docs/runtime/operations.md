# 运行、发布与运维

当前增量实现与验证见 [ALIGNMENT-01](alignment-01.md)，Windows待验见[操作清单](alignment-01-windows.md)。H04原证据保留形成时范围。

最新静态复核见 [MAIN-REVIEW-02](../reviews/main-static-review-02.md)，下一工程整改见 [工程收敛07](../work-packages/WP-IPC-ENGINEERING-CLOSURE-07.md)。当前preflight/backup由会自动迁移的SupplyStore打开源库；使用新程序处理schema1会先升到format2，不能据此制作旧EXE可读的升级前备份。旧格式升级前备份须由兼容旧版本完成并保留；整改前不要把新版本的preflight解释为无副作用检查。Upgrade写入的active-release指针当前也不被Run读取，实际启动由显式Release/PSScriptRoot决定。此为代码静态结论，Windows脚本执行本轮NOT_RUN。

本实现是 F-02 独立参考产品。所有设备、身份、数量策略和外部端点为 **SIMULATION**。核心、SQLite 与恢复为真实代码；没有旧现场工程迁移、厂商驱动或真实 WCS。D/RV 的客户与现场状态不改变。

## 环境与构建

SDK 锁定 `global.json` 的 .NET 10.0.402。NuGet 来源仅 nuget.org；Microsoft.Extensions.Hosting / Microsoft.Data.Sqlite 10.0.13，SQLitePCLRaw.bundle_e_sqlite3 2.1.14，实际传递依赖及内容哈希见各项目的 `packages.lock.json`。首次还原发现默认 2.1.11 原生 SQLite 高危公告，已升级依赖，未屏蔽审计。参考部署基线为 Windows 11 24H2 / build 26100+ x64、WPF，运维脚本检查此基线；这是新参考产品的工程约束，具体现场 OS、DPI、驱动及触控尚未验收。

在仓库根目录，将 SDK 加入 PATH 后：

```sh
./scripts/build.sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll .
```

核心和夹具可在 macOS/Linux 运行；WPF 只能在 Windows 运行。交叉编译成功不证明桌面运行。单节点 MSBuild 避免本次环境观察到的工作节点提前退出。

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

工作台、历史、报警、设备诊断、维护配置、工程控制六个受控页面与两个窗口共用唯一业务实例。布局切换只改展示。全局阻断贯穿页签，输入草稿和详情滚动不随快照重建。启动可加 `--view history --layout compact`，View必须在产品允许名单；布局持久化保留当前View。标准布局按有效区自动双列/单列，紧凑显式单列，内容滚动且主操作固定；小于800×420 DIP有效区显示阻断。确认窗初始焦点在取消、Tab在窗内循环、关闭归还焦点；Enter不触发高风险动作。Windows实际键盘/扫码/DPI/提示声听审仍待验。

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

当前加固结果见 [HARDENING-04](hardening-04.md)，旧RUNTIME-03报告保留其形成时证据。

过载首先关闭源的新输入；在过载之前已接纳的观察继续按业务规则处理和保存，其它许可仍有效。未接纳的观察只保留来源/累计计数等诊断，不承诺逐条持久化。过载标记在实际拒收时同步写入，不再由累计计数定时重写。排空后维护员检查来源与受影响实体；零pending且无未解决运行故障时才能确认过载。UI显示排空/请求/权限禁止原因。确认绑定当前 `OverloadVersion`，新拒收导致版本变化时拒绝旧确认；确认依据和最新来源原因写入SQLite审计后才删除标记。退出不会重建已确认标记；新一轮过载写入新标记，重启重建阻断。普通Resume仍只解除人工暂停。

请求列表分别显示原状态、发送阻断、本地终结禁止原因和下一步。`Prepared`在暂停、过载、原身份过期等情况下可能无法发送。维护员可选择原请求，输入检查依据，执行 `CancelPrepared`。允许条件：仍为Prepared、没有响应，且没有发送尝试，或每次尝试都有已结束的 `NotDelivered` 持久证明。应用在同一协调器内核验/保存状态、Outbox和审计；取消与领取/交付竞争不会复活已终结请求。CLI示例（先snapshot取得当前revision）：

```json
{"Action":"command","Command":{"Id":"unique-local-command","ExpectedRevision":42,"Kind":"CancelPrepared","OwnerId":"original-request-id","Value":"SIMULATION source inspected; local non-delivery evidence reviewed"}}
```

本地终结状态为 `CancelledBeforeDispatch`，原ID、报文、哈希、合同、目标版本和旧操作者均不变。作业或会话进入 `AwaitingDisposition`，另存 `LocalDispositionRequired` 阻断。**此前校验可能产生的预留、实体占位和原接受事实均保留。** 当前版本不提供解除该处置阻断、释放预留、重新授权旧请求或准入新实体的命令；这些需要后续INTEGRATION-BOUNDARY-05取得合同/实体证据。本地终结没有接收端回执，不会新增接受量。异常迟到回执保留为矛盾证据并进入Unknown核对。

T2领取为Dispatching；发送前的最终守卫与过载锁串行裁决，并先保存 `TransportHandoffAuthorized` 审计和 `TransportHandoff` 尝试状态。此后即使实际网络调用尚未开始，也保守视为不能证明未交付。跨进程中断按Unknown核对；不能本地取消或盲重发。发送前守卫失败时记录NotDelivered并回到原Prepared，可恢复对应许可后以同ID再尝试，或受控本地终结。Unknown只能查询原绑定；新登录不重新授权原身份已过期的报文。

活动报警集合与计数只查active=1，不受历史LIMIT影响；已恢复历史独立分页。按原报警ID已读，已读不减少活动计数；恢复匹配对象、来源和代次，新的发生不继承旧已读。请求报警可打开原请求详情及本地终结审计。

没有更改schema1 SQL或迁移校验和，也没有改写旧请求。新版读取原SIMULATION-v1报文/响应；兼容样本来自本次运行的未修改main可执行文件，全部为合成数据。新增JSON状态需要新版解释，**不得用旧EXE直接读取已产生AwaitingDisposition的新库**。升级前保留一致备份；回退旧版只使用升级前兼容备份、新数据根和恢复隔离，重新核对外部账及实体，不把schema号相同解释为任意版本可降级。

## 离线发布与部署

已提交独立 `packages.win-x64.lock.json`，使RID还原不改写便携锁。联网构建机执行（PowerShell 7与Python3）：

```powershell
./scripts/publish.ps1 -Mode Simulation -Output .artifacts/release-simulation -ProductDefinition config/product.reference.json
# 防伪模板另生成，使用全新目录：
./scripts/publish.ps1 -Mode Production -Output .artifacts/release-production
```

生成自包含 win-x64 `app`、Simulation专用 `fixture`、配置/锁/运维脚本、SHA256SUMS和ZIP。离线机器运行不需要 SDK 或联网还原；构建机仍需取得锁定NuGet包。安装目录只读，数据放 `%LOCALAPPDATA%/ImporterPc/data/{Mode}/{StationId}`；跨账户共享需运维显式设置共同数据根和租约根/ACL，当前默认单运行账户。不能随意放网络盘。Production包不带夹具可执行文件。

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
./station.ps1 -Action Backup -File 'D:/IpcBackups/station-new.db'
./station.ps1 -Action Preflight
./station.ps1 -Action Upgrade -NextRelease 'D:/Ipc/releases/next' -File 'D:/IpcBackups/before-upgrade.db'
# 指定选中的发布目录运行，不覆盖旧文件：
./station.ps1 -Action Run -Release 'D:/Ipc/releases/next'
# 必须用新的数据根，原库不删除：
./station.ps1 -Action Rollback -NextRelease 'D:/Ipc/releases/previous' -DataRoot 'D:/Ipc/recovery-new' -File 'D:/IpcBackups/before-upgrade.db'
```

升级先要求无活动箱/未决请求/恢复隔离，校验两套发布文件，再备份并更新旁置发布指针。当前存储格式2（schema_history id1+2，原schema1 SQL不变）；新版可前向迁移schema1备份，匹配格式2可恢复，未来格式/迁移拒绝，不声称任意旧EXE兼容新库。回滚/恢复只接受现存一致备份，创建新库并持久设置恢复隔离；备份中的Prepared亦转Unknown，可能已被外部处理，不能自动补发。先比对原外部接收账和实体库存，未决全部有权威结果后维护员带依据解除隔离。真实上游无查询/人工SOP时继续阻塞，不能凭“恢复成功”放货。

历史读取自数据库，可按对象/状态/日期片段筛选、分页和查看多请求/多尝试/回执/审计。模拟保留清理仅维护接口 `PruneHistoryAsync(days,evidence)` 或控制台 `prune-history`，至少保留1天，只删除过期已关闭且无未决的完整会话、保留当前会话、请求引用配置和清理墓碑审计；不提供生产默认保留天数。诊断导出仅元数据和所选对象请求身份/哈希/尝试，不含条码、原报文、端点、角色备注和私密路径。DB/备份本身不脱敏，必须由本机访问控制保护，禁止公开Git。

## 可选控制台

桌面之外可用同一装配验证核心，不能用它替代桌面验收：

```sh
dotnet src/Ipc.Product/bin/Debug/net10.0/Ipc.Product.dll --config src/Ipc.Product/bin/Debug/net10.0/config --data-root /tmp/ipc-client-data --action serve
```

每行一个JSON：`{"Action":"command","Command":{"Id":"唯一命令ID","ExpectedRevision":当前快照版本,"Kind":"Login","Value":"maintenance"}}`。先输入 `snapshot` 取得当前版本。命令/回执由 `Ipc.Supply/Model.cs` 定义；`emit` 输入 Lane/Kind/Codes/Quantity，入场身份由装配生成；`replay` 推进文件；`reconcile` 输入原 RequestId；`review-observations`、`clear-overload` 输入 Evidence。`quit` 受控停止。核心离线 `--action backup|preflight|restore|export` 与Windows脚本同一实现。

本机发布/维护冒烟可执行：

```sh
python3 scripts/smoke.py --product .artifacts/native-smoke-final --fixture src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --output .artifacts/smoke-results.json
```

该脚本在新临时目录使用实际进程、已发布核心、SQLite与loopback端点；检查第二进程、备份/预检查/恢复隔离、导出、不可写存储、损坏模块与Production防伪。它不执行Windows EXE，不代替WPF运行验收。

## ALIGNMENT-01 数据、后台与版本

箱波次和Item波次来自独立合成接收夹具的不同语义快照，SKU/任务ID/格口/单位/来源/版本/证据时间跟随持久回执；旧字段缺失显示“未取得”。原SIMULATION-v1请求字节和身份不改写。本箱接受量仅从唯一接受账投影，页面不累加计数。

应用读写由StationDispatcher的单个后台消费者拥有；UI读取不写报警，状态变化/维护脉冲更新报警。投影有独立版本与生成时间，超过3秒/读取失败阻断UI输入，后台命令仍查当前状态。设备健康回调只入队；原观察拒收marker保留H04同步持久性，其文件I/O受磁盘时延影响，真实SDK需单独验证。所有实际SQLite和网络工作均不在UI/设备回调执行。

ReleaseVersion=ALIGNMENT-01、StorageVersion=2、ContractVersion=SIMULATION-v1各自独立；旧EXE因schema_history id2拒绝新库，回退必须旧兼容备份+新根+恢复隔离。桌面内全库备份需要有效维护身份；离线运维使用数据租约和本机账户权限。

仅跑本包受影响验证可执行 `dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --alignment`（含H04）；交付前完整关键回归使用不带筛选参数的命令。Windows执行与截图按alignment-01-windows清单逐项留证。
