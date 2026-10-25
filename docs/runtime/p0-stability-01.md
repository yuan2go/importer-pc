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
