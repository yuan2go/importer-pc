# WP-IPC-ENGINEERING-CLOSURE-07：参考工程与操作工作台收敛

当前独立复核：[MAIN-REVIEW-03](../reviews/main-static-review-03.md)，基准main `677a5ad`（PR #6已合并）。8项指定整改路径静态补齐，E-06/E-10仍有工程缺口；全部运行NOT_RUN，Windows06/真实集成05分别保持NOT_RUN/BLOCKED。后续见工程增量08、参考验证09；下文07/更早记录保留形成时范围。

状态：**IMPLEMENTED / VALIDATION_NOT_RUN**。形成日期2026-10-16；依据 [MAIN-REVIEW-02](../reviews/main-static-review-02.md)，原分析基准main `cc3675fc9aa78a5b27d6c12c15e13c6d0a2856aa`。用户已明确授权先合并PR #5再实施本包；实际执行基准main `6d4063ec54e8167c8710e46befbe66f8b688ef08`，开发分支 `codex/wp-ipc-engineering-closure-07`。用户仍要求不测试；本轮不构建、不运行验证器或程序。

当前交付与逐项映射见 [实施记录](../runtime/engineering-closure-07.md) 及 [执行边界记录](../runtime/engineering-closure-07-checks.json)。以下勾选仅表示实现或文档已落地，不表示运行验收通过；用户要求不测试，运行验证全部NOT_RUN。

**目标：** 修复E-01～10，使当前参考产品的状态有效性、恢复证据、运维安全和UI操作信息形成可审查闭环，再提交Windows操作验收。

**架构：** 继承既有C#参考产品，保持一个业务后台所有者、一个发送器、多页面/窗口共享业务；基座负责生命周期与来源机制，Supply拥有命令守卫和事实，UI消费投影。只补必要契约和前向存储演进，不引入另一套框架、动态插件市场、微服务或万能总线。

**技术基线：** 当前仓库锁定的.NET10/WPF/GenericHost/SQLite及依赖锁；本轮已从合并#5后的main执行；尚未取得获授权旧现场工程，当前增量仅作用于独立参考产品。

## 全局约束

- 从最新main、当前INDEX/framework/decisions、本包及原工作包/相关D/RV开始；核验工作区和开放PR。保留并行修改及受限未跟踪文件。
- 此处E编号仅追踪新实现缺口，不改REQ/BR/CAPI/D/RV。历史issues、验收原记录和schema1 SQL/checksum不可覆写。
- T1/T2/T3、固定requestId/payload/hash/contract/binding、唯一接受账、Unknown不重发、Prepared非交付证据、实体/预留待处置不回退。
- Simulation必须可辨；Production缺SDK/合同/实体/数量/身份仍NotReady。未知来源时间不能通过刷新投影“变新鲜”。真实TTL/动作阈值不从模拟参数推导。
- UI不持设备/数据库，不通过切页或切布局重建业务。不得发布客户原稿/衍生视觉资产、SDK/字体、凭据、生产数据或私密路径。
- 本次“不要测试”持续适用于本复核及其直接续作，除非后续用户明确恢复验证。下面验收是将来需要满足的判定条件，**本次不执行**，不能生成PASS或伪造日志/截图。未获恢复验证授权的实施交付必须保留NOT_RUN。
- 原则上每个切片独立提交；本轮共享模型/来源/格式演进的核心原子提交范围裁决见07实施记录，桌面与部署随后分开提交。在开发分支创建main PR，附实际commit与边界，不强推、不自行合并。每次只更新本包相关过期入口。

## 切片1：当前输入失效与来源核对（E-01/E-03）

**责任文件：** `src/Ipc.Supply/Model.cs`、`StationApplication.cs`、必要时`Store.cs`；`src/Ipc.Desktop/ViewModels.cs`、`MainWindow.xaml.cs`。后续验证落点：`tests/Ipc.Verification/`中新场景及既有H04/Alignment场景，当前不执行。

**接口边界：** 保留当前`Command(Id,ExpectedRevision,Kind,OwnerId,Value,Quantity,EvidenceVersion)`与`CommandReceipt`幂等语义；新增观察核对的类型化选择值，明确`Code/Scope/Source/SourceEpoch/EvidenceVersion`，由应用解析，不能只传一段自由文本后全局清除。改变公共接口时同步所有调用者，不让UI继续调用旧无版本解除入口。

