> 2026-10-26整合：当前组合继承PR110及PR113差量，统一SM1.17.0、DirectPlc/WcsToPlc1.11.0、Contracts1.21.0，DB9不变。进程监督默认关闭，显式启用后的永久故障Block及恢复先暂停保持；普通手动启动不被改成自动供包。下述旧基线/版本/CI状态保留形成时身份，本轮实际结果见[整合执行记录](integration-20261026.md)。

> 2026-10-26恢复暂停补缺：监督子进程在原Application Paused账写入软件暂停后才接收；没有既有Unknown/输入的首次写前故障重启也不自动恢复供包。仅显式Resume沿原守卫解除暂停，不清Unknown、不重放。续修SM1.16.1、DirectPlc/WcsToPlc1.10.1、Contracts1.20.0，DB9不变；[补缺证据](validation/stability-integration-02.md)。下文1.16.0及原PASS均保留历史来源。

# 时间与存储故障分级 · 运行说明

本包F0 main a8cd431456f73e83040f75a0c5e3cc1423440d1b，原3a5b1d13已前进。SM1.16.0、DirectPlc/WcsToPlc1.10.0、Contracts1.20.0预留以避开并行PR110/111版本；DB9/SQL1–9不变。[工作包](../work-packages/WP-IPC-TIME-STORAGE-FAULT-01.md)、[问题卡/本次证据](validation/time-storage-fault-01.md)、[全仓UTC清单](time-storage-fault-01-utc-inventory.md)分别计证，不移动旧PASS。

## 时间来源

容量1秒、保留提示轮询1分钟用TimeProvider的GetTimestamp/GetElapsedTime；UTC只记录最后检查时间和7～30天日历cutoff。前跳不跳过间隔，回拨不冻结节流。这里不改变保留/退役的日历合同，不自动删除未决数据。

操作员会话在原AuthenticatedAt/ExpiresAt真实性检查之外增加同进程单调硬上限；按Set时已过年龄及provider剩余寿命计算，不因回拨延长。Clear/Set发布单一不可变grant。管理模式原已有15分钟CancelAfter，本包让Status/ValidSession也核对该token；实际Require与显示授权保持一致。2分钟持久委托及旧Supply角色/许可的回拨需另有专项，不由这条状态测试代验。认证服务15秒等待改Stopwatch；本机没有控制真实SCM或修改系统时钟。

RuntimeJournal的故障退避、Parcel UI快照寿命、RCS心跳Task.Delay和各CTS预算原已有单调机制，未重复实现。签发/资格/证书有效期、跨重启历史日期与冻结请求/回包仍UTC。其余源码风险及未改理由逐行在清单中，不声称全仓时间风险全部关闭。

## 新回包的本机回拨证据

原Application将reply.At早于PreparedAt或晚于当前UTC+1秒视作完整性冲突；在实时ApplyReply及三处恢复/原件依据检查存在同类规则。未发现客户合同单独规定这条绝对UTC先后约束，RCS适配器本身只是给回包赋本机UTC。ID、payload hash、重复ID原字节、已有终态矛盾、协议/业务/物理完成规则均保留。

Application在实际外呼前后捕获UTC端点与单调经过时间。只有观察到完成UTC小于开始UTC，才为本次新回包增加LocalClockEvidence：runtime/request/PreparedAt、两个UTC端点、单调经过ticks、原预算及原回包hash。回包At与IntegrationHttpResponse原字节不改。provider不得自带此字段；证据必须绑定本条原回包，且At接近实际观测端点，不能用一次回拨豁免任意过去/未来时间。

证据进入本次raw与原T3审计，恢复共用同一验证器；没有新表/SQL迁移。旧回包字段为空时不序列化，原构造函数ABI及历史字节不改写。历史无证明的异常时序保持原守卫，已有Conflict不会解除，Unknown不清除，不做Query替代或重发。跨重启或回拨发生在观测范围之外，无法证明的情况继续保守拒绝；未宣称监听了所有系统时间变更事件。

## 已知未提交写入的短故障

原Microsoft.Data.Sqlite已对BUSY/LOCKED按命令timeout自动重试，原默认5秒不是零重试；本包不把短BUSY一概写成原缺陷。实际缺口是写前I/O暂时失败同样永久围栏，以及没有明确分级。

