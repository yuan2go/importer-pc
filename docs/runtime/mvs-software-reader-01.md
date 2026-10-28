# MVS采图、软件识别与本地RCS联调

> 2026-10-29的组合、停止结果保留、紧密Mono8检查及原生配置回归见[SDK整合记录](integration-20261028-sdk.md)。以下原设备联调数字属于原分支，不代表当前整合树的设备验收。

2026-10-28，当前新供包台默认内置 `hikrobot-mvs-reader` / `HIKROBOT-MVS-READRESULT-v1`。用户已确认无PLC：电脑连接相机，每次有效识别均交RCS处理，同码不在客户端合并。识别请求不等于物理包裹数量或物理完成。原MvCodeReader及独立MVS采集模块保留原合同和历史证据，不静默转换已有配置。

同一 `MvsAcquisition` 拥有设备、物理租约、工作线程、取消、SDK缓冲和停止责任。按明确IP及正式序列号匹配，拒绝第0台或型号回退。当前软件读码器由SDK默认设置并核对关闭触发、持续采集；客户无需选择触发模式，不写曝光、像素格式或触发源。独立旧采集模块仍只核对原设置。先复制完整单帧，再归还SDK借帧，之后调用进程内ZXingCpp 0.5.3（内核3.1.1）识别。识别器只持有单帧托管缓冲，逐次交唯一Application持久输入、既有筛选、作业账、原请求和RCS适配器；无Python识别子进程、传图服务或新作业框架。

视频采样间隔计入 `skippedVideoFrames`，未识别原始帧不等于已识别业务输入。不同帧同码有不同输入/作业/请求身份。完整候选交原筛选规则；空白不建作业，多码不擅自选第一条。单帧默认8MiB、1～32MiB可配置；候选触及64条上限、非法编码、丢包、计数倒退或持久输入拒绝明确故障。入账拒绝的原识别保留在源对象中并阻断重启，仅为进程内保留，不能当作崩溃后的持久恢复。RCS超时/错配仍按原Unknown守卫，不自动重发或改写旧请求。

首配和日常参数继续编辑同一完整候选。填写实际IP和序列号；IP用于找到GigE相机，序列号核对物理设备，无TCP端口。旧TCP配置先点明确的SDK切换按钮。连续采集和逐次上报固定，无处理方式下拉框；相机页不再显示配置状态或稍后配置勾选。未接另一台时其IP、序列号均留空；部分填写报错，完整配置保存并受控重启后连接。RCS使用明确HTTP(S)地址，路径 `/rcs/sorter/api`，供包台编号、相机编码、仓ID和生产线ID沿现有合同配置；RCS自身可延后，缺失时拒绝业务。保存不连接设备。关闭MVS等占用程序，保证完整条码及两侧空白进入视野。

## 依赖和交付

自身版本保持1.0.0，安装包通过SourceCommit区分。MVS原生依赖沿现有 `--mvs-runtime` 固定目录、ACL、哈希及进程生命周期加载策略；`vendor/zxing-cpp/ZXing.dll` 另按嵌入清单核验，NuGet托管/原生闭包和Apache-2.0声明随离线介质交付。保留MvCodeReader旧路径，因此构建仍需原 `--mvreader-runtime`。厂商文件、字体、真实图像/条码/序列号/本机账本不进入Git或公开Release。

独立 `IpcContractPath` 冻结构建仍仅导出原MVS采集扩展，不含软件读码合同及ZXing依赖；软件识别属于当前内置组合。不能把独立采集模块当作新读码器使用。

## 用户指定的Python模拟RCS

模拟服务仅监听127.0.0.1，使用标准库；客户端无需安装Python。介质 `tools/rcs-local-simulator.py` 可用本机Python启动：

```powershell
python tools/rcs-local-simulator.py --port 18848 --data-root D:/ImporterPc.LocalValidation/rcs
```

服务地址为 `http://127.0.0.1:18848/rcs/sorter/api`，状态为 `http://127.0.0.1:18848/status`。数据根保存SQLite原请求/响应私账和endpoint.json。同码不同requestId仍各自记账、返回同一模拟任务/格口；同requestId报文冲突返回409。返回值带LOCAL_ONLY_NOT_REAL_RCS标记，不含真实上游业务接受或物理完成的含义。私有 `mode.json` 可选assigned、timeout、reject、wrong-id、malformed，分别用于正常、超时、拒绝、错配和错误形状测试；不能把模拟成功写成现场RCS验收。

## 已验证与边界

本机实际相机3072×2048 Mono8，原生MVS＋.NET软件识别＋Application/SQLite＋Python环回RCS：20秒51笔独立请求全部SlotResolved、0 Unknown，服务账本51个不同请求ID、1个模拟分配；另用受保护的随包原生目录及默认驱动工厂10秒21笔全部SlotResolved。真实图像、条码、IP、序列号、账本仅在D盘Git外。以上运行使用独立Simulation夹具及既有测试身份前置接口，不改正式权限守卫。

合成实际ZXing引擎验证同码三帧三份作业/HTTP请求、空白不建作业、原请求不改写、借帧归还和有序停止；既有驱动资源、原生参数和RCS恢复检查分别记录。本机Explorer继承提升权限的问题已补同用户普通令牌启动入口，并通过实际Windows子进程及原身份检查；原安装客户端同令牌启动成功，新冻结介质仍单独核验。正式模式继续要求原精确部署资格、明确配置和普通运行账号。安装器生成、介质依赖检查、Windows启动冒烟与实际现场RCS验收分别记录，不能用上述联调结果替代。
