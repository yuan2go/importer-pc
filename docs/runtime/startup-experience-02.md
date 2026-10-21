> 本轮继续纠偏以[STARTUP-UX-03](../design/startup-experience-03.md)为准：删除安装/首配无用入口，替代下文把它们折叠到高级选项或配置工具的当前指引；原实现与证据保留身份。

# 安装与首次配置：当前操作与验证边界

本轮基于main `3b883fe`，继承PR83 `0fea1b5`（含PR82），独立分支 `codex/startup-experience`。PR84原工作副本未修改。产品增量为SM **1.8.1**；不是对旧1.7.0安装器重新标版。对应[本轮设计决定](../design/startup-experience-02.md)。

## 用户提出的问题与处理

| 问题 | 当前处理 |
|---|---|
| 版本显示 | 使用介质实际版本，格式为“版本: SM V1.8.1 Windows 64位”。 |
| 安装高级项混乱 | 普通页保留安装目录、数据目录、快捷方式。其他选项分为系统检查、Windows账号、供包台身份与用途、原配置与备份、离线扩展；逐组说明条件。 |
| 工位命名 | 安装与首配面向用户的名称使用“供包台”；内部稳定标识及旧数据路径不改名。 |
| 工位模式 | 以中文“使用用途”解释模拟和生产，移入其他选项。它决定能力/数据隔离，不能在已有部署上直接切换。 |
| 运行环境 | 明确为系统兼容性只读诊断；不是可修改的业务参数，普通安装无需操作。 |
| 无人值守 | 交互安装不再显示计划导出按钮；后台原接口保持，普通用户无需理解。 |
| 种子、账号、运行来源 | 普通页不显示这些内部词。Windows账号默认自动取当前账号；配置来源与程序来源标识仅供诊断。模拟认证账号不创建Windows账号，也不是生产认证批准。 |
| 英文变量名 | 首配摘要使用中文对象/更改项；组件目录和错误提示中文化，精确身份保留在技术详情和原契约。地址、用户配置值和原诊断不假装翻译成另一份配置。 |
| 空条码规则 | 允许规则和排除规则均选填。留空不加该过滤条件；仍要求唯一有效码，重复候选不去重成唯一，候选不完整/超时仍拒绝。旧请求和原规则指纹不改写。 |
| 首配导入文件 | 第一步直接“导入配置文件…”，支持本版本安全模板和当前格式业务JSON；预览后应用到草稿。保留本供包台身份、模式与左右拓扑，不导入业务库、请求或接受账。旧软件未知格式不猜测转换。 |
| 大标题占空间 | 标题缩为18DIP单行，状态提示收紧；完整诊断放在工具内，保留五步结构和同一编辑器。 |
| 按钮太多、操作复杂 | 底栏最多四个按钮。最后一步“保存并启动”组合原检查、保存、权威核对和启动交接；草稿、帮助、诊断与修复进具体菜单，原权限和CAS保持。 |
| 第一次启动的恢复页 | 正常首配直接进向导。未完成关闭后显示“供包台尚未完成配置”，只保留“继续配置/退出”；继续仍为首次配置。真实启动/清理失败保留恢复页和停止守卫。 |

## 操作顺序

安装选好目录与快捷方式后，点击下一步核对，再安装。需要真实设备时，由部署人员在其他选项中确认生产用途及实际驱动；安装不会启动设备或自动授予生产资格。

首次打开后可直接填写五步，也可先导入已有配置。空条码规则无需处理。末步核对相机、调度目标、用途及本次更改，点击保存并启动。结构错误不保存；部署条件缺失允许保存不受影响的初始参数，实际启动继续拒绝缺能力。首次缺配置时写入的初始配置仍由正常启动建立原配置账；保存不冒充运行确认。

保存结果未知时保留原候选、核对权威内容，禁止重复保存。回执丢失但权威候选匹配时可确认原次保存；不创建第二次提交。正常关闭保护未保存输入，已保留的加密草稿可从配置工具继续。

## 实现入口

- [安装与首配窗口](../../src/Ipc.Desktop/StartupWorkflows.cs)：复用原维护契约，紧凑动作与文件预览。
- [启动路由](../../src/Ipc.Desktop/ParcelApp.xaml.cs)：首配未完成与真实失败分开；继续配置不降级为泛化维护。
- [文件导入](../../src/Ipc.Application.ParcelFlow.Desktop/StartupConfigurationFiles.cs)：已知编辑字段覆盖原完整候选，校验格式、用途、拓扑及精确提供者。
- [有界条码筛选](../../src/Ipc.Application.ParcelFlow/BarcodeFiltering.cs)：空允许规则不编造正则，排除仍使用原预算。
- [专项原生验证](../../tests/Ipc.Desktop.UiVerification/StartupExperience.cs)：调用真实WPF窗口及合成维护契约，不提升权限或安装程序。

## 验证与交付

本轮最终日志、原生图及源码身份见[验证回执](validation/startup-experience-02.md)。同机Windows 125%合成原生只证明对应源码与输入/显示路径，不能代替真实SDK/RCS、目标工控机DPI/触控、实际安装升级/重启/双账号或现场验收。D/RV及历史介质SourceCommit不变。用户原截图未加入公开仓库。

运行本轮原生验证：

```powershell
dotnet build src/Ipc.Desktop -c Release -m:1 -nr:false -p:BusinessModule=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -p:RestoreLockedMode=true
dotnet build tests/Ipc.Desktop.UiVerification -c Release -m:1 -nr:false -p:RestoreLockedMode=true
$env:IPC_FROZEN_DESKTOP=(Resolve-Path src/Ipc.Desktop/bin/Release/net10.0-windows/Ipc.Desktop.dll).Path
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --startup-experience
```

本机实际使用 `D:/tools/dotnet/dotnet.exe`，SDK沿仓库global.json。发布构建遵守现有冻结源码和完整介质校验，不把普通bin目录称为可安装发行物。
