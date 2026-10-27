# 条码生产者行协议 v1

规范标识 `BARCODE-PRODUCER-LINE-v1`。本文是 plain/sequenced 的唯一线上合同；[设计](../design/barcode-producer-01.md)定义供包台实现边界，[黄金对话](barcode-producer-v1.golden.json)是两侧共用的机器可读符合性样例。自研服务必须实现 sequenced；海康读码平台使用 plain。供包台为 IPv4 TCP 客户端，生产者为服务端。示例全为合成数据。

## 共用分帧

严格 UTF-8，无 BOM；LF 终止，也接受 CRLF。每行正文最多 4096 字节，不含 LF 及可选末尾 CR。支持任意拆包、UTF-8 字节拆分和多行粘包；不截断、不替换乱码。空行、正文控制字符、非法 UTF-8、超长行、EOF 半行均 Faulted 并关闭连接。EOF 完整行后也表示断连。JSON 转义解码后的字段仍须通过语义控制字符检查。上限包含 sequenced 的整个 JSON 对象；生产者不得截断候选以满足上限。

TCP 不加密、不认证对端；身份字段是配置一致性检查，不是密码学证明。部署仅限已隔离且受管理的本机/工位网络。协议无设备控制、参数下发、图像传输、原事件补发或业务结果确认。

## plain

每行是一个非空、无控制字符的条码，保留空格、大小写与前导零。供包台不向生产者发送应用字节。连接及实际入站字节提供有限传输健康，`liveness-timeout-seconds` 默认 300，合法无来件也会过期；内核 keepalive 不能代替业务心跳。部分字节可确认传输存活，但未结束行不能形成观察。

无生产者握手、源序号、触发或心跳，断连期间和连接内漏读均不可检测。配置及诊断必须显示此限制。默认 `entity.mode=observe-only`。业务模式 `trigger`（兼容既有 `line`）要求显式 `entity.evidence` 非空声明：现场一行对应一个新实体，同一实体不会重发/多帧重复发布，断线后原包裹处置及旧缓冲清除已约定。声明是运维证据引用，不是软件自动证明或投产批准。每个接收行产生独立事件及实体身份；同码两行不能合并。

## sequenced 消息

每行是单个 JSON 对象，键区分大小写，只允许下表字段；不接受重复键、未知键、null、注释、尾逗号、浮点/指数/负数序号。整数为规范十进制 0..9223372036854775806，耗尽前必须停止并受控换启动标识。字符串不得包含控制字符。`version` 精确为整数 1。

`epoch` 为供包台生成的 32 位小写十六进制新连接令牌，每次受控重连更换；`bootId` 为生产者每次进程启动新生成的同格式随机标识，不能复用。多相机服务可共享进程 bootId，各相机流独立计数；供包台用稳定 Reader.DeviceId 隔离身份，不能跨相机合并。`producerId` 是 1..64 位小写 ID（首位字母，其余字母/数字/点/横线），`producerVersion` 为 x.y.z；`cameraSerial` 为 1..32 位可打印 ASCII、无空白、严格区分大小写；`configHash` 为 64 位小写十六进制 SHA-256。生产者哈希覆盖相机身份、取图/触发、解码/码制、候选完整性和实体发布配置的确定性导出字节；导出格式由生产者固定并随版本交付，工位使用经核对的预期值，不自行重算厂商配置。

| 方向/类型 | 精确字段 |
|---|---|
| 客户端 open | type=`open`, version, epoch |
| 服务端 hello | type=`hello`, version, epoch, producerId, producerVersion, cameraSerial, configHash, bootId, sequence, trigger |
| 客户端 arm | type=`arm`, version, epoch, bootId, sequence, trigger |
| 服务端 ready | type=`ready`, version, epoch, bootId, sequence, trigger |
| 服务端 event | type=`event`, version, epoch, bootId, sequence, trigger, codes |
| 服务端 heartbeat | type=`heartbeat`, version, epoch, bootId, sequence, trigger |

`codes` 是 0..32 个完整非空 UTF-8 字符串，每个不得含控制字符或 BOM；允许多个候选与相同候选，Application 按现有筛选/歧义规则决定。零码也发送 event（codes=[]），不能跳过失败触发；过多候选或超长结果必须断开并告警，不能假装只有一个码。一次物理触发只允许一个最终完整 event，同触发多帧必须在生产者内汇总，禁止跨 event 增补。每个已绑定相机流内的触发事件标识是 `(bootId, trigger)`，条码不是实体标识。

## 握手及连续性

