# 全仓UTC用法清单 · TIME-STORAGE-FAULT-01

本次初扫固定b37f89ae（main a8cd4314加System默认时钟接缝/红测试，未改算法）。`rg --json --hidden`扫描工作树，再按`git ls-files`过滤；关键词`UtcNow|GetUtcNow`，计的是命中行，单行多个读取不冒充多个位置。共512行/180文件：src162、tests215、docs126、scripts7、examples2。两份Windows保留名aux.js另从Git blob读取（61577/89936字节），均无字面命中，其余已跟踪文件纳入工作树扫描；关键词缺席不表示没有时间逻辑，别名补查见下文。

[完整512行CSV](evidence/time-storage-fault-01/utc-inventory-baseline.csv)逐行列文件/行、用途、决定和理由；下面展开全部162个生产源命中行。行号对应初扫b37，不把修复后偏移号套回旧代码。已修位置通过本包新验证另计；“风险待专项”是[源码/推断]，没有运行不写无法复现/已通过，也没有为覆盖率硬改其他业务。

## 需要改与保留的分界

- 已先红后修：容量1秒/保留提示1分钟节流；在途回包时序及三处恢复核验；操作员最长会话；管理模式单调token到期状态。服务15秒等待改Stopwatch，真实SCM回拨未测。
- 保留UTC：冻结请求/回包/观察时间、审计/历史cutoff/文件日期轮转、签发与资格到期、RCS心跳合同Unix时间。没有把跨重启证据替换成进程计数器。
- 已有单调：RuntimeJournal故障退避、Parcel UI快照本地寿命、各CancellationTokenSource/Task.Delay预算；它们附带UTC显示字段不是退化依据。
- 风险待专项复现，未改未宣称通过：旧Product巡检UTC差值、旧Supply UI三秒Fresh、可选重量等待/新鲜度、UTC计划备份间隔、旧Supply角色/实体许可及持久委托/锁定。需要各自业务/进程恢复夹具；只删UTC边界可能放宽原证据守卫。

## 别名及不含UtcNow的时间逻辑

- `LocalManagementMode.cs`的`Clock→Now`，ValidSession/Status/Enter/Delegate/ValidateDelegation/CheckLock。15分钟CTS原已有单调取消；本包修状态与Require一致。2分钟委托记录和失败锁定为持久UTC，跨客户端/服务重启及回拨组合仍需专项，不能由状态测试代验。
- `CommonCapabilities.cs`的`OperatorSession.Valid(now,maximumLifetimeMs)`由调用方传UTC；本包新增同进程单调硬上限，Set/Clear原子发布。原身份AuthenticatedAt/ExpiresAt和UTC检查保留。
- `ParcelScheduledBackup.cs`的`CheckScheduledBackupAsync(now)`、`nextBackupCheck/NextDue`，入口由ParcelGuards传UTC。进程间计划持久化和进程内轮询应分开，回拨延后有源码风险，本包未以改容量轮询冒称备份调度已修。
- `StationApplication.cs`的`Now=clock.GetUtcNow()`流向旧Supply命令、8小时模拟角色、30分钟实体许可、原请求/尝试/响应/审计。时间真实性规则与新SM不同，需独立复现，不改旧业务合同。
- `ResourceLease`/`PackageUseLease`是文件/系统互斥的真实owner，未发现用UtcNow作可超时抢占的TTL；不会因UTC到期就删租约。设备Epoch/来源确认期限仍按原证据校验。Hik SDK资格与DeploymentQualification是跨重启绝对有效期。
- `.NET Task.Delay/CTS.CancelAfter`与Stopwatch/GetTimestamp是进程内预算；审计记录的At/ProgressUtc并不反向控制这些预算。证书NotBefore/NotAfter及签名资格必须保留日历语义，未改系统时钟或假造授权。

## 全部生产源命中行

