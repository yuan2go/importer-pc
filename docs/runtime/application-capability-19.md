# 当前Application能力与职责

Sportmaster、DirectPlc、WcsToPlc各拥有独立主流程和契约，ParcelFlow复用持久化、来源生命周期和资源机制。Supply、Inspection保持各自业务；不将其视为向后兼容产品。原19阶段版本和操作说明见[归档](../archive/pre-fresh-baseline-01/README.md)。

当前构建、运行和配置见[运行入门](getting-started.md)、[Application架构](application-architecture-22.md)、[版本规则](version-compatibility-01.md)。只接受schema3显式拓扑；非当前格式拒绝，请使用全新数据目录。设备观察经受控epoch和资源租约入场，左右投递位独立；一个业务实例服务多个窗口，不因页面切换重建连接。

Platform提供公共机制，Application拥有状态、准入与命令守卫；驱动/集成实现端口，UI只调用应用契约。可选登录、称重、采图和上传根据当前完整配置建立端口，缺失必需能力拒绝业务。管理模式独立于操作员登录，不存在MaintenanceLogin绑定或SIMULATION_ONLY维护授权。

测量关联原Admission、设备、epoch、测量身份、单位、精度、稳定性与有效期；不以默认值代替失败实测。当前关闭称重或配置重量是显式策略。图片绑定原AcquisitionIdentity，校验路径、媒体类型、长度和hash；不能取最近一张代替原图。BeforeMain等待上传；Attached保留独立责任，不覆盖主链状态。

T1原子冻结请求及实现，T2在外呼前登记尝试和接收责任，T3按原发生保存回包和业务事实。Unknown、原始回包缺失、图片及实体责任分别保留；无权威查询不能伪造成功。停止排空已拥有任务后释放资源；超时不等于释放完成。真实SDK、设备、协议和现场各自验收，合成能力不授予Production。
