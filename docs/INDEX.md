# 文档索引

当前保障Sportmaster与必需共享基座。SM使用内置MVS采图＋进程内软件识别、内置RCS，每次独立识别交RCS去重，无PLC；停用外置扩展。GitHub CI停用，必要验证在本机执行。

## 当前规则

| 目的 | 当前入口 |
|---|---|
| 项目规则及事实优先级 | [AGENTS](../AGENTS.md) |
| 本轮代码/测试收敛 | [03工作包](work-packages/WP-IPC-CONTRACT-CONVERGENCE-03.md)、[03报告](runtime/contract-convergence-03.md)；[02结果](runtime/contract-convergence-02.md) |
| 架构职责 | [框架](design/framework.md)、[F决策](design/framework-decisions.md)、[独立Application](design/application-architecture-22.md) |
| SM连接和配置启用 | [MVS](runtime/mvs-software-reader-01.md)、[内置装配](work-packages/WP-IPC-SM-BUILT-IN-ONLY-01.md)、[自动连接与保存](work-packages/WP-IPC-SM-AUTO-CONNECTION-WORKFLOW-01.md) |
| 当前格式与原件保全 | [版本规则](runtime/version-compatibility-01.md)、[当前合同](runtime/current-contract-cleanup-01.md)、[从零安装](work-packages/WP-IPC-FRESH-BASELINE-01.md) |
| 管理权限与首配 | [管理模式](runtime/management-mode-01.md)、[运行入门](runtime/getting-started.md)、[字段归属](work-packages/WP-IPC-PARAMETER-GUIDANCE-STARTUP-01.md) |
| 参数与作业UI | [PARAM-UX-02](design/parameter-settings-experience-02.md)、[参数V3](ui/parameter-settings-v3-20261020/README.md)、[作业V2](ui/sportmaster-task-redesign-v2-20261020/README.md)、[字体](design/typography-01.md) |
| 首页条码与包裹图片 | [SM-WORKSTATION-03](work-packages/WP-IPC-SM-WORKSTATION-03.md)、[当前实现与待验证项目](runtime/sm-workstation-03.md)；键盘/PDA键盘输入、同帧日常留图、图片弹窗和四项导航 |
| 当前调试按轮流程 | [SM-DEBUG-ROUND-04工作包](work-packages/WP-IPC-SM-DEBUG-ROUND-04.md)、[实现与待验证](runtime/sm-debug-round-04.md)；触发→同帧锁图/绿框→填请求→手动发送→再次扫码 |
| 管理界面与相机调试增量 | [SM-UI-DEBUG-02工作包](work-packages/WP-IPC-SM-UI-DEBUG-02.md)、[当前实现：待验证](runtime/sm-ui-debug-02.md)、[后续验证计划](runtime/sm-ui-debug-02-verification-plan.md)；[01历史实现与证据](runtime/sm-ui-debug-01.md)保留原来源 |
| RCS | [内置RCS](runtime/sportmaster-rcs-task.md)、[心跳](runtime/rcs-heartbeat-01.md)、[超时](runtime/rcs-timeout-errors-01.md) |
| 可靠性与维护 | [可靠性](runtime/code-reliability-hardening-02.md)、[长稳机制](runtime/win11-24x7-stability-remediation-01.md)、[历史维护](runtime/retention-ingress-02.md) |
| 安装与运行数据 | [Windows交付](runtime/windows-application-delivery.md)、[生命周期](runtime/lifecycle-ux-current-guidance-01.md)、[D盘](runtime/d-drive-storage-20261021.md) |
| 外部决定及公开范围 | [D决定](readiness/decision-pack.md)、[RV状态](reviews/STATUS.md)、[公开范围](baseline/PUBLICATION.md) |

SM相机离线只阻断依赖该相机的业务，自动重连不重发原请求。保存由Application有界等待在途，四类参数共用唯一候选和配置账，Pending与Effective分开。RCS空地址为待配置；有地址须完整校验。管理模式和操作员身份独立。

## 证据追溯

[整理前完整索引及控制文档](archive/pre-contract-convergence-02/README.md)保留原字节与SHA256。全部既有验证记录、冻结介质、D/RV及archive保持原身份。源代码构建、合成测试、本机Windows、系统安装、真实SDK/RCS及现场分别验收；历史PASS不能转记到当前提交。
