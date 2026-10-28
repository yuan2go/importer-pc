# 海康当前读码接入设计

当前路线以[MVS-SOFTWARE-READER-01](../work-packages/WP-IPC-MVS-SOFTWARE-READER-01.md)、[相机参数与启动](../work-packages/WP-IPC-CAMERA-CONFIG-STARTUP-02.md)及[运行边界](../runtime/mvs-software-reader-01.md)为准：MVS SDK采图，在同一进程调用ZXing-C++软件识别，再进入既有Application持久输入与RCS请求链。用户已选定SDK路线，不引入TCP生产者、独立读码服务或未授权VM SDK。UI不拥有连接，不创建第二业务实例。F-02/03/05/09、独立Application及原D/RV保持。

每实例按IP及预期物理序列号枚举唯一设备，保留DeviceId→位置拓扑；Production启用要求预期序列号，独立模拟/诊断可省略。首次取得的物理身份在实例重启后仍固定，并使用同一海康GigE租约域防止模块别名双占。全局SDK初始化引用计数、枚举缓冲复制、独占打开及完整原生资源释放后Finalize的边界继承现有硬化，不抢占其他客户端或写设备IP。

软件读码路径明确设置并读回连续采集、关闭触发；默认 `read-result`，显式 `observe-only`仍只产生不可准入观察。纯MVS采集模块没有解码器时继续仅观察，不能报告业务Ready。不同SDK的ABI、回调和触发合同不互相冒充，不恢复旧PLC/UDP锁或Thread.Abort。

每个独立识别帧保留设备、epoch和完整帧来源，建立读码请求身份；同码多帧不由驱动合并，技术重投保留原身份。识别请求身份不证明物理包裹数量或完成。Application继续负责候选完整性、筛选、准入、资源、过载和原请求冻结；Unknown不盲重发，RCS响应与物理完成分别记证。

解码只接受当前已确认的紧密Mono8图像；未确认stride的填充负载不能通过截取前Width×Height字节冒充紧密图。原生帧归还后再调用软件解码和宿主交接。停止期间已经返回的非空识别结果若未交给Application，保留完整Undelivered及故障，阻断原实例重启，不静默丢弃或自动补发。取消等待不等于原生任务已结束，清理失败仍保留原资源责任。

当前SM首配和日常复用完整候选、CAS、Pending/Effective与受控启用；每台相机只有一个业务编码供取格口与心跳共用。SDK入口无需TCP端口，物理序列号不是第二个RCS编码。旧连接只能由用户明确切换，打开页面不改写配置；切换保留本相机业务编码、另一相机及原请求。左右可只启用一台就绪相机；空位置不启动、不接包，两台均未配置或RCS未配置时拒绝相关业务。

SDK精确API门禁0x04080201与受保护加载保留；[ABI与编码回归](../work-packages/WP-IPC-HIKROBOT-BOUNDARY-REGRESSIONS-01.md)的实际/预期版本诊断继续有效。DLL和支持文件按嵌入清单核对并持有，不从任意PATH加载。厂商日志使用进程级D盘目录；固定支持副本随精确构建核验，不改变全机环境或产品工作目录。自有版本保持1.0.0，SourceCommit/SHA256区分构建；厂商文件留Git外。

[MvCodeReader阶段原设计](../archive/pre-mvreader-restore-01/README.md)、[ABI回归前设计](../archive/pre-hikrobot-boundary-regressions-01/README.md)及[原01设计/证据](../archive/pre-hikrobot-sdk-barcode-02/README.md)保留原字节和适用范围。既有MvCodeReader有界候选、编码与回调隔离仍守自己的合同，不作为当前MVS软件读码的现场证明。模拟、实际Windows内核、安装冒烟、真实SDK/相机/RCS和现场分别记证；原PASS不能转记到新整合树。
