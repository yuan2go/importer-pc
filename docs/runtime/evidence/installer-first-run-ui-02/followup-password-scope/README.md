# UI02 · 五位密码与隐藏使用范围

2026-10-22。用户已明确允许5位纯数字管理密码，并确认隐藏安装“使用范围”。执行时 main 仍为 `0bf86a0a588b75b95aa1963e8fe88dad976ca803`；增量代码提交 `043157fb2238d3408582565e30a64350ca13a050`，继承原 UI02 和 PR88，本地分支 `codex/installer-first-run-ui-02`。旧输入、截图、日志及介质保持原身份；本目录只记录后续纠偏。

## 修改文件与源码映射

| 文件 | 实际行为 |
|---|---|
| `src/Ipc.Platform/LocalManagementMode.cs` | 统一凭据创建策略最低5、最高256字符，允许纯数字及重复字符，拒绝控制字符。首次设置、修改及旧凭据转换复用此策略。既有密码验证、PBKDF2/随机盐、锁定、到期及服务守卫不变。 |
| `src/Ipc.Application.ParcelFlow.Desktop/ManagementModeWindow.cs` | 设置和改密提示与服务一致。无新增明文存储或日志。 |
| `src/Ipc.Desktop/StartupSetupWindow.cs` | 选项和确认不显示范围，新安装默认共用；已有部署保留原SID、数据目录，安装提交使用同一审阅请求。目录和独立快捷方式继续显示。 |
| `scripts/package-application.py` | 随包启动说明移除范围选择操作，按真实机器存储根说明新数据目录，避免旧用户目录指引。 |
| `tests/Ipc.Application.Verification/OperatorAdministration.cs` | 真服务的五位重复数字设密、改密、长度/控制字符拒绝、失败状态不变、旧授权撤销及无明文凭据检查。 |
| `tests/Ipc.Desktop.UiVerification/InstallerFirstRunUi.cs`、`OperatorAdministration.cs`、`StartupExperience.cs` | 真实WPF输入与服务授权、范围隐藏/升级身份保留、往返、长路径、错误/到期守卫及首配回归。 |
| 当前工作包、UI README、管理模式与首配运行说明、实施记录及INDEX | 以用户纠偏替代旧密码复杂度和范围展示；随包首配说明也同步隐藏范围的操作指引。原UI-SPEC/Preview及旧证据不倒写。 |

## 验证与截图

Desktop、原生验证、核心验证工程构建均0警告/0错误。定向原生61项、管理窗口27项、既有首配93项、管理服务78项通过。计数只定位原始日志，不代表系统级或现场验收。日志：[原生](NATIVE.log)、[管理窗口](MANAGEMENT-NATIVE.log)、[首配](STARTUP-REGRESSION.log)、[服务层](MANAGEMENT-CORE.log)；构建：[Desktop](DESKTOP-BUILD.log)、[Native](NATIVE-BUILD.log)、[Core](CORE-BUILD.log)。

真实Windows缩放125%，截图来自已显示HWND的WPF内容渲染，不含系统边框。安装事务边界为合成结果，鉴权与过期拒绝调用真实本地服务；无设备调用。已人工查看[常规选项](installer-options-1366x768-100-equivalent.png)、[小区域](installer-options-1024x768-125-equivalent.png)、[最小区域](installer-options-1024x768-150-equivalent.png)、[确认长路径](installer-confirm-long.png)、[首次设密](password-first-setup.png)。equivalent仅指逻辑工作区，不是实际切换系统DPI或分辨率。

一次核心回归命令漏传仓库参数，夹具把开关当目录，找不到配置文件。原失败保留为[命令错误](EARLIER-CORE-COMMAND-ERROR.log)，更正命令后通过，未改断言或跳过用例。

实际系统100%/150%DPI、目标显示模式、物理键盘/触控与工控机、完整UAC/新装/升级/备份/服务宿主和机器失败恢复仍待用户测试；未自动运行安装器或修改机器安装状态。真实SDK/RCS/设备及现场NOT_RUN。不推送、不创建PR、不发布。

源码、开发程序集和证据原字节身份见 [RESULT.json](RESULT.json)。单EXE会从最终冻结Git提交构建，交付目录附独立PACKAGE-INFO及SHA256；该包的SourceCommit与本次开发构建身份分别记录，不将开发截图冒称安装包实装。
