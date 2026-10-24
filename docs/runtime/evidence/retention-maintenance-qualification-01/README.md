# 完整历史与受保护维护 CLI：本机补证

本轮产品源码固定为main `5bb032d7354ef619064a015484a5001204edfff5`，DB9；验证夹具是单独新增、按源码及程序集hash记录的测试输入。结果和剩余资格见[验证报告](../../validation/retention-maintenance-qualification-01.md)。原[查询复核](../retention-performance-review-01/README.md)的原字节和SourceCommit保持不变。

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

完成后回到普通PowerShell，逐笔对账退役与完整备份：

```powershell
. ./scripts/project-environment.ps1
python scripts/reconcile-retention-qualification.py `
  --run D:/retention-rerun/evidence/PROTECTED-RESULT.json `
  --output D:/retention-rerun/evidence/RECONCILIATION.json
python scripts/check-retention-reconciliation.py `
  --run D:/retention-rerun/evidence/PROTECTED-RESULT.json
```

对账器只读取关闭且无非空WAL的快照，以Windows共享读句柄拒绝写入/替换；不对活动数据库使用immutable。`--completed-only`必须显式指定，结果标为局部完成，不能将运行中的大case算作PASS。负向检查只修改D盘临时合成副本，验证写入共享拒绝、非空WAL拒绝及篡改退役hash拒绝。

`exercise-simulated-authority`仅用于Simulation夹具诊断，其回执明确`ActualProtectedCli=false`和`ActualElevatedAuthority=false`，不能关闭真实受保护CLI缺口。

## 原始证据身份

第一轮简化输入夹具明确INVALID_FOR_COMPLETE_INPUT_STRUCTURE。其小样本性能仅为组件诊断，大样本未完成；用户明确同意后经标准UAC仅终止该夹具进程40552，部分数据库和日志保留。修正轮使用真实输入信封及完整Observation，逐笔解析、核对hash与归属后独立执行，不能覆盖第一轮失败身份。

原运行文件按执行时字节归档，原始数据库和二进制留在D盘，不加入Git。正式归档清单固定后记录每个文本原件的SHA256和字节数。先行编排脚本的恢复stdout文件名重复，较早case的stdout被覆盖；命令记录和隔离恢复数据仍保留，缺失stdout如实登记，仓库复现脚本已改为每case独立命名。后续补证使用新日志，不伪造原stdout。

## 资格边界

本机Windows11 Home/23H2、4核/8线程、约16GB；不是目标4核8GB。标准UAC、受保护隔离Simulation目录和实际CLI证明对应本机权限/备份/清理/恢复路径，不等于已登记Production安装、真实SDK/RCS、目标工控机、真实24h/72h、工业现场验收或历史D/RV关闭。CI、安装器构建和实际安装也必须各自保留来源及结果。