- [x] 先按Read三条路径登记字段有效性表：新有效输入待校验、无码/多码/非法、旧权威结果；把最后已证实值与当前可执行值分开，保留原观察和旧请求历史。
- [x] 在同一T1修订中使旧Task/Remaining/Supply对新Read失效；UI按有效性显示“未取得/待校验”，不从历史或当前配置补造SKU/数量。
- [x] 把来源观察核对纳入有commandId/expectedRevision的命令路径；确认对象包含每个被检查的来源/代次/版本与依据。
- [x] 每次新拒收推进相应来源证据版本，不能沿用SetBlock已有项而忽略新证据；执行时拒绝缺失、已变化或未选中的证据；新观察拒收/另一来源不被旧确认解除；重复同内容命令返回原回执。
- [x] 同步操作提示，明确核对不释放外部预留/实体，不清Unknown、过载或其它阻断。

**独立完成条件：** 同实体在拒绝后先重扫无效码、再重扫合法码等待新回执时，当前任务区没有旧有效数量；旧详情可查。旧证据确认与新ObservationReview竞争时，后者保留。此切片可在合成数据完成，不等真实WCS。

## 切片2：来源健康、连接代次与资源所有权（E-05/E-09/E-10）

**责任文件：** `src/Ipc.Platform/DeviceCapabilities.cs`、`Lifecycle.cs`；`src/Ipc.Supply/Model.cs`、`StationApplication.cs`；`src/Ipc.Product/ProductRuntime.cs`；`src/Ipc.Adapters/ObservationSources.cs`；`src/Ipc.Desktop/App.xaml.cs`；相关受控配置/运维说明。与W-05/B-01/B-03的边界分别记录。

**接口边界：** `DeviceState`新增可靠确认/有效性语义；设备端口提供由运行所有者注册的连接代次转换与健康事实，区分状态改变时间和最近确认时间。Supply只接纳已注册代次；UI投影包含来源有效性，不能通过GeneratedAt代替。机级租约根属于部署配置，不属于普通展示偏好。

- [x] 记录健康有效性的工程契约：来源、状态时间、可靠确认时间、失效证据/期限、连接代次；模拟源明确发出合成确认，真实未知期限继续阻断。
- [x] 加入受控新代次注册流程；旧代次健康/观察不能覆盖，新合法代次可到达Connecting/Ready；原实体许可仍因代次变化失效，不自动重新授权。
- [x] 让业务守卫、报警、UI共同使用来源有效性；刷新只读投影不延长设备有效期；有效确认可在状态未变化时更新新鲜度。
- [x] 停止/释放逐项收集异常，继续清理独立可安全释放的资源；故障来源仍回调时保持资源所有权，记录无法终结的任务，不能过早释放原生缓冲或设备租约。
- [x] 统一支持账户范围及工位/设备租约目录策略：需要跨账户时部署机级受控目录/ACL；否则明确固定账户限制。同一数据根锁和真实设备端控制权继续分开。
- [x] 保留原始启动失败原因与清理失败，不让清理异常覆盖唯一启动诊断；同步普通账户部署/退出/重启说明。

**独立完成条件：** 静默来源过期撤销就绪；新合法代次可连接、旧回调被拒；一源Stop/Dispose抛错不跳过另一源和其它独立资源清理。跨账户实机结果属于W-05；SDK缓冲/取消证明属于B-01，不能用合成源关闭。

## 切片3：只读预检查、升级前备份与实际启动选择（E-04）

**责任文件：** `src/Ipc.Supply/Store.cs`、`src/Ipc.Product/Program.cs`、`scripts/station.ps1`、`docs/runtime/operations.md`。保留现有`RestoreVerified`隔离规则。

**接口边界：** 将“只读打开并报告schema/format/未决/隔离/兼容性”“一致性备份”“显式前向迁移”分开；preflight/backup不能调用会迁移的默认构造路径。维护输出包含源格式、读取者兼容性和备份身份，绝不以新格式备份声称可供旧EXE回滚。

- [x] 建立无迁移只读检查路径，未知格式/校验和/未决保守拒绝；错误退出不写schema、状态、PRAGMA持久设置或新初始化数据。
- [x] 一致性备份在原格式上完成并校验；备份后才允许独立、受控的显式迁移，失败保留源库及备份。
- [x] 把升级前程序/配置/format/备份指纹绑定到操作回执，区分format1→2和format2原版本维护；回滚只能使用匹配旧版本的备份、新根和隔离核对。
- [x] 明确一个启动选择协议：要么Run与Upgrade/Rollback共享解析active-release并校验，要么统一显式Release并移除无消费者的指针行为。不得指针写完就报告程序已切换。
- [x] 完成失败保旧的操作文档：新程序检查旧库、预检查拒绝、备份失败、迁移失败、启动失败和回退隔离各有具体输入/输出边界。

**独立完成条件：** format1在preflight/backup成功或失败后仍是format1，备份可供匹配旧程序读取；未知格式不被改写；实际启动身份与升级/回滚选定版本一致。Windows脚本执行另属W-05，当前不执行。

