# 运行、维护、发布与恢复

当前工作包为 **UI-MAINTAINABILITY-16**。Storage/MinimumReader **5/5**、SIMULATION-v1、ExtensionContract **1**；参考扩展新增不可变版本 **1.0.4**。当前结果/源SHA/候选身份见[16报告](ui-maintainability-16.md)及包内release.json。运行方式以本文和[首次参考作业](getting-started.md)为入口；历次操作原文见[本次前置归档](../archive/pre-ui-maintainability-16/README.md)。目标设计、原SQL/checksum、旧候选、D/RV和原失败均保留。

Windows实际客户端/输入/DPI/声音/触控/PowerShell/UAC/ACL仍NOT_RUN，[同版后验](validation/ui-maintainability-16-windows.md)逐项记录。真实SDK/WCS、身份、实体/数量及逐SKU少货权威未取得，Production缺必需能力继续NotReady。软件暂停不是安全急停。

## 构建和定向验证

使用global.json固定SDK **10.0.402**。NuGet/传递依赖与内容哈希以各packages.lock.json为准；不改锁绕过还原。构建采用单节点，WPF交叉构建不证明Windows行为。

```sh
./scripts/build.sh
# 跨平台构建机需显式启用Windows目标：
dotnet restore ImporterPc.slnx --locked-mode -p:EnableWindowsTargeting=true
dotnet build ImporterPc.slnx --no-restore -m:1 -nr:false -p:EnableWindowsTargeting=true
# 查看当前全部选择器和准确覆盖：
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll --help
# 本包新增风险；原恢复/资源按影响另选
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --maintainability
```

默认reconciliation包含原H04/A01/07/08/11/12/14/15与AC/进程基线；architecture是独立专用范围，不能从默认通过推断它也运行。operation-closure保护Q1/Q2每次接收责任和T3/重启；conformance保护CAS/失败保旧及冲突/部署；closure、review-followup、architecture分别覆盖来源时效/停止、组合资源及精确包/原请求集成。只运行受影响范围，日志尾部须有完整scope完成标记；--health-profile是确定性双源测量，不是生产节拍测试。

## 首次运行与操作

[首次运行/首条合成参考作业](getting-started.md)提供完整可执行步骤。源码启动在build后seal实际app/config；发布包已经seal，不手改product.json哈希。

```sh
# 终端A：只绑定字面127.0.0.1；独立接收账，保持进程
dotnet src/Ipc.Fixture/bin/Debug/net10.0/Ipc.Fixture.dll --url http://127.0.0.1:18743/ --data /new/simulation-receiver
# 终端B：同一ProductRuntime，没有第二套业务账
dotnet src/Ipc.Product/bin/Debug/net10.0/Ipc.Product.dll --config src/Ipc.Product/bin/Debug/net10.0/config --data-root /new/simulation-client --action serve
```

Console每行snapshot或JSON命令：先取State.Revision，再用唯一Id/ExpectedRevision/Kind/OwnerId/Value。Login仅选择明确模拟operator/maintenance，OpenContext为数字字符串箱码。emit提供Lane/Kind/Codes/Quantity，实体入场由明确模拟事件建立新Admission；同码不同实体不合并。Read校验后才能Submit；接受量来自唯一账，实体离场/复位分别显式确认。超量使用合成SYNTHETIC-KEEP-v1，扫描保留件身份，重复身份不增量；绝非生产策略。

Windows源码桌面`dotnet src/Ipc.Desktop/bin/Debug/net10.0-windows/Ipc.Desktop.dll --config src/Ipc.Desktop/bin/Debug/net10.0-windows/config --data-root <独立根>`；发布包用station.ps1。所有窗口共享唯一业务/来源/发送器；布局只改展示。页面草稿、历史筛选/选区/滚动各窗独立；小客户区保护不取消已发请求，空间恢复后仍人工核对/恢复。暂停/恢复的正常互斥是状态提示，其他真实阻断持续显示。箱结果和原请求/原因保留；完整哈希/JSON在详情。历史选中行后可直接查本箱全部作业/关闭链，显式打开详情后返回保留列表上下文。

