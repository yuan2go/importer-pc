# SM审查修复26验证

实现、测试及介质源：`2354c6e9f437365cb0ebaf5bad2b31a751ca7751`。基线main：`90ed7c859bef2e2df8c4f3df7a6a550c038a826d`。后续提交仅整理文档证据，不将PASS扩张到不同产品代码。

[RESULT.json](RESULT.json)逐套记录14套应用606项、20套原生692项，共1298项PASS；三个Release构建零警告零错误。发布兼容性17项、交付指南4项通过。[logs](logs/)为本轮原始合成测试日志，含各自SHA256；127张Windows原生截图留本机，结果文件仅记录名称和哈希。日志中相机身份、条码、请求及端点均由测试构造；不包含实际安装业务数据、客户原稿或私有截图。

|问题|证据入口|结果|
|---|---|---|
|AUD26-001 P2|native-installer-first-run-ui.log|等待授权、复制与提交阶段文案/取消守卫|
|AUD26-002 P2|release-document-tests.log、release-compatibility-tests.log及RESULT介质记录|格式12来自冻结源码；Parcel12/班次指南齐备；796文件哈希通过|
|AUD26-003 P2|native-audit-fixes-26.log|十类本地回执、完整备份、首配摘要中文；未知外部内容不改写|
|AUD26-004 P2|native-session-shift-25.log|实际AutomationPeer含标题及实时数值，空班/历史切换更新|
|AUD26-005 P2|native-ui-spec-24.log|脱敏关键词提示、记录身份跳转合同不变|
|AUD26-006 P2|native-audit-fixes-26.log|环回Unknown→保存→失败重启；旧/待配置、原请求字节与POST次数不变；授权诊断可达|
|AUD26-007 P3|native-ui-spec-24.log|原生效率按钮Invoke/键盘Enter、原统计导航、三档布局|

开发中先后发现编译包含遗漏、就绪夹具前提错误、真实维护认证窗口抢占测试焦点、LabeledBy掩盖数字及次要入口挤压紧凑格口字号。最终源修复相关代码/夹具并完整重跑，没有删断言、跳过关键检查或吞异常。较早失败日志保留本机，不作为最终通过证据。

在冻结提交的Windows工作树复验，所有命令先加载项目D盘环境：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1 -nr:false
python -m unittest discover -s scripts -p test_application_compatibility.py
python -m unittest discover -s scripts -p test_release_documents.py
# 对RESULT中每个core.flag执行：
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --session-shift-25
# 原生套件在可交互Windows会话依次运行，避免其他模态窗口抢占焦点：
$env:IPC_FROZEN_DESKTOP=(Resolve-Path src/Ipc.Desktop/bin/Release/net10.0-windows/Ipc.Desktop.dll).Path
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --audit-fixes-26
# 对RESULT中其余native.flag逐一执行同一入口。
```

新介质及单文件安装器同源生成，发布EXE环境报告启动退出0，安装器/介质SHA256见RESULT；ActualWindowsInstallation=NOT_RUN，Authenticode=NOT_SIGNED。未触发GitHub Actions。前轮90ed7c85原安装的实际升级与卸载证据独立：已保留业务数据、完整备份核验、登记退役、系统入口移除；存在重启后文件清理，尚未执行重启。未将该原安装验收借给新介质。真实MVS、客户RCS、光学/输送条件及现场物理验收均NOT_RUN。
