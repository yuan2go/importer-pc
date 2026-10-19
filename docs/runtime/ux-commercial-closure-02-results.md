> 续修现行结果见[独立复审后三项修复](ux-commercial-rereview-03-results.md)，产品6ce1d6d/业务1.3.2。以下f1a655c身份和原参考验证保持形成时范围，不能混用为新候选证据。

# UX审查01 · WP-01～07工程交付

**复审更正：工程尚有可修复差额，未完成商用放行。** 固定f1a655c参考发行已交付；PR39在1a34a1e复审发现紧凑登录/诊断不可达、完成反馈恢复旧消息及原生验证器错误对象断言，续修见[工作包](../work-packages/WP-IPC-UX-COMMERCIAL-CLOSURE-02.md#2026-10-19-复审差额及续修设计)。下表已有证据按原源码读取，不能据其宣称WP-03/05全部闭合。本分支显式依赖PR37固定head，未合并PR37或PR38到main。缺商业组合、Windows环境、真实SDK/正式权威Unknown合同的工作仍明确受阻/待验；可选建议不算既定缺陷。

[工作包及UX/UXC映射](../work-packages/WP-IPC-UX-COMMERCIAL-CLOSURE-02.md) · [固定证据](evidence/ux-commercial-closure-02/README.md) · [参考预发布](https://github.com/yuan2go/importer-pc/releases/tag/ux-reference-f1a655c) · [产物hash](evidence/ux-commercial-closure-02/ARTIFACTS.json) · [商业输入账](ux-commercial-combination-02.json)。

## 身份和继承

|对象|实际身份|
|---|---|
|开工main|eeeed0e0700cd07a76587b2bb701e91d422b2c35，产品1.2.8/Contracts1.8.0/DB7；原PDF保持76702aec0ee5b46cf7c03df32ec4e9ed66f950055b4399fe86484dff31a056a9|
|PR37依赖|2d60fdc83a267c1ed4ad7a9f7ce200bd11255a94；其产品c384c3b及本地1.3.0候选继续按原身份读取，不冒称已发布|
|隔离分支|codex/ux-commercial-closure-02；依赖整合e42d5e7，本轮文档先行46343fa；原工作区/其他worktree及客户未跟踪材料未修改|
|产品冻结提交|f1a655c7d14e4ee012ea17694533f996a03c9246；业务1.3.1、Contracts1.9.0、DB7、配置schema1/2/3；公共API和SQL1～7不改|
|新不可变扩展|reference.parcel-capabilities2.2.1、reference.barcode1.3.1、sportmaster.rcs-task1.1.1；精确公共DLL重建/签名，不覆盖PR37旧身份|
|实际媒介|三Application portable core、Sportmaster选装portable/win-x64，共5个产品ZIP；3个扩展、identity/public TestOnly trust、完整原始验证证据ZIP及SHA256SUMS；远端下载回读另记|
|执行环境|macOS26.5.2/Darwin25.5 arm64、SDK10.0.402；真实Windows截图0张。参考输入及本机loopback，无生产设备或客户数据|

## 逐工作包结果

|工作包|已闭合|待验 / 受阻 / 可选|
|---|---|---|
|WP-01|集中输入账与一次问询；隔离参考候选选Sportmaster相机→WCS→格口、两UI共享实例|商业Application/硬件/OS/显示输入/SDK/正式合同/负载与批准人未收到，不能把建议当批准；D及原RV保持。|
|WP-02|继承UXC-01的每位最新观察/持久审计和双位原件守卫；新增慢快照、慢历史A→B、原件显式打开A→B、列表换源空选择保护。行/详情/核对及继续Owner一致；迟到原A查询结果显式标原A，不切回A；清选择清命令目标。|两布局原生回放待Windows；无码/多码/重复/旧序列/重启等参考回归PASS不等于现场。|
|WP-03|维护三个DataGrid显式可编辑，稳定设备/角色/位置仍只读；schema3完整表单、精确提供者/依赖、原配置账/CAS继承。普通操作开始前显示实际操作名、Busy/原因，冲突维护/命令禁用；Pause独立。失败保留说明，后台任务和整个动作仍归关闭排空；导航/安装刷新保草稿。|真实WPF文本/复选框BeginEdit/CommitEdit及Busy/Pause脚本已补且可编译，原生执行NOT_RUN；双窗CAS、非法数值、草稿切换、Pending/Effective及失败恢复参考已核。|
|WP-04|原端口、来源/epoch、资源、Production拒绝缺能力、原Unknown守卫继续继承；RCS明确本地lifecycle证据|真实厂商SDK/型号/固件/触发关联、正式协议/权威Query或处置依据未收到，BLOCKED_PER_CAPABILITY。QueryAsync仍返回无依据结果，不造查询、不POST假探活、不自动清Unknown。|
|WP-05|继承六态、健康范围、真实启动阶段及两布局关闭排空。普通反馈移出20DIP多项诊断滚动区；长原件摘要先显示待核对状态，原码/ID仍在详情。经典紧凑诊断被隐藏的实现已确认为复审缺陷；须恢复同一可展开入口。原生验证器以真实ClientArea测试两布局支持区内容、长码完整选择、旧Unknown+新拒收、固定动作和低于最低区Pause/恢复不Resume。|800×420是沿既有框架重排目标；原生DPI/键盘/HID/焦点/触摸、真实像素可读性NOT_RUN。UX-08启动为继承改进；UX-09仅针对已明确同步服务调用路径，不宣称目标机时延/班次达标。|
|WP-06|固定Git源码实际发布/ZIP完整性/精确依赖/同版解包运行；3核心主链、原件重启、安装/配置/激活/启动失败保旧/安全退休及签名RCS原始字节/回执/重启不重发参考PASS。运行/故障/备份恢复/安装更新SOP沿现有设施。|Windows普通账户/UAC/ACL、安装/更新中断恢复/卸载保数据、目标负载与代表班次NOT_RUN；真实组合及商用BLOCKED。参考媒介不是已在目标机安装。|
|WP-07|继承分页/结构详情/完整原JSON、精确包选择/原操作ID恢复；新增查询完成时间及查询时列表/可更新详情/页外目标说明。|新筛选/导出/安装日志枚举未采纳，保持可选，不自动升级缺陷。|

关键文件：[选择与反馈](../../src/Ipc.Application.ParcelFlow.Desktop/ParcelViewModel.cs)、[历史及原件目标](../../src/Ipc.Application.ParcelFlow.Desktop/ParcelTargets.cs)、[结构化表单](../../src/Ipc.Application.ParcelFlow.Desktop/MaintenancePage.xaml)、[alternate显示与选择](../../src/Ipc.Application.Sportmaster.Desktop/SupplyStationViewModel.cs)、[原生验证](../../tests/Ipc.Desktop.UiVerification/UxCommercialClosure.cs)。基础/Application/驱动/组件职责不变；DirectPlc完整UI仍暂缓。

## 验证及负证据

9组定向入口覆盖选择/反馈、输入持久化与重启、防重、配置/CAS、快照失效、双布局/共享实例、关闭排空、版本、资源和原件恢复P2。最终对应入口均有PASS，具体命令和原输出在证据索引；不是全量重跑。3业务WPF及原生验证工程交叉构建均0警告/0错误，Python兼容17项与随包指南2项通过。实际5ZIP CRC/文件hash和win-x64 PE身份通过；3portable解包主链和选装旅程通过。

保留两项负证据：第一，继承PR37的原选择交错真实RED，修后相同行/详情/Owner断言转绿；第二，配置首轮健康脉冲在途时被“Pause and authenticate maintenance before configuration”守卫提前拒绝，尚未进入预期CAS断言。夹具只对该精确前置拒绝有界重试、打印回执并保留原草稿Revision；最终仍严格要求Configuration revision conflict与原Pending未变，不改业务守卫或放宽断言。原失败未倒写。

资料校验基线267项继承错误。产品冻结快照曾新增1项本地命令路径元数据；最终公开JSON以明确路径标签替换并记录原/公开SHA，原始字节完整保存在独立raw-verification-evidence.zip，未加资料例外。最终882bd31干净归档检查267项、对基线新增0项，整体仍FAIL，不把原UI资产错误改成PASS。

实际ZIP/随包指南固定来自f1a655c；后继提交只更新状态、证据及版本勘误。[输入字节比较](evidence/ux-commercial-closure-02/source-parity.json)列出唯一当前指南差额；候选VERSIONS.md末段遗留PR37的1.3.0说明，其首段和实际清单均为1.3.1。[勘误](evidence/ux-commercial-closure-02/VERSIONS-ERRATA.md)独立追加，原ZIP不覆盖，不能说最终分支所有文档都与候选逐字节相同。

## 运行及剩余输入

从参考预发布下载所选ZIP及SHA256SUMS，核外层hash后解包到新目录；portable执行：

```sh
dotnet /unpacked/app/Ipc.Assembly.dll --config /unpacked/app/config --data-root /new-isolated-reference-root
```

Windows按候选WINDOWS-APPLICATION.md、SPORTMASTER-UI.md及当前[原生补验](validation/sportmaster-alternate-ui-windows.md)，以指定普通账户和受保护目录执行application.ps1；只在授权实验室使用Simulation/reference设备。原未决请求必须匹配原发布/合同/报文/目标核对，不因新包或新UI重发。软件Pause不是安全急停。

还需商业组合输入账中的型号/SDK/权限、正式协议与Unknown依据、Windows/显示/输入/硬件及目标负载/班次/RPO/RTO。取得后只推进对应真实驱动/集成及Windows/现场验收，不要求重建框架或重做已固定参考证据。
