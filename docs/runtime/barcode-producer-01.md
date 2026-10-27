# 条码生产者连接与受控切换

适用相机 MV-PD010003-06M-12C 本身不解码。供包台是 TCP 客户端，连接 PC 上的海康读码平台（plain）或自研读码服务（sequenced）。同一时刻只启用一个生产者。线上格式、字段范围、黄金对话与恢复语义只以[协议 v1](../protocols/barcode-producer-v1.md)为准；组件职责见[设计](../design/barcode-producer-01.md)。MVS 采集扩展仅用于[独立诊断](hikrobot-reader-01.md)，不得作为条码生产来源。

## 首次配置

在首配或参数设置的左/右相机页选择“条码生产者（TCP）”。IP/端口填写 PC 生产者监听地址，不是相机 GigE 地址。左右位置各编辑自己的 Reader；不能把两个端点配置到同一物理相机。没有配置完成的位置勾选“稍后配置此连接”，相关业务拒绝，不影响另一就绪位置。

- plain：选择 profile=plain，填写预期物理序列号（正式启用必填）。无握手可证明身份或配置，连接健康仅代表有限传输存活；无来件超过存活期限也会故障。连接中漏读及断连期间丢失均不可检测。
- sequenced：填写从已审核生产者导出取得的 producer.id、producer.version、camera.serial、producer.config-hash；严格匹配才握手通过。生产者版本和哈希不能从收到的报文自动接受。按约定核对高级参数中的握手总时限、心跳间隔及超时。配置哈希的确定性导出由服务提供，不能用任意字符串或示例哈希代替。
- 默认 entity.mode=observe-only，只显示观察和设备状态。切换到 trigger 前，填写 entity.evidence 的现场证据引用：一触发/行一新实体、完整结果、旧队列清空及断线原包裹处置。plain 的 line 仅为已有同语义配置保留；sequenced 禁止 line。声明不等于生产资格或现场验收。

保存校验拒绝错误组合、缺失证据、身份冲突或非法超时。运行仍须通过既有生产资格、RCS、管理授权、存储和原请求守卫。仅握手或识码成功不能证明实体准入、上游接受或物理完成。软件暂停不是急停。

以下只是合成的 sequenced Reader.Parameters 对照，不能直接投产；完整候选仍由参数页生成并统一保存：

```json
{
  "device.ip": "127.0.0.1",
  "device.port": "9100",
  "profile": "sequenced",
  "camera.serial": "SYNTHETIC-01",
  "producer.id": "synthetic.reader",
  "producer.version": "1.0.0",
  "producer.config-hash": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
  "entity.mode": "observe-only",
  "handshake-timeout-seconds": "5",
  "heartbeat-interval-seconds": "1",
  "heartbeat-timeout-seconds": "5"
}
```

## 切换与故障恢复

1. 暂停软件并进入管理模式；按原请求查询/处置流程清结所有在途、Unknown、未消费输入及中断责任。此时保存涉及生产者、端点、协议、身份、实体策略或读码延后的候选会被 ProducerSwitchBlocked 拒绝。
2. 停原生产者，确认相机独占句柄释放、旧发送缓冲清空，并核对断连期间实物。将确认记录写入保存原因/现场记录。不能让读码平台和自研服务同时采集。
3. 编辑并审阅完整候选。切回 plain 要明确清除 sequenced 的预期生产者身份字段。保存只形成 Pending；普通重连继续读 Effective。
4. 受控重启启用已审核候选，或在配置不变时显式受控重连。连接得到新的 Epoch；sequenced 重新走 open/hello/arm/ready，并只接受新触发。重连不重放旧观察，也不重发原请求。
5. 核对连接状态、故障原因、输入责任及现场清空，再明确恢复作业。业务就绪还须健康有效、配置有效、序号连续和显式实体声明。

供包台 TCP 会话占用与本机 MVS 诊断相同的物理序列号租约。停止等待若取消，原会话未排空前不会释放租约。租约不能约束外部海康读码平台或另一台机器，也不能替代 SDK 独占及人工确认；自研服务不要另行争抢供包台代理持有的相同租约。要诊断时同样先停生产、清结责任并释放现有会话。

## 诊断与故障表

现有设备连接诊断显示连接阶段、注册 Epoch、端点、最近确认时间和固定原因；`TcpProducerDiagnostics` 与健康证据提供计数。计数按提供者实例累计，进程/实例重建清零，不承担业务账职责。日志输出不含条码原文、物理序列号或原始报文；需要查看实值时使用受授权配置或业务责任账。

| 原因/现象 | 结果与处置 |
|---|---|
| ConnectFailure / ConnectTimeout / LeaseFailure | 未就绪；核对监听端点或原会话、MVS 诊断是否仍占用。不得改序列号绕过。 |
| HandshakeIdentityMismatch / HandshakeBaselineMismatch / HandshakeTimeout | Faulted；核对离线期望身份与生产者冻结边界，不自动学习新身份。 |
| SequenceGap / SequenceDuplicate / SequenceRollback | Faulted，违规事件不准入；按未知输入缺口检查实体，不据差值补造请求。 |
| TriggerDiscontinuity / BootMismatch / EpochMismatch | Faulted；旧实体/旧会话/进程边界无效，须受控恢复。 |
| HeartbeatTimeout | 即使仍有事件字节也撤销就绪；检查生产者心跳调度和网络。 |
| Protocol* / InvalidUtf8 / ControlCharacter / EmptyFrame / FrameTooLong / IncompleteFrame | Faulted，不截断或容错成功；对照黄金样例排查生产者。 |
| RemoteClosed / ReceiveFailure / plain 的 LivenessExpired | Faulted；断线期间输入未知，plain 也不能据重连成功声明未漏读。 |
| IngressRejected / IngressCallbackFailure | Faulted；先检查现有输入队列、存储和业务故障责任，不能盲重连补发。 |

所有故障撤销有效期、退役 Epoch、关闭连接，保留实例计数和本机租约直到实际停止排空；无自动重连。配置切换审阅、Pending/Effective、原报文/合同/目标版本和防重守卫沿用[生命周期操作](lifecycle-ux-current-guidance-01.md)。

## 开发验证入口

Windows 命令先运行 `. ./scripts/project-environment.ps1`。完整解决方案：`dotnet build ImporterPc.slnx -c Release -p:BusinessModule=Sportmaster -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false`。构建 `tests/Ipc.Application.Verification` 后运行其 DLL，参数 `. --barcode-producer` 验证黄金协议、loopback、业务准入与切换；`. --p0-tcp`、`. --p0-tcp-components` 验证原 plain 围栏。原生配置执行 `tests/Ipc.Desktop.UiVerification` 的 `. --barcode-producer`。冻结 Windows 介质用 `./scripts/verify-commercial-wpf.ps1 -Media <介质目录> -Output <全新证据目录> -BarcodeProducer`，实际加载发布 DLL，检查首配/日常参数和窄窗滚动。

本机 Windows/loopback/合成证据不证明真实读码平台、自研服务、MV-PD010003-06M-12C、工控机连续运行或真实 RCS 合同可用。投产仍需真实身份/哈希、触发完整性、外部独占释放、断线实物处置与现场验收。读码服务实现、平台现场设置和生产签名资格不在本包范围。
