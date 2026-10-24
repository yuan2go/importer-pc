# Sportmaster 双相机通信与现场诊断

> 2026-10-24 PR93已合并后的首次配置CI回归，见[续修与验证身份](validation/dual-camera-first-configuration-ci-01.md)。首配和运行参数现在按当前目录解析无版本字段的内置读取器；扩展包精确身份、配置账和原请求责任不变。下述原包局部PASS不扩张为PR93失败CI的完整交付PASS。

本包基线 main `165c2ffe353c59e69729ede4c92fcfd827344c9f`，任务[WP-IPC-DUAL-CAMERA-COMMUNICATION-FIX-01](../work-packages/WP-IPC-DUAL-CAMERA-COMMUNICATION-FIX-01.md)。本轮只进行Windows隔离合成/loopback验证与本机只读网络元数据采集；未连接生产相机、调用真实RCS、改现场网络或安装介质。历史D/RV、旧安装包和原验收不变。[修改前TCP说明](../archive/pre-dual-camera-communication-fix-01/README.md)保留原字节。

## 已证实与实施

1. **首帧竞态已隔离复现。** 原Application在Reader启动、能力探测和配置确认后才开放入队，TCP连接后立即读码，合法首帧会被误判为入队拒绝并断线。现使用同一按位有界队列：启动前仅开放接收，消费者等待启动终态，`SharedReady`仍要求完整运行态。启动确认前不准入、不生成请求；失败/取消后排空为`StartupIngressBlocked`持久审计，要求核对原输入，不自动补发。队列满沿用独立过载标记、位围栏与人工检查，未改成静默丢弃。
2. **单侧连接失败原本终止整个Application启动。** 驱动已围栏并清理的ObservationConnectionException（TCP连接IO/Socket/Timeout）现在保留该位Faulted并继续启动独立Reader；另一位仍须通过自身与共享能力全部守卫。配置、SDK装配、存储或公共能力失败不被当作可忽略相机问题。配置激活确认的是已选运行修订和资源启动终态，不代表全部相机/业务就绪；快照中的位健康与Availability继续决定业务。需要旧账人工确认的配置仍须原完整健康检查，不能借单侧隔离绕过。
3. **TCP健康竞争风险（静态证实，原窗口未强行重现）。** 原监测线程的`Poll`与`Available`组合可能与读取线程竞争：可读数据被读取线程消费后，Available为0不能独立证明EOF。现由唯一读取线程判断EOF/接收错误；周期确认仅表达本地传输会话尚未发现失败。没有相机应用层心跳，不能承诺静默断网立即被识别。
4. **诊断缺口已补齐。** 驱动输出有界、不含条码/原始协议字节的健康Source，复用ObservationSession及RuntimeJournal，仅记录连接状态转换，不逐次健康续期刷屏。读码、业务输入、原请求仍是原来的独立权威。管理重连中的SocketException返回MaintenanceIncomplete，保留资源和原责任；外部取消不误报成功。

## 当前协议和资源规则

成功交给Application现已先写入DB9输入接收账；驱动Ready、内存入队、持久接收、Admitted和完成分别计证。队列满/异常/停止的逐笔残留与独立worker进展见[长稳指南](win11-24x7-stability-remediation-01.md)。本地Ready仍不能证明静默远端存活。

供包台是TCP客户端，主动连配置IPv4/端口，不发送握手或测试业务报文。严格UTF-8；LF结束，也接受CRLF；正文最多4096字节，不含LF及可选的末尾CR。拆包（含UTF-8拆字节）和多行粘包按顺序处理。空行、控制字符、非法编码、超长行和不完整帧EOF均围栏连接；不截断、不替换乱码、不跳过坏帧继续声称健康。EOF即使无半帧也表示RemoteClosed。连接未建立时实际端点可能为空，仅保留ConfiguredRemote，不根据路由查询伪造已用源端点。没有新增应用读超时、自动重连/重放、源IP绑定或新配置字段。五秒连接预算是既有行为，预算超时现在明确分类为ConnectTimeout；调用方取消仍为ConnectCancelled。

