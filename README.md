# importer-pc

C#/.NET 10 Windows 工位客户端与独立 Application 工程。Platform 提供机制，Application 拥有业务状态和命令守卫，Driver 连接设备，Component 适配外部服务；同一业务实例服务多个页面与窗口。

## 当前任务：Sportmaster 任务与配置体验重设计

从 [完整设计入口](docs/ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md) 查看原型、高清图、字段映射和交接，再读 [实施裁决](docs/design/sportmaster-task-redesign-01.md)、[UIR 工作包](docs/work-packages/WP-IPC-UI-TASK-REDESIGN-01.md) 与 [Codex 实施提示](docs/work-packages/Codex-WP-IPC-UI-TASK-REDESIGN-01.md)。提交与材料核对见 [设计入库记录](docs/runtime/ui-task-redesign-01-publication.md)。

**本次仅交付设计资料、代码对照和实施计划；UIR-01～06尚未移植到 WPF。** 设计包含45个命名界面状态、48张PNG。95是含重复格式、源码、交接、QA及ZIP的原文件总数，不是95份界面设计。整包因接口限制以11分片和校验恢复保存，不增加设计数量。

代码复核基准为 `e2e69292899c709056ce9ab6159bf01f6f18de6c`，已含PR62/63/64/66/67/68。实施时继承最新main；主攻对象配置、错误定位、差异与生效状态、任务诊断，复用已完成机制。完整导航见 [文档索引](docs/INDEX.md)，协作规则见 [AGENTS](AGENTS.md)。

## 已实现能力与业务边界

Sportmaster、DirectPlc、WcsToPlc是独立主流程。Sportmaster左右读码相机取得上游格口并展示，无PLC下发或新增包裹预览；读码Reader与可选采图Camera不同。海康字段以实际驱动描述为准，没有原型中的通用9000端口。登录、称重等按真实组合适用，不新增每包确认。

默认工作台与辅助窗口共享Application。诊断OneWay、长格口、原生日历和800×420 DIP有效客户区保护已实现。配置沿唯一CAS/Pending/Effective账，保存后受控重启；重连不应用配置。原请求不盲重发；当前RCS无权威Query，Health仅证明本地状态，不能显示远端业务已恢复。

## 安装、维护与开发

| 任务 | 有效入口 |
| --- | --- |
| 人工安装、升级与依赖 | [英文SM单EXE及源码记录](docs/runtime/installer-release-info-01.md)、[发布下载记录](docs/runtime/publication-installer-release-info-20261020.md) |
| 首配、故障修复与数据恢复 | [安装与启动修复](docs/runtime/startup-deployment-02.md)、[商业操作手册](docs/runtime/sportmaster-commercial-delivery-01-operations.md) |
| 架构、构建与扩展 | [框架](docs/design/framework.md)、[Application运行](docs/runtime/application-architecture-22.md)、[扩展开发](docs/extensions/developer-guide.md) |

继承菜鸟品牌、原生首配、单EXE、目录/快捷方式及BUILD-INFO，不重建安装平台。已发布SM v1.4.1介质来自 `7c384540393b1e77ceeba048a87a3916dd1af63c`；原41项实际升级/启动只证明原主机Simulation范围，不证明新UI或真实设备已验。

## 证据与历史

[UI状态](docs/ui/STATUS.md)区分新设计、现有WPF和原验证。[v1.1及34张PNG](docs/ui/ss-ui-vis-001-v1.1/README.md)是历史模拟设计，不是本次强制布局。原失败、D/RV和各版介质身份保留；本次未新增测试、构建、安装或现场PASS，未来代码变更仍须做必要验证。

遵守 [公开范围](docs/baseline/PUBLICATION.md)，不纳入未许可SDK、客户资料、凭据或生产数据。清理前入口原字节见 [归档](docs/archive/pre-ui-task-redesign-20261020/README.md)。
