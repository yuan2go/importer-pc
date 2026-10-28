# MVS-SOFTWARE-READER-01

2026-10-28，继承 MVCODEREADER-RESTORE-01 的实际源码和历史证据。用户明确授权改为已真机验证的 MVS 采图＋软件识别，所有独立识别结果交 RCS 去重；本轮 RCS 采用用户指定的本机 Python 模拟服务，不代表真实现场 RCS 验收。

复用既有 MvsAcquisition 的唯一设备生命周期、独占租约、缓冲归还、取消及停止责任，使用官方 ZXingCpp 0.5.3（内核3.1.1，与诊断相同）在进程内识别完整候选。每次有效识别形成读码请求，不按条码合并；识别请求不等于物理包裹数量或完成。原图只在受界定的识别调用中借用，主流程不传图，不引入Python运行依赖或第二作业框架。

连续视频中未识别的原始帧与已识别输入分别计数；允许解码处理能力造成的原始视频帧采样间隔，明确计数，禁止静默丢弃已识别结果。识别候选触及上限、源计数倒退、过载或异常明确阻断；宿主持久输入在释放原生帧后执行。保留既有筛选、原请求冻结、Unknown不盲重发、断网和恢复守卫。

D-MVS-SOFTWARE-01：本轮用户批准MVS＋软件识别及RCS去重口径；不构成物理完成、真实RCS或正式部署资格批准。新路径实际验证及安装包SourceCommit/SHA独立记录；MvCodeReader旧路径的参数错误/无帧结果、此前介质和合成PASS保持原范围。相机图像、条码、序列号和本机服务请求仅存D盘Git外，公开只记录合成数据和不含现场身份的结果摘要。


当前运行和依赖说明见 [mvs-software-reader-01](../runtime/mvs-software-reader-01.md)。

- RV-MVS-SOFTWARE-01：PASS，实际ZXing引擎＋合成MVS端口＋Application/SQLite/HTTP，同码三帧三份原请求，空白帧无作业，持久输入拒绝/抛异常保留原结果并拒绝盲重启，缓冲先归还、资源有序停止。
- RV-MVS-SOFTWARE-02：PASS_SINGLE_DEVICE_SIMULATED_RCS，真机20秒51笔作业全部SlotResolved、0 Unknown；服务51个独立请求ID、1个模拟分配。受保护随包原生目录和默认工厂另10秒21笔全部SlotResolved。独立Simulation身份前置接口，不是Production资格。
- RV-MVS-SOFTWARE-03：PASS，原生WPF首配/日常参数同候选、左右独立、可见IP/序列号/触发要求及紧凑窗口；示例身份均为合成。
- RV-MVS-SOFTWARE-04：PASS，既有驱动46项自检、RCS超时/响应错误/重启Unknown原请求不重发检查、参数延后和单相机守卫；本地服务协议/冲突/去重/错配/拒绝/形状检查通过。
- RV-MVS-SOFTWARE-05：BLOCKED_ENVIRONMENT，默认身份守卫在当前提升权限账号按设计拒绝启动；全部RCS回归在进入未注入Simulation前置接口的运行时夹具时同样被阻断。适配器协议部分及独立RCS错误/恢复套件已验证，不伪造整套PASS。未改Windows账号、全机UAC或正式权限。
- RV-MVS-SOFTWARE-06：安装器和冻结介质生成、依赖核对及启动结果单独随实际SourceCommit交付；普通账号正式客户端和真实现场RCS仍不由本轮模拟验收替代。
