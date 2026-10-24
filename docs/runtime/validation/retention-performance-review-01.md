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

## 2026-10-24 再核查 RECHECK-02

本轮用户要求以PR99最新代码核查、修复并安全集成main。实际开工远端main已是`5bb032d7354ef619064a015484a5001204edfff5`：PR99/100/101均已合并，修复`464e8e2`已可达，原P2线程已resolved；没有再次修改生产清理逻辑或重复合并旧PR。其他工作区（包括另一个维护资格工作区）的未提交修改和四份附件均未吸收。

在新的D盘独立工作树使用Windows11 22631、SDK10.0.402、实际SQLite3.54.0重跑。SQL1～9与PR99 HEAD完全一致；旧查询明确`SCAN a`，新查询为`SEARCH temp.parcel_retention_pending_owners USING PRIMARY KEY (owner=?)`。`CommandAdmitted`审计的实际`owner`为runtime，业务归属在detail.Owner；既有owner/at索引不能直接替代这项JSON关联。当前准备阶段在驻留归档和已归档删除各安全阶段重建集合，均为常数次数，不按候选重建。连接独占、TEMP主键去重和每次清空重建保持原命令事实权威，不添加持久第二账或迁移。参见[SQLite查询规划](https://www.sqlite.org/queryplanner.html)、[表达式索引匹配要求](https://www.sqlite.org/expridx.html)和[TEMP生命周期](https://www.sqlite.org/tempfiles.html)。

128条Pending、128次不同无归属候选的本次正式配对如下，计时排除VM采样回调；0条Pending另有同规模配对：

|审计噪声条数|旧每候选ms|新每候选ms|新准备ms|旧/新128次近似VM指令|
|---|---:|---:|---:|---:|
|10,000|1.3758|0.0990|2.0738|3,998,000 / 10,000|
|100,000|10.5759|0.0892|7.2807|38,558,000 / 10,000|
|1,000,000|65.3926|0.0601|68.2305|384,158,000 / 10,000|

12组配对/45项计划、重复owner、单命令完成仍阻断、重建清除、主schema和备份不带TEMP断言通过；6项真实Repository迁移通过。`--win11-stability`验证Unknown、未决回复/命令、中断输入及冲突保护、提交前回滚、提交后永久退役、原回复指纹和重启不复活。维护备份/隔离恢复和版本兼容专项也通过。完整原始输出及来源hash见[本轮证据清单](../evidence/retention-performance-review-01/recheck-02/FILES.json)。

首次整组复现的`--upgrade-conflict-closure`失败，原`REPRODUCTION.json`保持FAIL。原库显示旧冲突未被确认、配置保持ActivationFailed，没有丢原责任；原样第二次通过。静态核查定位到夹具只等待Configure快照、忽略确认回执，后台健康I/O可在下一调用前关闭守卫。新增有界等待仅重试明确的准入前守卫拒绝/命令CAS冲突，必须取得Persisted并确认原冲突不再活动，才检查拓扑重启。其他拒绝、异常、超时和已接受结果不重试，不改变运行守卫、SQL或业务命令重放规则。该专项加入现有Linux CI并保留日志，原Windows冻结/安装器步骤和断言全部保留。修复夹具源码为`f4a62457150d859f5b778fb7de0d91687a691cda`；本轮最终CI/合并身份另按PR回执核对。

开工核对CI40099200158两项success，实际源码`3512f3ac9f1cac1a1382a3a1ddfa1bfe72e443c4`与开工main完整tree同为`9870a8b864638683f712fffac58f7ce691b4da4b`。安装器及Payload.SourceCommit均为该CI源码，安装器SHA256为`1b04853e174419673a545628258b65b388b43acf0ecf56c789665f3c30f450ab`；NOT_SIGNED、ActualWindowsInstallation=NOT_RUN。此为开工版本证据，不冒充本轮新提交的CI或新APPROVED审查。

`f4a6245`的本机Release构建0警告0错误；升级专项连续3次通过，随后`--win11-stability`、`--audit03-maintenance`、`--version-compatibility`均通过，命令、源码及程序集hash记于`recheck-02/FIXED-TESTS.json`。此前原样复现的首次FAIL与第二次PASS均保留。运行时先`. ./scripts/project-environment.ps1`，再`dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true`和`dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --upgrade-conflict-closure`；其余专项仅替换末尾flag。原`reproduce-retention-review.py`仍严格锁定形成时产品/测试输入，复现原SQL整组时应使用`5bb032d`独立工作树，不放宽旧来源校验来接纳本次测试差异。

本次只确认原审计重复扫描热点消除；计划仍有身份冲突扫描及未决输入筛选，微基准不构成整个维护线性复杂度或两小时预算证明。完整原件规模、受保护CLI、真实设备与24h/72h资格继续NOT_RUN/BLOCKED；另一个正在形成的维护资格工作包不被本轮代验收。
