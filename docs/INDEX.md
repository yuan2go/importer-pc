> PR81合并前补修：[角色重绑后的目录/页头/搜索即时更新与原生回归](runtime/validation/pr81-role-label-review-01.md)。PR80已合并，原介质与各版本证据保留身份。

> 参数 V3 独立商业体验复核与定向完善：[原生前后证据、问题及验收边界](runtime/validation/parameter-settings-v3-experience-01.md)。本轮开发分支继承 PR80，不更改其冻结介质或历史结果。

> 2026-10-21 界面文案增量：按用户本轮要求，固定提示、任务标签及选项采用中文，替代历史设计中的双语提示；实际编码和原始技术证据保留原值。[实现、运行和验证边界](runtime/chinese-interface-copy-01.md)。既有 V3 布局、业务合同和已发布介质身份不变。

> 参数 V3 原生交付收口：固定 main `767075abe62a77c30e70766bb5d3f1feca02686b`，开工无开放 PR。当前操作以 [1.7.0 原生说明](runtime/parameter-settings-v3-native-01.md) 为准；[原生截图、交付身份和验证边界](runtime/validation/parameter-settings-v3-native-01.md)沿 [PS-05](work-packages/WP-IPC-PARAMETER-SETTINGS-02.md)记录。V3 设计原件及旧发行证据保持身份，旧 1.6.1 不包含 V3。

> PR80 工程交付：同源 `bfec0a5672d846a865db62af9ef6a573a953f914` 的 Windows CI、V3 原生截图与 1.7.0 安装器已通过并下载核验。磁盘空间恢复后，最终同源介质的本机125%原生/键鼠检查、安装器选项/取消及隔离离线启动已通过；完整安装生命周期、SDK/RCS及现场另行验收。ce9dcd3 是保留的中间证据，不作为最终身份。

> 2026-10-21 PR78/79不测试合并、参数V3/启动回退来源与原CI产物下载见[本轮发布记录](runtime/publication-pr78-pr79-20261021.md)。原1.6.1安装包未重建，不能标为包含这两项增量。

> PR73审查与CI后续：[受控启动回退修复](runtime/pr73-review-followup-01.md)，基于main dfec787。原PR已合并，本次通过独立后续PR完成原未解决P1及当前CI验证，不改写历史发布结果。

