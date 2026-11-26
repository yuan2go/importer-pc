# Stage 1 文档瘦身与治理报告

分支 `chore/repo-hygiene-before-multi-business`。基线 `600b44f53cdd14b37aac2cd5b1848207ff6deed2` 包含局域网相机发现、当前用户工作站自启及单侧空托盘校准。使用独立工作树，原工作区的未提交内容未纳入本次提交。

## 结果

| 项目 | 治理前 | 治理后 |
| --- | ---: | ---: |
| AGENTS.md（Git 文件内容，UTF-8 字节） | 61,032 | 17,011 |
| 活跃 docs 文件（含索引与构建输入，排除 archive） | 91 | 72 |
| 指定盘点范围内缺少索引的文件 | 47 | 0 |

工程规则缩减 44,021 字节（72.13%），重组成六章。[工程规则](../../../AGENTS.md) 规定平台约束，[架构基线](../../design/framework.md) 区分现有依赖与下一阶段的解耦方向，[四类文档索引](../../INDEX.md) 直接列出全部 71 个活跃目标。编译与发布输入分别标明用途。

盘点从源提交的 Git 树取文件和索引链接。按任务排除 archive、ui、ui-v4、licenses、operator 及索引自身后，共 81 个文件。对照 scripts、.github/workflows、src、tests 的路径和文件名引用，另按发布映射验证动态选取的文件。47 个未索引项中，归档 17 个，保留并补索引 30 个。另有 2 个原已索引的阶段设计归档。

## 默认值、操作与交付路径

- 依据 [RCS 参数](../../../src/Ipc.Application.Contracts/RcsRequestPolicy.cs) 和[心跳实现](../../../src/Ipc.Integrations.SportmasterRcs/RcsHeartbeat.cs)，把新站默认心跳校准为 500 ms，注明明确接单且已有车号等条件下以 250 ms 探测、最长 2500 ms。自动识读与取格口请求各自的 1000 ms 保留。按[原请求的状态转换](../../../src/Ipc.Application.ParcelFlow/ParcelSmArrival.cs)修正了用新车号推断旧请求已绑定的说法。
- 依[采集](../../../src/Ipc.Drivers.Hikrobot.Mvs/MvsAcquisition.cs)、[在位模型](../../../src/Ipc.Drivers.Hikrobot.Mvs/MvsTrayPresence.cs)和[校准命令](../../../src/Ipc.Application.ParcelFlow/ParcelCommands.cs)区分放稳 150 ms、运行判空 300 ms 和显式校准 800 ms。MVID 放稳前轻量采图。两张现场卡片分别说明供包员如何处置、实施人员如何校准空闲工位。
- 依据[个人偏好](../../../src/Ipc.Platform/PresentationPreferences.cs)、[相机发现与绑定](../../../src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.CameraDiscovery.cs)和[受控快捷方式](../../../src/Ipc.Platform/DeploymentShortcuts.cs)，核对新用户 80% 音量、枚举选择到草稿及当前用户自启边界。
- 移除随包说明中误称当前 Parcel 数据库为格式 10 的段落，保留独立 Supply 格式及历史证据。修正 Supply 打包清单中已归档的 Windows 验证资料路径，交付名仍为 WINDOWS-VALIDATION.md。[发布映射](../../../scripts/release_documents.py)
- 扩展[文档门禁](../../../scripts/verify-industrial-docs.py)以核对整个活跃索引、仓库内链接和打包文档源，保留原故障码与手册版本检查。生产代码、测试断言、内嵌 tokens、UI 定稿、hallmark-wpf、许可证和发布 manifest 均无差异。

## 归档清单

原路径相对 docs/，每项用 git mv 移至 docs/archive/ 的相同层级。只修正因本次移动改变目的地的相对链接；同时修复了 17 份既有归档页中的 18 处反向引用。

