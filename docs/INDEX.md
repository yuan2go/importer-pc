> 2026-11-02 用户追加以 [SM-OPERATOR-CORRECTIONS-29](work-packages/WP-IPC-SM-OPERATOR-CORRECTIONS-29.md) 为准：操作中文、按位等待RCS后再读码、明确错误字段动作、恢复视频优先调试及真实采集诊断；继承保留数据重装复验28，旧连续解码/超时可继续扫码的冲突指引由本轮替代，原证据保持SourceCommit。

> 2026-11-02 当前斜放、模糊和完整多码搜索，以及快照先到的观察身份守卫，以 [SM-READER-ROBUSTNESS-28](runtime/sm-reader-robustness-28.md) 为准；替代27包的“首搜索零结果才补搜索”及两线策略。27原实现/验证保留历史身份，旧文档完整字节见归档。

> 2026-11-02 现场弱标签补充识别、无码/坏帧及重连清退旧格口见 [SM-UNREAD-PACKAGE-27](work-packages/WP-IPC-SM-UNREAD-PACKAGE-27.md) 与[运行和验证](runtime/sm-unread-package-27.md)。源码保持内置 MVS、逐次识别交 RCS 去重；无码是图像事实，不推定实体。私有照片重放与真实过机验收分开，历史证据保持原 SourceCommit。
> 2026-11-02 七项审查修复以 [SM-AUDIT-FIXES-26](work-packages/WP-IPC-SM-AUDIT-FIXES-26.md)、[实现与验证](runtime/sm-audit-fixes-26.md)为准。当前交付操作入口为 [Parcel12](runtime/parcel12-operations-26.md)，旧格式11指南保留历史身份，原始索引见 [归档](archive/pre-sm-audit-fixes-26/manifest.json)。

> 2026-11-02 用户追加以 [SM-SESSION-SHIFT-EXCEPTIONS-25](work-packages/WP-IPC-SM-SESSION-SHIFT-EXCEPTIONS-25.md) 为准：P07/P08现在实施；SM每次成功打开至完成关闭为一个持久班次，不按固定时刻切班，异常退出与原请求跨班责任分开。替代24包延期及旧排班对本班的定义，其他守卫/历史证据不变。

> 2026-11-02 24包交付记录以 [SM-UI-SPEC-CLOSURE-24](work-packages/WP-IPC-SM-UI-SPEC-CLOSURE-24.md)、[运行与验证](runtime/sm-ui-spec-closure-24.md)为准：先修订全局隔离、完整候选生效、软件次数及原请求查询语义；P07/P08已由后续25包接续实施；原24包交付当时保留既有功能。任务导航替代旧四类导航的摆放，字段所有权和原请求不变。混合修改全部待重启；确认项缺少外部合同不新增实际动作。历史证据保持原 SourceCommit。

> 2026-11-02 读码反馈与操作可靠性见 [READER-FEEDBACK-22](work-packages/WP-IPC-SM-READER-FEEDBACK-22.md)、[运行说明](runtime/sm-reader-feedback-22.md)：补齐信任拒收终态、反馈周期/时效、全码制映射、输入焦点和独立音频/音量；替代旧超时处置文案，保留逐次上报、原请求责任及现场待验边界。
> 2026-11-02 DB22本机实际更新发现的私有WAL句柄及原件身份补修见 [DB11-WAL-HANDLE-23](work-packages/WP-IPC-DB11-WAL-HANDLE-23.md)。原数据首次写入前失败，保全原件；本轮补齐WAL模式关闭和升级回归后重新出包。既有验证保持原来源。

> 2026-11-02 DB22用户最新授权：本机保留配置/历史更新，已知Parcel10→11允许在独占离线、严格结构校验、完整备份及原责任守卫下受控升级；其他旧格式仍拒绝，不自动降级。旧从零安装限制在本次范围内由[DB22工作包](work-packages/WP-IPC-DB11-COMPATIBLE-UPDATE-22.md)替代，历史证据不改写。