拆包每通道单条在途；保存和刷新期间新输入明确拒收并要求重扫，失败保留原输入，Persisted且owner/命令/版本相同才消费。无全局业务热键；HID/焦点实机待验。报警已读不代表恢复；没有关联Owner时进入该报警来源诊断并明确说明，不造作业关联。

无桌面通知输出/Flush后必须按实际Sequence确认notification-ack；没有确认不删除，64容量及消费者失败保护保留。此确认不等于提示声听审。

## 组件与唯一配置

[独立扩展开发/构建/静态检查/签名/安装](../extensions/developer-guide.md)。安装只登记包；窗内ConfigurationDraft是维护/组件页唯一草稿。设备实例有稳定DeviceId/业务Role，服务实例有Port/Target，不互借身份。字段和参数严格校验，错误可定位；对照当前有效值，保存后仍未生效。SaveConfig/ActivateConfig使用原权限/CAS、原请求和实体屏障；Validated→PendingActivation→受控重启→Effective。ActivationFailed保留旧Effective，完全停止后下次受控启动使用旧组合；不在失败进程并行创建新所有者。

新内容必须新ID/version，参考1.0.4不覆盖1.0.0～1.0.3。公共DLL按精确指纹校验，即使ContractMajor=1也不表示旧包普遍兼容。请求T1冻结原实现/包/参数/合同/目标，历史查询只找原能力；缺失明确阻塞，不重定向或默认重发。卸载检查Effective/Pending/所有未决责任/持久消费者根/依赖和实际租约；不可读/旧覆盖未知仍拒绝。不能删identities、消费者登记、原件或库绕过。

## 故障、接收责任与停止

T1事实/意图/Outbox原子保存，T2领取/Attempt，交付前最后守卫授权同事务登记独立接收ID；原查询在网络前单独登记，失败不外呼。每份响应按接收ID写原件并Flush，T3原子保存响应/冲突、原终态/唯一接受账和该次Committed。另一份相同请求响应不清除Q1缺失责任。

- Committed但原件未清：启动按原接收ID/内容核验后清理，不永久锁定历史终态。
- 已Flush但T3失败：保留原件，启动按原合同/实现重放本次T3，不外呼重发。
- 无可信原件：该次RawUnavailable保持隔离，列接收ID/请求/来源/时间及未取得原因；匹配查询不伪造遗失原件，没有通用解锁。
- format4脏围栏不能证明逐次覆盖，保留legacy原字节及独立责任；未知/损坏/.new原件拒绝并留诊断。

Dispatching中断为Unknown；查询原RequestId/固定目标，查不到或不支持不证明未处理。原终态与冲突正交，不重计/倒扣接受量。CancelPrepared仅有全部尝试明确NotDelivered且未收到响应时可本地终结；原预留/实体及LocalDispositionRequired保留，不能借取消放货。恢复只解除有权解除的阻断，不解除存储/权限/设备/实体/未决。

停止先拒新业务，排空已接纳观察，等待/取消来源、发送及整次查询/验回/T3/审计。未终止Stop/Dispose继续持有应用、Store、Journal、站点/设备/包/依赖租约；超时不是释放成功。重复清理等待原任务，不启动第二所有者。过载同步保留独立marker，维护核查来源/实体、排空和最新OverloadVersion后才确认；未接纳的SDK事件没有逐条持久承诺。

同状态健康续期更新串行所有者当前确认/有效期/序列，不每心跳重写业务聚合或审计；状态/有效性/失败变化仍保存，后续业务事务保存最新健康。快照读取不续期；断线/过期仍阻断并产生报警。重启清空所有设备/服务健康，原epoch和确认不能作当前许可。业务观察不合并、不降采样。前后测量与验证见16报告。

