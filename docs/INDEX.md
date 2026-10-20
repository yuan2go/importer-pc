> 本轮不测试发布与PR69～71合并记录、原执行产物下载及边界见[发布报告](runtime/publication-pr69-pr71-20261020.md)。参数仍为实施中快照，原介质保留各自SourceCommit。

当前已授权进入 PARAM-01 参数正式重构，四分类、业务条码筛选及配置闭环以 [实施裁决](design/parameter-refactor-01.md) 为准；原“本次只做设计”是设计入库阶段记录。完整 UI 工作包其余范围不因本包实施自动完成。

> 桌面名称增量：[固定显示“菜鸟供包台”与旧测试资源清理](runtime/desktop-shortcut-name-01.md)，新构建与下面原生结果分开记录。

> 已验证安装体验交付：SM业务模块v1.4.1单EXE，冻结源码`27b0d8f6cc0281fd82ee68fcda350233cb0a45ff`。[单窗口/一次授权及58项实际安装启动记录](runtime/installer-single-window-01.md)；本机Simulation通过，Win10 22H2/其他CPU/海康现场待验。本包不包含下述UIR-01～06新设计的WPF移植，原7c发行身份保留。
> 2026-10-20 RCS心跳实施增量见[配置、实现与验证](runtime/rcs-heartbeat-01.md)。本增量由用户另行授权；下文“本次只做设计”专指UIR设计入库任务，不限制本次RCS开发。

# 文档索引：Sportmaster 任务与配置体验重设计

当前任务统一由下列设计和工作包管理。**本次只做设计入库、源码对照与实施计划；UIR-01～06尚未移植WPF，未新增软件运行或现场验收结果。** 代码复核冻结于 `e2e69292899c709056ce9ab6159bf01f6f18de6c`；实施时读取最新main及并行成果，不回退到原图源码。

## 1. 当前阅读顺序

| 顺序 | 入口与用途 |
| --- | --- |
| 1 | [AGENTS](../AGENTS.md)：工作范围、分支、现有机制与证据规则。 |
| 2 | [完整设计入口](ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)：可点击原型、逐页高清图、字段/动作迁移、源码与QA。 |
| 3 | [设计与真实代码的实施裁决](design/sportmaster-task-redesign-01.md)：对象、字段、配置、诊断及原型能力的真实边界。 |
| 4 | [UIR-00～06工作包](work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)：完整实施范围和六任务验收；[Codex实施提示](work-packages/Codex-WP-IPC-UI-TASK-REDESIGN-01.md)用于下一轮执行。 |
| 5 | [设计入库记录](runtime/ui-task-redesign-01-publication.md)：本次实际提交范围、材料数量、核对方法和未执行项。 |

新设计45个命名界面状态、48张高清/紧凑PNG，覆盖58项可辨认字段/状态及39项维护/首配动作。**95是原交付文件总数**，包含重复格式、源码、QA和整包；不是95个页面。因接口限制，原ZIP按11分片保存，附 [分片清单](ui/sportmaster-task-redesign-20261020/bundle/bundle-manifest.json) 和 [校验恢复脚本](ui/sportmaster-task-redesign-20261020/bundle/restore_bundle.py)，恢复保持原ZIP字节，不增加设计数量。

主导航目标是作业、记录、设备与设置、诊断。配置先选对象，再完成编辑、校验、完整修订保存及生效判断；日常修改不重复首次向导。原型中的地址、数值、版本和成功反馈均是模拟，实施按真实组件描述与Application契约映射。

## 2. 当前代码已具备什么

基准已包含PR62工作台精简与诊断、PR63独立IP海康Reader、PR64品牌/单EXE、PR66组合及最小区修复、PR67英文业务命名与来源记录、PR68原字节发布。已完成内容不重新列为缺失机制；[组合报告](runtime/parallel-task-reconciliation-20261020-02.md)与[安装发行说明](runtime/installer-release-info-01.md)保留精确来源。

- Sportmaster左右独立Reader/投递位，只取WCS格口，不下PLC、不新增图片预览。DirectPlc、WcsToPlc仍为独立Application；DirectPlc完整UI延期，Supply/Inspection保留各自契约。
- Reader是读码观察来源，Camera是可选采图能力，不能因中文“相机”误绑。海康五字段已有真实描述，驱动不拥有左右概念，也没有演示9000端口。
- 工作台、记录、维护和辅助窗口共享运行实例；长内容、焦点/选择/草稿、原生日历及旧格口失效保护保持。诊断OneWay已修，默认及classic最小区修复已完成。
- 完整候选校验、唯一配置账、草稿CAS、加密未完成输入、配置审计与受控启动已存在。对象表单只改变编辑范围；保存不等于生效，重连不等于应用Pending。
- 原件、观察、准入、请求和发送尝试分别保留。当前Sportmaster RCS声明Query=false，Health只证明本地生命周期；不可发假查询或把连接恢复当原请求成功。
- 菜鸟品牌、原生首配/安装、英文SM单EXE、目录与两个快捷方式、完成后启动及BUILD-INFO均继承。普通人工安装不要求无人值守或批量平台。

## 3. 架构、运行与维护入口

