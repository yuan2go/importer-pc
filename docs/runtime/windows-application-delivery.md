> 当前源码：Sportmaster1.12.0 / DirectPlc及WcsToPlc1.6.0 / Application.Contracts1.17.0 / Parcel DB9。普通权限、D盘受保护部署和管理模式继续适用；输入接收、停止、保留维护及资格边界见[长稳运行指南](win11-24x7-stability-remediation-01.md)。旧COMM版本与验收保留在原结果及[本次原文归档](../archive/pre-win11-24x7-stability-remediation-01/README.md)。

> Sportmaster本轮缺省界面为supply-station，保留显式 `--ui classic`；展示设置提供布局/辅助窗/来源诊断，其他Application缺省不变。当前运行与本轮Windows合成验证见[视觉产品化](sportmaster-visual-productization-01.md)，原发行证据保持其SourceCommit。


> 现行商业化补差范围见[完整工作包](../work-packages/WP-IPC-COMMERCIAL-PRODUCTIZATION-01.md)及[基线核对](../reviews/commercial-productization-01-baseline.md)。本页保留稳定操作；商业化增量按上方当前产品指南读取。正常安装/首次启动/独立维护以[原生启动部署指南](startup-deployment-02.md)为准，旧媒介按其固定源码和随包说明使用；源码与介质身份见[发布记录](publication-deployment-20261019.md)。历史任务/测试/候选保持原身份，Windows/真实组合未验部分不变。
> COMM-00～10 当前操作见[产品操作指南](commercial-productization-01-operations.md)，实现与冻结验证见[实施结果](commercial-productization-01-results.md)。本页的历史版本/原验收不改写；当前边界仍受四层职责、唯一权威、指定账户和原请求守卫约束。

# 三个Application的Windows候选与维护路径

历史合并记录（2026-10-19）：当时源码同时包含PR44部署/修复向导及PR45全部运行入口门禁，共用最终MachineRuntimePolicy：指定未提权账户允许deny-only管理员组身份，已启用管理员权限/服务账户/非指定SID继续拒绝。此次发布的e79a8bf与16ecb57候选分别保留原范围，尚无包含两项增量的新组合媒介；详细身份与下载见[本次不测试发布记录](publication-deployment-20261019.md)。

当前COMM增量、完整冻结介质和实际验证见顶部实施结果；下文继承基础操作与历史交付语境。旧媒介按固定 SourceCommit 和随包指南读取，不能把旧 ZIP 改标为本包已完成版本。SourceCommit、ApplicationId、业务/ABI/DB/config 版本分别读取 ASSEMBLY.json、app/application-release.json 和 DEPENDENCIES.json；Supply 仍使用自己的 Storage/操作契约。候选工程的 Windows 参考范围和客户实际 OS/SDK/设备资格分别核对；正常原生流程不依赖 PowerShell，显式高级脚本依赖在对应章节列出。Windows 实际验收及真实组合未完成，不得把操作步骤写成已执行结果。

## 媒介与依赖诊断

启动/部署的原有基础见[原生安装与启动修复指南](startup-deployment-02.md)，商业化增量以本页顶部产品操作指南为准。固定旧候选、旧验收记录和旧SourceCommit不重写。下面脚本为显式维护兼容入口，正常安装和启动不依赖PowerShell。

## 首次启动与自动配置

Sportmaster 当前主入口为同源[单 EXE 安装程序](single-exe-installer-01.md)，准备后进入同一原生向导。高级兼容路径为完整解压可信Windows媒介后双击`Setup.cmd`；首次从未安装媒介双击`application.cmd`或`desktop/Ipc.Desktop.exe`也会进入原生四步向导。选择已有运行账户、原数据根、配置来源及可选独立信任/离线包，检查摘要后授权一次UAC。安装自动创建受保护策略、版本程序目录、新数据根和开始菜单入口；日常进程保持未提权。默认原用户及HKLM登记的D盘数据根，升级自动带入既有位置；不会以新seed覆盖数据账。已知机级政策/其他账户材料保持原值，身份冲突明确失败，不能删lease绕过。

开始菜单“运行”读取受保护的同版本部署位置；“启动修复”无需业务Start成功，提供配置/参数表单、待生效保存、摘要导出与完整备份隔离恢复。启动失败页也可进入维护、安装修复或重试。原Stop/Dispose未完成时不能创建第二个实例。详情及失败保旧、种子修复、精确扩展/Production边界见[随包向导指南](startup-deployment-02.md)。Windows原生交互、UAC、ACL、快捷方式与升级中断仍NOT_RUN。

