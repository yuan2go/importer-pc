# PR14 后最新复核与下一阶段决策

**任务：WP-IPC-AUDIT-RECONCILIATION-01｜2026-10-17 PDF发布及增量复核**  
**核查 main：0096f3435cc5066a1b50836ef534ec24221a7e6f**  
**FIX14 冻结实现：68cdc36f7821825c1ee9e53d866015be5ecf3044**  
**采用 UI：SS-UI-VIS-001 v1.1；下一实施包：[OPERATION-CLOSURE-15](../../work-packages/WP-IPC-OPERATION-CLOSURE-15.md)**

## 1. 直接结论

**现有架构仍适合继续开发，项目有局部实现偏离和操作闭环缺口，最高风险仍在异常接收责任。** FIX14 对原同 EvidenceId 矛盾落盘、实例健康归因、维护激活/草稿、无桌面通知和离线包引用已取得真实进展；不能继续把原 PDF 的 A01～04 全部列作未修。然而接收失败围栏按 RequestId 聚合，仍存在并发原查询中一份原件保存失败、另一份成功后掩盖前一责任的可达路径。它应作为下一包最高优先级修复，不应据 FIX14 的 PASS 或合并状态宣布恢复整体关闭。[E1](#e1)[E2](#e2)[E3](#e3)

**UI保留主工作台，修正关联操作；记录列表/详情需要局部结构调整。** 原来的共享箱、双通道、固定核对与动作区域和辅助抽屉可继续使用。箱级结果、历史对象语义、列表与详情分配、筛选标签、主次动作和就地禁用原因仍需完成；完整重建导航、视觉主题或更换WPF没有证据依据。新增维护草稿异常和扫码忙态条件路径应与这组操作修正一起完成。实际Windows客户端效果仍未验。[E4](#e4)[E5](#e5)

**下一步完成15中的接收责任、当前操作反馈、追溯与维护输入闭环，再进入已具备契约的真实联调。** 核心模块、历史查询、维护和UI可以继续开发；原请求异常恢复的扩展能力应等残余P1闭合。真实SDK/WCS、现场实体/数量/权限和Windows资格分别保持门禁；模拟成功不能放行现场。[E6](#e6)[E7](#e7)

本轮只修改审查、工作包和当前资料入口，保留原设计基准及业务代码。原 PDF 是8cab版本的历史判断，新增文档是0096版本的当前判断，两者不是冲突的“二选一”。

## 2. 版本、报告范围与证据是否可继承

|对象|核实身份与状态|使用边界|
|---|---|---|
|两份原项目/UI审查|均固定8cabf06d；采用v1.1；Linux x64；项目审查有关键构建/模拟/探针，UI审查主要为实物基准与WPF源检查，实际客户端截图0。|归并PDF保留原结论，不移植到新源码。原报告全文与问题可从本目录及项目审查入口读取。|
|本次上传PDF|27页、309806字节；SHA256 `7586e7c58f37266eea4a27af453a7a48fad86d14d7f225ba2824755ab457d441`。|逐字节保存；原“PR13开放/未提交/扩充FIX14”仅指形成时。|
|PR13|2026-10-17 10:34:46 UTC已合并；合并提交98c3977a08c372bffb9f8a3540b199b5df70e55f。|审查及14原任务定义已在main，不再等待承接开放PR。|
|PR14|2026-10-17 10:35:40 UTC已合并；main0096f34，PR头149b050。|代码确已合并；是否关闭仍看实现和测试场景。|
|FIX14运行|冻结68cdc36；macOS26.5/ARM64，SDK10.0.402/runtime10.0.13；不是本轮Linux运行。|核心真实SQLite/本地HTTP/发布宿主有证据；WPF仅交叉构建。|
|运行输入差异复核|167项运行、测试、示例、配置、脚本、根构建输入及两份随包指南；68cdc36→0096无差异；PR头149b050与0096完整树无差异。|可复用对应核心证据；不声称逐行审查167项。发布后指南更新需与原候选区分。|
|运行记录身份|19个final记录的日志SHA全部相符；PASS声明的非空尾部标记存在，3个原FAIL保留。|不是19项全部PASS；健康/发布通知的后继复验与收集器失败分开，原输出不改写。|
|本轮新增检查|Git/哈希、关键代码/断言、既有合成数据库只读检查、资料校验与远端文件回读。|未重跑业务测试/构建/打包；新残余路径没有新故障注入或Windows复现。|

详见[E1](#e1)。PR信息可由[PR13](https://github.com/yuan2go/importer-pc/pull/13)、[PR14](https://github.com/yuan2go/importer-pc/pull/14)核验。旧测试成功不证明未覆盖场景成功，源码变化也不自动否定所有旧证据。

## 3. 统一分类与五个维度

|分类|本次适用对象|
|---|---|
|符合设计|四类职责、一个业务权威和共享Runtime；原请求精确身份；原A01同ID冲突落盘；A02当前/辅助/历史健康分离；A03显式激活对象、A04原导入同源路径；R02/R03已验证核心保护。|
|经批准调整|受控签名离线后装、WPF原生控件实现、Windows统一后验；不按早期“只归档/只固定注册”倒退。|
|有合理依据但尚未批准的调整|用宽泛终态接收围栏换保守阻断有保护依据，但普通AfterT3中断也可留下不可通用解除责任；对恢复可用性的取舍不能当作已批准现场SOP。|
|未经批准且需要纠正的偏离|不同接收发生的责任互相覆盖；历史箱码被标作Item；列表/详情和主次动作失配；维护局部编辑依赖整份草稿合法。|
|本期应完成但尚未实现|箱级当前结果/原因、部分命令就地禁用原因、同版真实客户端操作验收；具体实现与验证缺口分开。|
|证据不足|忙态扫码/HID、异步选择时序、真实Windows几何/声音/ACL、完整旧扩展部署升级旅程、真实SDK/WCS及业务批准。|
|不属于本期范围|微服务化、万能事件总线、任意SDK热插拔、在线插件市场、第二套完整Inspection WPF、全品牌驱动、传图平台。当前已有headless/多根承诺不能再缩回“不支持”。|

|问题|一致结论|
|---|---|
|核心职责与依赖边界是否保持？|总体保持。Platform机制、Supply状态/持久化、Desktop应用契约、Adapters与类型化集成端口、Product/Assembly装配均有落点；兼容耦合不等于架构崩坏。[E2](#e2)[E3](#e3)[E6](#e6)|
|UI是否继承采用设计？|主体继承，部分状态语义及追溯布局仍有偏离；原型同源PNG不是客户端验收。[E4](#e4)[E5](#e5)|
|本期是否只有骨架或演示？|不是。SQLite事务、Outbox、原请求查询、独立插件、装配及通知均有实际运行；真实设备/合同仍缺能力，维护与异常细节尚未完整。[E2](#e2)[E3](#e3)|
|是否可运行、可追溯、可恢复？|参考正常链可运行可追溯；原冲突场景已修，接收失败残余和恢复可用性仍需收口，不能泛称恢复完整。[E2](#e2)[E8](#e8)|
|文档/代码/配置/设计/运行是否一致？|版本可追溯，但入口存在旧“14待实施/13当前候选/A全待修”叠加矛盾；本次原字节归档后更新有效入口。14历史检查和设计目标不倒写。[E1](#e1)[E9](#e9)|

## 4. 复核后的完整问题处置

编号沿用原报告；A/R在UI审查中的重复只计一次。R02与UI-R02、R03与UI-R03分别代表不同问题。新增残余采用后缀，避免把原子问题关闭误写为整条能力关闭。可机器读取的对应关系见[current-findings.json](current-findings.json)。

|编号/级别|0096处置|实现与证据；后续结果|
|---|---|---|
|A-01 / 原P1|原同EvidenceId失败窗口核心关闭；恢复整项部分完成|ResponsePersistence/Store新增正交冲突原件及T3；同ID异内容、迟到旧箱、真实SQLite回滚/重放、接受量唯一均有断言和原探针PASS。新残余见下两项。[E2](#e2)|
|A-01-R1 / 条件P1|新增确定源路径，必须修；故障注入待执行|同请求多个回执的接管成功/失败被RequestId集合混合，成功完成或重放可掩盖未保存原件的责任。不能继续给整体A01全绿。[E8](#e8)|
|A-01-R2 / P2|保守恢复可用性限制，需收口|已有AfterT3合成库中1条Context请求为Accepted、接受账0条、conflicts=0但有ReceiptPersistenceUncertain；原断言只验接受事实存活。须分清已完成与真正未核对接收，不能用通用解锁取消未知责任。[E8](#e8)|
|A-02 / 原P2|核心关闭，保留相关回归|逐实例Target/部署/epoch/sequence/时效，Supply只消费requests；主辅独立HTTP、反向故障/恢复/慢辅助/旧代/过期及原探针v2有证据。真实WCS健康契约另验。[E3](#e3)|
|A-03 / 原P2|核心接线和策略已验证；Windows未验|ActivateClick显式ConfigRevision→SendAsync→共享ConfigurationActivation；应用CAS/身份/实体/未决责任保留。不能继续报隐藏ConfigsList依赖。[E4](#e4)|
|A-04 / 原P2|原草稿覆盖核心关闭；维护整体有残余|每窗ConfigurationDraft，导入/普通字段/扩展/预览保存同源，实际应用保存重读验证存在。新增A-04-R1不是原覆盖问题未修。[E4](#e4)|
|A-04-R1 / P2|新增确定源路径，维护UI待修|选服务模块/新建实例读取默认目标却调用完整Draft.Build；数值处于空/非法编辑态时同步事件可无就地反馈抛异常。Windows进程结果未实测。[E4](#e4)|
|UI-01 / P2|保留|Context/CheckClose/Close的正常拒绝原件在库，当前箱投影仍无阶段/结果/原因。它不同于A01落盘丢失。[E5](#e5)|
|UI-02 / P2|保留|HistoryPresentationSession将箱码回填Barcode，XAML列仍称Item。须校正对象类型、列值、详情/复制/导出语义。[E5](#e5)|
|UI-03 / P2|保留结构问题，几何效果待测|主XAML仍为5*:6*固定分栏，未选详情仍占多数，固定列总宽1490；不可把旧估算529DIP当本轮测量。[E5](#e5)|
|UI-04 / P2|保留|筛选标签依赖Tooltip、日期完整时区字符串直接交用户；改可见标签和可用的本地日期时间输入，后台严格边界保留。[E5](#e5)|
|UI-05 / P2|保留，联动ReviewFits|只读详情仍套主动作样式/入口重复；收敛次动作时不能让ReviewFits因检查已隐藏控件而永久拒绝，也不能删可见性守卫。[E5](#e5)|
|UI-06 / P2|保留，限定部分命令|通道已有DisabledReason、扩展已有AccessFeedback；剩余关键命令只Tooltip应补就地原因，不重复重写已有反馈。[E5](#e5)|
|UI-07 / P3|随影响范围处理，独立优化暂缓|已有tokens/主题复用不彻底；只统一实际改动组件。没有证据认定运行缺字或全局不舒适。[E5](#e5)|
|R-01 / 条件P2|主消费路径已修，忙态风险保留|Persisted且作业/命令/版本匹配才清旧输入；但A等待SendAsync返回时文本仍可收B，结束键可能被忙态拦下，AB因版本变化不消费。现有测试用替换new而非真实忙态追加。[E4](#e4)|
|R-02 / 原条件P2|适用且核心关闭|两种headless宿主已承诺持续服务；实际交付/flush后显式序号确认，66次与发布宿主2次有证据，失败保留/64容量保护仍在。无需重建通知系统。[E2](#e2)|
|R-03 / 原条件P2|适用且核心关闭；旧根迁移/Windows另验|持久消费者登记覆盖离线/备份根，未知/不可读拒绝卸载；Effective/Pending/原Unknown有卸载拒绝验证，终态冲突的Pending/维护读侧有验证；未声明终态冲突加共享库卸载组合已动态执行。LegacyConsumersUnverified不能文案解除。[E2](#e2)|
|UI-R02 / 条件P2|保留|Accepted事实与当前来源/投影过期可同时成立；就近显示事实及当前可操作性，不能改Accepted为Unknown。[E5](#e5)|
|UI-R03 / 原条件P2|已实施；缺直接时序动态证据|PackageSelected catch/finally已核ticket，原错误路径已改；现有主要为接线断言，补A慢失败/B快成功真实可控时序与客户端。不能继续报未加代次。[E4](#e4)|
|UI-R04 / G-DPI|保留验证缺口|长原因/波次/拆包和新增标签竞争空间需实测；已有Wrap，不能指称无换行实现。[E5](#e5)|
|UPG-01 / 条件P2|新增升级旅程证据缺口|旧1.0.1两包在新宿主被明确拒绝、旧读者拒绝Storage4已有证据；完整旧Effective部署→1.0.2受控迁移未验证。[E7](#e7)|
|G-01|Windows后验保持|WPF实际启动/客户端截图、DPI/焦点/HID/触控/声音/UAC/ACL未验，不阻塞所有非Windows代码。|
|G-02|真实集成受阻|真实SDK、客户WCS及设备能力按B项逐个补，不静默模拟成功。|
|G-03|批准缺口保留|实体/复位、数量/拆包、身份/处置/保留规则按D项确认，模拟策略不升级为生产规则。|
|DOC-CURRENT|本轮文档修正|先保留0096当前入口原字节，再更新README/INDEX、有效文件中的过期执行时态和14→15导航；历史报告/设计不倒写。[E9](#e9)|

更早PR11永久包身份与真实AssemblyRef闭包、PR12实例改名的三个P2仍移出当前待修，原实现和回归断言保留。不因新发现而否认已经完成的独立问题。

## 5. 必须理解的根因与实际风险

### 5.1 A-01-R1：接收发生不能只按请求聚合

真实可达入口为多个共享Runtime的窗口或调用者并行原身份核对。`ProductRuntime.ReconcileAsync`登记任务但不按请求单飞；`ReconcileCore`在查询前取Unknown原请求，等待外部返回后交给Complete；IntegrationRuntime在途计数不串行同请求。它们允许两份查询都在Unknown阶段开始。[E8](#e8)

最小源路径反例：

1. Q1返回合法Rejected；`CompleteCore`将请求ID加入`incompleteReceipts`并建立围栏，但新ingress文件创建失败，未留下原件。catch仅留下内存StorageFailure。
2. 已在途Q2返回Accepted；CompleteCore继续接收，写原件/T3/删原件都成功，请求成为Accepted，随后`incompleteReceipts.Remove(requestId)`连同Q1未接管责任一起消失。
3. 受控停止等待查询任务结束，`CloseReceiptFence`看到集合空而删除围栏；重启没有Q1原件/冲突/不确定Block，且原请求已终态，原核对责任不再被发现。

另一路是Q1原件已保留但T3失败、Q2相反回执创建失败。重启时Unknown不进入终态围栏标记，重放Q1后可成为终态；请求级“有任一raw”也不能证明所有接收已接管。若出现残留`.new`，现有启动拒绝有保护；反例特指创建失败未留下原件的窗口，不能混淆。

这是源代码与调用生命周期确证，**本轮没有新运行故障注入，未宣称现场丢证或重复计量**。原同ID矛盾探针未覆盖这类组合。15须以可控查询返回顺序、文件接管失败、真实SQLite回滚和受控/异常重启取得关闭证据。修复应保留原回执、未能保存的事实、原请求与唯一接受账；不能靠禁止所有结果处理、删失败断言或通用解锁解决。[E2](#e2)[E8](#e8)

### 5.2 A-01-R2：保守拒绝与可恢复能力分别验

`RecoverReceiptFence`对围栏内终态请求无raw即加ReceiptPersistenceUncertain，`receiptOwners`又收集已终态请求。现有最终baseline的`crash-AfterT3/station.db`完整性检查正常，一条Context请求保持Accepted、接受账0条、冲突0，却有这一Block；`Program.cs:320`的AfterT3断言只验Accepted存活。它证明已提交的Context结果存活，不证明能恢复连续操作。

原14文档已经披露脏围栏会保守阻断，因此不能把此称作隐藏错误放行；但也不能把合理保守措施自动视为可接受的现场处置。15应明确具体接收窗口，验证已完成接收的重启行为与未接管原件的持续隔离分别正确。确无可信证据时继续阻断并给可追溯诊断；产品/对端/现场未批准前不加“备注后解锁”。[E8](#e8)

### 5.3 维护与扫码：编辑态、提交态、当前对象分开

A04原导入覆盖确已修。新增残余位于`ExtensionsView.ResetModuleEditor/NewInstanceClick`读取Integration默认值时却要求`ConfigurationDraft.Build()`解析全部整数。局部编辑允许暂时不合法，保存仍需严格验证；不能用吞异常或放松保存规则处理。UI-R03成功和异常分支的代次代码已经补齐，但仍需可控异步错误序列证明接线生效。[E4](#e4)

R01已补输入版本保护，不能删掉来强行清空忙态AB。需要定义并完整实现前一条处理期间的输入接收/隔离、清晰就地反馈和保留/重试行为，保证不串码、不无声丢码、不跨作业消费。现有业务防重和CAS继续保留；采用何种内部缓冲或交互实现由Codex按设计选择，不引入未经批准的自动提交。[E4](#e4)

### 5.4 UPG-01：格式升级不等于旧部署可直接运行

旧1.0.1包的公共DLL指纹与14不同，且不在唯一旧观察ABI允许例外内；检查日志为通用拒绝，结合签名清单与校验代码才可确定精确兼容边界。旧读者拒绝4且库hash不变是有效负例，不能当作完整升级成功。

当前Effective保留旧包时，换seed或只升库格式不会重写Effective；新宿主仍可能在装配旧包时拒绝。应先在匹配旧宿主/原包下清偿旧请求和实体/会话责任，并按实际已支持路径停用旧扩展/撤销旧绑定、受控重启，再显式备份升级及安装/保存/激活新1.0.2。具体顺序须在隔离合成旧部署上完成一条完整旅程；某真实部署不能清偿或无合法过渡能力时保持阻塞，不改原请求身份或签同版本新内容。[E7](#e7)

## 6. 模块级继续/暂停判断

|模块/活动|现在的决定|门槛与下一步|
|---|---|---|
|Platform、Product、Assembly主体|继续|保持职责和已有生命周期；仅为15真实问题修改必要机制/契约。|
|Supply异常接收/原身份核对|先修A-01-R1/R2|暂停叠加依赖“恢复已完整”的功能与交付承诺；不暂停无关业务。|
|实例健康/辅助诊断|继续|A02核心已有证据；涉及它时回归实际依赖归因，不重建。|
|主工作台/箱反馈/追溯|继续并行修正|UI01～06与UI-R02/R04关联验收；记录页局部重排。|
|维护与连续输入|继续修正，暂缓同类页面扩张|A-04-R1、R01忙态、UI-R03时序先闭合，保留A03/A04已修路径。|
|headless通知、共享多根保护|继续|R02/R03核心保留；旧库盘点和ACL独立验证，不缩承诺。|
|扩展升级/候选运维|局部待验证|UPG01完成可运行演练后才宣称旧部署可迁移；不影响全新合成开发根。|
|UI资源和长期舒适度|有限优化/部分暂缓|随本次组件收敛tokens；独立主题改版与广泛重构不进15。|
|真实SDK/WCS/设备|分项待联调|取得真实契约与授权后解除对应门禁；Production缺必需能力NotReady。|
|Windows实际UI/部署|已批准后验|已有获授权环境可做同版必要旅程；缺环境如实NOT_RUN，不新开VM/runner扩权。|
|现场上线|暂停|软件P1、Windows、真实接口/设备、客户语义和现场验收各有证据后再放行。|

## 7. 下一工作包与完成口径

[15完整工作包](../../work-packages/WP-IPC-OPERATION-CLOSURE-15.md)承接原PDF建议的UI补充范围和本轮新证据。它不是重复实施14，也不修改14的原AC要求。建议顺序：先当前基线/过期入口 → 接收责任与重启 → 同一事实模型的箱结果/恢复反馈 → 输入/维护与历史交互 → 必要升级旅程和同版客户端验收；按文件边界可并行。

允许自主决定内部类名、文件拆分、WPF布局实现、投影/持久化组织、测试入口和实现顺序；需要外部确认的是真实WCS矛盾处置与查询语义、设备/HID行为、实体/数量/身份规则、现场旧库盘点/升级停机策略和最终WPF使用条件。缺某一外部能力只隔离对应阻塞。

每项关闭至少绑定当前实现路径、实际源SHA、场景/命令/平台、结果或原始运行证据、残余门禁。代码接线与策略测试通过不替代WPF实图；静态截图不替代行为；PR合并不替代问题关闭。完整实施后交开发分支与main PR，遵守保护，不覆盖并行工作。

## 8. 引用与原始定位

以下路径行号均可在核查提交0096查看；文档发布后源码未改。E项是证据定位，不是本轮全部重新运行的声明。

<a id="e1"></a>

- **E1 版本与身份**：[输入核查](../../runtime/evidence/audit-reconciliation-publication-01/source-input-review.json)、[19记录复核](../../runtime/evidence/audit-reconciliation-publication-01/record-identity-review.json)、[原件清单](historical-inputs.json)、[14 checks](../../runtime/conformance-fix-14-checks.json)。
<a id="e2"></a>

- **E2 已实施核心**：[ResponsePersistence](../../../src/Ipc.Supply/ResponsePersistence.cs)、[Store](../../../src/Ipc.Supply/Store.cs)、[ConformanceVerification](../../../tests/Ipc.Verification/ConformanceVerification.cs)、[14证据目录](../../runtime/evidence/conformance-fix-14/README.md)、[冲突原探针关闭记录](../../runtime/evidence/conformance-fix-14/probe-contradiction-assertion.json)。R02见[HeadlessNotifications](../../../src/Ipc.Product/HeadlessNotifications.cs)与[published-notification](../../runtime/evidence/conformance-fix-14/published-notification.json)；R03见[ExtensionConsumers](../../../src/Ipc.Platform/ExtensionConsumers.cs)、[ExtensionMaintenance](../../../src/Ipc.Supply/ExtensionMaintenance.cs)。
<a id="e3"></a>

- **E3 健康**：[IntegrationRuntime](../../../src/Ipc.Product/IntegrationRuntime.cs)、[SenderModule](../../../src/Ipc.Product/SenderModule.cs)、[IntegrationHealth](../../../src/Ipc.Supply/IntegrationHealth.cs)、[健康探针v2](../../runtime/evidence/conformance-fix-14/probe-fixed-health-v2.json)及ConformanceVerification的HealthAndConsumers。
<a id="e4"></a>

- **E4 维护/输入**：[ConfigurationDraft](../../../src/Ipc.Desktop/ConfigurationDraft.cs):61–70；[ExtensionsView](../../../src/Ipc.Desktop/ExtensionsView.xaml.cs):128–144、186–195、243–250；[ConfigurationActivation](../../../src/Ipc.Supply.Contracts/ConfigurationActivation.cs)；[MainWindow.Extensions](../../../src/Ipc.Desktop/MainWindow.Extensions.cs)；[LaneControl](../../../src/Ipc.Desktop/LaneControl.xaml.cs):63–74；[ScanInputDraft](../../../src/Ipc.Desktop/ScanInputDraft.cs)；ConformanceVerification:127–201。新的异常/忙态路径尚无本轮Windows执行。
<a id="e5"></a>

- **E5 剩余UI**：[MainWindow.xaml](../../../src/Ipc.Desktop/MainWindow.xaml)、[LaneControl.xaml](../../../src/Ipc.Desktop/LaneControl.xaml)、[HistoryPresentation](../../../src/Ipc.Desktop/HistoryPresentation.cs)、[MainWindow.Closure](../../../src/Ipc.Desktop/MainWindow.Closure.cs)、[MainWindow.Presentation](../../../src/Ipc.Desktop/MainWindow.Presentation.cs)、[ViewModels](../../../src/Ipc.Desktop/ViewModels.cs)、[Workstation主题](../../../src/Ipc.Desktop/Themes/Workstation.xaml)、[Model](../../../src/Ipc.Supply.Contracts/Model.cs)、[v1.1实物](../../ui/ss-ui-vis-001-v1.1/README.md)、[原UI审查全文](inputs/ui-report-8cabf06d.original.md.txt)。
<a id="e6"></a>

- **E6 目标边界**：[framework](../../design/framework.md)、[framework-decisions](../../design/framework-decisions.md)、[controlled-extensions](../../design/controlled-extensions.md)、[D确认](../../readiness/decision-pack.md)、[真实集成05](../../runtime/integration-05.md)。
<a id="e7"></a>

- **E7 升级**：[legacy-compatibility](../../runtime/evidence/conformance-fix-14/legacy-compatibility.json)、[新候选身份](../../runtime/evidence/conformance-fix-14/candidate-inventory.json)、[旧13候选身份](../../runtime/evidence/arch-extension-closure-13/recheck-20261017/final-8cb475b/candidate-inventory.json)、[ExtensionPackages](../../../src/Ipc.Platform/ExtensionPackages.cs)、[ExtensionContractCompatibility](../../../src/Ipc.Platform/ExtensionContractCompatibility.cs)、[ProductRuntime](../../../src/Ipc.Product/ProductRuntime.cs)、[Maintenance](../../../src/Ipc.Supply/Maintenance.cs)。
<a id="e8"></a>

- **E8 新接收残余**：[StationApplication](../../../src/Ipc.Supply/StationApplication.cs):50–52、458–473；ResponsePersistence:7–35、40–64；ProductRuntime:286–308、375–387；IntegrationRuntime:150–176；[MainWindow](../../../src/Ipc.Desktop/MainWindow.xaml.cs):109–116、231。[本轮只读SQL结果](../../runtime/evidence/audit-reconciliation-publication-01/after-t3-readonly-review.json)核对现有[合成库包](../../runtime/evidence/conformance-fix-14/synthetic-databases.zip)中的AfterT3库与[Program验证](../../../tests/Ipc.Verification/Program.cs):320仅证明原Context结果保留。
<a id="e9"></a>

- **E9 文档处置**：[本次前置归档](../../archive/pre-audit-reconciliation-publication-01/README.md)、[publication checks](../../runtime/evidence/audit-reconciliation-publication-01/publication-checks.json)。原14报告/checks及旧PASS/FAIL/OPEN_AT_CREATION保持原形成时含义。