| 原路径 | 归档文件 | 归档理由 |
| --- | --- | --- |
| `baseline/artifact-checks.json` | [artifact-checks.json](../baseline/artifact-checks.json) | 早期资料盘点、冲突、缺项或一次性检查结果 |
| `baseline/CHANGELOG.md` | [CHANGELOG.md](../baseline/CHANGELOG.md) | 早期资料盘点、冲突、缺项或一次性检查结果 |
| `baseline/CONFLICTS.md` | [CONFLICTS.md](../baseline/CONFLICTS.md) | 早期资料盘点、冲突、缺项或一次性检查结果 |
| `baseline/continuation-checks.json` | [continuation-checks.json](../baseline/continuation-checks.json) | 早期资料盘点、冲突、缺项或一次性检查结果 |
| `baseline/MISSING.md` | [MISSING.md](../baseline/MISSING.md) | 早期资料盘点、冲突、缺项或一次性检查结果 |
| `design/cr-report-resolution.md` | [cr-report-resolution.md](../design/cr-report-resolution.md) | 已处置的 CR 审计表 |
| `design/cr-report-verification.json` | [cr-report-verification.json](../design/cr-report-verification.json) | 对应 CR 检查证据 |
| `design/implementation-principles-review.md` | [implementation-principles-review.md](../design/implementation-principles-review.md) | 一次性实现审查 |
| `design/installation-update-review.md` | [installation-update-review.md](../design/installation-update-review.md) | 覆盖安装复查记录 |
| `design/uninstall-data-boundary-review.md` | [uninstall-data-boundary-review.md](../design/uninstall-data-boundary-review.md) | 卸载 PR 的边界复核 |
| `design/industrial-engineering-01.md` | [industrial-engineering-01.md](../design/industrial-engineering-01.md) | 工业化交付阶段方案（原已索引） |
| `design/framework-decisions.md` | [framework-decisions.md](../design/framework-decisions.md) | 原编号决策表，现行职责已归入架构基线（原已索引） |
| `runtime/industrial-engineering-01.md` | [industrial-engineering-01.md](../runtime/industrial-engineering-01.md) | 对应来源提交的实施与验证记录 |
| `runtime/sm-manual-debug-flow-10.md` | [sm-manual-debug-flow-10.md](../runtime/sm-manual-debug-flow-10.md) | 旧人工输入与调试流程 |
| `runtime/sm-reader-robustness-28.md` | [sm-reader-robustness-28.md](../runtime/sm-reader-robustness-28.md) | 历史识读阶段验收 |
| `runtime/sportmaster-commercial-delivery-01-operations.md` | [sportmaster-commercial-delivery-01-operations.md](../runtime/sportmaster-commercial-delivery-01-operations.md) | 阶段性交付记录 |
| `verification/fresh-baseline-01/README.md` | [README.md](../verification/fresh-baseline-01/README.md) | 旧建库快照及形成时验证证据 |
| `verification/fresh-baseline-01/parcel-before.json` | [parcel-before.json](../verification/fresh-baseline-01/parcel-before.json) | 旧建库快照及形成时验证证据 |
| `verification/fresh-baseline-01/supply-before.json` | [supply-before.json](../verification/fresh-baseline-01/supply-before.json) | 旧建库快照及形成时验证证据 |

既有大文件盘点发现 docs/archive/skills/impeccable/scripts/data/font-index.json（约 1.1 MB）。它已在第三方历史归档中，本次未删除第三方文件。

## 补索引并保留的 30 项

