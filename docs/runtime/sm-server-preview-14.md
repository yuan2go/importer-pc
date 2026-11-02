# 在线相机实时预览调度修复14

2026-11-01，基线 main `3d7e0309`。[工作包](../archive/work-packages/WP-IPC-SM-SERVER-PREVIEW-14.md)限定范围；修改前索引原字节见 [归档](../archive/pre-sm-server-preview-14/manifest.json)。历史11/12/13、D/RV和原安装包继续保持原身份。

## 发现与修复

MVS 的 `Capture` 是包含同步 SDK 取帧、软件解码和持久交付的持续循环，原先用 `Task.Run` 调度，两台相机长期占用共享线程池工作线程。桌面 `ServiceAsync` 和 Application `OwnedRead` 的预览调用也依赖同一个线程池。在工作线程容量受限或发生饥饿时，设备已发布 Ready 并成功取得帧，不代表预览读取有执行机会。

新增独立进程夹具将共享工作线程上限设为2；两台合成 SDK 已发布 Ready 并各取得1个有效帧，随后阻塞在下一次 SDK 调用。原产品的预览工作在1秒内无法开始，检查失败；只修改采集线程调度后，同一检查通过。约束只影响该探针进程，finally 恢复原值。首次编写夹具误用了同一个 DeviceId，修正为两个独立设备身份后才取得有效负向证据；该夹具错误不作为产品复现依据。

生产改动为现有 worker 使用 `TaskCreationOptions.LongRunning` 和默认调度器创建专用后台线程，每台活动相机一个，仍由同一个 MVS 所有者和 Task 管理。固定该次取消令牌，不改 SDK 方法、原生缓冲、采图/解码/交付顺序或业务守卫。负向及修复后验证同时确认：SDK 调用未退出时 Stop 不完成且租约不释放；调用真正退出后两路缓冲与设备资源均排空。

这证明并修复了一个会阻塞预览的实际调度缺陷。正常容量下，原版本原生定时器/切页测试已经通过；因此本轮没有将所有空白画面归因于页面，也没有证据将线程池问题认定为用户服务器截图的唯一根因。无数据、坏帧、解码故障仍按真实诊断区分。

## 验证与维护

原始合成日志、源文件指纹和精确断言数见 [summary.json](evidence/sm-server-preview-14/summary.json)。Release 构建、独立线程池复现、MVS 边界、原生视频生命周期、视频/锁图、扫码请求和停止/重连回归分别记录，不借用旧 PASS。

新增原生生命周期用例不手动调用预览刷新，不停止窗口/调试/预览的三个定时器；两路连续合成图像通过真实 MVS 所有者、软件解码器及 Application 到达 WPF。覆盖无触发视频、持续换帧、历史页签、离页重进、左右像素来源、管理撤销和恢复；不新增隔离或 RCS 请求，SDK 打开/采集次数各为1。已查看原生截图，图像为合成灰度像素，不能作真实设备证据。

既有 `camera-stream-ui` 仍引用 PR162 已删除的 `setup` 字段，替换为核验该整块确认模块仍不存在；原视频、同帧、冻结请求、管理撤销和资源断言保留，没有跳过或放宽它们。新增2个定向入口；删除生产单元0个，重建连接/外部服务0个，合并业务机制0个；其余采集、预览和请求框架继承。

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --mvs-preview-scheduling
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --mvs-self-test
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --camera-preview-lifecycle
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --camera-stream-ui
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --camera-round-ui
```

本机 Windows 原生和合成 SDK 端口不等于真实 SDK/客户 RCS、发布介质安装或目标服务器验收；这些均 NOT_RUN，CI 保持停用。已安装旧介质不会因源码提交自动更新，须交付包含本修复的 SourceCommit 后在目标机确认两路有效帧、预览换帧和调试锁图；线程池正常但无帧时继续按11逐层诊断。
