> 2026-10-26 批量部署复核：PR #108 的 run 40210140349 冻结介质 core 失败（Frozen media batch preflight failed）；U03/U21 重新整改，撤回本页既有“全部/独立工程完成”作为当前结论。0a3d4152 及此前 PASS/FAIL 仅保留原范围，未证明批量预检通过。修复和最新 CI 尚待验证；原页字节与哈希见 docs/archive/pre-lifecycle-batch-preflight-01/FILES.json。

# 生命周期交互审查整合与问题处置

2026-10-25，开工核对当时远端 main 与本地 HEAD 均为 `3a5b1d13ec5969213db35ed43d72a5aecd437777`，开放 PR 为 0。源码为 Sportmaster 1.12.3 / Contracts 1.18.0 / DB9。本文整合三份审查和用户追加的 UI、身份显示、参数权限、软件日志及帮助删除要求；不将审查完成记为修复完成。

实施入口为 [七包总工作包](../work-packages/WP-IPC-LIFECYCLE-UX-OPTIMIZATION-01.md)，产品约束为 [增量设计](../design/lifecycle-ux-optimization-01.md)，本版实际可用路径为 [当前操作入口](../runtime/lifecycle-ux-current-guidance-01.md)。原规划只做复核/文档；本轮七包独立工程已实施并执行定向本机测试，见末尾收口及验证报告。未执行真实安装操作。

## 三份来源与证据身份

|来源|原任务标题及定位|结果与限制|
|---|---|---|
|A|审计菜鸟供包台全生命周期交互；chat `01a1731d-1c43-78f0-a2ca-cc782a6a18fd`|UX-01～12；源码、文档和既有截图静态审查，未运行产品。最终报告保留在原聊天|
|B|审查软件全生命周期交互；chat `01a1731c-c27a-7451-a362-21c829120bc3`|[F01～09及G01](lifecycle-interaction-audit-01/findings.md)、[原五包建议](lifecycle-interaction-audit-01/remediation-work-packages.md)。两组合成运行52＋48条PASS；不是安装或现场验收|
|C|审计全生命周期交互流程；chat `01a17318-5d68-7e22-a378-149e91e05a48`|LC01～12；源码、文档、发布入口及既有截图静态审查，未运行产品。最终报告保留在原聊天|

三份均固定上述 main。B 的10个文本证据哈希与清单匹配，两个日志实际计数为52/48 PASS、0 FAIL。该结果只确认记录一致性，不等于本次重新执行了100条测试。`D:/w/ipc-lifecycle-audit-01` 属于更早审查；`D:/w/la01-r2` 是B的隔离源码工作区，不能将两者合并成同一轮实测。

A/C的原聊天不是Git文档；下表保留原编号与必要结论，关键问题另定位到源码，后续执行不依赖读取聊天。原审查目录和四份HTML/spec/ZIP附件在本次开始时未跟踪，不能以全目录暂存把无关附件纳入交付。

## 33条原问题的归并

按原表口径，33条原问题归并为21组整改候选和LC06授权门槛；B另外列出的G01为真实接口门槛。U编号只是整合追踪，不替代UX/LC/F、REQ/BR/CAPI/D/RV原编号。P/S评级定义不同，以下以实施先后和证据强弱分开处理；未确认新的P0事故。

