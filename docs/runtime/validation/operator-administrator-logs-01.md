# OPS-ADMIN-LOGS-01 · 交付验证

2026-10-21，开发分支 `codex/operator-admin-logs`。开工及发布前复核 main 为 `3b883fe6a53b58b29eeca18c3719b5bfc0ee388e`；PR82～85仍open（84/85 draft），无后继相关PR。整合85 `d52ade7` 与84 `587fbe8`，保留原PR和历史SourceCommit。产品为Sportmaster 1.9.0 / Contracts 1.15.0 / 配置schema3 / DB8。

功能取舍见[工作包](../../work-packages/WP-IPC-OPERATOR-ADMIN-LOGS-01.md)，实际操作和安全边界见[当前指南](../operator-administrator-logs-01.md)。删除支持摘要的契约与实现，保留独立诊断、备份恢复和原业务审计。本轮不改写历史D/RV。

## 本轮工程验证

全部为本轮组合源码执行，Windows原生截图来自HWND/Dispatcher及合成端口。不是旧版本测试，也不是浏览器/HTML对WPF的替代验证。

| 验证 | 结果与范围 |
|---|---|
| 核心 `--operator-administration` | 44项PASS：真实密码哈希、首管不可覆盖、持久失败保护、退出/到期/账号撤销、一次性请求绑定helper授权、免登录管理员保护、参数保存守卫、轮转原始上下文、完整异常栈、307匹配/308时间记录全量导出、15000条流式导出与供包并发、正则超时/取消、未知防重。 |
| `--parameter-refactor` | 75项PASS，复用完整候选、参数继承和字段归属。 |
| `--configuration-confirmation` | 41项PASS，暂停/排空、原确认身份、恢复与持久确认守卫；只对明确未准入状态有界等待，不吞异常。 |
| `--parcel-recovery-p2` | 101项PASS，持久原次Query证据、重启与原请求不重发。 |
| `--version-compatibility` | PASS，跨业务/篡改合同拒绝；原配置和Pending保留。 |
| 原生 `--operator-administration` | 16项PASS、8张截图：常规/紧凑角色导航、真实账号显示、左右自动供包、完整候选跨退出保留、真实软件日志/上下文、800×420查询与取消、撤权关闭已打开页面。 |
| 原生 `--parameter-v3` / `--visual-productization` | 219 / 102项PASS，四类参数、保存核对、草稿/错误/焦点、条码与格口关联、800×420～1600×900客户区；过期入口断言已按新职责替换。 |
| 原生 `--startup-experience` | 90项PASS，五步完整候选、TCP/RCS静态配置、未知保存防重、首配/安装窗口。认证账可以创建，业务DB和设备租约不能在向导浏览时创建。 |
| 受影响Release构建 | 核心验证、原生验证、Sportmaster Desktop均0警告/0错误。 |
| Python发布检查 | commercial_delivery 5、release_documents 2、release_build 1、unified_release 15项通过。首轮缺dotnet PATH及GBK解码失败保留，配置真实SDK路径、UTF-8和D盘TEMP后重跑通过。 |

原始日志与截图见[证据目录](../evidence/operator-administrator-logs-01/README.md)。核心及原生夹具已接入同源冻结介质验证和PR Windows检查，不通过修改旧介质或扩大旧PASS实现。

## 关键页面

![操作员作业](../evidence/operator-administrator-logs-01/operator-work-1366.png)

![管理员参数](../evidence/operator-administrator-logs-01/administrator-parameters-1366.png)

![管理员日志](../evidence/operator-administrator-logs-01/administrator-logs-1366.png)

![紧凑日志](../evidence/operator-administrator-logs-01/administrator-logs-800.png)

## 发布身份与验证边界

产品源码冻结后再生成同源portable、win-x64介质及1.9.0安装器；最终SourceCommit、哈希、启动冒烟与冻结DLL检查随交付回执补记，不能以以上工作树检查冒充发布验证。

完整商业恢复在本机普通账号下被Windows部署目录权限拒绝，原失败保留；不放宽权限检查或将它记为PASS。首次错误运行的C盘空间保留守卫及无工作区参数启动错误也保留，未污染交付输入。

本机Windows 125%缩放，截图是指定DIP客户区的原生渲染，部分大客户区超过物理工作区；不声称目标工控机、物理100% DPI、触控、完整IME、双Windows账号/真实UAC安装升级/重启验证通过。真实SDK、客户相机实体语义、真实RCS、权威结果查询、现场安全与验收仍未验证；Production缺必需能力继续拒绝。安装器未签名，未提供签名证书；旧发行及其证据保持原身份。
