
# importer-pc

C#/.NET 10 Windows 工位运行基座与独立 Application 参考工程。Platform 提供机制，Application 拥有业务状态、守卫及 UI 契约，Driver 适配硬件，Component 适配外部服务。一个 Application 运行实例可服务多个页面和布局。

## 当前工作入口

**当前工作：[并发任务整合](docs/work-packages/WP-IPC-PARALLEL-TASK-RECONCILIATION-20261020.md)**。任务对照、冲突裁决、最终组合验证与合并状态见 [本次报告](docs/runtime/parallel-task-reconciliation-20261020.md)。商业使用继续按 [同版交付手册](docs/runtime/sportmaster-commercial-delivery-01-operations.md)，源任务历史证据保持原 SHA。

继承 [COMM-00～10 实施结果](docs/runtime/commercial-productization-01-results.md)、PR44/45 的原生安装/独立维护和身份守卫。指定 Windows 账户以非提权进程运行；正常安装、启动和维护不要求 PowerShell，开发验收脚本另列依赖。新窗口只整理现有契约调用，不新建配置或部署权威。

开始工作先读[统一索引](docs/INDEX.md)、[AGENTS](AGENTS.md)、本包及关联设计/实施/验收。修改代码前先清理相关过期当前指引，保留历史证据；继承最新 main，避免重做已完成能力。

## 当前运行与组合

Sportmaster、DirectPlc、WcsToPlc 保持三个独立主流程。Sportmaster 左右两个独立 reader/投递位，扫码取得上游格口，不下 PLC；DirectPlc 与 WcsToPlc 各单 reader/顺序位，分别走 PLC 与 WCS→PLC。Supply 和 Inspection 保留各自契约及入口，不套用 Parcel 的数据库或命令。

Sportmaster 缺省使用 supply-station，显式 `--ui classic` 保留兼容布局，导航及辅助窗口共享 Application、连接和原作业。登录、称重、采图、上传按实际已确认组合选择；Sportmaster 图片展示和长期图库不在范围，DirectPlc 完整 UI 继续延期。

[Sportmaster RCS 组件](docs/runtime/sportmaster-rcs-task.md)只按已支持合同处理目标请求、NEED_UNPACK、拒绝与 Unknown，不提供未确认的拆包提交或自动重发。数据库、配置 schema、公共契约和包版本按[版本与组合证据](docs/runtime/version-compatibility-01.md)及实际介质清单读取；版本号本身不证明精确加载兼容或 Production 资格。

## 安装、开发与维护

Windows 当前源码的原生流程、运行账户、配置来源及离线维护见[安装与启动修复](docs/runtime/startup-deployment-02.md)。首次使用见[首次运行](docs/runtime/getting-started.md)，升级/修复/恢复见[Windows 交付指南](docs/runtime/windows-application-delivery.md)和[配置、容量及恢复维护](docs/runtime/architecture-quality-closure-01.md)。商业化增量已沿原生部署内核实施，系统安装项是唯一部署登记的派生投影；自选程序根、首配修复、备份迁移和保留数据卸载见产品操作指南。

开发构建及发布入口见[Application 运行指南](docs/runtime/application-architecture-22.md)、[扩展开发](docs/extensions/developer-guide.md)。使用 global.json 固定 SDK 和锁定依赖，仅按受影响范围执行必要验证。发布时从固定源码生成新介质，读取 ASSEMBLY.json、application-release.json、依赖锁和 SHA256SUMS，不改标旧产物。

配置沿同一 CAS/Pending/Effective 账，资源成功后确认；停止失败保持资源与包租约。旧库升级先核未结业务和原件责任，不能通过升级消除 Unknown。原请求固定原报文、目标、合同及版本。软件暂停不是安全急停。

## 源码与已发布介质

[部署和全部入口门禁的合并/发布记录](docs/runtime/publication-deployment-20261019.md)区分最新合并源码与两组既有介质：

|参考发布|固定源码及范围|
|---|---|
|[原生部署 reference](https://github.com/yuan2go/importer-pc/releases/tag/startup-deployment-reference-e79a8bf)|e79a8bf；原生安装与启动修复/UAC 增量，Sportmaster portable 及三个 win-x64 组合|
|[全部入口 reference](https://github.com/yuan2go/importer-pc/releases/tag/runtime-all-entrypoints-reference-16ecb57)|16ecb57；Supply/Inspection portable 入口守卫，保持形成时共享账户政策|

上表是历史形成时的参考资产。[后继统一发行](docs/runtime/windows-no-test-release-20261020.md)及本包结果各自记录精确源码；旧介质和原 PASS 不覆盖后继候选。旧“本次文档未构建/未发布”说明原字节见[清理前归档](docs/archive/pre-sportmaster-commercial-delivery-01/README.md)。

## 证据与公开边界

[UI 实物及完整交接](docs/ui/ss-ui-vis-001-v1.1/README.md)包含原 HTML/CSS/JS、PPTX 与同源导出 PNG；PNG 是设计导出，不能代替 WPF 运行截图。[UI 状态](docs/ui/STATUS.md)及各 Windows 后验单独记录。

Windows 原生交互、Dispatcher/HWND、DPI/触控、UAC/ACL、系统安装维护及真实组合验证仍须按固定介质执行；未做部分保持 NOT_RUN。真实 SDK、RCS/WCS/PLC 合同、设备和现场资格按能力保留 BLOCKED/NotReady，Production 缺必需能力拒绝。历史 D/RV 和原失败不因局部参考 PASS 自动关闭。

仓库为 public，[公开范围](docs/baseline/PUBLICATION.md)持续有效；客户全量资料仍 PUBLIC_SUBSET_ONLY。仅提交获授权通用代码、文档、衍生 WPF UI 与合成证据，原客户资料、凭据、生产数据、未许可 SDK/字体不进入公开交付。修改前当前入口的原字节见[商业化整理前归档](docs/archive/pre-commercial-productization-01/README.md)。
