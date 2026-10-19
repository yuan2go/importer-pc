# importer-pc

C#/.NET 10工位运行基座及独立Application参考工程。Platform负责机制，Application拥有状态和守卫，Driver适配设备，Component适配外部服务。一个Application运行实例可服务多个页面和布局。

## 当前运行与组合

业务版本1.2.6、Application.Contracts1.8.0、Parcel DB7（SQL1–6原样保留；SQL7为数字请求持久归属与范围退役）、配置schema1/2/3；Supply另用Storage5和原操作契约。三个主流程保持独立：Sportmaster左右两个独立reader/投递位，扫码取得WCS格口，不下PLC；DirectPlc与WcsToPlc各单reader/顺序位，分别PLC与WCS→PLC。登录/称重/采图/上传按已确认策略选装，DirectPlc完整UI继续延期。

Sportmaster支持 `--ui classic`（默认）和 `--ui supply-station`，导航与辅助窗共享Application、连接和原作业。RCS组件 `sportmaster.rcs-task` 1.0.6只实现 `sorter.task.dest_request`；正常、NEED_UNPACK、拒绝和Unknown分别处理，不自动重发、不提供未确认的拆包提交。图片展示与长期图库不在范围。

[并发整合报告](docs/runtime/parallel-task-reconciliation-01.md)统一PR27/29/30/32、PR31恢复基线、冲突裁决及最终组合验证；历史候选、原失败与形成时结论保持原身份。新精确参考扩展为能力包2.1.4与reader1.2.4，按实际目标公共DLL构建，版本号本身不构成加载/Production资格。

## 开发、发布与维护

本轮工程入口为[审查03交付工作包](docs/work-packages/WP-IPC-COMMERCIAL-AUDIT-03-DELIVERY.md)，普通升级/窄恢复以现行维护正文为准。

先读[统一入口](docs/INDEX.md)、[工作约束](AGENTS.md)、[首次运行](docs/runtime/getting-started.md)、[配置/恢复/扩展维护](docs/runtime/architecture-quality-closure-01.md)、[版本与组合证据](docs/runtime/version-compatibility-01.md)及[扩展开发](docs/extensions/developer-guide.md)。

```sh
# global.json固定SDK 10.0.402，需Python3；按受影响范围验证
./scripts/build.sh
# 最终媒介/精确源码及运行回执见整合报告
python3 scripts/package-application.py --application sportmaster --target portable --output /new/immutable/output
```

旧DB2–5普通升级先只读核未结责任，不能通过升级消除Unknown或原件责任。窄维护CLI `recover-legacy-receipt` 要求原DB/WAL SHA、全备份、原目标Query和永久身份/原件依据，显式选择当前DB7；内部API旧调用默认DB5兼容。配置沿同一CAS/Pending/Effective账，资源成功后确认；停止失败保持资源与包租约。软件暂停不是安全急停。

## 证据与边界

[UI实物与完整交接](docs/ui/ss-ui-vis-001-v1.1/README.md)保留原HTML/CSS/JS、PPTX及34张同源导出PNG；这些不是Windows截图。[UI当前状态](docs/ui/STATUS.md)与[Windows后验](docs/runtime/validation/sportmaster-alternate-ui-windows.md)区分源码/跨构建/事件替身和实机。

Windows真实交互、Dispatcher/HWND、DPI/触控、UAC/ACL及维护流程NOT_RUN；真实SDK、RCS/WCS/PLC合同、设备/实体/权限与现场BLOCKED，Production缺能力拒绝。审查01原PDF仍缺失；[审查02原PDF与复核](docs/reviews/architecture-quality-audit-02/README.md)、[PR24](docs/reviews/pr24-independent-rereview-f862c2a/README.md)、[PR31](docs/reviews/pr31-independent-rereview-f068156/README.md)及历史FAIL不改写。

仓库public，[公开范围](docs/baseline/PUBLICATION.md)持续有效。只提交获授权通用代码、WPF衍生UI和合成证据；客户原HTML/PRD、私密链接、凭据、生产数据、SDK/字体保留本地。全量资料PUBLIC_SUBSET_ONLY，D批准及历史RV不因本轮参考PASS关闭。此前入口原字节见[归档](docs/archive/pre-parallel-task-reconciliation-01/README.md)。
