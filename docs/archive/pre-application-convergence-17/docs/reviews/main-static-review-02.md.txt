# 最新 main 架构与供包 UI 独立静态复核

复核日期：2026-10-16（Asia/Shanghai）。工作包：WP-IPC-MAIN-REVIEW-02。

**结论：参考工程已有主体实现，仍有10项工程缺口；Windows 操作验证未完成；真实 SDK/WCS 与生产规则仍受阻。不能将上一包的核心验证扩大为完整 UI 或生产验收。** 本文只依据代码和文档静态推导，没有运行程序复现。

## 1. 身份、范围与方法

- 分析基准：远端 main `cc3675fc9aa78a5b27d6c12c15e13c6d0a2856aa`；PR #4 已 MERGED，合并时间为 2026-10-16 20:41:37 +08:00。复核开始和交付前分别核对 main 与开放 PR。
- 当前参考代码源提交：`712446ae262c6b269bd6576ad5c17784a644c7d7`；ReleaseVersion `ALIGNMENT-01`、StorageVersion `2`、ContractVersion `SIMULATION-v1`。文档合并提交与二进制源提交分开。
- 初始工作区 main 与远端相同，有未跟踪受限资料；本次在基于上述 main 的独立 worktree、分支 `codex/wp-ipc-main-review-02` 写文档，不修改原工作区资料或业务代码。
- 已读：INDEX、ALIGNMENT-01/RUNTIME-03/H04工作包、framework/decisions、runtime记录、D决策包、历史17项RV、公开范围、当前各层源码/脚本及相关验证源码。只读历史结果，不执行其中的脚本或验证。
- UI核对同时读取获授权本地PRD、UI输入与交接正文。公开报告只写新的通用工程判断和既有编号；不复制客户原文、组织信息、原型、图片、私密链接或本地受限位置。它们仍不是公开main中的完整规格，也不因读取而获得公开许可。
- 用户明确要求“不要测试”：本轮测试、资料验证器、构建、发布、启动冒烟、浏览器/WPF运行、Windows/SDK/WCS/设备动作均 **NOT_RUN**。Git/PR读取、源码阅读、文档整理与文件摘要不属于运行验收。

本文 `E-*` 是新的工程复核索引，`W-*` 是Windows待验索引，`B-*` 是外部阻塞索引，不取代 REQ/BR/CAPI/D/RV。严重度是参考工程整改优先级；没有已发生生产事故的证据。

## 2. 已有实现应继承

|设计责任|main中的实现|本轮能够确认的范围|
|---|---|---|
|基座机制、业务所有权|Platform显式产品定义与结构校验；SupplyConfiguration拥有双通道/数量约束；通用观察端口在Platform|不再列“无框架”“Platform完全硬编码供包”为当前问题；仍有兼容配置DTO，不要求另造框架|
|多页面/布局共享实例|ProductHost单例；MainWindow第二窗口复用Runtime；ViewRegistry实际选页；布局只改展示|源码有共享所有权；真实双窗口操作仍W-02待验|
|任务来源链|ContextSnapshot/TaskSnapshot、来源/版本/证据/时间、箱波次与Item波次分别保存，原请求固定|字段与存储路径已有；E-01是修订时失效问题，不是整个任务链缺失|
|可靠发送与恢复|T1/T2/T3、原请求与绑定、Attempt、唯一接受账、Unknown核对、Prepared非交付证明、本地终结/待处置|关键保护路径存在；不能据静态阅读重报PASS，也不解除外部处置阻塞|
|后台隔离|StationDispatcher后台串行所有者；网络在事务外；Snapshot不写报警；健康/观察队列有界|不能继续指称当前UI直接同步读写SQLite；健康来源有效性另见E-05|
|UI基础|当前箱、双通道任务/数量、固定供包带、结构化请求详情、独立工程页、维护表单、确认窗安全默认焦点|已有工程实现；仍须区分E类结构缺口与W类实测|
|模式隔离|SupplyConfiguration明确拒绝Production；仅manual/replay与loopback模拟合同|缺能力拒绝业务符合约束；不是生产实现完成|

上一包 `alignment-01-checks.json` 的PASS为原执行记录，本轮未重跑；Windows为NOT_RUN、真实接口为BLOCKED的边界继承。历史HTML没有被本包修复，RV-01～17继续OPEN。

## 3. 工程缺口（无需等待真实SDK即可推进）