> 2026-11-02 全软件文案工业化第二轮见 [OPERATOR-COPY-02](work-packages/WP-IPC-OPERATOR-COPY-02.md)：参数、保存、日志、记录、诊断、异常、统计、维护中心、调试、首配/启动修复、安装/卸载及历史清理统一用现场用语；只改展示文案，业务守卫不变。
> 2026-11-02 当前产品闭环见 [SM-PRODUCT-CLOSURE-21](work-packages/WP-IPC-SM-PRODUCT-CLOSURE-21.md)、[实现与运行](runtime/sm-product-closure-21.md)。用户现已授权本机统计/班次，替代18的暂不增加统计；集中看板明确延期。Parcel当前数据库格式11；非当前输入保留原件并拒绝。条码规则可在Application屏障后热生效，设备/目标/班次/维护策略仍需重启。原指南与证据各守原SourceCommit，完整原字节见[归档](archive/pre-sm-product-closure-21/manifest.json)。
> 2026-11-02 合并前全量闭环与工业工程补强见 [SM-CAMERA-CHAIN-CLOSURE-21](work-packages/WP-IPC-SM-CAMERA-CHAIN-CLOSURE-21.md)。覆盖原审查逐项结论、跨位故障/取消唤醒、稳定任务区及编码/反色验证；用户授权完成验证后合并PR171到main，历史证据保持原SourceCommit。

> 2026-11-02 相机→展示→RCS全链路审查续修见 [SM-CAMERA-CHAIN-RELIABILITY-20](work-packages/WP-IPC-SM-CAMERA-CHAIN-RELIABILITY-20.md)。用户再次确认逐次识别仍交RCS去重，并授权现场配置码制；单帧拒收、资源背压、编辑终态、分位声音、旋转框及未送达证据按本包核验，19原验证保持原源。

> 2026-11-02 主条码随最新相机观察立即替换、多码候选不堆入主框、图片/格口身份对应见 [SM-CAMERA-BARCODE-CURRENT-19](work-packages/WP-IPC-SM-CAMERA-BARCODE-CURRENT-19.md)及[实施验证](runtime/sm-camera-barcode-current-19.md)。本轮替代16中仅码尾先行而主条码仍等业务处理的呈现；原请求及人工草稿守卫不变。
> 2026-11-02 当前作业卡字段、恢复字号与统一条码输入见 [SM-WORK-FIELDS-20](work-packages/WP-IPC-SM-WORK-FIELDS-20.md)：覆盖18的112/72字号与来源选择，沿用170的字段及失效守卫。[Windows补证](runtime/sm-work-fields-20.md)：Source0be38fbe、13套件388PASS，旧验收不转记。

> 2026-11-01 作业卡恢复波次、任务号、商品编码、返回数量常驻见 [SM-TASK-FIELDS-01](work-packages/WP-IPC-SM-TASK-FIELDS-01.md)，替代 UX-18 中“任务字段进详情”的条款；显示条件与确认格口一致。旧索引[原字节归档](archive/pre-sm-task-fields-01/manifest.json)。Windows 验证 NOT_RUN。

> 2026-11-01 当前 SM 操作体验增量见 [SM-OPERATOR-UX-18](work-packages/WP-IPC-SM-OPERATOR-UX-18.md)：格口优先、实际输入目标、暂停语义与类型化呈现。用户暂不增加件数统计。最终源44b41c93的Windows三构建与21套件574PASS见[验证记录](runtime/sm-operator-ux-18.md)，已整合PR168；历史16/17证据保持原SourceCommit。
> 2026-11-01 供包员界面文案工业化见 [OPERATOR-COPY-01](work-packages/WP-IPC-OPERATOR-COPY-01.md)：状态/下一步/原因分层，合同术语移出作业页，异常提示写明包裹处置；业务守卫不变。旧索引[原字节归档](archive/pre-operator-copy-01/manifest.json)。该包当时的 Windows NOT_RUN 记录保留；本轮18整合后的本机补证单独记录。

