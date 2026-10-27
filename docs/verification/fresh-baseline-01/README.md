# 当前数据库基线等价证明

旧写入端源码：`4e3748159af2599878e57329f887098b1c6be927`，是B1 PR120合并前提交。改动数据库源码前，实际调用ParcelRepository和SupplyStore，在全新临时目录完整执行旧迁移链，再经只读SQLite连接导出sqlite_master与身份项。两个before.json为本轮新增证据，不是历史介质或旧PASS。表、索引、触发器、视图按type/name排序，SQL仅折叠空白；无业务数据、凭据或机器身份。

当前验证器：`tests/Ipc.Application.Verification/StorageBaseline.cs`。`--storage-schema-only`仅建新库并比对；`--storage-baseline`另外在隔离CI创建Simulation运行时并启动/停止。测试不重放旧迁移链、不打开冻结历史介质。旧快照的SHA由仓库manifest固定。

| 项目 | 比对结果 | 允许差异 |
|---|---|---|
| Parcel | 原51个对象，现47个；其余逐字段完全一致 | 删除parcel_semantics、parcel_topology_semantics、parcel_integration_semantics、parcel_commercial_semantics四个历史格式标识表；platform_migrations结构不变，仅从1～9九条改为当前10一条及新基线哈希 |
| Supply | 26个对象全部一致 | schema_history只留id6与新基线哈希/本次建库时间；storage_format为6/6 |

实际新库基线身份哈希：Parcel `ECC9F99AFD48F914765248A6B39064A49F0DCBF3DDD2F208C87C75F8E07FEB23`；Supply `E713C53909199AE67714ED0D4824AC3CD561B38DFF59D03C40F8998FCED86A72`。哈希统一LF以消除源码检出换行差异；SQL执行内容不变。两个实际写连接均断言WAL和synchronous=FULL。

本机纯结构验证及缺身份表拒绝14条通过。恢复/强杀/Unknown/原始回包、缺身份索引拒绝和全新目录启动停止仍须本批CI通过后才满足合并条件，不从旧PASS推定。

验证边界记录：首次执行结构参数时，新增分派误放入另一个条件分支，意外进入默认合成验证并在DirectPlc等待超时。它在新临时测试目录生成数据，可能使用共享机器租约；没有执行安装或真实SDK/设备控制。分派已修正，纯结构验证单独通过。该次不计为PASS；临时测试目录交用户处理，不清理机器运行目录。
