# WP-IPC-RCS-TIMEOUT-INDEPENDENT-REVIEW-02

日期：2026-10-27。用途：RCS统一超时及开发阶段兼容清理工作包。第1～9.5节保留形成时的核查、实施顺序与风险要求；本次按用户“完成工作包”指令实施，交付状态见9.6，当前操作见[当前合同收口说明](../runtime/current-contract-cleanup-01.md)。未执行系统安装、设备操作或自动合并。

## 0. 最新用户裁决：开发阶段不要求旧版兼容

2026-10-27用户明确：“现在是开发中，不需要考虑兼容的问题。”本裁决替代本包先前要求保留1.3.2/1.3.3调用预算和默认无需变更的实施结论。第1～2节保留先前独立核查的历史事实，不再把旧版兼容作为新实现目标或验收门。

本包当前目标改为：**RCS只维护当前组件与一套请求超时规则**。`request-timeout-ms`缺省5000毫秒、合法范围100～30000；业务层与适配器使用同一冻结请求参数，不按旧版本号退回通用调用预算。清理仅为旧版兼容保留的时限分支、旧组件版本注册/选择入口和对应兼容测试，更新仓库内开发模板及受影响夹具，不新增旧版迁移、桥接或双轨维护。

“不考虑兼容”不等于清空业务账：已发送请求的身份、一次发送、Unknown责任、迟到隔离与停止所有权仍按原请求事实处理。不要自动改写已发送报文、重发旧请求或删除未清偿记录；也不扩大为删除本地数据、改外部协议或放松设备/系统权限。

随后用户要求：“分析下是否可以其他地方保留着这种兼容情况，一起整理到这工作包里。”据此将范围扩大到第9节已核实的同类兼容分支。执行时不只清RCS：按K01～K15逐项处理并记录去向；其中K14先核验是否仍是有意支持的第二布局。业务多实例/多布局机制、当前可选能力和历史原件保护不因名称含“兼容”被一并删除。第9节是本次扩展范围和新旧规则冲突时的依据。

## 1. 历史核查结论与当前实施入口

先前核查确认三个历史问题已由后续提交处理，PR #116已经合并；不重复修复旧CI失败，也不重开#116。**当前应落实第0节新增的单一超时策略**，不能因为旧兼容修复已经存在便结束任务。默认路径为“刷新来源 → 清理旧兼容实现及开发入口 → 定向回归 → 最终HEAD验证”。只有当前代码已经完整满足新裁决时，才可记录`CURRENT_POLICY_ALREADY_IMPLEMENTED`并停止重复修改。