> 2026-11-01 整个会话合并前复核以 [SM-SESSION-REVIEW-17](runtime/sm-session-review-17.md) 为准：涵盖最初实时视频、采集/解码/健康、全部反馈与推送要求，补齐迟到快照不能复活较新故障及真实呈现身份核对。历史14/15/16证据保持原SourceCommit。

> 2026-11-01 当前感知呈现规则见 [SM-RESPONSIVE-PRESENTATION-16](runtime/sm-responsive-presentation-16.md)：读到反馈先于持久交付，新观察清退旧格口，主动推送只合并显示，原业务持久责任不变。它替代15的短暂保留旧格口策略；15验证保持原源。

# 文档索引

2026-11-01 同码格口确认态、最新帧采集/解码解耦、按有效帧健康检查及读码到屏幕合成指标以 [SM-PERCEPTION-LATENCY-15](work-packages/WP-IPC-SM-PERCEPTION-LATENCY-15.md) 为准；[实施与验证](runtime/sm-perception-latency-15.md)区分合成端口、原生WPF、历史30张私有图重放及未取得的真实设备/客户RCS数据。

2026-11-01 在线相机实时视频调度修复见 [SM-SERVER-PREVIEW-14](work-packages/WP-IPC-SM-SERVER-PREVIEW-14.md)及[复现/验证](runtime/sm-server-preview-14.md)：持续 MVS 循环不再占用 Application 预览共用的线程池；受限线程池负向复现和本机原生视频回归单独记证，目标服务器实际验收仍待运行。

2026-11-01 PDA Backspace首轮失败已定位为Windows输入未处理前的测试断言；见[工作包](work-packages/WP-IPC-SM-PDA-INPUT-ACK-01.md)、[实测顺序与修复](runtime/sm-pda-input-ack-01.md)。最终重复/回归结果按新证据单独记录，不改写旧发布失败。

2026-11-01 SM 新站默认程序 `D:\CainiaoSupplyStation`、数据 `D:\ProgramData\ImporterPc\data`，既有部署保留原路径；[D-DRIVE-DEFAULTS-02](runtime/d-drive-defaults-02.md)、[工作包](work-packages/WP-IPC-D-DRIVE-DEFAULTS-02.md)及[本轮补证](runtime/pr161-windows-verification-20261101.md)。原生安装流程及真实D/D隔离新装通过，完整C/D/重装矩阵、最终EXE全新机和现场仍待验。
2026-11-01 PR165 人工调试轮次修复已通过 Windows 全量构建、原生离页/重进/其他窗口新轮次及容量/原请求责任回归；见[补证](runtime/pr165-windows-verification-20261101.md)和[工作包](work-packages/WP-IPC-MANUAL-DEBUG-ROUND-LIFECYCLE-01.md)。真实 SDK/客户 RCS/现场仍待验。

2026-10-31 人工调试轮次清理修复：

[工作包](work-packages/WP-IPC-MANUAL-DEBUG-ROUND-LIFECYCLE-01.md)与[实施及验证边界](runtime/manual-debug-round-lifecycle-01.md)：未发送意图清理、离页迟到结果和已保留操作发送资格。旧索引[原字节归档](archive/pre-manual-debug-round-lifecycle-01/README.md)。


2026-11-01 连续 PDA 键盘扫码拼接修复已完成 Windows 原生负向复现、构建及19项逐键回归，既有条码就绪回归通过；见[工作包](work-packages/WP-IPC-SM-CONTINUOUS-PDA-INPUT-01.md)、[原实现](runtime/sm-continuous-pda-input-01.md)和[本轮补证](runtime/pr164-windows-verification-20261101.md)。真实 PDA/SDK/客户 RCS/现场仍待验。
2026-10-30 两项关闭阻断修复见 [SM-SHUTDOWN-RESOURCES-13](work-packages/WP-IPC-SM-SHUTDOWN-RESOURCES-13.md)及[实施与验证](runtime/sm-shutdown-resources-13.md)：已终结后台任务的错误保留上报并继续真实清理；调试缓存切换观察采集任务退出，失败保留隔离并解除无执行者的等待。真实SDK/现场退出仍单独验收。

