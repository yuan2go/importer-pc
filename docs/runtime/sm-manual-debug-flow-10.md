# 人工供包与单轮调试 · SM-MANUAL-DEBUG-FLOW-10

2026-10-30，基准main `fa39f6bc39634a06d531cd59e38f052c609562b2`，独立分支 `codex/sm-manual-debug-flow-10`。见[工作包](../work-packages/WP-IPC-SM-MANUAL-DEBUG-FLOW-10.md)。开发分支经main PR交付；不自动合并或替换本机安装。

## 修复结果

之前人工提交在Application准入、观察处理、发送三个环节仍检查相机；内置RCS准备载荷又要求相机采集身份。仅保留输入框不能解决实际发送。现在Application分别判断显式人工命令与相机来源：未配置或断线的相机不阻止键盘/PDA输入，左右位可用性为两种来源的合并结果，相机连接事实独立展示。

人工命令仍验证会话、运行/配置版本、本位输入和作业基准、安装完整性、业务能力健康、容量、暂停、调试隔离及本位原责任；只在持久人工命令执行期间建立人工观察。作业及冻结载荷保留 `OperatorInputEvidence`，不构造 `Acquisition`。RCS接受真实人工来源或原相机来源，混入相机采集身份的人工载荷不作为合法人工输入。新增可选载荷字段在null时省略，历史冻结报文不重写。普通模式仍可提交，设置/日志/调试仍仅管理模式可见。

取格口与心跳继续使用真实供包台编号及左右来源编码：`left-bcr-code`/`right-bcr-code`，即现有参数中的左右相机编码，也用于人工条码来源。物理相机可延后配置，无需编造IP或序列号；已填写有效逻辑编码的待配置相机位置可参与心跳映射，编码缺失只阻止该位人工输入。RCS未配置、离线或同位原结果未决仍给出具体原因，保留输入且不创建新请求。心跳、传输与业务接受仍为不同事实。

调试沿既有单一Application和MVS owner，触发按钮置于相机选择旁，完整可选中条码置于右侧照片上方；空白/读取/照片不可用有明确提示。触发清空旧照片、条码、请求和结果；单码原帧显示绿色定位框，自动填充冻结JSON后暂停；只有明确发送才外呼，再次扫码才开始下一轮。原隔离、场景核对、授权、原帧身份、1:1及多窗口CAS继续有效。未把附件参考图或其他帧充作扫码照片。

## 验证与边界

证据见[目录](evidence/sm-manual-debug-flow-10/README.md)。Application人工验证使用Production配置、显式合成待配置/断线相机端口、真实SQLite和内置RCS适配器连接本地合成HTTP对端；不经过真实SDK、设备或客户RCS。原生验证使用真实Windows WPF控件和同一Application，未通过网页/CDP代替WPF。

- 人工链路24项：两相机未配置/断线、键盘/PDA、前导零、左右位、缺少逻辑来源编码的精确拒绝、实际RCS接受、防重复、RCS离线、暂停/恢复、相机观察仍受健康守卫、Unknown/原责任、重启原字节保留且不重发。
- 单轮Application36项、原生调试24项：清空、同帧像素/绿框、完整码在照片上方、冻结请求、下一件不覆盖、明确发送、撤权/多窗口/原请求保留及800×420滚动操作。
- 人工原生8项：普通模式下两种无相机情况实际取得格口；底部按钮及PDA回车分别供包，保留可编辑输入和相机真实状态。
- 既有MVS软件链路15项及作业呈现30项回归；所用图像、编码及对端均为合成数据。未倒写03/06/09的历史PASS身份。

历史 `--deferred-connections` 使用旧TCP相机配置，在当前分支和未修改main基准fa39f6bc均于 `deferred mask 1: editor accepts independently deferred fields` 失败，未删除断言、吞异常或计作PASS；不为该旧夹具重新启用SM TCP方案。当前路径由上述MVS及人工专门验证覆盖。

实际SDK连接、物理PDA、客户RCS/现场及新安装验收均NOT_RUN。本轮不制作备份、不改本机业务配置或操作现场设备。验证安装包按提交后的精确源代码发布，含既有获授权本地MVS闭包；仅本地离线交付，不公开上传SDK。

```powershell
cd D:/w/ipc-debug-round-layout-10
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -m:1 -nr:false --nologo --verbosity quiet
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll D:/w/ipc-debug-round-layout-10 --manual-supply
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll D:/w/ipc-debug-round-layout-10 --camera-rounds
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll D:/w/ipc-debug-round-layout-10 --mvs-software-flow
dotnet build tests/Ipc.Desktop.UiVerification -c Release -m:1 -nr:false --nologo --verbosity quiet
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/w/ipc-debug-round-layout-10 --manual-supply-ui
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/w/ipc-debug-round-layout-10 --camera-round-ui
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/w/ipc-debug-round-layout-10 --operator-workspace
```
