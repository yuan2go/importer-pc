# WP-IPC-UX-COMMERCIAL-QUALITY-AUDIT-01 · 原报告发布与下一阶段交接

[审查报告 PDF](WP-IPC-UX-COMMERCIAL-QUALITY-AUDIT-01.pdf) · [下一阶段 Codex 提示词](Codex-next-phase.md)

## 原报告身份与本次发布

用户于 2026-10-19 明确要求将已交付审查 PDF 上传本仓库 main，并结合最新代码、文档输出下一步执行提示词。本目录按该授权发布原报告及必要交接；原审查的只读范围与形成时结论保持。

|项目|身份与边界|
|---|---|
|原审查 main|`8f886f44bec01779318ec078a569500ab1beab56`；Business 1.2.8、Application.Contracts 1.8.0、Parcel DB7|
|原审查时间|2026-10-19 12:41:38 +08:00 开始隔离克隆；截至 05:13:00 UTC 的远端复核仍为同一 SHA|
|PDF 原件|24 页，276445 字节；SHA-256 `76702aec0ee5b46cf7c03df32ec4e9ed66f950055b4399fe86484dff31a056a9`|
|原验证范围|Linux 隔离构建、Runtime/SQLite/ViewModel 定向运行、Sportmaster WPF 交叉构建、实际远端发行 ZIP 身份核验；Windows 原生、目标硬件性能及真实设备/现场未验|
|发布准备复核|2026-10-19 05:36:46 UTC 重新获取远端 main，仍为原审查 SHA；存在开放 PR #37，按下文独立标识|
|本次修改|新增原 PDF、交接与提示词，更新入口及文件清单；无业务源码、产品配置、既有测试或设计原件修改|

PDF 保持原字节，不把 PR37 的后续整改写回历史结论。本次未上传原证据 ZIP；PDF 中的隔离本地路径是原审查证据定位，不能解释为仓库已包含对应文件。下一阶段可复用仓库内已有合成反例及可获得的原证据，缺少的运行证据应明确记录并按风险补验。未公开客户原 HTML、PRD、SDK、字体、凭据或生产数据。

## 最新 main、开放 PR 与产物必须分别读取

