# 连接、回执清理与故障恢复

SM 的相机、RCS 与人工输入独立判断。新站可将连接留待配置，保存时校验实际配置和设备身份。连接读取 Effective。两位均停用时，已配置的 RCS 仍发送空成员心跳。原请求编号由永久账分配，恢复连接不能重发请求或清除未知结果。[心跳与启停](rcs-heartbeat-01.md) · [运行与恢复](lifecycle-ux-current-guidance-01.md)

## ST02 冗余回执清理

只有Committed回执所属作业已归档、且数据库原始BLOB及原hash核验一致，才把spool视为可延后的冗余副本。文件共享冲突、IOException/UnauthorizedAccess进入清理待办，原文件保留；每次删除前仍核对hash。间隔按单调时间从500ms指数增长到最多30秒，持续重试；第五次起在现有容量/维护辅助状态显示ArchiveCleanupWarning。计数封顶32，不积累无界文件字典。解锁后独立维护循环重试，不要求额外扫码或操作员确认。

原始BLOB缺失/损坏、Committed原hash缺失、可读spool的hash矛盾均抛出完整性故障并沿原路径停供。启动原件及回复身份核验优先使用已校验的数据库归档原件，临时不可读的冗余副本不会单独阻止启动；活动/未归档原件的必要校验不放宽。

`dotnet run --project tests/Ipc.Application.Verification -c Release -- . --p0-cleanup`覆盖Windows独占句柄、继续输入、解锁自动清理、三类损坏和单调退避。

## 保留装配的 TCP 存活检测（SM 不使用 TCP 读码）

TCP驱动1.1.0在连接前启用SO_KEEPALIVE；参数`keepalive-time-seconds=60`、`keepalive-interval-seconds=10`、`keepalive-retry-count=3`。传输失联的理论探测时间约为时间+间隔×次数，即90秒，实际受Windows/网络调度影响，默认值须与现场网络团队确认。没有发送任何相机应用命令。

独立参数`liveness-timeout-seconds=300`限制最长无入站数据时间。TCP握手成功或实际收到字节才更新确认时间；本地100ms检查器只判断单调经过时间，不续Ready。到期Faulted、关闭连接并退役连接Epoch；最后故障仍投递到原Application注册，后续旧连接生产者拒绝。正在完成持久交接的原输入责任不撤销。默认不重连/重放，沿现有设备故障及管理诊断显示。

**300秒是待现场确认的技术默认：合法无来件同样会到期。** TCP ACK只能证明传输对端应答，无法证明相机应用正在转发；持有TCP连接但不转发的本地桩验证的是入站活性期限，不能当作网络丢包下keepalive检测上限实测。真正静默丢包需隔离VM/NIC测试。参数随完整候选保存，受控重启生效，重连不取新参数。

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