指定账户属于Administrators组但仅有UAC deny-only成员身份时允许普通运行；真正提升的日常进程、SYSTEM/服务账户和非指定SID继续拒绝。业务/API身份守卫仍只读，不在领域API中提权。本项目程序与机级配置使用管理员登记并保护ACL的D盘根，数据独立；并排程序修复不回滚数据库。只读基本配置不要求可选扩展库存就绪，确实绑定的扩展缺失仍拒绝校验和启动。

### 可选脚本维护

Windows候选自包含.NET/WPF；原生Setup、Maintenance和普通EXE不要求PowerShell。显式脚本命令仍需PowerShell7；脚本按SHA256SUMS验证媒介，并通过实际应用只读校验`deployment-location.json`。源码固定SDK10.0.402和锁定依赖不等于客户设备/OS生命周期或SDK资格。脚本与GUI统一[Windows功能兼容门禁](windows-client-compatibility-01.md)：原生x64 OS/进程、Windows内核10.0 build14393起；Win10 19045和Win11 22000起为优先目标。本包24×7资格另须目标Windows11、SDK及现场实测，不以功能可启动代替资格。

```powershell
$release = 'D:\ImporterPc\releases\APPLICATION-SOURCE-UNIQUE'
& "$release\application.ps1" -Action Verify
& "$release\application.ps1" -Action Install # 打开原生安装向导
& "$release\application.ps1" -Action Diagnose -DataRoot 'D:\ImporterPcData'
& "$release\application.ps1" -Action Run # 读取同版本部署位置；直接EXE也相同
& "$release\application.ps1" -Action Console
```

手工protect-release/provision-runtime/ProvisionData/provision-extensions保留为明确维护兼容工具；正常部署由向导完成，不要求用户执行它们。脚本高级参数`-Configuration`和`-DataRoot`仍可显式指向原位置；配置账Effective/Pending是唯一业务权威。独立信任轮换及旧库迁移须走各自现行守卫，不自动改政策、清责任或生成测试信任。

## 作业、故障与诊断

初次运行先读快照：当前位/reader/最新观察及分类、Admission/Operation、重量来源、主链与附加责任、配置确认、容量。无码等失败观察不沿用旧终结格口，原未决责任仍须处理；RCS仅本地生命周期证据时明确上游未确认。新实体依赖当前来源与全部业务守卫；相同码不证明同实体。PLC接受不等于物理完成；软件暂停不是安全急停。快照异常/挂起/乱序/实例变化时旧事实失效，新快照不会Resume或重发。故障和现场实物处置由授权人员执行。

|故障|实际操作与清责依据|
|---|---|
|条码/测量身份矛盾|保留原实体/重量，查看具体冲突ID/原Operation/受影响位；Pause、维护认证、核对同一实物后AcknowledgeIdentityConflict。此命令不清RecoveryRequired、Unknown、原raw/图像/包责任；独立无依赖位可按原守卫继续|
|WaitingWeight/超期|只关联原Admission的有效测量；重启保持隔离，不首次自动发送。无外呼且守卫允许才检查移除并AbandonUnsubmitted，不改旧重量或延长原期限|
|已发Unknown/缺原回执|保持原Request/目标/合同/报文，只执行真实支持的原Query；查不到仍Unknown。RCS没有权威Query/人工处置合同，不伪造成功、不POST假查询；保持实物和原件责任并联系协议责任方|
|RCS编号耗尽/恢复后范围退役|原实体无外呼时先检查处置，停止来源根，经独立分配新不重叠范围、CAS/受控重启后接新件。旧请求编号/原报文不变；不能删除Owner/范围/恢复账绕过|
|低磁盘/共享存储失败|停收相关共享业务，保全DB/WAL、原件、配置和精确发布；不删历史或原包“腾空间继续”。用完整备份/容量预检迁至足够容量的新卷，旧根停止后启用新根|
|安装/启动/配置失败|记录原扩展操作ID或Activation尝试。原操作恢复/修复同一包；旧Effective及Pending/失败回执保持。修正候选后显式restage，不自动重试或覆盖原账|
|停止/重连未结束|来源未真正Stop/Dispose时仍持租约，不能另起新所有者。迟到旧epoch不能成为当前许可；等待原任务终结并导出故障，不杀线程或释放在用原生资源|

导出前停止客户端，Inspect/Export持原应用所有权；返回代码/原因可区分占用、原件/配置错误及未来reader。诊断ZIP包含maintenance.json中的源DB/WAL/config指纹、版本、责任/容量摘要；不带数据库、完整业务payload或凭据。完整原始证据另由获授权维护按需保全，不能只凭摘要关闭责任。

```powershell
$db = "$data\Simulation\reference-17\sportmaster\sportmaster.db"
& "$release\application.ps1" -Action Inspect -Database $db
& "$release\application.ps1" -Action Export -Database $db -Destination 'E:\Support\new-diagnostic.zip'
& "$release\application.ps1" -Action Backup -Database $db -Destination 'D:\ImporterPcBackups\new-before-upgrade.db'
```

