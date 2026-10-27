# QUERY-FILTER-UX-01 限定验证

2026-10-27，main `d0a06313` 基础上接续 PR129。Windows DPI 125%，真实 HWND/Dispatcher、Application 和 SQLite，合成记录及设备端口。前七项为原生 WPF，后三项为应用/无窗口合同检查；不能合并称为现场测试。`parameter-v3` 包含参数重构、作业重设计和 V3 全链。

| 模式 | PASS 行 | FAIL 行 |
|---|---:|---:|
| query-filters | 26 | 0 |
| internal-pages | 40 | 0 |
| parameter-v3 | 239 | 0 |
| visual-productization | 117 | 0 |
| reliability-logs | 2 | 0 |
| debug-log-live | 40 | 0 |
| management-mode | 57 | 0 |
| history-session-contract | 1 | 0 |
| ux-commercial-presentation | 51 | 0 |
| stability-audit-history | 41 | 0 |

源码 canonical Git blob、原始日志与截图 SHA256 见 [verification.json](verification.json)。截图来自本轮实际运行窗口，位于本地 `.artifacts/query-filters/review`；未将用户附件加入 Git。`build`、`final-harness-build`、`headless-build`、`publish` 收据保留原输出；框架依赖发布的环境报告入口退出 0，发布 UI 程序集与测试程序使用的程序集 SHA256 相同。

原生查询专项覆盖本机显示、非本机偏移与小数秒不丢失、44 DIP 高度、实际日历弹出、按需精确输入、非法日期/时分秒不沿用旧日期、夏令时歧义拒绝、实际查询端点、修改条件撤销旧导出、两种窗口尺寸。日历和快捷范围均使用原时间合同，未改变系统时区。

早期失败原日志保留：首次查询测试未等待异步日志导航；收起的导出按钮不能从可视树定位；小窗口新增标题挤掉完整结果行；旧诊断测试依赖已迁移的技术字段和展开器。分别修复异步等待/按实际控件验证、实际布局预算和当前语义定位。原身份、草稿、焦点、防重、导出失效等关键断言继续保留。PR129 首个 CI 的无窗口编译失败由遗漏 partial 链接和容器 shim 引起，已纳入并完成真实构建；Windows CI 的旧诊断定位失败由当前全链测试重新验证。

发布只是构建和环境报告入口冒烟，没有安装替换。真实相机/RCS、现场用户可用性观察、物理触摸、100%/跨显示器 DPI、安装与现场验收均 NOT_RUN。历史验证不扩张为本包现场验收。
