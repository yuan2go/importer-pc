# WP-IPC-HIKROBOT-SDK-BARCODE-02

本文件保留原要求与原始验证身份。当前操作只读取[海康运行说明](../runtime/hikrobot-reader-01.md)，不将本文件的旧版本、SDK判断或页面要求用作当前配置。

2026-10-27；起点最新 main `52c7e58d87a5bfe452e77f22b543e1ee0cb124dd`。用户授权当前版本真实 SDK 读码、参数/首配、诊断与必要验证。原工作区未跟踪文件保留；PR125仅构建/station等另行修改，本轮独立 D 盘工作树，不合并或覆盖该分支。

## 原工作包要求（形成时范围）

复用既有四层架构、唯一完整候选、首配/日常共模、CAS、Pending/Effective及受控启用。左右物理身份按IP及可选序列号绑定，不按枚举下标。仅观察不创建作业，正式供包必须有可信包裹边界。同触发多帧不得变成多件、同码新实体仍独立、左右各自隔离。原RCS请求固定原报文/合同/目标，Unknown不自动重发。相机编码按CAMERA-SDK-SINGLE-CODE-01统一为本位唯一映射，取格口和心跳共用；统一供包台编号继承PARAMETER-GUIDANCE-STARTUP-01。

本轮允许修改UI与当前参数，不做旧配置/数据库/SDK迁移、TCP↔SDK转换或多代SDK适配。保留当前TCP模块的实际调用方。更换提供者重建所选schema默认项，不迁移驱动专属值；明确告知重置，取消切换保留本位输入，整份编辑取消沿既有草稿/配置账。SDK表单仅使用实际安装描述，不添加虚构端口、网卡或算法参数。保存不连接设备，普通重连只使用Effective。

原阶段设计/运行说明与参数快照见[编码前原件](../archive/pre-hikrobot-sdk-barcode-02/README.md)。旧“不改UI”、历史兼容/迁移目标不限制本任务；原SDK版本、0帧/0码、旧PASS/FAIL及D/RV不倒写。

## 原SDK核对证据

- 本机MVS的Development文档为工业相机Windows SDK V4.8.1。真实头文件`MvCameraControl.h`提供`MV_CC_GetImageBuffer`/`MV_CC_FreeImageBuffer`成对采图等接口；`MV_CC_HB_Decode`用于无损压缩流解压成原始图像，不是条码识别。Development的Includes、DotNet、Samples、Bin以及Applications未找到MVIDCodeReader算法接口或开发包。
- IDMVSSTD的Development.zip只包含`Modules/MvCodeReaderSDK`。官方`Modules/SDK Introduction.txt`独立列出MvCodeReaderSDK（智能读码器）、MVIDCodeReader（工业相机的一维/二维码及抠图算法）与MvLogisticsSDK，但本机压缩包未附后两者。
- 当前驱动仍是`MvCodeReaderCtrl.dll`精确2.0.0.4 ABI；SDK配置的CameraSeries列出智能读码器系列，不含本任务标准型MV-PD010003-06M-12C。这不等于已验证该设备不支持任何SDK，也不能靠改DLL名/放宽枚举名单获得真实条码。当前驱动没有MVS采图或PC算法调用，不能声称目标链路完成。
- 当前电脑仅有无线/虚拟网络，未发现用户现场双相机网卡/设备连接。现场地址、序列号、条码及原厂文件只留Git外，不写公共模板。

本轮用户追加纠正：客户端用户手册中搜索不到名称不能证明SDK不存在。MVIDCodeReader只是本机介绍文本中列出的组件名称，不是唯一必选依赖；不再按该名称反复搜索。依据当前实际开发包，MVS采集链路使用真实MV_CC接口继续实施；当前已核查的MVS/IDMVS头文件和示例尚未提供适用目标标准型相机的条码结果组件。准确缺项是“尚缺条码识别组件”。

需海康或供货商提供：适用于MV-PD010003-06M-12C及其实际内置相机/固件的Windows x64组件名称和版本、真实运行库及依赖、获取完整条码候选结果的接口文档和头文件、采集到条码结果的官方可运行示例、支持像素格式/码制/字符编码/结果缓冲所有权和并发约束、必要许可/加密狗/激活及部署条件。取得正式组件后按真实接口实施，不限定组件名称。设备身份/固件和现场触发实体证据另外核对。

未取得真实识别组件期间，采图会计数并正确归还原生缓冲，明确标记DecoderUnavailable、不生成假条码或供包作业，也不发布业务Ready；采集连接、真实条码与现场可用分别计证。

## 原实施与验证回执

当前工程增量：

- 目标走工业相机SDK路线。新增独立精确包`hikrobot.mvs-acquisition` 1.0.0，实际调用本机MVS采集接口；该包当前没有条码识别能力，不把既有MvCodeReader当目标默认或回退。没有创建空解码器、模拟码或逐帧业务输入。
- 新MVS实例完整枚举/IP和可选序列号绑定、物理独占、SDK初始化引用计数、Mono8/触发/负载核验、OneByOne有界原生节点、每路一帧借用及成对归还、连接/序号诊断、停止/重试及失败保有；缺组件始终不发布业务Ready。SDK函数实测版本4.8.2.2（0x04080202），实际DLL文件版本4.8.2.1，均独立记录。
- 保留实际智能读码器当前模块并升2.0.1，补条码队列字节预算、原生回调之外的单个故障发布任务，以及启动失败后残留队列清算。SDK模块共享海康GigE物理序列号租约域。
- 首配和日常参数统一新增切换提案/明确重置/取消；选择尚未确认时阻止保存并保留加密草稿。SDK只展示实际schema，MVS仅显示采图所需字段及缺组件状态，左右参数与两种RCS编码分别保持。
- Probe整体预算覆盖SDK加载/启动/采集/停止；超时报告当前原生阶段并保留实际owner，不把取消当完成。MVS正常采集诊断退出3表示识别组件缺失。使用当前签名/受保护原生依赖/完整候选/CAS/Pending/Effective链，无旧版转换或降级。