|位置（初扫）|用途|决定|理由|
|---|---|---|---|
|src/Ipc.Application.Inspection/InspectionRuntime.cs:56|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow.Desktop/ParameterRuntimePresentation.cs:24|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow.Desktop/ParcelSnapshotValidity.cs:14|UI快照寿命|已有单调保护|MonotonicFresh并行UTC未来时间拒绝；未改历史UTC字段|
|src/Ipc.Application.ParcelFlow.Desktop/ParcelSnapshotValidity.cs:26|UI快照寿命|已有单调保护|MonotonicFresh并行UTC未来时间拒绝；未改历史UTC字段|
|src/Ipc.Application.ParcelFlow.Desktop/ParcelSnapshotValidity.cs:49|UI快照寿命|已有单调保护|MonotonicFresh并行UTC未来时间拒绝；未改历史UTC字段|
|src/Ipc.Application.ParcelFlow.Desktop/ParcelViewModel.cs:35|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow.Desktop/ParcelViewModel.cs:269|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelAdministration.cs:10|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelCommands.cs:20|证据真实性/有效期|UTC守卫保留|绝对证据窗口与本地执行预算分开；此行不直接作为持续时间节流器|
|src/Ipc.Application.ParcelFlow/ParcelCommands.cs:39|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelCommands.cs:46|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelCommands.cs:87|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelCommands.cs:161|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelConflicts.cs:59|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelConflicts.cs:66|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelDeviceLifecycle.cs:100|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelGuards.cs:12|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelGuards.cs:58|等待重量/新鲜度|风险待专项复现|墙钟回拨可能延长等待/新鲜度；本包未运行可选重量组合回拨，不宣称已修|
|src/Ipc.Application.ParcelFlow/ParcelGuards.cs:65|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelGuards.cs:80|维护tick+计划备份传时|混合/间隔风险待专项|主循环Task.Delay单调；TryScheduledBackup的now/NextDue为UTC，回拨延迟备份尚未纳入本包修复|
|src/Ipc.Application.ParcelFlow/ParcelGuards.cs:85|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelHealth.cs:31|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelHealth.cs:38|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelHealth.cs:54|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelHealth.cs:67|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelHistory.cs:133|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelHistory.cs:137|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelHistoryRetention.cs:121|7～30天日历cutoff|UTC保留|跨重启历史保留与审计年龄，不改为进程寿命|
|src/Ipc.Application.ParcelFlow/ParcelIngress.cs:54|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelIngress.cs:84|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelIngress.cs:91|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelInitialConfiguration.cs:52|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelInitialConfiguration.cs:95|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelInputs.cs:30|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelLegacyReceiptRecovery.cs:121|回包时间完整性|已修ST13|只有新接收且有本机单调回拨证据可例外；历史无证明/真实冲突守卫保留|
|src/Ipc.Application.ParcelFlow/ParcelLegacyReceiptRecovery.cs:147|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelLegacyRecovery.cs:68|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelMaintenance.cs:91|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelMaintenance.cs:160|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelOperators.cs:16|操作员会话上限|已补单调硬上限|CommonCapabilities.OperatorSession已先红后改；原UTC真实性/到期检查保留|
|src/Ipc.Application.ParcelFlow/ParcelOperators.cs:23|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelOperators.cs:24|证据真实性/有效期|UTC守卫保留|绝对证据窗口与本地执行预算分开；此行不直接作为持续时间节流器|
|src/Ipc.Application.ParcelFlow/ParcelOverload.cs:21|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelReceiptCustody.cs:43|回包时间完整性|已修ST13|只有新接收且有本机单调回拨证据可例外；历史无证明/真实冲突守卫保留|
|src/Ipc.Application.ParcelFlow/ParcelRepository.cs:198|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelRepository.cs:274|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelRepository.cs:311|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelRestoreHandoff.cs:48|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelRestoreHandoff.cs:116|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:41|操作员会话上限|已补单调硬上限|CommonCapabilities.OperatorSession已先红后改；原UTC真实性/到期检查保留|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:275|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:322|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:346|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:417|观察新鲜度|风险待专项复现|需连同来源未来时间/epoch/进程内锚点验证，未仅删除UTC守卫|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:419|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:457|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:479|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:481|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:486|等待重量/新鲜度|风险待专项复现|墙钟回拨可能延长等待/新鲜度；本包未运行可选重量组合回拨，不宣称已修|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:492|等待重量/新鲜度|风险待专项复现|墙钟回拨可能延长等待/新鲜度；本包未运行可选重量组合回拨，不宣称已修|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:504|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:511|等待重量/新鲜度|风险待专项复现|墙钟回拨可能延长等待/新鲜度；本包未运行可选重量组合回拨，不宣称已修|
|src/Ipc.Application.ParcelFlow/ParcelRuntime.cs:518|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelStages.cs:31|证据真实性/有效期|UTC守卫保留|绝对证据窗口与本地执行预算分开；此行不直接作为持续时间节流器|
|src/Ipc.Application.ParcelFlow/ParcelStages.cs:107|回包时间完整性|已修ST13|只有新接收且有本机单调回拨证据可例外；历史无证明/真实冲突守卫保留|
|src/Ipc.Application.ParcelFlow/ParcelStartupMaintenance.cs:137|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelStorageCapacity.cs:16|节流间隔|已修ST12/其余UTC保留|容量与保留提示轮询改单调；7天到期cutoff仍UTC|
|src/Ipc.Application.ParcelFlow/ParcelStorageCapacity.cs:23|节流间隔|已修ST12/其余UTC保留|容量与保留提示轮询改单调；7天到期cutoff仍UTC|
|src/Ipc.Application.ParcelFlow/ParcelStorageCapacity.cs:26|审计/日历保留|已修ST12/其余UTC保留|容量与保留提示轮询改单调；7天到期cutoff仍UTC|
|src/Ipc.Application.ParcelFlow/ParcelStorageCapacity.cs:27|审计/日历保留|已修ST12/其余UTC保留|容量与保留提示轮询改单调；7天到期cutoff仍UTC|
|src/Ipc.Application.ParcelFlow/ParcelStorageCapacity.cs:32|审计/日历保留|已修ST12/其余UTC保留|容量与保留提示轮询改单调；7天到期cutoff仍UTC|
|src/Ipc.Application.ParcelFlow/ParcelStorageRelocation.cs:42|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelSupervision.cs:48|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.ParcelFlow/ParcelTerminalRecovery.cs:56|回包时间完整性|已修ST13|只有新接收且有本机单调回拨证据可例外；历史无证明/真实冲突守卫保留|
|src/Ipc.Application.ParcelFlow/ParcelTerminalRecovery.cs:118|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Sportmaster.Desktop/SupplyStationViewModel.cs:29|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply.Contracts/Model.cs:48|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply.Contracts/Model.cs:60|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply.Desktop/ExtensionsView.xaml.cs:89|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Application.Supply.Desktop/MainWindow.Alarms.cs:52|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Application.Supply.Desktop/MainWindow.Closure.cs:37|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply.Desktop/MainWindow.Extensions.cs:30|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Application.Supply.Desktop/MainWindow.Extensions.cs:35|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Application.Supply.Desktop/MainWindow.HistoryExport.cs:16|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply.Desktop/MainWindow.HistoryExport.cs:21|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply.Desktop/MainWindow.HistoryExport.cs:47|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply.Desktop/MainWindow.Presentation.cs:22|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Application.Supply.Desktop/MainWindow.xaml.cs:66|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Application.Supply.Desktop/MainWindow.xaml.cs:134|旧Supply UI快照寿命|风险待专项原生复现|仅UTC差值缺单调上限；不以SM新快照验证覆盖旧Supply|
|src/Ipc.Application.Supply.Desktop/MainWindow.xaml.cs:143|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Application.Supply.Desktop/MainWindow.xaml.cs:157|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply.Desktop/MainWindow.xaml.cs:369|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply.Desktop/MainWindow.xaml.cs:374|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply.Desktop/ViewModels.cs:116|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply/Maintenance.cs:194|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply/Maintenance.cs:205|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply/Maintenance.cs:232|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply/StationApplication.cs:33|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Application.Supply/Store.cs:63|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply/Store.cs:186|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Application.Supply/Store.cs:244|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Assembly/DeferredConnectionPorts.cs:15|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Assembly/DeferredConnectionPorts.cs:30|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Assembly/OriginalParcelQuery.cs:73|证据真实性/有效期|UTC守卫保留|绝对证据窗口与本地执行预算分开；此行不直接作为持续时间节流器|
|src/Ipc.Assembly/ParcelCapabilityFactory.cs:34|绝对资格有效期|UTC保留|签发/到期是跨进程绝对证据，不换成进程Stopwatch|
|src/Ipc.Components/ReferenceOptionalServices.cs:25|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Components/ReferenceOptionalServices.cs:31|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Drivers.Hikrobot/HikrobotReader.cs:128|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Drivers.Hikrobot/HikrobotReader.cs:172|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Drivers/ObservationSources.cs:56|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Drivers/ObservationSources.cs:66|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Drivers/ParcelDevices.cs:36|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Drivers/TcpLineObservationSource.cs:107|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Drivers/TcpLineObservationSource.cs:127|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Drivers/TcpLineObservationSource.cs:164|来源健康/观察UTC证据|UTC赋时保留|节拍应使用Task.Delay/单调；UTC用于确认/有效期与未来证据拒绝，不能伪造Ready|
|src/Ipc.Fixture/Receiver.cs:43|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Fixture/Receiver.cs:83|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Integrations.SportmasterRcs/RcsHeartbeat.cs:48|RCS心跳合同时间戳|UTC保留|Unix毫秒是既有报文字段，调度Task.Delay单调；不重写历史报文|
|src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs:63|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs:113|本机回包赋时|UTC保留/接缝可控|适配器没有PreparedAt冲突规则；不改HTTP原字节或At，Application另记证据|
|src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs:121|本机回包赋时|UTC保留/接缝可控|适配器没有PreparedAt冲突规则；不改HTTP原字节或At，Application另记证据|
|src/Ipc.Platform/ConfigurationRevisions.cs:44|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ConfigurationRevisions.cs:60|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ConfigurationRevisions.cs:70|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ConfigurationRevisions.cs:77|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ConfigurationRevisions.cs:89|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ConfigurationRevisions.cs:103|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ConfigurationRevisions.cs:104|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ConfigurationRevisions.cs:139|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ConfigurationRevisions.cs:146|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ConfigurationRevisions.cs:152|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ConfigurationRevisions.cs:164|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/DeploymentQualification.cs:29|绝对资格有效期|UTC保留|签发/到期是跨进程绝对证据，不换成进程Stopwatch|
|src/Ipc.Platform/ExtensionPackages.cs:79|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ExtensionStore.cs:136|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ExtensionStore.cs:206|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ExtensionStore.cs:222|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/ExtensionStore.cs:256|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/LocalManagementMode.cs:9|管理模式/审计入口|混合：已修状态/委托需专项|15分钟CTS本已单调，状态现在检查到期token；Now别名的2分钟持久委托/失败锁定另列，未冒称全覆盖|
|src/Ipc.Platform/LocalManagementMode.cs:29|管理模式/审计入口|混合：已修状态/委托需专项|15分钟CTS本已单调，状态现在检查到期token；Now别名的2分钟持久委托/失败锁定另列，未冒称全覆盖|
|src/Ipc.Platform/LocalManagementMode.cs:94|管理模式/审计入口|混合：已修状态/委托需专项|15分钟CTS本已单调，状态现在检查到期token；Now别名的2分钟持久委托/失败锁定另列，未冒称全覆盖|
|src/Ipc.Platform/LocalManagementMode.cs:103|管理模式/审计入口|混合：已修状态/委托需专项|15分钟CTS本已单调，状态现在检查到期token；Now别名的2分钟持久委托/失败锁定另列，未冒称全覆盖|
|src/Ipc.Platform/ManagementModeBroker.Installation.cs:126|15秒服务等待|已改Stopwatch|仅静态/构建核查；真实SCM+回拨NOT_RUN|
|src/Ipc.Platform/ManagementModeBroker.Installation.cs:127|15秒服务等待|已改Stopwatch|仅静态/构建核查；真实SCM+回拨NOT_RUN|
|src/Ipc.Platform/ManagementModeBroker.Windows.cs:90|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Platform/NativeSdkBoundary.cs:46|绝对资格有效期|UTC保留|签发/到期是跨进程绝对证据，不换成进程Stopwatch|
|src/Ipc.Platform/RuntimeJournal.cs:58|日志审计/日历轮转|UTC保留|退避已用GetTimestamp；GetUtcNow写审计，mtime/跨日保留是日历语义|
|src/Ipc.Platform/RuntimeJournal.cs:72|日志审计/日历轮转|UTC保留|退避已用GetTimestamp；GetUtcNow写审计，mtime/跨日保留是日历语义|
|src/Ipc.Platform/RuntimeJournal.cs:119|日志审计/日历轮转|UTC保留|退避已用GetTimestamp；GetUtcNow写审计，mtime/跨日保留是日历语义|
|src/Ipc.Product/IntegrationRuntime.cs:46|证据真实性/有效期|UTC守卫保留|绝对证据窗口与本地执行预算分开；此行不直接作为持续时间节流器|
|src/Ipc.Product/IntegrationRuntime.cs:186|旧Product巡检调度|风险待专项复现|ConfirmedAt与UTC差值会受回拨影响；现行Parcel独立巡检Task.Delay不等于旧Product已修|
|src/Ipc.Product/IntegrationRuntime.cs:206|证据真实性/有效期|UTC守卫保留|绝对证据窗口与本地执行预算分开；此行不直接作为持续时间节流器|
|src/Ipc.Product/IntegrationRuntime.cs:217|证据真实性/有效期|UTC守卫保留|绝对证据窗口与本地执行预算分开；此行不直接作为持续时间节流器|
|src/Ipc.Product/ProductRuntime.cs:159|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Product/ProductRuntime.cs:257|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Product/ProductRuntime.cs:271|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Product/ProductRuntime.cs:280|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Product/ProductRuntime.cs:303|旧Supply身份/实体许可|跨重启UTC及待专项|独立业务历史许可/角色时间需自己的回拨夹具；本包OperatorSession修复不自动覆盖它|
|src/Ipc.Reference.Protocol/ReferencePeer.cs:28|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Reference.Protocol/ReferencePeer.cs:55|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Reference.Protocol/ReferencePeer.cs:99|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
|src/Ipc.Reference.Protocol/ReferencePeer.cs:119|审计/呈现/冻结事实赋时|UTC保留|未发现此命中行独立承担间隔控制；变量别名消费者见正文补充|
