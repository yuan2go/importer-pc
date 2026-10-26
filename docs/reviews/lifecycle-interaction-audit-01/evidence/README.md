# 本轮证据

所有文本文件由本次独立审查生成或从本次运行目录复制。源码固定为 `3a5b1d13ec5969213db35ed43d72a5aecd437777`；未复制历史PASS作为新PASS。

|文件|用途|
|---|---|
|[environment.json](environment.json)|Windows/.NET、源码、构建类型、二进制hash、实际DPI和本轮结果|
|[build-desktop.log](build-desktop.log)、[build-ui.log](build-ui.log)|两次实际构建，均0 warning/0 error|
|[N01日志](N01-parameter-guidance-startup.log)|52条PASS；包括模型/控件断言及实际HWND键盘/显示验证，不能全数称为用户操作|
|[N02日志](N02-task-hierarchy-chain.log)|48条PASS；真实Application配合合成相机输入/loopback RCS，最后手动关闭后核对无额外请求|
|[N03过滤后的原生记录](N03-native-actions-filtered.json)|仅目标合成窗口的文字/状态；删去重复的大型ParcelHistoryItem对象转储，不更改保留的界面行|
|[local-screenshots.json](local-screenshots.json)|本机仓库外截图绝对位置/bytes/hash；截图未复制入仓库、未上传|
|[release-metadata.json](release-metadata.json)|读取时可见发布身份，元数据而非安装包验证|
|[dotnet-info.txt](dotnet-info.txt)|本轮SDK/运行时来源|
|[SHA256SUMS.txt](SHA256SUMS.txt)|本目录文本证据hash（不包含自身）|

N03原始本机记录：`D:\audits\lifecycle-audit-r2-20261025\native-actions.json`。关键截图为同目录的 `04-history-loaded.png` 与 `05-history-after-input-timeout.png`；后者显示新条码输入与旧8条列表并存。`06-history-query-empty.png`只是录制时的意图标签，**实际截图不是空结果**：该夹具缺少完整历史查询接口，点击查询走fallback。它不作为生产查询结论，保留原文件名及hash以免改写证据。

原生输入中UIA set_value一次返回超时；重读确认字段已改变，没有重复输入。自动化状态读取偶有空/滞后，已以新快照核对；不把工具超时记作产品超时。

## 复现命令及边界

在固定SHA的D盘隔离工作区执行（每个新PowerShell会话先载入环境）。下列命令运行既有合成夹具，不是Production启动指南：

```powershell
. ./scripts/project-environment.ps1
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:BusinessModule=Sportmaster --nologo
dotnet build tests/Ipc.Desktop.UiVerification --nologo
$env:IPC_FROZEN_DESKTOP = 'D:\w\la01-r2\src\Ipc.Desktop\bin\Debug\net10.0-windows\Ipc.Desktop.dll'
dotnet tests/Ipc.Desktop.UiVerification/bin/Debug/net10.0-windows/Ipc.Desktop.UiVerification.dll . --parameter-guidance-startup
dotnet tests/Ipc.Desktop.UiVerification/bin/Debug/net10.0-windows/Ipc.Desktop.UiVerification.dll . --task-hierarchy-chain --interactive
```

最后一条保留合成原生窗口供走查，关闭该窗口后完成最后断言并退出0。本轮原始测试目录见environment.json，选定截图另保存在稳定的D盘审查目录；不复制运行数据库、秘密或现场文件到Git。完整生产历史查询、实际部署退出、安装/卸载/UAC和真实设备须使用相应隔离环境另验。
