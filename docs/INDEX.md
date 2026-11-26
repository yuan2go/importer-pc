# 文档索引

工程规则见 [AGENTS.md](../AGENTS.md)。下表逐文件列出当前维护的契约、操作指南和构建输入。带“随包”的行说明打包脚本为何保留该路径；具体产品与交付文件名可查[发布映射](../scripts/release_documents.py)。[归档](archive/) 保存旧决策、审计、工作包和验证原件，按原提交查证。

## 平台通用能力（跨业务复用）

| 用途 | 文件 |
| --- | --- |
| 架构与构建 | [架构基线与模块边界](design/framework.md) · [运行入门](runtime/getting-started.md) · [D 盘开发与新站存储](runtime/d-drive-storage-20261021.md) |
| 当前格式与回归 | [代码与验证边界](runtime/current-contract-cleanup-01.md) · [配置及存储版本](runtime/version-compatibility-01.md) · [Parcel14 数据操作](runtime/parcel12-operations-26.md)（随包名 PARCEL14-OPERATIONS.md） |
| 安装与账户安全 | [部署生命周期](design/industrial-lifecycle-01.md) · [管理密码与机器维护权限](design/industrial-security-recovery-01.md) · [D 盘预检](runtime/d-drive-install-01.md) · [首次管理密码](runtime/management-password-startup-01.md) |
| Windows 交付 | [企业分发方式](design/enterprise-packaging-07.md) · [代码签名接入](design/code-signing-onboarding-06.md) · [Windows x64 兼容范围](runtime/windows-client-compatibility-01.md) · [Unicode 快捷方式](runtime/unicode-shortcut-compatibility-01.md) |
| 随包稳定性说明 | [存储故障与时间](runtime/time-storage-fault-01.md) · [P0 稳定性](runtime/p0-stability-01.md) · [输入、持久命令与离线投影](runtime/architecture-quality-remediation-01.md)。其中阶段性实测仅对应所记载构建。 |
| 随既有 Parcel 产品交付的材料 | [拓扑和角色](design/application-architecture-22.md) · [装配运行](runtime/application-architecture-22.md) · [所有权与日志](runtime/code-reliability-hardening-02.md) · [容量与备份](runtime/latency-capacity-01.md) |
| 随既有 Parcel 产品交付的安装资料 | [商业交付操作](runtime/commercial-productization-01-operations.md) · [安装与首启](runtime/startup-deployment-02.md) · [Windows 交付](runtime/windows-application-delivery.md)。SM 现场操作见本页末节。 |
| 暂停开发的装配，保留发布输入 | [Parcel 外置扩展说明](extensions/developer-guide.md) · [Supply 扩展说明](extensions/supply-guide.md) · [Supply 入门](runtime/supply-getting-started.md) · [Supply 运行维护](runtime/operations.md)。外置扩展不参与 SM 作业。 |
| CI 与公开边界 | [CI 路径选择与门禁](runtime/industrial-ci-policy-01.md)（路径亦用于 CI 选路测试） · [资产公开范围](baseline/PUBLICATION.md) |
| 正式发布清单和随包许可 | [文件清单](manifest.json) · [清单摘要](manifest.sha256) · [MIT](licenses/MIT.txt) · [Apache 2.0](licenses/Apache-2.0.txt) |

## SM 业务专属契约

| 用途 | 文件 |
| --- | --- |
| 供包与物理边界 | [连续供包契约](design/sm-continuous-induction-closure-01.md) · [轮次与原请求生命周期](design/sm-parcel-rounds.md) · [车辆到位及发送责任](design/sm-vehicle-arrival-rounds.md) · [投递位启停](design/station-position-control.md) |
| RCS 协议与运行 | [取格口字段及结果](design/sportmaster-rcs-task.md) · [内置 RCS 的运行与验证](runtime/sportmaster-rcs-task.md) · [心跳与到位](runtime/rcs-heartbeat-01.md) · [RCS 参数与冻结报文](design/rcs-parameter-description-compatibility.md) |
| 相机与条码 | [MVID 原生识码](design/mvid-native-reader.md) · [MVS 软件识码](runtime/mvs-software-reader-01.md) · [双相机性能测量](design/sm-dual-camera-performance.md) · [扫码、调试与诊断](runtime/sm-operator-corrections-29.md)（随包） |
| 配置与调试 | [参数归属、默认值及生效](runtime/parameter-inheritance-02.md) · [首配与启动](runtime/parameter-guidance-startup-01.md) · [现场设置与调试范围](design/management-workspace.md) · [管理授权](runtime/management-mode-01.md) |
| 进程与实例 | [工作站与运维的功能归属](design/sm-dual-desktop-operator-operations-proposal.md) · [受控 IPC 与离线维护](design/sm-readonly-operations.md) · [正式与测试实例隔离](design/sm-test-instance-isolation.md) |
| 运营记录 | [运行状态与日志证据](design/runtime-state-logging.md) · [班次与记录](runtime/sm-session-shift-exceptions-25.md) · [工作站日志视图](runtime/workstation-status-logs-01.md) · [调试与实时终端](runtime/debug-log-live-01.md)。后三份为随包说明。 |
| 生命周期与现场验证 | [安装、配置及卸载](runtime/lifecycle-ux-current-guidance-01.md) · [长稳采集与完整备份分析](runtime/win11-24x7-stability-remediation-01.md) |

## UI 规范与真值源

| 用途 | 文件 |
| --- | --- |
| 字体与语义尺度 | [TYPOGRAPHY-01](design/typography-01.md) |
| 定稿原型与视觉令牌 | [工作站与运维原型](ui-v4/importer_pc_ui_v5.html) · [ThemeTokens.xaml](ui-v4/ThemeTokens.xaml) |
| WPF 审查方法 | [项目审查规范](design/wpf-ui-review.md) · [hallmark-wpf](../.agents/skills/hallmark-wpf/SKILL.md) |
| 作业与导航行为 | [作业卡各状态](design/work-card-states.md) · [工作站与运维导航](design/operator-navigation-20261103.md) |
| 编译输入 | [tokens.json](ui/tokens/tokens.json) 以 Ipc.OperatorTokens.json 嵌入 WPF 程序和验证工程；配色与字体分别查定稿和 TYPOGRAPHY-01。 |

## 现场实施手册

| 读者 / 时机 | 文件 |
| --- | --- |
| 供包员开工、取件、核对结果 | [供包员作业卡](operator/quick-guide.md) |
| 实施人员安装到首件 | [部署卡](operator/deployment-card.md) |
| 现场配置、维护及恢复 | [实施与维护手册](operator/implementation-guide.md) |
| 根据故障码定位及处置 | [故障目录](operator/fault-codes.md) |
| 交付打印稿 | [生成与排版核查](runtime/industrial-print-guides-01.md) |

四份现场手册同时用作 SM 内嵌帮助与离线交付原稿。
