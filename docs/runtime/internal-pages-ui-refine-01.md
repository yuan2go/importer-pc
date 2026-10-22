# 内部页面风格与参数逐页校验

2026-10-23，执行基线 main `dd1dfefc9e829f557a1efdf6f7ad7532fec4ef81`，开发分支 `codex/internal-pages-ui-refine-01`。[工作包](../work-packages/WP-IPC-INTERNAL-PAGES-UI-REFINE-01.md) · [原生验证](validation/internal-pages-ui-refine-01.md) · [截图与文件清单](evidence/internal-pages-ui-refine-01/README.md)。用户提供的 HTML/ZIP 保持未跟踪，不纳入交付或公开证据。没有推送、发布、自动安装或连接生产设备。

## 实际变化和源码映射

| 功能 | 实现文件（仓库根相对路径） | 实际行为 |
| --- | --- | --- |
| 每个参数页 | `src/Ipc.Application.ParcelFlow.Desktop/ParameterSettingsControl.Navigation.cs`、`ParameterSettingsControl.cs`、`ParameterSettingsControl.Search.cs` | 下一页、目录前跳、搜索定位共用前进检查；逐个检查途经页面，在首个错误处停止。返回直接切换，保留输入；完整检查的纠错定位可直接到对应实例字段。异步结果在模型/输入变化后失效；旧错误定位不会拉回已返回的页面。 |
| 真实页内校验 | `src/Ipc.Application.ParcelFlow.Desktop/ParameterEditorModel.cs` | 按当前页真实字段筛选既有 schema/IP/范围/条件规则，条码仍调用既有离线、有时限的筛选契约。全空白可选规则维持原无筛选语义；指定格口 false/true、左右独立和跨字段规则不变。 |
| 首配与日常入口 | `src/Ipc.Desktop/StartupWorkflows.cs`、`src/Ipc.Application.ParcelFlow.Desktop/MaintenancePage.xaml(.cs)` | 首配按钮显示实际下一具体页；日常参数也有上一页/下一页。正式保存仍完整检查、实际权限、提交和权威回读；不因页内通过而保存、应用或启动。 |
| 普通参数错误 | `src/Ipc.Application.ParcelFlow.Desktop/ChineseInterfaceText.cs`、`ParameterSettingsControl.cs` | 缺失/失败提示用中文标签。原字段键、异常和实例身份保留在原折叠诊断中；不改日志/配置/请求原件。 |
| 作业记录 | `src/Ipc.Application.ParcelFlow.Desktop/HistoryPage.xaml(.cs)`、`HistoryEditing.cs` | 简短对齐筛选、蓝色查询、真实分页/导出、完整详情；小窗口将详情折叠，筛选局部滚动，保留结果区。清除筛选仅清输入，旧结果维持原固定范围，必须重新查询才能导出改变后的筛选。 |
| 日志查询 | `src/Ipc.Application.ParcelFlow.Desktop/SoftwareLogPage.cs`、`QueryWorkspaceTheme.xaml` | 关键词/级别有标签，时间/来源/上下文/导出可展开。查询/翻页/导出状态真实绑定；忙时才显示取消；翻页成功才提交偏移。更改筛选使旧快照失效。空结果/失败/取消分别反馈，失败细节收起；列表投影不改原 JSON、上下文或导出。关键词 Enter 查询、记录 Enter 读上下文。 |
| 工作台和诊断 | `src/Ipc.Application.Sportmaster.Desktop/SupplyStationTheme.xaml`、`SupplyStationWindow.xaml`、`SupplyStationViewModel.cs`、`SupplyStationWindow.Diagnostics.cs` | 白底、轻分隔、中性次按钮和导航、统一字号；保留大条码/格口、语义颜色及相机/扫描/任务三组。查询标题准确，诊断值有标签，长运行证据可展开，动作保留真实目标和服务守卫。 |

## 边界

同一 Application 和完整候选继续复用；换页不重建连接、作业或发送器。查询只读取原快照，列表/详情/命令仍按原作业身份。未知原请求不重发，日志导出保持原记录。现场协议、配置含义、编号持久化、操作员账号和 Windows UAC 均未改变。

页内通过仅证明所检查输入符合既有本地规则；不能证明设备联网、安装完成、正式保存或现场资格。退出/到期后原服务权限拦截继续有效，不能用 UI 状态替代。没有新增用户、续期、重试安装或现场能力。

旧当前指引原字节与哈希见[归档](../archive/pre-internal-pages-ui-refine-01/README.md)，历史 D/RV、原图、PR 和发布保持原 SourceCommit 与适用范围。