默认`entity.mode=observe-only`：形成UncorrelatedRead观察，不能建立包裹准入。`line`须现场确认一行一个实体并满足原Production资格；同码两行有不同实体身份，不按条码合并。旧配置格式、默认模式和字段不变。构造时复制连接参数，外部草稿修改不能改变当前套接字。

读取线程不跨Application回调持有驱动锁，启动时也不在启动调用栈内执行读码回调。Stop关闭套接字、取消并等待读取及本地确认任务；取消等待时保留任务/CTS及所有权，下次显式Stop继续排空，禁止重叠Start。Dispose和Start/Stop共用生命周期串行边界。Application原机级资源租约、规范化IP/数字端口独占、注册Epoch、单调Sequence、位内顺序、位间独立、停止屏障及失败保有机制继续使用。

## 端点到原业务请求的追踪

|链路|当前事实与代码|
|---|---|
|配置账→实际连接|唯一configuration.json的Effective经ParcelCapabilityFactory创建TcpLineObservationSource；Draft/Pending不热改连接，重连仍使用Effective，受控重启才激活候选|
|IP/端口→设备→位|`DeviceBinding.DeviceId`与`Topology.Positions[].ReaderDeviceId`精确映射；物理口名/枚举顺序/窗口焦点不决定left/right|
|输入→筛选→实体|Observation带DeviceId/Epoch/Sequence/SourceEventId；ParcelRuntime和BarcodeFiltering验证新鲜度、注册、完整候选及唯一匹配；observe-only/错误观察保留拒绝证据|
|实体→请求→格口|Admission/Operation/Acquisition及ReaderBinding冻结进入T1原请求；RCS仍按原位编码与合同处理，匹配本请求的有效响应才形成格口；失败新输入不沿用上件终结格口，未决原件独立保留|
|恢复|旧Epoch回调拒收、单侧受控重连不换另一侧Epoch，不重投原请求；Unknown、原响应资料、实体冲突和外部批准仍各自阻断|

代码路径：`src/Ipc.Drivers/TcpLineObservationSource.cs`、`src/Ipc.Platform/ObservationSession.cs`、`src/Ipc.Application.ParcelFlow/ParcelRuntime.cs`及ParcelHealth/ParcelDeviceLifecycle/ParcelOperatorProjection；原绑定、筛选和RCS实现复用，没有第二个配置账或诊断平台。

## 诊断与操作

作业页只显示“TCP 已连接”或“设备已确认”和必要的诊断入口，不以连接等同读码/准入/格口成功。管理模式中按本相机打开现有运行详情或日志：

* 连接状态转换带Device、Position、Epoch、时间；TCP证据带实际Local/Remote、ConfiguredRemote、Phase、Category、SocketError和Sequence。
* `ConfirmedAt`是本地会话确认；`LastReceivedAt`是最近收到字节；`LastValidObservationAt`是最近形成语法有效观察。后者不证明入队、筛选、实体准入、RCS接受或物理完成。当前Source随健康快照更新；历史日志在状态变化时保留当时证据。
* ConnectFailure/ConnectTimeout/ReceiveFailure/RemoteClosed属于传输链；InvalidUtf8/EmptyFrame/ControlCharacter/FrameTooLong/IncompleteFrame属于协议链；IngressRejected/IngressCallbackFailure属于应用入口链。正常业务Blocked/Rejected写原InputObserved，不关闭TCP。
* 暂停并核对现场实体及原请求，再进入管理模式受控重连。TCP网络恢复不解除实体/过载/存储/Unknown守卫。RCS的HTTP心跳与包裹业务响应分别核对，原Query=false没有被改造成重发接口。

普通快照遮蔽新增TCP详细端点，管理模式才可见；日志沿现有脱敏、有限队列、滚动与保留策略。无条码原文或callback异常文本写入新连接日志。管理退出/到期/改密码沿原授权失效规则。

## Windows PowerShell 5.1现场只读采集

[脚本](../../scripts/diagnose-camera-network.ps1)随Sportmaster候选放在包根目录，说明为CAMERA-COMMUNICATION.md；源码入口在scripts/。不要求PS7，无第三方依赖。先核对左右设备身份和实际端口，再以普通只读权限执行；如某节无权限，CollectionErrors会明确保留缺项。输出含网络私密信息，只保存在D盘、Git外，不直接提交或发送。

