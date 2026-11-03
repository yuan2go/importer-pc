# 首次安装：计划任务缺失异常与原操作恢复

2026-10-26，基线 main `e70b8ece`，工作包 [INSTALLER-TASK-LOOKUP-01](../work-packages/WP-IPC-INSTALLER-TASK-LOOKUP-01.md)。本增量替代“计划任务不存在只会抛出COMException”和“未知结果中的修复失败可返回安装选项”的当前实现；历史安装记录、UI02原稿及原验收不改写。

`DesktopRecoveryInstallation.FindRegisteredTask` 仅包围原GetTask调用。`0x80070002` 或 `0x8004130F` 的COM异常，以及没有FileName的原生映射FileNotFoundException表示任务缺失。具名缺失文件、目录缺失、权限、服务异常及其他IO错误原样抛出。默认关闭恢复时，任务缺失是幂等清理，不会创建或启动任务；启用恢复时仍使用原任务XML、所有者与权限检查。

`StartupSetupWindow.OperationFailed` 保持已有Unknown，只有可信原操作核对可解除。原安装已提交而修复失败时，显示系统入口修复结果待确认，原始错误仍在详情，保留原请求、准备介质、记录入口、核对及原系统入口修复；旧安装命令不重放。已确认提交前失败的原有重新审阅路径保持。

最初真实Windows只读查询复现FileNotFoundException，生产查找边界在修复前失败；修复后13项定向检查通过，包括实际COM查询、两次幂等清理、已存在身份及非缺失异常传播。原计划任务XML验证1项、监督/恢复18项、安装责任守卫9项、生命周期应用层213项通过。新增原生夹具通过真实异步修复按钮复现旧状态错误，修复后完整安装/首配原生旅程106项通过；当前实际缩放125%，665/800等为逻辑窗口尺寸，不冒充OS显示模式或目标工控机。核心、桌面及原生夹具Release构建零警告零错误。

原件及源码哈希见[定向证据](evidence/installer-task-lookup-01/FILES.json)。原生截图为合成安装契约与真实HWND/Dispatcher，未执行完整安装事务、设备、SDK/RCS或现场验收。本机原登记修复记录仅留D盘私有目录，不上传用户材料；不通过伪造回执、改写原失败记录、跳过权限或重复安装解决。

本机复现：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --p0-task-lookup
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --p0-task-validate
```

两项Windows计划任务检查均不注册或启动任务。CI的Windows边界旅程新增 `p0-task-lookup`，保留原任务XML及其他边界检查。后续冻结介质必须绑定实际源码提交和哈希，新安装包不自动安装；真实首次安装及现场仍单独计证。
