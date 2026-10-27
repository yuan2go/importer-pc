当前配置仅schema3，Parcel数据库仅10，Supply仅6/6；其他格式拒绝并提示使用全新数据目录。程序集版本与包身份以当前项目声明和本次构建清单为准。旧指南原字节及形成时证据索引见[收口前归档](../archive/pre-fresh-baseline-current-guides-01/README.md)，不用于当前操作。

> 2026-10-26：用户已授权修复PR110非CJK代码页中文快捷方式，并与PR112/108/111整合。[本轮源码、验证与边界](validation/pr110-unicode-shortcuts-01.md)取代旧“另包待授权”的当前状态；原CI失败、原PASS及SourceCommit保留，修复前原件见docs/archive/pre-pr110-unicode-shortcuts-01/FILES.json。

# P0稳定性修复运行说明

交付基线为 main `a8cd431456f73e83040f75a0c5e3cc1423440d1b`（PR109后）；F0原基线3a5b1d13的失败/通过记录各守原身份。范围见[工作包](../work-packages/WP-IPC-P0-STABILITY-01.md)，本次结果见[问题卡](validation/p0-stability-01.md)。

## ST01 连接分别延后

相机延后过滤、零读码器不发心跳、按Effective重连及跳过未配置候选编号范围已由main/PR109提供，本包保留其代码。本批在新基线先失败后修复的剩余边界是显式禁用扩展读码器仍被要求心跳编号；现在同样排除禁用实例，启用后继续恢复必填校验。

RCS预检和运行组件共用心跳映射，只纳入未延后且已启用的读码实例。全部读码器延后时不建立心跳发送循环，并报告无活动读码器；该处理待现场合同确认。相机与RCS可各自保存为待配置，软件可打开参数，现有Application守卫继续拒绝新业务。保存候选后受控重启，重连不应用配置。

恢复配置只为已配置候选登记编号范围，旧未配置RCS的空字段不阻断补齐后的受控启动；已有编号表、永久底账、在库/归档原请求绑定仍核验。待配置单项重连及全部延后时的整体重连返回Rejected，整体重连只处理Effective中已配置的会话。

复现（先加载Windows项目环境）：

```powershell
. ./scripts/project-environment.ps1
dotnet run --project tests/Ipc.Application.Verification -c Release -- . --p0-deferred
```

八组合夹具使用真实Application/SQLite/TCP/RCS代码和隔离测试装配，只连接127.0.0.1。它不安装机器运行策略，也不能替代受保护正式装配的启动资格。

## ST02 冗余回执清理

只有Committed回执所属作业已归档、且数据库原始BLOB及原hash核验一致，才把spool视为可延后的冗余副本。文件共享冲突、IOException/UnauthorizedAccess进入清理待办，原文件保留；每次删除前仍核对hash。间隔按单调时间从500ms指数增长到最多30秒，持续重试；第五次起在现有容量/维护辅助状态显示ArchiveCleanupWarning。计数封顶32，不积累无界文件字典。解锁后独立维护循环重试，不要求额外扫码或操作员确认。

原始BLOB缺失/损坏、Committed原hash缺失、可读spool的hash矛盾均抛出完整性故障并沿原路径停供。启动原件及回复身份核验优先使用已校验的数据库归档原件，临时不可读的冗余副本不会单独阻止启动；活动/未归档原件的必要校验不放宽。

`dotnet run --project tests/Ipc.Application.Verification -c Release -- . --p0-cleanup`覆盖Windows独占句柄、继续输入、解锁自动清理、三类损坏和单调退避。

## ST06 TCP存活（第一阶段）

TCP驱动1.1.0在连接前启用SO_KEEPALIVE；参数`keepalive-time-seconds=60`、`keepalive-interval-seconds=10`、`keepalive-retry-count=3`。传输失联的理论探测时间约为时间+间隔×次数，即90秒，实际受Windows/网络调度影响，默认值须与现场网络团队确认。没有发送任何相机应用命令。

独立参数`liveness-timeout-seconds=300`限制最长无入站数据时间。TCP握手成功或实际收到字节才更新确认时间；本地100ms检查器只判断单调经过时间，不续Ready。到期Faulted、关闭连接并退役连接Epoch；最后故障仍投递到原Application注册，后续旧连接生产者拒绝。正在完成持久交接的原输入责任不撤销。默认不重连/重放，沿现有设备故障及管理诊断显示。

**300秒是待现场确认的技术默认：合法无来件同样会到期。** TCP ACK只能证明传输对端应答，无法证明相机应用正在转发；持有TCP连接但不转发的本地桩验证的是入站活性期限，不能当作网络丢包下keepalive检测上限实测。真正静默丢包需隔离VM/NIC测试。参数随完整候选保存，受控重启生效，重连不取新参数。

### 第二阶段方案（待合同确认，未启用）

拟在同一完整候选增加`auto-reconnect`开关，默认false，由Application继续拥有注册Epoch及设备生命周期，驱动不私自创建第二会话。重试用单调时钟，1秒起、指数到最多60秒，0.8～1.2抖动；单轮最多8次，失败后保留告警，避免无限密集重连。只恢复原Effective连接，不能应用Pending、发送业务命令、清除Unknown或重放本地数据。每次完成旧资源排空后新注册，旧回调不进入新会话。

