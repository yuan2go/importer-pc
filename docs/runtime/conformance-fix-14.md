# FIX-14 实施、验证与交付

**A-01～04及适用R-01～03已实施并取得核心工程验证；WPF交叉构建通过。Windows实际UI/扫码器/触控/声音/ACL统一后验，NOT_RUN；真实SDK/WCS/现场与未批准业务规则仍BLOCKED，Production缺能力继续NotReady。** 不以核心通过、候选或PR状态替代Windows/现场验收，不改原审查的形成时结论。

源码冻结 `68cdc36f7821825c1ee9e53d866015be5ecf3044`，分支 `codex/conformance-fix-14`；开工及冻结复核main仍为 `8cabf06d3c6d53ba551b65e58a13db6cdcaba16a`，无后续代码提交。PR13/6406b4f只发布审查/本任务材料，未合并；本分支按原字节承接，不算main已实现。原主工作区未跟踪资料和其他工作树未覆盖。业务代码前的独立文档提交 `e8523a4` 已保存[原字节/SHA/blob与替代关系](../archive/pre-conformance-fix-14/snapshots.json)并解除旧当前指引；[有效基线与处置](conformance-fix-14-baseline.md)区分明确授权、现用待确认、建议、历史与后续。

[逐命令checks](conformance-fix-14-checks.json) · [新证据](evidence/conformance-fix-14/README.md) · [运行/恢复](operations.md) · [扩展指南](../extensions/developer-guide.md) · [Windows后验](validation/conformance-fix-14-windows.md)。本机macOS 26.5/ARM64、SDK10.0.402/runtime10.0.13；原审查Linux结果没有冒充本轮执行。

## 逐问题工程闭环

|项|实现与真实入口|核心工程/必要验证|遗留与放行边界|
|---|---|---|---|
|A-01|Supply/ResponsePersistence、Store、Maintenance、ExtensionMaintenance、HistoryQueries及原Complete/Reconcile；Storage4追加response_conflicts。新原件按请求+内容身份去重，保留原EvidenceId、来源、时间、Attempt和完整类型化响应；冲突与核对Block同T3。原终态、原响应、原发送尝试和接受账不覆写。|**IMPLEMENTED / VERIFIED**。同ID同内容、同ID异内容、不同ID矛盾、旧箱迟到、重启、接受恰一次、实际SQLite触发器回滚、原件目录不可写、原探针均验证。Pending/Unknown列表、原查询、业务守卫、历史详情/清理、维护预检及精确包保留均读取正交责任。|接收日志先Flush再T3，失败可验证重放；连原件无法接管时预存围栏在重启后形成ReceiptPersistenceUncertain，明确未保存原件而持续阻断。匹配查询不擦冲突；没有未经批准的通用解锁。真实合同/核对SOP不足继续阻塞对应责任。|
|A-02|Product/IntegrationRuntime、SenderModule，Supply/IntegrationHealth；独立IntegrationExtension。每实例按真实目标/部署指纹/epoch/sequence保存健康与时效，生命周期另列。|**IMPLEMENTED / VERIFIED**。实际ProductRuntime+独立HTTP：主好/辅坏、反向、恢复、慢辅助、过期、旧代次；必需requests依赖实际阻断。历史查询懒加载旧能力失败归原目标，保留原Unknown/终态责任，当前主目标不被污染。原精确包及在途Stop/Dispose/查询资源保护通过architecture。|Supply当前实际消费requests；其他命名端口没有业务消费者，故独立诊断。未来必需依赖须由业务声明，不能仅按“启用”猜关键性。真实WCS能力仍B-02受阻。|
|A-03|Contracts/ConfigurationActivation由应用与WPF共同调用；MainWindow.SendAsync、ExtensionsView.ActivateClick显式传本页选中修订。|**IMPLEMENTED / VERIFIED核心调用与WPF构建**。选中合法修订到真实Save/Activate返回Pending；无选择/无效/旧基线/换身份/两窗CAS拒绝；不依赖隐藏ConfigsList。源码链接策略、实际应用命令和WPF接线共同验证。|**DEFERRED Windows**：真实点击/焦点尚未执行；没有把纯策略当实机UI通过。实体、未决、权限、唯一Pending和包能力守卫保持应用权威。|
|A-04|Desktop/ConfigurationDraft是每窗唯一编辑源；普通字段/Devices与组件页Extensions/Version绑定同一对象；Effective独立只读。|**IMPLEMENTED / VERIFIED核心调用与WPF构建**。合法多设备/多服务/requests JSON导入、跨页字段/服务参数编辑、预览、真实保存/重读一致；非法导入保留原草稿，取消保留，异步schema选择代次隔离/读取期禁编辑；不同窗草稿/CAS验证。|**DEFERRED Windows**：实际控件、双窗、迟到schema输入旅程后验；安装≠激活，保存≠生效。|
|R-01|LaneControl → 同一SendAsync持久回执 → ScanInputDraft；LaneViewModel按当前作业切换输入所有者。|**IMPLEMENTED / VERIFIED**。实际ScanRetained A/A/B、Enter/Tab终止字符、拒绝保留、新输入后迟到、切作业隔离；仅同作业/命令/输入版本的Persisted消费文本，重复A不计量。|**DEFERRED** 实际Windows扫码器/焦点；专用箱码原焦点ticket保护继续，未新增生产人工Item录入。|
|R-02|既有Product serve和SupplyAssembly支持持续运行，均接HeadlessNotifications；stdout交付/flush后等待明确Sequence确认，不以写日志代替消费。|**IMPLEMENTED / VERIFIED**。真实SQLite/应用连续66次交付+确认，无溢出；消费者失败保留未确认并阻断；实际Product消费者未终止时保留库/资源，终止后重复清理释放。原closure队列阈值/失败保护继续。发布宿主的stdout通知/明确ack另有实际进程补充记录。|无确认继续保留队列，超过容量阻断；控制台交付不等于声音听审。主窗声音消费者和副窗不重复播放保持原责任。|
|R-03|Platform/ExtensionConsumers + 原管理/包锁；Product运行、Save/Activate、卸载/卸载恢复、备份/恢复入口登记/扫描所有消费者根。|**IMPLEMENTED / VERIFIED**。A停止、B无引用仍因A的Effective/Pending/原Unknown拒绝；根不可读拒绝，真实责任解除后允许；精确原包、反向依赖、安装身份和激活竞争保护保留。登记不因退出删除，不从UI分页推断引用。|**BLOCKED旧覆盖未知**：旧无登记库标LegacyConsumersUnverified，不宣称完整盘点；保留包/查询，需完整原数据根/副本盘点与批准迁移后才能退休。Windows保护目录/只读围栏/账户ACL仍后验。|

