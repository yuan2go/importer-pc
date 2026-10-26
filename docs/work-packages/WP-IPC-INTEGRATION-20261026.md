# INTEGRATION-20261026 · 并行任务收口

状态：现场已保全；文档PR114、P0 PR110、Unicode PR113、时间存储PR112、容量PR111及生命周期PR108已合入main，当前main ab7e31a9。PR108三项CI成功；合并后在干净main完成构建与16组回归。原本地F01～F03已在新分支补齐并取得实红/实绿，等待自身PR检查。PR110新头CI三job成功，合并后在干净main重新构建并执行8组P0/安装守卫通过；实际源见[整合执行记录](../runtime/integration-20261026.md)。其余包继续逐项处理，尚无最终整合或现场PASS。用户授权按保护和检查逐项合并全部开放 PR，遇到不能判断的业务/冻结字节/DB结构冲突跳过该项。不得强推、重写已发布历史、删分支/worktree/stash、降低校验，亦不生成本轮发布介质。

起点 `a8cd431456f73e83040f75a0c5e3cc1423440d1b`。远端备份标签 `pre-integration-20261026`。全仓工作树状态、暂存/未暂存二进制补丁、未跟踪文件及冲突文件的原字节位于仓库外 D盘 `audits/pre-integration-20261026`，不上传其中附件或机器资料。新工作树为 D盘 `w/ipc-integration-20261026`，分支 `integration/20261026`。原工作树保持不动。

## 来源、窗口与顺序

基准窗口为新加坡时间 2026-10-23 至 26 日，覆盖 PR90～113、10月25日 ST01～ST14 静态筛查、原长稳 R01～R12、LIA七包、各工作包和回执、近期本地未合并提交。另定点审查 PR50/71 的上传快照。筛查是问题假设，源码和可推翻的测试决定结果。F1～F5中，已取得实际执行提示的P0、卡顿容量、时间存储三组按对应ST编号追踪；原F编号全文映射不能仅从名称猜造，未取得部分明确列为资料缺口。

| 顺序 | 来源与固定头 | 基线/依赖及理由 |
|---|---|---|
| 0 | 当前文档清理 | 先消除README/INDEX过期当前状态，保留冻结证据，独立文档PR |
| 1 | PR110 `bbbc3747` | main `a8cd4314`；输入、清理、TCP声明与监督机制先行 |
| 2 | PR113 `c8ed93d7` | 基于PR110较早`980a1383`，缺后续TCP提供者身份和健康重启预算修复；须先继承最新父成果 |
| 3 | PR112 `4f8eb8e7` | 叠加PR110；时间、存储故障、恢复暂停消费监督机制；转main后验证 |
| 4 | PR111 `4da6bf6e` | main `a8cd4314`；与PR112重叠容量节拍、与PR108重叠投影/备份/详情，放在一起整合 |
| 5 | PR108 `665f43a9` | main `a8cd4314`；生命周期展示消费上述契约，同时保留权限、计划身份、取消和最近备份状态 |
| 6 | 本地稳定性补修 `42e0205a` | 基线`1dfb676a`，尚无PR且原工作树有未完成merge；核对原种子、预检详情、历史校验是否已被新任务覆盖，再单独承接有效缺口 |
| 7 | 最终main干净树 | 完整构建与关键套件、全部意图检查；不生成发布介质、不宣称现场验收 |

原整合分支`0b0231cc`及未提交成果只作经核实的参考：其中已有PR108/111语义整合和统一候选版本，但没有最终集成PASS，且缺PR113。本轮不会把它或其未提交锁文件整体当作正确快照覆盖main。

近期main时间线：10月23日PR90/91/92；24日PR93/94/95/96、ST原审查、PR97/98/100（含99）、101/102；25日PR103/104/105/106/107；26日01:11（新加坡）PR109。各真实commit/merge时间、merge-base、patch-id、删除行blame及双亲选边候选以本轮机器可读审计为准。已在main的任务必须继续进入回归矩阵。

## 意图台账

这里是判定基准；“已有实现”不等于本轮测试通过。最终结论、执行源与日志在整合交付中逐项登记。