```powershell
# 在完整源码目录；替换为真实已登记的目录，不使用新seed替代原配置账。
. ./scripts/project-environment.ps1
powershell.exe -NoProfile -File .\scripts\diagnose-camera-network.ps1 `
  -ConfigurationFile 'D:\已登记数据目录\Production\原站标识\sportmaster\configuration.json' `
  -ReleaseDirectory 'D:\已登记程序目录\当前版本' `
  -OutputDirectory 'D:\ImporterPcDiagnostics\camera-before-001'
```

可不传ConfigurationFile/ReleaseDirectory，仅采集本机网卡、IPv4/IPv6地址和前缀/IPv4掩码、接口映射/metric、Active/Persistent路由与NextHop、邻居、现有TCP连接及进程、HKLM存储根只读摘要；未传部署来源不伪造版本。配置只导出白名单（Effective设备/作业位/实体模式、Pending是否存在），不导出认证和原请求。有效文件不代表连接已启动，仍需用管理快照和TCP进程对应实际运行证据。

按显式CameraAddress可加入`Find-NetRoute`源地址/接口/完整路由选择，仍不发网络探测。配置里的地址只用于只读路由查询，不能隐式授权主动探测。主动模式必须另外显式提供地址和端口，例如隔离/授权维护窗口内：

```powershell
# 文档地址仅为合成示例；实际现场操作须先协调单客户端连接所有权。
.\scripts\diagnose-camera-network.ps1 -CameraAddress '192.0.2.10' -CameraPort 1234 `
  -EnableIcmpProbe -EnableTcpProbe -OutputDirectory 'D:\ImporterPcDiagnostics\camera-active-001'
```

每显式地址只做一次ICMP/一次TCP连接，各默认预算1秒，最多8个显式目标；TCP不发应用字节，可能占用相机唯一客户端。脚本会提示此影响，不扫描网段、不写网络/注册表/相机参数、不自动修复。

已知网口4、5接相机，7接交换机是物理标签，不能直接当Windows接口名称或InterfaceIndex。历史Find-NetRoute对10.5.2.213选10.5.2.210/以太网7/10.5.0.0/22，目标身份和完整下一跳仍未确认，不能据此判错；本轮没有用该目标进行主动探测。0.0.0.0下一跳表示on-link，空值表示本节未采集到，均须结合实际目标/网段判断。

安全修复路径：先用输出和管理日志分类，再由设备/网络/业务人员确认预期端点、网段、协议和实体语义。配置问题经同一账保存/核对/受控激活；网络问题另行取得现场变更授权和维护窗口，保存原地址、掩码、接口GUID/Index、全部相关路由/metric/下一跳，明确受影响业务及恢复原值的回退命令；按最小必要范围变更后重做只读核对和授权有限验证。不能把添加静态路由、更改metric或放行防火墙作为通用答案。回退不清业务账、不修改原请求、不借克隆目录双活。

## 版本、验证和边界

本轮新构建Sportmaster **1.11.3**、DirectPlc/WcsToPlc **1.5.2**（共享运行生命周期变化），TCP内建提供者 **1.0.1**；compositions及Sportmaster的TCP提供者兼容声明同步（另两业务无TCP提供者清单变更）。Application.Contracts仍 **1.16.0**、Parcel DB **8**，无SQL/配置迁移；Platform新增兼容的连接失败异常分类及可选健康状态转换回调重载，旧StartAsync签名保留。宿主公共DLL字节变化仍需按既有精确指纹重建新扩展闭包；旧签名包/介质和原请求固定身份，不覆盖或用新包替换历史责任。

[必要验证记录](validation/dual-camera-communication-fix-01.md)列出实际结果与失败记录。原生WPF投影文字变化沿现有布局和技能约束，无全量视觉重测；目标构建、隔离应用/进程、脚本和现场分别记录。不以合成测试宣布生产可用。现场仍需确认两台相机IP/端口/型号、实际TCP服务合同或SDK组合、独占客户端、拆包/触发/line实体边界、预期网段/下一跳、RCS真实响应及资格。没有新增现场批准人或关闭D/RV。
