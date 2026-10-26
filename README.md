> 时间与存储故障分级：[工作包](docs/work-packages/WP-IPC-TIME-STORAGE-FAULT-01.md)、[运行/依赖方案](docs/runtime/time-storage-fault-01.md)、[问题卡与验证边界](docs/runtime/validation/time-storage-fault-01.md)、[全仓UTC清单](docs/runtime/time-storage-fault-01-utc-inventory.md)。ST12/ST13及ST08隔离进程闭环已本机验证；PR112按用户选择叠加PR110，现场/已登记恢复策略另验。

> 当前源码操作入口：[文档索引](docs/INDEX.md)、[管理模式](docs/runtime/management-mode-01.md)、[本轮架构质量整改](docs/work-packages/WP-IPC-ARCHITECTURE-QUALITY-REMEDIATION-01.md)。本轮不是安装包发行；SM1.11.1重测介质及历史发布按索引分别读取。下述V3形成时状态保留历史范围。
> 父PR110原阶段入口（身份保留）：[文档索引](docs/INDEX.md)、[P0稳定性工作包](docs/work-packages/WP-IPC-P0-STABILITY-01.md)、[本次问题卡](docs/runtime/validation/p0-stability-01.md)与[管理模式](docs/runtime/management-mode-01.md)。SM1.14.0工程候选、Contracts1.18.0、DB9；这不是Production运行资格。历史介质及下述V3形成时状态各守原SourceCommit。
> P0草稿门禁：run40222910122（检查head1c769e10、实际CI源32170b66）Linux与完整冻结job通过，Windows P0独立步骤通过；原有非CJK代码页COM快捷方式断言失败使总CI仍FAIL。确切来源、12项冻结摘要与NOT_RUN见本次问题卡/ci-1c-final.json，后续仅文档提交不继承PASS。


> 历史安装包：[SM v1.6.1 · Windows x64 单 EXE](https://github.com/yuan2go/importer-pc/releases/download/installer-sm-v1.6.1-92586bf44cde/CainiaoSupplyStation_SM_Setup_v1.6.1_win-x64.exe)。PR76 已合并，固定源 `92586bf44cde`；[发布记录、哈希与边界](docs/runtime/publication-login-session-20261020.md)。已修复日常登录会话时长字段隐藏，未签名预发布，真实设备/现场待验。
>
> [V2 实施与运行](docs/runtime/ui-task-redesign-01.md)、[原版 UI 验证](docs/runtime/evidence/ui-task-redesign-v2/ACCEPTANCE.md)、[WPF 高清图](docs/ui/sportmaster-v2-wpf-20261020/README.md)仍保留各自构建身份。原型、本机 Windows、安装冒烟与真实设备分别计证。

# importer-pc

C#/.NET 10 Windows 工位客户端与独立 Application 工程。Platform 提供机制，Application 拥有业务状态和命令守卫，Driver 连接设备，Component 适配外部服务；同一业务实例服务多个页面与窗口。

## 参数设置 V3 及后续增量

从 [新版参数界面、可点击原型与27张高清图](docs/ui/parameter-settings-v3-20261020/README.md)直接查看本轮成果；[设计决定](docs/design/parameter-settings-experience-02.md)、[开发交接](docs/ui/parameter-settings-v3-20261020/HANDOFF.md)、[原验证](docs/ui/parameter-settings-v3-20261020/QA.md)和[最新源码核查与Codex执行提示](docs/work-packages/WP-IPC-PARAMETER-SETTINGS-02.md)统一在此。

参数采用同窗专注工作区、一个四类对象目录、自动校验与统一保存审阅。“维护与迁移”由模板、备份、安装、原账核对等具体任务入口替代。四类仍编辑同一完整候选；已保存与运行中分别显示。2026-10-21已核实main `42a047c3`包含PR78/79；V3的54个设计文件完整、53项登记哈希及25项源码指纹全部一致。下一步沿现有工作包修复实际差距并完成Windows原生交付，见[实施记录](docs/runtime/parameter-settings-experience-02.md)。原`837370e`/`bf61337`保留为设计形成时基线；上面的1.6.1安装包保留原源身份，并不包含V3。

[V2](docs/ui/sportmaster-task-redesign-v2-20261020/README.md)继续作为三组作业信息、首次启动和原业务合同的基线；其中参数版式和旧保存步骤已由V3替代。原资料、原哈希和历史验收保持，不重复导出或累计作新页面数。完整导航见 [文档索引](docs/INDEX.md)，工程约束见 [AGENTS](AGENTS.md)。

## 已实现能力与业务边界

Sportmaster、DirectPlc、WcsToPlc是独立主流程。Sportmaster左右读码相机取得上游格口及任务回执并展示，无PLC下发或新增包裹预览；读码Reader与可选采图Camera不同。海康字段以实际驱动描述为准，没有原型中的通用9000端口。登录、称重等按真实组合适用，不新增每包确认。

默认工作台与辅助窗口共享Application。诊断OneWay、长格口、原生日历和800×420 DIP有效客户区保护已实现。配置沿唯一CAS/Pending/Effective账，保存后受控重启；重连不应用配置。原请求不盲重发；当前RCS无权威Query；Health含客户端及心跳HTTP交付/失败证据，但不证明应用确认或原业务成功。心跳与取格口编码保持真实用途，未经合同确认不静默互换。

## 安装、维护与开发

| 任务 | 有效入口 |
| --- | --- |
| 人工安装、升级与依赖 | [历史1.6.1下载及回执](docs/runtime/publication-login-session-20261020.md)、[单窗口/一次授权](docs/runtime/installer-single-window-01.md)、[固定桌面名称](docs/runtime/desktop-shortcut-name-01.md)、[发行依赖](docs/runtime/installer-release-info-01.md) |
| 首配、故障修复与数据恢复 | [安装与启动修复](docs/runtime/startup-deployment-02.md)、[商业操作手册](docs/runtime/sportmaster-commercial-delivery-01-operations.md) |
| 架构、构建与扩展 | [框架](docs/design/framework.md)、[Application运行](docs/runtime/application-architecture-22.md)、[扩展开发](docs/extensions/developer-guide.md) |

继承菜鸟品牌、首配、单窗口单EXE安装、目录/快捷方式、一次UAC与普通权限启动、BUILD-INFO，不重建安装平台。历史SM1.6.1来自已合并main `92586bf44cde7efb6cec1538d7b9654ee518c0fb`，本轮验证同源构建、完整介质、最终DLL的会话字段、实际输入与安装准备/取消，未重验完整安装/UAC/升级。历史[SM1.6.0](docs/runtime/publication-pr74-20261020.md)、SM1.4.1介质 `27b0d8f` 的58项、原7c的41项及心跳portable CLI/签名夹具 `3f8a58ba` 各守原来源，详见[历史发布范围](docs/runtime/publication-pr69-pr71-20261020.md)。

## 证据与历史

[UI状态](docs/ui/STATUS.md)区分 V2 设计、现有 WPF 和原验证。[V1 设计归档入口](docs/ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)保留原45状态/48张PNG/95个物理文件及原哈希；95不是页面数。[v1.1及34张PNG](docs/ui/ss-ui-vis-001-v1.1/README.md)是历史模拟设计。原失败、D/RV和各版介质身份保留；原型模拟 QA、UI 实施与本次发布验证分别记录，均不能代替真实设备或现场验收。

遵守 [公开范围](docs/baseline/PUBLICATION.md)，不纳入未许可SDK、客户资料、凭据或生产数据。清理前入口原字节见 [归档](docs/archive/pre-ui-task-redesign-20261020/README.md)。
