# PR95整合后跨平台编译续修

2026-10-24。分支`codex/task-context-ci-fix`以main `67a6440711d1069f2cf82df265279d635d4e61b8`开工；当时无开放PR。PR94已合并为`524ff26264003b283ae188ee28fd6630513b36d2`，PR95协调提交`5b7709840cf5b0071dfb6e3767ce5e0bd59dc5c2`已包含它。四个本地未跟踪原稿/ZIP保留，不提交。

## 实际失败与修复边界

读取[PR95失败运行40048551030](https://github.com/yuan2go/importer-pc/actions/runs/40048551030)两个作业的实际日志：测试合并为`8b00714b0b9046a49167f529a382c182f281f452`，双亲`c2300cd`/`8517dec`。Linux编译出现五项CS0103：SupplyStationViewModel第53行两处TaskFeedback，ParcelViewModel第268～270行TaskFeedback、HasOperationalAttention和HasCurrentOperation。Windows首配在ParameterUiRefinement第23行提取device.ip时抛出Sequence contains no matching element。

正式`Ipc.Application.ParcelFlow.Desktop`工程通过SDK默认Compile项包含`TaskContextPresentation.cs`；`Ipc.Application.Verification`为保持net10.0跨平台目标，显式链接桌面模型源码但遗漏该partial文件。本次只新增该文件的Compile链接，复用正式实现，不复制成员、不引入WPF依赖、不改行为、安全断言或工作流通过条件。编译本身覆盖此缺口，不增加镜像断言。

TCP版本绑定沿用PR94的CapabilityDraft.BuiltInReaderBinding、首配和运行参数入口及ReaderCatalogBindings回归；没有重复实现或覆盖其修改。[PR94运行40047019681](https://github.com/yuan2go/importer-pc/actions/runs/40047019681)的两个作业已核实success，历史产物仍属于其测试合并`4a33d900eee109ef7abb513537c3b9e5f4d70641`，不能改标为本次源码。

## 本次验证

本机Windows、D盘项目环境：修复前locked Release构建实际复现相同五项CS0103；补链接后同命令0警告/0错误。AQA01完整入口PASS，包含实际子进程崩溃、持久命令回放和原请求守卫。MSBuild实际Compile项确认正式与跨平台工程各包含且仅包含一次同一TaskContextPresentation.cs绝对路径、SHA-256一致。宿主Sportmaster和原生夹具locked Release构建均0警告/0错误。

首配和参数壳原生入口均ExitCode=0。首轮直接调用首配夹具因漏设IPC_FROZEN_DESKTOP而显式失败；补构建宿主并指向同一工作树DLL后重跑成功，原断言未变。该本机构建验证不冒充冻结介质验证。日志保留在D盘仓库`.artifacts/task-context-ci-fix/`，原生完整证据路径由各次输出记录。后续托管CI和产物身份在取得证据后追加；原PR95的140项本机UI及截图保持[原验证范围](../task-content-hierarchy-01.md)。

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --architecture-quality-remediation
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1 -nr:false
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
$env:IPC_FROZEN_DESKTOP=(Resolve-Path src/Ipc.Desktop/bin/Release/net10.0-windows/Ipc.Desktop.dll).Path
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --startup-experience
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --parameter-shell-polish
```

托管工作流仍从新PR测试合并提交构建冻结介质，并核验SourceCommit与安装器Payload身份；重跑旧40048551030只会验证旧源码，不能证明本次修复。Linux运行、Windows工程验证、实际安装和真实现场分别记录。目标工控机、物理键鼠/扫码、其他DPI、真实相机/SDK/RCS、完整安装/升级/UAC及现场验收仍需实际执行；无生产设备控制或现场配置修改。历史REQ/BR/CAPI/D/RV不变。
