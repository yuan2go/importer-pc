# 完整历史与受保护维护 CLI：本机补证

本轮产品源码固定为main `5bb032d7354ef619064a015484a5001204edfff5`，DB9；验证夹具是单独新增、按源码及程序集hash记录的测试输入。结果和剩余资格见[验证报告](../../validation/retention-maintenance-qualification-01.md)。原[查询复核](../retention-performance-review-01/README.md)的原字节和SourceCommit保持不变。

终态：修正轮1千/1万条通过；60.48万完整清理在原两小时预算下FAIL（CLI共7855.705秒，输入未全部退役），局部责任守恒对账不能改判全链通过。259.2万修正样本因前例失败未运行；60.48万隔离恢复UAC取消，NOT_RUN。新增输入分页热点只在独立组件评估候选，未改业务源码。

这里的“完整”限定为reference流程合成作业、冻结请求/配置、原回复BLOB、准入/回复身份、完整输入及审计结构。最后8个正常作业以resident/spool形态交给真实CLI归档。六类独立保护owner用于核对未决命令、未确认冲突、中断输入、Unknown/Awaiting原件、近期审计和未到期条件。没有真实相机、RCS wire/数字号段、图片或生产数据。

## 复现

在Windows PowerShell 7、Python3.11+及仓库固定.NET SDK中使用新的D盘目录；先确认实际可用空间、HKLM已登记的D盘存储根和安装者身份。编排脚本不登记HKLM、不接管既有部署、不删除旧样本，也不延长产品两小时预算。`-RestoreAll`为每个完成的case执行实际隔离恢复，容量预检按原库、完整备份、恢复副本及8GiB本地预留计算；拒绝容量不足时记录NOT_RUN，不减少结构或安全条件。

```powershell
. ./scripts/project-environment.ps1
dotnet build src/Ipc.Assembly -c Release -p:RestoreLockedMode=true
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll `
  $PWD.Path --retention-qualification seed D:/retention-rerun/seed
New-Item -ItemType Directory D:/retention-rerun/evidence
```

然后在通过标准Windows UAC打开的提权PowerShell 7中运行以下命令。普通token应被实际产品守卫拒绝；不能用内部测试authority回调替代本步骤。

```powershell
. D:/projects/importer-pc/scripts/project-environment.ps1
& D:/projects/importer-pc/scripts/verify-retention-maintenance.ps1 `
  -Workspace D:/projects/importer-pc `
  -Evidence D:/retention-rerun/evidence -Seed D:/retention-rerun/seed -RestoreAll
```

仓库路径按实际工作树调整。默认规模1000、10000、604800、2592000；10000执行含VACUUM的`expire-history-compact`，其他执行`expire-history`，不得把不同动作的单点数字当成纯算法增长曲线。脚本要求产品源码干净且产品FileVersion的SourceCommit匹配当前HEAD；先构建再运行，不能借旧DLL身份。种子只跑短时reference流程，批量数据是明确的离线合成导入；8天前时间戳只触发7天到期，不累计实际运行时长。

当前产品仍含本轮发现的输入分页热点，以上是可复现入口，不承诺默认规模全部通过。遇非零退出控制器保留FAIL并终止后续case；不要把未到达的容量预检改写成已执行。两小时CTS开始前仍有hash和Repository构造成本，不代表进程严格两小时退出。完整复跑需新的证据/受保护目录和实际容量，不能覆盖本轮失败库。

完成后回到普通PowerShell，逐笔对账退役与完整备份：

```powershell
. ./scripts/project-environment.ps1
python scripts/reconcile-retention-qualification.py `
  --run D:/retention-rerun/evidence/PROTECTED-RESULT.json `
  --output D:/retention-rerun/evidence/RECONCILIATION.json
python scripts/check-retention-reconciliation.py `
  --run D:/retention-rerun/evidence/PROTECTED-RESULT.json
```

对账器只读取关闭且无非空WAL的快照，以Windows共享读句柄拒绝写入/替换，并在取得句柄后再次检查WAL；不对活动数据库使用immutable。`--completed-only`必须显式指定，结果标为局部完成，不能将运行中的大case算作PASS。负向检查只修改D盘临时合成副本，验证写入共享拒绝、非空WAL拒绝、加锁后WAL检查及篡改退役hash拒绝。

`exercise-simulated-authority`仅用于Simulation夹具诊断，其回执明确`ActualProtectedCli=false`和`ActualElevatedAuthority=false`，不能关闭真实受保护CLI缺口。

## 原始证据身份

第一轮简化输入夹具明确INVALID_FOR_COMPLETE_INPUT_STRUCTURE。其小样本性能仅为组件诊断，大样本未完成；用户明确同意后经标准UAC仅终止该夹具进程40552，部分数据库和日志保留。修正轮使用真实输入信封及完整Observation，逐笔解析、核对hash与归属后独立执行，不能覆盖第一轮失败身份。

