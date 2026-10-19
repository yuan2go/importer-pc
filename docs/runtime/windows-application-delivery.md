> 本分支按[UX修复工作包](../work-packages/WP-IPC-UX-COMMERCIAL-FIX-01.md)更新现行操作；修改前说明的固定Git原字节见[索引](../archive/pre-ux-commercial-fix-01/README.md)。开发验证、最终媒介身份和Windows/现场验收分别记录，本文不宣告后者通过。

# 三个Application的Windows候选与维护路径

本页与实际媒介同一固定源码生成；SourceCommit、ApplicationId、业务1.3.0、ABI1.9.0、DB7、配置schema1/2/3分别读取ASSEMBLY.json、app/application-release.json和DEPENDENCIES.json。Supply仍用自己的station.ps1/Storage5，不能套用这里命令。候选工程基线沿现有Windows11 build26100+ x64/PowerShell7；客户SKU/build、设备/SDK及合同未确认，实际Windows全部NOT_RUN，真实组合和商用BLOCKED。不得把本页步骤写成已经执行的Windows验收。

## 媒介与依赖诊断

从独立交付记录核ZIP SHA256，解压至新的 `C:\Program Files\ImporterPc\releases\<source-sha>\<application>`，不覆盖旧发布。三个Application各有自己的ZIP，不借用Sportmaster媒介启动PLC业务。保留SHA256SUMS、app/desktop的application-release.json、composition.lock.json、离线包及原签名身份；包内hash不是独立发布者信任。

