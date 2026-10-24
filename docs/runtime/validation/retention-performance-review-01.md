# 历史到期清理查询：合并后独立复核

2026-10-24，固定main `12a7cbdc11839dfae7eec88f9514e8559694bdd0`。本次由用户明确授权归档报告、复现工具、原始证据及索引，不改变业务代码。原始报告和日志见[证据目录](../evidence/retention-performance-review-01/README.md)，原稿保留原字节；本文提供仓库阅读入口。

## 状态与结论

核实时，[PR99](https://github.com/yuan2go/importer-pc/pull/99)和[PR100](https://github.com/yuan2go/importer-pc/pull/100)已合并。修复提交`464e8e2e225c832f358837afdd28a0772e117a65`随PR100进入main `12a7cbd`，原PR99 HEAD `5074660f`继续作为修复前比较源。所核对main已包含修复，本轮未重复修改或再次合并业务修复。

PR99的[P2审查线程](https://github.com/yuan2go/importer-pc/pull/99#discussion_r4366762288)和PR100的空闲日志保留线程均已解决。原Codex reviews为较早提交上的COMMENTED，不称为最终HEAD的新APPROVED审查。本项静态核对和定向验证未发现新阻断缺陷，不代表产品完成现场资格审查。

## 查询计划、索引与增长项

使用实际SQL1–9及当前构建的SQLite3.54.0。DB9已有`parcel_audit_owner_at(owner,at)`、`InputObserved`的`OperationId`部分表达式索引、`parcel_commands_pending(id)`部分索引和命令主键，但没有匹配原`CommandAdmitted` JSON Owner关联查找的索引。原计划含`SCAN a`，每个候选重复扫描审计，此项成本约`O(K*A)`，K为候选数、A为审计规模。

现实现每轮离线独占维护时从原审计/命令账派生TEMP待决owner集合，逐作业计划为`SEARCH temp.parcel_retention_pending_owners USING PRIMARY KEY (owner=?)`。准备阶段仍扫描一次审计并按命令主键关联，逐作业不再重复`SCAN a`；成本由一次准备和K次索引查找组成。这不是整个维护流程线性或满足两小时RTO的证明。

TEMP集合每轮清空重建，不成为第二份持久权威。同owner多个命令只完成一个时仍阻断；未决回复、未确认身份冲突、Pending/Interrupted输入、近期审计和终态/原件责任条件保持。原命令ID、报文、合同、目标、备份先于删除、逐作业事务、永久防重、维护权限及两小时预算未改。SQL1–9和既有checksum未改。

## 配对规模结果

每组检查128个不同且无未决归属的候选，另有128条结构真实的合成命令，分别设置0/128条Pending。以下是128条Pending场景正式测量：内存SQLite已预热，计时不含progress回调，VM指令另行采样。

|审计噪声条数|修复前每候选ms|修复后每候选ms|每轮准备ms|
|---|---:|---:|---:|
|10,000|1.0313|0.0832|1.1770|
|100,000|7.2444|0.0557|6.8093|
|1,000,000|67.6878|0.1017|111.3877|

128次检查的原查询近似VM指令数为3,998,000、38,558,000、384,158,000；修复后均约10,000，采样粒度1000指令。0条Pending场景同样保持该趋势。完整12组配对和45项归属/schema/备份断言见[正式结果](../evidence/retention-performance-review-01/original/RESULT.json)。先行带progress回调的计时独立保留，不混入本表。本机短时合成耗时不外推完整维护RTO或工控机容量。

## 迁移、原子性与恢复

原本机Windows11 22631 x64、SDK10.0.402，Release构建0警告0错误。以下保持`12a7cbd`源码身份：

|验证|结果与范围|
|---|---|
|查询和集合|12组配对、45项断言PASS；Pending保护、重复owner、集合重建、主schema和备份不包含TEMP|
|实际Repository迁移|6项PASS：合成DB8升级DB9、DB9重开、SQL1–8账面checksum保持、旧DB8读者拒绝DB9且原库字节不变|
|`--win11-stability`|PASS：提交前回滚、提交后退役保留、Unknown/冲突/未决命令/中断输入保护、原回复指纹和重启防复活|
|`--upgrade-conflict-closure`|PASS：未决DB4/5在升级前拒绝且保留原件，已结DB4升级DB9及原冲突/配置守卫|
|`--audit03-maintenance`|PASS：原库独占、完整离线备份和隔离恢复；不代表受保护清理CLI端到端验收|
|`--version-compatibility`|PASS：原兼容声明及维护拒绝守卫|

依赖二进制只登记文件名和hash，不上传DLL。详细命令、起止、退出码和日志见[原运行记录](../evidence/retention-performance-review-01/original/runtime-tests.json)。仓库复现入口见[运行说明](../evidence/retention-performance-review-01/README.md#仓库内复现)；新输出单独记录实际源码和时间，不改写原结果。

## CI、安装器与现场边界

[PR100最终CI40096864628](https://github.com/yuan2go/importer-pc/actions/runs/40096864628)的`aqa01-boundaries`、`frozen-sportmaster`及安装器构建成功，实际合成源码`6cebb3015f5a975bb2cd158b5b772ac7fbb60afc`与复核main完整tree均为`94ebc204eb8898fe239e8941d9696e6b807b25bd`，完整diff为空。安装器SourceCommit和Payload.SourceCommit均对应该合成源码；实际安装NOT_RUN，签名NOT_SIGNED，CI系统为Windows Server2025。

本次归档PR的最终HEAD、CI合成源码和合并回执另行核对，只证明归档版本通过对应检查，不倒写原性能、恢复、介质及CI身份，不把旧PASS扩张到新源码。

归档工具验证单列在[发布阶段证据](../evidence/retention-performance-review-01/README.md#归档工具验证与环境限制)：新复现8个步骤退出0、原件/局部manifest及文档检查通过；补充归档工具7项测试中，符号链接用例因本机缺少特权报WinError1314，整体FAIL_ENVIRONMENT（6项通过、1项环境错误）。原日志保留，没有改权限或跳过断言，不能被新CI的PASS覆盖。

完整raw规模维护RTO、受保护`expire-history` CLI全链、目标4核8GB、多Windows11版本、真实SDK/RCS、真实24小时/72小时连续运行和工业现场验收均未由本次复核或归档完成，保持NOT_RUN/BLOCKED。原D/RV及客户/设备/合同边界不变。
