> 当前实施：[V2 实施与验证记录](docs/runtime/ui-task-redesign-01.md)。实施基准 main `de2b045`；此前设计、PR73及其他介质验证各守原版本。

# importer-pc

C#/.NET 10 Windows 工位客户端与独立 Application 工程。Platform 提供机制，Application 拥有业务状态和命令守卫，Driver 连接设备，Component 适配外部服务；同一业务实例服务多个页面与窗口。

## 当前任务：Sportmaster V2 任务与配置体验重设计

从 [V2 设计入口](docs/ui/sportmaster-task-redesign-v2-20261020/README.md)查看原型、逐张高清图、字段映射和交接，再读 [实施裁决](docs/design/sportmaster-task-redesign-01.md)、[UIR 工作包](docs/work-packages/WP-IPC-UI-TASK-REDESIGN-01.md)与 [Codex 实施提示](docs/work-packages/Codex-WP-IPC-UI-TASK-REDESIGN-01.md)。本轮材料范围见 [V2 发布记录](docs/runtime/ui-task-redesign-v2-publication-20261020.md)；[前一版入库记录](docs/runtime/ui-task-redesign-01-publication.md)保留原提交事实。

**当前已获授权实施 UIR-00～06 并执行必要验证，继承 PR73 已完成的 PARAM-01。设计入库阶段的“仅设计／不测试”不限制本实施包；实际完成状态与证据以 V2 实施记录为准。** 参数配置为系统、业务模块、驱动、组件四类；首配为首次启动的五步独立流程；每个左右相机框固定相机信息、扫描候选/业务选中码、RCS任务信息，展示格口、波次、任务、SKU 与返回数量。界面状态/PNG 数量见 V2 索引，源码与格式副本不计作额外页面。

实施基准为最新 main `de2b045a6eb66c66cc22a237fb2c4e0f84f98e78`，已含PR69单窗口安装/固定桌面名称、PR70心跳、PR71参数快照和PR72发布。复用[PARAM已有模型与保存核对](docs/runtime/parameter-refactor-01.md)、条码筛选、首配共模和已合入机制；按用户本轮字段归属完善[参数实施](docs/design/parameter-refactor-01.md)、三组任务展示、对象编辑及异常返回，不回滚或重做并行成果。完整导航见 [文档索引](docs/INDEX.md)，协作规则见 [AGENTS](AGENTS.md)。

## 已实现能力与业务边界

Sportmaster、DirectPlc、WcsToPlc是独立主流程。Sportmaster左右读码相机取得上游格口及任务回执并展示，无PLC下发或新增包裹预览；读码Reader与可选采图Camera不同。海康字段以实际驱动描述为准，没有原型中的通用9000端口。登录、称重等按真实组合适用，不新增每包确认。

默认工作台与辅助窗口共享Application。诊断OneWay、长格口、原生日历和800×420 DIP有效客户区保护已实现。配置沿唯一CAS/Pending/Effective账，保存后受控重启；重连不应用配置。原请求不盲重发；当前RCS无权威Query；Health含客户端及心跳HTTP交付/失败证据，但不证明应用确认或原业务成功。心跳与取格口编码保持真实用途，未经合同确认不静默互换。

## 安装、维护与开发

| 任务 | 有效入口 |
| --- | --- |
| 人工安装、升级与依赖 | [单窗口/一次授权](docs/runtime/installer-single-window-01.md)、[固定桌面名称](docs/runtime/desktop-shortcut-name-01.md)、[原产物下载与发布回执](docs/runtime/publication-pr69-pr71-20261020.md)、[发行依赖](docs/runtime/installer-release-info-01.md) |
| 首配、故障修复与数据恢复 | [安装与启动修复](docs/runtime/startup-deployment-02.md)、[商业操作手册](docs/runtime/sportmaster-commercial-delivery-01-operations.md) |
| 架构、构建与扩展 | [框架](docs/design/framework.md)、[Application运行](docs/runtime/application-architecture-22.md)、[扩展开发](docs/extensions/developer-guide.md) |

继承菜鸟品牌、首配、单窗口单EXE安装、目录/快捷方式、一次UAC与普通权限启动、BUILD-INFO，不重建安装平台。最新已上传单窗口SM1.4.1介质来自 `27b0d8f6cc0281fd82ee68fcda350233cb0a45ff`，58项为该原Windows主机Simulation记录；心跳portable CLI/签名夹具来自 `3f8a58ba32887df07f6a7817b64afa693f903c50`。原7c/41项保留历史，两类新介质均不改标为ba80参数快照或V2；详见[发布范围](docs/runtime/publication-pr69-pr71-20261020.md)。

## 证据与历史

[UI状态](docs/ui/STATUS.md)区分 V2 设计、现有 WPF 和原验证。[V1 设计归档入口](docs/ui/sportmaster-task-redesign-20261020/REPOSITORY-ENTRY.md)保留原45状态/48张PNG/95个物理文件及原哈希；95不是页面数。[v1.1及34张PNG](docs/ui/ss-ui-vis-001-v1.1/README.md)是历史模拟设计，不是本次强制布局。原失败、D/RV和各版介质身份保留；原型模拟 QA 按其实际记录判断，本次未新增生产代码构建、Windows/设备或现场 PASS，未来代码变更仍须做必要验证。

遵守 [公开范围](docs/baseline/PUBLICATION.md)，不纳入未许可SDK、客户资料、凭据或生产数据。清理前入口原字节见 [归档](docs/archive/pre-ui-task-redesign-20261020/README.md)。
