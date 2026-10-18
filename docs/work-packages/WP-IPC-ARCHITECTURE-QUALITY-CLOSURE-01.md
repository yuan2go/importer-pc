# WP-IPC-ARCHITECTURE-QUALITY-CLOSURE-01

用户2026-10-18本包完整要求为授权范围。[编码前整改设计](../design/architecture-quality-closure-01.md)。固定main 2126ac2c06a0c352d96afbbf13ec1a719c352738；独立分支codex/architecture-quality-closure-01。原主工作区未跟踪资料保留。开放PR #23采集生命周期为依赖，不能以其OPEN状态冒称main已修复。

## 实施计划（writing-plans，顺序执行）

- [x] 固定main/工作树/开放PR；读取现行权威和审查输入；保存受影响入口原字节并清理过期“当前”指导。
- [ ] 原始审查PDF校验/单独归档main：尚未取得，BLOCKED；不生成替代品。
- [x] 继承PR23采集保有；复核新增设备生命周期互斥和原包版本边界。
- [x] 恢复/异常：ParcelRuntime/Recovery/Stages/Repository/Commands，修复AQ-01/02/03；SQLITE_BUSY与重启定向验收。
- [x] 容量：新增ParcelRetention/Repository历史设施、Contracts分页契约；终态事务归档、永久去重、容量一致、重启/备份恢复。
- [x] 阶段/设备：Stages/Health/Guards/Commands/ObservationSession，Attached隔离、按阶段屏障、维护外呼解锁、设备互斥、持续身份冲突。
- [x] 配置/扩展：新增配置审计及Parcel消费者读者；Assembly Factory/CLI/PlatformStore复用安装恢复/卸载；真实签名包和离线备份引用检查。
- [x] UI：先读v1.1原HTML/CSS/JS和完整交接；Parcel ViewModel/History/Maintenance及Sportmaster左右投影，命令Owner一致、完整候选编辑；DirectPlc不扩范围。
- [x] 参考HTTP有界正文/取消；必要构建、定向验证、一个必要候选独立解包实际进程旅程。
- [x] 同步运行/迁移/扩展/Windows与现场后验、问题源码证据及未完成范围；提交推送main PR，不合并业务。

每项先建立能复现旧缺陷的定向断言，再实现责任闭环并运行相应选择器；失败保留。测试使用真实Application/ParcelCapabilityFactory、SQLite和参考协议，外部门面仅故障注入，不代替主链。不得删关键断言或把受阻写PASS。

## 状态/问题追踪

|输入|编码前复核|验收落点|
|---|---|---|
|AQ-01|Start及Restore没有核心目录身份比较|构造设备前拒绝错Mode/Station；同身份恢复保留|
|AQ-02|确认先删marker清内存，DB后写|失败不清责、提交后中断可恢复、旧确认不抹新过载|
|AQ-03|无Stage的WaitingWeight未建重启屏障|新重量不自动首发，显式原件确认后单次继续|
|AQ-04|operations总数上限不进统一守卫，无归档入口|工作集/未结/历史分开，分页及去重重启保持|
|AQ-05|Parcel管理仅Install/Inspect，Supply读者不适用|安装中断恢复、安全卸载、离线/备份/资源引用|
|AQ-06|VM只看Ready，未比较TTL|统一当前有效性、来源身份与时间保留|
|用户三至五增量|逐文件复核，不以审查6项缩减范围|阶段/设备/UI/配置/HTTP/扩展各项定向结果|

本包实际实现、固定源码及参考PASS见[交付结果](../runtime/architecture-quality-closure-01-results.md)。原PDF仍未取得，归档项BLOCKED；Windows NOT_RUN、真实SDK/协议/现场BLOCKED，D/RV不改。

已推送并创建[PR #24](https://github.com/yuan2go/importer-pc/pull/24)，未合并main。PDF归档项保持BLOCKED，不以参考整改通过关闭该缺失原件。

前轮最终源码/候选3d6960756bfd843a1cb78c6648687b45f2da9f1c；增补DB3/同格式精确实现升级屏障、双窗口草稿CAS、驱动/适配器可变数据所有权、资源成功后配置确认及仅Pending含包的备份消费者。原PDF归档仍未完成，整包不标记全部验收完成。


PR24独立审查输入a2e987a的R01～R05于本轮完成整改，文档治理b59318a先于代码，真实装配与候选源码固定7850fcc02b04d1b572fcb89215945ee0f60be28c。见[本轮结果、固定证据及后验边界](../runtime/architecture-quality-closure-01-rereview-results.md)。更新同一PR供复审，不合并main；原PDF01、Windows/真实现场门禁仍保留。


3f6f762独立复审剩余R03已由用户授权继续整改，见AQD-08方案/计划：所有支持schema在去重前校验原库存，覆盖Effective/Pending/未结Frozen；前轮结果、原件及失败不改写，不合并main。

本次剩余R03闭环：先治理4b94c3f7905dd794ce72d82e9f4ce5064865a03f，业务/测试固定445e1f62bf01fe21e5f13576d2f483292571b709；所有支持schema统一去重前库存校验，schema1/2三类消费者及真实签名卸载验证通过。见[本轮证据/边界](../runtime/architecture-quality-closure-01-r03-results.md)。保持原件、29项修复前失败及旧候选身份，更新同PR24待复审，不合并main。
