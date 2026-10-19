# 审查03工程交付结果 · WP-A03-00～06

当前1401b7d候选退出最终交付：composition仍声明application-contracts 1.7.0，而实际ABI1.8.0。已上传原字节保留；正在以新身份修正和重建，不覆盖包。

已落实可独立执行的工程与不可变候选，未完成真实商用组合。代码与本轮交付在[PR #35](https://github.com/yuan2go/importer-pc/pull/35)，未合并main或PR34。[参考预发布及下载](https://github.com/yuan2go/importer-pc/releases/tag/audit03-reference-1401b7d)的16个远端资产state/size/SHA256与本地全部一致。实际运行/打包源码 **1401b7d3663bd7ca40aaa87773b023162178611c**；业务 **1.2.7**、Application.Contracts **1.8.0**、Parcel **DB7**，配置schema1/2/3。后续提交只补定向验证/证据/入口，最终回执核对发布输入同源，不冒称重新构建。Windows实际、真实设备/协议及现场仍独立未验。

## 起点与继承

执行最新main为7cf84f371dc0076231bde7a25e5d4f22404db93b，结束前再次fetch仍一致。主工作区保留ec698bd及6个未跟踪资料入口、0个已跟踪修改；另建受管理隔离工作树，不覆盖其他工作树。真实旧Release为reference-reconciliation-20261019-21e6cae，源码21e6cae3c9e20f34fc9429812e48e7f95e455fb9、Sportmaster Windows ZIP SHA256 54ce02ece4e88dd352687f446d72448f4fda2cabc16cf9e11e47a616e01c97a1。执行中未把旧产物算作新交付。

[原PDF/ZIP及发布交接](../reviews/commercial-quality-audit-03/README.md)原字节未动，SHA仍为96f54f86fe06b8565686285e5e83a7860497e4da2e7bd84eb7edf6112ed0472b / b5507a2f4d8721908c9c3e2b8ff7e1904ec83546df2c861e64e0c55c6bb77e85。PR34始终OPEN/c07304418aa68428331ba141418edfb3519a6939；逐文件继承其当前件/共享快照、有原终态依据的标记收敛，补本包差额。CQ03不是F01～F05的同号映射，没有宣称合并或全量复审PR34。其1.2.5过期断言失败仍在原审查目录；本轮精确兼容断言/夹具为实际1.2.7，未删唯一性或关键断言。

## 逐工作包结果

|工作包|已完成工程及依据|剩余、类别与边界|
|---|---|---|
|WP-A03-00 / F05|业务前独立提交 **13bb1fd**，整理入口、升级/窄修复/原Query、ABI、DB、CLI/API及Windows随包来源；当前普通DB2～6未结在写库前拒绝，专用custody CLI显式7、旧API默认5；Supply仍Storage5。所有最终媒介指南与冻结源码逐字节比对通过|首个真实组合的Windows/SDK/协议/实体/重量/负载/离线/维护输入未确认，已列支持矩阵与确认请求，不用参考值定稿|
|WP-A03-01 / F01、F02|**fa022d4**：先验证有效来源，再核既有Admission/Observation，最后对新件准入；同测量ID矛盾保原重量与完整矛盾正文，沿持久IdentityConflict隔离真实受影响位。两PLC WaitingWeight矛盾后WCS/PLC attempts均0，另一独立秤/位正常完成一次；重启、授权核对、原责任不互相清偿及资源释放通过。共享DB事务失败仍停收、测量/意图回滚，不被吞为局部冲突|代码缺陷已在参考层关闭；真实实体来源/秤及PLC动作资格尚未取得，不能据此放行设备|
|WP-A03-02 / F03|**b140401**及最终1401b7d：按实际T1持久边界选择编号，SQL7同事务唯一绑定数字ID/Operation/Request/hash；范围与恢复退役持久。真实签名加载的左右两种反序各一次POST；重启、实际归档、备份后源继续消耗ID、恢复退役整个旧范围、CAS/重启新范围可继续。显式合成SQL6→7保原ID/原报文并回填唯一Owner；歧义在只读预检拒绝，原DB字节/版本不动|跨工位/新库/克隆的独占范围仍需外部配置分配和停用旧根；没有上游幂等或全球恰好一次承诺。RCS权威Unknown合同仍缺|
|WP-A03-03 / F04|**436f718**继承并完善两布局公共时效、当前件与维护保护，补单调时钟、退役实例拒绝、修订倒退失效、旧格口标旧和轮询失败仍可独立软件暂停。失败/挂起/迟到/实例变化、双窗草稿/CAS与正确Owner、恢复不Resume/重发通过。先读v1.1原HTML、CSS/JS、图册和完整交接，保留专业信息层级|Windows原生窗口/Dispatcher、DPI/触控/HID、实际点击及截图 **NOT_RUN**；PNG是原设计同源图。DirectPlc完整UI延期仍保留，指定商用场景必要HMI未验|
|WP-A03-04|保持Driver/Component、资格/签名/精确公共DLL及资源生命周期边界；提供新精确参考包，实际RCS HTTP适配不改成Production，不造Query或人工“成功”|**BLOCKED，能力未实现/输入未确认**：真实reader/秤/PLC SDK与可执行真实Driver、SDK位数/许可、Admission与同件重量/物理完成合同未提供。RCS Unknown无权威Query/处置合同，不能清原件责任；不是单纯“有代码未测试”|
|WP-A03-05|**d374a11 / 1401b7d**：实际三套win-x64媒介；application.cmd/ps1提供依赖诊断、绝对路径启动、普通账户新数据目录ACL、快捷方式、离线检查/诊断/一致备份与恢复预检；复用唯一配置、安装/绑定/激活、失败保旧、回退/修复/卸载保留数据SOP。离线备份先Normalize原Mode再登记Effective/Pending消费者。解压真实入口9组冒烟、签名选装激活/失败回退/卸载、从/tmp执行CLI均通过|PowerShell仅macOS解析及非Windows拒绝通过；实际普通账户、UAC/ACL、多会话/设备独占、快捷方式、升级中断/修复/卸载Windows流程 **NOT_RUN**。自包含不等于设备运行库已满足|
|WP-A03-06|完成本包相关有界队列/过载、T3/原件恢复、停止超时保租约、重启、备份恢复、诊断导出、故障SOP及依赖/许可来源登记；保留原失败。资源套件原始peak memory footprint为63357744 bytes，数据库/目录实际字节见资源记录|正常/突发负载、离线时长、保留/RPO/RTO及支持责任方 **未确认**；真实断电、长期资源/磁盘趋势、SDK反复重连与现场 **NOT_RUN/BLOCKED**。上述短时参考耗用不是产能/SLA验收|

SQL1～6的原字面量逐字节比较一致；SQL7追加归属/范围/恢复边界，旧6读者拒读7。旧请求、原payload/目标/合同/hash、原raw/图像/包、原D/RV与REQ/BR/CAPI均未重写。身份冲突、RecoveryRequired、远端结果与独立资料责任分别处理；软件暂停不是安全急停。

## 实际验证与产物

[证据目录与日志分类](evidence/commercial-audit03-delivery/README.md) · [冻结媒介/包hash](evidence/commercial-audit03-delivery/ARTIFACTS.json) · [7个ZIP全量核验](evidence/commercial-audit03-delivery/media-verification.json) · [9组实际进程记录](evidence/commercial-audit03-delivery/smokes/commands.json) · [最终DLL身份](evidence/commercial-audit03-delivery/verification-runtime-files.json)。

已执行必要Release/WPF跨构建、输入/共享存储故障、实际签名RCS反序T1/SQL6迁移、当前件/快照、原件/P2/升级/资源、精确版本和随包链接验证；9/9解压进程default/BeforeMain/Attached通过，可选场景另起独立本机HTTP peer。实际选装CLI证明两个签名参考reader、签名WCS、安装→CAS激活→合成启动失败保旧→移除绑定/卸载及原历史保留；实际签名RCS的拒绝/Unknown/重启Query重连不重发。没有把VM/shim当WPF运行，也没有循环全库或改保护规则换PASS。公开日志仅规范本机路径，原始副本及双摘要见public-path-normalization；不改配置原hash含义。

公开归档检查采用同一完整Python/Pillow环境比较最新main：原267个失败（图册PPTX媒体类型265、历史本机元数据2）继续保留，不修改原图册或放宽检查器；本轮错误增量另见archive-error-delta.json。归档总状态仍FAIL，不能写成全部PASS。

最终Windows媒介（源1401b7d，Reference/Simulation）：

|文件|SHA-256|
|---|---|
|sportmaster-selected-win-x64.zip|d97cec0f887ad3fa6308b050aaf21fb5bbb8be840fd7987919d60b1732c0c23b|
|direct-plc-win-x64.zip|268285bd2bfb645f9e201138c0f24078e50a46d870ad7a092bc72fc3be05b087|
|wcs-to-plc-win-x64.zip|6a123683313b0f328e5c26a0824122b0d5af2e0b3a32c19c40c6571034f83c7b|

portable与选装包完整hash、配置/协议/公共DLL/每包身份以同一ARTIFACTS与组合记录为准。新能力包2.1.5、reader1.2.5、RCS1.0.7均Simulation/TestOnly，按冻结公共DLL构建。d374a11/1.2.6本地候选因离线备份原Mode登记差额而退出最终交付，原目录、原包和结果保留，不覆盖身份。标准MIT/Apache-2.0文本、实际NuGet声明/copyright和运行时原通知随包；未取得厂商SDK分发许可或项目级商业许可，不凭此推定授权。

运行：解压后按随包WINDOWS-APPLICATION.md执行 分别执行`application.ps1 -Action Verify`、`application.ps1 -Action Diagnose -DataRoot <绝对根>`及`application.ps1 -Action Run -DataRoot <绝对根>`；先指定普通SID完成保护及运行根配置。portable用同包 `app/Ipc.Assembly.dll --config <绝对配置> --data-root <隔离根>`。实际离线Inspect/Export/Backup/PlanRestore的argv与源DB/config指纹见cli/。发布下载与PR回执见[交付回执](evidence/commercial-audit03-delivery/publication.json)。

## 逐组合准入与下一步

|Application / 组合|继续开发|隔离参考实验室|Windows原生证据|真实设备实验室 / 现场试点 / 商用|
|---|---|---|---|---|
|Sportmaster内置参考WCS，两布局；可选登录/独立双秤/采图/BeforeMain或Attached|通过|通过，仅合成实体及参考peer|证据不足，NOT_RUN|不通过；真实Driver/实体合同、Windows及目标条件未闭合|
|Sportmaster签名RCS1.0.7，双位/异步采图|通过|通过，实际HTTP客户端对合成本机peer|证据不足，NOT_RUN|不通过；除真实Driver/Windows外，Unknown权威处置未实现，不能有条件放行|
|DirectPlc参考PLC，按配置同件重量及可选能力|通过|通过，0发送冲突反例及实际媒介路径|证据不足，NOT_RUN|不通过；真实秤/PLC握手/物理完成、必要HMI/Windows与目标条件未闭合|
|WcsToPlc参考WCS→PLC，按配置同件重量及可选能力|通过|通过，格口/PLC阶段及恢复分开|证据不足，NOT_RUN|不通过；真实WCS/PLC/重量/完成合同、Windows与目标条件未闭合|

旧Supply/Inspection只沿自身兼容规则；本包没有为它们授予新商用结论。关键错控/丢件/重复副作用/无法恢复风险没有真实关闭证据的组合不作“有条件通过”。

补证顺序：确认首个Application及Windows SKU/build/账户/目录/输入环境；取得设备型号/固件/SDK/位数/许可与权威实体—重量—动作合同；为RCS取得真正的原请求查询或经协议方批准的可审计处置合同；实现并独立签名真实Driver/Component、锁精确资格/包/配置/数据库；以同一候选hash执行Windows普通用户/ACL/UAC/中断升级/修复/卸载/备份恢复，再按确认负载/离线条件验证资源与故障。实际现场运行另需明确授权，不从模拟材料补造客户需求、批准人或设备方法。