### E-01 / P1：重扫修订没有完整撤销旧任务与数量的当前有效性

证据：[StationApplication.cs](../../src/Ipc.Supply/StationApplication.cs) 311～323、518～524行；[ViewModels.cs](../../src/Ipc.Desktop/ViewModels.cs) 90～96行。依据：framework §6.3/8/10，RV-02/03/04，REQ-035/036。

同实体已有合法校验结果、之后业务拒绝再重扫时，无码/多码/非法输入分支只更新Status/Reason，旧Barcode、Task、Package、Remaining、Supply继续作为当前字段。合法纠正分支只清Task、不清Remaining/Supply；新校验未回执时，UI仍直接显示旧数量。旧事实可以保留在历史，但不应继续显示成新观察/新修订的有效任务。守卫阻止此时提交，不等于显示语义已正确。

完成条件：当前观察、待验证输入、最后已证实结果分开；新Read立即撤销不再适用的任务/数量有效性，无效观察如实显示无码/多码/格式问题；旧请求与旧快照完整留史。不要仅清界面或删除旧账。

### E-02 / P1：固定动作带未绑定持续可见的任务核对信息

证据：[MainWindow.xaml](../../src/Ipc.Desktop/MainWindow.xaml) 7～13行，[LaneControl.xaml](../../src/Ipc.Desktop/LaneControl.xaml) 3～14行，[MainWindow.xaml.cs](../../src/Ipc.Desktop/MainWindow.xaml.cs) 226～244行。依据：framework §10，RV-13/14，REQ-017～022/035/036。

整个箱区、左右任务和拆包计数都在同一个WorkScroll内；窄/矮有效区把右通道排到下一行，但左右供包按钮仍固定且按业务守卫启用。源码没有保证点击某通道供包时，其条码/SKU、供给/保留/移除量和计数仍可同时核对。800×420的几何门槛只检查总客户区，未证明内部当前任务可用；本轮没有测量任何实际控件遮挡或DPI结果。

完成条件：按当前通道提供紧凑且持续可见的任务摘要/计数与动作关联，长ID/来源/最近记录按需折叠；单列下清楚切换当前操作对象，保留另一通道事实。不要通过缩小文字或只固定按钮宣称适配完成。具体几何验收属于W-01。

### E-03 / P1：观察核对解除入口不能约束到已检查的来源证据

证据：[Model.cs](../../src/Ipc.Supply/Model.cs) 的 `ReviewObservationsAsync(string evidence)`；[StationApplication.cs](../../src/Ipc.Supply/StationApplication.cs) 695～704行；[MainWindow.xaml.cs](../../src/Ipc.Desktop/MainWindow.xaml.cs) 424行。依据：framework §6.2/6.4/9，F-10，RV-01/02。

该入口只有自由文本；后台执行时按Code删除全部ObservationReview，不带commandId、expectedRevision、scope/source/epoch/evidenceVersion。在维护员看过旧投影、随后产生新来源拒收的时序中，旧说明可以清掉未检查的新阻断或另一来源阻断。SetBlock（725～727行）在同Code/Scope/Source已有项时也不更新证据版本，不能只给UI补一个旧版本字段。它不清除Unknown或Overload，但仍缺“恢复只解除对应证据”的约束。

完成条件：按来源/代次/证据版本选择待核对项，应用入口CAS复核并保存幂等回执；新发生不被旧确认解除；多来源分别留痕。继承ClearOverload已有的证据版本机制，不改成全局恢复按钮。

### E-04 / P1：预检查和备份可在备份前迁移旧库；升级指针未接入启动

证据：[Program.cs](../../src/Ipc.Product/Program.cs) 11～35行，[Store.cs](../../src/Ipc.Supply/Store.cs) 41～73行，[station.ps1](../../scripts/station.ps1) 28～33、45～61行。依据：framework §11，F-09/12。

当前程序的preflight/backup均先 `new SupplyStore(dbPath)`；该构造器以ReadWriteCreate打开并将schema1前向迁移到format2，然后才检查未决或创建备份。因而“拿新程序检查/备份旧库”会先修改源库，甚至检查随后拒绝也已迁移；这不能生成旧EXE需要的升级前schema1备份。用旧发布脚本操作旧库的正确路径应保留，不能把风险扩大为所有既有升级都必然失败。

另一个独立步骤缺口：Upgrade只写active-release.txt，Run使用Release/PSScriptRoot，从不读取指针，当前入口不形成统一切换行为。

