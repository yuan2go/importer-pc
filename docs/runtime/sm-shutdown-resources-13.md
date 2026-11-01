# SM关闭资源修复13

2026-10-30，基线 main `67ab87c1`，分支 `codex/sm-shutdown-resource-13`。范围见[工作包](../work-packages/WP-IPC-SM-SHUTDOWN-RESOURCES-13.md)。本轮修复Application与MVS生命周期，不修改页面、现场配置、SDK ABI、安装或原请求合同。

## 行为与责任

1. 原图后台写盘完成后，状态入账失败会使其Task终结并报错。关闭曾直接await该失败Task并提前返回，之后每次重试都重新抛错。现在关闭按实际Task观察主工作、过载写入、维护、监督及辅助工作：真正终结的失败按原Task身份只登记一次，继续排空其他工作和停止端口；原生停止/释放失败仍保留所有权。只有真实清理结束才设置disposed。原始异常在Stop/Dispose最终回执中继续抛出，不将失败改成成功；资源已释放后的再次Dispose可完成，避免永久卡在旧任务上。
2. `SetInspectionAsync` 等待缓存边界时同时观察原采集Task。若采集提前结束、边界尚未完成，切换明确失败，保留原隔离和错误，释放生命周期锁，使Stop/Dispose能够执行真实清理。取消只结束等待，不证明SDK调用已返回。既有采集任务已经结束的判定与同时结束竞态均通过Task等待处理，不用轮询猜测，不新增相机连接。

业务请求、原报文、尝试、回执和Unknown均不因此改写。原图文件已落盘而状态提交失败时保持Pending及原文件，留给既有重启Interrupted核对；不补写Saved、不重新拍照或发送。`StopGrabbing/CloseDevice 0x80000003` 的既有处理保留，归还缓冲、销毁、Finalize失败继续阻断实际资源释放。

## 验证及原失败保留

新增回归先在未修复产品源码上失败：调试切换等待超时；原图状态一次提交失败使关闭与再次Dispose重复抛出同一错误。测试修复后覆盖先Stop再Dispose、直接Dispose、重复Dispose、可重开SQLite、原PNG保留、原请求/尝试不变，以及调试切换失败后真实有序停止。仍阻塞在原生代码或写盘中的任务不提前释放。

扩大回归中两处旧夹具按当前合同修正，未删除现行守卫：

- 离线激活测试仍要求相机离线时全部供包/恢复不可用，在未改产品代码的main上同样失败。PR162现行合同允许独立人工输入；改为明确断言Camera=false、Manual=true，保留离线状态、配置保全、重连新epoch、单次原请求、原生清理失败不自动重开等检查。
- 窗口生命周期夹具反射PR152已删除的`pendingViewportPause`，触发NullReferenceException。以实际`ServiceAsync/DrainAsync`及刷新任务替代已不存在的字段，继续验证子窗/主窗竞争、在途等待、失败重试、五秒超时和计时器所有权。未恢复视口自动暂停产品行为。

一次交叉baseline构建后还发现源文件恢复时间戳导致增量构建沿用baseline DLL。保留该失败日志，强制Rebuild完整依赖后重新执行专项，不把旧产物当本轮成功。执行记录、原始合成日志、哈希和计数见[证据摘要](evidence/sm-shutdown-resources-13/summary.json)。断言数不是现场用例数。

扩大`--acquisition-lifecycle`运行在23条断言后超时：夹具重连并清掉旧epoch的排队输入后，未处理现行`IngressInterrupted:left`责任就等待恢复相机准入。在恢复main产品源码并完整Rebuild的对照轮同样复现。该工作负载尚未进入停止路径；不删除这个原请求/输入责任守卫换PASS。基线对照及精确日志在证据摘要单独记录，该套不能记为通过。

本轮通过的覆盖包括两项新关闭回归、MVS Probe全套、Application原图/资源/离线重连/调试轮次/共享存储失败、窗口生命周期替代夹具、人工供包/人工调试，以及实际Windows WPF按轮调试。后者运行真实窗口、SQLite和本机HTTP，使用合成采集端口，不能扩大为真实MVS设备验收。精确断言数以摘要为准。

整树清单校验还发现基线main继承的50条旧blob身份和56条缺失清单记录。使用既有`refresh-integration-manifest.py`按实际暂存Git字节统一核对，未修改这些继承的产品文件，也不把它们转记成本轮实现。原manifest仍可从精确基线提交追溯。最终Application、桌面构建与测试目录的ParcelFlow DLL哈希一致，未上传二进制。

## 运行与边界

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --shutdown-resources
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --mvs-self-test
```

测试使用合成SDK/解码端口、真实SQLite/PNG及本机HTTP。Windows事件/计时器替代夹具与真实WPF运行分别记证。未操作当前客户端或真实相机，未做SDK拔插/真实退出、现场RCS、系统安装、发布包或目标机长稳验收；这些均NOT_RUN。GitHub CI继续停用。历史D/RV与旧介质、旧PASS保持原SourceCommit，原索引字节见[归档](../archive/pre-sm-shutdown-resources-13/README.md)。
