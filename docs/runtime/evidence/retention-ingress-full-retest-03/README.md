# 输入分页整合与完整历史续验原件

本轮固定产品源码`b83998f18c56df2dd965630b25908feedc00a56f`，继承main/PR104 `404e8552`及PR105 `10ad5d2`。报告见[续验记录](../../validation/retention-ingress-full-retest-03.md)。原PR103、PR104及PR105既有原件不改写。

[FILES.json](FILES.json)当前仅登记39份已结束输出的字节数/SHA256；[CAPTURE.json](CAPTURE.json)说明捕获范围。原始文本位于`original/preliminary/`，禁用Git换行转换。运行中的新受保护维护结果尚未加入终态清单。数据库、程序集、SDK、安装包及现场资料不入Git。

- `baseline-recovery/`：标准UAC重试原5bb032d介质的9.46GB完整备份隔离恢复。原回执聚合耗时字段为null，三项实际Stopwatch都有值；`DERIVED-TIMING.json`保留原文件hash并从三项相加得到543.3802166秒，未倒写原字段。
- `FIXED-SOURCE.json`：产品和夹具DLL身份及构建时脚本hash。之后容量预检脚本单独补强，实际执行版本以`EXECUTED-HELPER.ps1`、`PROTECTED-LAUNCH.json`和`CAPACITY-POLICY-CHECK.json`的hash为准，不冒用构建时旧脚本hash。
- `LOCAL-SUITES.json`及七个日志：整合后的分页/事务/取消/强杀、可靠性、九处强杀、稳定性、维护、兼容与升级冲突定向回归。
- `FULL-BACKUP-PLAN.json`：实际SQLite3.54在原完整备份的只读计划，锁前后拒绝非空WAL并用Windows读句柄排除写入；输入外层rowid范围查找、无TEMP排序，子查询仍有索引。
- `RETENTION-INGRESS.json`：短JSON/稀疏rowid磁盘输入组件的精确oracle、VM数和分页/删除时间，不是完整作业维护RTO。
- `ORDINARY-AUTHORITY.json`：同一新产品真实普通token被安装者守卫拒绝，关闭的1000-case DB hash不变、无备份/恢复目录创建；未访问活动大库。
- `RECONCILIATION-small-in-progress.json`：只核对已完成1000/10000 case，63项断言。大case仍在进行，不能据此标PASS。另有Windows写入共享、非空WAL、加锁后WAL和篡改退役hash拒绝检查。

复现先在Windows执行`. ./scripts/project-environment.ps1`，构建验证工程，再显式`dotnet build src/Ipc.Assembly -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true`。`--action business-identity`必须为sportmaster/1.12.2；旧DLL残留不能证明根入口身份。七组命令、实际参数和执行脚本均见原件/源码。

完整重验使用`scripts/verify-retention-maintenance.ps1 -ReuseCompletedRun <旧PROTECTED-RESULT.json>`，新Evidence目录及标准UAC/原HKLM D根。先前完整备份按manifest实测恢复到新受保护根；旧失败源库、原备份和此前恢复不覆盖。现成备份按实际字节计容量，复制系数和8GiB reserve保留，首case额外恢复和重复维护也计容量。实际运行未加`-RestoreAll`：首1000-case另做新备份恢复和重复维护，10000压缩，大case先从完整原件实际恢复再维护；大case新备份再次拷贝第三份、副本容量及259.2万档分别保留结果/缺口，不能冒称都通过。

新恢复起点已包含原8个resident/spool的先归档结果，故不重演那8项先归档IO；原首次运行的归档证据仍属5bb032d。日期加速和批量数量不累计持续运行时间。本机4核/约16GB及隔离Simulation，不能替代目标4核8GB、Production安装接管、真实SDK/RCS、24h/72h或工业现场资格。