2026-10-30 人工取格口与相机配置解耦、调试说明删除及本机首帧故障排查见 [SM-MANUAL-CAMERA-INDEPENDENT-12](work-packages/WP-IPC-SM-MANUAL-CAMERA-INDEPENDENT-12.md)。它替代10中人工输入仍需相机来源编码的要求；实际修复与验证边界单独记录。

2026-10-30 调试实时视频及逐相机取帧/解码诊断见 [SM-CAMERA-STREAM-11](work-packages/WP-IPC-SM-CAMERA-STREAM-11.md)及[验证记录](runtime/sm-camera-stream-11.md)。实时预览独立于单轮解码暂停和锁定照片；连接在线不代替成功取帧，ReadOperations不是识码计数。

2026-10-30 当前人工供包与调试流程以 [SM-MANUAL-DEBUG-FLOW-10](work-packages/WP-IPC-SM-MANUAL-DEBUG-FLOW-10.md) 为准：人工输入独立于相机在线/配置，保留RCS及原责任守卫；调试沿五步按轮锁图和手动发送。

2026-10-30 当前作业导航及条码输入以 [SM-NAVIGATION-INPUT-09](work-packages/WP-IPC-SM-NAVIGATION-INPUT-09.md) 为准：普通模式只显示作业/记录，管理模式增加设置/日志/调试；单一条码输入和任务区在离线/待配置时保留，底部提交。替代03四项入口常显、06置顶独立输入及全屏配置提示，历史证据保留。

2026-10-30 PR156、PR158 顺序整合进 PR155 的来源、审查、定向验证及待验边界见 [08 报告](runtime/pr155-integration-08.md)与[工作包](work-packages/WP-IPC-PR155-INTEGRATION-08.md)。合入 main 不代表真实安装或现场验收通过。

2026-10-29 D 盘预检与服务登记错误保全见 [D-DRIVE-INSTALL-01](runtime/d-drive-install-01.md) 和[工作包](work-packages/WP-IPC-D-DRIVE-INSTALL-01.md)。SYSTEM 权限漏检已复现并修复；C/D、D/D 真实 SCM 矩阵因 Windows 管理授权未取得仍 NOT_RUN，未宣称现场 1783 已修复。当前 C 盘数据未迁移。

当前保障Sportmaster与必需共享基座。SM使用内置MVS采图＋进程内软件识别、内置RCS，每次独立识别交RCS去重，无PLC；停用外置扩展。GitHub CI停用，必要验证在本机执行。

## 当前规则

