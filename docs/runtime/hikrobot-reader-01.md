# 海康标准相机当前安装、参数与诊断

目标 MV-PD010003-06M-12C，Windows x64，自有版本 1.0.0。当前装配是内置 MVS 采集；正式识别组件尚缺。参数的单一事实来源是 `src/Ipc.Drivers.Hikrobot.Mvs/parameters.json`，实际 ABI 与依赖身份见同目录 `sdk-boundary.json`、`bundled-runtime.json`。

## 安装与参数

当前SM安装包内置真实MVS采集驱动及核查的GigE原生闭包；无需另装驱动包、配置扩展信任或退出首配再切换。原生库不上传Git，离线构建输入来自获授权本机D盘。安装器按既有受保护部署完整性、所有权、指定运行账户及生命周期安装。不得依赖MVS客户端全机搜索路径，不修改PATH。

首次配置和日常管理参数共用同一完整候选。左右直接显示海康SDK、IP、预期序列号、触发要求、关联和有界采集缓冲；没有TCP端口/keepalive或TCP切换。公共模板无现场IP。每台只有一个相机编码，取格口与心跳共用；序列号是物理设备身份，不是第二个RCS编码。

相机和RCS可以明确稍后配置；未配置相机的位置不启动、不接包，另一台已就绪相机可独立作业。两台相机都未配置或RCS未配置仍拒绝新入场，见[当前单相机规则](station-single-camera-recovery-01.md)。保存候选后按原Pending/Effective受控重启启用。Production启用须预期物理序列号，左右不得重复，同一源实例重连不自动学习另一台设备。切换页面不重建连接，未保存输入保持同一候选。

## 真实SDK与边界

当前ABI为MV_CC_GetSDKVersion=0x04080201，入口DLL文件版本4.8.2.1。14个已核查x64 DLL约10MiB，主入口MvCameraControl.dll、GigE预载MVGigEVisionSDK.dll，完整字节/hash、AMD64和保护目录逐项核验，整目录只允许当前清单，禁止同名异字节已加载模块。SDK模块及文件保持至进程退出，换库受控重启。SDK缓冲由实际GetImageBuffer/FreeImageBuffer成对归还；连接/停止/资源释放沿原加固实现。

MVS仅采图，缺适用正式条码识别组件。只有真实连接/实际帧才能分别报告连接/帧；不把图像解压、模拟码或连通当识码成功。内置装配默认未注册解码器，仍仅观察、不创建包裹或RCS作业，健康不发布业务Ready。正式算法仍需厂家提供适用组件、x64运行库、真实接口文档/示例与必要许可，不限定组件名称。

## 构建与必要核验

开发侧正常本机交付使用package-application.py的--mvs-runtime指向已核查的14库及SDK-NOTICE.txt目录；这不是安装用户的操作。缺文件/字节变化/非AMD64/缺许可告知应拒绝正式介质。CI无厂商文件时仅可显式生成VERIFICATION_ONLY_VENDOR_ABSENT验证介质，不宣称SDK已内置或可正式交付。

安装内置字节随application-release与安装介质清单一起校验。参数可读不证明SDK已加载；连接诊断、实际帧、算法、RCS与现场分别记录。仅枚举初始化验证不打开或触发相机，不证明真实读码。

## 解码框架与离线验证

[解码设计](../design/mvs-decoding-01.md)是决策、状态机及故障表的唯一来源。`Ipc.Platform/FrameDecoding.cs` 定义只读帧/候选及取消端口，`Ipc.Drivers.Hikrobot.Mvs/Decoding` 实现预分配池、有界队列、规则选择、触发归并、自检、统计和隔离进程。当前没有厂商算法或新的 Production 注册，假解码器仅在测试和离线工具中编译。

显式装配通过 `MvsDecoding.Create(context, decoderFactory)` 接入端口；相机仍走既有内置字节校验边界。适用算法的工厂必须按原 `NativeSdkBoundary` 和部署资格流程构造；原生调用不能保证取消时使用 `ProcessFrameDecoder`，worker 在资格核验后调用 `RunWorkerAsync`。端口可用不等于获准生产，不通过配置任意加载 DLL/执行文件。

默认 `entity.mode=observe-only`、帧池4槽、队列2帧、载荷上限8 MiB、池预算64 MiB、解码500 ms、解码停止2000 ms。池为4×8=32 MiB/路，双路64 MiB，SDK内部队列另计；隔离进程每路另预分配最多32 MiB。所有上限在创建池/连接相机前校验。正式触发需显式 `trigger`、`acquisition.trigger=on`、`trigger.expected-source/selector`、已核对 `trigger.frames-per-entity`、有效 `selection.version` 及码制等规则；不提供现场触发值或生产运单规则。默认内置装配因缺解码器会明确拒绝 trigger，不能通过修改配置解除。

每次启动执行内置 Code39 合成图自检。`ConfirmationSource` 分别报告 acquisitionHealthy、decoder、triggerVerified、selectionValid、entityMode、businessReady、规则身份和计数；序列号及条码不进入诊断。`Ipc.Mvs.Decoding` Meter 提供取帧/解码/无码/丢弃/旧代/SDK断档/失败、队列水位及耗时直方图，快照累计当前源对象生命周期；计时使用 Stopwatch。无码/歧义是观察，不是业务成功。故障保持，受控 Stop 后新 Epoch 再次自检；不自动重连。

当前MVS入口只接收紧密排列的Mono8单图，长度必须等于宽×高；没有已知行填充合同就拒绝，不猜测跨度。通用解码端口及离线回放支持显式Stride。选择正则使用NonBacktracking，单次及整批预算均为100ms；选择超时按无码报告。

主宿主使用现有事务账的 Revision 预留运行代次，再由 ObservationSession 增加连接代次；随机注册尾缀防恢复副本碰撞。未改变数据库 schema、原请求、TCP 提供者或生产资格。独立端口调用方也须提供新的受控注册身份，不能复用旧 Epoch。

在新的 D 盘工作树执行：

```powershell
. ./scripts/project-environment.ps1
dotnet build ImporterPc.slnx -c Release -p:BusinessModule=Sportmaster -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release "-p:IpcContractPath=$PWD/src/Ipc.Platform/bin/Release/net10.0/Ipc.Platform.dll" -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --self-test
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --decoding-test $PWD
dotnet build tools/Ipc.Mvs.Replay -c Release -p:RestoreLockedMode=true -m:1 -nr:false
```

回放命令、人工真值格式、指标分母与合成结果见[回放夹具说明](../../tests/fixtures/mvs-replay/README.md)。输入真实样本和输出报告只能放仓库外；应用采集不留存图像。Stop/Dispose 等待的是实际资源所有者，解码停止违约会失败并保有原帧/租约，不能将超时当作停止成功。相机 SDK 本身不可返回时继续沿原保有策略，本包未改其强制终止能力。

本框架实际执行的构建、合成/原生验证及未验边界见[验证记录](evidence/mvs-decoding-01/README.md)。
