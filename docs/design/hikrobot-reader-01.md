# 海康当前读码接入设计

2026-10-28 当前入口：[MVCODEREADER-RESTORE-01](../work-packages/WP-IPC-MVCODEREADER-RESTORE-01.md)、[运行与诊断](../runtime/hikrobot-reader-01.md)。旧 MVS 采图设计和 SDK/失败记录保留[原字节](../archive/pre-mvreader-restore-01/README.md)。

用户确认同机旧客户端的 MvCodeReader 和 MVIDCodeReader 路线均能读码，且现场有一包一次的触发信号。本版复用现有 MvCodeReader 适配器，内置装配与独立签名扩展共用同一实现。路径为 IP/物理序列号绑定、真实 SDK 设置与读回、Ex2 回调、完整候选复制、有界队列、Application 筛选及准入、持久化请求与现有 RCS。UI 不拥有连接，不引入第二业务实例或算法服务。

采集模式和实体身份分开。外部触发只改 TriggerMode 和 SDK 计算的 GigE 包长，实际触发源沿设备已有配置核对。作业按连接代次/通道/触发计数关联，不按条码或每帧建立新实体；同触发重复帧不重发，新触发同码仍独立。连续采集只用于不可准入的诊断。资源失败、未知触发责任、配置及权限守卫不能由恢复按钮或换页面清除。

MVIDCodeReader 的采图加算法、MVS 的仅采图与 MvCodeReader 的条码回调分别保留实际能力和 ABI。型号不匹配不回退第 0 台，不复制旧天猫业务的 PLC/UDP 锁、硬编码码制、丢帧节流及 Thread.Abort。新观察沿现有持久输入责任和业务筛选，不因 UI 或日志刷新静默丢弃。

所有 DLL 与 SDK 支持文件由嵌入清单固定字节，沿受保护加载边界在进程期保持，禁止任意 PATH 加载。初始化、停止、回调、物理租约及失败重试继承现有加固。自有版本保持 1.0.0，SourceCommit/SHA256 区分构建。厂商文件留 Git 外；用户确认旧程序可读不等于本版现场验收。