## 切片4：当前任务、常驻状态和提示事件（E-02/E-07/E-08）

**责任文件：** `src/Ipc.Desktop/MainWindow.xaml`、`MainWindow.xaml.cs`、`LaneControl.xaml`、`ViewModels.cs`；`src/Ipc.Supply/Model.cs`及应用投影；必要的Product提示事件所有者。保持ViewRegistry和共享Runtime。

**接口边界：** 输入为版本化业务/健康投影和有序提示事实；输出为每窗独立展示状态。提示播放仅一个所有者，有界队列/序号去重；不让每个窗口各自播放，节流不能静默消费必须提示的事件。

- [x] 优先排布当前操作对象的条码/SKU、任务、供给/保留/移除量、有效计数及动作；在单列中维持清楚通道关联，折叠非当前必核信息，保留另一通道异常提示。
- [x] 常驻工位/模式/有效身份、CAM0/CAM1/WCS健康来源和报警计数/入口；未知/过期独立呈现。技术修订和长来源ID放可展开区，不去掉Simulation事实。
- [x] 非拆包状态隐藏拆包输入/清空；主操作确认内容引用当前对象/版本和关键数量；对象变化时原确认不能落到新作业。
- [x] 用带身份和序号的提示事件接收接受/拒绝/拆包变化，保存单所有者仲裁/合并策略；同次两通道事件必须各有可解释的声音/可视结果，不在节流后直接标已消费。
- [x] 建立Windows操作清单与状态矩阵，覆盖布局/滚动/长内容/输入焦点/双通道提示，全部先填NOT_RUN；不把XAML完成当W-01～04通过。

**独立完成条件：** 当前任务的必要信息与可执行动作形成明确对应，固定按钮不能孤立于核对对象；两个近邻提示事实不会被轮询/节流吞掉。实际可读性/触控/听审只在06包登记。

## 切片5：历史会话与关闭链、查询状态（E-06）

**责任文件：** `src/Ipc.Supply/Model.cs`、`StationApplication.cs`、`Store.cs`；`src/Ipc.Desktop/MainWindow.xaml`、`MainWindow.xaml.cs`、`ViewModels.cs`。

**接口边界：** 应用新增类型化历史查询，至少表达会话/箱、作业或请求状态、通道、日期范围及分页；列表结果含稳定ID/排序。会话可导航到Context/CheckClose/Close及其Attempt/Response/Audit，作业继续多Request详情；查询无副作用且无权时返回明确失败。

- [x] 从现有Store.Sessions/RequestsForSession补会话历史应用查询，关闭链可由旧箱发现，不依赖知道requestId；不只把当前Session按钮搬位置。
- [x] 用明确字段过滤替代整段JSON任意命中，补日期/状态/通道、完整日期与时区；需要索引/结构迁移时追加新迁移，不改schema1或id2。
- [x] 分离Loading/LoadedEmpty/Loaded/Error；查询失败保留旧数据时明确标“旧结果”，不伪装新条件查询成功。
- [x] 保持selectedId、窗口独立筛选/页码/详情/滚动；异步查询与详情均带代次，迟到结果不能覆盖后来查询。
- [x] 展示跨箱关闭、无作业箱、拒绝/未知、多请求多尝试、旧字段缺失的证据；接受计数仍读唯一事实账。

**独立完成条件：** 换箱后可找到旧箱关闭检查/关闭回执；空箱也可追溯。跨日期/状态查询与空/失败可区分，当前通道变化不改历史字段。真实WCS少货明细仍等B-02/B-04。

## 交付与验收记录

- [x] 每个E逐项记录实现文件、commit、静态/模拟/Windows/真实边界；修复完成但未执行验证时写“已实现待验证”，不得写PASS。
- [x] 更新runtime实施路径、配置/部署/回滚说明、manifest和新包身份；不覆盖ALIGNMENT/H04原检查JSON。
- [ ] 后续明确恢复验证授权后，按受影响模块、关键恢复/防重/资源、必要构建与发布启动范围验证；以仓库当时真实命令为准，失败须修复，不删断言或吞异常。
- [x] 已创建[main PR #6](https://github.com/yuan2go/importer-pc/pull/6)，提供工程完成/Windows未验/接口受阻分别清单。06只对同一源提交/包留证；05按能力解阻。未授权测试时不为生成发布包而暗跑构建/冒烟。

退出条件：E-01～10均有实现或有证据的范围裁决，未验证显式列出；W/B仍可OPEN/BLOCKED。此退出不代表生产放行、旧工程迁移完成或历史RV全部关闭。
