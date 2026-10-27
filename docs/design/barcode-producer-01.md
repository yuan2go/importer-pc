# TCP 条码生产者接入设计

唯一线上合同为[行协议 v1](../protocols/barcode-producer-v1.md)。工作范围见[BARCODE-PRODUCER-01](../work-packages/WP-IPC-BARCODE-PRODUCER-01.md)。

## 组件与状态

`TcpLineObservationSource` 保留唯一套接字/分帧/停止实现；profile 选择纯条码解释或 sequenced 状态机。协议解释器只接受有界完整行，产生握手回复或完整事件，不引用业务/数据库/UI。状态为 AwaitHello → AwaitReady → Streaming → Faulted/Stopped。所有超时/协议错误撤销有效期，不允许坏帧后继续正常读码。接收和监测共用状态锁；不跨 Application 输入回调持锁。Stop 必须实际排空任务才释放物理租约，取消等待保有资源。

`ObservationSession` 保有注册 Epoch；驱动再检查 wire epoch/boot/sequence/trigger。wire sequence 包含心跳，Observation 序号可跳过心跳值，但源状态机先完成连续性判断；平台不臆断通用观察必须逐号连续。AdmissionId 由稳定 Reader.DeviceId/boot/trigger 派生，SourceEventId 由 Reader.DeviceId/boot/sequence 派生，与条码和连接 Epoch 无关。同一多相机服务的 boot 相同也不会合并左右实体；原触发不能靠换 Epoch 变成新实体。plain 使用本次注册加本地行序号，跨重连重复不可识别的限制必须明示。

Application 保有准入与原请求责任。Ready 有效期来自真实握手/心跳，observe-only 在业务就绪守卫和 Observation.Kind 两层阻断。候选校验使用既有提供者 schema 和装配校验；没有第二配置账。每位故障仅阻断依赖该位的业务，共享 RCS/存储等守卫继续适用。

## 配置及生命周期

Reader 仍为 `tcp-line`，不改变自有产品版本 1.0.0/既有提供者合同身份；`profile` 缺省 plain，`entity.mode` 缺省 observe-only。新增字段及组合规则以协议为准。SM 默认候选改为 TCP observe-only，首配和日常使用同一 schema，用户必须显式选择业务模式并提供证据；不自动写 line。MVS 保持诊断实现，不提供生产就绪。

生产者切换包括 Reader 的提供者、地址、端口、profile、期望身份、实体策略或协议参数变化。保存时暂停/管理权限及无在途责任检查在同一 Application 串行区执行，避免与输入准入竞态；Pending 不改变当前连接。受控启用再次继承现有排空、原请求、失败回退及配置确认规则。恢复只处理允许解除的阻断，不能重发原请求。

物理互斥以规范的预期序列号为机级键，端点租约仍保留，防止同端点别名。TCP 在 Start 前取物理租约，失败/取消后待实际 Stop 排空释放；MVS 仍按实际枚举身份取同键，不用配置 IP 代替物理身份。保存时跨 TCP/内置 MVS/已绑定 SDK 扩展检查重复物理身份。读码平台的独占须现场协调，软件不能证明外部进程退出。

## 取舍

选择 fail-closed，避免建立跨连接重放缓存、幂等 ACK 或第二责任账。代价是断线需要人员核对遗漏期间实体，不能自动恢复；换来已观察/已送请求不会由自动补发再次准入。自研服务须保证握手冻结边界和完整触发结果，不能依赖供包台按条码去重。现有 plain wire 字节语义保留，新增业务模式证据和正式物理身份是保存安全条件，不静默补全。

规格中的缺口只证明协议不连续；不能推算漏了多少实体，更不能生成补偿 RCS 请求。软件暂停不是急停。

## 风险验证

黄金样例驱动解析与拒绝；loopback 验证分帧复用、握手未完成不准入、身份不符、连续性故障、心跳独立期限、旧 Epoch 和原连接排空；配置测试覆盖互斥、默认观察、模式组合及证据；Application 验证在途切换拒绝、Pending 不热生效、受控重连保持暂停/原请求。已有 plain 拆粘包、控制字节/超长、Stop 所有权测试继续执行。没有 getter/日志文案测试或 CI 性能基准。

Windows 源码构建与本机 loopback 不证明目标工控机、真实读码平台、自研服务、真实相机/触发或 RCS 现场可用。Production 既有资格/签名和管理授权不在本包重做，也不放宽。
