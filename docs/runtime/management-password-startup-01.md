# 首次管理密码与维护权限交接

2026-10-29，基线 main `7af0be61`。用户截图的 -532462766 是未处理 .NET 异常；本机 Windows 事件指向 `ParcelStartupServices.Handle` 的安装维护权限检查。机器关闭 UAC，当前日常程序主动采用 NORMALUSER 受限令牌，直接 runas 未恢复维护权限。旧的本地鉴权与 UAC 合成截图不涵盖这条真实链路。

`WindowsMaintenanceProcess` 根据实际 Windows 令牌的管理员组被禁用状态，请同用户、同会话的真实 Explorer 桌面执行 runas；其他情况继续原 Windows runas。不依据可尚未生效的UAC注册表值选择路径。使用查询和等待所需的最小进程权限，不从普通程序申请管理员进程完全访问。核验桌面及 helper 的实际映像和 Windows 身份，固定进程句柄，并验证管道 PID、启动时间。helper 等待客户端确认才执行；现有同源哈希、受保护安装目录、DPAPI 原输入、发起者身份及维护帧合同继续校验。权限检查移入可信通道的失败处理，不放宽它。无可信回执保持结果待核对，不终止或盲重放 helper。

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

## 首轮冻结交付（已被令牌补修替代）

源码 `4c65aebff7be80cb84bb8c93b923ce9628b2555d` 已生成本机离线修复候选，版本仍为1.0.0；安装器 SHA256 `d8ea6dcc2b9493ae4d020174a0ac03b46dd46c6e66329876d1a86981fb5a940e`，介质 ZIP SHA256 `f2b651721c5ab44d7c31edbef96854f0df46679978b39accd8c051fd2f1c3f56`。SDK只在本机D盘候选中，不上传二进制。实际安装器准备成功，识别原部署为“更新”，显示原D盘目录后取消，未提交安装或替换现有站点。

同一冻结包的 WPF DLL及同源夹具再次执行本包原生用例通过，介质清单前后不变。沿既有冻结WPF工具生成引用项目，仅选择新增的 `--management-password-layout` 用例；原生安装/其他页面不转记PASS。隔离真实SCM/System服务的首设权限、实际密码、不可覆盖、锁定、撤权、服务故障等检查完整通过；10项文档/安装包测试通过。详见[交付身份](evidence/management-password-startup-01/delivery.json)、[冻结WPF](evidence/management-password-startup-01/frozen-wpf-result.json)及[真实受保护服务日志](evidence/management-password-startup-01/frozen-system-broker.log)。

构建期间PR148合入main `6df6e4e7`，本分支已继承其相机/Python验证和当前文档，并保留CI停用决定。上述候选及测试仍固定原SourceCommit，不把合并后的树或正式站点安装登记改写为已验。PR149承接本包修改。

## 实际令牌补修

随后在合并树 `2ee05729` 的回归中失败：本机此前读取的 `EnableLUA=0` 已变为1，但尚未重启的当前进程仍不支持原runas交接。未修改、恢复或要求用户修改UAC。原失败保存在[回归失败日志](evidence/management-password-startup-01/integrated-authority-fail.log)，首轮4c65aebf安装器标记停止交付，不能把先行PASS扩张到此状态。

实现改为检查真实令牌：管理员组存在但未启用时沿桌面runas；桌面只要求真实Explorer、同用户和同会话，最终helper仍必须具有真实管理员权限。这样避免把注册表或仅有TokenElevationType/IsTokenRestricted当作生效事实。保持普通运行令牌和全部服务端守卫。该状态下重新构建、真实权限往返、生产helper等待确认及普通权限拒绝通过，见[补修日志](evidence/management-password-startup-01/effective-token-authority.log)。当前交付以随后冻结的令牌补修候选身份为准，原包与失败不覆盖。
