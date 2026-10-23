> AQA01/PR92复核：本文的Application所有权与业务拓扑继续有效；当前实现版本为Sportmaster1.11.2、DirectPlc/WcsToPlc1.5.1、Contracts1.16.0、Parcel DB8，SQL1～8保持。本轮复核及证据见[整改说明](../runtime/architecture-quality-remediation-01.md)。下文2026-10-20及22形成时的软件版本和验证保持历史身份，不是当前程序集版本。修订前原字节见[归档](../archive/pre-aqa01-pr92-review/README.md)。

> 2026-10-20形成时操作裁决：[审查整改](../runtime/sportmaster-audit-remediation-01.md)使用业务1.4.0 / Contracts1.10.0 / Parcel DB8；recover-legacy-receipt CLI显式目标8，旧API缺省5保持兼容。下文旧版本、候选和验证均保留形成时身份，不能使用旧程序回读DB8。

> 业务版本及兼容管理按[版本管理增量](version-compatibility-01.md)；原历史结果和D/RV保持。

# Application架构22 · 现行增量

WP-IPC-APPLICATION-ARCHITECTURE-22，开工main ea881293c888a824f26c9f9fa07352a22786206b，无开放PR。此设计在业务代码前形成。19/20及PR20/21的原请求、资格、原图、备份与健康修复继承；历史原字节见[归档](../archive/pre-application-architecture-22/README.md)。本包替代本轮TOPOLOGY/PREVIEW/UI补充任务，不新增页面/图片展示/DirectPlc UI。

## 所有权与拓扑

构建只选择一个Application，同一实例服务所属页面。Sportmaster明确两个独立投递位left/right、两个独立reader；每位扫码→WCS有效格口→应用结果，不调PLC。DirectPlc和WcsToPlc明确一个main投递位、一个reader，按配置关联本件重量，然后分别PLC或WCS→PLC。旧Supply共享箱/数量/AGV规则仅在原业务适用。

Application声明角色、数量、绑定和完成规则。Platform保持通用设备会话、资源、会话、配置、契约机制；驱动提供采集/PLC，组件提供认证/WCS/上传。WCS字段、单位、缺省操作人、认证和响应转换归组件；PLC地址、编码和握手归驱动。无登录不造操作人。

新配置schema3增加Topology（Readers、Positions）。Position稳定ID映射ReaderDeviceId；设备/投递位/采集/Admission/Operation/Request各自独立。Reader旧字段仅为兼容锚点，必须等于第一个拓扑reader，不构成第二权威。称重和取图绑定可在Position显式选择实例，能力库存与绑定保持同一修订；重复设备/角色/占用、非法设备共享、错kind或未启用拒绝。

## 调度与状态作用域

每设备独立epoch/sequence/健康及重连；注册回调固定设备和epoch，旧回调不能更新新会话。每位一个有限队列和一个执行任务，位内顺序，最多一个主业务未结实体；短事务锁保护共享账与快照，外部等待释放锁。左侧慢WCS不占用右侧输入消费者。新实体在本位主链完成后可准入，Attached上传未结独立保留；PLC接受不是物理完成，顺序供包须等权威完成才能下一件。

位故障（读码断连、实体Unknown/冲突/重量等待）只阻本位；公共服务失效、存储故障、全局软件暂停和容量枯竭阻相关共享路径。队列满形成带位/设备身份的持久过载记录，要求暂停检查后确认；不无声丢件。输入被守卫拒绝写审计，不自动排队跨实体重试。相同条码不去重实体；迟到重量和回执仅匹配原件。

## 可选能力与资源

称重关闭不调秤；启用必须拿本件有效值，绝不读最近缓存或补0。协议强制实测不能被配置关闭。取图与上传分开：Images可无Uploader，仅取图；有Uploader支持BeforeMain/Attached。仅取图原件保留至主链终态/安全取消；上传Unknown/Conflict保留原件。所有释放先持久后删，重启延续；原件配额约束字节和件数。

同物理reader兼取图须由已构造reader暴露类型化关联采图能力，工厂复用该对象，不另开设备连接；没有该能力拒绝绑定。独立取图设备绑定明确分离，必须验证原Acquisition。模拟只证明关联逻辑，真实采集关联/离场/复位/联锁需要协议与现场验收，不编造信号。

多设备部分构造失败由工厂保有已构造资源；启动失败由runtime保有全部已返回端口。Stop逐设备停止回调，排空每位任务、附加任务和外部调用后停共享能力；任一超时/异常保持数据库/包/设备租约，重试清理。不因超时宣告已停。

## 配置与兼容

同一CAS/Pending/Effective，所有绑定受控重启生效；在途冻结原位、实际reader、操作人、配置和实现。已接纳原件不随Pending变化。旧schema1/2保持显式单reader历史模式，不自动推断Sportmaster左右；采用schema3必须提交完整拓扑。旧UI不能编辑schema3结构，配置保持只读并拒绝保存；运行/快照契约保持兼容。

22形成时追加数据库迁移3，后续归档阶段追加4，PR26修复追加5；RCS追加6，审查03追加7，此后追加8；各次迁移均保留此前SQL及checksum原字节，当前为DB8/SQL1～8。DB5将缺raw资料责任与已确认业务结果分开，修复旧OutcomeReconciled责任与归档引用；不反填历史拓扑或改原请求/hash。进入新存储版本前先检查旧业务及资料责任，普通启动仍拒绝无法证明兼容的旧外部动作。旧DB2/3 Attached污染场景由[现行维护入口](../runtime/architecture-quality-closure-01.md)取得离线所有权、完整备份并以匹配原发布只Query原请求；仅确认外部业务已终结且各缺raw均有权威依据后，由recover-legacy-receipt CLI显式升级当前DB8（旧API默认DB5），资料、原图及包引用持续保留。旧代码已释放原图只能如实登记缺口，不伪造恢复。配置2→3仍通过完整候选与CAS显式完成；旧schema2保持历史语义。配置旧账未知须成功启动资源并由维护身份确认；新公共DLL字节变化要求新不可变包，不重签/覆盖旧包。

## 验证与交付

复用真实Application/ParcelCapabilityFactory入口和tests/Ipc.Application.Verification。新增--application-architecture覆盖双位交错、顺序供包、错绑/重连/局部共享故障、仅取图/上传/身份/重量、部分失败/排空、CAS/迁移/备份恢复。只跑受影响既有回归、必要WPF兼容构建、实际三选装portable包解压启动。Windows UI/UAC/ACL/设备实际NOT_RUN，真实SDK/WCS/PLC/采集/现场BLOCKED，D/RV不改。本包开发分支提交PR，不合并。
