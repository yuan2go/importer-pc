# 固定源码证据索引

以下链接固定到 `3a5b1d13ec5969213db35ed43d72a5aecd437777`。行号是本次读取版本，后续实现应重新定位。源码证明实现存在/不存在于所检索入口，不能证明安装、硬件或用户实测通过。

|证据ID|源位置与核验点|
|---|---|
|S01|[StartupSetupWindow.cs:109](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Desktop/StartupSetupWindow.cs#L109)：未知终态隐藏Next，119–129只保留记录指导；207–223仅打开目录；262阻止重放；281–300实际安装选择和调用|
|S02|[ParcelStartupServices.cs:100](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Assembly/ParcelStartupServices.cs#L100)：ResumeRemovalAsync及237附近固定原介质/登记清理；353–406提升、等待、可信回执和Unknown分支；全src检索没有Desktop的ResumeRemovalAsync调用|
|S03|[ProductMaintenanceWindow.cs:18](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Desktop/ProductMaintenanceWindow.cs#L18)：18/37丢弃Status/Retained；47退役转结果；61–70终态只有关闭；31–36原责任检查、备份及确认|
|S04|[WindowsDeploymentMaintenance.cs:9](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Platform/WindowsDeploymentMaintenance.cs#L9)：完整卸载回执；15–29系统修改/卸载命令；44–77固定清单、先退役后清理、三类结果；79–119保留非清单文件/重启删除|
|S05|[ConfigurationSaveReviewWindow.cs:37](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Application.ParcelFlow.Desktop/ConfigurationSaveReviewWindow.cs#L37)：保存/核对/返回；49待受控重启；117–129保存状态区分。配套 ConfigurationSubmission.cs、ConfigurationReviewPresentation.cs、MaintenancePage.xaml:74–77 为唯一候选/读回/Pending与运行输入|
|S06|[SupplyStationWindow.xaml.cs:130](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Application.Sportmaster.Desktop/SupplyStationWindow.xaml.cs#L130)：关闭等待、草稿守卫、root StopOwner、子窗关闭、停止失败保留资源；SupplyStationWindow.Administration.cs 为模式退出和页面切换；ConfigurationCloseGuard.cs 为加密保留/放弃/继续编辑|
|S07|[HistoryEditing.cs:9](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Application.ParcelFlow.Desktop/HistoryEditing.cs#L9)：过滤字段没有统一失效通知；清空有专门提示；29–39原样Position；44–56导出固定选择、签名守卫和取消|
|S08|[ParcelTargets.cs:71](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Application.ParcelFlow.Desktop/ParcelTargets.cs#L71)：单一next游标、状态文案、固定范围、请求过期丢弃、fallback历史；120开始按原选中作业核对|
|S09|[HistoryPage.xaml:5](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Application.ParcelFlow.Desktop/HistoryPage.xaml#L5)：查询/清空/下一页/导出；9原始投递位输入；19状态；23投递位直接绑定PositionId；HistoryPage.xaml.cs 为文件选择与事件入口|
|S10|[ParcelHistoryQuery.cs:34](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Application.ParcelFlow/ParcelHistoryQuery.cs#L34)：条件校验/SQL精确匹配/固定上界/独立只读连接/完整性核验；80以后导出新文件、partial、结果回执和预算。SoftwareLogPage.cs 则另有条件失效、前后翻页、上下文和取消|
|S11|[SportmasterRcsTaskService.cs:71](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Integrations.SportmasterRcs/SportmasterRcsTaskService.cs#L71)：QueryAsync返回null，签名模块声明不支持查询；不把POST代作查询。Sportmaster/Desktop的ViewModel及ParcelCommands等保留Unknown/恢复/发送资格|
|S12|[StartupWorkflows.cs:225](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/src/Ipc.Desktop/StartupWorkflows.cs#L225)：离线修复、完整备份、恢复接管、保存读回、首配完成；后续同文件含恢复预检、管理模式和草稿。ParcelApp.xaml.cs:37起先选入口/分类再启动，StopAsync持有释放责任|
|S13|[commercial-productization-01-operations.md:7](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/docs/runtime/commercial-productization-01-operations.md#L7)：版本、16–18安装步骤、75继续清理的声称；INDEX:30/32多个当前版本；startup-deployment-02为旧安装步骤来源|
|S14|[ParameterGuidanceStartup.cs](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/tests/Ipc.Desktop.UiVerification/ParameterGuidanceStartup.cs) 与 [TaskHierarchyChain.cs](https://github.com/yuan2go/importer-pc/blob/3a5b1d13ec5969213db35ed43d72a5aecd437777/tests/Ipc.Desktop.UiVerification/TaskHierarchyChain.cs)：本轮实际重跑的既有夹具。后者HierarchyCommandTransport/IManagementParcelFixture不提供IParcelHistoryApplication，原生扩展走查不能用来判定生产筛选结果|

本轮新问题使用 LIA01-Fxx 标识，不复用或覆盖 REQ/BR/CAPI/D/RV。架构关联：F03/F04涉及F-09/F-10；F08涉及F-03；G01延续原外部合同及RV-06/07等风险。关联只表示需要共同验收，不表示原历史问题关闭。
