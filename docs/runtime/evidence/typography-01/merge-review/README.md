# PR98 合并前审查续修

2026-10-24 用户授权按依赖顺序合并97、98，不安装或发布正式介质。97已以 `c310705` 合入main；98整合该main无冲突，代码修正 `5e9f8b4`，本次锁定构建及原生验证源码 `02af32e6b188e893c92f9b33c017262a72c00970`。此前受控字体对照和CI证据保持原身份。

[审查意见](https://github.com/yuan2go/importer-pc/pull/98#discussion_r4366372678)指出紧凑日志清除详情后筛选栏未恢复。新增原生回归在旧生产代码上实际失败，见 `before.log`。`ClearDetail`现在同时复位展开状态、正文及可见性；翻页显式清除选中项，避免同一条目保留选择但详情已隐藏。查询、翻页、筛选变化和离页返回都使用一致的清除行为，保留筛选草稿、查询快照撤销及权限守卫。

最终源码锁定构建零警告/错误，原生管理模式56项PASS。现有 `--management-mode` 入口已复用日志恢复验证，包含在既有Windows CI门禁内；本机结果和程序集SHA256见 `RESULT.json` / `management-02af32e.log`。两张原生图取自同次800×420 DIP日志客户区、实际Windows11 125%缩放，显示展开详情和重新查询后恢复筛选。合成数据/真实HWND与Dispatcher，未替代现场或物理输入验收。新CI必须针对合入97后的PR98最终提交通过，最终托管状态见[PR98检查](https://github.com/yuan2go/importer-pc/pull/98/checks)。

保留中间失败：`fixture-compile-error.log` 是夹具缺少本地Dispatcher刷新辅助方法；`fixture-client-error.log` 是误用依赖宿主ClientArea的尺寸辅助方法，改为测量独立控件实际客户区；`paging-selection-failure.log` 是首轮修复后翻页仍保留相同选中对象，进一步修正页切换清选。没有删除原断言或缩减门禁。

复现：

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --management-mode
```

证据目录由 `.artifacts/WINDOWS-WPF-EVIDENCE.txt` 返回。实际安装/UAC、正式介质、目标IPC/操作者、Windows11实际100%/150%、SDK/设备/RCS及现场仍NOT_RUN。
