# MANAGEMENT-MODE-01 · 独立版本证据

本地产品/介质/安装器固定为 `74921e4b3a757f2830b2be286bcee86833f66649`，SM1.11.0 / Application.Contracts1.16.0；[RELEASE.json](RELEASE.json)、[SHA256SUMS](SHA256SUMS)、[安装器原回执](INSTALLER.json) 保留实际身份。安装器未签名，不包含默认管理密码。

本地原生页面通过26项、首次配置通过93项，Windows11、125% DPI，使用真实已发布WPF DLL与HWND/Dispatcher，端口与数据为合成。测试固定于 `f89148c26140eb8b9303789e1b74d981b7f0a0c9`；与产品源码的差异只有异步验证等待和manifest，产品二进制未重建或改写。此差异显式记在 [RESULT.json](local-frozen/RESULT.json)，不称为同提交测试。

首次固定等500ms的检查在异步查询完成前失败，保留[失败原记录](local-frozen/initial-timing-failure.log)；修正为15秒有界等待按钮恢复后检查真实结果，并增加原始上下文必须包含选中原记录的断言。[通过原记录](local-frozen/WINDOWS-WPF.log) 与 [首配原记录](local-frozen/STARTUP-WPF.log) 保留实际验证范围。

| 当前界面 | 截图 |
| --- | --- |
| 免登录普通作业，固定左右关联 | [常规](local-frozen/ordinary-work-1366.png)、[800×420 DIP](local-frozen/ordinary-work-800.png) |
| 管理密码入口，无用户名/账号列表 | [进入管理模式](local-frozen/management-mode-entry.png) |
| 首次设置与改密码 | [密码确认](local-frozen/management-mode-first-setup.png)、[修改后退出](local-frozen/management-mode-password-change.png) |
| 模式状态与按需功能 | [作业](local-frozen/management-work-1366.png)、[参数](local-frozen/management-parameters-1366.png) |
| 软件日志和原始上下文 | [查询](local-frozen/management-logs-1366.png)、[上下文](local-frozen/management-log-context.png)、[紧凑布局](local-frozen/management-logs-800.png) |

复现：用仓库脚本冻结Sportmaster，再执行 `scripts/verify-commercial-wpf.ps1 -Media <展开的win-x64介质> -Output <新的证据目录> -ManagementMode`；历史介质使用明确的 `-FixtureCommit`。首配以同一NativeMedia.csproj及 `IPC_FROZEN_DESKTOP=<介质>/desktop/Ipc.Desktop.dll` 执行 `--startup-experience`。所有本地输出、缓存和交付使用D盘项目环境。

[归档比较](ARCHIVE.json) 使用原检查器比较最新main与产品源码：既有298项完全保留，新增0、移除0；这不是全仓库静态检查PASS。凭据哈希、令牌、实际账号/Windows SID、原运行数据和私有安装日志不进入公开证据。真实SDK/RCS、目标工控机与触控、完整安装器人工交互、重启及现场验收未验证。
