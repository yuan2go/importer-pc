# 实现与验证原则：项目审查

日期：2026-11-09（Asia/Singapore）。代码基线：`5a12e2f1`；与本次核对的 `origin/main`（`67a03763`）源码树相同。审查后按用户要求修复 A1、A2、A3，并对 R1 先做故障注入再修复；当前数据格式未变。

## 结论与证据边界

现有核心设计有明确业务依据：Application 持有作业、原请求和配置状态，设备实现负责原生资源，UI 调用应用契约；请求先落账再发送，未知结果不自动重发。不能因为这些流程复杂，就删除防重、原件保留、授权复核或实际资源释放检查。

A1、A2、A3 已完成针对性修复。R1 故障注入复现了清理失败仍报告保存成功，以及失败对象在退出流程中失去引用，已一起修复。没有据此宣称真实 SDK 句柄泄漏；现场发生概率未知。下文保留问题的触发条件和审查证据，验证结果以文末本次修复记录为准。

这是覆盖全仓结构、当前 SM 关键执行链及验证工具的审查，不是对每行代码、所有线程交错、全部设备和现场工况的穷举证明。故障在现场出现的概率均未知；没有用测试通过数推算可靠性或现场故障率。

## 审查范围（修复前基线）

按 Git 跟踪文件盘点，`src/` 有 469 个 C# 文件、27 个项目文件、20 个 XAML 文件；`tests/` 有 307 个 C# 文件、9 个项目文件；`scripts/` 有 32 个 PowerShell 和 59 个 Python 文件。该统计不包含生成的 `bin/obj`、未跟踪材料和外置 SDK。对工程引用、条件编译、失败处理、重试、锁、缓存、测试入口进行全仓检索；下表所列关键链进一步逐段阅读。

|范围|核查的实现与边界|结论|
|---|---|---|
|SM 与共享业务|`SportmasterFlow`、`ParcelRuntime`、`ParcelStages`、`ParcelGuards`、原请求与回包恢复|事务边界和一次尝试守卫有必要；未发现自动重发未知 RCS 请求的新增路径|
|放置、人工输入与启停|`ParcelPlacement`、拒绝放置、`ParcelPositionControl`、当前作业卡及相关回归|按放置证据和原请求状态判断；停用等待不能用定时器或小车状态替代|
|RCS|`SportmasterRcsTaskService`、报文解析、心跳、`ParcelAutomaticCameraReads`|边界有身份与大小检查；首次远端确认未参与自动识码判断，见 A1|
|相机及识码|MVS/MVID 选择、原生 ABI 边界、采集/解码、静止判断、会话隔离、释放与自动重连|没有把重连等同于重发；销毁成功后才释放占用的主路径值得保留；真实 SDK 尚未验证|
|配置|配置账 CAS、保存结果核对、局部设备替换、原配置保留|正常路径完整；离线提示问题见 A2，清理失败路径见 R1|
|存储、恢复、历史|当前格式只读预检、事务写入、原件校验、查询容量、快照缓存、维护|本地 SQL 重试有边界；旧格式写入实现仍保留，见 A3；历史规模成本没有本次实测数据|
|管理授权|临时授权、后台状态读取、逐管道连接回收、窗口关闭和执行时复核|不是只靠按钮禁用；授权状态缓存不能替代执行时复核|
|WPF|工程依赖、共享实例、页面生命周期、管理窗口、退出失败处理、操作反馈|业务实例未因切页重建；仅编译和代码审查，不宣称本次做过界面实机验收|
|安装与卸载|交互/静默/批量入口、`InstallationConfiguration`、`StoppedApplicationOwnership`、安装登记与介质|当前 SM 安装/卸载走停机所有权守卫；缺启动种子读取配置账的 Effective，不提升 Pending；不能把旧维护实现误报成当前安装主入口|
|发布及工程工具|锁定恢复、SM 构建选择、发布源身份/哈希、文档检查、Python 测试、CI 配置|沿用现有工具可完成本次验证；未生成安装包，未刷新集成 manifest|
|暂停开发范围|DirectPlc、WcsToPlc、Supply、Inspection、外置扩展及示例|只核查边界和是否进入 SM；按现行规则保留，不把其存在本身判成缺陷，也不新增抽象|