离线Backup在Windows要求提升维护权限，不启动来源；数据库一致性备份与配置、接收原件、未释放图像、overload及Effective/Pending包消费者同一恢复集，COMPLETED.json最后落盘。运行内备份仍要求Pause/维护认证/外呼排空，并调用同一备份实现。已有目标拒绝覆盖，中断的未完成集保留以便调查；仅完整COMPLETED集可恢复。SQLite采用[Online Backup API](https://www.sqlite.org/backup.html)获得一致快照，不能手拷正在使用的db替代。

## 升级中断、回退、修复、卸载与恢复

1. Pause、核对并清偿业务/原件责任，Stop退出，使用匹配原发布执行Inspect及Backup。Inspect的OrdinaryUpgradeAllowed只表示原责任/原件hash检查结果，不能替代目标兼容/签名/设备资格。旧DB2～6未结不能先启动新程序升级；已证明终态但仍缺原raw的旧Attached仅走专用recover-legacy-receipt（CLI显式DB9，旧API默认5），保留资料责任。
2. 将新媒介解压至新版本目录，独立核hash、保护ACL/安装所需新不可变包；用新目录Diagnose核目标静态组合。配置修改沿原CAS/Pending/Effective，不以新seed覆盖历史。保留旧发布及全部恢复集。
3. 显式用新release的Run、同一data根启动。迁移在来源Start前受守卫检查，SQL1～8及checksum不改；追加SQL9使旧8读者拒读。资源/SDK失败保留ActivationFailed、旧确认配置和Pending；停止失败的实例后修复依赖/候选，再显式restage或使用已确认Effective启动，不并行重建连接。对构造前、部分Start后、迁移/确认关键点断进程逐项留证。
4. 升级中断后，先核DB实际版本/原尝试及是否发生外部副作用。已成为DB9的根不能交旧8及更早程序，不回滚业务账。不覆盖库、删配置或清marker。优先用匹配当前发布完成本地原件恢复及依赖修复。旧发布仅可在原隔离备份上按其兼容能力核对原请求；不得启用新作业去重用旧RCS编号。
5. 修复程序文件使用已核hash的同一媒介重新解压到新目录、保护并Verify，数据/包/信任原样保留；更换版本按升级路径。卸载只移除明确所选的快捷方式和已停用发行目录，数据根、机器政策、信任、Store、永久身份、备份和原件全部保留。扩展卸载沿ManageExtension，须所有当前/离线/备份消费者及原责任清偿；不删除登记绕过。不提供自动删除业务数据的卸载动作。
6. 恢复到新的 `<root>/<原Mode>/<原Station>/<原Application>`，先PlanRestore验证完整集合/hash/容量，原根必须停用；不得在两个根同时操作设备。Restore成功仍隔离；核对原ID/报文、DB/配置/包/协议和实物，RCS来源范围退役，未决事实按原合同核对。没有权威终态依据不能Resume或重发。

```powershell
& "$release\application.ps1" -Action PlanRestore -File 'D:\ImporterPcBackups\before.db' -Destination 'D:\Recovered\Simulation\reference-17\sportmaster'
& "$release\application.ps1" -Action Restore -File 'D:\ImporterPcBackups\before.db' -Destination 'D:\Recovered\Simulation\reference-17\sportmaster'
```

## 支持交接、负载与再分发

支持记录至少包含源码完整SHA、ZIP与SHA256SUMS hash、Application/ABI/DB/config版本、Effective/Pending指纹、站/模式/账户SID、设备/固件/SDK/协议版本、源时间及请求/发生/冲突ID。参考负载结果仅解释合成场景；目标为4核/8GB、双位合计30～60件/分钟，现场选择7～30天已结明细保留；实际容量增速、离线时长、图片大小及RPO/RTO仍需测量确认，不能承诺长期稳定或按参考默认推现场预算。真实断电/设备重连/普通账户/ACL/UAC/目标DPI验收需同一ZIP/机器记录，现场运行还需明确授权。

DEPENDENCIES.json从实际发布deps.json及锁定NuGet元数据登记版本、包hash、license表达式、copyright和来源；licenses保留标准MIT/Apache-2.0文本（SPDX license-list-data v3.27.0），自包含运行时的LICENSE/ThirdPartyNotices按实际随包原文保留。包声明是依赖来源依据，不授予本项目新的商业许可，也不代替维护角色的最终再分发核对。PowerShell独立安装，不随包再分发；无厂商SDK/字体/凭据。真实SDK/驱动/系统组件需另外取得合法分发依据与精确资格，不改Reference/Simulation包名充作Production。
