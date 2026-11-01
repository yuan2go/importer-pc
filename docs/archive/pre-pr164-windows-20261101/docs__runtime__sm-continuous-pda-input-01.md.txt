# 连续 PDA 扫码拼接修复：实现完成，Windows 验证待执行

2026-10-30。基线 main `67ab87c1a1b146611504342c6ab080ed045231ec`；[工作包](../work-packages/WP-IPC-SM-CONTINUOUS-PDA-INPUT-01.md)。本轮为 Linux 工具环境，无 dotnet、PowerShell 或可访问的 Windows 交互桌面。**没有执行 WPF 原生复现/测试或构建，不宣称 P1 已验证关闭。** GitHub Actions 保持停用。

## 源码依据与修复

原 `SubmitBarcodeAsync` 在 Admitted 后只重置 `editing/inputBasis`，`RefreshBarcodeDisplay` 在值相同时直接返回；TextBox 保留 A 和末尾光标。B 的首字符按 WPF 默认行为追加，`InputChanged` 又为它取得新基准，随后 Enter 可能提交 A+B。原测试整体设置 `Text`，没有经过该路径。以上为当前源码追踪，尚无本轮 Windows 运行证据。

`SupplyStationPosition` 新增仅在无草稿、无在途/未确认命令时生效的替换准备。结果刷新（包括值未改变）及返回输入框焦点时选择整条；首个 PreviewTextInput/粘贴在原字符预算检查前再次准备，防止操作员点击旧结果后光标落在中间/末尾。首字符进入后由既有 `InputChanged` 转为草稿，后续编辑保留正常光标行为。保留结果条码和下方原任务；输入下一件时原任务仍由原 ViewModel/Application 投影。

生产修改仅限该控件的代码和事件绑定/操作提示。Application、RCS、持久化及业务准入无变更；不通过清空业务事实、改请求、取消 Unknown 或自动重发实现替换。拒绝/冲突草稿继续保留。键盘和 PDA 共用明确提交入口，键盘可按需修改后点击提交，PDA 终止回车提交一次；不按输入速度自动提交或推断来源。

## 必要 Windows 验证

新增 `ContinuousPdaInput.cs` 和 `--continuous-pda-input` 分派，不删除旧测试、不新增 CI/依赖或重复框架。旧测试 0 删除/合并，生产 Application/RCS 0 修改。新测试不通过赋值 `TextBox.Text` 或模拟文本事件输入条码：Windows SendInput 注入真实按下/释放，经 HWND/WPF 文本和键盘事件进入输入框。每批注入前检查前台 HWND、键盘焦点和无按住修饰键，条件不满足即失败。

测试复用 `ManualSupplyHarness`、实际 SQLite/Application 和内置 RCS 对合成 HTTP peer：

- A、回车、B、回车，中间无清空/赋值/改选区/重新聚焦；核对 A/B 的独立命令与作业、逐字符 WPF 事件和真正 HTTP loadCode，无 A+B。
- 保留上一件结果，额外回车不建新命令；旧结果中移动光标后输入仍从新条码开始。
- 手动输入不自动提交，左右键/Backspace 修改、焦点返回和轮询保留草稿/光标；按钮提交保留 Keyboard 来源。
- 原 HTTP 调用挂起时注入后续字符/Enter，输入保持锁定、无第二命令。
- 明确标注的 UI 传输夹具在真实持久准入之后返回 Pending 或丢失回执；后续扫码不改原输入，核对复用相同命令且无再次 HTTP 请求，不把夹具当生产结果。
- 合成无效 RCS 回包形成实际业务 Unknown；下一件只形成拒绝回执并保留草稿，原作业/尝试次数和 Unknown 不变。最终管理授权读取第一件原冻结报文，与最初 HTTP 原字节核对。

在 Windows x64 的独立 D 盘 checkout、交互桌面上执行。保持测试窗口前台，勿在执行中操作键盘；先运行环境脚本，不更改机器全局环境。仅运行以下必要定向验证，任一非零停止并保留失败证据：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false --nologo --verbosity quiet
if ($LASTEXITCODE) { throw 'WPF verification build failed' }
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll $PWD.Path --continuous-pda-input
if ($LASTEXITCODE) { throw 'Continuous PDA native verification failed' }
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll $PWD.Path --workstation-barcode-ready
if ($LASTEXITCODE) { throw 'Existing draft/basis regression failed' }
```

首项原生测试应先用于基线负向复现：在基线独立 checkout 仅加入新测试文件和 Program 的新参数分派，产品控件保持原字节，构建执行后保留真实失败日志及实际命令值；再对修复 checkout 执行同一测试和既有定向回归。不得给产品增加测试开关模拟缺陷，不把预期失败写成实测结果。运行结果目录由 `.artifacts/WINDOWS-WPF-EVIDENCE.txt` 给出；保存每轮源码身份、WINDOWS-WPF.log、命令及退出码，负向与修复轮分别归档。

| 验证 | 本轮状态 |
|---|---|
| 差异空白检查、XAML XML 语法、索引归档原字节核对 | PASS（静态检查） |
| WPF/验证工程构建 | NOT_RUN |
| 未修复版本 A→B Windows 原生负向复现 | NOT_RUN |
| 修复版本 SendInput 定向回归 | NOT_RUN |
| 既有草稿/焦点/基准 Conflict 定向回归 | NOT_RUN |
| 物理 PDA、真实 SDK/客户 RCS、安装/目标机/现场 | NOT_RUN |

没有复用其他提交的 PASS。必要 Windows 原生证据缺失，修复只提交草稿 PR，不合并、不关闭 PR159 对应线程。公开内容仅通用源码、合成测试和上述记录，不包含客户数据、SDK、凭据或安装介质；D/RV 保持原状态。
