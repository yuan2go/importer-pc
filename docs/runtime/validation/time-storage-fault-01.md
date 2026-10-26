# TIME-STORAGE-FAULT-01 · 问题卡与验证

F0 main a8cd431456f73e83040f75a0c5e3cc1423440d1b；初始红测试与System默认接缝提交b37f89ae。开工时PR108/110/111未合并；用户现已授权叠加PR110，PR112 base为其分支（963165d7），整合93aea521。原工作区/四附件和历史SourceCommit不动。Windows 11家庭中文版22631、约16GB，不是目标26100+/4核8GB。只用新D盘Simulation/本地合成对象，不改系统时间/机器政策、不接真实设备/RCS。

[初始红结果与DLL/log hash](../evidence/time-storage-fault-01/first-red.json)。[源码]定位机制、[测试夹具]人为注入/模拟时间与原生SQLite故障边界、[实测]本机时间分别记录。[文档/PR声明]不代替本次运行。

## ST12 问题卡

确认：b37的ParcelStorageCapacity.cs:16容量1秒、:23保留提示1分钟均以UTC差值节流。UTC回拨12小时而单调过去2秒，检查仍跳过，首个断言FAIL。PR111已有容量单调修复但不在main，本包没有转记其PASS。

修复：容量及保留提示节拍均GetTimestamp/GetElapsedTime，UTC留作审计/日历。全仓扫描512命中行及别名在[清单](../time-storage-fault-01-utc-inventory.md)。额外先红后修：操作员最长会话被回拨延长、管理模式CTS已到期而Status仍授权；服务15秒等待改Stopwatch。

验收：回拨、前跳不能跳过节拍、保留提示继续检查、操作员单调最长寿命、已过期管理token的状态拒绝，本机通过。残余风险：清单中的旧Product/Supply、可选重量、计划备份及持久委托等未做专项，不声称全仓风险已关闭。NOT_RUN：真实NTP/SCM回拨、跨班次长时与目标硬件。

## ST13 问题卡

确认：ParcelStages.ApplyReply及ParcelReceiptCustody、ParcelTerminalRecovery、ParcelLegacyReceiptRecovery都包含早于PreparedAt的拒绝；适配器SportmasterRcsTaskService只是本机赋时。真实应用调用本地ReferenceWcs期间，注入UTC回拨1小时且单调前进，首个用例形成Conflict/Blocked、仅一次外呼，断言FAIL。未发现独立客户文档规定必须用墙钟先后判冲突，不把猜测当合同。

修复：新增可缺省且不改变旧序列化的LocalClockEvidence，只由接收Application为新回包生成，保留原At/HTTP原字节；本机端点回拨、单调经过时间、原请求与原回包hash绑定。实时与恢复共用验证。ID/hash/终态矛盾等原冲突规则保持，已有Conflict不得解除。

验收：在途回拨正常接受；证据可持久恢复且无再次发送；无证明的早到仍拒绝；改证据/任意旧时间不被豁免；已有Conflict仍Blocked，本机通过。残余：不在观测范围的回拨继续保守拒绝。NOT_RUN：真实RCS/系统NTP、跨重启真时钟变化、现场联调；本包新CI已执行既有冻结/单次请求回归，不能扩大到真实RCS。

## ST08 问题卡

确认更正：原Database.Open默认5秒，provider已有BUSY/LOCKED自动重试；不是“所有瞬时失败立刻停供”。本包初始用例在BeforeIngressDurable注入两次写前IOException，第一次即关闭accepting、来源返回false，断言FAIL。

已修：显式本地事务与提交阶段分类。只在未COMMIT且确认回滚时重试，原ID/body/hash固定，5秒单调预算/32次封顶退避。提交结果不明不重复、不报成功；FULL/损坏/结构类不做瞬时重试。WAL/FULL不变，未改DB9结构。

已验：两次写前I/O后第三次输入交接成功；真实SQLite写锁约188～194ms释放后只一行；两次写后、提交前注入有真实回滚而只留一行；真实已提交后的结果丢失只执行一次且报Uncertain；注入FULL及原生max_page_count耗尽均永久分类/一尝试/无行；持续I/O约5秒受预算限制；FULL=2/WAL保持。这里只是本地持久化阶段[测试夹具]，没有冒充物理抖动/写满。

闭环确认：合入父PR后又先红后改，[永久故障红测试](../evidence/time-storage-fault-01/st08-supervisor-before.json)在93aea521观察退出码78仍被启动3次，失败测试提交6d1d597e。

