# Sportmaster默认供包台信息层级与操作反馈

> 2026-10-24 深度复核：本轮继承 PR97，整合最新 main `c5ac51d`；新增真实 Application → 冻结 HTTP 请求 → 原回包 → 原生画面的独立验证。当前源码 `cf2ab07`、证据表、反证和复现步骤见[深度复核](evidence/task-content-hierarchy-02/deep-review/README.md)。下述 `1663033`、371项及旧图保持上一轮身份，不能代替新链路或使用者证据。

2026-10-24。开工main `c91559f6f981a9f2c9f89bd5afc271089f0e9d8e`，当时无开放PR；继承PR93～96。最终生产/夹具源码`166303306e73e2d970ab44231ec26d4792a943a6`，包含首次CI发现的紧凑视口续修；初次`c1e7950`结果保留来源。最终结果见[CI续修清单](evidence/task-content-hierarchy-02/CI-FIX.json)，[初次清单](evidence/task-content-hierarchy-02/RESULT.json)不覆盖最终源码。工作包及替代关系见[HIERARCHY-02](../work-packages/WP-IPC-TASK-CONTENT-HIERARCHY-02.md)，交付[PR97](https://github.com/yuan2go/importer-pc/pull/97)。

## 实现与使用

默认供包台保留固定左右相机、扫描与任务内容。每位从上到下为紧凑设备标题、有效格口或异常结果、对应条码、两列任务字段、扫描/原件详情。正常设备与共享RCS技术状态在按需详情；等待空位不显示四个空任务字段。RCS有效回执允许缺省的新增波次和返回数量显示“回执未包含”，不借请求数量补值；必需的任务号、商品编码或格口缺失不能建立接受事实，仍为Unknown。其他参考适配器的缺项展示不代表RCS生产合同允许缺少必需字段。

无效新输入后，Application不再把已结束原作业当作本次当前作业，历史仍保留原请求。此时指引为“检查本次输入及准入条件；历史结果见作业记录”，不会指向禁用的“查看详情”。若原件仍未决，指引继续指向该原件详情。筛选拒绝、Unknown、回执冲突和命令守卫的已知内部原因使用中文展示；新输入的未准入结果与当前原作业待核对状态分别表达，原始诊断和报文不改写。

Unknown、Rejected、Conflict、等待回应、新输入未准入和快照失效使用同一事实投影；原因、下一步在普通作业视图可见。新输入未准入隐藏旧格口和任务矩阵，原作业责任仍可查；单侧异常不改变另一位资格或整站暂停。长条码/格口可以选择复制，受限高度内部滚动；800×420时长格口限高64 DIP以保留至少一行条码，固定动作保持可达。取得格口不表示物理投递完成。

`TaskContextPresentation`维护本窗反馈归属，不是业务状态机。全局暂停/恢复的当前回执有独立紧凑区域，日志页不会隐藏，普通页内读取不会覆盖；下一次全局意图替代旧回执，迟到的旧命令结果不能覆盖较新的独立暂停。可手动收起回执；运行实例变更后旧回执退出。已收到`Persisted`的成功回执在发起页显示后，离开该页即退场，避免旧成功回执挤占查询列表；拒绝、连续冲突、未知和待核对结果继续跨页保留。页内反馈绑定导航代次，页面切换后的迟到结果不进入当前页。原Application守卫、暂停独立通道及三次冲突上限保持。

左右相机与供包台标识页缩紧字段间距，将连接/编码说明归入现有“参数说明与诊断详情”；字段标签、必填星号、数值单位/约束和错误保持就近。心跳供包台编码明确命名；取格口和心跳编码仍是独立字段，共用首配/日常模型、完整候选和统一保存，不增加独立配置账。

记录、日志列表通过`LocalTimestampConverter`显示现场Windows本地时间。原业务JSON、日志原文和导出保留原时间与offset，日志详情另列原始ISO时间。日志高级条件的入口和“当前查询（本地）”范围在折叠时仍可见；改筛选立即标为待查询，不将新输入冒充旧查询范围。分页、上下文、导出继续沿原服务端Selection快照。

实现路径：`SupplyStationPosition.xaml`、`SupplyStationViewModel.cs`、两套窗口及`ParcelViewModel/TaskContextPresentation`、`ParameterSettingsControl`、`HistoryPage.xaml`、`SoftwareLogPage.cs`。没有业务协议、配置schema、依赖、部署或设备接入变更；运行/发布入口沿[既有Windows交付](windows-application-delivery.md)。

## 验证与边界

[原生前后对照](evidence/task-content-hierarchy-02/README.md)和[逐项结果](evidence/task-content-hierarchy-02/RESULT.json)绑定实际源码。前后是同尺寸、同合成场景的真实WPF HWND/Dispatcher渲染；参数对照使用同一日常共用表单、实际TCP/RCS描述和合成候选，未保存、连接或修改生产配置。不是HTML或物理桌面截屏。

初次本机232项PASS分属`c1e7950`与`4c54d85`；后续CI暴露查询视口和旧几何断言问题，[续修记录](evidence/task-content-hierarchy-02/ci-fix/README.md)保留失败及裁决。最终`1663033`本机6组371项PASS：task-hierarchy 151、visual-productization 115、management-mode 27、internal-pages 25、parameter-v3-only 23、parameter-shell-polish 30。原参数精修9项及对应表单图保持`4c54d85`身份，不与371重复求和。

V3验证真实Application保存后Pending/Effective分离和三尺寸整窗保存入口；不把模拟保存扩大到生产配置。最终两项构建0警告/0错误；本地Sportmaster win-x64 framework-dependent publish成功，显式等待进程的`--environment-report`退出0并产生有效JSON。这不是完整安装/受保护服务启动冒烟。完整parameter-v3链在本机显示偏好写入处受既有受保护D盘用户目录ACL阻断，未降低断言或修改权限，未标PASS；完整冻结参数链由PR Windows CI独立验证。CI保留全部既有门禁，另将task-hierarchy纳入同一冻结Windows介质回归并保留失败日志/截图，CI以PR实际检查结果为准。

旧TaskHierarchy异常夹具只改Wcs.State，不能作为完整异常证据。本包同步原请求回执、作业状态/原因/格口及位置准入/完成事实，另一位保持原快照。Unknown无成功回执，Rejected有明确拒绝，Conflict保留矛盾原回执；不修改生产规则去迁就夹具。

迭代修正：原生截图复核发现最小尺寸长格口挤掉条码，已限高；参数逐字段折叠帮助造成重复入口，改为既有统一说明。夹具曾因轮询覆盖瞬时手工失效标记及投影刷新代次竞争失败，改为持续注入SnapshotAsync读取失败、按真实显示事实有界等待，保留原断言。早期编译缺少窗口VM名称/可空标注和独立窗口NameScope已修正，不以这些失败宣称业务规则错误。

目标工控机、其他Windows DPI、物理键鼠/扫码、真实双相机/SDK/RCS、安装/升级/UAC和现场验收均NOT_RUN；须取得目标环境、真实配置/合同和设备操作授权。当前只证明本机合成软件行为，不改变D/RV、历史SourceCommit或旧PASS范围。未自动合并、安装或发布正式介质。

## 复现

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --task-hierarchy
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --task-hierarchy-parameters
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --parameter-shell-polish
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --internal-pages
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --parameter-v3-only
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --visual-productization
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --management-mode
```

输出目录由`.artifacts/WINDOWS-WPF-EVIDENCE.txt`记录，隔离数据/临时文件均在D盘。before参数在main归档源码的隔离目录中仅加入本轮参数采集夹具和Program路由，以`--baseline`运行；生产源码保持main原字节。正式测试不使用baseline跳过新断言。