|整合项|原编号|需完成的结果|主要源码/文档定位|归属与当前状态|
|---|---|---|---|---|
|U01|UX-05部分、LC04、F01|卸载结果区分正常保留、异常残件、重启和未完成清理|`src/Ipc.Desktop/ProductMaintenanceWindow.cs`、`src/Ipc.Platform/WindowsDeploymentMaintenance.cs`|LIA-01；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U02|UX-05部分、LC03、F02；UX-01交叉引用|同源介质核对并续办原卸载清单|`ParcelStartupServices.ResumeRemovalAsync`、`ProductMaintenanceWindow`、同源介质启动入口|LIA-01；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U03|UX-02、F03|安装结果未知后可核对固定原操作并解释下一步|`src/Ipc.Desktop/StartupSetupWindow.cs`|LIA-01；REOPENED_BATCH_PREFLIGHT，定向本机证据见下表；系统/现场限制保留|
|U04|LC05|维护结果未知不能进入通用“修正后重试”状态|`src/Ipc.Desktop/StartupWorkflows.cs` 的 Run/OperationFailed；`ParcelStartupServices.cs`未知异常|LIA-01；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U05|UX-06|备份目录变化使旧卸载计划失效，确认/提交目标一致|`ProductMaintenanceWindow.cs` 的 backupDirectory/backupFile/Read|LIA-01；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U06|UX-03、LC02、F04|参数保存、停止、重开、生效读回连贯，可稍后应用|`ConfigurationSaveReviewWindow.cs`、`MaintenancePage.xaml`、`ParcelApp.xaml.cs`|LIA-02；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U07|F08|根窗口退出显示实际影响；辅助窗口保持轻量|`SupplyStationWindow.xaml.cs`、`ShellWindow.xaml.cs`|LIA-02；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U08|F05|历史条件变化立即表明尚未应用、旧结果的固定范围|`HistoryEditing.cs`、`ParcelTargets.cs`|LIA-03；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U09|F06|按业务位名称选择并保留历史稳定ID|`HistoryPage.xaml`、`HistoryEditing.cs`、`ParcelHistoryQuery.cs`|LIA-03；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U10|F07|上一页/下一页保留同一次查询上界与原作业目标|`ParcelTargets.cs`、`HistoryPage.xaml`|LIA-03；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U11|UX-04|到期有解释，重新验证回到原页和草稿|`SupplyStationWindow.Administration.cs`、`ManagementModeMenu.cs`|LIA-02；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U12|LC11|适用的默认/经典壳及多窗口统一草稿保护|`ConfigurationCloseGuard.cs`、两种主窗关闭处理|LIA-02；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U13|UX-07|主反馈中文且可行动，技术原文保留详情|`ChineseInterfaceText.cs`、`StartupWorkflows.cs`、`ParcelApp.xaml.cs`|LIA-06；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U14|UX-08、LC09|小屏异常信息和高频命中区域可用|参数/作业Theme、主窗回执和历史布局|LIA-06；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U15|UX-09|日志日期选择、快捷范围与固定导出范围一致|`SoftwareLogPage.cs`、`DateTimeInput.cs`|LIA-06日志子项；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U16|UX-10|开始菜单入口易辨认，保留版本/原归属保护|`src/Ipc.Platform/DeploymentShortcuts.cs`|LIA-06；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U17|UX-12、LC08|登录/审阅/查询的焦点、Esc、差异定位可用键盘完成|`SupplyStationWindow.Diagnostics.cs`、`ConfigurationSaveReviewWindow.cs`|LIA-06；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U18|LC07|明确保留天数不等于自动清理，提供既有受控维护旅程|`ParameterSettingsControl.Policy.cs`、`ParcelHistoryRetention.cs`、到期维护CLI|LIA-07；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U19|LC10|备份计划可见最近真实结果、等待原因及到限停止|`ParameterSettingsControl.Policy.cs`、`ParcelScheduledBackup.cs`|LIA-07；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U20|LC12|首配显示当前位置、剩余必需项、延后连接和保存状态|`StartupWorkflows.cs`、`ParameterSettingsControl.Navigation.cs`|LIA-06；IMPLEMENTED，定向本机证据见下表；系统/现场限制保留|
|U21|UX-01/11、LC01、F09|唯一当前操作入口、同版随包指南和交付身份|README、INDEX、两份operations、startup-deployment、`scripts/release_documents.py`|LIA-04；REOPENED_BATCH_PREFLIGHT，定向本机证据见下表；系统/现场限制保留|

未给完整路径的文件位于 `src/Ipc.Application.ParcelFlow.Desktop/`、`src/Ipc.Application.Sportmaster.Desktop/` 或表中已指明的运行入口；实施前用文件名定位并读取上下文，不能把旧行号直接用于新main。

## 本轮用户追加要求的覆盖

|要求|最新源码已具备|本批收口|
|---|---|---|
|整体UI与操作简化|普通导航作业/作业记录；固定左右位；参数V3共模；无逐包确认|LIA-02/03/06补闭环、焦点、反馈与小屏，不重建UI框架|
|管理员及账号显示|`ManagementModePresentation.AccountDisplay`独立显示操作员/免登录；`ManagementModeMenu`显示普通/管理模式|LIA-02负责权限/到期，LIA-06负责各宿主/紧凑布局显示；无正常管理员账号列表|
|参数权限|`ConfigurationSubmission`等应用入口要求Management；UI隐藏不是唯一保护|LIA-02复核读、编辑、导入、草稿、恢复、快捷跳转、保存和撤权期间任务|
|软件日志查询与导出|`ParcelAdministration`、`SoftwareLogReader*`、`SoftwareLogPage`已有真实来源/固定选择/上下文/导出|LIA-06优化交互并验证轮转、撤权、取消、范围和原子发布；复用后端|
|帮助与支持完整删除|当前src未发现ProductHelp、ProductSupportWindow、IParcelSupportApplication及原支持摘要入口|LIA-04删除现行/随包文档失效指引；LIA-06/05保证入口及服务不回流。诊断、日志、备份和原作业详情保留|

## 证据修正和不应重复开发的内容