闭环修复：已知持久化边界先围栏并发布一次致命故障。桌面在启动前拥有IRuntimeFatalSource观察，正常释放后取消观察；RuntimeFatalMonitor将版本/commit、实例、Epoch、最近状态及预算写ST14致命日志并刷盘，然后退出。超预算/结果不明继承有限拉起；永久类退出78直接持久Block/alert而不反复拉起。原维护故障文案和ST02临时占用重试保持。

闭环验收：[测试夹具/本机实测]真实ParcelRuntime、CrashDiagnostics、RuntimeFatalMonitor、DesktopRecoverySupervisor独立进程串联：持续I/O→围栏快退→一次重启；提交后丢交接结果→快退→一次重启且原Pending保留Interrupted；永久FULL分类→首次退出即Block。两种重启均核对既有Unknown/冻结请求字节不变，provider调用为0；永久退出日志含版本/SourceCommit/实例/Epoch/最近状态，告警存在。测试只有合成IO/FULL注入，实际页配额FULL在独立Atomic夹具验证。

残余风险：5秒为可控重试预算，原生I/O/Flush挂起无法托管硬截止；物理卷写满/权限损坏时诊断与监督账本也可能落盘失败，不能宣称日志/告警必达。NOT_RUN：真实卷故障、已安装计划任务触发/SCM/WER/UAC/卸载、目标硬件长稳；[VM步骤](../time-storage-fault-01.md#windows-vm验收本次not_run)。自动恢复依赖PR110既有一次UAC登记，本机未安装政策。

## 前阶段本机回归（未含叠加闭环）

新时间/存储夹具、Atomic边界、parameter-guidance-startup、deferred-connections、rcs-heartbeat、默认档retention-ingress均通过；core锁依赖构建、四产品WPF及自包含安装器编译0警告0错误。实际源与日志hash另附，不用这条摘要给后续提交授予PASS。未安装本机产品或政策，未关闭旧完整恢复缺口。

统一NOT_RUN：目标26100+/4核8GB、24h/72h、真实SDK/相机/RCS、物理断电、Production安装/接管、604800完整UAC恢复、2592000。未执行大档；没有把短测或CI授予现场运行资格。

## 固定源码本机证据

修复源ec8081f36f89d3068896abfcba77ac13c478cc1d重新锁构建后，两新选择器再次通过。[逐断言、预算结果、DLL与原始log hash](../evidence/time-storage-fault-01/local-ec808.json)只归该源；四产品/安装器前述工作树编译不混为这次DLL身份。这是依赖获批准前的历史阶段，未验证新增进程闭环；当前叠加实现由后续独立证据另计。

|问题|当前修复源位置|
|---|---|
|ST12|ParcelStorageCapacity.cs:15、CommonCapabilities.cs:33、LocalManagementMode.cs:127、ManagementModeBroker.Installation.cs的Wait|
|ST13|ParcelStages.cs:85/112、ParcelReceiptChronology.cs、ParcelReceiptCustody.cs:43、ParcelTerminalRecovery.cs:56、ParcelLegacyReceiptRecovery.cs:121；Contracts/ParcelContracts.cs可缺省字段|
|ST08|ParcelIngress.cs、Ipc.Platform.Storage/AtomicStorageWrite.cs、ParcelStorageFailure.cs、ParcelRuntime.cs、ParcelCommands.cs、ParcelGuards.cs、RuntimeFatalFailure.cs、CrashDiagnostics.cs、DesktopRecoverySupervisor.cs、Ipc.Desktop/ParcelApp.xaml.cs|

改动文件已复筛：没有新增HTTP/RCS重试；新的同步退避仅在原同步持久交接路径，仍由来源回调/原lane所有者保有资源；不在UI刷新getter内睡眠。事务回调只有本地SQL，COMMIT结果不明不重做，异常不会被吞掉变成功。时钟证据与nullable字段不修改旧JSON/null省略语义；原SQL1–9没有变更。长期内存/实际native挂起/现场资格不由这些检查授予。

## 前阶段CI身份（ec808）

[Linux job121289396953](https://github.com/yuan2go/importer-pc/actions/runs/40233280329/job/121289396953)与[Windows冻结介质job121289397000](https://github.com/yuan2go/importer-pc/actions/runs/40233280329/job/121289397000)均SUCCESS，head ec8081f3，实际检出合并ref d062ec8d3d3593c14f57f734488150479cd3db67。[原源/两job/新介质hash](../evidence/time-storage-fault-01/ci-ec808.json)固定身份；不是本机DLL，也不把它记成后续文档提交验证。未签名、实际Windows安装NOT_RUN。ST08缺失进程闭环没有被这次CI授予通过。

## 叠加后独立验证

四个新选择器（time-storage-fault/atomic/supervisor/process-checks）及父P0 cleanup/rcs/unknown-restart/supervisor已本机通过；后续源提交/日志哈希在新增回执另计。Windows本机reliability-02受既有MachineRuntimePolicy（未安装指定账户政策）阻断，不绕过或登记；其Linux CI另验，不能把环境阻断写成通过。旧P0非CJK快捷方式COM门禁仍由其原CI记录说明，未改断言。

## 阶段3965回执（原COM阻断保留）

[本机逐断言、DLL及log hash](../evidence/time-storage-fault-01/local-3965.json)固定3965cea59ef79ee82f3408721d24d679e9e6cd7e：8个选择器PASS，四BusinessModule WPF构建及Sportmaster自包含win-x64安装器编译PASS。0502的首次精确进程复验遇到注入dotnet的映像尚未确认；生产监督器安全Block并非业务恢复失败，3965仅补夹具启动确认，不削弱断言。两次尝试的身份与结果均保留，0502该次不计PASS。

[CI独立回执](../evidence/time-storage-fault-01/ci-3965.json)固定PR头3965、实际合并检出72aba1ce153c434b14097d9f3b35de3c2cbd7b87（run40260949436）：Linux job121373047065 SUCCESS，覆盖本机受政策阻断的既有可靠性/进程死亡段；Windows新4个时间存储选择器、全部P0边界选择器及真实Dispatcher钩子步骤通过。Windows p0 job121373038435最终FAIL，原因仍为未改动DeploymentShortcuts.cs:45的中文路径COM快捷方式，未删断言/跳过，PR保持草稿。

Windows冻结介质job121373038672 SUCCESS；SM1.16.0单EXE安装器SHA256 `111133345d59bb259f03147b68ec3266c147b76010ef8d4a2483f53a3c435882`，payload `0d496d64db1e5463e1869074a0d32baa43a52951630476dfdeda995ddb389791`。这份介质实际SourceCommit为72aba1ce；未签名，实际安装NOT_RUN，CI Server 2025不等于目标Windows 11工控机长稳资格。

本段后的回执提交只包含文档/manifest/合成证据，不给后续提交转记上述PASS；其自动CI另外计证。父PR110尚未合并，PR112继续叠加其分支，父合并后才转main，不自行合并。

## 继承PR110 980a后的当前回执（99a5）

收尾检查发现父分支推进：980a1383继承Unicode Shell Link COM修复；255ccd4a同步双方历史，版本仍SM1.16.0/Contracts1.20.0。没有在子包重写该修复，原3965/72aba的COM FAIL保留。99a593b1为合并后manifest校对源；[本机精确回执](../evidence/time-storage-fault-01/local-99a5.json)重新锁构建，8个新/父P0选择器、三ACP隔离COM、四WPF及自包含安装器编译全部通过。

[新CI独立回执](../evidence/time-storage-fault-01/ci-99a5.json)：run40262133399，PR头99a593b1ec3840a1545a230d54195024342aa195，实际合并源b3606350376cde9231c0523d98c116d6e3dd6186；Linux job121376836336、Windows P0 job121376836219、冻结安装器job121376836360均SUCCESS。原installer-options断言及新增ACP1252/936/65001创建/回读/升级/归属保护/删除都执行，无全机区域修改。当前COM门禁已解除，旧FAIL没有倒写。

新未签名安装器SHA256 `76a378c2318c2932bf5b8966f8b7d15c2867c71526f5e132ba84dc08efd7311b`，payload `211db4f0b1363c7c0289723ceabd81d0d95eef469700c568f523551565e7dc24`，实际SourceCommit为b3606350。实际安装/任务/SCM/WER/卸载、真实时钟/盘故障与目标硬件长稳仍NOT_RUN。

最后只补文档与manifest：核对合并后实际文件Git blob/sha，补首红证据缺项，原证据正文和SourceCommit不变；这个回执头的自动CI独立计证，不转记99a5/b360 PASS。PR112仍叠加父分支并保持草稿；父合并main后再转main，不擅自合并。
