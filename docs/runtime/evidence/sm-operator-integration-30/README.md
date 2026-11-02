# SM操作工流程整合30验证

本页记录9ce54bab首轮冻结验证，保持原SourceCommit。随后实际升级出现授权交接未启动，用户未见管理员提示；该介质尚未实际安装。后续维护启动边界修复见[当前工作包](../../../work-packages/WP-IPC-SM-OPERATOR-INTEGRATION-30.md)，不能将本页PASS扩张到补修源或实际安装后的流程。

产品和发布介质源为`9ce54bab1f243fa2c4b0641f46336847fced8e23`，整合main `38cb7dc0e8e15f009ee88c5a35f1db53c27d5205`（PR183）。后续测试适配提交`dc81df9d`只修订日志/响应性夹具与说明；`git diff --exit-code 9ce54bab dc81df9d -- src config scripts`通过，产品、配置与出包脚本相同。

[RESULT.json](RESULT.json)记录23套应用711项、24套Windows原生WPF 1018项，共47套1729项PASS；另有17项发布兼容性和5项交付指南测试通过。三个Release构建零警告零错误。原始合成成功日志与SHA256在[logs](logs/)；原生截图留本机，只记录名称和哈希。夹具使用合成相机身份、条码、SDK及环回RCS，不包含用户截图、现场数据或客户原稿。

原生回归先在冻结源执行；日志及响应性旧场景仍要求持续同码创建新请求，与main放置合同冲突。后续测试先断言同放置不重复发送，再使用不同包裹或经连续空托盘帧形成新放置，保留原始请求/回包、订阅上限、持久化前反馈、焦点与跨位隔离断言。修改后的相关套件和剩余套件重新执行；失败形成期日志留本机，未作为通过证据。所有最终成功套件逐项见RESULT。

新介质797项文件哈希核对通过，数据库格式12、Parcel12操作指南、班次指南及扫码调试指南齐备，不交付旧DB11指南。发布EXE在本机Windows 11 x64执行环境报告退出0。单文件安装器同源生成且共享程序集核对通过；介质仅在本机，不上传SDK、字体或用户数据。

实际保留数据升级已打开官方安装器，当前等待用户处理Windows管理员授权；原登记为b16d3f93，执行前已重新核对原配置及两份生产数据库与私有基线完全相同。**实际安装后的启动、参数、投包、查询、异常、班次及备份流程仍PENDING**，不能用上述原生/合成测试代替。真实MVS相机、客户RCS、输送条件和现场验收均NOT_RUN。既有AUD26及其他历史证据保留原SourceCommit，不倒写。

复验先执行`. ./scripts/project-environment.ps1`，按RESULT逐项运行：

```powershell
dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet build src/Ipc.Desktop -c Release -p:BusinessModule=Sportmaster -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --same-placement-29
$env:IPC_FROZEN_DESKTOP=(Resolve-Path src/Ipc.Desktop/bin/Release/net10.0-windows/Ipc.Desktop.dll).Path
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --operator-corrections-29-ui
python -m unittest discover -s scripts -p test_application_compatibility.py
python -m unittest discover -s scripts -p test_release_documents.py
```

实际原生套件依次执行，避免多个窗口或授权提示抢占焦点。相机/恢复专项使用实际MVS所有者、软件解码、SQLite、进程崩溃与环回HTTP端口；仅设备SDK与上游响应为明确的合成边界。未启动原生产配置或发送真实客户业务请求。
