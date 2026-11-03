# 作业主界面信息与操作完善

2026-10-21。用户明确先完善 Sportmaster 作业主界面的信息和操作，启动修复暂不处理。本次继承 main `3b883fe` 与 PR82 源码 `58abdbc` 的中文化和参数精简；PR83 参数继承为并行任务，不改其工作区。独立分支 `codex/workstation-information`。

以 UIR-01、UIR-05 和 V2 已确认的左右两位、相机/扫描/任务三组信息为准；PARAM-UX-02 仍仅替代参数布局。原 V2 设计和实施证据保持各自身份，不重复实现响应字段或配置机制。

本轮限定：补足运行相机身份、扫描时间和操作提示，按原作业状态展示恢复操作，并增加本位诊断入口。所有事实来自原 Application 快照；设备、原请求、当前观察、格口取得与物理完成分开。换页复用原运行实例；未知请求不能因界面调整重发。

原生基线已在修改前构建并运行，使用本机 Windows、.NET SDK 10.0.402、125% 缩放及合成端口。Impeccable Operate/WPF 与 ui-ux-pro-max WPF 建议用于本次检查。真实 SDK/RCS、目标工控机和现场不在本轮合成证据内。

## 实现与核对结果

实现提交：`ebe0be76bf1bfb6cffffc91063541bbba8d9d0ee`。本轮只修改 Sportmaster 展示层、诊断导航及原生夹具。

| 原界面问题 | 本轮处理 |
|---|---|
| 相机身份藏在详情，难以确认当前左右对象 | 非极小窗口直接显示已生效配置中的相机地址（有实际声明时）和稳定设备身份；不读取待生效候选冒充运行地址 |
| 本次读取时间藏在展开区，筛选技术说明占据常用区域 | 显示本次读取时间、原始候选数；完整候选、选中码、规则和连接周期仍可展开核对。极小窗口保留详情入口 |
| 正常作业常驻不适用的恢复按钮 | 常规底栏显示查看详情与本位诊断；原作业确有 Unknown/Conflict 才出现核对原请求，原 Availability 有 ContinuePrepared 才出现继续原阶段；原权限、快照/窗口保护和提交守卫仍执行 |
| 诊断入口需要先寻找对应设备 | 本位诊断带入具体读码身份；返回对应相机参数复用原编辑器和未保存候选。诊断往返不会发送、查询、重建原业务实例或更换左右原作业 |
| 成功、处理中缺少下一步说明 | 标准布局底栏提示按当前格口投递或等待本次回应；紧凑布局将空间留给条码、格口和固定操作。格口取得仍不代表物理完成 |

方法为 Impeccable 原生 Assessment A 内联核对，网页 detector 不适用 WPF。已查看 V2 交接、字段和操作迁移，以及修改前/后的原生图片；V2 原型 `file://` 打开被浏览器协议策略拒绝，没有绕过。原型源码和 PNG 可读，原型浏览器交互走查本轮未完成。

## 运行与验证

使用仓库固定 .NET SDK 10.0.402；本机 SDK 位于 `D:/tools/dotnet/dotnet.exe`，加入 PATH 后可执行：

```powershell
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -c Release -p:BusinessModule=Sportmaster -m:1 -nr:false --no-restore
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -c Release -p:BusinessModule=Sportmaster -m:1 -nr:false --no-restore
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll D:/projects/importer-pc --task-redesign
```

最终源码的[桌面构建](evidence/workstation-information-01/workstation-desktop-build.log)和[原生验证构建](evidence/workstation-information-01/workstation-build.log)均为零警告、零错误。[原生日志](evidence/workstation-information-01/WINDOWS-WPF.log)退出码0、126条PASS、25条CAPTURE记录和29张PNG输出（含共享几何捕获），覆盖左右独立关联、处理/拒绝/Unknown/过期时整组任务失效、原责任保留、诊断不外呼、相机对象往返及未保存候选、焦点/选区、尺寸和软件暂停保护。原生几何包括1600×900、1366×768、1440×768、1100×600、911×512和800×420 DIP及不足空间保护。沿用原参数/保存/草稿断言，没有删断言或放宽几何门槛。

修改前基线和中间改动另外执行了 `--visual-productization --baseline` 与 `--visual-productization`，均退出码0；它们仅为各阶段证据，不冒充最终源码新取得的额外PASS。

本机Windows125%，物理工作区1536×816 DIP；大尺寸截图是实际WPF HWND/Dispatcher backing，不称为物理1600×900屏幕通过。最终没有重建安装包或进行已安装EXE首启验证；真实SDK/RCS、目标IPC、物理100%缩放、触控和现场验收未执行，D/RV与旧介质SourceCommit不变。

## 原生截图

- [修改前1366窗口](evidence/workstation-information-01/before-normal-1366.png)
- [已取得任务1600逻辑客户区](evidence/workstation-information-01/work-assigned-1600.png)
- [1366逻辑客户区，视图确认后软件保持暂停](evidence/workstation-information-01/work-assigned-1366.png)
- [左位结果未知、右位结果保留](evidence/workstation-information-01/work-unknown.png)
- [800×420未知原件与新拒绝观察分开](evidence/workstation-information-01/supply-station-v2-client-800x420-unknown-rejection.png)
- [本位诊断携带原设备身份](evidence/workstation-information-01/work-position-diagnostics.png)

以上只有合成条码、示例任务、参考设备和模拟端口，不代表客户接口回应或现场验收。源码PR以main为目标；PR82仍未合并，本分支继承其中文化/参数精简，审查时应将依赖与本轮增量分开。PR83参数继承不在本轮合并或修改范围。
