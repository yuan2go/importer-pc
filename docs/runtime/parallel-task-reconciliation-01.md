# 并发任务整合报告

工作包[WP-IPC-PARALLEL-TASK-RECONCILIATION-01](../work-packages/WP-IPC-PARALLEL-TASK-RECONCILIATION-01.md)。开工2026-10-19 01:08:15 +08:00，main **713f23d7c39743762adb884fdffa18745cad08f8**。本节为代码修改前的调查/裁决，最终SHA、验证和统计在交付时补入；当前不宣称整合完成。

## 成果保护与调查证据

[起始清单](evidence/parallel-task-reconciliation-01/start-inventory.json)保存4个Open PR（均非Draft）、完整HEAD/base/checks、26个可访问注册worktree及全部refs。GitHub main未保护、规则集空、无必需检查；合并前重新核验。所有分支沿共同基线追溯，并非只看最近提交。源任务会话实际读取，不以标题推导原要求。6个相关任务已通过受支持消息接口确认停止写入；整合在独立工作树进行，原树不清理、不reset、不强推。

主树198项未跟踪文件及PR27树35项暂存证据已在Git外保存原字节、SHA和binary patches，所有refs已bundle。PR27在盘点窗口推送a4ecfd1，原e86d664和新增证据均纳入；35项已由作者提交，不能重复认作本任务提交。忽略目录已盘点，文本报告/日志另有快照，原二进制/测试库保留原位置。原始客户HTML/PRD及受限基线不公开；两份delivery12副本核对现行工作包后按历史来源登记，不冒充新增要求。无法访问或已删除的外部会话无法取回；架构审查01原PDF继续缺失，不用02代替。

## 任务对照

