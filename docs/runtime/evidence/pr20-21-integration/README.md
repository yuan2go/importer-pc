# PR20/21 整合证据

PR20修复源码：1b6962e1909e1a33967fce8902eb001bec341d6c。固定SDK10.0.402，macOS。

命令：`dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false`，`dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --backup-health-recovery`。

[原链接负例FAIL](logs/pr20-red.log)、[中间测试清理FAIL](logs/pr20-fixture-cleanup-failure.log)、[修复PASS](logs/pr20-green.log)、[构建](logs/pr20-build.log)。所有输出仅合成测试数据，运行路径为本地临时目录，不是生产数据或客户资料。文件SHA由docs/manifest.json逐项记录。

Windows/Junction实际NOT_RUN；真实SDK/设备/WCS/PLC/现场BLOCKED。PR21和完整整合验证结果将追加，不把旧结果当新源码通过。