| 身份 | 本次实际核验 |
|---|---|
| 用户提供的历史PR HEAD | `d8079bd747a6dda737997722f38b30489ad38c86` |
| 后续修复及PR最终HEAD | `d485fd7a548cdc944d4e2dad2e47b66ea985e40d` |
| PR状态 | #116 closed、merged；2026-10-26 13:53:45 UTC合并 |
| 首轮独立审查时的远端main | `e70b8ecef3aafb5e11d56624291e4d7dfa9b6185`，即#116合并提交；本次扩展审查已前进至第9.1节来源 |
| 历史失败CI | [40309541459](https://github.com/yuan2go/importer-pc/actions/runs/40309541459)，实际检出merge源`a9dc6c162c4f1c058586655bdeb5d588a5491b20`，Windows job失败，另两项成功 |
| 最终PR HEAD的CI | [40333278916](https://github.com/yuan2go/importer-pc/actions/runs/40333278916)，AQA01、P0、frozen-sportmaster三个job全部success |
| 最终CI实际构建源 | `31b4118292ff146c4e17f73ea3c226cdeb5bccf2`，将最终HEAD合入当时base`77d2db61`的检查合并源 |
| 当时的代码树等价核验 | `d485fd7a`、`31b41182`与当时main `e70b8ece`的tree均为`e763cc88be3d39627b8e4c9a984fcb8b7a789754`；实际git diff均为空，不扩张到后续main |
| 审查线程 | [4376356521](https://github.com/yuan2go/importer-pc/pull/116#discussion_r4376356521) 当前`is_resolved=true`、`is_outdated=true`；bot的旧汇总仍记d8079bd，不作为当前未解决状态 |

树等价证明所审代码内容一致，**不证明不同SourceCommit生成的二进制、安装介质或运行资格相同**。最终CI是31b41182源的实际运行结果，不能改记为e70b8ece二进制已经运行。若后续要求发布新的main介质，再按第6节冻结实际发布源。

首轮核查时工作区属于开放的#118作业/日志任务，另有#117安装任务查询修复。2026-10-27扩展审查已确认二者均合入main，详见第9.1节；不能再把它们写成待合并，也不能回退已合入的日志、管理模式或安装修复。当前工作分支仍停在#118的旧提交，本包实施应另从最新main开始。四份原有未跟踪原稿保持原位、不入Git。

## 2. 独立定位与已取得的证据

### 2.1 旧版预算：历史上是真回归，后续曾按兼容要求修复

历史d8079bd中，`RcsTaskOptions.RequestTimeoutMs`为非空int，省略字段统一默认5000；适配器无条件`CancelAfter`。因此即使业务层保留旧调用方10000毫秒预算，1.3.2/1.3.3在第5秒之后收到的合法回包仍可能被提前取消。审查指出的路径成立。

后续d485fd7a将内部选项改为`int?`：有显式字段先严格解析100～30000；省略时仅1.3.4默认5000，旧版本为null。适配器仅在非空时设置组件截止；原调用token继续关联，`HttpClient.Timeout`保持Infinite，应用层仍按冻结原请求选择有限业务预算。

源码锚点（固定main源）：

- [选项缺省与范围](https://github.com/yuan2go/importer-pc/blob/e70b8ecef3aafb5e11d56624291e4d7dfa9b6185/src/Ipc.Integrations.SportmasterRcs/RcsTaskOptions.cs#L41)
- [适配器条件截止](https://github.com/yuan2go/importer-pc/blob/e70b8ecef3aafb5e11d56624291e4d7dfa9b6185/src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs#L100)
- [业务层冻结预算选择](https://github.com/yuan2go/importer-pc/blob/e70b8ecef3aafb5e11d56624291e4d7dfa9b6185/src/Ipc.Application.Sportmaster/Application.cs#L37)
- [已有旧版延迟回包回归](https://github.com/yuan2go/importer-pc/blob/e70b8ecef3aafb5e11d56624291e4d7dfa9b6185/tests/Ipc.Application.Verification/RcsTimeoutErrors.cs#L133)

本节仅解释历史审查与修复，不要求新实现继续保留旧预算。已读`review-fix/legacy-timeout-red.log`：新增旧版夹具在修复前实际抛TimeoutException。已读修复后的专项原件：1.3.2、1.3.3均以调用方10秒预算接受延迟5500毫秒的有效回包，各只有一次目标请求。5500毫秒跨越错误的5秒截止，能够覆盖审查中的6秒回包风险；不要为复述原例再制造整套重复测试。

### 2.2 TaskHierarchyChain：展示合同已改变，旧断言未同步

历史CI Windows job `121526974002`在TaskHierarchyChain第136行失败。旧断言要求`AttentionReason`包含`SYNTH_CHUTE_CLOSED`；本包产品要求优先显示原message，因此界面应为“合成格口关闭”，code应保留在原回执，而非强制拼入主提示。

最终断言同时核对：主提示等于原message、原`Destination.Code`仍为`SYNTH_CHUTE_CLOSED`、另一位仍为`R-10`。随后继续核对缺taskId的Unknown、不得借用旧格口、非超时未知仍拒绝本位新准入。见[最终夹具第136行](https://github.com/yuan2go/importer-pc/blob/e70b8ecef3aafb5e11d56624291e4d7dfa9b6185/tests/Ipc.Desktop.UiVerification/TaskHierarchyChain.cs#L136)。

判定：该失败由本PR有意改变展示合同而旧验证未同步引起，不能解释为业务拒绝原件丢失或右位被污染。已完成的是更新断言的验证对象，并保留/加强原code和独立右位检查；不是删除断言或改回技术code掩盖问题。若后续回执code、匹配身份或右位事实真的改变，必须修产品/投影，不能套用本结论放行。

### 2.3 LifecycleNative：未进入停止路径，原测试未验证到目标情景

历史CI同job在LifecycleNative第67行失败。单凭当时复合断言无法断定停止守卫回归。后续本机定位原件`native-lifecycle-completion.log`明确显示`stopCalls=0`、`reopen=0`及“只有已保存候选可进入生效流程”：候选B已入账，但ViewModel尚未观察到Pending，激活前置守卫拒绝执行，停止回调尚未调用。

对比base77d2db61与历史d8079bd，产品`ConfigurationActivation.cs`及`SupplyStationWindow.xaml.cs`无差异；该静态事实结合实际stopCalls=0支持夹具前置时序问题，不能仅靠“文件没改”断言一切产品行为安全。

最终夹具先有界等待`CanRequestActivation`和Pending确为B，再调用激活；失败阶段必须`stopCalls=1`、原窗可见且恢复可用、`reopen=0`；第二次成功必须`stopCalls=2`、唯一重开、旧窗关闭。随后读回B、无Pending、保持暂停，所有旧作业仍冻结A。固定250/300毫秒睡眠改为观察真实异步关闭完成，仍有超时上限。见[最终生命周期夹具](https://github.com/yuan2go/importer-pc/blob/e70b8ecef3aafb5e11d56624291e4d7dfa9b6185/tests/Ipc.Desktop.UiVerification/LifecycleNative.cs#L67)。

判定：已定位并修复夹具的候选观察/异步关闭竞态，目前证据不支持将这两处失败归为本PR新增的停止所有权产品回归。停止未完成不释放资源、不重开第二实例的产品守卫保持。已有18项原生生命周期连续两次通过；最终云端冻结工程亦成功。

### 2.4 证据可信度与边界

本次实际读取了两轮GitHub Windows job原始日志（失败job121526974002、成功job121608011168）、审查原文/当前线程状态、主干及两版源码差异、运行说明和复核原件。独立核验`review-fix/FILES.json`列出的16份日志字节SHA256均匹配Git对象；6份TestedSourceFiles按Windows CRLF字节表示匹配，不能把Git LF表示差异误判为源码被修改。

原件入口：[review-fix/FILES.json](https://github.com/yuan2go/importer-pc/blob/e70b8ecef3aafb5e11d56624291e4d7dfa9b6185/docs/runtime/evidence/rcs-timeout-errors-01/review-fix/FILES.json)。该清单以d8079bd标记起点，并列实际修改文件hash，不能误读为所有修复都已包含在d8079bd。记录的专项48、心跳27、TCP7、Unknown重启3、应用生命周期213、原生任务239与生命周期18×2属于既有执行；本次没有重跑，不能将这些数量改记为本次新测试。

## 3. 后续执行必须维持的业务不变量

1. 新请求只使用当前受支持RCS组件。请求超时缺省5000毫秒，显式值100～30000严格验证；不再保留1.3.2/1.3.3的隐含预算语义或自动兼容选择。旧版本配置不得静默冒充当前组件；仓库内开发配置明确更新，未更新的外来配置明确拒绝并说明需按当前模板配置，不新增自动迁移层。已发送请求的冻结配置、版本、实现身份和目标保持原事实。
2. 业务层与适配器通过共同的解析规则从同一冻结绑定取超时，避免两个默认值/校验规则漂移。连接和响应读取受总请求预算约束；检查现有固定10秒连接上限，不能让配置30秒被遗留隐藏上限抢先截断。调用方取消和软件停止仍可提前终止，不要求网络故障必须等满；调整共享HTTP客户端前核实心跳仍有自己的有限预算。
3. 每个原请求先持久化身份与Attempts=1再调用外部服务。超时是Unknown，不是拒绝、成功、物理完成或已结；禁止自动重发、自动清偿、删除旧记录、把新实体绑定到旧requestId。
4. 只有已确认的RCS请求超时可解除该项新准入阻断；新扫码即使同码，也须独立admission/operation/requestId及新冻结报文，审计关联前一超时原件。原Unknown仍占未清偿容量，其他身份、健康、暂停、容量和原件守卫继续约束准入。
5. 迟到响应只属于原请求，不能覆盖新位结果或重分类原件为新作业成功；保留原回包、hash、Complete及响应前缀。非法/不匹配回包、缺失格口、拆包和一般Unknown不能借用超时例外。
6. 重启保留原Unknown和普通暂停；恢复只恢复允许的新准入，不重放旧请求。停止失败时原实例、窗口组、数据库/连接/发送器仍由原owner持有，未实际释放前不得重开。
7. 不改原请求合同、REQ/BR/CAPI/D/RV编号、生产资格或真实设备门禁。第9节允许撤掉旧数据库读写/升级支持，但保留当前Parcel DB9、Supply格式5的真实身份、建库SQL校验和未清偿保护；不为清理重新编号、覆盖原库或自动重建。软件暂停不是安全急停。无厂商SDK/现场许可时使用显式合成端口，Production缺能力拒绝业务。

## 4. 给Codex的执行步骤

### G0：刷新身份并核对当前统一策略

所有Windows命令先执行`. ./scripts/project-environment.ps1`，临时、dotnet/NuGet和证据均在D盘。先检查git status、分支、并行PR及AGENTS；不要reset/clean、覆盖四份本地原稿或在#118工作树提交本包实现。需要实施时使用从最新main派生的`codex/`开发分支/隔离工作树。

重新fetch main及#116，读取PR实时状态、线程与最新CI；记录完整SHA及UTC时间。分别核对历史d485修复与第0节新裁决的实现状态、main与被测源的tree/相关文件差异。不要把本包的“最新”当成下一次执行的最新。

- 若仍有旧版本时限分支、旧组件选择入口或保留旧预算的测试：进入G1；第9节其他残留进入G3。先前绿色CI只说明旧策略曾通过，不能替代新策略验收。
- 若当前代码已完整满足第0节、第9节逐项有明确去向且所需验证来源一致：记录`CURRENT_POLICY_ALREADY_IMPLEMENTED`及依据，不重复改代码。不能只确认RCS就跳过扩展清单。
- 若仅缺最终来源运行证据：进入第6节补证。若展示/停止仍有当前失败，再按G2定位；不要重跑历史红版或强造已经修复的故障。

### G1：统一当前RCS请求超时，清理旧兼容路径

按以下范围实施，不再恢复nullable组件截止或旧版本缺省语义：

- 在`RcsTaskOptions.cs`与`SportmasterFlow.RequestTimeoutMs`中统一解析：字段省略为5000，显式值严格校验100～30000，删除按1.3.2/1.3.3/1.3.4区分默认预算的分支。解析/校验尽量复用一个职责适当的位置，保持UI只调用应用契约。
- 在`SportmasterRcsTaskService.cs`始终应用当前组件的有限请求截止，关联原调用取消；不让较短的通用服务预算覆盖它。连接、发送、body读取共用该次总预算，不按阶段重新计时。核查固定10秒连接上限，按统一策略处理，同时保持共享心跳有限可取消。
- 搜索并清理能力目录、内置/扩展组件声明、兼容清单和参数版本筛选中的旧RCS支持项；仅保留当前版本实际需要的入口。更新可跟踪的开发模板、组合和受影响合成夹具，不修改客户/本地现场文件，不自动迁移未跟踪配置。其他兼容清理依第9节逐项实施，不能直接删除全局框架的所有版本机制。
- 旧版延迟回包必须成功的兼容测试不再是需求，将其替换为当前策略测试；这是用户需求变更，不是删断言掩盖实现失败。原日志、审查、编号和历史失败保持原身份。
- 继续复用已有发送、取消与原件保管机制，不另建发送器、重试框架或第二配置账。显式坏值必须报错，不吞异常或默认回退成功。

若问题出在展示或原件，分层核对HTTP原文→关联解析→StageReply/持久原件→operation投影→左右位UI。核对code/message/关联身份以及另一位事实；不得仅改提示文字使断言通过。

若停止路径实际被调用且违反所有权，沿`CheckActivationAsync → root.Close → Drain → stopOwner → ReopenOwnerAsync`定位；保留管理/暂停/Pending确认，修实际竞态或资源释放。不能放宽CanRequestActivation、吞停止异常、提前closeReady或通过重复Close制造第二实例。

### G2：条件性夹具修复

只有证明确为旧合同/异步观察问题才调整夹具。message主提示与code原件分开断言；停止测试必须先证明确实观察到候选并计数停止回调，再有界等待关闭完成。禁止删除业务、原件、右位、草稿、停止失败或唯一重开断言；禁止无上限轮询、单纯增大sleep或反复重跑直到绿。

维护/日志UI纠偏已随#118合入；按正式合入后的合同处理旧提示断言，不能用本包恢复已被用户移除的常驻横幅。

### G3：执行其他兼容清理

按第9.4节顺序，在同一工作包内拆成可审查的变更组；每组给出“原入口→当前唯一入口→拒绝旧输入的方式→受影响夹具”，不要做一次无法区分责任的大范围替换。更新当前指引、模板、发布选择、CLI和测试调用方，历史报告/原件/哈希保留。第9.3节的保留项需在审查中明确，不能用删除保护逻辑来减少兼容代码。

旧输入识别的结果应是明确不支持并保留原件，不能生成新身份、自动选择新默认后覆盖、开放首次密码设置或将未知状态判为已结。当前格式的取消、故障恢复、数据搬迁和备份仍需可用；它们不是跨版本兼容需求。

## 5. 最小定向回归矩阵

本轮统一策略属于新变更，需验证当前语义；旧兼容绿色结果不替代新结果。只运行下面受影响项，不重跑已完成的旧版修复过程。若G0证明当前策略早已完整实现，才复用同来源证据。

| 编号 | 必须观察的结果 | 入口与执行范围 |
|---|---|---|
| T1 单一默认策略 | 当前组件省略字段时5000毫秒实际到期；通用预算为10000也不能延长该默认值；旧版本入口已移除/明确拒绝，开发模板与能力目录一致 | 调整`--rcs-timeout-errors`，替换LegacyRequestTimeoutChecks的旧兼容成功目标，保留一次发送及原件断言 |
| T2 显式时限 | 显式10000可接受5500～6000毫秒有效回包；100/30000边界及坏值验证；较短通用预算不能抢先；无调用方deadline也有组件截止；更短调用方取消独立传播；body卡住保留前缀与Complete=false；连接没有遗留隐含10秒上限 | 与T1合在同一专项一次；连接预算改动用可控网络/传输夹具验证，普通立即连接的loopback不能冒充慢连接证明，不另造生产重试机制 |
| T3 重扫/Unknown | 双位并发；同码新实体新编号；旧原件/次数/历史/审计关联不变；迟到不覆盖；非超时Unknown/拆包不准入；重启暂停、显式恢复后新扫码、旧请求不重发 | 专项已调用`TimeoutRescanChecks`。仅当该路径/驱动交付/恢复也修改时再加`--p0-rcs`和`--p0-unknown-restart`，不无条件重复整套稳定性 |
| T4 共享RCS生命周期 | 心跳与取格口不混淆、不重复发送或泄漏任务 | 只有共享适配器/取消/生命周期变更才加`--rcs-heartbeat`；仅改WPF等待不触发 |
| T5 原生任务链 | 原message可见、code原件保留、右位结果独立；旧格口不借用；Unknown/拆包守卫不松动 | Windows真实WPF `--task-hierarchy`；改此夹具或产品投影才跑一次，不用网页/CDP替代 |
| T6 原生停止 | Pending B被观察→stopCalls=1失败保留原窗/资源、0重开→第二次成功仅1重开→B读回且旧作业仍冻A | Windows真实WPF `--lifecycle-ux`；提供同源`IPC_FROZEN_DESKTOP`，或用冻结介质`-LifecycleUx`，避免空路径反射误当产品失败 |

测试证据写入新的D盘目录，保留失败、单调耗时、请求/实体ID、接收端目标命令次数、原件hash、左右位快照与必要截图。目标次数按dest_request核对，不能把心跳混入。异步前置条件用状态/事件确认，设独立总体截止；失败后先诊断，只重跑受影响项及必要最终组合，禁止重复无关长稳/全量清理/安装交易。

命令模板（在已固定的执行工作区使用实际绝对路径，不把下面占位符当参数值）：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --rcs-timeout-errors
# 按矩阵风险追加 --rcs-heartbeat / --p0-rcs / --p0-unknown-restart

# 已有冻结介质时，直接对该介质编译和运行同源夹具：
./scripts/verify-commercial-wpf.ps1 -Media <冻结介质绝对路径> -Output <新的D盘目录> -TaskHierarchy
./scripts/verify-commercial-wpf.ps1 -Media <同一介质绝对路径> -Output <另一新的D盘目录> -LifecycleUx
```

先核对脚本所需SDK、源提交可用性及介质SHA256SUMS。不同夹具源仅可显式使用FixtureCommit并分别记录身份，不能标成同源。源码WPF运行须构建同一应用/夹具，Lifecycle反射指向该次桌面DLL；不拿当前#118输出替代main介质。

## 6. 最终HEAD、合并及发布门

**#116已合并，不再合并或重开该PR。** 当前单一策略清理应在最新main的新开发分支形成独立可审查变更。以下为该变更的合并门，也是需要新main介质时的补证要求：

1. 产品修复与必要夹具完成后固定完整HEAD S；记录base、HEAD、实际检查merge源M、相关tree与配置/公共依赖。任何影响执行的后续变更都使旧结果退出“最终”身份。
2. 在最终S或其真实检查合并源M构建并运行所需T项一次；冻结介质、夹具、manifest/库存及DLL来源可追溯。文档收口提交可保留产品S，但明确只是文档后续，不篡改二进制SourceCommit。
3. 最新候选的必需CI完成且成功；不得引用d807旧失败的其他成功job、早期本机PASS或旧HEAD绿色充当最终成功。AQA01、P0、Windows冻结工程分别核对。工程资格通过与Production因真实条件不足拒绝必须分开，不能将拒绝/异常进程退出都算PASS。
4. 审查结论有代码和验证证据支撑，线程真实解决；不因为bot旧汇总滞后重复实现。若main在验证后前进，审查实际差异；只有触及相关合同/依赖/状态/夹具才追加相应T项，不机械重跑无关模块。
5. 原请求、一次发送、Unknown容量责任、独立重扫、晚到隔离及停止所有权均满足第3节，第9节清单逐项有代码/验证或保留依据，无已知相关FAIL或结果不明；遵守main保护，不强推、不代签批准。
6. 如要交付新e70b8ece或后续main介质：重新从该完整源冻结并做必要发布启动/定向冒烟，记录实际SourceCommit和SHA。代码树相同可减少无关重复，但不把31b41182的安装EXE哈希、运行结果改记给新介质。新安装、升级/回退另需获授权环境，不在本包暗中执行。

形成工作包时，历史问题已有修复的结论不代表当时统一策略已经实现或验收；当时没有运行新策略或新main二进制。本轮实施及运行结果见9.6，真实现场仍未验。无需为证明安全合并而关闭原D/RV或生产资格缺口。只有G0证明新策略已完整实现时才走`CURRENT_POLICY_ALREADY_IMPLEMENTED`出口。

## 7. 现场仍需验收

真实RCS的网络/连接/响应读取超时、远端在本地超时后可能已经接受/执行的事实、晚到回包与人工核对流程，须用获授权真实合同和现场系统验证；新扫码不等于原件物理取消，不承诺上下游物理效果exactly-once。

真实相机/SDK的实体边界、连续同码、重复帧、断线重连、双位并发与容量压力；目标Windows工控机上的停止失败/恢复、DPI/触控/键鼠及资源排空；实际安装/升级/回退、SID/ACL和系统入口；生产资格、签名及现场安全联锁分别验收。不得以回环HTTP、构建、CI或软件暂停替代。

沿用原D-02真实契约/成功语义、相关设备及实体决定和RV责任，不编造批准人或把历史OPEN直接关闭。原版本兼容套件因机器策略未配置而停止的记录仅保留为历史，不将旧版本兼容套件作为本轮必需目标；系统身份、权限和生产能力门禁仍须保持，不为过测试部署或绕过机器权限。

## 8. 交付清单与可直接执行提示

交付应包括：实时身份/修复是否仍在的核查表；问题三分法（真实产品回归、展示合同不一致、夹具前置竞态）；必要的当前红→绿证据；最终源/夹具/CI/介质身份及命令；修改路径和hash；未验项。不复制客户原稿、私链、账号数据、SDK/字体/安装包或大型数据库。

给执行Codex的提示：

> 按第0节最新用户裁决执行：当前是开发阶段，不维护旧版本兼容。先刷新main及并行PR，保留#116已经修复/合并以及#117/#118已合入的事实，但不要因此跳过当前策略清理。统一RCS请求超时为组件参数：缺省5000毫秒、合法100～30000，业务与适配器同源解析。再按第9节K01～K15及执行顺序处理其他已核实的兼容残留，更新当前模板、调用方、文档与必要夹具，不新增迁移层。用第5/9.5节的定向验证及第6节最终HEAD门收口；每项必须有去向，只有当前策略完整实现才输出CURRENT_POLICY_ALREADY_IMPLEMENTED。保留原请求身份、一次发送、Unknown责任、独立重扫、迟到隔离、原件与停止所有权；不自动重发、不删除未清偿记录、不放宽守卫，不覆盖本地原稿，不自动合并其他PR或控制真实设备。

## 9. 扩展审查：其他兼容残留及统一清理范围

### 9.1 本轮来源、方法和结论边界

2026-10-27本地时间核查，记录UTC `2026-10-26T22:12:09Z`。重新fetch后main为`05f28a7650dbeaefa8d96a8173edcd626f635b32`：#117安装修复先合于`a0abe13a`，#118经整合提交`4326b9a1`合于`05f28a76`。GitHub开放PR查询本次返回空。下一执行仍须刷新，不能把这一快照永久当成最新。

当前工作分支仍是`codex/workstation-status-logs-01`，HEAD为`7f161d367bf2389cd6f1405d3f4bddf38007d300`；与上述main的产品源差异只有已合入#117的`StartupSetupWindow.cs`和`DesktopRecoveryInstallation.cs`，本节定位的其他产品文件与main一致。下表行号是该main快照的源码锚点，执行时按符号重新定位。审查覆盖`src`、`config`、`examples`、`scripts`及相关测试，搜索兼容/旧版/legacy、版本判断、迁移、缺字段回退后读取具体分支和调用关系；不是把搜索命中都判为应删代码，也不声称已证明所有路径可达或穷尽全库。

**确有多处同类残留。** 其中部分仍是正常运行入口，部分只负责旧输入保全，另有一些名称含“兼容”但实际校验当前组合。以下15项纳入同一工作包；已经被后续提交清理的项只补来源记录。此次仅静态分析和工作包修订，不将任何项标成已实施/已通过。

### 9.2 逐项处理清单

| 编号 / 优先级 | 已核实的实现与影响 | 执行目标及限制 |
|---|---|---|
| K01 / 第一组：RCS版本与时限 | `RcsTaskOptions.cs:41`、`SportmasterFlow.RequestTimeoutMs`仍按1.3.4/旧版本分支；`ParcelCapabilityFactory.cs:183,188,305`仍接受并展示1.3.2/1.3.3/1.3.4，旧版本隐藏新时限字段；`config/compatibility/sportmaster.json`多个集合重复保留旧版本 | 按G1只维护当前组件和一套时限。目录、静态校验、内置/签名包、字段声明与模板同步；不能只改默认值却保留旧版本可选项。`BusinessCompatibility.Require`的精确组合守卫保留。 |
| K02 / 第一组：旧RCS quantity校验分支 | `RcsTaskOptions.cs:14,36`仍接收`LegacyQuantity`；`RcsDestinationProtocol.cs:12,25`带`verifyLegacyIntent`，`SportmasterRcsTaskService.cs:90`允许通过旧quantity重建报文比对；`ParcelCapabilityFactory.cs:31`另有旧签名配置数量阻断 | 当前新请求仍按已确定的一次扫码一个包裹生成`quantity=1`，移除当前配置对旧quantity参数的接纳和旧格式发送入口。冻结旧报文不能重写为1再发送；不受支持的原件只保全/明确阻断，不清偿Unknown。不得借清理改外部字段、数量业务规则或放开一次发送。 |
| K03 / 第二组：配置schema1/2与单读码回退 | `ParcelConfigurationRules.cs:17,19,60`接受1/2/3，schema1隐含UntilDispatch；`ParcelBindings.Readers/Positions`在无Topology时使用`Reader`和`main`；`SupplyStationViewModel.cs:19,253`把左位改为兼容main，右位失去对应位置 | 当前Parcel配置统一要求schema3及完整拓扑。Sportmaster保持left/right；DirectPlc、WcsToPlc按各自`PositionRoles`保留main，不强行改成双相机。删旧schema的运行/保存入口及UI伪装映射，旧输入在资源创建前拒绝。当前schema3的`Reader`锚点及默认相机/秤仍有真实消费者，先统一当前调用再去冗余，不能只删除一致性检查。 |
| K04 / 第二组：旧参数表单和双编辑实现 | `CapabilityEditing.cs:178,210,217`进入`StartLegacyConfiguration`、保存`BuildLegacyConfiguration`并生成schema2；`StructuredConfiguration`、`ConfigurationReviewPresentation`及`MaintenancePage.xaml.cs:113`仍维护“旧版表单/旧版兼容参数” | 当前只保留参数V3/首配共模及同一完整候选，撤掉旧表单、角色绑定开关和对应导航。高级JSON若是当前受控工具可保留，但必须走同一schema3校验，不能成为旧格式逃生口。保存核对、草稿保护、修订冲突和受控重启继续有效。 |
| K05 / 第二组：旧管理员账号转换为管理密码 | `LocalManagementMode.cs:68,82,189`解析`administrators.json`并执行一次转换；`WorkstationAdministration`两条契约、`ParcelAdministration`、`ParcelManagementMode`、Broker的`ConvertLegacy` RPC、`ManagementModeWindow.cs:28,108`形成完整入口链 | 删除转换流程、旧账号输入、RPC及当前调用方，只维护管理密码/临时授权。遇到旧凭据或已配置标记，明确不支持并保留原件，不能当作从未设置而开放重置。保持锁定、会话到期撤销、普通启动、受保护服务/管道身份及操作员身份独立。 |
| K06 / 第二组：仍生成但不运行的MaintenanceLogin | `SportmasterFlow.InitialConfiguration`和开发模板仍生成`MaintenanceLogin=reference-login`；`ParcelBindings.All/Bound`与能力编辑继续包含它；`ParcelCapabilityFactory.cs:467`明确不创建该认证端口；#118的`IsLegacyMaintenanceOnly`仅隐藏页面 | 从当前初始模板、参数角色/库存和无消费者的维护认证契约清理这条旧链。正常操作员`Login`不能删除；同一实例若还被操作员角色引用不能整条移除。旧字段存在于冻结记录时不能改写历史hash；新配置不再制造该组件。保留真实管理模式授权，不能把“清理认证”理解为免密码进入设置/日志。 |
| K07 / 第二组：缺省条码筛选走LegacySingleCode | `BarcodeFiltering.cs:19,34`对null走严格单原始码；`FlowDefinition.cs:10`默认调用null；Sportmaster初始配置也可能无筛选；`StructuredConfiguration.cs:68`把两个空规则保存回null。故它既被注释为旧兼容，也被当前UI持续生成 | 收敛为明确的当前“未配置筛选时严格单码”语义和明确的已配置筛选语义，移除按历史版本解释及Legacy状态/文案。必要时用显式当前策略表示，不能直接将null替换成全匹配：一个有效码加一个无效候选时，旧严格分支与筛选分支结果不同。保留多码/重复码/非法码/候选截断/正则超时拒绝和当前允许/排除规则。不得臆造现场正则。 |
| K08 / 第一组：宿主旧契约例外和二进制桥接 | `ExtensionContractCompatibility.cs:7–9`允许固定历史`Delivery12`哈希的observation扩展；`Ipc.Adapters/Compatibility.cs:2–7`转发6个已搬迁类型；`ParcelCapabilityFactory.cs:16`保留旧三参数构造函数以供已编译调用方 | 去掉历史契约哈希特例和仅为旧二进制服务的桥接，当前扩展按当前公共契约重建并完成精确闭包核验。源码调用方/项目引用先更新，再删除空桥接程序集和发布选择。`Ipc.Adapters`命名空间本身不等于旧DLL，不能误删Drivers/Integrations中的当前实现。签名、hash、依赖、RID/架构和装配守卫继续严格；不重签覆盖旧扩展版本。 |
| K09 / 第三组：旧数据库读写、升级和专项修复 | `ParcelRepository.cs:98–158`允许目标DB2～9并在启动执行迁移；`ParcelHistory/ParcelIntegrationIds/ParcelReceiptCustody`有旧格式分支；`ParcelLegacyRecovery`、`ParcelLegacyReceiptRecovery`及`ParcelCli`含旧原件/投影修复。Supply当前格式5，`Maintenance.cs`支持1～4前向升级，`Store.cs`恢复副本也调用迁移 | 当前运行/恢复仅接受各业务当前格式，撤下跨旧格式升级与旧写入修复入口，调用方和夹具同步。旧库在只读预检明确拒绝，不能先开启写连接/WAL再失败。保留当前格式建库、完整备份/恢复/隔离/原件核对及不明责任阻断。DB9/格式5建库仍使用原SQL链及checksum，这些不是“可以删除的旧版本支持”；详见9.3。 |
| K10 / 第三组：旧配置账自动补确认记录 | `ConfigurationRevisions.cs:109–134`为无Confirmation的已有账按LegacyEnvelope/PriorActivation补元数据；同一方法也负责新Seed首次启动，`RecoverInterruptedActivation`负责真正的中断恢复 | 去掉仅为旧配置账升级的接纳路径，当前新账从创建时写完整确认元数据；未知/缺失必要元数据的已有账明确拒绝并保全。必须保留新站首启资源验证、首次配置修复、Pending失败留Effective、停止失败与崩溃后的确认恢复。不能整删`PrepareEffectiveConfirmation`或将已有Effective字段直接当成功证据。 |
| K11 / 第四组：参考Supply旧请求/健康接口 | `IntegrationRuntime.cs:130–141`在原请求无Implementation时拼出`legacy.contract1/legacy-unrecorded`；`StationClosure.cs:117`为旧合成调用者补epoch；旧bool WCS健康仍可投影成DeviceState，`ResponsePersistence.cs:48`含旧回执围栏接续 | 参考业务仍在仓库当前构建范围内，清理其旧调用契约和缺身份执行回退；合成夹具改用当前冻结实现、真实epoch确认及服务健康事实。缺实现身份的旧请求不借当前目标、不推测身份。旧回执围栏先识别并阻断/保全，再撤掉旧自动接续；当前回执丢失/写入失败保护保留。不要因此删除Supply或另造一套主框架。 |
| K12 / 第四组：历史查询向旧接口降级 | `ParcelTargets.cs:94–113`在没有会话查询契约时退回不可取消的Query/metrics，在完全没有查询契约时再用`HistoryAsync`；代码明确注释legacy端口不能取消I/O | 当前作业记录统一到支持固定范围、分页、取消的会话契约；装配或页面入口明确检查必需能力，撤掉旧查询路径并更新当前实现/测试替身。仍保留取消后结果隔离、读操作排空、页面代次及同一查询范围。不要删其他有意可选的能力接口，见9.3。 |
| K13 / 第四组：旧安装计划与快捷方式 | `DeploymentBatch.cs:64`接受无OperationId的旧计划预检/识别后拒绝新提交；`DeploymentShortcuts.cs:12,68–75`保留null历史默认和多套旧命名清理 | 当前生成/提交只接受明确OperationId、当前选项和登记格式，删除旧计划成功解释与旧命名迁移分支，保留最小识别/拒绝。当前安装中断、已提交回执幂等识别和同版修复不是旧版兼容；#117修复必须继承。旧文件不自动删除；现有快捷方式目标/参数/归属核验不可放松。 |
| K14 / 需先判定用途：Sportmaster classic入口 | `Presentation.cs:12`仍有`--ui classic`，`DesktopLaunchOptions.cs:33,41`允许；`package-application.py`明确将其称为Explicit compatibility；该入口与supply-station共享同一Application | 默认列为旧页面退出候选。先按V2字段/操作迁移表确认当前主界面已覆盖其必要能力，并核对它是否仍承担用户明确保留的第二布局。只服务旧页面时从Sportmaster撤下并更新启动/打包/生命周期夹具；若仍是当前批准布局，记录证据为保留项。不能删除DirectPlc/WcsToPlc仍使用的classic公共Shell，也不能因“无兼容”删掉当前功能。 |
| K15 / 第四组：工具别名与历史资格记录路径 | 应用/原生验证Program仍将`--operator-administration`映射到`--management-mode`，`verify-commercial-wpf.ps1`保留OperatorAdministration别名；`application_compatibility.py`能读取旧schema资格记录，要求独立可信原hash | 当前CI/脚本统一现行命令，删仅为旧调用保留的别名；新发布/资格生成只走当前schema和输入绑定。历史证据核验与生产资格接纳分开：保留历史只读核验所需工具或固定Git来源，不能把旧记录自动补成新PASS，也不能把历史原命令/日志批量改写为新名字。 |

K08是装配边界的实际例外，不只是注释。K07、K09、K10最容易在“删除兼容”时误改当前业务或毁掉建库/恢复流程，应按真实调用图拆解。K03与K04可同步处理，但新schema3仍存在的冗余锚点应先迁移当前源码调用方，不开展客户/本地数据自动迁移。

### 9.3 不应误删的机制，以及需要单独说明的相似情况

1. **当前版本匹配仍需要。** `BusinessCompatibility`、`ExtensionPublicContracts`、发行SourceCommit/DLL哈希、签名与依赖闭包用于确认当前组合可运行，不等于承诺向后兼容。删除K08的历史白名单，而不是让任意扩展加载；版本号不能统一写成“latest”或删除校验。接口形状变化时统一重建受影响业务、桌面、内置/示例扩展和同源夹具，依法保留旧产物原身份。
2. **数据库的迁移SQL同时用于新建当前库。** Parcel空库依次应用SQL1～9，Supply空库应用原schema和格式2～5；现有当前库也用这些字节校验自身。可删除“从旧库继续升级/读写”的路径，但本包不重写SQL、不重编号、不更换当前库hash。用“新库初始化”“当前格式运行”“旧/未来/外来格式只读拒绝”区分入口。旧数据不能静默重建；即便库号是9，冻结操作内的旧配置/实现也不能被自动改成当前值。失去可执行解释时保留原件并阻断相应执行，不能解释为已结或允许重发。
3. **配置失败回退是当前可靠性需求。** Pending启动失败保留上次确认的Effective、未保存草稿保护、首次启动失败修复、同版备份还原和恢复后暂停继续保留。它们处理的是当前软件的成功/失败事务，不是旧版本运行规则。K10不能绕过资源确认或管理模式。
4. **留空保留天数目前是显式产品选项。** `ParcelMaintenance.cs`中的nullable天数、`StructuredConfiguration.cs:63–64`和`ParameterSettingsControl.Policy.cs:39–40`明确允许“日志按容量轮转 / 已结历史不清理”；新站默认7天已存在。不能仅因注释写旧配置就把所有null改成7并自动删数据。本包保留这一当前选项，清理“旧版”措辞；若将来决定取消“不开到期清理”，应另有明确产品裁决。已结到期仍须维护窗口/备份，未决、防重和编号责任不删除。
5. **供包台编号的重复键不是单纯旧分支。** `ParameterOwnership.CommonStationParameter`已让同一候选编辑系统StationCode及left/right-station-code；`RcsTaskOptions.Position`仍从组件左右键取值，心跳从StationCode取值。全部当前模板/校验/冻结请求也依赖这些键。可在K01/参数组列入一致性核对，但本包不机械删键或互换编号。若实施当前内部模型去冗余，须同时更新取格口/心跳冻结映射，验证两者一致且原请求不变；外部合同和左右bcr仍独立。
6. **真实可选能力不等于旧接口退路。** `SoftwareLogRuntime.cs:28`的`IRuntimeDiagnosticsControl`用于区分运行中的日志热开关/实时流与离线启动维护的历史日志查询；离线服务没有运行流是正常能力边界，应保留。类似地，延后连接、操作员可选登录、图像可选能力和不同业务的main/left/right不应被本次统一策略删掉。
7. **保护性拒绝不能当兼容实现删掉。** `LegacyConsumersUnverified`防止在未知消费者库存下卸载精确包；旧凭据/权限标记防止首次设置绕过；原件、SID/ACL、恢复围栏、旧请求冻结身份、异步任务排空和迟到隔离保护的是实际责任。保留最小识别、拒绝与保全逻辑，撤掉使旧输入继续运行/转换成功的路径。只是历史命名的`ImporterPc.Administrator.*`服务/存储根不要求为更名而更名；当前安装、授权及修复仍使用它们，名称本身不构成兼容分支。
8. **平台适配和证据历史继续分开。** Windows版本/架构检查、Unicode快捷方式支持、真实SDK能力限制是运行环境范围；不能按“开发无需兼容”全部撤掉。历史文档、Release、CI日志、原始hash及D/RV保留，当前入口加本包取代说明，不重写历史结论。

### 9.4 实施顺序与完成判定

1. **固定当前策略和源码边界。** 刷新main、所有开放PR，逐条登记K项是否仍存在；在最新main的独立分支实施。将本节裁决链接到INDEX、当前版本/管理/配置/运行指引，原历史材料保留。先列出当前必需格式和契约，禁止靠通配符或“读到什么就兼容什么”代替清单。
2. **第一组：K01/K02/K08。** 统一RCS规则和当前组件集合；去掉宿主旧哈希特例/已编译调用方桥接。先更新源码调用方和构建声明，再重建当前扩展。当前真实SDK缺失时给出明确BLOCKED，不能靠重开旧哈希例外过关。
3. **第二组：K03～K07。** 收敛当前配置、管理模式和参数入口，清理ghost维护认证及旧表单。更新全部受影响的仓库开发模板和合成配置生成器；保留当前其他业务的合法拓扑。UI修改前按AGENTS读取V2/V3实物和项目WPF skills，此处静态清单不能代替交接字段核对。
4. **第三组：K09/K10。** 收紧旧库/旧配置账接纳，保持当前格式的初始化、启动失败/激活/恢复闭环。先做只读拒绝与当前初始化定向验证，再删除无当前消费者的旧写入路径；不得使用用户本机运行目录来试验。
5. **第四组：K11～K13/K15；判定K14。** 清当前参考业务、查询接口及工具/安装入口。K14若确认退出，仅撤Sportmaster选择并核对功能覆盖；有当前多布局依据则明示保留。安装验证默认用合成目录/受控夹具；不为本包自动执行系统安装或改SCM/受保护根。
6. **一次收口。** 每项以`已移除并验证`、`当前机制，附保留依据`或`受阻，附必要证据/依赖`登记，不允许以“没用到”代替调用方核对，也不能未完成就宣称全库清理完成。同包可拆审查提交/PR，依赖清楚；每组针对风险验证，最后只对最终HEAD做必要组合，不为每个小提交重复全部长稳/安装测试。

执行源码搜索至少覆盖：`src`、`config`、`examples`、`scripts`、当前CI和测试构造器；历史`docs/archive`、已冻结evidence及发布快照只登记，不批量替换。`legacy`等关键字零命中不是验收目标；证明当前入口唯一、旧输入明确拒绝及保护不退化才是。

### 9.5 扩展范围的定向回归

第5节T1～T6继续按实际改动触发。下表补充新范围，不要求盲跑所有历史兼容套件；旧“升级/转换成功”样例改为“明确拒绝、原件未动”，并为当前成功路径保留等价业务验证。单纯删除整套测试、把失败改成跳过不算需求更新。

| 编号 / 对应项 | 必须验证 | 现有定位与执行范围 |
|---|---|---|
| T7 / K03、K04、K06、K07 | 三个Parcel业务当前schema3模板各自可验证；旧schema1/2在创建资源前失败；Sportmaster左右独立；V3/首配保存同一候选且无维护认证残影；无筛选严格单码与显式筛选的边界保持 | `ParameterRefactor`、`ParameterInheritance`、`ParameterGuidanceStartup`与相关条码夹具；选择受影响case，必要时给它们增加一个定向入口。原生`-ParameterV3`/`-ParameterGuidanceStartup`按涉及页面选择一次，复用T5的左右位验证 |
| T8 / K05 | 当前密码初设/进入/改密/到期撤销/重启普通模式；旧凭据不能转换，也不能被当作首次设置覆盖；日志/参数守卫仍有效；受保护服务不回落可写本地账 | `--management-mode`及原生`-ManagementMode`。只在RPC/服务边界变动时追加`AdministratorBoundary`受保护验证；没有获授权环境时明确未验，不自动装服务。旧转换成功断言替换为拒绝与原件hash断言 |
| T9 / K08、K01 | 当前精确契约扩展可静态检查/合成加载；Delivery12哈希和未知契约均拒绝；删除转发DLL后所有当前组合能构建，发布闭包无缺依赖；外来旧二进制不被自动桥接 | `ExtensionVerification`、Provider identity/组合声明及相关Python打包测试；构建受影响项目/选择。该验证针对当前闭包，不声称真实SDK或现场扩展通过 |
| T10 / K09、K10 | 新空库建成当前格式，当前库重启/备份/恢复可用；旧/未来/外来库与缺确认账只读拒绝，DB/WAL/配置/回执hash不变、零外呼；原Unknown及编号责任不丢；Pending失败、首次修复和停止所有权正常 | 在现有UpgradeConflictClosure、CommercialProductization、ConfigurationRevisions/生命周期夹具中替换过期升级目标、保留当前恢复验证。使用小型合成库，覆盖一个实际旧库＋边界旧/未来格式即可，不重复全部历代升级组合；T6仅在原生生命周期受影响时追加 |
| T11 / K11、K12 | 缺冻结实现拒绝执行且不借当前目标；epoch/健康过期守卫有效；查询只有当前会话入口，取消、代次、固定筛选和停止排空仍成立 | 参考`IntegrationRuntime`、`HealthRenewalVerification`及应用历史/可靠性夹具，更新所有受影响替身；无须运行无关大库性能测试。保留实时调试日志在运行服务可用、离线历史查询正常的检查 |
| T12 / K13、K15 | 当前计划必须有操作身份/明确选项；未知旧计划不生成新身份、不重复安装；当前提交回执重复识别和安装修复仍正确；快捷方式冲突不覆盖用户目标；CI仅用现行别名 | 安装计划/`InstallerOptions`、`ShortcutLifecycle`、#117 task-lookup与安装状态夹具按实际变化选取；用合成文件/回调，不自动执行系统交易。历史日志仍按原命令/源核验 |
| T13 / K14 | 若退出classic，Sportmaster默认/显式当前入口和多窗口关闭/停止均正常，旧参数明确拒绝；DirectPlc/WcsToPlc仍可用公共Shell，V2迁移字段/操作无缺失 | `DesktopLaunchOptions`、`--sportmaster-window-lifecycle`及受影响原生导航/窗口用例。若保留为当前布局，交付当前依据并验证仍共享同一Application，不能把它写成已删除 |

格式/接口/公共契约变化时，受影响项目构建是必要的；只有声明/文档变化不增加运行测试。新增测试入口须在交付中给出实际命令和范围，不能在工作包中虚构已经存在的开关。最终HEAD的所需CI、源/夹具/介质身份和现场未验项仍按第6～7节执行。

### 9.6 本轮交付状态

本轮从已含PR116、117、118的main `05f28a7650dbeaefa8d96a8173edcd626f635b32` 开始，开发分支为 `codex/current-contract-cleanup-01`。已实施K01～K15；没有重复实现三个已合并PR的主体，也没有把旧版本兼容恢复为目标。

| 项目 | 实施去向与验证重点 |
|---|---|
| K01/K02 | `RcsRequestPolicy`统一当前1.3.5及5000毫秒缺省、100～30000范围；业务/适配器同源，连接计入总期限；旧版本/quantity拒绝。定向验证覆盖5/10秒差异、超过10秒连接、早取消、一次发送、独立重扫与迟到隔离。 |
| K03/K04/K06/K07 | schema3和显式拓扑、V3完整候选、严格单码当前策略；退出旧表单、维护认证角色及隐式main映射；操作员登录仍独立保留。参数、首启、任务布局及状态/日志定向回归保留完整候选和草稿保护。 |
| K05 | 删除旧账号转换契约、RPC及界面；旧凭据/旧标记拒绝覆盖；当前锁定、会话到期撤销和普通模式启动保留。会话ID区分相同到期时间的不同授权，参数读取拒绝跨授权结果。管理模式专项通过；受保护安装服务仍以Windows授权CI/现场为证据。 |
| K08 | 删除Delivery12白名单、工厂/扩展清单旧二进制桥接、Adapters转发程序集；Parcel公共合同和三业务为2.0.0，重建组合/依赖锁。当前哈希接受、历史/未知哈希拒绝验证通过；发布冻结继续核对精确闭包。 |
| K09 | DB9/格式5当前写入；旧库/旧升级在写连接前拒绝，原不可变SQL不变。当前原回执、备份与隔离保留；新增当前DB9离线Query核对的两处真实子进程退出验证，不重复Query/Submit，不清偿缺失原始响应责任。 |
| K10 | 新Seed原子写入完整当前确认；已有缺元数据账拒绝，当前InitialRepair、Pending回退、停止/崩溃恢复保留。配置确认、AQA及恢复专项覆盖。 |
| K11 | 原请求缺冻结实现拒绝Execute/Query，不借用当前路由；全部回执文件在恢复写入前预检，旧围栏/无预约原文不导入。设备状态使用当前epoch/source事实；内置及扩展服务统一使用服务健康事实与相同守卫，去除旧设备健康字段解释。新增数据库/原文逐字节保全及内置服务真实准入回归。 |
| K12 | 历史界面仅接受可取消的当前查询会话；缺少契约时在旧调用前拒绝。固定筛选、分页、导出、授权撤销及已拥有读取排空仍保留。 |
| K13 | 安装计划和提交共用OperationId/快捷方式明确选择检查；只清理登记归属入口。安装选项、快捷方式定向验证，未执行本机系统安装。 |
| K14 | Sportmaster的classic明确拒绝；公共Shell仍服务DirectPlc/WcsToPlc，测试直接构造该当前Shell。V2左右三组信息、58字段及39操作按最新归属保留，V3替代旧参数入口。 |
| K15 | 当前管理模式命令单一路径；旧资格须显式历史审计且不产生当前PASS。脚本定向测试及新增当前合同CI检查覆盖。 |

阶段性日志保存在忽略目录 `.artifacts/current-contract-cleanup-01/`；只提交源码、合成夹具和汇总，不提交用户原稿、凭据、日志数据库或发布二进制。原生TaskHierarchyChain的“明确拒绝保留原因/另一位成功”与LifecycleNative的“停止失败保留原窗口/拒绝重开”断言继续存在。已确认的夹具问题包括未等待健康/暂停投影、普通模式脱敏快照与授权原件比较、当前组件标题/目录变化以及125%缩放下物理像素边界比较；修复相应前置与比较口径，没有取消原业务断言。

本地Windows工作树的历史SQL原始字符串受CRLF影响，历史规范SQL仍为Git中的LF字节；不得重写历史checksum消除差异。最终冻结发布和CI使用Git导出的规范源。需要受保护机器策略、提升权限或原生安装的检查按其真实环境记录，不能用跳过权限换PASS。

最终提交由PR和CI记录实际HEAD、SourceCommit及介质身份；本节阶段性结果不能替代最终HEAD门。只有所需检查通过、冻结介质启动及相关原生定向验证通过、没有未解释的相关失败后才可合并；仍遵守main保护且不自动合并。真实RCS、相机/SDK、工控机输入与DPI、系统安装/重启和现场联动继续按第7节独立验收。

实施PR：[119](https://github.com/yuan2go/importer-pc/pull/119)。首轮冻结验证发现并补充处理：参数加载依赖尚未更新的普通模式快照，可能漏掉未绑定组件，现改为应用完整读取并复核同一授权；当前2000条原记录检查改走公开打开入口，不再反射已退出的迁移方法。旧格式仍允许只读完整备份保全，但拒绝当前可写恢复，测试分别检查原件哈希和无新目标。首轮CI失败原件保留于[40422554265](https://github.com/yuan2go/importer-pc/actions/runs/40422554265)，不能将后续修复倒记为该次通过。