完成条件：独立只读兼容/未决检查和一致性备份，显式迁移入口及升级前备份身份；源库在失败预检查后不变。启动解析与升级/回滚选择形成闭环，或明确删除无消费者的指针协议，统一显式Release行为；不能仅改文案称升级成功。原库/旧备份保留，恢复仍隔离。

### E-05 / P1：投影新鲜不代表设备健康有效，新连接代次缺少可达接纳路径

证据：[DeviceCapabilities.cs](../../src/Ipc.Platform/DeviceCapabilities.cs) 5～14行，[StationApplication.cs](../../src/Ipc.Supply/StationApplication.cs) 354～366、526～570行，[MainWindow.xaml.cs](../../src/Ipc.Desktop/MainWindow.xaml.cs) 140行。依据：framework §4.1/5.2/6.2/9，RV-01/03/09。

设备守卫只看Health==Ready，未按健康源时间/有效期撤销；相同epoch和health的重复健康通知在更新时间前即返回。Snapshot每次读取重新生成GeneratedAt，因此UI的3秒限制不能证明底层设备状态仍新鲜。另在非not-connected之后，SetDeviceCore无条件拒绝不同epoch的健康，只有进程启动重置；现有端口没有显式注册合法重连新代次的步骤。不能用接纳任意新epoch修复，否则迟到旧回调也可覆盖。

完成条件：区分状态改变时间与最后可靠确认/失效期限，注册受控连接代次转换，旧代次拒绝；守卫和报警使用来源有效性。模拟时钟/模拟重连可以先实现；真实SDK心跳含义、超时阈值与设备事件能力仍B-01/B-03，不拍脑袋给生产TTL。

### E-06 / P2：历史关闭会话不可发现，查询仍缺明确条件和状态

证据：[Store.cs](../../src/Ipc.Supply/Store.cs) 100～108行，[Model.cs](../../src/Ipc.Supply/Model.cs) IStationApplication，[MainWindow.xaml](../../src/Ipc.Desktop/MainWindow.xaml) 15行，[MainWindow.xaml.cs](../../src/Ipc.Desktop/MainWindow.xaml.cs) 256～304、350行；[ViewModels.cs](../../src/Ipc.Desktop/ViewModels.cs) 130～142行。依据：framework §6.4/7/8/10，RV-06/16，REQ-037/040/046。

Store虽有Sessions，但应用契约/页面没有历史会话查询；History只列operations，Details(operationId)只读同owner请求，关闭owner为sessionId。“当前关闭详情”只取当前Session。换新箱后，旧箱关闭检查/关闭请求缺少可发现入口，不能要求操作者从外部知道ID。普通查询是对整段JSON的instr文本匹配，缺日期/结果/通道的明确条件；时间缺年份/时区。刷新替换整个结果源，未保留选中ID，也没有独立的加载/空/失败状态，失败时旧结果可能继续留屏。

完成条件：会话→作业/关闭请求均可导航，使用历史快照；查询类型化、分页稳定，明确空与失败，保留选区及详情代次；列表展示日期/时区。已有Operation→Request→Attempt树继续继承。

### E-07 / P2：常驻操作信息不完整，低频技术信息占据主层

证据：[MainWindow.xaml](../../src/Ipc.Desktop/MainWindow.xaml) 4～13、41～44行；[MainWindow.xaml.cs](../../src/Ipc.Desktop/MainWindow.xaml.cs) 82～95行。依据：framework §9/10，RV-04/05/14/15，REQ-006/026。

顶栏没有物理工位ID、独立WCS连接/来源时间、常驻活动报警入口/计数；正常就绪时也无法直接辨认WCS状态，AlarmSummary只在恢复页，设备主要在通道/诊断页。全局阻断带跨页存在，这部分已改善；但投影修订/来源长证据默认占主层，主屏还始终显示非拆包计数输入、不可用清空按钮。

完成条件：工位、模式、身份有效性、CAM0/CAM1/WCS健康与报警常驻且作用域明确；未知不伪装正常，技术ID/来源放可展开诊断。拆包操作按状态显现；不掩盖Simulation或未批准规则。独立WCS源投影需先有E-05机制；真实合同仍B-02。

### E-08 / P2：提示声按快照轮询且全局节流会丢应提示的状态变化

证据：[MainWindow.xaml.cs](../../src/Ipc.Desktop/MainWindow.xaml.cs) 21、70～81行。依据：framework §10、供包UI三类提示声要求（REQ-025）。

