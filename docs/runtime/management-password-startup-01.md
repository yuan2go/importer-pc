# 首次管理密码与维护权限交接

2026-10-29，基线 main `7af0be61`。用户截图的 -532462766 是未处理 .NET 异常；本机 Windows 事件指向 `ParcelStartupServices.Handle` 的安装维护权限检查。机器关闭 UAC，当前日常程序主动采用 NORMALUSER 受限令牌，直接 runas 未恢复维护权限。旧的本地鉴权与 UAC 合成截图不涵盖这条真实链路。

`WindowsMaintenanceProcess` 在该条件下请同用户、同会话的真实 Explorer 桌面执行 runas；其他情况继续原 Windows runas。使用查询和等待所需的最小进程权限，不从普通程序申请管理员进程完全访问。核验桌面及 helper 的实际映像和 Windows 身份，固定进程句柄，并验证管道 PID、启动时间。helper 等待客户端确认才执行；现有同源哈希、受保护安装目录、DPAPI 原输入、发起者身份及维护帧合同继续校验。权限检查移入可信通道的失败处理，不放宽它。无可信回执保持结果待核对，不终止或盲重放 helper。

首次设密仍无默认密码，Windows 维护资格只批准首设；随后必须验证实际管理密码才能进入模式。已有密码不可覆盖、5–256 字符、哈希/锁定/撤权、原请求和业务准入不变。当前站点密码和凭据未由验证夹具修改。

密码窗口首设默认 520×540 DIP，仍受工作区限制；长反馈最多占 80 DIP，可滚动查看全文。较小窗口保留局部表单滚动和底部动作。修改沿现有 Noto 字体/字号和管理模式语义，未重做参数表单。合成原生截图与日志位于[本包证据](evidence/management-password-startup-01/README.md)。

GitHub Actions 已全部 `disabled_manually`，本轮取消运行 `40829341553`。`sportmaster-commercial.yml` 与 `hikrobot-sdk.yml` 移除 push/PR 自动触发；统一发行原本仅手动，保持。没有启动新的 CI，不把取消结果当测试通过。

本机验证入口（先执行项目 D 盘环境脚本）：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --maintenance-authorization-startup
dotnet build src/Ipc.Desktop -c Release -p:Application=Sportmaster -p:RestoreLockedMode=true -m:1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --management-password-layout
```

已完成 Windows NORMALUSER→同用户管理员 helper 的实际进程及回执验证；原生 WPF 在实际120 DPI验证默认无滚动、Tab、长失败、400×360小窗口、动作可达和密码缓冲区清空。截图为合成控件内容，不是生产数据或真实设备验收。最终构建/同源包及受保护服务结果追加到本包证据，不扩张历史 PASS。真实 UAC 开启的批准/取消、目标 IPC、SDK/RCS、现场仍未验；本修复不要求用户修改 UAC。
