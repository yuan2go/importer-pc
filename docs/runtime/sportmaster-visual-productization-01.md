# Sportmaster 原生视觉产品化

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

整合增量：实施期间PR56合入main `1e97740f73fa1714505a128283f3df5bb96a5518`。最终组合完整继承其首配目录→设备→策略→检查启动四步及独立恢复页、未保存确认、检查失效/保存启动守卫、可搜索离线帮助、草稿和工程门禁修正与全部历史证据；本轮仅在其首配上补视觉分组/可复制诊断，未保留一套并行首配。原计划的三步组织由此替代，最终验证必须重冻新组合，不能拼接旧PASS。PR57仍为独立发行任务。
