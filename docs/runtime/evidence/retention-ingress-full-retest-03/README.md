# 输入分页整合与完整历史续验原件

PR105已合并为main `ed42da81`，本证据通过[PR106](https://github.com/yuan2go/importer-pc/pull/106)追加。新604800完整受保护维护及独立逐笔对账通过；2592000实际容量预检拒绝，整轮为`PARTIAL_WITH_RECORDED_GAPS`。原FAIL/NOT_RUN不改判，也不授予目标机或现场资格。

本轮固定产品源码`b83998f18c56df2dd965630b25908feedc00a56f`，继承main/PR104 `404e8552`及PR105 `10ad5d2`。报告见[续验记录](../../validation/retention-ingress-full-retest-03.md)。原PR103、PR104及PR105既有原件不改写。

[FILES.json](FILES.json)登记125份闭合文本的字节数/SHA256：39份前期输出、3份连续复用回归及83份终态追加。前42份原字节未变；[CAPTURE.json](CAPTURE.json)说明范围，`original/**`禁用Git换行转换。数据库、程序集、SDK、安装包及现场资料不入Git。manifest只刷新相对已合并PR105的本轮差异；旧root检查点保留，当前终态在`retention_ingress_full_retest_03_followup`单列。

- `baseline-recovery/`：标准UAC重试原5bb032d介质的9.46GB完整备份隔离恢复。原回执聚合耗时字段为null，三项实际Stopwatch都有值；`DERIVED-TIMING.json`保留原文件hash并从三项相加得到543.3802166秒，未倒写原字段。
- `FIXED-SOURCE.json`：产品和夹具DLL身份及构建时脚本hash。之后容量预检脚本单独补强，实际执行版本以`EXECUTED-HELPER.ps1`、`PROTECTED-LAUNCH.json`和`CAPACITY-POLICY-CHECK.json`的hash为准，不冒用构建时旧脚本hash。
- `LOCAL-SUITES.json`及七个日志：整合后的分页/事务/取消/强杀、可靠性、九处强杀、稳定性、维护、兼容与升级冲突定向回归。
- `FULL-BACKUP-PLAN.json`：实际SQLite3.54在原完整备份的只读计划，锁前后拒绝非空WAL并用Windows读句柄排除写入；输入外层rowid范围查找、无TEMP排序，子查询仍有索引。
- `RETENTION-INGRESS.json`：短JSON/稀疏rowid磁盘输入组件的精确oracle、VM数和分页/删除时间，不是完整作业维护RTO。
- `ORDINARY-AUTHORITY.json`：同一新产品真实普通token被安装者守卫拒绝，关闭的1000-case DB hash不变、无备份/恢复目录创建；未访问活动大库。
- `RECONCILIATION-small-in-progress.json`：原阶段只核对1000/10000 case的63项断言，不包含当时活动的大case。另有Windows写入共享、非空WAL、加锁后WAL和篡改退役hash拒绝检查。

以上前期文件在`original/preliminary/`。终态文件在`original/completed-run/`：

- [实际完整CLI回执](original/completed-run/protected/PROTECTED-RESULT.json)、逐命令out/err、FIXTURE/VERIFIED、备份及恢复marker/config：保留原UTC及实际执行参数。604800维护4721.3243221秒；2592000容量预检需要97794680422字节、可用10432905216字节，未生成本轮最大样本。
- [独立逐笔对账](original/completed-run/RECONCILIATION-terminal.json)：三个完整档、89项检查；逐笔原body/input hash、六保护owner、命令/回执/冲突、原raw及全部永久准入/回复指纹不变。Windows读句柄排除写入和替换，锁前后拒绝非空WAL。
- [推导汇总](original/completed-run/TERMINAL-SUMMARY.json)：分别列出CLI、独立inspect、恢复及文件时间近似分段，不把隔离恢复当业务接管RTO。完整作业正文4712745380字节、原回包895111400字节属于reference合成数据，不是现场wire/raw尺寸。
- `RUN-OBSERVATIONS.jsonl`及observer源码/启动记录：只读JSON和文件属性、D盘容量及进程资源，不打开活动SQLite；终态后自行退出。
- `components/retention-ingress/`：两个输入页实际进程强杀的边界/PID/exit原件；组件产品hash与本轮固定产品一致。不是活动大库强杀或硬件断电。
- `BACKUP-CONTENT-IDENTITY.json`、`FROZEN-BUNDLE-IDENTITY.json`：新旧完整备份DB/config hash相同，仅manifest时间不同；实际冻结执行介质107份DLL/EXE/JSON的hash清单，不含二进制。中途CI明确保留其b83998f1及合成提交身份，最终CI在PR106另核。

预审P2 [r4369996878](https://github.com/yuan2go/importer-pc/pull/105#discussion_r4369996878)指出：已经复用的夹具再次复用时，已有两个来源属性令Add-Member抛错。`original/reuse-metadata-fix/`保存原语句失败及实际新AST语句回归：首轮/连续/序列化复用通过，父夹具文件hash未变。新脚本仅在新元数据副本上用`-Force`更新最近父备份及其夹具hash，旧父文件和祖先来源链不改写。本轮完整大库是首次复用原夹具，实际执行已冻结的232A…脚本；后续修复脚本25AE…不冒用为本轮执行版本。

复现先在Windows执行`. ./scripts/project-environment.ps1`，构建验证工程，再显式`dotnet build src/Ipc.Assembly -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true`。`--action business-identity`必须为sportmaster/1.12.2；旧DLL残留不能证明根入口身份。七组命令、实际参数和执行脚本均见原件/源码。

完整重验使用`scripts/verify-retention-maintenance.ps1 -ReuseCompletedRun <旧PROTECTED-RESULT.json>`，新Evidence目录及标准UAC/原HKLM D根。先前完整备份按manifest实测恢复到新受保护根；旧失败源库、原备份和此前恢复不覆盖。现成备份按实际字节计容量，复制系数和8GiB reserve保留，首case额外恢复和重复维护也计容量。实际运行未加`-RestoreAll`：首1000-case另做新备份恢复和重复维护，10000压缩，大case先从完整原件实际恢复再维护；大case新备份再次拷贝第三份、副本容量及259.2万档分别保留结果/缺口，不能冒称都通过。

新恢复起点已包含原8个resident/spool的先归档结果，故不重演那8项先归档IO；原首次运行的归档证据仍属5bb032d。日期加速和批量数量不累计持续运行时间。本机4核/约16GB及隔离Simulation，不能替代目标4核8GB、Production安装接管、真实SDK/RCS、24h/72h或工业现场资格。

复用的`FIXTURE.json`保留原生成时的Operations/Archives/RawRows及ProductSha256/FixtureSha256作为来源字段，不能当作本次恢复后统计或运行DLL身份。最近父源由ReusedFromBackup/PriorFixtureSha256定位；恢复后的实际统计看新CLI的maintenance-inspect，当前运行DLL与SourceCommit看PROTECTED-RESULT顶层。后续回执单独绑定，不重写旧字段。
