# 文档索引

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
