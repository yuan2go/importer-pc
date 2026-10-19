# COMMERCIAL-PRODUCTIZATION-01：源码基线与文档收敛

核对日期：2026-10-19。源码基线：[`b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5`](https://github.com/yuan2go/importer-pc/commit/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5)。本文服务于[商业化工作包](../work-packages/WP-IPC-COMMERCIAL-PRODUCTIZATION-01.md)，记录实际起点、需补齐的产品闭环和文档清理依据；本次没有实施业务代码，也没有重新执行历史测试。

阅读顺序：[当前索引](../INDEX.md) → 本文 → [产品设计](../design/commercial-productization-01.md) → [实施逻辑](../implementation/commercial-productization-01.md) → [必要验收](../runtime/validation/commercial-productization-01.md)。下一次编码必须重新读取最新 main 及并行修改，本文的固定 SHA 是可追溯起点，不授权覆盖其后的实现。

## 1. 本次结论

商业化工作的重点已经从“新建安装、修复入口”转为“补齐已有入口的完整使用流程”。当前源码已提供原生 WPF 四步安装向导、启动失败修复、独立离线基本配置、完整备份恢复预览与执行；它们应继续使用同一部署登记、配置修订账和业务守卫。程序位置选择、客户可理解的组件目录、首次站点与设备配置、系统级安装维护体验、连续作业与售后交付仍需按本包逐项收口。[安装与修复源码](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Desktop/StartupWorkflows.cs)与[已有实施记录](../runtime/startup-deployment-02-results.md)分别说明源码行为和原验证边界。

相比前次基于 `524916b2088f267175bec46daf01e6df020932b7` 的分析，本基线已包含 PR #44、#45 及后续发布回执。最新共享账户政策允许指定运行账户在未提权状态下使用 UAC 过滤令牌；管理员组的 deny-only 成员身份本身不再构成拒绝。启用管理员权限、错误 SID、系统或服务账户仍不能用于正常作业。Supply 与 Inspection 也在持久初始化前复用同一身份门禁；不能为了安装便利建立第二套宽松运行入口。[合并裁决与发布事实](../runtime/publication-deployment-20261019.md)保留双方来源及最终政策。

源码合并不等于统一介质已交付。现有两组 reference 发布冻结在各自旧源码，尚不能证明本基线的完整组合可在 Windows 安装、运行和维护；详见第 3 节。当前 Windows 原生验证仍为 NOT_RUN，真实 SDK、协议、设备和现场条件仍按原记录为 BLOCKED，Production 资格没有因文档整理改变。

## 2. 已有能力与应补齐的部分

下表中的“已有”来自列明的源码或形成时记录。日常作业、历史、健康等已有长期实现的领域，以设计和相关实现为复核入口；本文不因尚未逐页复核就认定其全部缺失。

|工作单元|基线已具备的内容|本包需补齐或复核的闭环|直接依据|
|---|---|---|---|
|COMM-00 文档|框架、F 决策、历次工作包和证据已存在；新部署/UAC 说明已加入顶部|收敛多个“当前/下一阶段”入口，分清现行要求、旧任务和历史证据；先完成此步再编码|[AGENTS](../../AGENTS.md)、[框架](../design/framework.md)、[F 决策](../design/framework-decisions.md)|
|COMM-01 安装|`StartupSetupWindow` 已有检查介质、输入参数、摘要授权、安装完成四步；普通 Setup/Maintenance/运行入口无 PowerShell 前提|以用户任务呈现步骤、说明和进度；支持有保护条件的程序位置选择；给出真实安装结果及下一步，不把“安装完成”显示成“可生产”|[StartupWorkflows.cs](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Desktop/StartupWorkflows.cs)、[操作指南](../runtime/startup-deployment-02.md)|
|COMM-01 位置与账户|安装目标固定在 `%ProgramFiles%\ImporterPc\releases`；数据根可选择；策略、配置种子与部署登记受保护；账户选用已有 Windows 用户|程序位置必须贯穿请求、预览、helper、ACL、回执、快捷方式和维护入口；界面选项不能绕过受保护路径策略。保留旧部署的数据根与账户归属|[WindowsDeploymentSetup.cs](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Platform/WindowsDeploymentSetup.cs)、[ParcelStartupServices.cs](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Assembly/ParcelStartupServices.cs)|
|COMM-02 组件与前置条件|向导可选择独立信任文件和原始离线扩展包；安装按依赖完成，安装阶段不加载模块或 SDK|将包/版本/能力/依赖/许可与需独立安装的厂商运行库解释为客户可理解的目录；已安装、已配置、已激活、已连接、允许业务分别显示|[ParcelStartupServices.InstallPackages](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Assembly/ParcelStartupServices.cs)、[安装信任校验](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Platform/WindowsDeploymentSetup.cs)|
|COMM-02 首次配置|安装接收包含 `business.json` 的扁平配置来源目录，保存种子；离线维护可以在业务未启动时读取并提交 Pending|提供新站点/继承原部署/导入模板/完整恢复的明确分支；首次选择真实设备稳定身份、左右业务角色及选定 RCS 参数。只展示装配允许且实际可配置的内容|[ConfigurationFiles / ValidateSeed](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Platform/WindowsDeploymentSetup.cs)、[ParcelStartupMaintenance](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Application.ParcelFlow/ParcelStartupMaintenance.cs)|
|COMM-03 设置|启动修复页编辑现有能力参数、容量、超时及基本重量策略；保留配置 hash、revision 和状态；原站点、模式、拓扑和精确提供者在该入口固定|统一日常设置与离线维护的领域校验、草稿、差异、CAS 和生效结果；明确哪些设置立即生效、需停止或重启，避免“已保存”冒充 Effective|[StartupRepairWindow](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Desktop/StartupWorkflows.cs)、[StageAsync](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Application.ParcelFlow/ParcelStartupMaintenance.cs)|
|COMM-04 作业与角色|框架规定一个 Application 多页共享实例、输入草稿隔离、业务命令守卫；登录按产品能力选择|按实际任务复核连续扫码、焦点、辅助窗口、状态可见性、权限反馈及角色工作区；可选登录不能变成强制登录，也不能使维护变为匿名高权限|[框架 §9–10](../design/framework.md)、[现行 UI 入口](../ui/STATUS.md)|
|COMM-05 健康与恢复|启动失败可进入离线修复；业务未启动、可选扩展不可用时仍可读配置和导出摘要；原责任由正常启动守卫处理|统一故障来源、影响、证据时效与允许动作；分别解释未配置、缺依赖、未连接、过期、业务拒绝和 Unknown；复核重试启动前的停止/释放完成条件|[离线维护源码](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Application.ParcelFlow/ParcelStartupMaintenance.cs)、[已有实施结果](../runtime/startup-deployment-02-results.md)|
|COMM-06 历史指标|框架和既有作业模型区分实体准入、作业、业务请求、发送尝试和接受事实|基于原事实账补齐查询、详情、导出、时间范围和指标口径；不得把相同条码合并成一项作业，不把取得格口计算成物理完成|[框架 §6–10](../design/framework.md)、[RCS 运行说明](../runtime/sportmaster-rcs-task.md)|
|COMM-07 升级修复卸载|并排版本、原 seed 保留、配置来源 hash 固定、受保护匹配 helper、版本专属快捷方式及最后提交部署登记已实现；失败保留前次登记和原数据|补全客户可执行的预检、维护窗口、版本识别、修复/卸载入口及数据保留政策；有依据决定安装技术，不把原 ZIP/WPF 向导标为已实现 MSI 或系统级卸载|[WindowsDeploymentSetup.Install](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Platform/WindowsDeploymentSetup.cs)、[实施边界](../runtime/startup-deployment-02-results.md)|
|COMM-08 备份与迁移|已有完整备份预检/恢复 UI；恢复使用原账身份、新目标目录和隔离标记；离线保存只检查 DB 副本，不改原 DB/WAL|补齐备份列表、策略/容量和可理解的恢复选择；区分原站点灾备与新站点模板，保留未决责任和请求编号归属；避免重新实现一套恢复引擎|[PlanRestoreAsync / RestoreAsync](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Application.ParcelFlow/ParcelStartupMaintenance.cs)、[恢复维护说明](../runtime/architecture-quality-closure-01.md)|
|COMM-09 诊断帮助|启动支持摘要已包含 Application、配置 hash/revision、诊断及扩展库存，不含配置参数、报文和数据库字节|在原机制上提供分级支持包、脱敏预览、关联定位、离线帮助及版本对应 SOP；不能把当前轻量摘要夸成完整售后平台|[ExportAsync](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Application.ParcelFlow/ParcelStartupMaintenance.cs)|
|COMM-10 品牌与批量交付|产品定义、版本组合、WPF 实物设计及固定参考包已存在|统一安装/窗口/快捷方式/关于/帮助身份；模板批量配置复用同一校验链；集中管理、许可收费、多语言/OEM按已确认产品需求触发，不无条件建设云平台|[版本与组合](../runtime/version-compatibility-01.md)、[UI 实物](../ui/ss-ui-vis-001-v1.1/README.md)、[设计工作包](../work-packages/WP-IPC-COMMERCIAL-PRODUCTIZATION-01.md)|

### 2.1 首配与启动修复还需解决的静态风险

补读本基线的 [ParcelRuntime.StartAsync](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs) 和 [ParcelCapabilityFactory.ValidateDeployment](https://github.com/yuan2go/importer-pc/blob/b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5/src/Ipc.Assembly/ParcelCapabilityFactory.cs) 后，有两项不能被“已有离线配置窗口”掩盖的实施风险。以下是源码条件分析，**尚未执行场景复现，不能写成已复现 FAIL 或已修复 PASS**。

1. **未确认 Effective 可能挡住用于修复它的 Pending。** `StartAsync` 仅在 `Confirmation.Required != true` 时尝试 Pending 激活，随后校验并创建当前 Effective 资源。如果首次 Effective 持续构造/启动失败且仍要求确认，离线 Stage 新候选未必能打通正常启动。COMM-02/03 必须复核新站及未确认配置的受控修复路径，在同一 owner 下检查旧责任、保留原配置/确认记录字节，不能靠跳过确认、改站号或清空账来解锁；必要验收为 CPV-05/06/09。
2. **离线表单可填写不等于候选可以 Stage。** Production 的部署校验要求 schema3、真实 reader、维护认证能力，并读取绑定精确配置 hash/契约身份的 qualification；当前 `ParcelStartupMaintenance.ValidateAsync` 与 `StageAsync` 都直接使用 `ports.Validate`。缺少资格或配置 hash 已变化的候选可能在未连接设备之前即被拒绝。首配应区分可继续填写的非生效草稿、结构/字段校验、部署资格校验、Pending 提交、资源激活和业务准入；不得为了让首配显示成功放松原 Production 校验或复用旧配置资格。业务登录仍可选，维护认证及部署资格是另一个边界；必要验收为 CPV-04/05/06/07。

这两项需要明确的软件行为与持久状态收口，不能全部归因于“缺少真实设备”。实际 SDK/资格输入不足只阻塞依赖该输入的部署或生产路径，草稿采集、诊断、受控修复机制和合成场景仍可完成。

## 3. 源码、历史验证和介质身份

|对象|固定来源|实际含义与不可替代的部分|
|---|---|---|
|本包静态起点|main `b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5`|包含 PR44 向导/UAC 与 PR45 Supply/Inspection 入口守卫的合并源码；本文不为该组合添加测试 PASS|
|[原生部署 reference](https://github.com/yuan2go/importer-pc/releases/tag/startup-deployment-reference-e79a8bf)|`e79a8bfdc65328619c77013fb314eaaef4a35a2f`|Sportmaster portable 及 Sportmaster/DirectPlc/WcsToPlc 三个 win-x64 ZIP；含 PR44，不含 PR45 新增入口守卫|
|[全部入口 reference](https://github.com/yuan2go/importer-pc/releases/tag/runtime-all-entrypoints-reference-16ecb57)|`16ecb571cba32188f62a6e6e677030cd589dbf48`|Supply、Inspection 两个 portable ZIP；沿用形成时旧共享账户政策，不含 PR44 向导和 UAC 政策更新|
|PR44 历史验证|[startup-deployment-02-results.md](../runtime/startup-deployment-02-results.md) 及其固定证据|37 项定向检查、72 条既有 Application 检查、22 项身份决策、三个 WPF 交叉构建和冻结媒介检查等 PASS 仅属于原记录的源码/环境/范围；不是本包重跑结果|
|原发布“不测试”记录|[publication-deployment-20261019.md](../runtime/publication-deployment-20261019.md)|记录原任务上传、合并与资产回读；NOT_RUN_BY_USER_REQUEST 只描述原发布流程，不构成本包永久禁止必要验证的要求|

两组 reference 共 6 个 ZIP，附 4 个清单附件，共 10 项资产。它们保持原 SHA 和证据；不能拆装混成一个“最新已验证版本”，也不能只修改 Release 文案把旧 ZIP 标为本基线。下一次发布须从新的固定组合源码生成介质，并针对同一介质完成适用的必要验收。Windows UAC/ACL、真实令牌、交互、触控/DPI、安装/恢复故障路径尚未验收；真实接口/设备和现场门槛单独保留。

## 4. 编码前必须完成的文档收敛

本次整理按下表处理当前入口；具体提交文件与哈希以 Git diff、归档索引和 manifest 为准。之后实施者还须按实际变更范围检查各领域的当前操作指南，发现新增漂移时先修正文档再编码。

|位置|本基线的漂移|处理方式|
|---|---|---|
|根 README|仍用 PR41/42、`ff31b0e`/`f354d3f` 两个较早候选描述当前状态|重写为当前架构、最新源码事实、本工作包、当前运行维护入口及独立介质身份；旧候选只保留为历史入口|
|`docs/INDEX.md`|多套“本轮有效/当前执行/当前阅读”并列，包含旧 main 版本及先前不测试任务语境|建立唯一当前阅读顺序；把先前工作包、原审查、原发布回执分到历史与证据区，保留可检索性|
|`framework.md` / `framework-decisions.md` 顶部|历次增量以多个“当前”段落堆叠，容易让旧阶段指令压过新包|收敛顶部路由，链接本包及仍有效的核心设计；不重写历史目标、既有 F 决策形成依据和原实现映射|
|`getting-started.md` / `windows-application-delivery.md` / `architecture-quality-closure-01.md`|现行运行指南顶部需要与最新部署/UAC、参考介质和本包实施路由一致|收敛顶部现行指引并保留未过期操作正文；固定旧候选的原事实继续由原结果与发布回执说明|
|旧账户/首装操作说明|早期政策可能把 deny-only 管理员成员身份一并拒绝，早期脚本说明可能被误读为当前正常入口前提|现行用户操作说明采用最终政策与原生入口；原 SID 报告、旧测试和旧介质说明保持形成时含义|
|旧结果中的“产物仅本地”|原部署结果形成时尚未上传，后来已获授权上传旧字节|不改写原结果，沿发布回执补充后续状态，避免制造原结果形成时已经公开的假象|
|manifest 的当前元信息|工作包/源码/版本摘要可能沿用早期快照|准确区分本包源码基线与历史快照；保留未变文件/旧证据条目，仅更新实际变更与新文件的身份|

原 README、INDEX、两份框架文件及上述三份现行运行指南，共七份文件以原字节 `.txt` 归档到 [pre-commercial-productization-01](../archive/pre-commercial-productization-01/README.md)。采用文本快照可避免历史相对链接作为当前导航执行，也避免归档指令成为现行工作约束。归档文件名、来源提交及 SHA256 由归档索引记录。

“清理过期文档”在本包中包括撤下失效当前入口、改正文义、消除重复权威和保留来源。它不包括删除原问题、原失败日志、D/RV 状态、旧候选哈希、原测试断言或历史授权记录。不得以更新本包状态为理由，把 Windows NOT_RUN、真实能力 BLOCKED、PUBLIC_SUBSET_ONLY 改为 PASS。

## 5. 实施过程中不能漂移的边界

1. 保持基座、独立 Application、驱动、外部组件四类责任；打包时选择独立主流程，同一 Application 的多个页面共享运行实例。安装/设置向导不拥有第二套业务状态机。
2. Sportmaster 左右 reader/投递位独立，只执行当前已支持的格口请求流程；无 PLC、图片展示或长期图库。DirectPlc 完整 UI 继续延期，WcsToPlc 保持独立主流程。
3. 使用唯一数据根下的配置修订账；种子、导入模板、界面草稿均不能另成 Effective 权威。首次完成状态必须能从真实配置、身份与能力验证事实重建，不能靠孤立布尔标记绕过旧账。
4. 对外请求冻结原身份、报文、目标、合同及版本。连接恢复、安装成功、设置改址、重启和恢复备份均不授权重放 Unknown；没有原权威结果只能进入相应核对流程。
5. 驱动和外部组件按产品装配与资格选择；正常安装不擅自启动设备或执行业务验证动作。缺少真实合同/SDK/权限时完成不受影响的软件路径，受影响路径明确保留阻塞。
6. 工业质量以持久责任、恢复、可维护性和可验证交付闭环判断，不以新增步骤/页面/测试数量判断。后续仅补缺口；已有机制有证据可复用时不另建配置、更新、恢复或扩展平台。

## 6. 本包证据记账方式

本次可记录的成果是文档收敛、静态基线核对与实际提交回读。实现后的结果另写运行结果/证据文件，记录执行命令、退出状态、固定源码、介质 SHA256、环境、范围及剩余，链接本包验收编号；不得把本文的设计要求直接改称测试已通过。

若开工时 main 已前进，先列出新增源码/文档与本包假设的差额，继承其正确实现，再更新工作包的待办范围。已完成的能力应改为继承与验证，不能为了满足计划重新实现一次。当前代码若与历史报告冲突，以可核验最新行为制定补差任务，同时保留历史报告形成时事实。
