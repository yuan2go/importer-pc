# WP-IPC-APPLICATION-ARCHITECTURE-22

用户2026-10-18完整任务为授权范围；[现行设计](../design/application-architecture-22.md)。不以此映射缩减用户目标，19/20/PR20/21原修复不重复实施。

## 实施计划（writing-plans，本会话顺序执行）

- [x] 契约/校验：ParcelTopology、Position投影/冻结、schema3；三个Flow声明数量，旧Reader锚点与只读配置兼容。
- [x] 装配/会话：ParcelCapabilityFactory构造多设备及能力绑定，资源防重，设备独立epoch/健康/重连，部分失败保有责任。
- [x] 执行：每位有限队列，独立消费，位内实体准入；共享短事务/外呼释放锁；位故障隔离与共享阻断、过载审计、Stop排空。
- [x] 可选能力：仅取图、原采集复用、后台上传、终态释放；登录/重量原件及协议守卫继承。
- [x] 兼容/交付：追加SQL3/迁移前屏障，CAS配置完整性、备份恢复、原扩展新版本；默认/可选配置及随包指南同步。
- [x] 验证：新增Architecture22宿主选择器及受影响19/20/资源回归；必要WPF构建和三真实选装包解压进程冒烟。
- [ ] 记录实际源码/包/日志SHA及剩余边界，提交推送并创建main PR，不合并。

文件边界：Application.Contracts拥有拓扑和实体事实；ParcelFlow拥有调度/业务账与原件；Platform通用设备会话；Assembly拥有具体工厂；Drivers/扩展提供采集能力；既有UI只加结构编辑保护。目标/实现/模拟/现场分别报告。原SQL/checksum、历史报告、发布身份、REQ/BR/CAPI/D/RV保持。

实现和参考验证见[22结果](../runtime/application-architecture-22-results.md)，运行闭包固定8981d54；最终PR单独回执，不改历史证据。