| 任务 | 预期行为与不变式 | 文件/符号 | 判定方法 |
|---|---|---|---|
| ST01 / PR109/110 | 独立延后、禁用读码器不强求心跳编号；无活动读码器不发空心跳；待配置拒绝业务 | ParcelCapabilityFactory、RcsConfigurationValidation、ParcelConnections | deferred-connections、p0-deferred、正式provider兼容验证 |
| ST02 | 已核验DB归档的冗余原件暂时占用仅退避；缺失/损坏/矛盾仍停供 | ParcelArchiveCleanup、ParcelHistory、ParcelIntegrationIds | p0-cleanup，锁定/解锁和三类损坏 |
| ST03 | 在线/计划备份完整取消，I/O不无界占gate；失败不写COMPLETED，Stop保有真实所有权 | ParcelCommands、ParcelRepository、ParcelScheduledBackup | latency-storage-checks、生命周期备份状态 |
| ST04 | 大详情仅可见且修订变化时后台生成；完整原文受管理权限与选择代次保护 | ParcelDetailsProjection、ParcelTargets | latency-details-checks、原生选择/撤权/取消 |
| ST05 | 历史count在gate外按修订缓存，多窗共享同次快照；不丢LIA字段 | ParcelSnapshotCache、SharedSnapshotRead、ParcelHistory、ParcelCommands | latency-projection-checks、快照字段断言 |
| ST06 | TCP keepalive/单调接收活性可观测，原Epoch/资源隔离；不重放 | TcpLineObservationSource、ParcelConnections | p0-tcp、p0-tcp-components；自动重连合同另列 |
| ST07 / G01 | Unknown保留原请求、目标、报文及责任；交接无外呼；无假Query/清责 | ParcelStages、ParcelGuards、RequestHandover | p0-rcs、p0-unknown-restart、LIA交接；真实处置需合同 |
| ST08 | 只重试确认未COMMIT且已回滚的本地输入事务；结果不明不重做；永久故障首次阻止拉起 | AtomicStorageWrite、RuntimeFatalMonitor、DesktopRecoverySupervisor | time-storage-atomic/supervisor/process-checks |
| ST09 | 原生SDK未结束时不释放handle/委托/租约，不虚称取消；是否隔离由证据决定 | HikrobotReader及Sdk生命周期 | 阻塞SDK/资源既有夹具；真实SDK挂起/隔离为独立决定 |
| ST10 | 两级预警先于硬阻断，近期增长估算可解释，不删未决 | ParcelStorageCapacity | latency-capacity-checks、配置默认/公式静态核对 |
| ST11 | PASSIVE WAL结果、高水位可观测；同实例仅一导出 | ParcelHistoryQuery、ParcelStorageCapacity | latency-storage-checks、读事务与导出所有权 |
| ST12 | 容量/保留间隔单调；UTC仅审计/日历；合并不改回墙钟 | ParcelStorageCapacity、ParcelHistory、RuntimeJournal | time-storage-fault-checks、回拨/前跳 |
| ST13 | 新回包本地回拨证明绑定原请求/hash；实时与恢复同校验；历史原字节不变 | ParcelClockEvidence、ParcelStages、ParcelReplyIdentities | time-storage-fault-checks，无证明/矛盾继续拒绝 |
| ST14 | 致命退出、有限拉起、健康预算、循环保护；恢复先暂停，不自动供包/重放 | DesktopRecoverySupervisor、CrashDiagnostics、BusinessLaunch | p0-supervisor、time-storage-process-checks、0/2次预算 |
| 快捷方式 / PR113 | Unicode中文创建/读取/清理；错目标/参数/目录/空目标保原件；COM释放一次 | DeploymentShortcuts、ShellLinkFile | 三ACP同DLL、installer-change-guards、生命周期44断言 |
| LIA01 | 部署/卸载原操作核对，计划不换身份，未知不重投，保留系统/文件真实结果 | ParcelStartupServices、DeploymentBatch、DeploymentRemoval | lifecycle-ux、批量计划/Recovery漂移 |
| LIA02 | 完整候选Pending→受控停止→生效；权限到期/撤销立即有效；多窗退出守卫 | StartupWorkflows、ManagementMode、ConfigurationSubmission | lifecycle-ux、原生权限/草稿/取消 |
| LIA03 | 固定查询上界、前后页与对象ID，改条件不能沿旧查询导出 | HistoryEditing、ParcelTargets | 原生history-context、固定范围/选择 |
| LIA04 | 当前说明对应代码；历史证据保持身份；随包映射无死链 | INDEX、release_documents | 本地链接、canonical manifest、文档映射测试 |
| LIA05 | 既有详情中交接原请求；按权限最少必要信息，无外呼/重发 | ParcelTargets、RequestHandover | 同码不同实体、Unknown/迟到、原文权限 |
| LIA06 | 管理模式/独立操作员、真实日志过滤/上下文/导出；帮助支持保持删除 | SoftwareLogPage、宿主XAML、ManagementModePresentation | 原生lifecycle-ux/reliability-logs、静态无旧入口 |
| LIA07 | 清理预检、完整备份核验；最近成功不被失败抹掉；到限不删旧集 | ParcelScheduledBackup、ParcelStartupRetention、RecoveryFiles | 备份取消/损坏/额外文件、持续授权 |
| PR90/91/94 | 四类参数共模、逐页校验、TCP字段与首配回退不丢 | ParameterSettingsControl、FirstConfiguration | 参数/首配关键套件 |
| PR92 | 过期输入不借旧格口，持久输入和命令守卫完整 | ParcelRuntime/Guards、作业投影 | architecture-quality-remediation |
| PR93 | 首帧和启动竞争、单侧故障/停止取消保持所有权 | ObservationSession、双相机装配 | dual-camera-communication |
| PR95/96/97/98 | 有效格口判断链、跨页回执、Noto字体与查询状态保留 | Sportmaster.Desktop、Presentation.Wpf | task-hierarchy-chain、原生日志与字体资源hash |
| PR99/100 | DB9输入/永久身份、诊断分级、双机隔离与到期维护 | ParcelIngress、Retention、RuntimeJournal | win11-stability、SQL1–9逐字节比较 |
| PR101/102/103/105/106 | 输入rowid线性分页、未决保护、完整预算；原大规模失败/成功各守身份 | ParcelHistoryRetention、RetentionIngress、维护脚本 | retention-ingress、查询计划/对账、冻结证据hash |
| PR104 | 取消/健康/Dispose竞争、日志读及单调退避、进程强杀责任 | ObservationSession、SoftwareLogReader、RecoveryFiles | reliability-02、h02-crashes |
| PR107 | 仓/产线、统一台号、7天默认、窗口偏好、quantity=1一次原码请求 | 配置Schema、RCS、DesktopLaunch | parameter-guidance-startup、sportmaster-rcs |
| 本地 F01～03 | 原种子维护升级守卫、可达预检详情、历史原件完整性不弱化 | ParcelStartupServices、StartupSetupWindow、ParcelIntegrationIds | StabilityAuditFixes、StabilitySeedUpgrade及原件反例 |

