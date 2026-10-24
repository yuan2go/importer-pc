# 历史清理查询的独立复核原件

2026-10-24，新加坡时间。此目录归档已完成的独立复核，原始输出来自 `D:/projects/ipc-retention-verification-20261024`。本轮只发布报告、脚本、SQL、合成结果与日志，不重复修改业务代码。完整结论和边界见[复核报告](../../validation/retention-performance-review-01.md)。

## 固定身份

|身份|固定值及适用范围|
|---|---|
|复核源码/main|`12a7cbdc11839dfae7eec88f9514e8559694bdd0`|
|复核完整tree|`94ebc204eb8898fe239e8941d9696e6b807b25bd`|
|原PR99 HEAD|`5074660fd10e7a91d81923edac782891e377a519`，用于修复前SQL|
|实际修复|`464e8e2e225c832f358837afdd28a0772e117a65`，随PR100合入|
|PR100最终HEAD|`ff95d3a384a9440b35d8a3a23aa89d1428b292db`|
|原CI合成源码|`6cebb3015f5a975bb2cd158b5b772ac7fbb60afc`，tree与复核main相同|
|原CI|[40096864628](https://github.com/yuan2go/importer-pc/actions/runs/40096864628)，两项作业及安装器打包成功；Windows Server2025|
|本机|Windows11 22631 x64，SDK10.0.402；实际起止见原运行记录|

`original/` 的28个文件保留原始字节，不修改其中的绝对路径、源码、环境和旧命令。`FILES.json`登记28个原件的大小、SHA256和来源；`original/SHA256SUMS`是原清单，覆盖其他27个原件。[PR100原有配对证据](../parallel-task-integration-01/retention-plan/RESULT.json)保持原位置和身份。

## 原件用途

|文件|用途|
|---|---|
|[REPORT.txt](original/REPORT.txt)|原独立复核报告，包含原本机路径和命令|
|[RESULT.json](original/RESULT.json)|正式计时：3种规模×0/128条Pending×修复前后；VM计数与计时分开|
|[RESULT-timing-with-progress-callback.json](original/RESULT-timing-with-progress-callback.json)|先行计时包含progress回调，独立保留，不混入正式耗时|
|[old.sql](original/old.sql)、[new.sql](original/new.sql)、[prepare.sql](original/prepare.sql)、[schema.sql](original/schema.sql)|实际源码提取的查询、准备及SQL1–9|
|[indexes.json](original/indexes.json)、[prepare-plan.json](original/prepare-plan.json)|实际索引与准备计划；每候选完整计划在RESULT.json|
|[Program.cs](original/Program.cs)、[prepare_probe.py](original/prepare_probe.py)、[MigrationProgram.cs](original/MigrationProgram.cs)|原探针源码；两个原csproj也保留，含历史本机路径|
|[IDENTITY.json](original/IDENTITY.json)、[ENVIRONMENT.json](original/ENVIRONMENT.json)、[REMOTE.json](original/REMOTE.json)|原源码、迁移/依赖hash、环境和PR/CI/安装器快照|
|[runtime-tests.json](original/runtime-tests.json)、[migration-result.json](original/migration-result.json)|四组业务验证和6项真实Repository迁移检查；对应日志完整保留|

## 仓库内复现

只校验原件可在任何支持本仓库Python的环境执行，不编译或访问设备：

```powershell
python scripts/reproduce-retention-review.py --verify-only
```

Windows复现须先在本仓库执行环境脚本，输出目录必须是尚不存在的D盘目录：

```powershell
. ./scripts/project-environment.ps1
python scripts/reproduce-retention-review.py --output D:/projects/ipc-retention-review-new-run
```

脚本校验产品源码与原复核基线一致，在新目录生成适配当前工作树的项目路径，使用当前Release构建的SQLite托管/native依赖。新输出包含查询配对、迁移、四组业务验证日志及`REPRODUCTION.json`，记录本次实际HEAD、命令、起止和退出码。新结果保持新的复现身份，不写回`original/`，也不覆盖已有输出。历史csproj中的绝对路径仅适用于原本机。

不归档DLL、SDK、字体、安装包或合成数据库。完整raw规模维护RTO、受保护`expire-history` CLI全链、目标4核8GB、真实SDK/RCS、真实24h/72h和工业现场验收仍为NOT_RUN/BLOCKED。原安装器NOT_SIGNED、ActualWindowsInstallation=NOT_RUN。本次归档PR的CI独立核对，不替换原CI和SourceCommit。

## 归档工具验证与环境限制

`publication/`单独保存归档时的[新复现回执](publication/REPRODUCTION.json)及[归档工具原始测试日志](publication/archive-tool-tests.log)，不混入28个原件。新复现是在`12a7cbd`工作树加本次归档增量时运行，产品输入与原基线一致；不是归档提交的新冻结发行。8个执行步骤（构建、提取、查询、迁移及四组业务验证）均退出0。

原件hash核对、修改原件必须拒绝的反证、暂存Git原件字节/局部manifest/链接检查通过；发布文档2项测试通过。补充`tools/test_verify_archive.py`运行7项，6项通过，符号链接用例因Windows WinError1314缺少特权报错，整体为FAIL_ENVIRONMENT；没有更改机级权限、跳过或删除断言。该日志保持原字节，CI成功也不将这组本机结果倒写成PASS。此处局部manifest检查不宣称完成全仓历史归档重新验收。