输入接管仍必须原SQLite持久Pending成功后才返回true。新AtomicStorageWrite冻结一次原ID/body/hash，在显式事务内写本地SQL；只有尚未开始COMMIT、确认rollback/auto-rollback后，才允许重试。退避25/50/100/200ms封顶，总预算5秒、最多32次，均单调；每次SQL忙等待设短步，之后恢复标准连接设置。单次native I/O/Flush/必要回滚不是可强杀的托管预算，不能据此承诺操作系统硬截止。

COMMIT开始后的I/O/结果丢失记DurableOutcomeUncertain，不重新执行SQL回调，也不报告成功；SQLite确认的BUSY COMMIT仅可在原事务上继续等待，不能重跑业务。SQLITE_FULL、CORRUPT/NOTADB、结构/约束错误及损坏扩展码按Unrecoverable。前置可恢复故障耗尽预算为RetryBudgetExceeded。只重试本地输入事务，不重试RCS/设备动作、不降低FULL/WAL、不跳过重启核对。

依据：[Microsoft.Data.Sqlite BUSY/LOCKED重试](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors)、[SQLite错误与自动回滚](https://www.sqlite.org/lang_transaction.html)、[SQLite错误码](https://www.sqlite.org/rescode.html)。实际测试是合成故障/原生SQLite锁及页配额，不是物理盘抖动/写满实测。

## 已接入PR110的故障闭环

用户2026-10-26明确批准叠加PR：PR112以codex/p0-stability-01为base，PR110来源963165d7保留历史，经93aea521合入。父PR合并main后再转回main；不强推、不改旧证据。原先“等待依赖”的阶段记录仍固定于ec808证据，不能当作当前状态。

Application先停止准入，发布单次IRuntimeFatalSource通知；RuntimeFatalMonitor由桌面进程所有者在StartAsync前观察，在正常完全释放所有权后取消/释放观察。它把实例、设备Epoch、最近状态/错误阶段与预算交给ST14 CrashDiagnostics，连同版本/commit写运行日志并Flush(true)，随后快速退出。核心库不自行Environment.Exit，不在未知业务状态继续运行。

RetryBudgetExceeded或DurableOutcomeUncertain走ST14原明确致命退出码及有限监督重启；FULL/损坏/结构不符等Unrecoverable走明确退出码78，监督器先持久Block/alert.json，再停止重启，不耗完普通崩溃重试次数。恢复需要按P0指南由授权维护修复后重置。输入、业务提交、维护及已知仓库打开/恢复边界接入；校验候选参数拒绝仍是原业务结果，不伪称坏库。临时冗余回执占用仍由ST02本地有界退避处理，不发全局致命通知。

新进程沿用Pending→Interrupted、Prepared显式继续、Dispatching→Unknown及原冻结请求核对；任何类别都不触发业务重发/清Unknown。只对确认回滚的输入事务做5秒内重试，其余已持久/外部阶段失败直接退出，避免重新执行外部动作。

### 运维与限制

自动拉起依赖已通过既有一次UAC安装阶段登记的PR110恢复策略，普通运行仍非管理员。不在本包测试机注册任何服务/任务/HKLM；未登记的开发直接启动进程会退出，由开发者手动重启。监督次数/间隔及SCM/WER/卸载路径沿用[P0运行指南](p0-stability-01.md)，本包不新增系统安装条目或数据库表。

受控永久存储退出不再向可能写满的卷追加WER dump；原未知致命崩溃仍走ST14 WER路径。日志/监督账本也可能因物理写满、坏盘或权限损坏写入失败，不声称能保证刷盘/告警落地；相关系统调用不可由托管5秒预算强杀。必须在隔离Windows VM做真实卷容量/权限故障测试。

### Windows VM验收（本次NOT_RUN）

在全新隔离Simulation VM内，通过P0规定的唯一UAC安装候选，记录介质SourceCommit/哈希与注册策略；确认普通运行。用独立合成数据库锁占用/限额盘模拟短锁、超预算及FULL，保存fatal日志、监督state/alert、退出码、拉起时间。短锁释放应一次持久交接；超预算应快退并按登记间隔启动唯一客户端；FULL应首次即Block，修复前不再拉起。对同根既有Unknown与Pending逐条核对原报文/身份、无外部重发；完成授权维护修复再重置。最后按P0卸载验证任务、WER与SCM登记清理。不得在真实工位执行。