必要验证结果（源码提交及冻结包回执在后续附录精确补录）：

| 范围 | 已执行结果及边界 |
|---|---|
| 工程构建 | Sportmaster全Solution指定BusinessModule=Sportmaster、SelfContained=false、locked构建通过，0警告/0错误；独立SDK Probe构建通过。 |
| 驱动/资源 | MvCodeReader定向46断言、MVS采集26断言及两种原生签名/缺资格拒绝组通过。覆盖完整候选、同触发、多实体、队列/字节过载、迟到回调、阻塞宿主健康、Free/Stop/Close/Destroy/Finalize失败保有和实际停止后释放。均为显式合成原生端口，非真机读码。 |
| 当前参数/业务守卫 | `--hikrobot-sdk-editing`、`--parameter-refactor`、`--parameter-guidance-startup`、`--configuration-confirmation`、`--sportmaster-rcs`、`--rcs-timeout-errors`通过；装配策略15项通过。 |
| 延后/原请求 | 首轮`--deferred-connections`在全量重连断言失败，诊断轮在Unknown/Pending下重连断言失败。定位到健康监测仍在保留服务调用时，真实维护守卫合法拒绝。夹具使用既有BeforeMonitorProbe在真实预留前排程并等待实际排空，再断言维护成功；未改生产守卫、未接受失败状态、未重试Unknown。修复后定向通过，原失败日志Git外保留并登记哈希。 |
| Windows原生 | HWND/Dispatcher实际参数交互通过：明确切换/取消、字段替换、左右隔离、非法IP保留/定位、MVS仅采图状态、首配共模；1120×760与820×520 DIP，实际125%缩放。合成安装目录描述，无SDK/真实RCS/安装/UAC/目标工控机验收。 |
| 真实SDK | 本机当前MVS完整路径加载、17个实际导出检查、GetSDKVersion、Initialize、EnumDevices、Finalize成功；GigE设备0。MvCodeReader实际加载/枚举成功，设备0。这只证明本机SDK加载/枚举，不能记真机帧或条码通过。 |

运行与依赖方法见[当前SDK操作说明](../runtime/hikrobot-reader-01.md)，实际ABI和依赖声明见两个驱动目录的sdk-boundary/manifest/build/schema。新增必要CI验证驱动与原生编辑；原仓库必需门禁保持，远端结果分别记录。

工程实现：当前可做范围已完成，采集可构建/配置/联调；完整目标中的真实图像→条码组件接入仍BLOCKED，不能宣布整个工作包完成。工程验证：上述范围PASS，失败前样本保留。真机左右帧/真实条码/可信包裹关联：本机NOT_RUN/BLOCKED；用户已提供的现场MVS成功取流仍属用户现场证据，不转记本机或本源码读码验收。真实RCS/格口全链与现场可用：BLOCKED/NOT_RUN。

需海康或供货商落实上列适用识别组件资料及必要许可后，按真实接口完成实际解码，再进行授权现场双机读码、同触发多帧/同码新包/左右同码、失联/过载/恢复及RCS格口核对。不无限追某个组件名称，也不以连接成功、采图或模拟结果替代识码。SDK二进制、现场配置、凭据和私密输出不提交，Production资格/权限/原请求守卫不降低。


整合收口：开发起点`52c7e58d`，实施源码`9bfde2eb`；工作期间PR125进入main后已合并最新`36c55a58`，整合树`154fce47`。参数/延后连接及驱动定向重新通过，整合Sportmaster构建0警告/0错误。原生首配`--startup-experience`也通过；首次执行少传IPC_FROZEN_DESKTOP、独立驱动冻结构建首次少传IpcContractPath均被原守卫明确拒绝，补齐实际冻结路径后通过，未降低守卫。

最终资源审查补强宿主健康通知异常隔离：通知失败明确报错，仍完成应有原生清理；原生清理失败仍保有原owner。增加2项定向断言，MVS当前26项。新增Windows CI沿用最新main已有长路径/保留文件名checkout设置，原门禁未替换。证据摘要/私有日志哈希见[runtime/evidence](../runtime/evidence/hikrobot-sdk-barcode-02/verification.json)。SDK枚举首次曾把DLL文件4.8.2.1误当API版本，校核MV_CC_GetSDKVersion后按真实4.8.2.2固定ABI，原失败仍保留；不是新增旧SDK兼容。


冻结工程包：资源补强源码`443f75b1`，冻结宿主`154fce47`。MVS采集1.0.0与智能读码器2.0.1的工程TestOnly签名包均通过PASS_FROZEN_HOST_EXTENSION_CHECK，宿主及检查器字节未变；精确源码/契约/包/manifest/payload指纹见[冻结包回执](../runtime/evidence/hikrobot-sdk-barcode-02/frozen-packages.json)。这些是Git外D盘的Simulation工程包，不是现场安装介质、Production资格或真机读码证明。原9bfde2eb/整合前测试与包不倒写为本次冻结包。SDK/私钥/信任文件及二进制未进入Git。
