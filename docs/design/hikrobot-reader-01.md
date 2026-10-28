# 海康当前SDK接入设计

当前装配及参数以[BUNDLED-MVS-CAMERA-01](../work-packages/WP-IPC-BUNDLED-MVS-CAMERA-01.md)、[单相机规则](../runtime/station-single-camera-recovery-01.md)及[操作/依赖说明](../runtime/hikrobot-reader-01.md)为准。SDK采集实现沿用[SDK-BARCODE-02](../work-packages/WP-IPC-HIKROBOT-SDK-BARCODE-02.md)和后续硬化；F-02/03/05/09、独立Application及原D/RV保持。本次[边界回归](../work-packages/WP-IPC-HIKROBOT-BOUNDARY-REGRESSIONS-01.md)只修诊断、当前指引和测试，不改变RCS合同、数据库或物理完成语义。[清理前设计](../archive/pre-hikrobot-boundary-regressions-01/README.md)及[原01设计/历史证据](../archive/pre-hikrobot-sdk-barcode-02/README.md)保留原身份。

目标标准型相机当前接入本机实际MVS x64采集接口；尚缺适用条码识别组件，名称不预定。客户端手册不能代替SDK头文件/示例/运行库证据；HBDecode只是图像解压。已有MvCodeReader模块服务其实际智能读码器场景，不是标准型设备的旧版本回退，也不据此宣布目标支持。

一个当前MVS实例按IP及预期物理序列号枚举唯一设备，保持原DeviceId→左右位拓扑。Production启用须配置预期序列号；仅Simulation/独立诊断可省略，首次取得的物理身份在同一实例重启后仍固定。实际序列号采用同一海康GigE租约域，避免SDK模块别名双占。SDK全局初始化有引用计数，全部设备资源实际释放后才Finalize；枚举静态缓冲串行并复制描述符。打开独占，不抢占其他客户端，不写IP/曝光/码制/触发、不发软件触发。配置只校验既有触发要求和Mono8/PayloadSize。

每路一个后台采集所有者，使用有期限GetImageBuffer及成对FreeImageBuffer；最多一帧借用，无托管原图队列、无每帧Task/图像保存/上传。SDK节点数和单帧负载有界，OneByOne顺序；丢包、序号断档/反向、错误像素/尺寸/长度均故障围栏，普通无触发不等于断线。健康核对原IP/序列号及设备连接；缺识别组件时所有状态都没有业务有效期，不构造假条码或观察，不为每帧建包裹。

取消只结束调用方等待；实际采集/检查完成前不Close/Destroy/Finalize或释放物理owner。Free失败保留原缓冲，Stop重试先归还同一借用再停流；Stop/Close/Destroy/Finalize失败均保有未完成阶段及设备租约，禁止第二Start。宿主健康处理在原生借用归还后调用，不持元数据锁。SDK无图像回调，避免把业务/UI放入SDK线程。

既有智能读码器回调模块继续严格复制完整有界候选，Application拥有筛选、实体、RCS及格口事实；既有条码字节预算及单独异步故障发布保持，宿主健康处理不阻塞原生回调。其固定ABI、编码和触发合同不自动适用于标准型相机。目标识别组件取得后，按真实缓冲/并发/触发合同接入已存在观察链；当前不编造通用解码接口或临时算法实现。真实条码、包裹关联和现场放行是后续外部条件，当前不能宣布完成。

当前SM首配和日常直接使用内置MVS，复用原共模、完整候选、CAS、Pending/Effective及受控启用；不提供TCP切换、端口或独立驱动安装流程。每台相机只有一个业务编码，取格口与心跳共用，物理序列号不是第二个RCS编码；不展示尚无实际消费者的解码字符集参数。SDK精确API版本为0x04080201，入口DLL文件版本4.8.2.1，按已核查14库清单内置，继续受保护加载和原请求冻结守卫，不增加兼容转换、回退或版本矩阵。

左右可仅启用一台实际就绪相机服务对应位置，未配置位不启动、不接包；两台均未配置或RCS未配置仍拒绝业务。此规则不把采图当识码：当前MVS缺正式解码组件，仍仅观察、无业务Ready、无条码作业。SDK依赖、识码合同、实体关联及现场验收分别取得证据。