Windows自包含候选带.NET/WPF运行时；PowerShell7为外部前置，不含在媒介中，Windows PowerShell5.1不能代替。application.cmd在找不到pwsh.exe时明确退出2并指向本页。实际Runtime版本以各runtimeconfig.json和随包运行时文件为准，固定SDK10.0.402与锁定NuGet不代表客户自动得到维护更新。PowerShell从[官方Windows安装页](https://learn.microsoft.com/en-us/powershell/scripting/install/install-powershell-on-windows)取得，离线安装包需维护者单独核来源/hash。OS生命周期仍按[官方.NET Windows支持矩阵](https://learn.microsoft.com/en-us/dotnet/core/install/windows)核具体SKU/build；框架支持不替代本产品、SDK及现场资格。

在普通PowerShell7终端设置实际路径（以下站名为合成示例）：

```powershell
$release = 'C:\Program Files\ImporterPc\releases\SOURCE_SHA\sportmaster'
$data = 'D:\ImporterPcData'
& "$release\application.ps1" -Action Verify
& "$release\application.ps1" -Action Diagnose -DataRoot $data
```

Verify检查完整文件集合、hash、越界及链接路径；Diagnose调用真实业务版本/组合及deployment-check入口，不启动reader/SDK或Submit，再检查指定Windows运行SID。配置无效、缺包/依赖/机器策略或SID错误明确失败；没有用Simulation回退Production。坏配置不通过覆盖旧configuration.json或换空根恢复。配置根参数可用 `-Configuration <绝对配置目录>`，其business.json是seed，持久Effective/Pending仍属于数据根内唯一配置账。

## 安装与普通账户

由提升的维护会话先执行protect-release.ps1，再provision-runtime.ps1指定实际普通RuntimeAccount；机器政策已存在时不删除重建，核原SID和ACL。安装脚本必须从保护后的可信Program Files发行目录执行。数据卷使用独立新目录，ProvisionData创建仅SYSTEM/Administrators及指定运行SID可写的目录；已有目录拒绝覆盖，转入ACL核对，不删除旧数据。

```powershell
& "$release\protect-release.ps1" -Release $release -RuntimeAccount 'MACHINE\station-user'
& "$release\provision-runtime.ps1" -RuntimeAccount 'MACHINE\station-user'
& "$release\application.ps1" -Action ProvisionData -DataRoot $data -RuntimeAccount 'MACHINE\station-user'
# 仅使用独立渠道提供的批准信任；Simulation示例可为TestOnly，Production不能使用测试根
& "$release\provision-extensions.ps1" -Mode Simulation -TrustFile 'D:\ApprovedMedia\trust.json'
```

provision-extensions.ps1沿机器政策读取指定运行SID；安装/恢复原操作/绑定/校验/StageConfiguration/受控重启见[扩展指南](../extensions/developer-guide.md)。安装不等于绑定或激活，资格/启用/健康/业务可用分别检查。提权拒绝或错误SID不得改成成功。日常以指定普通账户运行，不保留管理员桌面生产作业；机级站点/设备租约继续阻止另一个登录会话或不同数据根重复拥有设备，不能删lease绕过。

```powershell
& "$release\application.ps1" -Action Run -DataRoot $data -Ui classic
# Sportmaster另可选择supply-station；DirectPlc/WcsToPlc只能classic
& "$release\application.ps1" -Action Shortcut -DataRoot $data -File "$env:USERPROFILE\Desktop\Sportmaster-candidate.lnk" -Ui supply-station
```

快捷方式保存绝对程序/配置/数据路径及工作目录；从其他cwd启动仍指向相同目录。启动先展示真实阶段及诊断，配置预检、持久账/原件/精确实现检查及真正设备Start是独立门禁；不显示虚构百分比，启动失败不进入可作业状态。DirectPlc完整UI延期保留，共用classic仅作为现有参考HMI；指定商用场景的可操作性须另验。Sportmaster不新增展图/长期图库。

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
& "$release\application.ps1" -Action Backup -Database $db -Destination 'E:\Backups\new-before-upgrade.db'
```

离线Backup在Windows要求提升维护权限，不启动来源；数据库一致性备份与配置、接收原件、未释放图像、overload及Effective/Pending包消费者同一恢复集，COMPLETED.json最后落盘。运行内备份仍要求Pause/维护认证/外呼排空，并调用同一备份实现。已有目标拒绝覆盖，中断的未完成集保留以便调查；仅完整COMPLETED集可恢复。SQLite采用[Online Backup API](https://www.sqlite.org/backup.html)获得一致快照，不能手拷正在使用的db替代。

## 升级中断、回退、修复、卸载与恢复

1. Pause、核对并清偿业务/原件责任，Stop退出，使用匹配原发布执行Inspect及Backup。Inspect的OrdinaryUpgradeAllowed只表示原责任/原件hash检查结果，不能替代目标兼容/签名/设备资格。旧DB2～6未结不能先启动新程序升级；已证明终态但仍缺原raw的旧Attached仅走专用recover-legacy-receipt（CLI显式DB7，旧API默认5），保留资料责任。
2. 将新媒介解压至新版本目录，独立核hash、保护ACL/安装所需新不可变包；用新目录Diagnose核目标静态组合。配置修改沿原CAS/Pending/Effective，不以新seed覆盖历史。保留旧发布及全部恢复集。
3. 显式用新release的Run、同一data根启动。迁移在来源Start前受守卫检查，SQL1～6不改；追加SQL7使旧6读者拒读。资源/SDK失败保留ActivationFailed、旧确认配置和Pending；停止失败的实例后修复依赖/候选，再显式restage或使用已确认Effective启动，不并行重建连接。对构造前、部分Start后、迁移/确认关键点断进程逐项留证。
4. 升级中断后，先核DB实际版本/原尝试及是否发生外部副作用。已成为DB7的根不能交旧6程序，不回滚业务账。不覆盖库、删配置或清marker。优先用匹配当前发布完成本地原件恢复及依赖修复。旧发布仅可在原隔离备份上按其兼容能力核对原请求；不得启用新作业去重用旧RCS编号。
5. 修复程序文件使用已核hash的同一媒介重新解压到新目录、保护并Verify，数据/包/信任原样保留；更换版本按升级路径。卸载只移除明确所选的快捷方式和已停用发行目录，数据根、机器政策、信任、Store、永久身份、备份和原件全部保留。扩展卸载沿ManageExtension，须所有当前/离线/备份消费者及原责任清偿；不删除登记绕过。不提供自动删除业务数据的卸载动作。
6. 恢复到新的 `<root>/<原Mode>/<原Station>/<原Application>`，先PlanRestore验证完整集合/hash/容量，原根必须停用；不得在两个根同时操作设备。Restore成功仍隔离；核对原ID/报文、DB/配置/包/协议和实物，RCS来源范围退役，未决事实按原合同核对。没有权威终态依据不能Resume或重发。

```powershell
& "$release\application.ps1" -Action PlanRestore -File 'E:\Backups\before.db' -Destination 'D:\Recovered\Simulation\reference-17\sportmaster'
& "$release\application.ps1" -Action Restore -File 'E:\Backups\before.db' -Destination 'D:\Recovered\Simulation\reference-17\sportmaster'
```

## 支持交接、负载与再分发

支持记录至少包含源码完整SHA、ZIP与SHA256SUMS hash、Application/ABI/DB/config版本、Effective/Pending指纹、站/模式/账户SID、设备/固件/SDK/协议版本、源时间及请求/发生/冲突ID。参考负载结果仅解释合成场景；目标正常/突发速率、离线时长、图片大小/保留、容量增速和RPO/RTO未确认，不能承诺长期稳定或按参考默认推现场预算。真实断电/设备重连/普通账户/ACL/UAC/目标DPI验收需同一ZIP/机器记录，现场运行还需明确授权。

DEPENDENCIES.json从实际发布deps.json及锁定NuGet元数据登记版本、包hash、license表达式、copyright和来源；licenses保留标准MIT/Apache-2.0文本（SPDX license-list-data v3.27.0），自包含运行时的LICENSE/ThirdPartyNotices按实际随包原文保留。包声明是依赖来源依据，不授予本项目新的商业许可，也不代替维护角色的最终再分发核对。PowerShell独立安装，不随包再分发；无厂商SDK/字体/凭据。真实SDK/驱动/系统组件需另外取得合法分发依据与精确资格，不改Reference/Simulation包名充作Production。
