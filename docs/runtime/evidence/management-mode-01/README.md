# MANAGEMENT-MODE-01 · 最终交付与证据入口

最终本地源码、介质、安装器及原生测试共同固定为 `0204fcf2fdc8b44d5a4d16b6c5080fa1925cab84`，SM1.11.0 / Application.Contracts1.16.0。没有默认管理密码，也没有管理员账号维护。

- [发行清单](final-0204fcf/RELEASE.json)、[介质哈希](final-0204fcf/SHA256SUMS)、[安装器回执](final-0204fcf/INSTALLER.json)、[包形成时范围快照](final-0204fcf/SUMMARY.json)。Windows介质738个文件；安装器SHA256 `61e7125dd07098660003cd5285c501dfa9090b26bd10652b2085d2af5ea87e63`。
- 同提交、真实已发布WPF DLL、HWND/Dispatcher：[管理模式26项](final-0204fcf/native/WINDOWS-WPF.log)、[首配93项](final-0204fcf/native/STARTUP-WPF.log)、[二进制与介质未改变的回执](final-0204fcf/native/RESULT.json)。Windows11、125% DPI，合成数据与端口。
- 本地[核心原回执](final-0204fcf/local-core/RESULT.json) 保持整体FAIL：[日志](final-0204fcf/local-core/RUN.log) 中管理模式/日志69项已完成，后续商业化恢复步骤被Windows提升权限守卫拒绝；没有跳过或放宽它。
- Windows [CI85原结果](ci85-d33843/SUMMARY.json) 分别保留：受保护凭据/真实SCM及退出失联撤权54项、完整核心139项通过，后续视觉受控延迟场景失败、安装器跳过，整轮FAIL。CI源码与本地最终包具有同一Git树；本机同提交[完整视觉108项](final-0204fcf/visual-original/RESULT.json) 另记。合成场景仅调整延迟预算后的[108项](final-0204fcf/visual-phase/RESULT.json) 使用独立5266fe4测试提交，生产配置仍为原值。
- 最后 [CI86整轮PASS](https://github.com/yuan2go/importer-pc/actions/runs/39644141559)，[独立摘要](ci86-184ea4/SUMMARY.json)：组合源码184ea4，System边界54项、核心139项、视觉108项、参数227项、管理模式26项及单EXE打包/上传通过。原始JSON与日志保留目录关系并核对acceptance引用哈希；CI介质、二进制、安装器和SourceCommit不与本地包互换。148项包输入哈希相同，5266fe4相对0204fcf只改变测试和manifest。
- `74921e4` 为加固异步退出和调整对话框高度前的候选，原[候选记录](candidate-74921e4/README.md) 保留其源码、独立测试提交、固定500ms等待失败以及参数227项验证，不能扩张为最终包PASS。

| 最终关键界面 | 真实原生截图 |
| --- | --- |
| 免登录普通作业与左右独立关联 | [常规](final-0204fcf/native/ordinary-work-1366.png)、[800×420 DIP](final-0204fcf/native/ordinary-work-800.png) |
| 管理密码入口，无用户名 | [进入管理模式](final-0204fcf/native/management-mode-entry.png) |
| 首次设置与修改密码 | [首次设置](final-0204fcf/native/management-mode-first-setup.png)、[修改并退出](final-0204fcf/native/management-mode-password-change.png) |
| 模式状态与按需页面 | [作业](final-0204fcf/native/management-work-1366.png)、[参数](final-0204fcf/native/management-parameters-1366.png) |
| 真实软件日志、原始上下文与紧凑布局 | [查询](final-0204fcf/native/management-logs-1366.png)、[上下文](final-0204fcf/native/management-log-context.png)、[紧凑布局](final-0204fcf/native/management-logs-800.png) |

本机最终版已于2026-10-22北京时间10:27实际升级到D盘SM1.11.0/0204fcf，见[本次独立安装核对](installed-d-20261022/README.md) 与[范围摘要](installed-d-20261022/SUMMARY.json)：738个文件一致、原身份与配置/数据根保持、4个快捷方式及真实System宿主/v2状态核对通过，原仿真账已起停退出0。管理密码未设置，未启动生产或真实设备。此前749候选取消、包形成时NOT_UPGRADED及CI机制验证各守原时间和来源，不改写为本次安装回执；具体边界见[总验证记录](../../validation/management-mode-01.md)。

保留原始公开归档检查结果：最新main基线与最终产品源码均298项，新增0/移除0；不是全仓静态PASS。真实SDK/RCS、目标工控机/触控、完整人工安装器交互、重启、签名和现场验收仍未验证。凭据、令牌、实际Windows SID、生产数据及私有安装日志不进入公开证据。