| 目的 | 当前入口 |
|---|---|
| 项目规则及事实优先级 | [AGENTS](../AGENTS.md) |
| 本轮代码/测试收敛 | [03工作包](work-packages/WP-IPC-CONTRACT-CONVERGENCE-03.md)、[03报告](runtime/contract-convergence-03.md)；[02结果](runtime/contract-convergence-02.md) |
| 架构职责 | [框架](design/framework.md)、[F决策](design/framework-decisions.md)、[独立Application](design/application-architecture-22.md) |
| SM连接和配置启用 | [MVS](runtime/mvs-software-reader-01.md)、[内置装配](work-packages/WP-IPC-SM-BUILT-IN-ONLY-01.md)、[自动连接与保存](work-packages/WP-IPC-SM-AUTO-CONNECTION-WORKFLOW-01.md) |
| MVS性能与诊断 | [时延调查及补采](runtime/mvs-latency-01.md)、[工作包](work-packages/WP-IPC-MVS-LATENCY-01.md)；旧真机记录保持原SourceCommit |
| SM原图与多码 | [同帧后台留档与多码参数](runtime/sm-original-frame-01.md)、[工作包](work-packages/WP-IPC-SM-ORIGINAL-FRAME-01.md)；默认拒绝多码，显式关闭才拼接 |
| 当前格式与原件保全 | [版本规则](runtime/version-compatibility-01.md)、[当前合同](runtime/current-contract-cleanup-01.md)、[从零安装](work-packages/WP-IPC-FRESH-BASELINE-01.md) |
| 管理权限与首配 | [管理模式](runtime/management-mode-01.md)、[运行入门](runtime/getting-started.md)、[字段归属](work-packages/WP-IPC-PARAMETER-GUIDANCE-STARTUP-01.md) |
| 参数与作业UI | [PARAM-UX-02](design/parameter-settings-experience-02.md)、[参数V3](ui/parameter-settings-v3-20261020/README.md)、[作业V2](ui/sportmaster-task-redesign-v2-20261020/README.md)、[字体](design/typography-01.md) |
| 首页条码与包裹图片 | [SM-WORKSTATION-03](work-packages/WP-IPC-SM-WORKSTATION-03.md)、[当前实现与待验证项目](runtime/sm-workstation-03.md)；键盘/PDA键盘输入、同帧日常留图、图片弹窗和四项导航 |
| 就绪时输入条码 | [SM-BARCODE-READY-06](work-packages/WP-IPC-SM-BARCODE-READY-06.md)、[原生定向验证](runtime/sm-barcode-ready-06.md)；就绪输入置顶，空态兼顾扫码/键盘/PDA |
| 当前调试按轮流程 | [SM-DEBUG-ROUND-04工作包](work-packages/WP-IPC-SM-DEBUG-ROUND-04.md)、[实现与待验证](runtime/sm-debug-round-04.md)；触发→同帧锁图/绿框→填请求→手动发送→再次扫码 |
| 扫码调试闭环验证 | [SM-DEBUG-CLOSURE-07](work-packages/WP-IPC-SM-DEBUG-CLOSURE-07.md)、[审查/定向验证/剩余边界](runtime/sm-debug-closure-07.md)；实际软件链路，真实设备/现场待验 |
| 调试筛选一致性修复 | [SM-DEBUG-FILTER-05](work-packages/WP-IPC-SM-DEBUG-FILTER-05.md)、[定向原生验证](runtime/sm-debug-filter-05.md)；null/显式空规则原义保留，04其他待验边界不变 |
| 管理界面与相机调试增量 | [SM-UI-DEBUG-02工作包](work-packages/WP-IPC-SM-UI-DEBUG-02.md)、[当前实现：待验证](runtime/sm-ui-debug-02.md)、[后续验证计划](runtime/sm-ui-debug-02-verification-plan.md)；[01历史实现与证据](runtime/sm-ui-debug-01.md)保留原来源 |
| RCS | [内置RCS](runtime/sportmaster-rcs-task.md)、[心跳](runtime/rcs-heartbeat-01.md)、[超时](runtime/rcs-timeout-errors-01.md) |
| 可靠性与维护 | [可靠性](runtime/code-reliability-hardening-02.md)、[长稳机制](runtime/win11-24x7-stability-remediation-01.md)、[历史维护](runtime/retention-ingress-02.md) |
| 安装与运行数据 | [Windows交付](runtime/windows-application-delivery.md)、[生命周期](runtime/lifecycle-ux-current-guidance-01.md)、[D盘](runtime/d-drive-storage-20261021.md) |
| 外部决定及公开范围 | [D决定](readiness/decision-pack.md)、[RV状态](reviews/STATUS.md)、[公开范围](baseline/PUBLICATION.md) |

SM相机离线只阻断依赖该相机的业务，自动重连不重发原请求。保存由Application有界等待在途，四类参数共用唯一候选和配置账，Pending与Effective分开。RCS空地址为待配置；有地址须完整校验。管理模式和操作员身份独立。

## 证据追溯

[整理前完整索引及控制文档](archive/pre-contract-convergence-02/README.md)保留原字节与SHA256。全部既有验证记录、冻结介质、D/RV及archive保持原身份。源代码构建、合成测试、本机Windows、系统安装、真实SDK/RCS及现场分别验收；历史PASS不能转记到当前提交。
