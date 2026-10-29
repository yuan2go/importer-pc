# 原生管理界面与相机调试

> 2026-10-30：当前源码已由[SM-UI-DEBUG-02](sm-ui-debug-02.md)继续完善，用户要求02先实现后验证。本篇为01原来源实现与测试记录，不代表02构建、测试或现场通过；01页面采样与独立缩略图说明不再是当前调试页定义。

2026-10-29。工作包 [SM-UI-DEBUG-01](../work-packages/WP-IPC-SM-UI-DEBUG-01.md)，初始实现 `0dc1566d`，生产代码收口 `18524c58`，调试测试夹具 `1d9e44b6`，MVS探针内置依赖锁定 `9c99167b`，基于 main `f870ffc0`。本轮参考用户本地设计的层级、间距和状态区，修改实际 WPF 实现。未修改、发布或复刻原 HTML 及其资产；原工作区已有修改保留。

## 使用方式

进入管理模式后，日常侧栏及窄窗口“页面”菜单新增“诊断与恢复”“调试”。诊断页分别显示左相机、右相机和 RCS，提供对象参数与调试入口；自动重连仍是默认行为，受控重连放在折叠区。参数页保留单目录、完整候选与保存复核，增强字段标签，并直接显示 IP、物理序列号、相机编码的用途说明。

调试页选择左/右相机，查看已有连接的图像、扫描判定和原请求回执。“暂停画面”仅停止本页预览刷新，采集和业务继续。图像与扫描来自独立快照，不宣称同帧对应。页面仅收集打开期间的扫描采样，最多50条；完整记录进入“查看原作业记录”按原作业ID重新查询。每条只持有有界文字摘要和ID，不保留完整 HTTP 响应对象图。摘要截断明确标记，原件不变。

本地筛选测试使用当前 Effective 规则，不保存、不创建业务请求；参数中的 Pending 不会被误作当前运行规则。Simulation 模式可通过原 Application 准入提交模拟读码，Production 不显示此功能。没有手工拼装/发送 RCS 报文、Unknown 重发或更换原目标的入口。现行明确超时后的新扫描例外仍归业务守卫，本页不修改。

安装页增强当前步骤标识，说明内置 MVS/识别/RCS 与 D盘约束；阶段明细随实际回执更新，不制造总进度百分比。已知检查结果保留可滚动原文，同时缩小状态区域让出表单空间；执行中和结果未知时展开显示。Unknown 仍只读核对原安装，关闭不等于回滚，不替换 Windows 授权和提交前取消边界。

## 依赖与资源

- `ICameraDiagnostics` 是可选 Application 只读契约。`ParcelRuntime` 在所有者/gate 内验证管理权限、停止状态、当前配置与设备ID；返回前复查撤权和取消，并拒绝错误 epoch、未来或超过3秒的图像。
- `MvsAcquisition` 的 `IObservationPreviewSource` 从现有已验证 Mono8 托管副本提取缩略图，不增加 SDK 方法、设备连接、lease 或发送器。原生帧在识别前已归还；识别的原输入责任、停止排空和重连机制不变。
- 单相机缓存最多一张640×480灰度图，每次读取返回独立副本；最多2fps，页面750ms轮询，不超过一个并行读取。需求超过2秒未续期后在下一帧释放缓存；无后续帧时最多保留一张，停止/故障/新epoch会清除。
- 切页停止轮询、清图；切换相机清除旧选择和记录。返回页可继续使用同一运行实例。失去管理权限清除图像、采样与测试文字，迟到响应不能重画。页面数据不自动写磁盘，不增加外置扩展/独立信任要求。
- 主实现为 `StationDebugControl.cs`、`ParcelCameraDiagnostics.cs`、`MvsPreview.cs`；导航、参数和安装修改均在既有桌面项目中。现行字号与字体遵循 TYPOGRAPHY-01，无新增字体或资产依赖。

## 验证与复现

