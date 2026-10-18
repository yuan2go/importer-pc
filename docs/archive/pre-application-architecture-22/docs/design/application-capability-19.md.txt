> 20可靠性/部署增量已生效：[现行指南](../runtime/capability-reliability-20.md)及[设计](../design/capability-reliability-20.md)。下文19阶段策略/版本/保守恢复说明按形成时范围保留；冲突处以20为准，原字节已归档。

# Application及公共能力现行设计 · 19

基线08b8067aa6cba6a148252296c7a5b18a27b3be65；用户19授权为本轮目标。本文取代17/18的当前任务指导、共同布尔分派及“仅前置上传”阶段限制。原报告和已发布字节不修改；[原入口](../archive/pre-application-capability-19/README.md)。

## 所有权

Platform拥有模块生命周期、会话/认证、重量、读码、关联取图/上传通用协议、签名/schema/安装/精确加载、资源租约、配置修订机制。原公共类型的完整命名保留，以Application.Contracts类型转发到Platform，避免仅换程序集却破坏发布类型名。Platform没有Application程序集依赖。公共协议不包含ParcelOperation、格口业务、WCS/PLC状态。Application.Contracts拥有业务意图和外部可证明事实。三个Application各自给出下一步骤、前置、结果解释、终态及恢复规则；ParcelFlow只执行共用持久机制及能力策略。不引入流程引擎或消息总线。

Sportmaster只请求WCS并展示有效格口；DirectPlc验证重量后准备PLC；WcsToPlc验证重量、WCS格口后在同一T3提交原格口与原PLC意图。三个构建产物只选择一个主流程，多页面共享实例。现场字段/认证/编码/单位/PLC握手由适配器管理；声明输入要求、查询及物理确认能力，配置校验器拒绝实测/图片依赖冲突。

## 可选能力与守卫

登录启用时新观察必须有未失效会话；接纳冻结不可变身份。失效不复活、退出/换班不改旧件；未结件只允许原主体重新认证。软件暂停不是授权；维护另需认证组件提供的维护能力，合成维护授权明确标记Simulation。无登录不伪造操作人；旧Supply/Inspection契约及数据维持原路径。

实测秤启用必须绑定且健康，测量按admission/device/id/value/unit/precision/quality/stability/time校验。WaitTimeoutMs是本件等待期限，MaximumAgeMs是测量新鲜度；准入、Resume、提交和UI使用共同守卫。超时进入显式阻断，只能无外呼前受控放弃并检查实体；迟到/重复不替换已冻结重量。测量去重和作业关联同事务；重启无自动继续或重发。

图像在接纳原观察时准备并取得，保存admission/采集令牌/时间关联，重量等待不重新拍当前画面。公共媒体契约允许合法媒体类型和上传URI，不规定PNG/reference协议。驱动采集，组件上传，Application选择前置或附加策略。前置未成功不得进依赖主链；附加状态不覆盖主链，独立查询/继续/失败处置，不因未知而自动重传。要求图片地址的协议拒绝附加策略。

缓存有单件、总字节、待处理件数限额；释放先持久记录再删除，重启完成清理。原件仅在上传终态或安全放弃后释放；未知/冲突/未完成原件不得清理。已释放历史不验证文件存在，不引入长期图片存储。

## 并发、恢复与配置

外部等待释放聚合锁；请求T2和接收发生先持久、外呼结束重新取锁更新最新聚合，快照与软件暂停可用。保留在途责任，Pause不会撤销已发动作；Stop排空且不提前释放资源。原请求身份、目标、参数、转换实现hash、原回执独立责任与WCS接受/PLC意图同事务保持。

ContinuePrepared继承原守卫，只允许从未发送的准备意图；Unknown查询无记录仍Unknown。安装、启用、绑定、健康分别显示。沿现有ExtensionStore/受控加载管理认证、相机、上传，声明schema生成编辑表单；同一个ParcelConfiguration草稿/CAS/Pending/Effective。参数和二进制默认受控重启，存在未结业务或附加责任拒绝激活。暂停不是维护授权，切页不重建设备。

## 兼容和资格

保留所有原迁移SQL/checksum；新增字段/能力使用新契约身份与不可变示例包版本，不覆盖旧签名包。旧未决请求必须用匹配发布清偿。仅合成数据及已授权通用实现可公开；无真实SDK/协议则仅对应适配器与现场资格BLOCKED，Production仍拒绝。Windows构建不代表Windows运行。
