# 2026-10-20 当前产物上传快照

用户最终指令为“不要管其他任务，只上传”；本次只提交、推送当前文件快照，不等待其他任务，不执行测试、构建、打包、运行或合并，不改变 main 或仓库可见性。报价表与受限原稿、秘密及生产数据不上传。

此快照不代表并行任务已经完成，新增或修改代码均为 NOT_RUN_BY_USER_REQUEST。历史日志、原 PASS/失败、D/RV 与冻结介质仅适用于原版本，不将旧介质改标为本次代码。manifest 未作为本轮验收重新生成。后续完成状态以所属任务的真实报告为准。

来源工作树：`commercial`；父提交：`ee769f0e8f2b7cbc45515fc552175cf4679f03cb`；统计基线 main：`c445b0ec32a3db44f00a1968f3c501aa36eb786f`。采用独立 Git index 和上传分支，避免改动原任务工作树或分支。

纳入当前差异：

- `src/Ipc.Application.ParcelFlow.Desktop/StartupConfigurationDraft.cs`
- `src/Ipc.Application.ParcelFlow/ParcelLegacyReceiptRecovery.cs`
- `src/Ipc.Application.ParcelFlow/ParcelLegacyRecovery.cs`
- `src/Ipc.Application.ParcelFlow/ParcelMachineOrigin.cs`
- `src/Ipc.Application.ParcelFlow/ParcelMaintenance.cs`
- `src/Ipc.Application.ParcelFlow/ParcelProductMaintenance.cs`
- `src/Ipc.Application.ParcelFlow/ParcelRestoreHandoff.cs`
- `src/Ipc.Application.ParcelFlow/ParcelRuntime.cs`
- `src/Ipc.Application.ParcelFlow/ParcelStartupMaintenance.cs`
- `src/Ipc.Application.ParcelFlow/RecoveryFiles.cs`
- `src/Ipc.Desktop/ParcelApp.xaml.cs`
- `src/Ipc.Desktop/StartupWorkflows.cs`
- `src/Ipc.Platform/WindowsDeploymentMaintenance.cs`
- `tests/Ipc.Application.Verification/CommercialProductization.cs`