启用前须确认相机重连是否重发缓冲条码；没有原帧/实体身份或可靠清缓冲合同，软件不能凭同码判断重复。本轮先交付第一阶段及方案；配置开关和自动重连执行路径待该合同确认后实现，不能声称已具备安全自动恢复。

夹具：`--p0-tcp`（FIN/RST/停止转发），`--p0-tcp-components`（原协议/停止所有权/健康证据断言不改）。

## ST14 崩溃闭环

现行实现覆盖全局异常、外部进程监督及安装系统登记；业务观察器不重启、不重放、不强制GC。Supply共用全局异常钩子，自启动/监督登记覆盖三个Parcel产品。

桌面构造时注册AppDomain、TaskScheduler及WPF Dispatcher钩子。致命异常记录版本、SourceCommit、进程、实例、连接Epoch、最近状态及有界进度预览，日志同步Flush(true)后调用RaiseFailFastException，退出码0xE0000014；UI异常不继续执行。任务未观察异常只计数、每秒最多一条采样日志，不启动任何业务恢复。日志沿已有轮转；落盘失败不伪造成功，native异常参数标识是否刷盘。诊断目录沿受保护D盘存储锚；目录不可写、磁盘挂起、堆损坏等情形仍可能无法取得托管日志，不能承诺所有native崩溃都有完整上下文。

安装沿原一次UAC，在唯一部署登记提交后投影以下系统项，失败进入原CommittedNeedsSystemRepair路径；普通权限不写HKLM或创建任务：

- 每产品、指定账号SID唯一的计划任务；InteractiveToken、LeastPrivilege、登录触发、IgnoreNew，不保存Windows密码。数据根迁移/版本升级更新同一个任务。既有启动窗口通过已登记任务启动监督者，后台等待确认；只传递已校验的页面/布局/UI选择，配置和数据根固定取权威登记。未登录不能显示WPF，本包不修改自动登录、电源或更新政策。
- 独立WinExe监督进程等待客户端退出，默认30秒后重启，最多3次；健康运行900秒用单调时钟复位预算。正常退出码0不重启。监督者自身的计划任务失败重启间隔至少60秒、同样有限次数，符合Windows最小一分钟限制。
- 进程创建前刷盘记账；运行后保存PID、启动时间、源版本及可执行文件hash。监督者重启只接管匹配的原进程；创建结果不明或账损坏即围栏，不能猜测原进程不存在。部署退役/根改变时停止监督，不杀业务进程。它不检测或强杀挂起进程，不发业务命令、不清Unknown、不应用Pending、不重放。
- 循环保护写入数据根runtime-recovery/产品/state.json、alert.json及轮转诊断，任务最终返回77；后续普通启动会提示维护。常规已知崩溃预算可在原UAC“修复系统登记”入口、原进程已退出时复位；原创建结果不明不能由该入口盲目清除，需运维先保全并核对原进程。
- HKLM64的WER LocalDumps/Ipc.Desktop.exe为本机同名客户端共享，默认DumpCount=2、DumpType=1（mini），目录为受保护机器根diagnostics/crash-dumps，指定运行账号可写。保留最少约128MiB容量检查只是安装预检，不是dump字节硬上限；完整dump选项须另作容量评估。不同产品共享该键，最终采用最近一次获授权安装的dump政策；既有外部WER登记不覆盖。
- 既有LocalSystem管理授权服务设置SCM恢复：5秒重启、30秒重启、最后NONE；重置周期86400秒，同时覆盖服务主动报告非零失败。最后NONE避免SCM无限重复最后一次重启动作。服务删除时恢复动作随SCM条目删除，共用服务在最后消费者卸载时删除。

DesktopRecoveryOptions作为安装请求/部署登记的可选属性提供Enabled、RestartDelaySeconds、MaximumRestarts、HealthyResetSeconds、DumpCount、DumpType及边界校验，不增加日常操作步骤。旧记录缺该属性时保持原行为及原序列化字节；按2026-10-26整合授权，新安装默认关闭（Enabled=false），保持原手动启动行为；显式提供Enabled=true才注册并启用监督。已有冻结回执保持其原政策与身份，不把旧默认开启的测试改写为本次证据。选项沿安装计划hash、回执核对和系统修复，不进入业务数据库。

卸载清单SystemEntries列出任务、WER键、dump目录、原SCM服务和随数据保留的监督账。移除本产品任务；最后消费者卸载时移除本安装器拥有的WER键及命名为Ipc.Desktop.exe.<PID>.dmp的受控dump，最多64项核查，空目录删除；外部登记/未知文件不擅删，保留路径进入回执。其他产品仍使用的共享系统项保留。业务、配置及监督审计账继续遵循既有“RemovedDataRetained”语义。真实登记、强杀重启、dump轮转、SCM及卸载往返尚待VM，不能把XML验证或子进程测试记作系统安装PASS。

参考：[Task Scheduler最小重启间隔](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-interval-restarttype-element)、[InteractiveToken及仅验证标志](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskfolder-registertask)、[WER LocalDumps](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps)、[SCM恢复动作](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/ns-winsvc-service_failure_actionsw)。实际验证范围见问题卡。