1. 客户端连接后发送 open，状态 Connecting，没有业务有效期。
2. 生产者冻结发布，丢弃旧连接待发送结果，等待新触发边界；发送 hello，sequence/trigger 为该 boot 的当前高水位。身份/版本/相机序列号/哈希须与 Effective 精确相等，否则 HandshakeIdentityMismatch。旧 epoch 立即拒绝。
3. 客户端返回 arm，回显 bootId 及两个高水位。此时生产者仍不能发送 event/heartbeat。
4. 生产者只有确认旧缓冲清空、没有跨边界触发在途时才能发送 ready，必须原样回显。随后才为新物理触发采集并发布。握手总预算 `handshake-timeout-seconds` 默认 5（1..60），零散字节和 hello 不延长。
5. ready 后，每条 event/heartbeat 的 sequence 都必须恰好为上一条+1。相等为 SequenceDuplicate，小于为 SequenceRollback，大于+1为 SequenceGap。event 的 trigger 必须上一 trigger+1；heartbeat 的 trigger 必须保持原值。缺口、重复、回退、错误状态/类型/版本/字段、boot 变更均 Faulted，不投递违规输入。
6. 生产者在 `heartbeat-interval-seconds` 内至少发送一条 heartbeat，默认 1（1..60）。客户端 `heartbeat-timeout-seconds` 默认 5（2..300），必须至少为间隔的三倍。ready 建立初次期限，此后只有合法 heartbeat 续期；event 或零散字节不能掩盖心跳停止。到期立即失去业务有效性并在监测周期内关闭连接。时间预算用单调时钟，显示时间使用 UTC。

生产者不得在同连接再次 hello/ready；想修改身份或配置须停止并走受控切换。不允许热换相机、热改解码配置或重新学习期望身份。

## 断线和恢复

采用 fail-closed，无 resume、无事件 ACK/重放窗口。任何断线或协议错误退役当前 Epoch；未确认的期间视为有未知输入缺口，不从重新连接成功推断没有漏包。生产者停止发布并丢弃跨连接队列；不得用新序号包装旧触发，不得把旧包裹重新报告为新触发。

维护人员暂停、检查断线期间实体并按原请求责任处置、停原生产者、确认物理相机与旧缓冲释放后，才能显式重连/启用。重连后仍保持软件暂停，需明确恢复；原请求 Unknown、原输入中断、冲突、过载与存储守卫保持，不能以握手通过解除。启动是新观察边界，不追溯补齐已丢失事件。供包台不能从文本证明现场清空，证据声明和维护流程是必要前提。

业务模式必须 `entity.mode=trigger` 并填写 `entity.evidence`，声明一触发一新实体、同触发多帧汇总、零码/多码完整上报及断线处置。sequenced 拒绝 `line`。observe-only 仍检查全部协议与健康，但不能创建业务作业。

## 相机独占和切换

启用的生产端点填写预期 `camera.serial`；sequenced 任意运行模式必填，plain 正式启用必填。相同序列号只能配置一个生产者，MVS/智能读码器诊断配置与其互斥。供包台 TCP 生命周期持有与 MVS 相同的 `device:hikrobot-gige:<serial>` 机级租约作为本机诊断互斥代理；自研服务不能同时以同一独占键另取供包台租约，应以 SDK 独占持有实际设备。租约不跨机器，也无法约束未实现租约的读码平台。SDK 独占及现场协调不能被本机租约替代。

切换顺序：软件暂停并处置所有在途/未知责任 → 停原生产者 → 确认相机及缓冲释放 → 审阅完整候选并保存 → 受控重启启用 → 新 Epoch/握手 → 检查并恢复。普通重连只读 Effective，不能应用 Pending；有在途责任拒绝保存生产者切换候选。不得并行开启 MVS 诊断或以修改 IP/端口绕过物理身份。

## 故障及可观测

所有协议/身份/连续性/心跳违规均发布原注册上的 Faulted、撤销有效期并关闭套接字；不回送原帧或错误详情。计数覆盖连接尝试/建立/关闭、握手失败分类、序号缺口/重复/回退、触发违规、心跳次数/超时、当前及最大心跳间隔、旧 Epoch 拒绝。计数在提供者实例生命周期内累积，重启归零；状态变更日志受既有滚动保留策略管理，不是业务账。

日志只记录类型、计数、状态、Epoch 和固定原因码，不记录条码、物理序列号、原始帧、对端异常原文或握手对象。应用权威输入/请求账仍保留原有必要业务证据。连接健康不等于识码成功，识码不等于实体准入、外部接受或物理完成。