原运行文件按执行时字节归档，原始数据库和二进制留在D盘，不加入Git。[FILES.json](FILES.json)固定210份文本原件的字节数/SHA256，[CAPTURE.json](CAPTURE.json)保留捕获来源且明确没有复制DB或二进制。`.gitattributes`禁止原件换行转换。先行编排脚本的恢复stdout文件名重复，1000条stdout被10000条覆盖；命令记录和隔离恢复数据仍保留，缺失stdout如实登记，仓库复现脚本已改为每case独立命名。后续补证使用新日志，不伪造原stdout。

|原件目录/文件|身份与范围|
|---|---|
|[invalid-initial](original/invalid-initial/)|第一轮简化输入INVALID、夹具及回归失败、明确授权后的单进程停止；不是完整规模PASS|
|[simulated-authority-probe](original/simulated-authority-probe/)|内部Simulation回调探针；不是实际提权或受保护CLI|
|[corrected-final/PROTECTED-RESULT.json](original/corrected-final/PROTECTED-RESULT.json)|实际运行终态FAIL，产品5bb032d，执行脚本与DLL hash另存|
|[FAILED-SCALE-SUMMARY.json](original/corrected-final/FAILED-SCALE-SUMMARY.json)|60.48万失败耗时、退出码、近似分段、DB/WAL/工作集采样|
|[PARTIAL-CUSTODY-DIAGNOSTIC.json](original/corrected-final/PARTIAL-CUSTODY-DIAGNOSTIC.json)|关闭快照局部提交对账；604800作业/157184输入退役、447622输入保留；不能替代all-N验证|
|[RECONCILIATION-final-completed-cases.json](original/corrected-final/RECONCILIATION-final-completed-cases.json)|仅1000/10000完成case，67项断言；不覆盖失败大case|
|[POST-BUDGET-RECOVERY-LAUNCH-CANCELLED.json](original/corrected-final/POST-BUDGET-RECOVERY-LAUNCH-CANCELLED.json)|60.48万恢复启动UAC取消，未启动helper；不是恢复结果|
|[NOT-RUN-2592000.json](original/corrected-final/NOT-RUN-2592000.json)|直接原因是前例失败；独立容量估计不足与实际控制器预检未到达分开|
|[input-plan-component/PROVENANCE.json](original/corrected-final/input-plan-component/PROVENANCE.json)|SQLite3.54.0实际DB9计划、候选SQL、源码、各次失败与最终精确oracle组件计时；不是产品修复|
|[DRAFT-CI-69FE77C.json](original/corrected-final/DRAFT-CI-69FE77C.json)|中途草案CI身份，不冒用为最终HEAD|

输入计划探针的`Program.cs`、`InputPlan.csproj`、`original.sql`、`rowid-proposal.sql`均保留原字节。复跑时复制到新的D盘目录，把csproj中的绝对ProjectReference调整为实际仓库的`src/Ipc.Application.ParcelFlow/Ipc.Application.ParcelFlow.csproj`。`dotnet run --project D:/新目录/InputPlan.csproj -c Release -- D:/新目录 D:/关闭完整备份.db --bench`会在新目录写PLAN/BENCH；该备份需先由上面的完整夹具/实际CLI产生。读原库时拒绝非空WAL并持有Windows读句柄，不能以活动库替代。组件的内存DDL/合成数据和oracle由原程序生成，原本机9.46GB备份不随仓库发布。最终`BENCH.json`与较早无保护项/仅配对结果分开；组件不能代替完整清理、迁移、恢复或维护预算。

归档后的普通token发布检查另存[publication-checks/RECEIPT.json](publication-checks/RECEIPT.json)：当前对账脚本在原1千/1万闭合快照执行73项检查，并验证四种拒绝场景。原`corrected-final`中的67项回执不重写；新增结果只说明发布脚本和小样本对账，不是重新执行维护/恢复或改变原大样本FAIL。

随后独立复核收紧测试入口：`RequireReference`固定`retention-qualification`站点，提权脚本在任何创建/ACL/复制前检查`Qualification`已有父目录。普通token使用实际夹具和实际脚本AST中的目录检查循环，验证错误站点拒绝且未建DB、规范1000条夹具生成、带方括号的普通路径接受、junction父目录拒绝。构建0警告0错误；[boundary-final回执及执行源码](publication-checks/boundary-final/RECEIPT.json)单列未提交时的源码/DLL hash，不冒用原完整运行身份。此测试未执行新的受保护CLI。最初只读探测发现`Split-Path -LiteralPath`不能与`-Parent`组合，改用其默认父目录参数集后才运行上述边界测试；没有以放宽链接守卫换PASS。

前一HEAD `6bed970`的[CI元数据](publication-checks/CI-6bed970.json)保留其实际合成源码`8e28accc`和安装器hash；该检查早于测试入口补强，不是补强后最终HEAD的CI。后续最终CI/审查/合并身份在PR103单独核对。

## 资格边界

本机Windows11 Home/23H2、4核/8线程、约16GB；不是目标4核8GB。标准UAC、受保护隔离Simulation目录和实际CLI证明对应本机权限/备份/清理/恢复路径，不等于已登记Production安装、真实SDK/RCS、目标工控机、真实24h/72h、工业现场验收或历史D/RV关闭。CI、安装器构建和实际安装也必须各自保留来源及结果。
