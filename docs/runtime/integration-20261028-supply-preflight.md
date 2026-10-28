# WT001：Supply 6 原始回执启动预检

本次承接[10/28 整合工作包](../work-packages/WP-IPC-INTEGRATION-20261028.md)中当前合同与原件保全任务，基线为 main `1720c44c514f81d6d8348b778fa4d5b1c290476f`。保全的旧补丁作为任务线索；本次在当前 Supply 6 上实现，不恢复格式 5 或迁移路径。原 Schema 源码片段与基线一致，格式号、原请求与接收责任模型不变。

## 问题和修复

原先 `StationApplication` 在自身恢复提交前校验所有回执，但 `SupplyStore` 已打开可写连接，Product 也可能先记录配置激活失败或部署阶段。真实子进程在 `ReceiptFlushed` 崩溃后，旧路径允许带 `[]` 围栏的库打开 writer，测试观察到 DB/WAL 字节改变。此前正常关闭数据库后的回归不足以证明崩溃 WAL 保全。

- `SupplyStore.ReadStartupState` 通过私有只读视图返回配置选择所需事实；不向 Product 暴露可提交的 Store。
- Store 构造在任何 writer、journal 设置和新建库之前检查围栏、当前格式及全部 raw。存在 raw 时必须提供原请求合同；孤立 raw 不创建空库。空围栏同样拒绝，也阻止遗漏该文件的数据库单独备份。
- `ReceiptInputs` 是只读预检与 Application 恢复共用的校验器：文件身份、原请求、原合同响应、接收预约、发送尝试、Committed 原响应及内容摘要。全部原件通过后才允许恢复写入和删除已处理 raw。
- Product 先只读计算 Effective/Pending 选择，取得最终资源与包租约，再由同一个受拥有的 `IntegrationRuntime` 验证冻结原合同。门禁通过后才建立 Store、日志、消费者登记，写配置失败与部署阶段，并初始化当前集成和应用。
- 门禁前失败保留 Pending；资源冲突保持 NotReady，可在冲突解除后重试。门禁通过后的模块初始化失败仍沿原激活失败/回退路径。早建的历史集成端口仍归原 Product 停止与释放链所有。

这不增加自动 Execute/Query，不把 Unknown 改为可重发。SDK 相机路线、Sportmaster 协议、安装器和数据库结构未改变。

## 实际本机验证

Windows 开发命令先执行 `. ./scripts/project-environment.ps1`。原数据、失败日志及 before/after 证据留在仓库外 D 盘；测试只使用合成配置、环回 HTTP 与真实 SQLite/子进程。

| 命令/入口 | 已实际取得的结果与范围 |
|---|---|
| `dotnet build ImporterPc.slnx -c Release -p:Application=Sportmaster -p:BusinessModule=Sportmaster -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false` | Windows 完整构建成功，0 警告/0 错误 |
| `dotnet tests/Ipc.Verification/bin/Release/net10.0/Ipc.Verification.dll . --current-contracts` | 退出 0，57 条 PASS 行，0 FAIL；含当前真实崩溃 WAL、11 类拒绝、两种合法 raw 恢复、孤立 raw 与空围栏备份拒绝 |
| 同一 Supply 可执行文件的 `--query-fixture` | 退出 0，10 条 PASS 行；真实代理取消、排空和异常传播 |
| `dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll` | 同用户普通令牌，退出 0，80 条 PASS 行 |
| Supply `--receipt-preflight-product` | 本机未验证：普通令牌在受保护机器存储策略拒绝自定义租约根，未到回执门禁；不将子进程准备成功记成 Product 通过 |

PASS 行包含断言及套件汇总，不是去重后的独立用例数量。

拒绝场景对账 DB、WAL、raw、围栏及种子配置的存在性、长度、SHA-256，并比较原请求/尝试/接收责任/配置/审计等行；代理计数区分准备阶段与待测阶段，待测阶段 Execute/Query 均不增加。SHM 单独记录，SQLite 只读访问可能更新共享内存，不声称整个目录逐字节不变，也不使用忽略 WAL 的 immutable 连接。

新的 Product 专项已纳入 `sportmaster-commercial.yml` 的 Linux 当前合同作业，执行实际 Product 入口：Effective/Pending 围栏、坏 raw 与无效 Pending 的组合、真正冻结合同的 raw 恢复、Unknown、合法 Pending、失败回退、新库和最终资源竞争。完整 Supply 默认验证与既有初始化失败/所有权套件继续在该工作流运行。CI 是否通过，以关联 PR 的精确 head/测试合并树和原始日志为准，不沿用此前 PR 的绿灯。

## 失败记录与证据边界

首次真实 WAL 红例保留，证明旧 writer 顺序存在缺口。新增 committed-hash 损坏夹具第一次使用正式 Commit 被不可变回执守卫拒绝，因此未到预定崩溃点；随后改成明确的 SQL 损坏注入，原守卫没有放宽。修正后的子进程仍验证非正常退出、实际崩溃点、非空 WAL 和 raw 数量。

当前源码的只读审查与运行结果分别记载。本机 Product 策略限制、真实设备/RCS、现场、受保护 Supply 部署以及安装发布不属于上述 PASS；本次没有修改本机 HKLM 策略或发布介质。旧报告和历史 PASS 保留各自来源。
