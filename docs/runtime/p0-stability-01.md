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
