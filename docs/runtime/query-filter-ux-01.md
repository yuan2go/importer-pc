> PR129 同步 main `fdd24fa5` 后的版本核对、原生复测及先前 CI 修正见[同步记录](evidence/query-filter-ux-01/main-sync.md)。开发阶段自有模块保持 1.0.0。

# 作业记录与日志查询筛选体验

2026-10-27，QUERY-FILTER-UX-01 接续 OPERATOR-WORKSTATION-01 / PR129。用户指出筛选条件混乱、机器格式时间难懂、日期框形态异常；本次调整两个真实 WPF 查询页，保留原查询、权限、取消和导出合同。

## 当前行为

- 常用条件集中在首行。起止时间、快捷范围单独成组，其他条件按需展开。作业记录提供今天、最近 24 小时、最近 7 天、不限时间；日志保留必须指定时间范围的限制。
- 默认输入和摘要使用本机日期时间。起止标签改为“开始时间 / 结束时间”，一行说明保留开始包含、结束不包含的含义。日期框与时间框均为 44 DIP，明确的“日历”按钮打开原生日期选择器。
- 每个时间框的“更多”提供清空与精确输入。默认不显示 UTC 偏移；需要时仍可读取和输入包含时区、小数秒的完整值。未编辑的精确值不会被本机显示改写。新的本机时间按所选日期解析时区；夏令时不存在或重复的时刻要求明确输入时区。
- 筛选条件使用一个滚动区；常用窗口高度显示完整分组，小窗口仍保留查询动作、表头和完整结果行。日志高级筛选取消嵌套滚动。作业记录的技术查询序号和指标说明移入更多信息，普通摘要只列实际条件。
- 日志的待查询/已应用范围与记录覆盖时间使用可读本机时间。日志原文、导出内容、原始请求和服务返回的时间值不被改写。

## 实现和守卫

`DateTimeInput.cs` 与 `QueryWorkspaceTheme.xaml` 实现共享输入；`QueryTime.cs` 是无 WPF 依赖的时间呈现帮助类。`HistoryPage`、`HistoryEditing`、`SoftwareLogPage` 使用同一控件和格式。无新依赖、无系统时区更改。

非法日期或时间继续拒绝查询；不能因修改时间框而恢复先前有效日期，不能扩大原查询。修改条件会撤销旧导出资格，已应用摘要保持原选择；取消、管理权限失效和原查询选择释放继续沿用 Application 守卫。

同时修复 PR129 原生和无窗口测试的集成遗漏：无窗口项目显式纳入新的呈现 partial 与工作区容器 shim；诊断导航测试按当前信息分层定位，继续核对控件复用、焦点、草稿、原作业身份和无重复业务调用，不删除这些合同断言。

## 验证与运行

本包 Windows 原生、应用合同、构建与发布结果见 [限定验证记录](evidence/query-filter-ux-01/README.md)。历史证据保持各自源码身份，不将旧 PASS 改写为本次结果。

在 D 盘工作区执行：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -c Release -m:1 -p:RestoreLockedMode=true -p:UseSharedCompilation=false
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll $PWD --query-filters
```

同源发布验证入口增加 `scripts/verify-commercial-wpf.ps1 -QueryFilters`，Windows CI 纳入同一测试。此次使用合成数据；现场供包员/维护员可用性观察、真实相机/RCS、物理触摸、安装替换、跨显示器 DPI 和现场验收均未执行。
