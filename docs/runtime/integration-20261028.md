# 2026-10-28/29 整合执行与归宿

代码整合检查点为 main `035a125f42445463f835be8d966598353573435e`。本文档收口提交的后续 main SHA 由 Git、PR 及仓库外最终回执记录；以下 SHA 均明确绑定各自树，不声称是包含本文的自引用最终 SHA。

用户最新范围：只保障 **SM / Sportmaster 及其必需共享基座**，其他业务模块开发暂停。独立会话“导出完整UI为HTML操作页”及其工作树明确排除本次冻结；其负责人继续保管产物，本任务不修改。SM 当前生产及初始配置均为 Weight.Source=Disabled、Scale=null、无 PLC；SDK 连续采集/进程内软件读码 → 每次准入独立 RCS 原请求，quantity=1。同码是否业务去重由 RCS 决定，Unknown 保留原请求责任。

## 已执行的合并顺序

| 顺序 | PR/任务 | 实际 main 合并 SHA | 结果/依据 |
|---|---|---|---|
| 1 | [#141 基线验证](https://github.com/yuan2go/importer-pc/pull/141) | `1c9c29f3b9e93e5fa0ea669908a0754429c9d5b8` | 修正当前合同测试适用性；原失败保留 |
| 2 | [#133 ABI/编码边界](https://github.com/yuan2go/importer-pc/pull/133) | `be02baf8f8ee9572614fe8ec5b23d98c7d626e7d` | ABI门禁保留；Broken pipe 的根因与修正属于 HTTP 测试夹具生命周期 |
| 3 | [#139 安装与普通启动](https://github.com/yuan2go/importer-pc/pull/139) | `2bcde8bb063d97dc2907990616426d5e07c9c2c1` | 原操作/CAS/身份守卫、普通令牌、隔离安装生命周期 |
| 4 | [#137 SDK 与 WT049](https://github.com/yuan2go/importer-pc/pull/137) | `1720c44c514f81d6d8348b778fa4d5b1c290476f` | 三处SDK边界及四项未提交源码/测试补修；[来源](integration-20261028-sdk.md) |
| 5 | [#142 WT001 共享预检](https://github.com/yuan2go/importer-pc/pull/142) | `e6d3d05075e9dcf45f8165178bdfc3e91d66cd46` | Supply6拒绝前保全DB/WAL；属于已完成历史整合，不扩大当前开发范围 |
| 6 | [#143 WT024](https://github.com/yuan2go/importer-pc/pull/143) | `3a25d6ba3bea686b6bb7caaef919bb1dec2d4b12` | 监督退出责任、存储分类、登记/迁移租约；[记录](integration-20261028-supervisor-storage.md) |
| 7 | [#144 WT048](https://github.com/yuan2go/importer-pc/pull/144) | `c6e353f318e986a12a775a026f4f3a3c89884e22` | 真实维护阶段、取消/提交边界与Unknown防重；[记录](integration-20261028-maintenance-progress.md) |
| 8 | [#145 WT054](https://github.com/yuan2go/importer-pc/pull/145) | `035a125f42445463f835be8d966598353573435e` | 原件/owner只读守卫、真实取消、最小性能优化；[记录](integration-20261028-history-open.md) |

每项通过正常 merge commit 合入，合并前备份及对应源CI逐项核验，合并后双亲/树核对。未强推、未删除分支、未改写历史。

## PR与本地产物的最终处置

| 原任务 | 归宿 | 未采用/未完成边界 |
|---|---|---|
| #133/#137/#139 | 已按上表合并；原分支与补修来源保留 | 真实SDK/现场资格不转记 |
| [#134](https://github.com/yuan2go/importer-pc/pull/134#issuecomment-6488062808) | 已关闭 SUPERSEDED；资源保有/原帧归还/不可交付结果意图由#137当前实现承接 | 原触发实体、独立decoder/回放框架及池预算兼容缺陷未交付，原头36a98016和bundle保留 |
| [#135](https://github.com/yuan2go/importer-pc/pull/135#issuecomment-6488067451) | 已关闭 SUPERSEDED；用户只保留SDK相机接入 | TCP plain/sequenced/握手/心跳合同完整保留于原头07b352f6，未声称迁入SDK；其他既有TCP组件不删除 |
| [#136](https://github.com/yuan2go/importer-pc/pull/136#issuecomment-6488071683) | 已关闭 SUPERSEDED；紧密Mono8拒绝/归还守卫已由#137承接 | host、VM worker/SDK/许可、配置hash、TCP端点和完整协议/E2E未完成，不记DONE；原头4608f71c保留 |
| WT024/WT048/WT054 | 按上表增量完成当前有效缺项 | 不把WT054的844项中断整合状态整包回灌，不复活旧owner补填 |
| WT049 | 四项源码/测试合入#137 | 新增真机PASS叙述缺独立可核原日志，保留为未核实历史声明 |
| WT012/WT059 | 原UI/审查分别与99a8d62a、4c42f567归档逐字一致，重复保留 | 不覆盖当前V2/V3或用同名审查编号互换 |
| WT021 | 原PR92后审报告保留；165c2ffe与4edee109同tree | 同tree不等于同构建身份，旧介质本轮未重验 |
| WT043 | 四源码/csproj已吸收、两lock仅行尾差异；独有ABI比对脚本归档 | 安装SDK比对未运行，不引入厂商文件，不列为业务缺项 |
| WT051 | 原980a1383回执保全 | 引用原日志未找到，ActualInstallation=NOT_RUN，不能转记当前安装PASS |
| WT055 | 后稿源码已吸收；53旧lock及13文档保留为历史 | 不重复合并旧版本整合树 |
| 两个旧RCS超时分支 | 由PR116后续合同取代，原ref保留 | 不恢复手动授权重扫或旧默认超时 |
| 两个squash来源分支 | PR107/PR109完整tree相等已对账，保留ref | 不以git cherry的独有提交数误判遗漏 |

[完整逐对象清单](evidence/integration-20261028-dispositions.json)记录原119分支、59工作树、11个脏树、17个reflog孤儿及本轮新增ref的归宿。原stash=0。初始#140记录58个工作树；本轮把其已存在的整合工作树WT046一并盘点为59，未把新增HTML任务混入冻结原件。

原119分支分别有本地 backup/20261028 标签，不推送；主bundle、reflog bundle和各次执行bundle在D盘仓库外，均经过 verify。11个脏树的暂存/未暂存补丁、原index、冲突元数据、未跟踪文件及326份ignored证据保全；原1013个工作文件再次核对差异0。受限附件四份仅本地保管，公开清单不列原件内容或指纹。

## 验证身份与边界

PR145头 `71f0bb5845b19c3e36c4180a57da7b709dfbe68d` 的9项CI通过，实际测试合并树 `f988ac16098ef6996b081d6d28ea4df388e9475e`：[商业工程](https://github.com/yuan2go/importer-pc/actions/runs/40817273484)、[SDK](https://github.com/yuan2go/importer-pc/actions/runs/40817273500)。Linux/Windows稳定性各83、监督36/原子17/故障29/进程5、恢复91/备份健康68、隔离安装44/普通子进程4、真实受保护helper48条PASS。PASS是日志断言行，不相加当作独立用例数。helper回执 SourceCommit/FixtureSourceCommit 均f988ac16、ExitCode=0、UnchangedMedium=true；证据artifact12873840038的SHA256为df2e6951c6d8362368058db6ebcf16ad1adc62360cb9aa25f065cbf082bd30a7。

实际 main `035a125f` 的重跑为 **8项通过、1项失败**：[商业工程失败记录](https://github.com/yuan2go/importer-pc/actions/runs/40818596660)、[SDK通过记录](https://github.com/yuan2go/importer-pc/actions/runs/40818596630)。失败定位 `CapabilityClosure.cs:185` 的DirectPlc实测称重夹具：250ms截止，等待准入/快照后投递的新实体已WeightTimedOut。该测试没有配置在SM生产流程；用户已暂停非SM开发，因此本轮不修、不跳过、不放宽它，失败继续保留。不得将此前9项通过转记为main全绿。

WT054配对绑定父c6e353f3与实现07b28f8e的精确干净DLL，同一harness/2000条合成库，AB×5/BA×5各10样本。中位1735.280ms→1311.439ms，本机净下降24.42%；原始样本/哈希见[配对证据](evidence/integration-20261028-history-open-paired.json)。restore为空且范围有重叠，不构成非零恢复压力、目标IPC、容量或SLA结论。

本机Windows BHR历史末轮57 PASS/2 FAIL（缺符号链接特权）及早先提权拒绝回执保留；不调整本机策略来换PASS。隔离CI、普通本机、原型/合成、真实接口和现场分别计证。SM最终检查命令、源身份和本次实际结果由本页后续记录及仓库外终态回执承接。

## 本轮仍未验证或暂停

- 非SM开发暂停：DirectPlc称重夹具失败未修，其他模块不作为本轮交付保证。
- 真实相机/SDK、真实RCS、物理实体、目标IPC、外部卷/物理I/O、24h/72h、交互UAC、第二版本介质升级及恢复启用系统修复仍未验证；不发布新介质。
- RCS权威Query/人工处置合同缺口继续显式保留，不猜造业务规则或自动重发Unknown。
- 原#134预算兼容问题和#136服务缺项仅归档，不能因关闭PR转写为已实现。
- 历史真机声明与原日志未核项保持原证据身份；不修改冻结哈希、原PASS/FAIL、原介质。

## 最终复现入口

Windows先执行 `. ./scripts/project-environment.ps1`。SM构建沿既有Actions：`dotnet build ImporterPc.slnx -c Release -p:Application=Sportmaster -p:BusinessModule=Sportmaster -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false`；核心验证工程单独Release构建。SM选择器包括 mvs-software-flow、mvreader-flow、sportmaster-rcs、rcs-timeout-errors、station-single-camera、deferred-connections、p0-deferred、management-mode、parameter-guidance-startup、stability-audit-fixes。原生入口使用已有 startup-experience / parameter-v3 / hikrobot-sdk-editing；camera-config-startup 不是有效选择器。

文档清单使用 refresh-integration-manifest.py 的暂存Git规范字节重算；当前入口本地链接使用既有release_documents.render核验。冻结日志中63条等号分隔行会命中旧宽泛冲突正则；精确Git冲突标记另核验，不改原件换“无结果”。

## SM检查点本机实跑

对代码检查点`035a125f42445463f835be8d966598353573435e`执行全量Sportmaster解决方案及核心验证工程Release构建，均退出0、零警告错误；文档收口不更改生产源码。普通令牌下以下10组全部退出0、无FAIL；详见[命令、退出码和日志SHA256](evidence/integration-20261028-sm-checkpoint.json)。

| SM/必需基座入口 | PASS断言行 |
|---|---:|
| mvs-software-flow | 11 |
| mvreader-flow | 6 |
| sportmaster-rcs | 156 |
| rcs-timeout-errors | 57 |
| station-single-camera | 52 |
| deferred-connections | 130 |
| p0-deferred | 58 |
| management-mode | 81 |
| parameter-guidance-startup | 23 |
| stability-audit-fixes | 83 |

当前入口README/INDEX/工作包/执行报告及三项增量报告的本地链接使用仓库既有release_documents.render检查；逐对象清单覆盖119/59/17原对象，无空归宿。最终文档PR后，再对干净main确认SM构建及相关回归，将真实SHA、范围和未验证项保存在仓库外终态回执，不以本检查点替代后续实际执行。

## Windows清单检出修正

在文档合并main `769d9cd894ff4d135264a5c2033a90e3641c0a35` 的新干净Windows工作树中，canonical校验实际退出1：`Manifest checksum differs`。Git对象中的JSON仍是LF且与已提交SHA一致；本机普通检出把JSON转换为108484个CRLF，造成工作副本的字节校验失败。没有篡改或重标历史证据。

当前`.gitattributes`对`docs/manifest.json`与`docs/manifest.sha256`精确指定`text eol=lf`，让普通Windows检出保持生成器的规范字节。仅限当前两个清单路径，归档/冻结回执各守原字节规则。使用原生成器重算当前清单；在修正后的普通检出再次运行原`--check`，不能只在手工归一化后的工作副本声称通过。业务源码、测试、配置与CI入口不因本修正改变。