## 冲突与默认值原则

语义冲突按双方意图合成，先失败后修复；不机械整块选边。版本来自业务csproj/compositions一致性规则，原AGENTS没有自动计算公式。候选采用已存在整合分支的统一SM1.17.0、DirectPlc/WcsToPlc1.11.0、Contracts1.21.0，不因每个PR再提升一次；DB9及SQL1–9不变。锁文件由真实目标restore生成，清单按暂存Git规范字节生成，旧签名/冻结介质不改。

TCP自动重连、Unknown人工清责不能猜测相机重发或RCS权威合同。可明确实现且不越界的能力继续推进；合同不足的项保留开放及拒绝默认。SDK隔离属于需证据决定的任务，不把没有实测的风险当作已证实泄漏。目标机和系统安装资格仍NOT_RUN。原F编号全文无法获取的映射列为资料缺口，不以推测填满台账。

## 先行文档清理

| 文档 | 处理 | 原因 |
|---|---|---|
| README | 合并为现行入口，旧全文按Git原字节归档 | V3形成状态和旧下载曾处在主入口 |
| INDEX | 当前规则/待整合/历史三层；所有旧入口保留在原索引归档 | SM1.11.x旧介质与阶段“当前”混杂 |
| version-compatibility运行说明 | 去除写死的旧当前ABI，指向项目和组合声明 | 实际main为Contracts1.18.0，原首段1.17.0过期 |
| 长稳运行说明 | 当前维护指向PR106最新证据，旧失败保留历史身份 | 仍将PR103失败及中途UAC取消当最新状态 |
| 冻结回执/原件/PASS/介质清单 | 不改不删 | 仅索引标为历史，证据不转记 |

## 验证与回滚

每合入一项即执行已合入任务的相关意图断言；原生Windows验证串行使用全新D盘Simulation根。保留全部失败、取消和NOT_RUN，最终干净main仅做一次完整构建及关键套件。没有设备、SDK、真实RCS、目标IPC、正式安装/任务/SCM/WER、物理断电及24h/72h证据，不授予这些资格。

代码撤回只能在新分支对实际main合并提交做revert PR，并重新验证；不得reset或强推main到备份标签。部署回退必须沿原维护/完整备份/未决责任守卫，不覆盖业务账或回退永久编号。本轮不执行部署回滚。分支、worktree、stash仅列清理建议，不删除。