## 发布组合与安装

Python和PowerShell共享package-reference.py的组合解析；core-reference、barcode-reference、integration-reference分别不带扩展/观察包/观察和集成包。私钥留在外部构建位置，独立公钥策略TestOnly且只用于Simulation。先提交源/脚本/配置/随包指南，再以全新目录发布：

```sh
python3 scripts/package-reference.py --target portable --composition integration-reference --dotnet <固定dotnet> --extension-key <外部私钥> --extension-trust <独立信任JSON> --output /new/maintain16-portable
python3 scripts/package-reference.py --target win-x64 --composition integration-reference --dotnet <固定dotnet> --extension-key <外部私钥> --extension-trust <独立信任JSON> --extension-check-host /new/maintain16-portable/app/Ipc.Product.dll --output /new/maintain16-win-x64
```

```powershell
./scripts/publish.ps1 -Mode Simulation -Composition integration-reference -ExtensionKey <外部私钥> -ExtensionTrust <独立信任JSON> -Output <全新目录>
```

包内release.json/PACKAGE.json/composition.lock.json/SHA256SUMS登记源/配置/模块/扩展身份；ZIP另算SHA。哈希不代替可信分发、签名或ACL。portable需.NET运行时，win-x64自包含但未实际Windows验收。解压新根后运行smoke.py、smoke-extension.py；实际输入为解压app/fixture，不将工作区运行冒称发布冒烟。

Windows使用只读可信Program Files发布目录，提升维护会话运行protect-release.ps1、provision-runtime.ps1指定实际普通RuntimeAccount、provision-extensions.ps1配置受保护ProgramData包库/独立策略。随后该指定账户运行station.ps1 -Action Init/Run；机级租约/ACL缺失拒绝，不回退用户级。组件管理提升辅助程序只执行固定管理操作，UAC取消/ACL拒绝必须反馈失败。不要从Downloads可写目录提权。Windows基线为11 24H2/build26100+ x64，实际安装仍NOT_RUN。

## 备份、旧版本和恢复

SQLite online backup用于一致备份，WAL/foreign_keys/synchronous FULL/busy timeout保持。备份/数据库含敏感数据，只在授权受限位置保存。离线运维竞争同一数据租约；明确选择发布目录，不自动切指针。station.ps1支持Preflight/Backup/Upgrade/Rollback/Export；必填Release/NextRelease/File/Receipt按脚本帮助提供。升级先校验两套SHA、无实体/未决/恢复隔离并保存原格式备份/持久回执，再前向升级；原SQL1～5/checksum不改。format1～4必须显式到5，未来格式拒绝。

旧1.0.1～1.0.3 Effective：保留匹配旧宿主和精确包，用旧能力清偿Unknown/接收/实体/箱；通过旧维护保存/激活无旧绑定的合法过渡Effective，完全停止；再备份/格式升级（若需要）、安装匹配新宿主的新包、保存/激活/重启。只换seed或schema不改变Effective。缺原能力只阻塞该迁移，不改旧请求或同版本重签。15合成旧部署旅程继续按其原SHA证据，不能宣称已重跑16。

失败核查实际format/回执，不假设回滚。回退只用匹配旧发布+原格式备份+全新数据根，保持恢复隔离；备份Prepared可能已被远端处理，转Unknown并核对外部账/实体，禁止自动补发或回滚外部接受事实。旧库LegacyConsumersUnverified需真实根/副本完整盘点与批准，无通用忽略开关。

历史查询/导出按身份、对象和原分页父上界，子事实为读取期间快照；UTC保存、本地日期时间显式时区。保留清理仅显式模拟维护期间，至少1天、完整终结会话且无原责任，当前会话/引用/清理审计保留。诊断导出只含批准元数据，原报文/条码/端点/私密路径不进入公开日志。磁盘/库损坏不能创建空库冒充恢复。