同次刷新两个通道进入需提示状态时，首个播放后更新lastSound，另一个被800ms门槛压掉却仍更新audible状态，之后不会补播；轮询间发生并结束的状态也没有独立事件消费。事件是否实际发生过、是否需要声音、是否成功播放没有分开。Windows听审决定音色/音量，但上述丢失来自代码仲裁。

完成条件：有界提示事件/序号、去重和仲裁由单一所有者处理；相邻两通道结果不能静默丢掉必要提示，可合并但需表达来源并保持等价可视信息。重启/新窗口不重放全部旧成功。现场可辨性属W-04。

### E-09 / P2：资源停止/释放遇到单项异常时可能跳过后续清理

证据：[ProductRuntime.cs](../../src/Ipc.Product/ProductRuntime.cs) 203～208、274～285行，[Lifecycle.cs](../../src/Ipc.Platform/Lifecycle.cs) 46～50行。依据：framework §4.1/5.1，RV-09/10。

ObservationModule.StopAsync在逆序source.StopAsync处没有逐项捕获，某个源抛错则后续来源、队列完成和worker等待被跳过。ModuleCatalog的外层Stop会继续其它模块，已有保护应保留；但Dispose仍顺序await，ProductRuntime.Dispose遇到catalog异常会跳过application/store/journal/租约释放。上一包已覆盖“停止屏障写库失败仍停模块”，不能把该用例当成“任意适配器释放失败”已覆盖。

完成条件：每个拥有资源均尝试受控清理并汇总异常，后台任务终态可观察；未停止的SDK回调/worker仍使用资源时禁止过早释放底层内存或让新实例抢占。先用会抛错的合成源明确所有权；真实原生资源验证仍B-01。

### E-10 / P2（跨账户部署条件）：工位/设备租约默认路径是用户级

证据：[ProductRuntime.cs](../../src/Ipc.Product/ProductRuntime.cs) 50～64行，[Lifecycle.cs](../../src/Ipc.Platform/Lifecycle.cs) 55～73行；[App.xaml.cs](../../src/Ipc.Desktop/App.xaml.cs) BootOptions。依据：framework §11，RV-09/10。

文件锁能保护相同路径下的实例，但默认设备/工位lease放LocalApplicationData，桌面入口不提供机级LeaseRoot选择；不同Windows账户的默认路径不同。若部署允许跨账户/登录会话运行，同工位/设备键不会在同一目录争用，现有同账户第二进程记录不能证明这一边界。当前参考工程未连接真实设备，没有“已经发生设备争抢”的证据。

完成条件：明确支持的运行账户边界；部署需要跨账户时提供受控机级所有权与ACL，不能让普通用户任意覆盖租约根。若首版仅支持固定运行账户，在部署契约中明确并在目标环境确认；不以文件租约宣称设备端控制权。

## 4. Windows待验证（W类均NOT_RUN）

|索引|目标|必须留下的同版本证据|不能被什么替代|
|---|---|---|---|
|W-01|1920×1080、1366×768各100/125/150%；跨屏DPI、工作区、最小有效区及长内容/拆包|OS build、物理尺寸、DPI、实际客户区DIP；正常/拒绝/Unknown/拆包/配置/恢复独立截图及动作；特别核对E-02|交叉编译、理论DIP、旧HTML截图|
|W-02|两窗口同Runtime；草稿/焦点/选区/详情/滚动独立；关闭副窗|两窗口身份、动作与结果、唯一请求/发送证据|仅核心双调用幂等记录|
|W-03|扫码器Enter/Tab、弹窗取消默认/Tab圈定/Esc/焦点归还；无箱初始焦点|真实输入设备配置与按键轨迹；不触发危险确认|仅XAML/键盘源码阅读|
|W-04|三类提示声及双通道相邻结果；触控目标、可读性与持续操作|E-08整改后事件对应、听审/触控记录，实际人员/场景|SystemSounds存在或按钮名义尺寸|
|W-05|自包含包启动、PowerShell脚本、普通账户、路径/ACL、跨会话租约、升级/回滚/备份恢复|固定SourceCommit/包SHA，安全的新数据根；E-04/E-10边界结果|包CRC/PE检查、macOS冒烟|
|W-06|慢盘/故障时持续绘制、投影失效禁用、停止与重启恢复|对应故障、观察与保留的请求身份，E-09失败路径|后台方法返回Task或单个停止用例|