## 修复前确认的问题

### A1 · P1 · 首次 RCS 心跳确认前，自动识码可能提前放行（已修复）

依据为“RCS 离线暂停扫码”和“外部事实应通过明确类型传入内部”。

审查基线定位：`src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs:71`、`src/Ipc.Application.ParcelFlow/ParcelAutomaticCameraReads.cs:16`；业务准入还有 `ParcelGuards.cs` 中的 `AdmissionCapabilitiesReady`。

`StartAsync` 在等待首个定时心跳前设置 `running=true`，同时清空 `heartbeatFailure`。`HealthAsync` 的 `Ready` 据此为真，但 `RemoteConfirmation` 仍是 `Unconfirmed`。`AutomaticCameraReadsAllowed` 只检查 Ready 和有效期。健康读取又把 `ConfirmedAt/ValidUntil` 设置成“本次读取时间 / 三秒后”，因此这些时间说明的是本地对象仍可读取，不能证明最近收到过 RCS 响应。

本机探针使用真实 `SportmasterRcsTaskService`、仓库合成拓扑、回环地址、允许范围内的 3600000 ms 心跳间隔；启动后立即读取两次健康状态，没有发送格口请求。结果为：

```text
Ready = true
RemoteConfirmation = Unconfirmed
ConfirmationAdvancedWithoutHeartbeat = true
ValidityAdvancedWithoutHeartbeat = true
```

这证明服务边界的状态组合存在；自动识码放行来自调用链静态核对。本次没有接真实相机复现扫码发送。默认一秒心跳也有首轮确认前的窗口，具体现场持续时间和发生率未测量。

最小修复方向：自动识码判断使用已经存在的远端确认类型，并由真实响应事实决定确认时效；不要用每次本地读取重新续期。应分别处理没有配置相机编码的人工来源，避免通过全局改成“无心跳绝不允许任何提交”破坏人工输入契约。无需新增重试框架、配置开关或备用 RCS。

针对性验证：尚未收到首个响应、断开后重新连接、确认过期和恢复四种边界；断连期间仅预览，恢复后同次放置不重复请求。现有离线相机测试会先等待 `Ready=false` 再送帧，不能覆盖启动前置窗口。

### A2 · P2 · 相机地址不可达时，保存提示仍可能宣称已重新连接（已修复）

审查基线定位：`src/Ipc.Application.ParcelFlow/ParcelLiveDevices.cs:127`、`:151`。

新地址不可达抛出 `ObservationConnectionException` 时，代码记录连接失败并继续使配置生效，这符合规则。但该分支没有加入 `faults`；如果没有其他启动异常，最终提示仍是“设置已保存并生效：已重新连接……”。保存成功和设备连接成功被合成了同一条成功消息。

这是已确认的控制流与文案不一致，不是配置应当回滚，也不能据此断言底部设备状态显示在线。本次未操作真实相机。

最小修复方向：保留配置立即生效和自动重连，将返回提示改为“设置已生效，相机暂未连接，正在自动恢复”，仅在已确认的情况下报告连接成功。利用现有不可达相机替身检查返回提示、该位离线及另一位继续工作，不新增整套测试框架。

### A3 · P2 · 当前格式规则之外仍保留旧格式升级实现与专属测试（已修复）

审查基线定位：`src/Ipc.Application.ParcelFlow/ParcelProductMaintenance.cs:147`、`ParcelStorageUpgrade10.cs:67`、`ParcelStorageFormat.cs:7`；对应测试 `tests/Ipc.Application.Verification/Database11Compatibility.cs:80`。

