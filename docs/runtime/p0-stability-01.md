# P0稳定性修复运行说明

本增量基于 main `3a5b1d13`，范围见[工作包](../work-packages/WP-IPC-P0-STABILITY-01.md)。历史长稳、安装及冻结介质保持原身份；本次结果见[问题卡](validation/p0-stability-01.md)。

## ST01 连接分别延后

RCS预检和运行组件共用心跳映射，只纳入未延后且已启用的读码实例。全部读码器延后时不建立心跳发送循环，并报告无活动读码器；该处理待现场合同确认。相机与RCS可各自保存为待配置，软件可打开参数，现有Application守卫继续拒绝新业务。保存候选后受控重启，重连不应用配置。

复现（先加载Windows项目环境）：

```powershell
. ./scripts/project-environment.ps1
dotnet run --project tests/Ipc.Application.Verification -c Release -- . --p0-deferred
```

八组合夹具使用真实Application/SQLite/TCP/RCS代码和隔离测试装配，只连接127.0.0.1。它不安装机器运行策略，也不能替代受保护正式装配的启动资格。

## ST02 冗余回执清理

只有Committed回执所属作业已归档、且数据库原始BLOB及原hash核验一致，才把spool视为可延后的冗余副本。文件共享冲突、IOException/UnauthorizedAccess进入清理待办，原文件保留；每次删除前仍核对hash。间隔按单调时间从500ms指数增长到最多30秒，持续重试；第五次起在现有容量/维护辅助状态显示ArchiveCleanupWarning。计数封顶32，不积累无界文件字典。解锁后独立维护循环重试，不要求额外扫码或操作员确认。

原始BLOB缺失/损坏、Committed原hash缺失、可读spool的hash矛盾均抛出完整性故障并沿原路径停供。启动原件及回复身份核验优先使用已校验的数据库归档原件，临时不可读的冗余副本不会单独阻止启动；活动/未归档原件的必要校验不放宽。

`dotnet run --project tests/Ipc.Application.Verification -c Release -- . --p0-cleanup`覆盖Windows独占句柄、继续输入、解锁自动清理、三类损坏和单调退避。共享Parcel改动同时进入DirectPlc/WcsToPlc1.8.0，DB9/SQL1–9保持原字节。

## ST06 TCP存活（第一阶段）

TCP驱动1.1.0在连接前启用SO_KEEPALIVE；参数`keepalive-time-seconds=60`、`keepalive-interval-seconds=10`、`keepalive-retry-count=3`。传输失联的理论探测时间约为时间+间隔×次数，即90秒，实际受Windows/网络调度影响，默认值须与现场网络团队确认。没有发送任何相机应用命令。

独立参数`liveness-timeout-seconds=300`限制最长无入站数据时间。TCP握手成功或实际收到字节才更新确认时间；本地100ms检查器只判断单调经过时间，不续Ready。到期Faulted、关闭连接并退役连接Epoch；最后故障仍投递到原Application注册，后续旧连接生产者拒绝。正在完成持久交接的原输入责任不撤销。默认不重连/重放，沿现有设备故障及管理诊断显示。

**300秒是待现场确认的技术默认：合法无来件同样会到期。** TCP ACK只能证明传输对端应答，无法证明相机应用正在转发；持有TCP连接但不转发的本地桩验证的是入站活性期限，不能当作网络丢包下keepalive检测上限实测。真正静默丢包需隔离VM/NIC测试。参数随完整候选保存，受控重启生效，重连不取新参数。

### 第二阶段方案（待合同确认，未启用）

拟在同一完整候选增加`auto-reconnect`开关，默认false，由Application继续拥有注册Epoch及设备生命周期，驱动不私自创建第二会话。重试用单调时钟，1秒起、指数到最多60秒，0.8～1.2抖动；单轮最多8次，失败后保留告警，避免无限密集重连。只恢复原Effective连接，不能应用Pending、发送业务命令、清除Unknown或重放本地数据。每次完成旧资源排空后新注册，旧回调不进入新会话。

启用前须确认相机重连是否重发缓冲条码；没有原帧/实体身份或可靠清缓冲合同，软件不能凭同码判断重复。本轮先交付第一阶段及方案；配置开关和自动重连执行路径待该合同确认后实现，不能声称已具备安全自动恢复。

夹具：`--p0-tcp`（FIN/RST/停止转发），`--p0-tcp-components`（原协议/停止所有权/健康证据断言不改）。
