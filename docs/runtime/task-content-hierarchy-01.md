# 页面任务优先级调整与原生验证

2026-10-24，基线main `c2300cdb73bda9192e2e7912b4fee12753b81121`，实现提交 `8bf2a9d00144d5a4cdc9432d1d09851dc086815d`。工作包：[TASK-CONTENT-HIERARCHY-01](../work-packages/WP-IPC-TASK-CONTENT-HIERARCHY-01.md)。后续提交仅补证据与说明，不把历史介质或D/RV改标为本轮通过。

## 结果及实现路径

经典窗口原五行布局改为身份导航、必要状态、页面主体、底部区域四行。`ShellWindow.xaml`的普通状态不再常驻占用最高280 DIP；需要处置的失效、暂停、阻断和原请求Unknown/Conflict仍跨页显示，长原因在48 DIP内滚动读全。当前页反馈独立限高36 DIP，抑制明确的重复读成功信息；原文留在管理诊断中。诊断移到底部按需展开，操作员登录只随工作台出现，原作业命令只留在工作台且忙碌时仍保留位置、禁用按钮。

`TaskContextPresentation.cs`与`ShellWindow.xaml.cs`负责显示投影及当前页反馈范围；Application仍拥有所有命令准入。`SupplyStationWindow.xaml`及其ViewModel压低重复品牌/标题/状态的层级，修复日志页原先一并隐藏业务异常的问题。两套窗口继续共用原应用实例、候选和守卫。

`Workstation.xaml`将本件条码、目标格口放在首位，设备身份退到小标题；每位内容可滚动，选择原命令目标的按钮固定可达。`Workstation.xaml.cs`按投递位ID保留视图条目，避免轮询重建控件而丢失滚动和展开状态。默认供包布局保留既有相机、扫描、任务三组字段。

`HistoryPage.xaml`、`QueryWorkspaceTheme.xaml`、`SoftwareLogPage.cs`及`ParameterSettingsControl.cs`降低重复标题/介绍权重，保留查询、结果、字段与原统一保存栏。不新增卡片或业务操作步骤；无依赖、配置schema、安装机制及权限合同变更。

## 可核验的前后对比

[全部截图及逐页对比](evidence/task-content-hierarchy-01/README.md)，[文件哈希](evidence/task-content-hierarchy-01/FILES.json)，[机器可读结果](evidence/task-content-hierarchy-01/RESULT.json)。前后均实际显示WPF窗口，使用同一合成场景与采集夹具。before编译的是基线生产源码，after编译的是本轮实现；before采用基线模式，不对旧布局强套新布局断言。PNG由已显示的HWND/Dispatcher内WPF VisualBrush/RenderTargetBitmap采集，不是浏览器原型或静态XAML推算，也不是整个物理桌面的截屏。

下表为800×420 DIP客户区的实际PageHost高度；1366×768、1092×614数据完整保存在两份WINDOWS-WPF.log。

| 页面 | 经典调整前 → 后 DIP | 默认供包调整前 → 后 DIP |
| --- | --- | --- |
| 工作台 | 146.4 → 208.8 | 308.8 → 316.0 |
| 作业记录 | 146.4 → 275.2 | 291.2 → 316.0 |
| 参数设置 | 194.4 → 275.2 | 336.8 → 336.8 |
| 日志查询 | 190.4 → 275.2 | 367.2 → 367.2 |

800×420仍是最低可操作尺寸，字段/结果需要滚动；不能据此宣称该尺寸的长时间作业舒适性。截图检查覆盖两套窗口、四页、三尺寸，以及登录、失效、长阻断、Unknown、Rejected和诊断展开。日志对比使用固定12条合成查询结果，实际查询合同由独立internal-pages回归覆盖；合成异常投影不代表真实设备产生或现场通过。

## 验证

本机Windows 11 x64（10.0.22631）、.NET 10.0.13、实际125% DPI：

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 新task-hierarchy原生专项 | 85项PASS | [日志](evidence/task-content-hierarchy-01/after/WINDOWS-WPF.log) |
| 既有parameter-shell-polish | 30项PASS | [日志](evidence/task-content-hierarchy-01/regression/parameter-shell-polish.log) |
| 既有internal-pages | 25项PASS | [日志](evidence/task-content-hierarchy-01/regression/internal-pages.log) |
| Release验证工程构建 | 0警告、0错误 | [构建](evidence/task-content-hierarchy-01/regression/build.log) |
| Sportmaster win-x64 framework-dependent publish | PASS，仅本地产物 | [发布构建](evidence/task-content-hierarchy-01/regression/publish.log) |
| 发布exe的--environment-report入口 | PASS | [启动入口](evidence/task-content-hierarchy-01/regression/startup-entry.log) |

专项验证包括页间作业身份不变、焦点/选区/原生键盘遍历、刷新后滚动/展开不变、800宽保存可达、忙碌恢复行位置稳定、失效禁用、完整长原因可达、未知/拒绝不沿用旧格口、日志页未知提示、撤权后保留草稿与独立操作员、直接保存拒绝、窗口关闭。既有回归继续覆盖管理模式真实本地会话过期、暂停恢复、候选字节不变、参数字段与单位、日志取消/失败/冻结分页/明细。

迭代中发现并修复了经典位内容截断、刷新重建导致滚动丢失，以及独立复核指出的忙碌恢复行消失。夹具先后修正导航等待、实体回执匹配和长阻断等待条件。最终回归曾因登录回执尚未进入快照而空引用；已补有界回执等待及非空断言，未删减原断言，重跑通过。归档日志是最终成功运行，不能据此否认前述迭代失败。

物理键鼠人工验收、Windows 100%/150%等其他OS缩放、目标工控机、完整安装/首次启动旅程、真实SDK/RCS和现场验收均NOT_RUN。本轮发布入口只验证环境报告分支，不等同完整生产启动。并行PR94的TCP提供者修复尚未合入基线，本包未重复实现，也不声明其首配字段问题已通过。未合并PR、未安装、未制作正式发布介质。

## 复现与运行

在Windows仓库根目录执行以下命令；隔离配置、运行数据、临时目录均由项目环境及夹具放在D盘。夹具输出目录见`.artifacts/WINDOWS-WPF-EVIDENCE.txt`。不得把合成身份/端口当作生产默认。

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification -c Release
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --task-hierarchy
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --parameter-shell-polish
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --internal-pages
dotnet publish src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.win-x64.lock.json -r win-x64 --self-contained false -o .artifacts/task-hierarchy-desktop
& ./.artifacts/task-hierarchy-desktop/Ipc.Desktop.exe --environment-report
```

重采before应在独立隔离检出中使用上述基线的生产源码，带入本轮`TaskHierarchy.cs`与对应Program路由，运行`--task-hierarchy --baseline`。不要在有他人修改的工作目录中替换源码。基线与实现身份、源文件及证据SHA-256见manifest；截图中的随机合成作业ID允许每次不同。