规则要求只支持当前数据格式，旧格式在写入前拒绝并保留原件。运行时正常打开数据库确实先只读检查格式 12，但维护实现仍识别 10/11，能够执行 `ALTER TABLE`、回填以及格式身份更新，错误文案也引导先备份再升级；专属测试仍验证这种升级。

调用链核对的重要限制：当前 SM 安装入口已改走停机所有权检查，没有发现它向 `ExecuteChangeAsync` 传入 `purpose="Upgrade"` 和非空变更回调。因此这是仍被编译的旧维护能力、规则和测试方向的冲突，不能写成“当前完整安装包必然修改旧业务库”。本次没有执行旧库迁移。

最小修复方向：退出不再支持的升级写入分支、入口提示及其专属成功断言，保留当前格式结构校验和旧格式拒绝后 DB/WAL/配置原件不变的验证。不要删除仍用于验证当前格式的共用 schema 比较代码。已冻结请求的原字段、原报文读取，以及规则明确要求的心跳秒值换算，是另有现行依据的行为，不能一并当作旧格式兼容层删除。

## 故障注入与待验证风险

### R1 · 设备热替换的清理异常没有完整保留旧所有者（已复现并修复）

审查基线定位：`src/Ipc.Application.ParcelFlow/ParcelLiveDevices.cs:77`、`:164`。

旧设备先成功 Stop，再调用 Dispose；Dispose 抛错只记录异常类型，随后替换 `ports/readers`。未启动候选的 `DisposeQuietlyAsync` 则直接吞掉所有清理异常。

已观察到的是这些异常处理方式；没有观察到真实 SDK 句柄泄漏。当前 MVS 的 Stop 主路径已经执行 Destroy/Finalize，不能把“Dispose 可能失败”直接等同于“相机仍被占用”。尚未确认的是 Dispose 独有资源、清理失败对象能否再次被停止流程访问，以及失败原因是否足够诊断。

本次在真实 `MvsAcquisition` 的解码器释放边界注入两种失败：Stop 成功后的 Dispose 失败，以及从未启动的候选 Dispose 失败。修复前两种场景均返回成功保存，且正常退出没有再次清理失败对象。修复后保留原配置；旧设备仍由原 ports/readers 持有，未采用候选由一个待清理列表持有，退出成功释放前不丢弃对象或释放应用所有权。重复释放失败继续拒绝退出，之后可再次关闭。这里增加列表的依据是已复现的候选所有权丢失，成本为失败对象保留到退出，无后台清理重试或通用恢复框架。

A1 将本地客户端健康与远端响应时间分开：自动识码要求 `RemoteConfirmation`、`RemoteConfirmedAt`、`RemoteValidUntil` 同时有效，实际心跳响应更新后两项；沿用心跳遥测已有的 `max(3000 ms, 3 × 心跳间隔)` 窗口。没有相机编码的人工来源仍走原独立准入。A2 保留配置生效，并明确暂未连接及自动恢复；正常保存也不把本地 RCS 启动当作连接成功。A3 删除旧格式升级、回填及专属成功断言，维护与运行共用当前格式校验；格式 12 建库 SQL 内容未变，旧格式拒绝覆盖 DB/WAL 和配置原件。

### R2 · 大历史库启动与维护成本尚未量化

`ParcelRepository` 打开时校验原始行、回包身份和终态依据，`ParcelRuntime` 还做冲突、原图及恢复检查；这些检查涉及不同责任，不能全部按重复代码删除。部分路径遍历历史，快照序列化和周期状态日志也有成本。

本次没有大历史库、目标工控机或 24/72 小时长稳测量，不能宣称性能不足或满足指标。若现场出现启动慢、日志阻塞或历史读取拖慢，再用现有性能采集工具定位实际耗时，核对同一信任边界是否真正重复扫描，避免预先增加缓存、锁或多级索引。

