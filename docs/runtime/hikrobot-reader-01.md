# 海康读码 SDK：安装、参数与诊断

当前工作包：[MVCODEREADER-RESTORE-01](../work-packages/WP-IPC-MVCODEREADER-RESTORE-01.md)。自有版本 1.0.0，Windows x64。用户确认同一台现场相机的 Sportmaster/MvCodeReader 和天猫/MVIDCodeReader 两条旧路线均能读码，本版优先复用现有 `Ipc.Drivers.Hikrobot`。旧 MVS 采图说明及原验证保持[归档身份](../archive/pre-mvreader-restore-01/README.md)，不把它作为本版必须新增算法 SDK 的依据。

## 流程和参数

内置 `hikrobot-idmvs` 使用 MvCodeReader 2.0.0.4 的真实 GigE 枚举、CreateHandle、OpenDevice、Ex2 条码回调和 StartGrabbing。IP 与预期序列号唯一匹配，不按枚举下标或同型号第 0 台回退。回调复制完整有界候选及触发/帧/通道/时间身份，原始图像指针不进入 UI；宿主队列交给唯一 Application，复用条码筛选、SQLite 输入与作业账、RCS 格口申请、原请求冻结和 Unknown 不重发。

用户最终纠正：现场没有 PLC，电脑直接接相机；每次识别结果均上报 RCS，由 RCS 去重，PCS 是口误。默认 `acquisition.mode=continuous`、`entity.mode=read-result`。打开设备后、开流前关闭 TriggerMode，使用 SDK 计算的 GigE 包长并读回模式；不要求 Line 或 PLC，不发送软件触发、不改 IP、曝光或码制。每个独立识别帧形成独立请求，同码不同帧也上报；原帧完全重投保留原请求身份。帧身份包括设备/连接代次/帧来源，表示读码请求，不作为物理包裹数量或完成证据。

`existing` 沿用设备设置；`observe-only` 只观察，`external-trigger + trigger` 为其他已确认触发现场保留，不是本现场默认。MVIDCodeReader 的算法句柄/绑定/轮询与当前 SDK 不是同一 ABI，本版未复制天猫的 PLC/UDP 解锁、CODE128/长度限制、按型号回退第 0 台或 Thread.Abort。完整候选仍由既有业务筛选；不会把无码或多码未通过筛选伪造为成功上报。

首配与日常参数使用同一完整候选，显示内置读码 SDK、IP、正式必填序列号、采集模式、实体关联、编码和缓冲。每台只有一个 RCS 相机编码，取格口与心跳共用。相机/RCS 可明确延后；一台就绪相机只服务对应投递位。保存不连接设备，Pending 经受控重启才生效。现有 MVS 候选需在原参数界面明确切换并重置连接参数，复核 IP/序列号后保存；没有静默转换、自动回退或第二配置账。

## 离线依赖

构建侧通过 `package-application.py --mvreader-runtime <已核查目录>` 提供实际运行依赖，同时沿既有 `--mvs-runtime` 提供保留采集模块的依赖。目录须包含嵌入清单规定的 32 个原生 AMD64 DLL、3 个 SDK 支持配置文件及 SDK-NOTICE.txt；全部文件按长度和 SHA256 校验。构建只对已固定来源哈希的 log4cxx.properties 日志目标作确定变换；原 SDK 不改写，目标字节再次按嵌入清单校验。SDK 从工作目录读三个支持文件，故交付根/app/desktop 均保留受保护固定副本；加载前核验并持有，使用标准安装快捷方式启动。日志目录通过本进程变量指向供包台数据根，不改全机环境或产品进程工作目录。运行时沿受保护路径、ACL、全目录和同名已加载库检查，DLL 与配置保持至进程退出。厂商文件不进入 Git 或公开 Release，不修改全机 PATH。CI 无 SDK 时只能显式生成验证介质。独立业务资格、正式物理身份及原权限守卫继续有效。

## 复测和现场诊断

开发侧先执行 `. ./scripts/project-environment.ps1`。构建 `tests/Ipc.Application.Verification` 和 `tests/Ipc.Drivers.Hikrobot.Probe`，独立 Probe 的 `IpcContractPath` 指向同源冻结的 Ipc.Platform.dll，均使用 Release、RestoreLockedMode=true 和 -m:1。运行 Probe `--self-test`，Application 验证器运行仓库目录加 `--mvreader-flow`。原生参数入口为 `Ipc.Desktop.UiVerification` 的仓库目录加 `--hikrobot-sdk-editing`。

现场先关闭占用相机的旧程序/设备工具，使用交付的 vendor/hikrobot-idmvs 目录和相机 IP（已含经核验的日志配置）：

```text
Ipc.Drivers.Hikrobot.Probe --list SDK_DIRECTORY
Ipc.Drivers.Hikrobot.Probe --connect-triggered SDK_DIRECTORY CAMERA_IP 15
Ipc.Drivers.Hikrobot.Probe --connect-continuous SDK_DIRECTORY CAMERA_IP 15
```

框架依赖型工具也可通过 dotnet 加对应 DLL 运行。`--list` 只加载及枚举，不打开设备；continuous 关闭触发，用于本现场连续读码检查；独立诊断工具本身不创建作业。triggered 仅用于另有外部触发的现场。诊断仅计数帧和候选，不输出真实条码、不请求 RCS；零码退出 3，真实候选退出 0，故障退出 1，整体超时退出 124。诊断后的模式仍需按现场设置核对，不能将连续采集诊断当作正式供包。

工程验证、原生参数、SDK 加载枚举、真实条码、逐次结果上报和真实 RCS/现场分别记录。当前机器只有 WLAN，真机识码仍待现场联调，不能由旧程序成功或本版合成测试转记为通过。

本机已通过同码三帧同时进入实际驱动/Application/SQLite/环回 RCS 并形成三次请求的合成验证，原帧重投不增发。该测试采用独立 Simulation 夹具，不代替 Production 部署资格、普通账号或现场 RCS 验收。正式模式缺少现有精确部署资格仍拒绝业务，不能从本轮驱动接通推定资格自动具备。
