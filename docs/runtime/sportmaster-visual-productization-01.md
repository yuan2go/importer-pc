> 2026-10-27 查询布局与本地日期时间呈现以 [QUERY-FILTER-UX-01](../work-packages/WP-IPC-QUERY-FILTER-UX-01.md) 为准；旧默认 UTC 输入和长时间摘要保留历史范围，查询合同不变。

# Sportmaster 原生视觉产品化

> main 续核：PR58 的整套视觉实现及 PR57 的发行整合已合入 main `e0beb5d32c6a24a7799ba698731566bca6a6833a`。本轮沿[续核设计](../design/sportmaster-visual-main-closure-01.md)修复共享日历的无效日期恢复路径：错误保持到明确更正/清除，关闭日历不再使用旧日期，记录与支持共用此规则。最终候选的源码与库存以其 `app/application-release.json` / `SHA256SUMS` 为准；下文 dfc2ab6 是先前已验证的历史介质，不代表后继源码身份。续核结果单独记账，目标 Windows 和现场资格仍须独立验收。

任务 WP-IPC-SPORTMASTER-VISUAL-PRODUCTIZATION-01；开工main `f3059d499cd4dbc60b443f0a54cac939b2e5db2f`。[编码前基准](../design/sportmaster-visual-productization-01.md)、[清理前原字节](../archive/pre-sportmaster-visual-01/README.md)、[本轮证据](evidence/sportmaster-visual-01/README.md)分别管理目标、来源与实测。业务1.4.0 / Application.Contracts1.10.0 / Parcel DB8不变，无新生产NuGet、浏览器、字体或设备依赖。

## 实际使用

沿原安装机制，完整win-x64介质解压后运行 `Setup.cmd`，安装后指定Windows账户非提权从开始菜单或 `desktop/Ipc.Desktop.exe` 启动。首次工位配置按目录与工位、设备与服务、运行策略、检查与启动四个步骤，另有恢复维护页；可保存未完成草稿。保存Pending和显式启动仍受原身份、原责任、精确能力与Production资格守卫，安装和配置检查不启动设备。

Sportmaster省略UI参数进入新版supply-station；`--ui classic`显式兼容。页面参数 `--view sportmaster-history|sportmaster-maintenance`、布局 `--layout standard|compact`继续可用；显式 `--ui supply-station`继续有效。高级 `application.ps1`按介质Application选择缺省，显式-Ui继续保留；无参application.cmd、安装和快捷方式沿desktop缺省，无第三套界面。其他Application缺省不变，仍拒绝Sportmaster专属UI。

工作台按左/右位、本次条码、有效格口、结果与下一步排列。短格口突出，长格口分级字号且完整可选/滚动。正常为中性界面和安静的有效结果；拒绝、Unknown、失效有独立文字/语义。新失败观察不借用旧格口，原责任仍可追溯，格口取得不表示实体完成。

顶部页面有选中态；展示设置收纳标准/紧凑、辅助窗和只读来源诊断。启用操作员登录的组合在该菜单仍可认证/登出，维护授权独立。工程标识、原请求、原始技术原因按需展开；紧凑时详情在局部滚动区，固定动作不随详情滚走。软件暂停不是安全急停。

记录使用原生日期选择、时间和显式UTC偏移，支持今天、最近24小时和不限范围；开始含、结束不含。高级精确时间原文仍可编辑；非法日期/时间保留为无效条件，不能悄悄使用旧时间。列表时间与UTC偏移分行避免截断；状态显示中文但筛选仍保留原契约值。原件JSON完整可选可复制，冻结查询/分页/导出语义不变。

维护把认证、保存、草稿/重读/重连分层，保持原CAS和唯一配置账；原生表格和参数表单保留选择。首配的模板、备份、恢复和支持放入低频区域；全部技术诊断可展开选择复制。帮助与支持使用同一时间输入及原有本地摘要服务。

## 连续操作与实现边界

`SupplyStationWindow`复用现有页对象和Application；子窗仍共享设备/发送器生命周期。位置字段只发布变化，不再对500ms轮询发出全字段通知；最近列表按显示事实保留，命令目标始终从当前快照的Position.CurrentOperation精确匹配。历史原件/配置全文只在文本变化时通知；切回记录不自动重查覆盖当前页，显式查询仍可刷新。维护参数表单在同一选择的切页/加载中保留原控件。

standard纵向条码/大格口，compact横向字段；小客户区使用更紧凑比例，工程来源通过展示设置打开。800×420 DIP以下原保护不变。真实连接、生产准入、原请求合同/目标/报文和恢复语义未放宽，没有PLC下发、相机预览、图库或DirectPlc UI。

项目skills固定Impeccable4.7.0上游46b822f（完整SHA与逐文件hash见UPSTREAM.json）并保留ui-ux-pro-max。实际使用Operate critique、layout、clarify、polish，独立WPF查询和错误恢复/表单UX查询；网页检测/CDP不用于WPF验收，技能不进入客户端闭包。`--design-system`忽略stack，未将其通用输出当成WPF定稿。