| 路径 | 用途或硬引用 |
| --- | --- |
| [design/application-architecture-22.md](../../design/application-architecture-22.md) | DirectPlc / WcsToPlc 随包 ARCHITECTURE-22.md |
| [design/runtime-state-logging.md](../../design/runtime-state-logging.md) | 当前状态日志与可重建证据 |
| [design/sportmaster-rcs-task.md](../../design/sportmaster-rcs-task.md) | 当前内置 RCS 取格口合同（与同名运行指南职责不同） |
| [design/station-position-control.md](../../design/station-position-control.md) | 当前投递位启停契约 |
| [extensions/developer-guide.md](../../extensions/developer-guide.md) | SM、DirectPlc / WcsToPlc 随包 EXTENSIONS.md |
| [extensions/supply-guide.md](../../extensions/supply-guide.md) | Supply 随包 EXTENSIONS.md |
| [manifest.json](../../manifest.json) | 正式发布清单 |
| [manifest.sha256](../../manifest.sha256) | 正式发布清单摘要 |
| [runtime/application-architecture-22.md](../../runtime/application-architecture-22.md) | DirectPlc / WcsToPlc 随包 APPLICATIONS.md |
| [runtime/architecture-quality-remediation-01.md](../../runtime/architecture-quality-remediation-01.md) | SM、DirectPlc / WcsToPlc 随包 ARCHITECTURE-QUALITY-REMEDIATION-01.md |
| [runtime/code-reliability-hardening-02.md](../../runtime/code-reliability-hardening-02.md) | DirectPlc / WcsToPlc 随包 CODE-RELIABILITY.md |
| [runtime/commercial-productization-01-operations.md](../../runtime/commercial-productization-01-operations.md) | DirectPlc / WcsToPlc 随包 PRODUCT-OPERATIONS.md |
| [runtime/d-drive-install-01.md](../../runtime/d-drive-install-01.md) | SM、DirectPlc / WcsToPlc 随包 D-DRIVE-INSTALL.md |
| [runtime/debug-log-live-01.md](../../runtime/debug-log-live-01.md) | SM 随包 DEBUG-LOG-LIVE.md |
| [runtime/industrial-ci-policy-01.md](../../runtime/industrial-ci-policy-01.md) | CI 选路策略及 test_industrial_ci.py 的路径样本 |
| [runtime/latency-capacity-01.md](../../runtime/latency-capacity-01.md) | DirectPlc / WcsToPlc 随包 LATENCY-AND-CAPACITY.md |
| [runtime/management-password-startup-01.md](../../runtime/management-password-startup-01.md) | SM、DirectPlc / WcsToPlc 随包 MANAGEMENT-PASSWORD-STARTUP.md |
| [runtime/operations.md](../../runtime/operations.md) | Supply 随包 OPERATIONS.md |
| [runtime/p0-stability-01.md](../../runtime/p0-stability-01.md) | SM、DirectPlc / WcsToPlc 随包 P0-STABILITY.md |
| [runtime/parcel12-operations-26.md](../../runtime/parcel12-operations-26.md) | SM、DirectPlc / WcsToPlc 随包 PARCEL14-OPERATIONS.md |
| [runtime/rcs-heartbeat-01.md](../../runtime/rcs-heartbeat-01.md) | SM 随包 RCS-HEARTBEAT.md |
| [runtime/sm-operator-corrections-29.md](../../runtime/sm-operator-corrections-29.md) | SM 随包 SM-SCAN-AND-DEBUG.md |
| [runtime/sm-session-shift-exceptions-25.md](../../runtime/sm-session-shift-exceptions-25.md) | SM 随包 SM-SESSION-SHIFTS.md |
| [runtime/startup-deployment-02.md](../../runtime/startup-deployment-02.md) | DirectPlc / WcsToPlc 随包 STARTUP-DEPLOYMENT.md |
| [runtime/supply-getting-started.md](../../runtime/supply-getting-started.md) | Supply 随包 GETTING-STARTED.md |
| [runtime/time-storage-fault-01.md](../../runtime/time-storage-fault-01.md) | SM、DirectPlc / WcsToPlc 随包 TIME-AND-STORAGE-FAULTS.md |
| [runtime/unicode-shortcut-compatibility-01.md](../../runtime/unicode-shortcut-compatibility-01.md) | SM、DirectPlc / WcsToPlc 随包 UNICODE-SHORTCUTS.md |
| [runtime/windows-application-delivery.md](../../runtime/windows-application-delivery.md) | DirectPlc / WcsToPlc 随包 WINDOWS-APPLICATION.md |
| [runtime/windows-client-compatibility-01.md](../../runtime/windows-client-compatibility-01.md) | SM 随包 WINDOWS-COMPATIBILITY.md |
| [runtime/workstation-status-logs-01.md](../../runtime/workstation-status-logs-01.md) | SM 随包 WORKSTATION-AND-LOGS.md |

## 门禁与未运行项

Windows PowerShell 各检查先执行 `. ./scripts/project-environment.ps1`。

| 检查 | 本阶段结果 |
| --- | --- |
| python scripts/verify-industrial-docs.py | 通过。71 个入索引文件、68 个 Markdown 入口及专题、323 处仓库内链接、47 个打包文档源、5 个 CP 故障码。活跃区零孤儿、零本地断链。 |
| python scripts/test_industrial_ci.py | 4 项通过。原断言未改。 |
| python scripts/test_release_documents.py | 4 项通过。另按四种产品生成文档并回读全部相对路径，SM 35 份、DirectPlc 和 WcsToPlc 各 23 份、Supply 5 份。 |
| 门禁故障样本及归档路径 | 临时样本中的失效链接、孤儿页、缺失打包源均被拒绝。新移入页中的 22 处本地链接可达，改动的既有反向链接逐项对应原目标。 |
| 现场打印稿专项 | 锁定依赖的 test_operator_print.py 共 6 项通过。首次生成发现作业卡超出一页，按现场动作精简后重新生成。作业卡 1 页、部署卡 1 页、实施手册含故障目录 7 页。脚本已核对页数、可搜索文本及嵌入字体。 |
| Git 差异 | 已核对允许的文档/路径脚本范围、空白检查及禁改路径。 |
| 产品构建、核心业务回归、远端 CI | 未运行。上述 CI 选路测试为本机结果。 |
| Windows 客户端原生操作、厂商 SDK / 相机 / PDA、客户 RCS、现场验收与实体打印 | 未运行。 |

核对范围为仓库内活跃文档、发布输入及本次移动影响的路径。历史报告仅证明其来源提交；本阶段未生成安装包或刷新发布清单。
