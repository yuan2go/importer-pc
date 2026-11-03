> 当前扩展：[REVIEW-FOLLOWUP-11](review-followup-11.md)，SourceCommit `4edb909e71097a7da61506beaa10ae5db82ec6cb`。下文09原执行选择和形成时静态边界保留；新增E11动态验证详见末节。

# REFERENCE-VALIDATION-09 验证选择与合成场景

2026-10-16；用于AUDIT-RECONCILIATION-01。此页登记验证程序的实际选择规则、输入边界和断言，不预写执行成功；同版SourceCommit、运行日志、Release/Storage/Contract及包SHA由最终`reference-validation-09.md`和checks绑定。历史07/03的NOT_RUN保留形成时记录。

程序入口为`tests/Ipc.Verification/Program.cs`。先启动独立loopback Fixture并确认`SIMULATION-v1`健康，再执行所选模块；每次使用新的临时数据根，退出时停止夹具，证据根路径写到输出。没有访问真实WCS、SDK或设备。选择器可以位于workspace参数前后，一次只接受一个选择器；未知/多个选择器明确失败。

|选择器|实际范围|用途|
|---|---|---|
|无参数或`--reconciliation`|H04 + A01 Data/Composition/Runtime + E07 Closure + E08 Followup/参考旅程 + 原AC/SQLite/进程中断基线|首次必要完整风险基线；保留旧无选择器行为并追加本次范围|
|`--hardening`|H04|保持既有兼容语义|
|`--alignment`|H04 + A01 Data/Composition/Runtime|保持既有兼容语义，覆盖显式迁移与应用后台/队列路径|
|`--data`|A01 Data/Composition/Runtime|A01受影响路径定向复核，不重复H04|
|`--baseline`|原AC/SQLite/进程中断基线|新增模块修复后保留关键恢复基线的独立入口|
|`--closure`|E07 ClosureVerification|07风险的修复后定向复核|
|`--followup`|E08 FollowupVerification及ReferenceJourneyVerification|08鉴权/最终配置所有权与持续参考闭环的定向复核|

在仓库根执行，先按`global.json`使用实际10.0.402 SDK还原并构建Fixture和Verification（或统一solution构建）。固定SDK路径以执行环境核验为准，下面`dotnet`指实际选定宿主，不依赖系统PATH巧合：

```sh
dotnet build tests/Ipc.Verification/Ipc.Verification.csproj -m:1
dotnet build src/Ipc.Fixture/Ipc.Fixture.csproj -m:1
dotnet run --project tests/Ipc.Verification --no-build -- "$PWD" --reconciliation
# 失败修复后只重跑受影响范围，例如：
dotnet run --project tests/Ipc.Verification --no-build -- "$PWD" --closure
```

`--no-build`只适用于对应源码已经成功重建的同一Debug输出。程序通过后不反复运行无关全量；发生新源码变化、失败或未解疑点时再按风险复核。完整默认含进程中断，`--child`为内部子进程入口，不是用户缩减选择器。

## 存储验证资产对齐

`AlignmentDataVerification`不再以构造Store触发隐式升级。独立创建format1和format2历史样本，断言只读检查及普通启动拒绝不改变源SHA；原格式备份和回执保留源/备份Format与SHA；恢复只升级新的隔离副本。显式升级绑定旧/新程序、发布清单和配置身份，验证3条迁移与3:3最低读者，schema1/id2/id3 checksum分别固定：

- `D4CED2B811DFF4D3D66DC66C431B9DCC8C75F491CA30F10A2E5507AAD741C981`
- `4C1BBC06BAF48D95D464867131CD63BFD7048DAC64515B07916652497B60A897`
- `AF01C0A2D5AE4FEB3481E4E703C0BC544CC746FD044736CD8A5B3D86B661FD25`

schema1原件、SIMULATION-v1请求/响应fixtures保持原字节。验证原请求ID、payload/hash、目标/合同均不重写；旧schema1/format2读者拒绝新增迁移。活动会话预检查失败、备份输出竞争、初始回执写入失败、真实SQLite迁移触发器失败均保留源和原格式备份；迁移失败回执保留原异常。未来格式、未来迁移及已知迁移checksum损坏在写源和创建恢复输出前拒绝。

`HardeningVerification`继承原H04防重/Unknown/本地终止/回滚断言，只将过期的两条迁移/2:2预期对齐为3条/3:3并补固定id2/id3 checksum。没有删除历史兼容断言。

## 07新增风险

