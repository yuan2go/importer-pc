# 海康 SDK 当前接入与诊断

当前任务及验证状态见[SDK-BARCODE-02](../work-packages/WP-IPC-HIKROBOT-SDK-BARCODE-02.md)，并发释放、物理身份及诊断保护增量见[HARDENING-01](../work-packages/WP-IPC-HIKROBOT-HARDENING-01.md)。本文件定义当前操作；旧驱动冻结介质、0帧/0码、原PASS/FAIL及SDK1.0.x步骤已按原字节[保留](../archive/pre-hikrobot-sdk-barcode-02/README.md)，不能转记当前源或目标型号。

## 设备、采图与识码

目标是MV-PD010003-06M-12C在Windows x64下通过SDK取得真实条码。当前已核查本机SDK头文件、官方示例、托管布局和运行库：

| 当前模块 | 实际职责与状态 |
|---|---|
| `hikrobot.mvs-acquisition/mvs-acquisition` 1.0.0 | 目标标准相机的实际MVS GigE采集。SDK函数返回4.8.2.2，DLL文件版本4.8.2.1；只支持此当前ABI。枚举、按IP/可选序列号独占打开、读取触发/Mono8/PayloadSize、有限SDK节点、GetImageBuffer/FreeImageBuffer成对取图、连接诊断及停止已实现。尚缺适用条码识别组件；不生成观察/条码/作业，健康保持Unknown（无业务有效期）。 |
| `hikrobot.idmvs-reader/idmvs-reader` 2.0.1 | 既有智能读码器MvCodeReader 2.0.0.4回调条码链。保留实际使用场景，不当作目标标准型已获支持的证据。回调之外消费，观察队列条数加字节预算。 |

两者是当前实际SDK模块，不能互为旧版本回退，不做TCP参数转换或旧配置兼容。目标标准型当前采用MVS采集链，补入适用识别组件后才能完成条码链。MVS `MV_CC_HB_Decode`仅把无损压缩流解压为像素；没有条码含义。客户端手册没有某个SDK名称不能证明组件不存在，也不限定识别组件必须叫某个名称。

## 参数与受控启用

管理模式→参数设置→驱动→左相机/右相机，与独立首配共用完整候选。仅显示精确安装模块实际schema：

- 两路分别填写`device.ip`，Production正式启用必须填写`device.serial`，左右不得绑定相同序列号。IP只作连接地址，物理身份固定到唯一完整候选；更换相机须审阅并受控启用新候选。Simulation/独立诊断可省略预期序列号，但同一源实例Stop/Start仍固定首次取得租约的实际序列号，这不提供跨进程保证。明确延后项可稍后配置且拒绝业务。实际型号/固件/序列号另以设备标识核对。
- MVS的`acquisition.trigger=existing/on/off`分别为沿用/要求已开启/要求已关闭，只读取并校验设备状态，不改触发、不发软件触发。`entity.mode`固定仅观察，正式作业不可选。现阶段没有有效的条码编码参数，不能把图像像素当作条码字节。
- MVS `image.nodes`默认3、范围1～4；设置SDK输出队列为OneByOne，不选择丢旧取新。SDK内部另有其预分配，不能声称这就是进程全部内存。`image.maximum-payload-bytes`默认8MiB、范围1～32MiB；打开及每帧校验负载。应用每路至多借用一个SDK图像缓冲，不建立原图副本/存图/上传队列。丢包、序号断档/重置及格式不符故障围栏。
- MvCodeReader的`entity.mode=observe-only/trigger`、严格`code.encoding`、条码观察`queue.capacity`默认128沿原用途；新增`queue.maximum-code-bytes`默认16MiB、范围1～64MiB，覆盖排队及正在处理的候选字符串和条目预算，不是原图预算。trigger仍须已有TriggerMode=On及可信实体/重连处置证据。
- 心跳`bcr-code`与本位取格口编码分别维护，统一供包台编号继承当前系统候选。SDK页没有TCP端口、TCP keepalive或入站时限。

选择其他连接方式先显示重置后果；“取消切换”保留原字段，“切换并重置参数”清空本实例参数并采用新schema默认值，须重新填写实际连接和心跳编码。另一相机及RCS取格口映射保留，不自动恢复旧提供者参数。未决定的选择阻止保存，首配/日常加密草稿保留此状态。整个编辑的取消沿既有配置账和未保存草稿流程。

保存通过原完整候选、静态校验、审阅、CAS、Pending/Effective及受控重启。保存、连接、识码和业务准入分别核对；普通重连不应用Pending。可明确延后连接；延后项不启动，正式业务保持阻断。缺识别组件、真实实体边界、必需能力或Production资格时不得借采集成功解除业务阻断。

## 独立诊断

Windows命令先运行项目D盘环境脚本。构建与目标冻结宿主相同的公共契约：

