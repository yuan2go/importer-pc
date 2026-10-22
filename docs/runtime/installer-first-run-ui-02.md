# 安装器与首次配置入口 UI 02

后续纠偏已实施：管理密码最低 5 个字符（允许纯数字及重复字符），原长密码继续可用；安装选项和确认页隐藏“使用范围”，新装保持共用默认值、升级保留原 SID 与数据目录。下面记录原交付形成时的实现和验证身份；本次增量及截图见 [纠偏证据](evidence/installer-first-run-ui-02/followup-password-scope/README.md)，其优先于下文原密码策略和范围展示描述。

日期：2026-10-22；工作包：[INSTALLER-FIRST-RUN-UI-02](../work-packages/WP-IPC-INSTALLER-FIRST-RUN-UI-02.md)。执行时最新 main 为 `0bf86a0a588b75b95aa1963e8fe88dad976ca803`，本地继承 PR88 的 `793d092851052c63663f19b5de8d2b7703392d8d`，开发分支 `codex/installer-first-run-ui-02`。仅本地实施、构建与合成验证；不推送、发布、正式安装或连接生产设备。两份原未跟踪设计文件保持原字节；本包归档副本与它们一致。

本地代码已提交为 `6220fd6a6d3e5d49f9179eb3197b6c0b34518bbc`，未推送或创建 PR。

## 实现与源码映射

安装器入口仍为 `src/Ipc.Installer/App.xaml.cs` → `StartupSetupWindow`，桌面首次配置仍为 `ParcelApp` → `StartupRepairWindow`；全部采用既有 .NET 10 / WPF，无 WebView。UI 与事务分层不变。

| 设计状态 | 实际源码与服务 | 行为 |
|---|---|---|
| 安装选项、确认 | `src/Ipc.Desktop/StartupSetupWindow.cs`：`InstallationFolder`、`Row`、`Summary`、`CaptureInputs`、`Render`；`ParcelStartupServices.Review` | 两目录归入安装位置；范围和独立快捷方式归入安装设置；确认读取同一 `WindowsDeploymentRequest`。自选路径不因切换范围丢失；默认路径保留原联动规则。长路径可滚动、提示、复制，确认完整换行。 |
| 准备与取消 | `StartupSetupWindow.Next`；原 `InstallerPreparation` / `InstallerPayload` | 原字节校验进度保持；准备阶段可取消并排空。安装提交阶段不提供不安全取消，窗口关闭也不能遗弃该操作。 |
| 正在安装 | `WindowsInstallationStage`；`ParcelStartupServices.Handle/ElevateAsync/ReadMaintenanceResponse`；`WindowsDeploymentSetup.Install` | 一次现有 UAC，确认 helper PID 后读取有界阶段消息；备份、复制校验、配置、组件、服务、登记、快捷方式分别报告。无可信总量时只用不确定进度；等待授权提示在 helper 开始后消失。 |
| 失败与结果未知 | `CompleteMaintenanceResponse`、`InstalledReceipt`、`InstallationOutcomeUnknownException`；`StartupSetupWindow.OperationFailed` | 明确失败可返回选项重新核对；缺失、截断、损坏、退出码冲突、超时或回执核对失败按结果未知处理，不暴露重复安装操作。详情来自本次真实阶段与异常；实际记录目录存在才提供打开入口。无自动回滚承诺。 |
| 安装完成 | `StartupSetupWindow.Next/Render`；`IStartupMaintenance.ClassifyAsync` | 成功仅依真实安装终态及完整部署登记核对。首配提示依据实际配置分类；识别到现有配置不要求重复首配，无法核对时明确说明。相机/RCS 提示仅限 Sportmaster。 |
| 完成后打开 | `ParcelApp.xaml.cs`：`--after-install`、`ParcelStartupWindow.AfterInstallation` | 新站进入现有首配；已有配置先等待明确“启动供包台”操作，安装器不自动创建业务运行实例或启动设备作业。正常日常启动合同不改。 |
| 待授权首配入口 | `StartupWorkflows.cs`：`StartupRepairWindow`、`RefreshAuthorization`、`AuthorizationGate` | 区分未设密、待验证、主动退出、到期与服务不可用。关闭密码对话框后仍可重新授权或退出配置。有效授权后才读取配置；失权隐藏现有编辑器及保存/应用入口，原候选仅留在进程中，重新授权恢复同一实例。 |
| 管理密码 | `ManagementModeWindow.cs`；`IManagementMode`、`ParcelManagementMode`、`LocalManagementMode` | 设置：两框、“设置并继续”；验证：一框、“进入管理模式”；保留失败锁定、旧版凭据转换和服务校验。设置后还须真实进入并读回 Authorized。显隐由明确开关控制，提交/关闭/失权清空控件，无密码日志或新增明文持久化。 |
| 统一视觉与适配 | `StartupAppearance.xaml/.cs`；`StartupWorkflowWindow` | 白底、蓝色主操作、系统字体、分组与标签对齐、紧凑进度/完成/授权窗。样式仅用于安装与管理密码/授权入口，参数内页保持现有控件。窗口限制到工作区，内容可滚动，主要操作固定。 |

