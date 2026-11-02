# 文档目录

当前项目事实以[AGENTS](../AGENTS.md)、本轮工作包和实际源码为准。设计、实现、合成验证、Windows原生、系统安装、真实设备/接口及现场验收分别记录。

## 现场与实施

| 读者/任务 | 入口 |
|---|---|
| 供包员 | [作业卡](operator/quick-guide.md) |
| 实施人员 | [部署卡](operator/deployment-card.md)、[实施手册](operator/implementation-guide.md) |
| 故障处置 | [故障目录](operator/fault-codes.md)、[日志/维护现行操作](runtime/lifecycle-ux-current-guidance-01.md) |
| 当前数据与恢复 | [Parcel12](runtime/parcel12-operations-26.md)、[版本兼容](runtime/version-compatibility-01.md) |
| 管理权限 | [管理模式](runtime/management-mode-01.md)、[IE01当前安全修复](runtime/industrial-engineering-01.md) |

现场文案正在按IE01候选整理，打印页数/新人旅程未验；新指南明确尚未交付的入口，不以文档替代产品实现。

## 当前实现与设计

| 范围 | 入口 |
|---|---|
| IE01全量工业化 | [总设计](design/industrial-engineering-01.md)、[安全](design/industrial-security-recovery-01.md)、[生命周期](design/industrial-lifecycle-01.md)、[操作与质量](design/industrial-operations-quality-01.md)、[实施/验收工作包](work-packages/WP-IPC-INDUSTRIAL-ENGINEERING-01.md)、[实际进度](runtime/industrial-engineering-01.md) |
| 分层、所有权与决策 | [框架](design/framework.md)、[F决策](design/framework-decisions.md)、[Application](design/application-architecture-22.md) |
| 内置MVS与软件识别 | [MVS运行](runtime/mvs-software-reader-01.md)、[内置装配](work-packages/WP-IPC-SM-BUILT-IN-ONLY-01.md)、[自动连接/配置启用](work-packages/WP-IPC-SM-AUTO-CONNECTION-WORKFLOW-01.md) |
| 托盘与当前识别 | [一次放置一次请求](work-packages/WP-IPC-SM-SAME-PLACEMENT-29.md)、[识别可靠性](runtime/sm-reader-robustness-28.md)、[无码/坏帧](runtime/sm-unread-package-27.md) |
| 班次/统计/记录 | [运行班次](runtime/sm-session-shift-exceptions-25.md)、[当前产品闭环](runtime/sm-product-closure-21.md) |
| 人工来源与调试 | [人工相机独立](work-packages/WP-IPC-SM-MANUAL-CAMERA-INDEPENDENT-12.md)、[按轮调试](runtime/sm-debug-closure-07.md) |
| RCS与原请求 | [内置RCS](runtime/sportmaster-rcs-task.md)、[心跳](runtime/rcs-heartbeat-01.md)、[超时](runtime/rcs-timeout-errors-01.md) |
| 参数和UI | [PARAM-UX-02](design/parameter-settings-experience-02.md)、[参数V3](ui/parameter-settings-v3-20261020/README.md)、[作业/首配V2](ui/sportmaster-task-redesign-v2-20261020/README.md)、[字体](design/typography-01.md) |
| 安装与存储 | [Windows交付](runtime/windows-application-delivery.md)、[D盘默认](runtime/d-drive-defaults-02.md)、[IE01生命周期](design/industrial-lifecycle-01.md) |
| 可靠性与历史维护 | [可靠性](runtime/code-reliability-hardening-02.md)、[长稳](runtime/win11-24x7-stability-remediation-01.md)、[到期维护](runtime/retention-ingress-02.md) |

SM无PLC、不依赖外置扩展；相机填写实际IP/序列号/唯一编码，取格口与心跳共用映射。相机自动来源与人工来源分别守卫；没有日常暂停/恢复按钮。四类参数共用唯一候选与配置账；待生效和正在使用的配置分开。Unknown固定原请求/目标/合同和字节，无权威Query合同就不假造查询。

## 开发与追溯

- [开发运行入门](runtime/getting-started.md)、[全部工作包](work-packages/README.md)、[IE01基线核查](reviews/industrial-engineering-design-01.md)
- [D决定](readiness/decision-pack.md)、[历史RV状态](reviews/STATUS.md)、[公开范围](baseline/PUBLICATION.md)
- [CHANGELOG](../CHANGELOG.md)、[整理前索引及来源清单](archive/pre-industrial-user-docs-01/manifest.json)、[更早完整控制文档](archive/pre-contract-convergence-02/README.md)

历史原型、介质、失败/通过及SourceCommit保留原身份。旧文档中的“当前”受形成时范围限制，不能用历史PASS替代本轮验收。证据外置尚未执行，未经目标存储/许可核验不移动客户资产或重写Git历史。