> 历史公开下载（不含V3）：[SM v1.6.1 Windows 单 EXE](https://github.com/yuan2go/importer-pc/releases/download/installer-sm-v1.6.1-92586bf44cde/CainiaoSupplyStation_SM_Setup_v1.6.1_win-x64.exe)。PR76已合并main，固定源`92586bf44cde`；[发布记录、SHA256与待验边界](runtime/publication-login-session-20261020.md)。已修复日常登录会话时长字段隐藏；未签名预发布。
>
> 当前局部修复：[已绑定登录的会话时长字段](runtime/login-session-lifetime-fix-01.md)，基于main `837370e`，原1.6.0发布及其未修复记录保留原身份。V2原实施、设计、PR73及其他介质验证各守原版本。

> PR73历史不测试发布、源码及产物下载边界见[发布报告](runtime/publication-pr73-20261020.md)。

> PR73 参数实施补充：原 PARAM-01 参数及必要联动已完成，原提交验证见[实施记录](runtime/parameter-refactor-01.md)；PR73 发布阶段只做不测试的合并/上传（历史范围）。原 PARAM-01 不自动代表 V2；本版完成范围与新证据见顶部实施记录。

> 历史不测试发布与PR69～71合并记录、原执行产物下载及边界见[发布报告](runtime/publication-pr69-pr71-20261020.md)。PR71形成时参数为实施中快照，PR73已完成原PARAM-01范围；原介质保留各自SourceCommit。

此前已授权完成 PARAM-01 参数正式重构，四分类、业务条码筛选及配置闭环以 [实施裁决](design/parameter-refactor-01.md) 为准；原“本次只做设计”是设计入库阶段记录。完整 UI 工作包其余范围不因本包实施自动完成。

> 桌面名称增量：[固定显示“菜鸟供包台”与旧测试资源清理](runtime/desktop-shortcut-name-01.md)，新构建与下面原生结果分开记录。

> 历史完整安装体验：SM业务模块v1.4.1单EXE，冻结源码`27b0d8f6cc0281fd82ee68fcda350233cb0a45ff`。[单窗口/一次授权及58项实际安装启动记录](runtime/installer-single-window-01.md)；本机Simulation通过，Win10 22H2/其他CPU/海康现场待验。该旧包不包含UIR-01～06移植，不能把其58项结果记为当前1.6.0通过，原7c发行身份保留。
> 2026-10-20 RCS心跳实施增量见[配置、实现与验证](runtime/rcs-heartbeat-01.md)。本增量由用户另行授权；下文“本次只做设计”专指UIR设计入库任务，不限制本次RCS开发。

# 文档索引：参数设置 V3 与 Sportmaster 作业

**参数体验当前修订：** 用户针对实际参数页提出新的美学与操作纠偏，现采用 [PARAM-UX-02](design/parameter-settings-experience-02.md) 和 [参数设置V3](ui/parameter-settings-v3-20261020/README.md)。此修订替代V2参数版式、底部步骤按钮和“维护与迁移”总入口。PR74实现与发行记录继续保留真实来源，不因新设计而倒写。

当前参数工作包继续使用 [PS-00～05及最新Codex执行提示](work-packages/WP-IPC-PARAMETER-SETTINGS-02.md)。2026-10-21核对main `42a047c3`：PR78/79已合并，54个设计文件完整、53项登记文件和25项源码指纹全部一致；下一步是原生交付收口及实际缺口修复。原`bf61337`是V3形成时整合基线，原验证身份不改变。源码与Windows待验边界见[参数V3实施记录](runtime/parameter-settings-experience-02.md)，PR74/76及PR79已完成内容复用。

## 1. 当前阅读顺序

| 顺序 | 入口与用途 |
| --- | --- |
| 1 | [AGENTS](../AGENTS.md)：工作范围、分支、现有机制与证据规则。 |
| 2 | [V3设计入口](ui/parameter-settings-v3-20261020/README.md)、[参数决定](design/parameter-settings-experience-02.md)、[交接](ui/parameter-settings-v3-20261020/HANDOFF.md)：当前参数布局、字段去向和保存流程。 |
| 3 | [参数工作包、最新源码核查与Codex执行提示](work-packages/WP-IPC-PARAMETER-SETTINGS-02.md)、[V3原验证](ui/parameter-settings-v3-20261020/QA.md)：当前收口；设计包内CODEX-NEXT保留形成时字节，执行以更新后的工作包为准。 |
| 4 | [V2设计](ui/sportmaster-task-redesign-v2-20261020/README.md)、[原V2裁决](design/sportmaster-task-redesign-01.md)、[UIR-00～06](work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)：三组作业、首配、原合同和历史任务，参数入口以V3为准。 |
| 5 | [V2实施](runtime/ui-task-redesign-01.md)、[原验收](runtime/evidence/ui-task-redesign-v2/ACCEPTANCE.md)、[同源WPF图](ui/sportmaster-v2-wpf-20261020/README.md)、[PR76会话修复](runtime/login-session-lifetime-fix-01.md)：保持各自源码及环境身份。 |

V2 以命名界面状态和导出图片分别计数，实际数量见本版索引；源码、交接格式及归档文件不重复计作页面。原 58 项字段/状态、39 项操作的可辨认事实保留，当前迁移表明确各项在 V2 的去向。

日常导航为作业、作业记录、参数配置、诊断与恢复。参数配置顶层为系统、业务模块、驱动、组件：系统保留区域/项目/供包台编码及本机设置，业务模块维护当前Sportmaster已有条码筛选、请求规则与应用预算，驱动按安装类型及业务要求显示左右实例，组件只有一个 RCS 连接。首次配置仅在首次启动出现，五步为业务模块、系统、驱动、组件、检查与完成。每个相机框固定相机信息、扫描信息、任务信息三组；相机候选经Application筛选形成选中码，匹配RCS回应后更新任务，旧任务整组退出当前显示。

## 2. 当前代码已具备什么

最新主线还已包含PR74的V2实现、PR76会话时长修复与对应发布；下列机制继续保留。

基准已包含PR62/63/64/66/67/68的工作台、独立IP Reader、品牌/安装、布局与发布；另已并入PR69单窗口安装/固定桌面名称、PR70 RCS心跳、PR71参数实施快照和PR72发布回执。已完成机制不重新列为缺失，未完成快照不标为验收通过。[组合报告](runtime/parallel-task-reconciliation-20261020-02.md)、[安装发行说明](runtime/installer-release-info-01.md)、[最新发布范围](runtime/publication-pr69-pr71-20261020.md)保留精确来源。

- Sportmaster左右独立Reader/投递位，取得RCS/WCS格口及任务回执，不下PLC、不新增图片预览。DirectPlc、WcsToPlc仍为独立Application；DirectPlc完整UI延期，Supply/Inspection保留各自契约。
- Reader是读码观察来源，Camera是可选采图能力。海康当前1.0.3描述已有六项（含心跳bcr-code），没有演示9000端口；取格口旧左右编码与心跳编码目前独立，不能静默替换。
- 工作台、记录、维护和辅助窗口共享运行实例；长内容、焦点/选择/草稿、原生日历及旧格口失效保护保持。诊断OneWay已修，默认及classic最小区修复已完成。
- 完整候选校验、唯一配置账、草稿CAS、加密未完成输入、审计与受控启动已存在；PARAM快照新增四分类共模、字段定位与保存结果Unknown/PendingRestart/NotSaved/Conflict核对。对象页不形成第二账，保存不等于生效，重连不应用Pending。
- Sportmaster已有有界条码筛选、完整候选与选中码证据、本地多行样例及旧null严格单码兼容。V2保留这些能力并完善显示，不默认首码、去重伪造唯一或生成生产正则。
- taskId/skuId 沿原解析与应用传递；本版已补结构化展示及 waveNo/响应字符串 quantity 的兼容传递、历史读取和当前投影。新观察拒绝、Unknown 或失效时当前任务整组退出，原请求责任单独保留。
- 原件、候选/筛选、准入、请求和发送尝试分别保留。RCS已有周期心跳及HTTP交付/失败证据，尚无应用确认合同且Query=false；不复制心跳、不发假查询，不把HTTP或连接恢复当原请求成功。
- 菜鸟品牌、原生首配/安装、英文SM单EXE、单窗口准备/选项/摘要/完成、一次安装UAC、固定桌面入口、两个快捷方式及BUILD-INFO均继承。缺配可编辑与业务就绪分开；普通人工安装不要求无人值守或批量平台。

## 3. 架构、运行与维护入口

| 任务 | 有效资料 |
| --- | --- |
| 分层及业务所有权 | [框架](design/framework.md)、[F决策](design/framework-decisions.md)、[独立Application](design/application-architecture-22.md)、[能力职责](design/application-capability-19.md) |
| 构建、装配与版本 | [Application运行](runtime/application-architecture-22.md)、[扩展开发](extensions/developer-guide.md)、[受控扩展](design/controlled-extensions.md)、[版本与组合](runtime/version-compatibility-01.md) |
| 安装、依赖、首次运行 | [单窗口/一次授权](runtime/installer-single-window-01.md)、[固定桌面名称](runtime/desktop-shortcut-name-01.md)、[英文安装包与BUILD-INFO](runtime/installer-release-info-01.md)、[首次运行](runtime/getting-started.md)、[安装范围](runtime/installation-delivery-scope-01.md) |
| 配置、恢复及交付维护 | [商业操作手册](runtime/sportmaster-commercial-delivery-01-operations.md)、[配置/容量/恢复](runtime/architecture-quality-closure-01.md)、[Windows交付](runtime/windows-application-delivery.md)、[原请求恢复](runtime/parcel-recovery-p2.md) |
| 海康和RCS真实边界 | [Reader运行与资格](runtime/hikrobot-reader-01.md)、[RCS协议与恢复](runtime/sportmaster-rcs-task.md)、[心跳及原验证](runtime/rcs-heartbeat-01.md) |
| Supply/Inspection兼容使用 | [Supply首次操作](runtime/supply-getting-started.md)、[维护](runtime/operations.md)、[共同账户前置门禁](runtime/runtime-identity-preflight-02.md) |

实现继续使用现有C#/WPF、锁定SDK/依赖和应用命令守卫。UI不直接读写配置账或SQL，不因换页重建设备会话；软件暂停不是安全急停。当前任务的文件入口与必要契约补足集中在实施裁决中，不另建通用配置或部署权威。

## 4. 设计、源码、介质与验收分别记录

| 对象 | 证据与适用范围 |
| --- | --- |
| 当前 SM v1.6.1 安装包 | [登录会话修复与发布](runtime/publication-login-session-20261020.md)。固定main源92586bf，734文件介质、最终DLL聚焦24项和实际键盘输入、安装准备/取消通过；完整安装/设备/现场待验。 |
| 历史 SM v1.6.0 安装包 | [PR74合并与发布回执](runtime/publication-pr74-20261020.md)。固定源1878be50d5be，原733文件及会话字段未修复记录保持原身份。 |
| 当前参数设计与模拟QA | [V3设计入口](ui/parameter-settings-v3-20261020/README.md)、[UI状态](ui/STATUS.md)。27张PNG为24个命名状态加3个尺寸，原型与Windows/生产链路分开。 |
| 现有WPF及组合 | [工作台原证据与补修](runtime/sportmaster-clear-workstation-01.md)、[组合报告](runtime/parallel-task-reconciliation-20261020-02.md)。原233项组合/9项完成页回归各守原源码、方法和环境。 |
| V2 当前 WPF 实现 | [UIR-00～06 实施](runtime/ui-task-redesign-01.md)、[本版验收](runtime/evidence/ui-task-redesign-v2/ACCEPTANCE.md)。固定源 `26bb7b9` 完成参数、首配、三组作业和相关维护闭环；32 张 WPF PNG、本机输入和发布启动各记边界。 |
| PARAM历史实施及快照 | [PR73实施记录](runtime/parameter-refactor-01.md)、[PR71历史范围](runtime/publication-parameter-snapshot-20261020.md)。保留原参数实施和不测试发布身份，不扩张为本轮验证。 |
| 已发布单窗口SM v1.4.1 | [27b0d8f及58项原生记录](runtime/installer-single-window-01.md)、[下载/上传回执](runtime/publication-pr69-pr71-20261020.md)。原SourceCommit保留，不含后续固定名称、心跳或参数快照。 |
| 已发布心跳Simulation | [3f8a58ba原验证](runtime/rcs-heartbeat-01.md)及[发布范围](runtime/publication-pr69-pr71-20261020.md)。portable CLI/签名夹具不是WPF安装包，不改标为最新main。 |
| 历史7c安装发行 | [原41项证据](runtime/installer-release-info-01.md)、[原字节发布回执](runtime/publication-installer-release-info-20261020.md)。原SourceCommit和验收边界保留。 |
| 本轮验证与现场待验 | [本版 UIR-06](runtime/evidence/ui-task-redesign-v2/ACCEPTANCE.md)、[既有Windows后验](runtime/validation/sportmaster-alternate-ui-windows.md)、[商业必要验收](runtime/validation/sportmaster-commercial-delivery-01.md)。本机 Windows 11 125% 与原生合成结果已记录；目标工控机、真实 SDK/RCS 和现场仍待验。 |

正常作业最低有效客户区仍是800×420 DIP；不足空间保护、恢复后人工确认和软件暂停继续保留。几何夹具的原生HWND backing surface保留严格断言，只证明原生布局，不等于物理屏幕、多DPI或触控通过。原7c的41项升级/启动属于原Windows主机Simulation；受支持系统、精确SDK、trigger实体关联、客户RCS和现场资格按真实证据另验。

V2 原型入库时的 QA 和不测试合并记录属于设计阶段；本次代码的必要构建、风险验证及发布启动另见本版验收。此前 PR69/70 原证据与 PR71/72 不测试发布各守原范围。缺外部协议时明确不可用并保留维护路径，不伪造成功。

## 5. 历史来源与公开范围

| 历史资料 | 保留用途 |
| --- | --- |
| [V1 任务重设计](ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md) | 原45状态/48张PNG/95个物理文件及原哈希保留；ZIP的11分片见[恢复说明](ui/sportmaster-task-redesign-20261020/bundle/README.md)。95不是95个页面，也不与V2叠加计算。 |
| [v1.1原稿与34张PNG](ui/ss-ui-vis-001-v1.1/README.md) | 历史模拟设计与原交接，不是本次强制布局；共享箱/数量/拆包演示不能套入Sportmaster。 |
| [0e79e73原生36图集](archive/sportmaster-native-gallery-20261020-0e79e73/README.md) | 本次理解功能的原截图与导出来源，不代表最新main。 |
| [PR58视觉证据](runtime/sportmaster-visual-productization-01.md)、[日期续修](runtime/sportmaster-visual-main-closure-01.md) | 原冻结源码和验证方法，继承有效修复，保留原失败。 |
| [商业实施](runtime/commercial-productization-01-results.md)、[PR47复核](reviews/pr47-commercial-productization-review.md)、[早期部署发布](runtime/publication-deployment-20261019.md) | 已有能力与精确历史介质的追溯入口，不是多套当前任务。 |
| [本次清理前入口](archive/pre-ui-task-redesign-20261020/README.md) | README/INDEX/STATUS等e2e原字节及完整旧索引；旧文档继续可查。 |

[公开范围](baseline/PUBLICATION.md)、[D决策](readiness/decision-pack.md)、[RV状态](reviews/STATUS.md)和原REQ/BR/CAPI编号持续有效。只提交获授权材料；凭据、生产数据、未许可SDK/客户资料不纳入。资料清单见 [manifest](manifest.json)，校验方法见 [工具说明](../tools/README.md)；历史资产问题不通过新增豁免或局部PASS消除。