现有执行细目继承 [alignment-01-windows.md](../runtime/alignment-01-windows.md)。已存在ALIGNMENT-01包可用于建立基准，但整改后的UI必须绑定新源提交/新包重新登记，不能复用旧截图关闭新缺口。实际运行须由后续明确授权任务执行，本次不安排或执行验证。

## 5. 真实接口、规则与许可阻塞（B类均BLOCKED）

|索引|缺失证据/批准|受阻路径|工程仍可推进|
|---|---|---|---|
|B-01 / D-01/14；RV-09|旧工程commit/发布基线；设备型号/固件、SDK/样例/位数、分发及联调授权|真实读码驱动、回调/缓冲所有权、断连/停止、原工程兼容迁移|继承当前参考产品；E-05/E-09合成实现；不另造框架或编SDK函数|
|B-02 / D-02/07；RV-01/06/07/16|版本化WCS合同；CAPI-01～10真实映射；副作用、身份、接受/物理完成、查询权威范围、报警及断档恢复|真实请求/查询/关闭、无查询人工SOP、预留处置；AwaitingDisposition继续持有|原请求不可变、端口/追溯、工程夹具与合同映射模板；不把loopback路径当WCS|
|B-03 / D-05/06；RV-01/02/09|相机—实体位—箱映射；入场/装载/许可/离场/复位来源、时效、顺序与最终联锁|生产准入/下一实体/重连重新授权、设备动作|显式模拟事件、来源版本和过期机制；不按同码/时间窗推实体|
|B-04 / D-03/04；RV-02/08|数量单位/需求版本、等量与不足量分支、拆保留/移除、承运次数、剩余件归属及释放规则|真实自动/人工/拆包分支；真实并发需求分配|当前SYNTHETIC-KEEP-v1继续明确模拟；不将缺自动发车简单归为工程bug|
|B-05 / D-08/11/10；RV-10/12|认证/跳过登录批准、换班与特殊处置权限、扫码来源/编码|真实登录、身份续期/换班/强制关闭与人工纠错|守卫、旧责任快照、输入隔离和显式模拟身份；不得补一个假生产登录页|
|B-06 / D-09/12/13/15/16；RV-13/17及PUB-01|目标工控机/显示输入、单站预算/保留政策、验收人员/指标；原稿和视觉资产公开许可/新可编辑资产|现场持续操作/节拍、生产保留参数、旧视觉资产正式交付及完整公开归档|Windows模拟验证准备、通用UI改进；不上传受限材料/字体/SDK|

Production保持NotReady是当前正确限制，不应为“做完下一阶段”而回退模拟成功。真实SDK/WCS和实体动作必须有明确联调授权；取得文档不等于获准操作设备。

## 6. 下一阶段工作包及顺序

1. **先执行 [WP-IPC-ENGINEERING-CLOSURE-07](../work-packages/WP-IPC-ENGINEERING-CLOSURE-07.md)**：E-01～10按五个增量切片整改；不依赖完整SDK/WCS即可完成大部分代码。取得旧工程先核验继承，不重建第二套框架。
2. **[WP-IPC-WINDOWS-VALIDATION-06](../work-packages/WP-IPC-WINDOWS-VALIDATION-06.md)**：独立维护W-01～06；环境可准备，最终UI验收依赖07相应整改及固定新包。此包已有编号，现补正式正文。
3. **[WP-IPC-INTEGRATION-BOUNDARY-05](../work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md)**：B-01～06按能力接收/审批；可与工程整改同时准备资料，真实实现/联调依赖对应输入与授权。此包已有编号，现补正式正文。

三个包不互相代替：工程整改不能关Windows格；Windows模拟运行不能解真实合同阻塞；D批准不能直接关闭RV。最先处理E-01/E-03/E-04/E-05，避免只做外观后把逻辑缺口送到Windows验收。

## 7. 本轮交付边界

本轮只增加复核报告及三个后续工作包，修正当前入口/manifest的阶段指向；业务缺口全部保持OPEN，未改实现。原issues、D批准、历史检查JSON及原验收记录不倒写。PR #4的OPEN字段是形成时快照，本报告依据GitHub核验记录其现为MERGED。

发布仅文档PR；不发布新二进制、不自行合并、不控制设备。Git/PR交付回执见最终答复。测试/构建/启动及本轮所有运行验证均NOT_RUN。