|任务/原要求（实际会话）|基线、分支、PR与实际成果|证据与缺口|重复、依赖与处理|
|---|---|---|---|
|架构质量闭环01：容量/Attached隔离、采集并发、UI目标、配置扩展、原PDF；后来独立修复DB4预迁移和schema1/2冲突|PR24 f531d3a已合并；后续PR31 da9233d合并为起始main713f23d。共享维护/原件/配置及升级守卫已实现|原审查01 PDF未取得；[PR24原审](../reviews/pr24-independent-rereview-f862c2a/README.md)、[PR31原审](../reviews/pr31-independent-rereview-f068156/README.md)及历史失败保持|继承起始main，不重做PR24/31。PR30的DB6不能绕开PR31守卫|
|审查02 N01–N07实施：配置断进程、旧Attached/缺raw、容量、CAS、选装与维护；后续两P2恢复及custody竞态|PR26已合并；新PR32 codex/parcel-recovery-p2，HEAD0aac919，4笔起始main外提交，共同基线baac260。永久ID核验、持久Query依据与确定性夹具|[PR32原审](../reviews/parallel-task-reconciliation-01/pr32-original-review.md)、[结果](parcel-recovery-p2.md)，原FAIL及发布身份保留；参考范围无已确认残余缺陷|与PR31修改相邻恢复文件；合并两者语义，不按ours/theirs整文件覆盖|
|Sportmaster第二套UI：按提供HTML转换WPF，客户端参数可选；公开衍生代码，原HTML不传；后续6项P2修复|PR27 codex/sportmaster-alternate-ui，初始e86d664，盘点末a4ecfd1；共同基线713f23d，17笔。classic/supply-station、多窗共享实例、读取失效/顺序、小客户区暂停、关闭排空|[原审](https://github.com/yuan2go/importer-pc/blob/a4ecfd1c2a687caca9d52f2cfc08ee4fce9ff91e/docs/reviews/pr27-ui-independent-review.md)、[结果](https://github.com/yuan2go/importer-pc/blob/a4ecfd1c2a687caca9d52f2cfc08ee4fce9ff91e/docs/runtime/sportmaster-alternate-ui-results.md)、固定候选与原失败。Windows原生交互未验|与PR30共享VM、PR29共享打包/指南。保留RCS类型化结果和新版UI，UI不拥有业务流程|
|业务版本贯通发行物、业务/组件/驱动兼容声明与真实组合证据；后续修复P1/P2并复审|PR29 codex/version-compatibility，HEADe0f5303，共同基线baac260，11笔。业务1.2.1、release schema2、内嵌ProviderSets、快照事件/实际DLL校验、维护共用入口|[最新原审](../reviews/parallel-task-reconciliation-01/pr29-original-review.md)：仍有执行方式/验证资格改标P2；原P1配置和维护P2已修|修复剩余P2；与RCS契约1.7和新组件版本声明联动；不删兼容守卫换通过|
|按实际给定sorter.task.dest_request及三类响应实现首个Sportmaster RCS组件，明确命名|PR30 codex/sportmaster-rcs-task，HEAD822ce92，共同基线baac260，6笔。纯准备T1、HTTP原件T3、正常/NEED_UNPACK/拒绝、Unknown禁重发、签名扩展、SQL6/Contracts1.7|实际读原提示词和独立协议探针；[设计](https://github.com/yuan2go/importer-pc/blob/822ce92f01e01487fdf037eca9219a601250d458/docs/design/sportmaster-rcs-task.md)、[结果](https://github.com/yuan2go/importer-pc/blob/822ce92f01e01487fdf037eca9219a601250d458/docs/runtime/sportmaster-rcs-task.md)。初期报告无阻断，收尾实际行内P1另见C08；真实RCS未联调|与PR31迁移守卫、PR32持久Query依据相交；与PR29内嵌版本声明冲突，需新精确包|
|不测试发布任务：提交现有成果、输出数量并合并当时PR|PR28 e43940e已合并，原main baac260；发布8份既有产物及回执|[原回执](publication-20261019.md)保留NOT_RUN；无未提交新增源码|本次显式要求组合必要验证，旧不测试仅属原任务，不扩展为永久限制|

## 文档权威与裁决

有效顺序：本次用户边界→框架F决策及19/20→22拓扑→质量闭环/PR31/PR32恢复增量→RCS协议与版本声明→UI参数/投影。后续增量只替代其明确范围，历史报告/PDF的形成时结论保持。旧Supply Product/Storage5不成为Parcel操作入口。根README中错指reviews/runtime的链接修复为docs路径。原入口字节在[归档](../archive/pre-parallel-task-reconciliation-01/README.md)。

|冲突|来源与影响|采用方案与理由|
|---|---|---|
|C01 原件恢复及DB迁移|PR31先判旧未结；PR30追加SQL6；PR32永久ID/持久Query。自动文本合并也可能先升级破坏旧读者|保留迁移前判定，SQL1–5原样，SQL6只追加；DB5→6有未决责任同样保守判定，专用CLI显式选择6、原API默认5保持兼容。持久原件/永久ID是依据，64条投影不替代|
|C02 兼容声明与RCS|PR29仅列参考ProviderSets、Contracts1.6；PR30组件1.0.1针对旧精确宿主。直接组合会拒绝RCS或装错包|按最终真实Contracts1.7/业务版本更新声明，新增RCS允许组合；旧包字节不覆盖，用新不可变版本针对最终宿主构建验证|
|C03 证据改标|PR29最新独立复审反例，可将本地peer说成外部进程/Windows PASS|扩展既有输入事件绑定和重验，核对命令、实际peer配置/场景及参考资格；保留schema1/2历史原件，不伪造旧事件|
|C04 打包/多UI/文档|PR27新增UI随包说明、PR29 schema2及稳定快照、PR30 RCS指南/包选装，共改脚本与compositions|合并同一发布入口，锁定最终版本、契约、组件和全部必要指南；没有第二打包系统|
|C05 VM与恢复投影|PR27共享目标/读取顺序，PR30 UnpackRequired与完整结果，PR32Query接受依据|保留全部投影和防串位/防过期结果的守卫；业务判断仍在Application，UI仅调用契约|
|C06 多份当前设计/历史OPEN|源分支各自INDEX与manifest、框架说明互相覆盖，旧报告自称当前|当前入口统一索引；历史原字节、PDF、失败、候选与SHA保留；manifest只按最终受控文件集合重算|

## 产物分类与缺口

保留所有4个PR代码、配置、脚本、工作包、设计、验证记录和历史快照；PR29/32此前仅在Git外的独立报告在本次原字节归档。源候选/签名包/数据库保持原位和指纹，当前公开源代码与合成证据不夹带私钥、SDK、客户原稿或机器配置。02 PDF SHA256 dce6b0e235c44e414cc14a976dbd3cea0d24f634aab1276be894b6b69b62283d，另两份已公开PDF及同源UI资产继续原字节。陈旧远端docs/ui-assets和docs/final-ui-assets分支作为历史候选保留：非Open PR，不为祖先关系盲合旧入口。

## 验收口径

最终固定代码、需求覆盖与完整交付状态见文末。源码核实、自动化、参考进程、Windows实机和真实设备现场分别列明；本次参考验证不授予现场PASS。

## PR32组合验证

代码`271e8d2`继承起始main和PR32。LegacyReceiptRecovery保留PR31 OutcomeReconciled归档分支，Awaiting本地/外查统一传Repository做永久ID核对。构建0警告/0错误；恢复P2、升级冲突及receipt-custody三组exit0，见[准确命令及指纹](evidence/parallel-task-reconciliation-01/pr32-validation.json)。原FAIL日志原字节含历史行尾空白，未为格式检查改写原件。

## C01的显式SQL6迁移边界（编码前裁决）

普通启动对DB2–5的未结业务/原件继续在读写打开和配置写入前拒绝；不能通过SQL6绕过PR31。原recover-legacy-receipt直调API默认保留已确认的DB5目标；最终CLI额外显式选择当前格式6，仍先取得Owner、原SHA、全备份和主/上传终态及持久Query依据，随后使用同一UpgradeCustodyOnly执行。无新Query权限、无清原raw责任。这样保留原调用兼容性，又避免旧库经窄迁移到5后被6普通启动永久挡住。新字段只在内部维护API末尾可选，不增公共Contracts接口。合成DB4夹具必须真正移除SQL5/6的新增表及标记，不能把DB6误称DB4。

## PR30整合中的原失败与修复

文本自动合并会在配置写入前先打开DB5、随后再打开DB6，已改为单次DB6预检打开，继承PR31读写前门禁。原SQL6测试允许“未知SQL5直接升级”，与新确认的旧读者保护相冲突，实际组合运行被LegacyUnsettled拒绝（原FAIL保留）。现改为验证未知原件拒绝且DB/raw/责任原字节保留，并独立验证已终结SQL5升级6的原请求、结果及SQL1–5 checksum保持；未删除原责任断言。DB4夹具原只删SQL5标记，组合下残留SQL6导致Unknown/changed，现真正移除5/6增量对象和标记，原失败保留。另加DB5未知拒绝与DB5已具Query依据显式6迁移，API默认5的原回归继续适用。

## PR29剩余P2闭环与版本组合

原e0f5303独立复审的两个实际反例在旧验证器均PASS，当前均拒绝；保留原事件不变，即使给变造文件自身hash也会因实际endpoint/参考资格矛盾拒绝。新记录schema3绑定命令、工作目录、场景、资格和计划检查；旧schema1/2要求独立可信原记录SHA，不能补造历史执行方式。原始未改记录带可信原SHA仍可重验。原P1及维护P2守卫保留，41项Python、三业务VC01/RCS/配置确认通过；15项证据定向包含新metadata/降级/重新绑定攻击。初次新测试只因拒绝更早触发input而与旧异常文案不符，原输出保留，仍断言必须拒绝。最终精确签名包与发行物在UI组合完成后一次冻结构建，不用早期Debug包冒充最终包。

## C05 UI与RCS组合差额（修改页面前）

已按入口检查v1.1 README/GALLERY、原单文件HTML和分离HTML/CSS/JS、完整UI交接；另核用户明确给定HTML及PR27已批准的WPF衍生设计和原始审查，不上传客户HTML。保留蓝/紫双位、固定主动作和局部滚动诊断。PR27的Status没有UnpackRequired分支，RCS三类详情只在ParcelViewModel；组合后复用同一个只读DestinationSummary，既有诊断区显示任务/SKU/所有拆包格口及零/未知差别，并补中文阻断状态。业务状态/命令守卫、原请求/HTTP、窗口和读序代码保持所有权；不引入拆包提交、PLC、图片展示或长期图存储。随包UI指南只属于Sportmaster；其他产品共享版本/维护指南。

## C07 实际发行二进制清单缺陷

在d4d448a五份实际媒介核对时发现原package-application.py从整个output递归收集Ipc DLL，ASSEMBLY/composition.lock的Binaries包括随后删除的build-intermediates。Sportmaster core清单96项中84项不在ZIP，Windows selected142项中115项不在ZIP。原候选、签名包及原12组参考PASS保留其身份，媒介整体未获合格；不把测试PASS扩张为发行完整。

修复同一发布入口，只从最终app/desktop收集实际DLL，仍保留完整Files/SHA256SUMS及原包静态验收。实际ZIP核对器verify-media.py固定检查无缺失/无未列二进制及逐文件SHA，原d4反例FAIL保留。新源码需重建精确指纹，业务1.2.3/能力包2.1.1/reader1.2.1/RCS1.0.3使用全新目录与身份，不覆盖d4的1.2.2/2.1.0/1.2.0/1.0.2。原14/19/29等更早候选保持形成时范围。

## C08 行内评审补查与裁决（代码前）

回读实际GitHub行内评论及original_commit_id：PR27两项来自a698651，命令读取失效已由a4ecfd1统一ReadSnapshotWithId覆盖；读取恢复后Message旧横幅仍可能残留，补仅清除该次读错消息并保留其他业务消息。PR29维护schema2评论来自1570b32，当前共享方法/实际发行DLL已修复；静态探针需检查桌面依赖Ipc.Assembly.dll（工厂所有者），不能误查只拥有窗口的Ipc.Desktop.dll。

PR30评论来自c168a30，已确认修改base可令base+全局序号重复。采用现有T1/永久作业及归档作为唯一权威，在Repository启动时以有界SQL读取已分配numeric ID上界并拒绝历史跨实体重复；内存仅缓存这个可重建标量，正常新件O(1)，不建第二账/表/迁移。新请求ID必须严格高于本Application库的持久历史上界；低基值/过旧区间在准备阶段持久阻断本件，WCS意图/尝试未创建、无POST，按既有检查移除/修订配置恢复。T1提交后推进标量，回滚不推进；原Prepared/Unknown继续使用冻结请求，不重新分配，归档/重启/备份恢复不复用。此为本地生成约束，不宣称跨工位全局幂等；外部分配不重叠范围仍需现场治理。SQL1–6和公共契约形状保持。

CLI夹具原两个失败为合成HTTP对端误读请求字段barcode（实际请求是loadCode），不是组件请求不符；第三次参考包卸载在CanConfigure快照与命令之间被Monitor守卫拒绝，原审计证明前一个包已退休且本次没有进入维护动作。夹具仅对明确的“active calls drain”前置拒绝等待后再试，不重试未知外呼；原FAIL及原库保留。

最终新冻结候选使用业务1.2.4/能力2.1.2/reader1.2.2/RCS1.0.4，不覆盖ed514f6或更早媒介。ed514f6实际ZIP/六组合/维护验证仍保留其适用版本，但最终验收必须重跑新组合。

## 最终候选、需求覆盖与验证

固定运行源码 **21e6cae3c9e20f34fc9429812e48e7f95e455fb9**；业务1.2.4、Contracts1.7.0、Parcel DB6、能力包2.1.2、reader1.2.2、RCS1.0.4。后续整合证据/统计提交不改变SOURCE_PATHS运行与随包指南闭包。所有结果对应该固定候选，当前main须逐文件核对闭包一致，不能把旧候选PASS挪用。

|已确认要求|最终实现位置|来源|最终结果|
|---|---|---|---|
|四类职责与独立主流程；同实例多页面；可选能力不复制流程|src/Ipc.Application.{Sportmaster,DirectPlc,WcsToPlc}、ParcelFlow、Assembly/ParcelCapabilityFactory、Desktop|19/20/22、PR27/29/30|源码核对；实际Release DLL的application-architecture/architecture-quality/review-closure PASS|
|Sportmaster双reader/左右位隔离、只请求格口、不PLC/不展图|SportmasterFlow、ParcelAcquisition/Runtime、SupplyStationViewModel|22/27/30|采集生命周期/UI/RCS回归和解压CLI双位PASS；同码/原报文与各自结果隔离|
|DirectPlc/WcsToPlc单相机顺序处理、DirectPlc完整UI延期|各Flow、Topology/Assembly；既有维护外壳|22/29|三core实际启动、主链和重启PASS；两个WPF跨构建PASS不等于完整UI验收|
|纯准备T1、HTTP原件T3、超时/拒绝/未知不重发、拆包零值/未知区分|SportmasterRcsTaskService、ParcelRuntime/Repository、DestinationSummary|PR30/27、C08|RCS及精确签名RCS组PASS；实际selected CLI原请求/原HTTP字节、重启/Query/重连不POST PASS|
|数字ID不因低基值、归档、恢复而复用|ParcelHistory.InitializeIntegrationRequestHistory/RequireNewIntegrationRequestId、Repository.Commit、Runtime.PrepareWcs|PR30行内P1、本轮C08|原RED复现；当前低区间发送前Block、旧意图不改、归档与备份恢复重建上界PASS；不承诺跨站ID治理|
|原件/永久ID/持久Query、旧库迁移与配置确认守卫|ParcelLegacyReceiptRecovery、ParcelCustodyMigration、ParcelUpgradeGuard、ConfigurationActivation|PR31/32/30|recovery-p2、upgrade-conflict-closure、receipt-custody、configuration-confirmation PASS；SQL1–5原校验和保持、6只追加|
|版本贯通、实际业务程序集维护校验、组合资格不能改标|BusinessCompatibility/ReadPublishedRelease、compatibility JSON、application_compatibility.py|PR29、C02/03|三业务actual metadata/维护门禁PASS；5份实际app/desktop工厂IL门禁PASS；schema3六记录重验PASS；旧反例拒绝保持|
|配置CAS/启动成功确认、扩展安装恢复/租约/退休、选装部署|原ConfigurationStore、ExtensionStore、原发布入口|PR24/26/27/29/30|精确Release quality-extensions/signed-business-compatibility PASS；实际CLI双reader+WCS配置/激活、显式启动失败保旧Effective及安全退休PASS|
|读取失效/乱序/恢复横幅、小视口暂停、多窗关闭排空|ParcelViewModel、SupplyStationViewModel、SportmasterWindowLifetime|PR27、C05/08|UI/窗口两组PASS；读错横幅原RED、恢复后仅清理自有横幅PASS；Windows原生后验另列|
|完整媒介与指南、原产物/历史证据保存|package-application.py、release_documents.py、manifest、归档/证据索引|PR27/29/30、本轮C04/06/07|5份ZIP实际逐文件/SHA及精确Binaries PASS；核心不夹带选装包；selected各含3精确包；全部233待保护原文件未改|

[固定证据索引](evidence/parallel-task-reconciliation-01/qualified-evidence-index.json)列明原始日志SHA、精确包来源/宿主指纹和验证DLL；[构建](evidence/parallel-task-reconciliation-01/qualified-build-summary.json)、[全部组合验证](evidence/parallel-task-reconciliation-01/qualified-verification-summary.json)、[六运行记录索引](evidence/parallel-task-reconciliation-01/qualified-journeys-index.json)、[实际CLI摘要](evidence/parallel-task-reconciliation-01/qualified-selected-cli.json)、[媒介核验](evidence/parallel-task-reconciliation-01/qualified-media-verification.json)、[实际版本/维护](evidence/parallel-task-reconciliation-01/qualified-actual-metadata-maintenance.json)均可追溯。12组实际Release DLL回归、3组精确签名扩展、6条core运行路径及独立selected CLI全部exit0；41项脚本与含Pillow环境的10项archive工具自测PASS。未受影响旧Supply沿用ed514f6锁定构建证据，不重复扩大为现场资格。

复现：使用固定SHA的新隔离checkout；以.NET10.0.402运行package-application.py分别生成3个portable core；使用build-extension.py针对实际core的Platform/Contracts和Assembly构建3个新精确包（RSA至少3072、自有获授权publisher/trust）；同一package入口传3个--extension-package生成Sportmaster selected portable/win-x64。执行本目录verify-media.py；selected-release-journey.py <新候选目录> <dotnet> <独立trust> <源码目录>运行合成HTTP对端与实际CLI。smoke-application.py的default、--optional-features、独立--peer-fixture及--attached-upload后用application_compatibility.py --record/--package重验。定向C#旗标与准确日志见summary，测试签名私钥及本机trust不发布。

## 失败、保留物与验收上限

- d4d448a候选媒介Binaries含不存在路径：**FAIL，已修复**；ed514f6媒介完整性及当时组合PASS，但后来补查的数字ID P1尚缺，因此其资格仍仅限原执行范围。两轮候选/包/原数据库不覆盖，当前21e6cae重新构建验证。
- C08两项回归在旧组合真实RED，当前PASS；SQL6原旧测试越过PR31守卫及DB4夹具残留SQL6的FAIL、PR29旧改标反例原PASS保留。没有删责任断言或吞异常换PASS。
- selected CLI前两次合成对端误取barcode字段、第三次前置active-call守卫拒绝，均保留原日志/库。夹具纠正字段与明确前置拒绝等待；未知动作不自动重试。最终真实CLI PASS。旧Windows IL探针误选窗口程序集Ipc.Desktop.dll的FAIL保留，正确工厂在Ipc.Assembly.dll，5份实际工厂均PASS。
- 系统Python缺Pillow曾使工具测试1项FAIL及archive图像诊断不同；换到具Pillow的现有运行环境10项PASS。全量public archive仍有起始main同样的**267项历史资产问题（265 PPTX media类型不匹配、2旧私密元数据标记）**，不改历史资产/不加例外。必须核对最后差集为零；这不是全仓archive PASS。
- Windows实际客户端、Dispatcher/HWND、DPI/触控、UAC/ACL、维护安装恢复、原生关闭竞争：**NOT_RUN**；macOS arm64跨构建/事件替身不代替这些验收。真实SDK、客户RCS/WCS/PLC、设备/实体、权限及现场：**BLOCKED**。按既有Windows后验清单，在Windows x64对同一ZIP/精确包运行并保存截图/回执，随后按客户合同治理编码/单位/认证/握手与跨站ID区间，联调超时/重复响应/实体处置；Production缺能力继续拒绝。
- 原审查01 PDF缺失，不能取得的外部会话/已删产物不能宣称取回；02原PDF及另两份公开PDF、原UI HTML/CSS/JS/PPTX/34PNG保持原字节和形成时边界。原始客户HTML/PRD、受限基线、两份旧delivery12本地副本、私钥/本机配置、缓存与测试库不上传；已授权通用实现、原公开报告和本轮合成证据有清晰索引。

源码、配置、脚本和原报告均进入同一Git历史，不创建第二套框架/配置/扩展平台。原D/RV编号与批准状态不倒写。发布参考媒介不是Production交付批准。

最终public archive差集：起始267、当前267、新增0/删除0；[差集回执](evidence/parallel-task-reconciliation-01/archive-error-delta.json)。历史FAIL保持，新增入口与清单没有新增错误。
