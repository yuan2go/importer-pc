# UI02 · 安装结果待确认实施记录

2026-10-22。执行时重新fetch的main为 `0bf86a0a588b75b95aa1963e8fe88dad976ca803`；在本地UI02 `a9253cded4cb2bae4b4890b33e73ef4497933a90` 上续做，代码提交 `f655783c6d695a47e2c1675716666864322e94c1`。远端唯一开放PR88仅作来源核查，未更改其远端；本轮不推送、创建PR、发布或连接设备。原两份未跟踪交接文件保持原样。

现行输入：[九页规范与预览](../ui/installer-first-run-ui-02/result-unconfirmed-20261022/README.md)。它替代原未知结果的“安装未完成”、红色主文、展开详情及统一退出清理约定；原件、旧代码和原证据保留历史身份。此前用户明确认可的5～256字符、纯数字/重复数字可用的密码及隐藏使用范围继续有效。首配内页、业务协议、配置含义、事务提交、权限存储与服务守卫不改。

## 修改文件与真实绑定

| 文件/位置 | 行为 |
|---|---|
| `src/Ipc.Desktop/StartupSetupWindow.cs`：`Render`、`OperationFailed` | 已确认失败与未知结果分别持有互斥状态。未知标题“安装结果待确认”，中性说明、浅琥珀警示，不将完成步骤标成完成。正文不以旧进度推导仍在授权/执行或已经成功/失败。 |
| 同文件：`Details` | 安装详情默认收起。历史摘要移除旧授权指令，明确不代表当前状态；诊断结果独立显示。进一步展开“原始消息文本”保留界面已接收文字，不改真实安装日志，不生成时间戳或模拟终态。 |
| 同文件：`Next`、`OpenRecordDirectory` | 发起本次请求时从真实 `WindowsDeploymentSetup.Registration(ApplicationId)` 捕获所在目录，连接到现有安装记录位置。该目录也可能有其他历史记录，不冒充唯一终态回执。目录缺失或无法打开时明确反馈，完整异常路径保留在反馈提示中；不创建假记录、不刷新结果、不重放安装。未知状态也拒绝旧安装命令。 |
| 同文件：`OperationCompleted`、动作栏 | 主要默认操作为“打开安装记录目录”，次要“关闭”；等待结束后焦点移到记录入口，Tab可到关闭，Esc保留原生关闭语义。无重试、重装、回滚或无依据刷新。已知失败仍可重新审阅，安装执行中仍阻止关闭。 |
| `src/Ipc.Assembly/ParcelStartupServices.cs`：`WriteInstallationProgress`、`ReadMaintenanceResponse` | 修复真实阶段发送端使用缩进文档、接收端按行读取的格式冲突：发送端每行一个紧凑JSON。测试调用实际生产写入函数再交给实际解析器，而非只喂手写示例。观察管道失联不打断持久事务，进度仅由真实阶段驱动。 |
| 同文件：`CompleteMaintenanceResponse` | 保留缺失/截断/损坏/来源/预算守卫，并将失败状态与退出码矛盾、结果字段类型无效归入未知。只有可信终态和原部署登记一致才可成功。实际helper失败码为2、完成码0；不把旧阶段当终态。 |
| 同文件：`ElevateAsync` | 结果未知时保留DPAPI保护的维护请求文件，避免helper仍持有读取句柄时删除失败掩盖未知状态，或关闭后删除其待用输入。没有加入Kill、事务取消或重复执行。其他路径继续原清理。 |
| `src/Ipc.Installer/App.xaml.cs`、`StartupSetupWindow.CanCleanPreparation` | 未知结果关闭时，单EXE不调用准备介质清理，保留可能仍被独立提权helper使用的文件；退出码3表示结果未知。已结束的常规路径继续原清理。窗口正常退出码0仍可包含用户取消，不是新的安装成功凭据。 |
| `tests/Ipc.Desktop.UiVerification/InstallerFirstRunUi.cs` | 覆盖实际写入/读取、观察丢失、矛盾终态、真实异步未知返回、原始文字/摘要、默认焦点/Tab、记录缺失、无重放、准备文件清理许可、关闭，以及既有长路径、页面往返、密码/到期守卫。 |
| INDEX、工作包、UI入口、原实施记录与本报告 | 明确第9页与原8页的替代关系；新附件逐字节归档，用户实机原截图只保存在D盘私有输入区，未加入公开源码。manifest仍按Git规范原字节登记。 |

## 关闭与后台生命周期核查

真实helper由 `Process.Start` + Windows `runas` 独立启动；`ElevateAsync`等待并释放进程句柄，没有调用终止进程。等待超时仍可能执行，因此不能按窗口关闭取消。helper在已核验请求后执行原受保护事务，阶段写入丢失捕获IO错误继续；最后消息丢失不改变已提交的部署事实。

原单EXE `Closed` 无条件删除工作区存在影响仍在执行安装的风险；本轮以 `CanCleanPreparation` 阻止未知结果时清理，服务端加密请求也保留。安装仍在进行时保留原Busy关闭守卫。native夹具验证从真实异步安装动作进入未知页面并关闭，服务边界使用合成结果；真正提权事务等待超时后关闭、系统退出/重启行为尚未实装复验，不能把源码核查和合成验证写成机器事务PASS。

原截图对应的实际事务终态没有在本轮被追认。缩进阶段帧是已确认的源码问题；修复后消息往返通过，不据此宣布原安装成功。没有额外只读终态核对服务，所以本轮不添加刷新。未知结果保留的准备目录及加密请求，应在核对后台已结束后受控清理；不自动丢弃。

## 验证与原生截图

必要验证及原始日志见[独立证据](evidence/installer-first-run-ui-02/result-unconfirmed-20261022/README.md)。本机实际Windows缩放125%；WPF HWND/Dispatcher内容截图为合成安装边界和真实本地鉴权的原生证据。非HTML截图、非生产数据、非真实安装成功凭据。

定向旅程、既有首配回归、原责任/备份阻断通过；Desktop、native与核心验证构建无警告/错误。测试数仅用于定位日志，不代替验收范围。原生往返/长路径、默认焦点与Tab、折叠/展开和原始文本、已知失败/未知、管理取消/错误/到期及失权实际提交拒绝均有对应记录。

实际系统100%/150%DPI、1024×768/1366×768目标显示模式、物理键盘/触控和目标工控机、完整UAC接受/拒绝、新装/升级/备份、未知结果仍在执行时关闭/重启、System服务宿主与真实SDK/RCS/设备/现场未在本轮执行。逻辑窗口矩阵960×700、800×560、665×460 DIP不等于切换了系统DPI或显示模式。未自动运行安装包或改变当前安装状态。

## 本地运行

```powershell
. ./scripts/project-environment.ps1
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:BusinessModule=Sportmaster --no-restore -m:1
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj --no-restore -m:1
$env:IPC_FROZEN_DESKTOP=Join-Path $PWD 'src/Ipc.Desktop/bin/Debug/net10.0-windows/Ipc.Desktop.dll'
dotnet run --project tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj --no-build -- $PWD --installer-first-run-ui
```

随后从冻结Git源构建单EXE，安装器/内嵌介质及共享DLL必须同源一致，真实SourceCommit和SHA256记在D盘交付目录的PACKAGE-INFO。开发截图身份与冻结包身份分别保留，既有D/RV及历史介质不改写。
