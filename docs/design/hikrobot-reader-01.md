# 海康能力与职责

MV-PD010003-06M-12C 使用 `Ipc.Drivers.Hikrobot.Mvs` 的真实 MVS 采集；适用的官方条码识别组件尚未取得。`Ipc.Drivers.Hikrobot` 的 MvCodeReader 服务智能读码器，不是该型号的回退实现。HBDecode 是图像解压，不能替代条码解码。

解码层决策、状态机和故障表只定义在[MVS 解码设计](mvs-decoding-01.md)。采集生命周期、参数、依赖和运行入口统一见[海康运行说明](../runtime/hikrobot-reader-01.md)。`Observation`、`DeviceHealthConfirmation` 与受控注册以 `src/Ipc.Platform/DeviceCapabilities.cs`、`ObservationSession.cs` 为准；业务准入、条码最终筛选和原请求责任属于 Application。

一个设备实例服务所有页面。IP 是连接地址，Production 预期序列号是物理绑定；每台相机一个 RCS 编码，由取格口和心跳共用。SDK 缓冲必须成对归还，释放失败保有原设备所有权。无图像不等于断线，采集成功不等于识码、业务接受或物理完成。故障不自动重连，软件暂停不是急停。

原生适配沿 `NativeSdkBoundary`、精确包/完整依赖校验和独立部署资格流程；模拟、自检与回放均不能授予 Production 资格。适用组件、真实触发合同和现场读码指标须单独取得证据。原始要求及既有验证见[SDK工作包](../work-packages/WP-IPC-HIKROBOT-SDK-BARCODE-02.md)，D/RV及形成时回执保持其原身份。
