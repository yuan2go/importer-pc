# importer-pc

C#/.NET 10 Windows 工位客户端。Platform 提供机制，Application 拥有业务状态与命令守卫，驱动与集成实现端口；一个业务实例服务多个页面和窗口。

当前阅读入口是[文档索引](docs/INDEX.md)。2026-10-26 并行任务整合的范围、顺序、不变式和验证状态见[整合工作包](docs/work-packages/WP-IPC-INTEGRATION-20261026.md)。源码合并不代表已发布新安装包或取得现场资格。

| 任务 | 入口 |
|---|---|
| 开发、构建与运行 | [运行入门](docs/runtime/getting-started.md)、[D盘环境](docs/runtime/d-drive-storage-20261021.md) |
| 管理模式与独立操作员 | [管理模式](docs/runtime/management-mode-01.md) |
| 首配、参数与延后连接 | [参数用途与启动](docs/work-packages/WP-IPC-PARAMETER-GUIDANCE-STARTUP-01.md)、[独立延后修复](docs/runtime/validation/deferred-heartbeat-01.md) |
| 安装、卸载与生命周期 | [当前操作指南](docs/runtime/lifecycle-ux-current-guidance-01.md)、[七包实现](docs/runtime/lifecycle-ux-implementation-01.md) |
| 历史维护与恢复 | [维护运行](docs/runtime/retention-ingress-02.md)、[完整维护证据](docs/runtime/validation/retention-ingress-full-retest-03.md) |
| 架构与扩展 | [框架](docs/design/framework.md)、[决策](docs/design/framework-decisions.md)、[扩展开发](docs/extensions/developer-guide.md) |
| 界面基线 | [参数V3](docs/ui/parameter-settings-v3-20261020/README.md)、[作业与首配V2](docs/ui/sportmaster-task-redesign-v2-20261020/README.md)、[字体规范](docs/design/typography-01.md) |

Sportmaster、DirectPlc、WcsToPlc 是独立主流程。Sportmaster 保持左右读码、每次已准入扫码一次原条码 RCS 请求，无 PLC 下发或新增包裹预览。配置使用唯一 CAS/Pending/Effective 账，保存后受控重启，重连只使用 Effective。待配置能力拒绝业务，Unknown 保留原请求与原责任，不盲重发。软件暂停不是安全急停。

Windows 开发命令先运行 `. ./scripts/project-environment.ps1`，项目、缓存与运行数据使用 D 盘。真实 SDK、RCS 合同、目标工控机、24h/72h 和现场验收分别计证。

历史发布与设计见[历史入口原件](docs/archive/pre-integration-20261026/README.md)及[文档索引](docs/INDEX.md)。旧安装包、原型、截图、PASS/FAIL 各守原 SourceCommit，不能当作当前 main 的验证。遵守[公开范围](docs/baseline/PUBLICATION.md)，不提交受限原稿、生产数据、凭据或 SDK。
