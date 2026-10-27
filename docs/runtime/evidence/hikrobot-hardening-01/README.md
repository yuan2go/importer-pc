# HIKROBOT-HARDENING-01 验证

实现源码 `450bd40dddd953be2ab4d3957ad9431fe77f6f1c`，main起点 `d0a06313`；本目录是随后单独登记的证据，不把原审查或PR126证据倒写成修复通过。

Windows 11 x64 build22631 / .NET SDK10.0.402。本机为用户指定模拟环境。提交后重新构建的产物运行原独立回归 **20通过/0失败/0跳过**，原修复前为16/4；原20项及7组持续模拟源与审查时逐字节相同，哈希见[verification.json](verification.json)。SDK Probe保留原46项读码器断言、26项MVS断言和6组原生策略检查，另增16项清理/身份/诊断断言。Production身份及实际日志写入/尾部/查询/上下文/导出14项通过。

双路17Hz/30秒模拟每路510条元数据及510条合成条码结果，计数/实体和左右隔离通过；各驱动100代启停通过。这里没有真实像素传输或解码。提交后Windows HWND/Dispatcher验证：SDK参数10项、调试实时日志40项、真实Application/SQLite及loopback RCS任务链48项通过。完整Solution、SDK Probe、原生验证和两个独立项目均Release/locked构建0警告0错误。

本轮较早的5个受影响Application入口（SDK编辑、启动指导、RCS超时、延后连接、TCP失联）及Python15项兼容测试单独保留运行身份；不扩张为完整CI或现场验收。新增日志夹具首轮误写多行JSON，随后首次导出又被业务目录保护正确拒绝；两次失败日志及修正原因都登记，修正输入格式和目标位置后仍保留记录数、脱敏及禁止目录断言，没有删断言或改守卫换PASS。

实际MVS加载17导出并完成Initialize/EnumDevices/Finalize，设备0；只记本机SDK运行，不记真机帧/条码。真实识别组件、实体触发、RCS/物理格口、安装权限和24/72小时长稳未验证。两个SDK的API/依赖声明不改；Platform指纹和驱动源码变化后，部署必须从新的SourceCommit重建签名包与资格，不覆盖历史冻结包。此PR不生成安装介质或Production批准，不修改并行PR127的统一版本。

原始日志、截图、测试SQLite及SDK留在Git外D盘。仓库只登记通用代码、合成检查结果和日志SHA256。

## 复现

在修复工作树先执行项目环境脚本，再构建当前完整依赖：

```powershell
. ./scripts/project-environment.ps1
dotnet build ImporterPc.slnx -c Release -p:BusinessModule=Sportmaster -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false
dotnet build tests/Ipc.Drivers.Hikrobot.Probe -c Release -p:IpcContractPath=D:/w/ipc-hik-hardening-01/src/Ipc.Platform/bin/Release/net10.0/Ipc.Platform.dll -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --self-test
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --hikrobot-hardening
dotnet build tests/Ipc.Hikrobot.IndependentReview -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Hikrobot.IndependentReview/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll
dotnet build tests/Ipc.Hikrobot.LocalSimulation -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Hikrobot.LocalSimulation/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll
```

原生入口先按相同BusinessModule/SelfContained/locked参数构建 `tests/Ipc.Desktop.UiVerification`，分别运行 `--hikrobot-sdk-editing`、`--debug-log-live`、`--task-hierarchy-chain`。CI的SDK工作流已加入新回归/身份日志/持续模拟，远端结果按PR实时检查单列，不预填PASS。