保留原 12–256 字符、至少五个不同字符及禁止控制字符策略。单一字符类别中的五个不同字符仍可符合规则；没有新增“五类字符”要求。Windows UAC、应用管理会话和操作员身份彼此独立。旧配置含义、现场协议、原请求责任、数据库/配置格式及 D/RV 状态保持。

## 验证与真实截图

Windows 原生 HWND/Dispatcher 定向旅程、真实本地管理服务守卫、合成安装边界与 helper 消息解析的结果见 [独立证据](evidence/installer-first-run-ui-02/README.md)。截图为本机 WPF 渲染的合成场景，不是 HTML 预览、实际安装成功或现场验收。

本包预览是本地 HTML；工具安全策略拒绝直接打开 `file:` URL。本轮以其源码、UI-SPEC 和旧原生截图核对设计，最终视觉核验使用实际 WPF 窗口，未改用浏览器绕过限制。

## 本地运行

从项目根目录 PowerShell 执行，缓存与临时数据继续留在 D 盘：

```powershell
. ./scripts/project-environment.ps1
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:BusinessModule=Sportmaster --no-restore -m:1
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj --no-restore -m:1
$env:IPC_FROZEN_DESKTOP=Join-Path $PWD 'src/Ipc.Desktop/bin/Debug/net10.0-windows/Ipc.Desktop.dll'
dotnet run --project tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj --no-build -- $PWD --installer-first-run-ui
```

测试使用独立 D 盘临时合成目录，安装服务边界注入可控结果；不执行机器安装、SCM 修改或设备连接。实际单 EXE 内嵌载荷需随后获授权打包，不将本地开发构建冒称已发行介质。

## 未验证边界

当前 Windows 显示缩放为 125%；原生窗口另覆盖小工作区等效 DIP 尺寸和长路径、WPF 焦点遍历。未更改用户显示配置，100%/150% 实际 Windows DPI、1024×768/1366×768 目标显示模式、物理键盘/触控与辅助技术人工验收仍待执行。等效逻辑尺寸不等于切换了系统分辨率或 DPI。

完整 UAC 接受/拒绝、真实新装/升级/备份与安装失败的机器事务、进程异常退出后现场恢复、独立安装 EXE、重启/双用户、真实 SDK/RCS/设备和现场均未在本包重新执行。现有持久安装事务被保留，新增阶段通过源码、消息解析和合成 UI 边界验证；不扩大历史介质或 CI 的 PASS。

## 修改文件

产品代码：`src/Ipc.Desktop/{StartupSetupWindow.cs,StartupWorkflows.cs,ParcelApp.xaml.cs,Ipc.Desktop.csproj}`；`src/Ipc.Application.ParcelFlow.Desktop/{ManagementModeWindow.cs,StartupAppearance.cs,StartupAppearance.xaml}`；`src/Ipc.Assembly/ParcelStartupServices.cs`；`src/Ipc.Platform/{WindowsInstallationStage.cs,WindowsDeploymentSetup.cs}`。

必要验证：`tests/Ipc.Desktop.UiVerification/{InstallerFirstRunUi.cs,Program.cs,OperatorAdministration.cs}`（最后一项只同步已批准的新按钮文案）；文档：本报告、本工作包、UI-SPEC/预览和索引、旧索引归档、独立证据、`docs/manifest.json/.sha256`。原本地交接文件不纳入提交。

`.gitattributes` 仅增加本包设计副本、归档和原生回执的原字节保留规则。最终以 Git 索引原字节核对完整清单及本包文档引用，避免 Windows 自动换行导致假哈希差异。旧全库归档扫描器仍报告历史 UTF-16 JSON、字体/样例和旧 PPTX 项，本包未扩修或关闭这些历史项，也不宣称其全库扫描 PASS。
