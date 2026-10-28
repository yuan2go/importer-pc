# 安装生命周期与普通启动证据

2026-10-28；代码/冻结介质/冻结 fixture SourceCommit `221f058333258622b9150f9d2d77c4346c4bb6f5`。后续证据提交不改写这些来源。Windows 23H2 x64 / 实际显示 125% DPI；本机 UAC 关闭、父进程管理员权限。所有临时、数据、发布和缓存目录在 D 盘。

本目录只发布通用代码验证日志及合成 UI 截图，不含用户原截图、真实业务配置/数据、凭据或 SDK/字体二进制。`FILES.json` 固定本目录原文件 SHA256；排除自身以避免自引用。日志按原字节保留，包含早期真实失败；每个冻结回执保留自己的 SourceCommit 和边界。

执行前 `. ./scripts/project-environment.ps1`：

```powershell
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --ordinary-runtime-startup
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --installer-change-guards
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --lifecycle-ux
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --p0-supervisor
dotnet tests/Ipc.Verification/bin/Release/net10.0/Ipc.Verification.dll --runtime-identity
dotnet tests/Ipc.Verification/bin/Release/net10.0/Ipc.Verification.dll --windows-runtime-setup
dotnet tests/Ipc.Verification/bin/Release/net10.0/Ipc.Verification.dll --installed-lifecycle-windows
./scripts/verify-commercial-wpf.ps1 -Media <frozen-media> -Output <new-evidence> -StartupExperience
./scripts/verify-commercial-wpf.ps1 -Media <frozen-media> -Output <new-evidence> -InstallerFirstRunUi
./scripts/verify-administrator-broker.ps1 -Media <frozen-media> -Output <new-evidence>
```

构建均 Release / RestoreLockedMode / 单 MSBuild worker；桌面明确选 `-p:Application=Sportmaster -p:BusinessModule=Sportmaster`。包由现有 `package-application.py --application sportmaster --target win-x64 --mvs-runtime <authorized-local-runtime>` 与 `package-installer.py --media <frozen-zip>` 生成。

真实 Windows 隔离产品完成安装、同源再装、更新、失败、原操作修复、卸载、保留数据重装；每阶段真实普通子进程通过原机器身份保护，可读程序/写数据且不能改程序。末尾核验真实 sportmaster 登记、运行策略与可信根字节未改。原责任、CAS、备份、Unknown、监督进程 PID/预算/故障保护继续检查。

`frozen-startup` / `frozen-first-run` 是同源实际 WPF，安装及业务回调合成；`frozen-broker` 使用真实 SCM、System 与受限 Windows 调用者，但服务根、密码和业务数据合成。单 EXE 冒烟只实际准备/选项/取消，退出码 0。真实产品更新、OS 重启删除、SDK/相机/RCS和目标工控机/现场验收仍未执行。

早期失败是合成介质缺少必要配置、反射命中重名 Render、默认 Supply 桌面不含 Sportmaster 类型，以及平台分析器要求回调内明确 OS 守卫。修复输入/定位及明确保护，保留失败，不删断言、不跳过关键检查换 PASS。
