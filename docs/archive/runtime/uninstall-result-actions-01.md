# 卸载结果按钮修复 · UNINSTALL-RESULT-ACTIONS-01

2026-10-28，基线 `d506f77c`。用户报告三个按钮无用；截图中的继续清理已返回来源不匹配，不能据截图认定所有点击事件都未触发。当前源码只读核对直接回放原日志 Remaining，未重新检查磁盘；关闭按钮此前经通用维护 Run 执行。

## 操作规则

“只读核对原卸载结果”重新取得原部署锁、验证受保护登记/回执并检查实际剩余文件；界面给出本次检查完成反馈。检查不写卸载日志、不删除文件、不将清单外项当作程序文件处理。无法读取、路径异常或扫描超预算时保留原结果并报告核对失败；Prepared 不因目录消失而自动变为已完成。

“继续原清单清理”只在原版完整安装包来源匹配且存在待清理状态时开放。来源不匹配时页面展示两个源码身份，提示退出后打开原版完整安装包；服务的来源、CAS、清单及权限检查继续有效。清理失败保留原清单和回执。

“关闭”只关闭当前窗口。维护执行中仍阻止关闭，空闲和未知结果状态可退出；关闭不表示卸载成功，也不取消已提交操作。

## 本次验证

实现提交 `d85bb4a4dba8a7d83ea98359ba48736f6605012a`，版本保持 `1.0.0`。Windows 11 `10.0.22631`、.NET SDK `10.0.402`。详细源码 blob、发布 DLL 与原日志 SHA256 见[回执](evidence/uninstall-result-actions-01/verification.json)。

|验证|实际结果及防护风险|
|---|---|
|Sportmaster Release 构建、UiVerification 构建及桌面 publish|PASS，0警告/0错误；发布为依赖本机 .NET 的桌面目录，不是完整安装包|
|`Ipc.Verification --commercial-removal`|17项 PASS；真实 Windows 文件占用＋合成重启队列、解锁后重查、外来文件、目录替换、链接拒绝、32769项超预算及原日志保留|
|原生 `--uninstall-result-actions`|43条检查 PASS（含操作完成与可用性检查）；按钮事件、刷新成功/失败、来源不匹配、Unknown防重、同源续办、Busy关闭守卫及实际模态返回|
|发布目录 DLL 的 `--startup-experience`|214条检查 PASS，包含上述按钮回归；首配/保存回读/会话恢复及原有卸载结果仍通过。发布与验证加载的 `Ipc.Platform.dll` SHA256一致|
|视觉核对|真实 HWND/Dispatcher，125% DPI；960×760和680×500 DIP合成窗口，结果、详情及底部按钮可读，无页面整体改版|

内核和定向原生首轮在提交前执行，其源文件 blob 与实现提交一致；发布后完整启动回归对应上述实现提交。首次内核构建的新断言缺少可空注解，已修正；首次原生回归暴露卸载核对错误被通用参数错误翻译覆盖，已在结果页保留实际原因与正确操作说明。两份原始失败及后续 PASS 均[保留](evidence/uninstall-result-actions-01/)，未删除断言或改写原日志。

复现（PowerShell，先执行项目环境脚本）：

```powershell
. ./scripts/project-environment.ps1
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1 -nr:false
dotnet run --project tests/Ipc.Verification -c Release -p:RestoreLockedMode=true -- --commercial-removal
$env:IPC_FROZEN_DESKTOP = (Resolve-Path src/Ipc.Desktop/bin/Release/net10.0-windows/Ipc.Desktop.dll).Path
dotnet run --project tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -- . --uninstall-result-actions
```

新按钮回归已接入原 `StartupExperience`，沿现有同源冻结发布 CI 执行；内核检查加入 Windows CI。当前仅本地结果，未预先声明远端 CI 通过。发布目录与合成截图留在 `D:/DevCache/importer-pc/verification/uninstall-result-actions-01/`；公开证据只含通用源码标识和合成文本日志，不含用户截图、安装数据、DLL或SDK。

没有执行本机真实卸载、Windows实际重启、UAC往返、安装包替换或设备操作；截图中的旧安装实际残留和关闭失效现场未重放，不将合成测试当作该安装已经修好。