1. 本机已有同源1.12.3安装EXE，位于 `D:/deliveries/cainiao-main-20261025-3a5b1d13/`，238735051 bytes，交付回执记录SHA256 `3ed576d6fa078da0234d06a44a9f07a7a46cbe43c34bd57662c90a64dfb92608`。本轮读取文件/回执，未重新安装；该包与公开旧Release、现场版本分开。
2. B的历史输入截图证明“新条件与旧结果并存且反馈不足”；生产翻页/导出已有条件签名守卫。夹具不实现完整IParcelHistoryApplication，不能从fallback推断生产过滤失效或导出错误。
3. 仓ID、生产线ID、统一供包台编号、新请求quantity=1、新站7天、本机窗口模式、相机/RCS明确延后配置均已在PR107实现；旧请求/旧配置保留原身份。
4. 卸载完成不再弹管理认证是用户已批准的修复。续办从同源介质进入；不在已卸载结果页恢复依赖已移除服务的认证、修复按钮。
5. 原有管理员账号版说明仅适用于旧版本/显式迁移；不得据“管理员及账号显示”重建另一套正常账号权限系统。

## 外部门槛

|门槛|当前处置|所属|
|---|---|---|
|G01 原RCS结果核对|无权威Query，保留原请求与阻断。先完善既有详情中的交接；真实查询/人工裁决待合同、权限和实物责任证据|LIA-05|
|G02 管理密码遗失（LC06）|未发现当前模式的获批准恢复流程。登记决策与用户可行动指引，不通过删账、默认密码、Windows运行资格或重新首设绕过|LIA-02治理项；不会阻塞其他可独立完成工作|

## 文档处置记录

本轮在编码前归档并纠正README/INDEX/UI状态、商业操作指南、Sportmaster手册及startup-deployment的当前入口。原字节及SHA256见[归档](../archive/pre-lifecycle-ux-optimization-01/README.md)。未改原审查证据、V1/V2/V3原件、D/RV和历史发行记录。新设计是本批增量，不覆盖业务合同。

后续逐项填写实现commit、定向验证、实际界面证据和剩余外部门槛。状态至少区分OPEN、IMPLEMENTED、VERIFIED_LOCAL、BLOCKED_EXTERNAL、ACCEPTED_FIELD；后两类不得由本机合成PASS替代。本批规划与文档完成时，U01～20仍未获得本批修复资格，U21只完成前置澄清。

## 本轮工程收口（原编号保留）

七包独立工程已实现。此状态表示当前代码与对应本机验证，不重写UX/LC/F原报告，不关闭历史RV。规划总账原件及NOT_IMPLEMENTED的FILES固定于`4c42f567`和[归档](../archive/lifecycle-ux-planning-01/README.md)。实际源码冻结、失败原件、最终日志和同源候选见[验证报告](../runtime/validation/lifecycle-ux-optimization-01.md)。

|包/整合项|实际实现与证据|剩余边界|
|---|---|---|
|LIA-01 / U01～05|固定OperationId/请求指纹；原安装只读核对和已提交入口修复；卸载完整回执、目录变更失效、同源原清单续办；Application lifecycle/installer-change-guards/installer-options、库存内核和InstallerFirstRunUi|受保护UAC/系统安装卸载往返/真实重启NOT_RUN|
|LIA-02 / U06/07/11/12|同一owner停止守卫/草稿/重开；真实B读回、原请求A保留、停止失败；服务鉴权、权限到期解释/原编辑返回、独立操作员；Application management-mode及native lifecycle|G02无批准密码恢复政策；目标人因/触屏待验|
|LIA-03 / U08～10|完整查询契约153条/四页固定上界，重复码实体独立；名称↔稳定ID/停用位；更改条件和CSV范围；实际取消|真实现场规模/使用者旅程独立|
|LIA-04 / U21|唯一当前指南；旧UI/账号/支持说明退出当前随包映射，三产品映射/链接测试；实际候选与库存另记|不重新标记旧EXE/manifest、不发布安装包|
|LIA-05|只读原请求交接、事实/尝试/责任分列；模式内散列/脱敏诊断；复制无外呼；Query无合同的准确解释|G01权威RCS Query/人工处置政策继续BLOCKED_EXTERNAL|
|LIA-06 / U13～17/20|原生日期/快捷范围/固定导出；真实来源/上下文/资源释放；首配叶页进度/延后；焦点/Esc；友好快捷方式及旧归属保护；原生尺寸/DPI证据|真实键盘/手套/目标IPC人因NOT_RUN|
|LIA-07 / U18/19|正式备份状态含最近成功；到限失败保留旧集；停机独立维护预检/固定计划/完整备份/原回执核对；中断、丢终态、取消和原Unknown保护|原删除算法/索引/预算不变；大库/长期/现场不扩张|

最终独立工程冻结`0a3d4152`，PR[#108](https://github.com/yuan2go/importer-pc/pull/108)。2026-10-26八套同源发布DLL原生旅程通过，补充了只读备份投影、真实固定查询及两个最小窗口修正；源身份、失败原件及正常机器策略受阻见最终验证。U状态不扩张到G01/G02、历史RV、真实系统/现场资格。
