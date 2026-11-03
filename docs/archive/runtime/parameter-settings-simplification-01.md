# 参数页精简增量

2026-10-21；用户在要求页面中文化后，指出界面繁琐并指定先优化参数页。远端 main 固定为 `3b883fe6a53b58b29eeca18c3719b5bfc0ee388e`，在 PR82 的 `fb2bba42614c129f7696e89b3d8593f5c65b7871` 上续修；开工工作区干净，继续同一开发分支与 PR，不覆盖其他任务。继承 PR78/79/81、PARAM-UX-02 和现有参数共模。本轮增量设计见 [PARAM-UX-02](../design/parameter-settings-experience-02.md) 顶部，原 V3 产物和中文文案证据保持原身份。

## 发现与处理

按 Impeccable Operate / WPF 原生 Assessment A 内联审查，并参考 ui-ux-pro-max 的 WPF 线性布局建议。网页检测不适用于原生验收。本轮四步为核对当前原生截图与重复信息、实施表单和外围精简、走查尺寸与保存状态、完成原生回归和截图复核。

| 原页面问题 | 本次结果 |
|---|---|
| 编辑参数时，底部左右作业摘要与保存栏叠加 | 参数工作区收起作业摘要，运行模式与软件暂停边界放在上方紧凑状态条；返回作业恢复摘要 |
| 普通字段重复显示字数或选值说明，纵向占用大 | 常规提示保留为悬停帮助、自动化帮助及原技术信息，必要用途、数值范围和错误仍就地显示；收紧字段及分组间距 |
| 相机及调度连接重复解释层级 | 删除重复页说明与服务连接标题，保留运行连接摘要、地址、诊断及共享连接说明 |
| 保存审阅仍显示已满足的暂停动作和重复成功说明 | 有效快照确认暂停后只显示状态；状态过期时保留独立暂停入口。正常审阅隐藏重复成功提示，授权、差异、原因及失败反馈保留 |

没有修改配置字段归属、完整候选、业务守卫、保存请求、CAS、Unknown 核对或设备连接生命周期。`CanConfigure=false` 仍不被简化解释为未登录。后台轮询和业务实例继续运行，返回作业不自动恢复新入场。取格口与心跳编码继续分别编辑。

## 运行与验证

使用仓库固定 .NET SDK 10.0.402，执行：

```powershell
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -c Release -p:BusinessModule=Sportmaster -m:1 -nr:false --no-restore
dotnet build tests/Ipc.Desktop.UiVerification/Ipc.Desktop.UiVerification.csproj -c Release -p:BusinessModule=Sportmaster -m:1 -nr:false --no-restore
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll <仓库绝对路径> --parameter-v3
```

两个 Release 构建均为零警告、零错误。完整原生入口退出码 0，[日志](evidence/parameter-settings-simplification-01/WINDOWS-WPF.log)有183条 PASS、46次截图；此前中文增量的177条结果不重写。此次新增6条检查覆盖参数页摘要收起及模式/安全说明保留、普通提示帮助、1366×768调度与心跳输入可见、保存审阅的已暂停动作收起、过期状态暂停入口保留，以及返回作业恢复原摘要。沿用完整入口同时检查角色目录、焦点/选区、草稿、搜索、错误定位、差异、敏感引用、保存与恢复。

已实际查看本轮[相机](evidence/parameter-settings-simplification-01/camera-1366.png)、[调度连接](evidence/parameter-settings-simplification-01/rcs-1366.png)、[保存审阅](evidence/parameter-settings-simplification-01/parameters-save-review.png)和[800×420兼容尺寸](evidence/parameter-settings-simplification-01/rcs-800.png)。常用1366×768尺寸的调度连接与心跳输入可同时显示，底部保存入口固定可达；小尺寸使用滚动。原生测试还覆盖1092×614，当前本机缩放125%。截图是 HWND/Dispatcher 合成端口运行，逻辑客户区和物理屏幕边界在日志中分别记录，不能当作目标工控机实测。

本轮按最终 Git 暂存字节核对变更清单、图片和报告链接；不重置历史公开归档的298项既有问题或扩张原首轮归档比较。未重建或发布安装包；旧1.7.0介质、SourceCommit与原D/RV状态不变。真实SDK/RCS、目标工控机、物理100%缩放、触控、完整中文输入法组合及现场验收未执行。