新局部实现不关闭原HTML的RV-01～17，不补造D批准、客户合同、SDK方法或实体/数量处置。旧原件、原审查、原失败、原12/13工作包、SQL/checksum及旧候选保持；[保留输入核对](evidence/conformance-fix-14/preserved-inputs.json)记录实际同字节证明。

## AC14-01～07

|AC|交付与证据|状态|
|---|---|---|
|01|最新main/PR/工作区核对；规定设计/12/13/运维/扩展/D/RV/公开范围、v1.1交接与HTML/CSS/JS/tokens读取；e8523a4先归档和清理后才业务源码。原UI图仅作设计，未冒称WPF截图。|**VERIFIED**；原未授权客户资料未进入分支。|
|02|A-01完整读写/恢复/守卫/保留链，真实SQLite失败与无法接管原件的持续保护；format1/2/3只读检查、备份显式升级、失败保旧、原checksum和旧读者/未来格式拒绝。|**VERIFIED**；原终态/接受量/请求精确身份不改，处置批准不足不解锁。|
|03|A-02逐实例事实、Supply实际依赖、历史原能力、超时/取消和原资源所有权；独立插件/HTTP/冻结宿主验证。|**VERIFIED** 核心/参考；真实接口BLOCKED。|
|04|A-03/A-04实际页面调用同一选择/守卫/草稿，实际应用保存/CAS/激活路径+WPF交叉构建。|**IMPLEMENTED / VERIFIED核心**，**DEFERRED Windows**点击/显示/输入。|
|05|R-01输入所有权；R-02已支持headless的明确消费；R-03已支持多根的离线检查和覆盖未知拒绝。|**VERIFIED** 核心保护；旧库完整盘点与实际Windows边界分别BLOCKED/DEFERRED，不撤能力换关闭。|
|06|固定SHA必要五选择器、两原探针副本、locked restore/WPF构建、原读者/精确插件边界、独立插件重复构建、同版解压启动/Production防伪，全部新日志。|**VERIFIED** Mac参考；Windows/SDK/现场未运行。|
|07|本报告逐A/R/AC、操作/扩展/设计/配置/版本/manifest；独立源码、测试公钥、三候选与远端逐字节回读，开发分支面向main PR。|源码/候选已交付；PR与资料对账回执以下方最终记录为准。|

## 必要验证与同版身份

每条实际argv/UTC/平台/退出码/完整scope标记及日志SHA在[checks](conformance-fix-14-checks.json)；最终日志均绑定68cdc36。没有盲跑全量reconciliation，也没有逐个循环所有旧选择器。

