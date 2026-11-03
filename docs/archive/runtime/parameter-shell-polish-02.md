# 参数与经典窗口细化：实现和集成

本轮实现与验证代码提交：`26f239f7e23b1476c7734c35f5f4454c408f4d7c`；后续清单提交不改变源码/测试。

执行时 main `dd1dfefc9e829f557a1efdf6f7ad7532fec4ef81`，PR88已合并，无开放PR。本地独立分支 `codex/parameter-shell-polish-02` 的增量父为 `9900cbe9e9dd573d46c6b8b2dbb1d52dc46b7bc1`，保留本会话上一轮未推送的内部页面和逐页校验。原 AccountDisplay 已是独立单行，本轮没有重建账号/模式模型，也未修改 ManagementModePresentation 或权限服务。

## 实际修改与源码映射

| 文件（仓库根相对路径） | 变化 |
| --- | --- |
| `src/Ipc.Application.ParcelFlow.Desktop/ShellWindow.xaml` | 产品/身份与导航/供包控制分行。长身份截断但 Tooltip 和自动化名称完整；模式使用现有中性菜单。当前页面有选中样式；暂停/恢复按已有真实状态显示。参数页不混入作业登录输入及原请求动作，动作在原工作页仍保留，环境/软件暂停边界持续可见；状态与阻断信息仍可滚动查看。 |
| `src/Ipc.Application.ParcelFlow.Desktop/ShellWindow.xaml.cs` | SelectedWorkspace只反映实际PageHost内容，用于展示；没有业务或权限状态。菜单和导航沿用原命令；失权立即关闭参数/日志工作区，真实服务守卫不变，编辑器和进程草稿仍保留。 |
| `src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.Policy.cs` | 短标签与必要局部提示；完整范围、恢复集要求和低频显示说明收进可键盘展开的原诊断区。操作员服务选项按真实能力显示中文，选项对象、实例身份、绑定和配置不改；原ID在提示/诊断可核对。 |
| `src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.cs` | 操作员会话的毫秒和目标效率的件/小时独立对齐显示；存储MiB、日志天数等单位保留。 |
| `src/Ipc.Application.ParcelFlow.Desktop/ChineseChoiceTextConverter.cs` | 复用既有中文反馈投影于经典参数页，原始技术反馈仍在诊断；不更改原异常或日志。 |
| `tests/Ipc.Desktop.UiVerification/ParameterShellPolish.cs`、`Program.cs` | 原生前后采集、长身份/布局/页脚、候选字节不变、键盘/菜单、暂停恢复、实际退出/到期与服务写拒绝。 |

保留四类参数、同一Application/完整候选、schema字段/单位/范围、操作员身份、管理密码策略/存储/服务校验、保存/CAS/权威回读/待生效区分、原请求责任及Unknown不重发。换页不建立第二个业务实例，不引入UI框架、WebView、设备SDK或字体依赖。

## 继续集成

本次交付本地独立 commit 和 D盘补丁，不推送/创建PR/发布/安装。完整提交序列以9900cbe9为父；`source-only.patch`仅含本轮src/tests，可供只有最新main的集成方先检查，不能把先前本地成果重复计作本次改动。目标若缺少上轮内部页与逐页校验，应分别审阅其原提交f1d723f和清单提交9900cbe9，不重建或覆盖。

实际前后图、日志、完整修改清单与来源见[证据](evidence/parameter-shell-polish-02/README.md)，验证及未执行项见[记录](validation/parameter-shell-polish-02.md)。原当前指引[归档](../archive/pre-parameter-shell-polish-02/README.md)，历史D/RV、版本、发布和截图保持各自身份。

本轮src/tests补丁已对最新main独立临时索引执行 `git apply --cached --check` 通过，见[原回执](evidence/parameter-shell-polish-02/MAIN-APPLY-CHECK.log)。这是应用检查，不是main合并树运行验收；没有改动真实工作索引或旧分支。