## 运行和验证入口

开发构建：`dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:Application=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -m:1 -nr:false`。正常运行仍需已安装机器政策和指定账户，不用调试参数绕过。

合成原生检查：`dotnet run --project tests/Ipc.Desktop.UiVerification -p:Application=Sportmaster -- . --visual-productization`；查看首配时设置IPC_FROZEN_DESKTOP为同版Sportmaster桌面DLL。该环境变量只供测试夹具定位DLL，客户端不读取它。最终发布DLL验证复用[Windows清单](validation/sportmaster-alternate-ui-windows.md)中的verify-commercial-wpf入口。

冻结打包：先提交运行源码/配置/随包指南，再执行 `python scripts/package-application.py --application sportmaster --target win-x64 --output <全新目录> --dotnet <10.0.402路径>`。本轮本地介质和实际检查的精确源码/库存/结果统一写在证据入口；不覆盖或重标旧Release。

## 验证资格

本轮已在Windows 125%下检查实际WPF/Dispatcher，包括代表尺寸与800×420客户区、左右延迟/丢回执及原请求核对、共享实例、焦点/选择/展开/滚动/草稿、日期时区、历史/维护/首配/帮助。最终同版介质结果以证据中的机器记录为准；开发中失败保留，不能把旧截图或构建通过当作本轮原生体验。

原 `--sportmaster-ui` 的解析断言通过后，在本机因缺少维护安装的机器政策拒绝Runtime.Start，记录为环境门禁阻断，没有修改生产检查或把该整套测试改报PASS。显式合成身份/参考端口的原生夹具另行覆盖本轮风险。继承55025ce验证只适用于原SHA与范围。

仍需现场/目标机补验：100/150%及混合DPI多屏、物理触控/手套/HID/读屏/声音/视距、长时负载、提权安装/UAC/第二账户及真实SDK/WCS/实体/现场。真实能力与生产资格仍BLOCKED/NOT_READY；本轮不宣布商业验收通过，也不关闭旧HTML的D/RV。

整合增量：实施期间PR56合入main `1e97740f73fa1714505a128283f3df5bb96a5518`。最终组合完整继承其首配目录→设备→策略→检查启动四步及独立恢复页、未保存确认、检查失效/保存启动守卫、可搜索离线帮助、草稿和工程门禁修正与全部历史证据；本轮仅在其首配上补视觉分组/可复制诊断，未保留一套并行首配。原计划的三步组织由此替代，最终验证已重冻新组合，没有拼接旧PASS。PR57仍为独立发行任务。

## 原轮冻结交付身份与实测（dfc2ab6）

运行源码和同版原生夹具固定为 `dfc2ab68d263340037751651c442dd5b33023b32`；该提交相对9247db6仅修正维护展开区的验证定位，客户端代码相同。最终文档/证据及验证脚本的预览输出目录修正另行提交，不重标介质SourceCommit。

本地介质 `.artifacts/sportmaster-visual-dfc2ab6-win-x64.zip`，SHA256 `f599db06205f0f1f782593654611839532c99ee27d6ee88dbb38393c63082ed9`。746项库存无变化、ZIP CRC通过；库存SHA、大小、版本及逐DLL身份见[MEDIA.json](evidence/sportmaster-visual-01/MEDIA.json)。本轮不覆盖旧Release。

冻结原生入口（新输出目录）：

```powershell
./scripts/verify-commercial-wpf.ps1 -Media .artifacts/sportmaster-visual-dfc2ab6-win-x64 -Output .artifacts/new-commercial-pages -Dotnet D:/tools/dotnet/dotnet.exe
./scripts/verify-commercial-wpf.ps1 -Media .artifacts/sportmaster-visual-dfc2ab6-win-x64 -Output .artifacts/new-visual-pages -Dotnet D:/tools/dotnet/dotnet.exe -VisualProductization -Interactive
```

验证机为Windows 11家庭中文版22631 x64，SDK10.0.402、WPF DPI1.25，低于安装目标26100+；不以本机界面测试授予目标安装资格。首配/维护/帮助与视觉工作台是分别运行的同版检查，日志和结果在证据final/；真实可见HWND输入与RenderTargetBitmap布局图分别标记。原生实测覆盖左右延迟/回执丢失/原请求核对、Unknown/拒绝/失效、长内容、800×420实际客户区、连续快照焦点/选择/滚动/展开/历史选择/草稿、显式时区和共享窗口资源。

相关构建和生命周期回归通过；合并后发行文档2项、商业交付脚本5项回归通过。全树归档检查仍为FAIL：保留原稿PPTX的265项媒体类型不符及2项原元数据标记，规范Git字节导出未发现新增内容身份/manifest checksum错误；详见[检查摘要](evidence/sportmaster-visual-01/archive-audit-summary.json)。未删除原稿、压制检查或改写历史结果。