## 按八项原则的判断

|原则|当前有依据的实现|需要收敛或补证的地方|
|---|---|---|
|最小且完整的改动|SM 内置相机/RCS，业务版本固定，非 SM 暂停开发|A3 的失效行为已删除；不全仓重构暂停模块|
|新增机制须有失败路径和成本|相机断流自动重连、放置重复抑制、原请求冻结均有现场或契约依据|R1 已完成故障注入及修复；R2 仍需测量；未知概率明确标注|
|信任边界校验、明确类型|RCS JSON 配额/重复键/身份检查，原生尺寸/码值/几何检查，存储格式只读校验|A1 已消费远端确认类型与实际响应有效期；同一方法内的冗余判断可局部合并|
|业务和关键失败路径清晰|T1/T2/T3、外部调用与原件落账有明确注释|`ParcelRuntime` 分布于 63 个 partial 文件，需沿调用链理解状态；仅数量不能作为拆类理由|
|保留真实失败语义|未知请求不重发，回包持久化失败不伪装成网络失败，销毁失败保留占用|修复 A2 的成功提示；补证 R1；不要全局给异常加重试|
|测试实际行为|核心测试适配器启动真实验证进程，使用真实应用和数据库，设备/网络用可控边界|补 A1 启动窗口；旧升级测试不能反过来定义现行需求|
|现有工具和源头生成|锁文件、SM 条件构建、发布来源哈希、文档脚本均存在|本次未改生成产物、manifest 或发布介质；打印测试环境缺依赖须单独报告|
|报告真实验证与现场边界|核心回归、编译、探针和文档检查均有本次结果|CI、真实 SDK、客户 RCS、Windows 交互和现场验收不可由本机通过替代|

### 应保留的复杂度及其成本

|机制|明确失败路径或契约|成本与边界|
|---|---|---|
|业务 gate、设备锁、外部调用计数|并发命令、维护与在途设备调用不能交错取得资源|增加排队与锁顺序负担；`ExternalAsync` 释放并重新取得业务 gate，需要复核可变状态，不能去掉必要复核|
|原请求 T1/T2/T3 和回包保管|发送之后崩溃、收到回复但未入账，不能据异常判断未发送|增加磁盘写入及恢复代码；必须保留请求身份、次数和原目标|
|`AtomicStorageWrite` 的有限重试|本地 SQL 在提交前已确认回滚，或同一事务 COMMIT 因 Busy 暂时受阻|有五秒预算及尝试上限；不重试业务/网络副作用，不能移植成通用请求重试|
|相机重连延迟及会话 epoch|断流后自动恢复、旧回调不能进入新会话|有检测与重连延迟；身份/清理失败阻断进一步自动重连符合契约|
|放置账、原图缓存及快照缓存|同件不重复请求，漏读不闪结果，查看必须对应原图|占用内存和维护失效条件；快照缓存按修订/权限分开，连接新鲜度仍重新计算|
|配置 CAS、执行时授权与安装锁|并发草稿覆盖、授权到期、安装时资源尚未释放|有拒绝或等待成本；它们保护动态事实，不属于可删的重复格式校验|

### 可选的局部简化

1. `ParcelConfigurationRules.Validate` 在开头已经拒绝非当前 schema、空 Reader 和旧 MaintenanceLogin，后文仍重复判断同样条件（约第 33、59、70 行）。可在下次相关修改时合并，保留字段内容和跨字段关联检查。这不需要新的验证框架。
2. RCS `RequestDestinationAsync → MatchesFrozen → PrepareWire` 在同一同步检查链上重复验证原绑定和 payload 哈希，可核对每个调用入口后减少重复序列化。不能顺带删掉外部发送边界或磁盘重新载入边界的校验；本次没有性能收益数据。
3. 对 `ParcelRuntime` 的后续修改，优先把一个业务步骤、它的状态读写和失败清理放在容易追踪的位置。暂不按函数行数或 partial 数量拆服务，也不为暂停模块增加统一调度层。
4. 现有覆盖率脚本明确只统计四个托管产品程序集，并标注不是全产品覆盖率。这是正确的边界。若扩展覆盖，优先围绕 A1/A2/R1 对真实行为补用例，不以提高百分比或 PASS 行数为目标。