| 任务 | 有效资料 |
| --- | --- |
| 分层及业务所有权 | [框架](design/framework.md)、[F决策](design/framework-decisions.md)、[独立Application](design/application-architecture-22.md)、[能力职责](design/application-capability-19.md) |
| 构建、装配与版本 | [Application运行](runtime/application-architecture-22.md)、[扩展开发](extensions/developer-guide.md)、[受控扩展](design/controlled-extensions.md)、[版本与组合](runtime/version-compatibility-01.md) |
| 安装、依赖、首次运行 | [英文安装包与BUILD-INFO](runtime/installer-release-info-01.md)、[安装与启动修复](runtime/startup-deployment-02.md)、[首次运行](runtime/getting-started.md)、[安装范围](runtime/installation-delivery-scope-01.md) |
| 配置、恢复及交付维护 | [商业操作手册](runtime/sportmaster-commercial-delivery-01-operations.md)、[配置/容量/恢复](runtime/architecture-quality-closure-01.md)、[Windows交付](runtime/windows-application-delivery.md)、[原请求恢复](runtime/parcel-recovery-p2.md) |
| 海康和RCS真实边界 | [Reader运行与资格](runtime/hikrobot-reader-01.md)、[RCS协议与恢复](runtime/sportmaster-rcs-task.md) |
| Supply/Inspection兼容使用 | [Supply首次操作](runtime/supply-getting-started.md)、[维护](runtime/operations.md)、[共同账户前置门禁](runtime/runtime-identity-preflight-02.md) |

实现继续使用现有C#/WPF、锁定SDK/依赖和应用命令守卫。UI不直接读写配置账或SQL，不因换页重建设备会话；软件暂停不是安全急停。当前任务的文件入口与必要契约补足集中在实施裁决中，不另建通用配置或部署权威。

## 4. 设计、源码、介质与验收分别记录

| 对象 | 证据与适用范围 |
| --- | --- |
| 新设计与模拟QA | [设计入口](ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)、[UI状态](ui/STATUS.md)。只证明目标呈现和模拟交互，不证明WPF或生产链路。 |
| 现有WPF及组合 | [工作台原证据与补修](runtime/sportmaster-clear-workstation-01.md)、[组合报告](runtime/parallel-task-reconciliation-20261020-02.md)。原233项组合/9项完成页回归各守原源码、方法和环境。 |
| 已发布SM v1.4.1 | [原发行及41项证据](runtime/installer-release-info-01.md)、[原字节发布回执](runtime/publication-installer-release-info-20261020.md)。介质源码为 `7c384540393b1e77ceeba048a87a3916dd1af63c`，e2e文档提交不改标EXE来源。 |
| 后续实现验收 | [UIR-06](work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)、[既有Windows后验](runtime/validation/sportmaster-alternate-ui-windows.md)、[商业必要验收](runtime/validation/sportmaster-commercial-delivery-01.md)。按新源码执行受影响验证，原记录不改写。 |

正常作业最低有效客户区仍是800×420 DIP；不足空间保护、恢复后人工确认和软件暂停继续保留。几何夹具的原生HWND backing surface保留严格断言，只证明原生布局，不等于物理屏幕、多DPI或触控通过。原7c的41项升级/启动属于原Windows主机Simulation；受支持系统、精确SDK、trigger实体关联、客户RCS和现场资格按真实证据另验。

本次没有新增测试、构建、安装或现场PASS。该文档任务的未执行状态不免除以后代码变更的必要验证；缺外部协议时完成准确不可用说明及其余任务，不伪造绿色成功。

## 5. 历史来源与公开范围

| 历史资料 | 保留用途 |
| --- | --- |
| [v1.1原稿与34张PNG](ui/ss-ui-vis-001-v1.1/README.md) | 历史模拟设计与原交接，不是本次强制布局；共享箱/数量/拆包演示不能套入Sportmaster。 |
| [0e79e73原生36图集](archive/sportmaster-native-gallery-20261020-0e79e73/README.md) | 本次理解功能的原截图与导出来源，不代表最新main。 |
| [PR58视觉证据](runtime/sportmaster-visual-productization-01.md)、[日期续修](runtime/sportmaster-visual-main-closure-01.md) | 原冻结源码和验证方法，继承有效修复，保留原失败。 |
| [商业实施](runtime/commercial-productization-01-results.md)、[PR47复核](reviews/pr47-commercial-productization-review.md)、[早期部署发布](runtime/publication-deployment-20261019.md) | 已有能力与精确历史介质的追溯入口，不是多套当前任务。 |
| [本次清理前入口](archive/pre-ui-task-redesign-20261020/README.md) | README/INDEX/STATUS等e2e原字节及完整旧索引；旧文档继续可查。 |

[公开范围](baseline/PUBLICATION.md)、[D决策](readiness/decision-pack.md)、[RV状态](reviews/STATUS.md)和原REQ/BR/CAPI编号持续有效。只提交获授权材料；凭据、生产数据、未许可SDK/客户资料不纳入。资料清单见 [manifest](manifest.json)，校验方法见 [工具说明](../tools/README.md)；历史资产问题不通过新增豁免或局部PASS消除。