|范围|结果|证据|
|---|---|---|
|locked restore；解决方案含WPF交叉构建|PASS，0警告/0错误|final-restore/final-build|
|--conformance|PASS完整scope；A/R新增反例、实际SQLite、66通知、停止资源、离线根、显式1/2/3升级|final-conformance|
|--baseline|PASS完整scope；T1/T2/T3、子进程崩溃、唯一计量、恢复/清理|final-baseline|
|--followup|PASS完整scope；配置/CAS/激活/权限与持续双箱Unknown/原查询|final-followup|
|--closure|PASS完整scope；来源健康、历史/关闭、64容量/通知失败、独立资源清理|final-closure|
|--architecture|PASS完整scope；冻结宿主后独立插件、精确原请求/查询、依赖/永久包身份、慢Stop/Dispose租约与两个业务装配|final-architecture|
|矛盾原探针/健康原探针副本|PASS；原Accepted保持1、重启仍在恢复列表且阻断；主Ready/辅助Unavailable且错误归属真实，副作用请求0|final-probe-contradiction、final-probe-health-v2及adaptation|
|Storage/旧插件|PASS；实际13旧读者拒绝format4且DB字节未改；原1.0.1包对新宿主公共指纹不兼容则明确拒绝，不伪支持|legacy-compatibility.json|
|候选portable/win-x64/供包装配；两类插件各宿主重复独立构建|PASS；同源指纹一致、manifest/payload一致、冻结宿主不变|candidate-inventory、plugin-reproducibility|
|实际解压候选核心/两插件链|PASS；SQLite+独立接收端接受2，受控停止，核心7项与Production防伪通过|smoke-core、smoke-extensions及完整日志|
|Windows实际客户端/SDK/WCS/设备/现场|NOT_RUN / BLOCKED|W01～06沿13后置；B-01～06和D/RV不变|

Release=CONFORMANCE-FIX-14；Storage/MinimumReader=4/4；业务合同SIMULATION-v1、ExtensionContract1。宿主Platform SHA256 `96ab8f18f81748895f33021273d1f29167895bdb78d33c00659187f623ff6ba4`，Supply.Contracts `115e7f874fb1d65ac02c3d355d7c6012b723f30a853201e0c14554b3bed8d8e2`。两种参考包均1.0.2；Barcode独立源码6507edb4、Integration独立源码68cdc36；原1.0.0/1.0.1不重签。

[明确未验预发布下载](https://github.com/yuan2go/importer-pc/releases/tag/conformance14-reference-68cdc36)含三个候选ZIP、完整冻结源码、两类独立源码、两个ipcx、独立TEST ONLY公钥政策及精确清单/哈希。私钥不在库或附件。11附件已全部重新下载，CRC、精确文件集及内层SHA均核对；第12份回读收据亦已上传/下载同字节核对，见 [收据](evidence/conformance-fix-14/readback-receipt.json)；[远端回读](evidence/conformance-fix-14/remote-readback.json)。源码与后续文档提交、宿主与独立插件身份分别记录。

|候选|ZIP SHA256|
|---|---|
|供包portable|c51b548d957357d65bfba3e8e57910d8c1bdeeeb1e4e253ac3f13d6b3a8fe0b3|
|供包win-x64|24c3b1a9209b5035d0fe34282a7564141cb65bff28a348101f5afe5e4f90ecbf|
|SupplyAssembly|87ecfbbf1dc223f2feff7371011640209bdc4967033df673dad60a3c16639f05|

解压后依包内OPERATIONS/EXTENSIONS和SHA256SUMS操作；portable用固定.NET10，`dotnet app/Ipc.Product.dll --config app/config --data-root <新合成根> --action serve`，Fixture独立启动；SupplyAssembly用`dotnet app/Ipc.Assembly.dll --config app/config --data-root <新合成根>`。Windows只按同版station.ps1/provision脚本及[后验场景](validation/conformance-fix-14-windows.md)，不把交叉发布当启动验收。

## 失败、保守边界与发布记录

全部探索失败和完整日志保留，没有删有效断言、吞异常或跳关键路径换PASS。新增WPF文件首轮缺using、探针测试元数据不是40位SourceCommit、新证据ID未同步Task.Provenance被原合同正确拒绝、共享只读锁内另开读句柄失败，均定位修正。两个旧T3测试预期依据新“已Flush原件可重放”的持久能力调整：原回滚/不重发/原payload/独立接收账/唯一接受断言保留并加强，不以Expected改宽掩盖失败。

健康探针首轮失败来自串行完成假设：主两次完成时慢辅助尚Unconfirmed；v2只等待该实例真实完成，原业务不变。原核心冒烟收集器错误期待不存在的status字段，exit0/七结构项全PASS，但原记录保留FAIL；后续按原结构结果判完整，未重跑换日志。冻结前工作树日志不冒称最终同SHA验证，最终验收只使用有精确源/包/命令记录的结果。

无法接管的原件不会被重构或补造；非正常接收退出保守形成持续责任，无批准SOP不提供解锁。旧共享库原根覆盖未知继续保留精确包；不删除登记或以新根绕过。旧读者/插件拒绝是兼容边界通过，不能解释为旧请求已迁移或现场已修。

发布宿主追加证据：[真实控制台通知/确认](evidence/conformance-fix-14/published-notification.json)，两次交付/明确序号确认，接受2、Backlog0、进程退出0。首轮额外脚本将Host日志的State误当业务Snapshot并受本机代理影响，失败/原脚本保留；v2仅限定ProjectionVersion快照及loopback禁代理，未修改产品。

资料校验：10项工具测试通过。固定main归档与本轮均保留267项历史资产错误（PPTX媒体类型及原UI私密元数据标记），新增错误0，未加豁免、未降低断言；[逐项对账](evidence/conformance-fix-14/archive-comparison.json)。它们不被改写为业务测试失败或资料全PASS。公开证据仅做个人路径别名映射，原字节/公开字节SHA与完整顺序保留在provenance；没有复制受限客户材料。