## 本次修复验证

所有命令先执行 `. ./scripts/project-environment.ps1`。设备自动测试使用 `IPC_VERIFICATION_WITHOUT_VENDOR_RUNTIME=1`，真实 MVS 驱动和 RCS 适配器分别连接可控 SDK 替身与回环 HTTP 服务。

修复前故障注入记录在 `.artifacts/cleanup-before-both.log`：两条路径都错误地报告保存成功、改变配置，并在退出时丢失失败对象。修复后验证如下，未引用其他提交的运行结果。

|类别|本次结果|
|---|---|
|编译|SM Release 桌面构建成功，0 警告、0 错误，使用锁定恢复|
|本机核心测试|46/46 通过，0 失败、0 跳过；36 个行为套件、1141 条 PASS 检查记录，不等于 1141 个独立 MSTest 用例|
|清理故障注入|旧设备 Stop 成功后 Dispose 失败、未启动候选 Dispose 失败均拒绝保存并保留原配置；再次关闭仍失败时保留所有权，之后关闭成功完成释放|
|RCS 与相机|心跳首轮、过期、重连、恢复；软件与原生路线首轮确认前只预览、人工来源仍独立；离线及恢复保留当前放置和原请求；启停、管理窗口及状态日志回归通过|
|配置保存|不可达相机保留生效配置，返回暂未连接及查看自动恢复状态的提示；另一位继续请求格口，未知原请求不重发|
|存储|冻结的格式10/11 schema、其他非当前版本在运行及维护入口拒绝；DB/WAL/配置字节不变，无部署回调；当前格式结构、优化器表和伪装表拒绝检查通过；格式12建库 SQL 内容核对未变|
|额外受影响回归|`--session-shift-25` 通过，保留班次、原请求及进程崩溃恢复断言；仅删除旧格式升级的专属成功断言|
|脚本与文档|格式检查通过；文档检查 8 个入口、205 个链接、5 个故障码；发布说明 5 项、部署兼容 1 项 Python 测试通过|
|CI|提交前未运行本次修复 CI；提交后以 [PR #238](https://github.com/yuan2go/importer-pc/pull/238) 对应来源提交的实际检查记录为准|
|覆盖率|本机未运行；CI 沿用仓库现有采集及门槛，不将其解释为真实设备可靠性|

主要记录：`.artifacts/review-fixes-tests/core.trx`、`.artifacts/review-fixes-core.log`、`.artifacts/review-sm-build.log` 和 `.artifacts/review-*.log`。本地产物不提交；固定的旧 schema 测试输入保存在 `tests/Ipc.Application.Verification/fixtures/parcel10-schema.sql` 与 `parcel11-schema.sql`，来源和哈希在测试中核验，产品代码没有迁移实现。

```powershell
. ./scripts/project-environment.ps1
$env:IPC_VERIFICATION_WITHOUT_VENDOR_RUNTIME = '1'
dotnet test tests/Ipc.Core.Tests -c Release -p:RestoreLockedMode=true -m:1 --logger 'trx;LogFileName=core.trx' --results-directory .artifacts/review-fixes-tests
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1
./scripts/verify-industrial-format.ps1
python scripts/verify-industrial-docs.py
python -m unittest discover -s scripts -p test_release_documents.py
python -m unittest discover -s scripts -p test_deployment_compatibility.py
```

Windows 实机交互、真实 SDK、真实相机、客户 RCS、现场验收及长稳：未运行。仅本机合成边界验证不能代替这些验收。R2 性能测量和可选简化未实施。