发布准备时 [PR #37](https://github.com/yuan2go/importer-pc/pull/37) 为 OPEN，读取 head 为 `2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94`，基于上述 main；本次报告发布不合并它。分支已有实质源码整改，不能继续把 main 的每个问题当作待从零实现。执行下一阶段时须重新核对 main 和 PR 的最新状态。

PR37 的[工作包](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/docs/work-packages/WP-IPC-UX-COMMERCIAL-FIX-01.md)使用 UXC-01～07、G-01/02；本报告使用 UX-01～11、G-01～04、WP-01～07，必须按具体行为映射，不能按数字等同。其[结果及证据入口](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/docs/runtime/ux-commercial-fix-01-results.md)固定产品源码 `c384c3bc4750bbaafbcc3b834d3b12aa126c3b41`，业务 1.3.0、Contracts 1.9.0、DB7。产品冻结点至读取 head 的生产源码、配置和构建脚本没有再次改变；后续包含文档、证据及 RCS 测试夹具变化。

[ARTIFACTS.json](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/docs/runtime/evidence/ux-commercial-fix-01/ARTIFACTS.json#L63-L66)明确声明候选只在本地，未远端发布。此次未取得其新 ZIP 字节；原报告验证过的 1.2.8 远端发行不能证明 1.3.0 候选。PR 的开发日志属于作者固定范围的参考验证，此次仅核代码差异和证据身份，没有独立重跑其测试，也没有 Windows 运行截图。

## 实际代码覆盖与下一阶段差额

|本报告范围|PR37 实际代码覆盖|下一阶段处理|
|---|---|---|
|UX-01 / WP-02 最新观察|[独立输入契约](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.Contracts/ParcelInputs.cs)、[持久投影](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow/ParcelInputs.cs)及两布局区分新观察与原作业；非法输入不创建新作业。|继承并核对原反例、不盲重发及双位独立；不能仅凭新字段宣称 Windows 已验。|
|UX-02/03/04 / WP-03 配置与页面|结构化 schema3、编辑来源、草稿保留、CAS、初始页加载已接入。[维护 XAML](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow.Desktop/MaintenancePage.xaml)仍有下述实际控件差额。|保留新表单和单一配置账，补真实控件读取→编辑→校验→Pending→Effective 闭环。|
|UX-05 / WP-03 等待反馈|[RunCoreAsync](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow.Desktop/ParcelViewModel.cs#L147-L154)仍仅设置 Busy；桌面 XAML 无 Busy 绑定，普通慢动作开始时没有统一的即时反馈。|补可见处理中、合理禁用及原因、结果与失败反馈；保留独立 Pause，不把该缺口说成已发生重复外呼。|
|UX-06/07/08 / WP-05 状态与启动|[共享展示](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow.Desktop/ParcelPresentation.cs)已有语义样式、健康证据说明；[启动阶段](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Desktop/ParcelApp.xaml.cs)已有实质实现。|继承并在真实状态下核可见性。原 UX-08 为本报告改进建议，不能倒写成此前已批准必交缺陷。|
|UX-09/10/11 / WP-05 后台与窗口|[服务调用所有权](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow.Desktop/DesktopServiceCalls.cs)、classic 排空及双布局重排已实现。|验证慢读取选择交错、紧凑状态可读性和实际关闭；不把 Task.Run 或交叉构建当作目标机性能证明。|
|G-01/02/04 / WP-01/04 真实组合|真实 SDK/驱动和商业组合输入未补齐；[RCS QueryAsync](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs#L61-L65)仍显式返回 null。|按首个选定 Application 的正式合同完成接入及权威 Unknown 处置；缺输入仅阻断相关能力，不编造接口或人工成功。|
|G-03 / WP-06 实际交付与验收|有新候选身份和参考旅程，Windows 原生仍 NOT_RUN，候选未远端发布。|取得或按同一冻结源码构建可核验媒介，完成真实安装、交互、更新恢复和目标条件测量。|
|WP-07 可选易用性增量|历史加载/分页/结构详情、安装精确包选择已有覆盖；过滤检索及安装操作枚举不能据此算已全部完成。|按实际已采纳维护任务补差额；新增建议单列，不自动升格为商业阻断。|

### 发布阶段进一步发现的具体核验点

1. **历史选中项与命令目标的异步交错，源码风险，未运行复现。** [RefreshAsync:114～131](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow.Desktop/ParcelViewModel.cs#L114-L131)在 await 前捕获 A，用户期间选择 B 后，仍可能把 Selected 写回 A；SelectedHistory 可继续为 B。[历史加载:47～56](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow.Desktop/ParcelTargets.cs#L47-L56)也按读取前选择恢复。[核对/继续:71～84](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow.Desktop/ParcelTargets.cs#L71-L84)读取 Selected.Id 作为原作业目标。定向覆盖“慢快照/慢历史加载中 A→B”，使选中行、详情及命令 Owner 一致；现有 Application CAS/原请求守卫仍有效，不能据源码推断已发生错单或误投。
2. **结构化表格的可编辑接线，源码确认，原生待验。** [全局 DataGrid 样式:21](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Desktop/Themes/Workstation.xaml#L21)仍设 IsReadOnly=True，[维护页:12～17、43～50](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow.Desktop/MaintenancePage.xaml#L12-L50)未覆盖，影响应可编辑的文本/复选框列。模板内 ComboBox/TextBox 另行核对，不能说全部表单不可用；稳定设备 ID 的明确只读保护继续保留。VM 直接赋值不是原生编辑证据。
3. **紧凑状态和原生验证器不同步，分别属于可读性风险与源码确认。** 800×420 DIP 是既有框架重排目标，1280×680 是旧候选阈值，PR37 确有重排；不能只凭数值降低判为降标。但 [原生验证器:97～102](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/tests/Ipc.Desktop.UiVerification/Program.cs#L97-L102)仍将 1100×650、911×512 窗口一律断言为保护，与[当前客户区判断](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow.Desktop/ParcelPresentation.cs#L74-L85)不同步。按实际客户区修旧假设，保留低于最低区暂停及恢复不自动 Resume 的断言。真实窗口补验应包括“长码未决原作业＋新输入拒收”，核 [classic 20 DIP 状态区](https://github.com/yuan2go/importer-pc/blob/2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94/src/Ipc.Application.ParcelFlow.Desktop/ShellWindow.xaml#L6)和 compact 原件摘要能否完整传达关键状态；此次未复现实际裁切。

## 执行和验证口径

先清理本包相关过期现行入口、候选版本与后验说明，继承 PR37 已正确整理的内容；原始要求、已批准设计、REQ/BR/CAPI/D/RV 编号及历史证据保留，不靠改弱设计关闭差异。文档先提交，再开始实际代码。三个主业务流程各有独立 Application，Sportmaster 仍为相机→WCS→格口，两个布局共享业务实例；DirectPlc 完整 UI 暂缓。

下一阶段复用已有定向回归和可信证据，仅补上述具体交错、原生控件/状态及受影响主链、恢复、防重、配置 CAS 和资源验证，执行必要构建及同一候选的发行冒烟。没有 Windows、SDK、正式协议或目标硬件时，完成其余工程工作并交付准确的待验步骤和缺口，不把参考结果写成商用通过。本次文档发布只做新增文件身份、PDF 可解析性、链接、增量清单和 Git 差异检查，不宣称全库资料校验或业务测试 PASS。

Git 自动将该 ASCII 封装 PDF 当作文本并报告格式空白；PDF 保留原字节，按解析及完整 hash 核验。文本空白检查仅适用于本次 Markdown、JSON 和摘要文件，不修改 PDF 的格式空白来获得文本检查通过。