|范围|实际合成动作及关键断言|
|---|---|
|E-01|独立接收端业务拒绝后，无码/多码/非法码立即撤销当前Task/数量/条码，保留PreviousConfirmed；合法纠正等待自己的回执；旧重复回执不能覆盖当前输入；所有原请求和目标仍完整|
|E-03|同来源新拒收推进EvidenceVersion；旧ExpectedRevision冲突、当前revision配旧证据仍拒绝；精确选择一个来源/epoch/version后另一来源仍阻断；相同commandId重放同回执、改内容冲突；旧无版本入口拒绝；Unknown不释放、不重发、不计接受|
|E-05|显式模拟时钟推进超过来源期限，Snapshot继续新鲜但业务仍Expired；重复确认不能延长TTL，未来时间/未注册来源拒绝；新epoch必须CAS注册并重新确认；旧健康/观察保留拒收证据；旧实体许可直到显式模拟Revalidate与定向来源核对才有效|
|E-06|3个空箱都有Context→CheckClose→Close→Attempt/Response历史；typed字段/状态/含时区日期精确查询；分页边界排除后插入父记录；整个关闭aggregate保留删除后旧页边界明确失败，新查询可继续；活动箱保留|
|E-08|两通道相邻拒绝形成有序独立序号；未确认队首保留、错误/重复确认拒绝、重复回执不新增提示；第65个必要提示在64容量后明确溢出故障并阻断；释放容量不伪称丢失声音已播放；重启保留业务历史且不重放旧提示|
|E-09|合成来源单项Stop/Dispose抛错仍清理独立来源并保留原异常；故障来源未停止时不调用其Dispose；5秒Stop超时仍持有同一任务/回调所有权，晚完成后只释放一次；ModuleCatalog汇总各模块原失败并可达最终资源终态|

`Rig`在明确的合成场景动作边界发出新的来源健康确认并查询独立loopback健康；它不在Snapshot中续期，不把已注入故障改回Ready。静默/TTL测试显式关闭辅助确认，并按模拟来源时间验证。生产TTL没有被延长或定义。ProductRuntime参考旅程使用真实ManualObservationSource合成心跳及实际来源队列/发送器。

## 继承与分层界限

原AC/T1/T2/T3、丢回执Unknown固定请求/查询、原目标合同、唯一接受账、恢复隔离与后台队列断言继续保留。08权限四类身份和Effective/seed/Pending/回退/竞争/部分失败覆盖位于`FollowupVerification.cs`；持续两箱/两通道参考旅程位于`ReferenceJourneyVerification.cs`，由`--followup`和完整选择器调用。

E-09的动态故障覆盖到ObservationModule和ModuleCatalog；ProductRuntime在仍活来源时保留store/journal/data与设备租约的组合分支为静态检查，未用私有字段反射或新增生产注入接口伪造动态通过。实际SQLite停止屏障故障验证在观测原异常/内存屏障/来源终止后撤销合成触发器，再验证一次持久重试；保留原故障日志，不把永久故障遗留到验证宿主清理中。

这些断言验证工程合成行为。WPF几何、PAGE01～08实际操作、DPI、双窗、焦点、扫码、提示可辨性和PowerShell/Windows账户ACL仍按06绑定同版实际Windows证据；真实SDK/WCS、规则/实体/权限及现场准入仍按05逐能力处理。不能用本程序PASS关闭W-01～06、B-01～06或历史D/RV。

独立审查后追加配置回归：复用历史Version的ABA授权被拒绝；推广新Effective同时事务替代旧Effective，草稿插入先后不能替代激活审计顺序；遗留多Effective按实际激活审计选择；SQLite触发器令推广审计失败，断言配置状态与选择一起回滚。Rejected/ActivationFailed的版本身份同样保留，纠正须新Version。

## REVIEW-FOLLOWUP-11 新增范围

`--review-followup`=实际桌面共享的WaveComparison/BoxEntryFocusPolicy源码+XAML结构检查+Product组合所有权。完整`--reconciliation`也包含E11；原选择器范围不削减。

- WaveComparison保留原始前导零、两种波次及未知；差异显示在固定结果区，ItemWave不在Expander。没有新增供包放行规则。
- 焦点策略运行初次登录、新箱/拒绝、身份变更/过期、延后、重复投影、多窗独立及A/B排队旧ticket竞争，验证一次性与代次。执行的是链接的实际展示策略；XAML结构检查不是实际WPF焦点。
- 内部sourceFactory构造仅供既有验证友元使用；公开Host/CLI仍用ReferenceProduct注册，所有配置/模式/最终租约校验仍先行。
- 真实Product分别注入Stop抛错、Stop任务未终止、Dispose任务未终止。捕获原异常，独立来源清理，未终止来源不重复Stop/Dispose；数据根及全部站点/设备键保持独占。相同数据根第二Product和不同工位/根争设备均不能建源。
- 在Product尚未完成Dispose时经原ReconcileAsync查询真实接收账的丢回执请求；真实SQLite保存Accepted、真实journal追加T3Response/ReconciliationQuery，Stopping仍阻断新入场，原payload/Binding/Attempt未变。
- 显式解除故障/完成原任务，重试Dispose后恰一次释放来源；同一lease文件名保留，全部键可重新取得，公开注册工厂可重启同库，原请求远端发送恰1次。

这补齐了上一版Product组合层只有静态检查的验证缺口。实际日志/原数据根/三故障证据与新包SHA见11报告及checks；不倒写上一版09的运行结果，不改schema/旧请求/历史断言。Windows几何、真实键盘焦点、扫码、声音与ACL仍NOT_RUN，真实SDK回调仍BLOCKED。