本机 Windows 11 `10.0.22631`、.NET SDK `10.0.402`、原生125%缩放。以下PASS只对应表中来源；后续文档提交不改变生产代码，未扩张为最终main或发布介质验收。

| 验证 | 来源 | 结果 |
|---|---|---|
| Sportmaster桌面与原生验证工程构建 | `18524c58` / `1d9e44b6` | 0警告、0错误 |
| 调试页原生验证 | `1d9e44b6` | 18项PASS，4张原生截图；含迟到帧、清空采样、退出管理清除、无额外RCS外呼 |
| 作业/诊断回归 | `0dc1566d` | 32项PASS，8张截图 |
| 参数与本机设置回归 | `0dc1566d` | 11项PASS，5张截图 |
| 安装、首配与维护进度 | `0dc1566d` | 141项PASS，30张截图；包括Unknown、取消边界与紧凑布局 |
| 管理模式回归 | `0dc1566d` | 57项PASS，16张截图 |
| MVS生命周期与预览 | `9c99167b` | 锁定依赖构建0警告/0错误；54项合成原生端口PASS |
| 软件识别完整链路 | `1d9e44b6` | 实际ZXingCpp、Application、SQLite、本地HTTP模拟RCS通过；校验当前epoch、过期图拒绝、三次同码独立请求、停止与原件保全 |

最终原生调试测试之前的一次失败是测试计时器与人工挂起回帧竞态：等待旧帧后，下一次合法右相机刷新可能已发生，导致“应为空图”的断言失败。`1d9e44b6` 在该确定性测试段停止计时器，保留“旧图不得回填”断言，验证右图后恢复真实计时器继续测切页；没有修改生产隔离逻辑或放宽断言。失败日志与后续PASS均在本机保留。

源码构建与合成原生验证不等于真实设备或现场验收。运行前执行 `. ./scripts/project-environment.ps1`，所有临时数据保留D盘。以下入口使用独立合成配置，不安装/替换现场软件：

```powershell
dotnet build src/Ipc.Desktop -c Release -m:1 -nr:false -p:BusinessModule=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -p:RestoreLockedMode=true
dotnet build tests/Ipc.Desktop.UiVerification -c Release -m:1 -nr:false -p:BusinessModule=Sportmaster -p:NuGetLockFilePath=packages.sportmaster.lock.json -p:RestoreLockedMode=true
$env:IPC_FROZEN_DESKTOP=(Resolve-Path src/Ipc.Desktop/bin/Release/net10.0-windows/Ipc.Desktop.dll).Path
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --camera-debug
```

其余原生入口：`--operator-workspace`、`--station-settings`、`--installer-first-run-ui`、`--management-mode`。每次目录回执见 `.artifacts/WINDOWS-WPF-EVIDENCE.txt`，截图及 `WINDOWS-WPF.log` 保留本机，不进入Git。窗口覆盖1366×768、1024×768、800×420 DIP；安装测试还包含紧凑缩放等效布局，实际 Windows DPI 与原生截图单独记录。

```powershell
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -m:1 -nr:false -p:RestoreLockedMode=true
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --mvs-self-test
dotnet build tests/Ipc.Application.Verification -c Release -m:1 -nr:false -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --mvs-software-flow
```

探针 `--mvs-self-test` 独立执行现行 MVS 的既有生命周期/停止/异常测试和本次预览测试，不改变旧 `--self-test` 断言。旧全量探针本轮在 `PolicyVerification` 的外置包 `ZXingCpp, Version=0.5.3.0` 托管闭包校验失败，尚未关闭，不能报告全量通过；SM 当前停用外置扩展，MVS专项与完整软件识别链路分别验证。跨平台 Application 测试的 WindowEventShim 仅补齐新导航编译占位，真正调试控件验证由 Windows WPF 入口承担。

尚未执行真实海康相机、客户 RCS、生产安装替换、长时间稳定运行或现场验收。没有更新 D/RV 历史状态、数据格式或发布版本；CI保持停用，原 SDK/字体/生产数据/私有设计及截图未上传。
