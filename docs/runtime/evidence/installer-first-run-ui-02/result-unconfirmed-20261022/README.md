# UI02 · 第9页与真实回执绑定证据

2026-10-22；代码提交 `f655783c6d695a47e2c1675716666864322e94c1`，执行main基准 `0bf86a0a588b75b95aa1963e8fe88dad976ca803`。本地分支 `codex/installer-first-run-ui-02`，未推送、创建PR、发布或操作设备。完整修改清单、源码映射、关闭核查和运行命令见[实施记录](../../../installer-first-run-ui-02-result-unconfirmed.md)。原八页输入、旧验证与介质保持原SourceCommit。

Windows实际缩放125%。22张PNG来自显示HWND后的WPF/Dispatcher内容渲染，不含系统标题栏；安装边界为合成异步结果，鉴权及到期提交拒绝调用真实本地服务。不是HTML预览、真实安装/UAC/系统服务或现场验收。`local-source`为开发验证标识，正式包版本来自冻结介质；不把开发截图版本标识当成发行身份。

## 结果与记录

| 风险 | 验证结果 |
|---|---|
| 实际阶段帧、可信终态 | [NATIVE.log](NATIVE.log)。调用生产写入函数再调用实际读取/终态解析，遍历阶段；坏格式、截断、丢失、矛盾退出码与无效结果类型拒绝作为成功/已知失败，观察通道失联不打断helper。 |
| 结果未知页面和关闭 | 同日志。从绑定安装动作的异步异常进入独立未知状态，标题/琥珀提示、完成步骤不高亮、详情默认折叠、历史摘要与原始文字分开、原始文字保留；仅记录与关闭、焦点/Tab、小窗口固定操作、记录缺失反馈、拒绝旧命令、关闭时准备清理许可为false，无重放。 |
| 既有首配与安装布局 | [STARTUP-REGRESSION.log](STARTUP-REGRESSION.log)。完整候选及五步、原权限和启动门禁、安装两页/完成行为保持。 |
| 原责任、备份与未知请求 | [CHANGE-GUARDS.log](CHANGE-GUARDS.log)。原Unknown责任阻断、无重复发送/提交，先完整备份后调用部署。 |
| 构建 | [Desktop](DESKTOP-BUILD.log)、[Native](NATIVE-BUILD.log)、[Core](CORE-BUILD.log)，均0警告/0错误。实际安装器宿主由随后冻结同源打包构建，不以开发UI测试冒充宿主实装。 |

定向原生82项、首配93项、责任守卫8项只用于定位日志，不代表整机或现场验收。先前本地抛光截图保持在私有开发目录，最终证据为本目录文件；本轮未通过删除断言或吞掉测试失败换PASS。源文件、程序集及本目录日志/图像原字节见[RESULT.json](RESULT.json)。

## 真实原生截图

| 页面/状态 | 截图 |
|---|---|
| 安装选项与长路径/逻辑工作区 | [960×700](installer-options-1366x768-100-equivalent.png)、[800×560](installer-options-1024x768-125-equivalent.png)、[665×460](installer-options-1024x768-150-equivalent.png) |
| 确认安装 | [同一请求与完整长路径](installer-confirm-long.png) |
| 安装中 | [等待授权](installer-stage-WaitingForAuthorization.png)、[复制文件](installer-stage-CopyingFiles.png)、[提交登记](installer-stage-CommittingInstallation.png) |
| 已知失败 | [失败及重新审阅](installer-failure.png) |
| 未知结果默认 | [折叠详情](installer-unknown.png) |
| 历史与诊断 | [可读摘要](installer-unknown-expanded.png)、[原始文字展开及滚动](installer-unknown-original-text.png) |
| 未知结果小工作区/记录缺失 | [800×560](installer-unknown-125-equivalent.png)、[665×460](installer-unknown-150-equivalent.png)、[真实目录缺失反馈](installer-unknown-records-unavailable.png) |
| 安装完成 | [需要首配](installer-complete-first.png)、[已有配置](installer-complete-existing.png) |
| 管理授权 | [首次设密](password-first-setup.png)、[验证失败](password-rejected.png)、[服务不可用](password-service-unavailable.png) |
| 首配入口 | [尚未设密](first-run-unconfigured.png)、[真实到期](first-run-expired.png) |
| 安装后打开 | [等待用户主动启动](after-install-start-gate.png) |

文件名中equivalent指调整窗口逻辑尺寸，全部真实系统缩放仍为125%。没有切换实际100%/150%DPI或1024×768/1366×768系统显示模式。物理键盘、触控、目标工控机与辅助技术人工验收仍待执行；只核查了真实WPF焦点/Tab和可达性。

## 核查与未验边界

布局、文案和抛光按现有白底/蓝主按钮与WPF原生控件执行：修正红色失败归类及大空白，历史摘要去掉旧授权指令；原始文字须显式展开，不冒充当前状态；目录不可用反馈收紧，完整异常仍可查看提示。未引入WebView、浏览器检测或新参数页。密码仍为此前已授权的5～256字符策略，隐藏使用范围继续有效。

客户端独立启动提权helper，等待和关闭没有Kill；未知结果保留准备介质及DPAPI保护请求，避免销毁仍可能需要的输入。native验证从真实绑定的异步操作进入未知并关闭，验证宿主清理许可为false；真正System/UAC安装在仍执行时关闭、宿主退出码3和重启恢复未实装执行。已结束窗口不能证明旧安装成功或失败。

完整UAC/新装/升级/备份、服务宿主、真实系统DPI和显示模式、目标IPC、SDK/RCS/设备及现场为NOT_RUN。未自动运行安装器、重装、清理当前机器安装状态或连接设备。冻结包与当前开发构建身份分开，其实际构建/同源校验及SHA256在D盘交付回执中保存，不扩大旧版本验收或D/RV。