```powershell
. ./scripts/project-environment.ps1
dotnet build ImporterPc.slnx -c Release -p:BusinessModule=Sportmaster -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -p:IpcContractPath=D:/frozen-host/Ipc.Platform.dll -p:RestoreLockedMode=true -m:1 -nr:false
# 以下从Probe输出目录执行；地址均为文档示例，替换为现场配置。
dotnet Ipc.Drivers.Hikrobot.Probe.dll --mvs-list D:/installed-vendor-sdk/MVS-x64 --timeout-seconds 15
dotnet Ipc.Drivers.Hikrobot.Probe.dll --mvs-connect D:/installed-vendor-sdk/MVS-x64 192.0.2.10 10 --timeout-seconds 40
# 已有智能读码器模块的独立诊断：
dotnet Ipc.Drivers.Hikrobot.Probe.dll --list D:/installed-vendor-sdk/MvCodeReader-x64 --timeout-seconds 15
dotnet Ipc.Drivers.Hikrobot.Probe.dll --connect D:/installed-vendor-sdk/MvCodeReader-x64 192.0.2.10 10 --timeout-seconds 40
```

MVS目录必须包含MvCameraControl.dll、MVGigEVisionSDK.dll及同版完整依赖；MvCodeReader目录使用原主DLL和签名Preload声明。只按完整路径加载、文件保持锁定，不改PATH/当前目录或全机网络。单独工具不调用RCS、不输出条码或保存图像；枚举输出含现场身份，只留D盘Git外。现场独占维护窗口内先停原客户端/MVS/IDMVS；独立工具的临时设备锁不代替日常宿主机级租约，底层独占打开继续检查设备占用。

`--timeout-seconds`覆盖加载、初始化、枚举、打开、接收及停止的整体诊断预算（1～120秒；默认采集秒数+30秒）。PHASE记录阶段和单调耗时。达到期限立即报告OVERALL_TIMEOUT与当时原生阶段、取消后续工作；仍在执行的原生调用/句柄/缓冲及设备owner保留到实际结束，进程可能继续等待，不以托管取消冒充原生终止。Stop失败保留原owner，输入`retry-stop`只重试同一个停止；无stdin不自动释放。人工终止进程需核对设备占用与原实体，不能把终止当作干净停止。

退出码0表示所请求的枚举/诊断正常结束，不单独证明读码；1表示失败；124表示整体期限到达。MVS采集窗口正常结束返回3，明确表示仅采图、条码识别组件不可用。即使有帧也不报告已识码；0帧记录“未见触发/帧”，不据此推断设备不支持。健康Source含阶段、帧/字节计数、DecoderUnavailable及故障原因，不含图像或条码。

## 包、安装与边界

两种SDK源的Dispose请求立即围栏新启动和读码回调，实际清理与Start/Stop串行；重复Dispose加入原任务，失败后可重试原清理阶段。停止健康通知异常不阻止可执行的清理，完成后仍明确报错；原生清理失败继续持有句柄、委托及租约。业务日志遮蔽条码字段、候选、嵌套及截断诊断片段；原始业务账/请求/回执保持不变。

使用当前`src/Ipc.Drivers.Hikrobot.Mvs/extension.build.json`或既有MvCodeReader声明构建独立精确签名包；不把SDK放入ipcx、公共仓库或普通安装包。新MVS工程包版本1.0.0，原智能读码器当前2.0.1；当前精确组合声明已同步。新的Platform指纹/源变化须针对实际冻结宿主重建/签名，不复用旧包、旧资格或旧身份。

沿用`scripts/build-extension.py --definition <当前声明> --version <当前版本> --contract <冻结宿主/Ipc.Platform.dll> --check-host <冻结宿主/Ipc.Assembly.dll> --output <新包.ipcx> --key <Git外私钥> --publisher <受信发布者> --trust <独立信任>`。源码先提交且干净。工程TestOnly签名只能用于Simulation。Production声明沿`extension.build.production.json`，仍须真实独立发布者、精确签名部署资格及原有受保护SDK文件政策；本包不生成或虚构Production批准，MVS采集源本身也不发布业务Ready。

SDK目录全部DLL文件按绝对路径/hash绑定并锁定；只按声明预载动态GigE依赖和主DLL，不逐一初始化全部库。原生DLL保持至进程退出，受控重启才能更换。Windows x64、机级受保护D盘、指定账户、ACL、完整依赖、装配与NativeSdkBoundary守卫均继承。公共参数模板不含现场IP/序列号，当前同版安全设置模板也不携带站/设备/目标/凭据。

## 尚缺的识别资料与现场验证

请海康或供货商提供适用于目标套件及其实际内置工业相机/固件的Windows x64条码识别组件：准确名称与版本、运行库及全部依赖、接口文档/头文件、由采集到完整条码候选结果的官方示例、输入像素格式与结果编码、缓冲所有权/释放及并发约束、必要许可/加密狗/激活和部署要求。名称不限；不从博客猜接口，不以空解码器或模拟码占位。

工程构建/定向模拟、实际SDK初始化与枚举、真机帧/读码、RCS闭环与现场可用分别记录于工作包。真实左右读码、可信触发实体、真实RCS及现场条件仍BLOCKED/NOT_RUN；原D/RV和历史验收不变。软件暂停不是安全急停。
