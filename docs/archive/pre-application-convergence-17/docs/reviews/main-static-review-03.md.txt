# 合并07后的架构与供包UI独立静态复核

日期：2026-10-16（Asia/Shanghai）；工作包：WP-IPC-MAIN-REVIEW-03。

**不能确认10项全部闭环。8项的指定整改路径在源码中补齐；E-06、E-10各有一项剩余工程缺口。10项的编译、运行和验收均未执行。另有UI-01实物基线到WPF的落实差异；Windows六组待验与真实集成六组阻塞继续独立登记。** 这里的“静态路径补齐”只表示本轮未发现同级缺口，不表示PASS或生产放行。

## 1. 基准、方法与证据边界

- 按用户要求先合并 [PR #6](https://github.com/yuan2go/importer-pc/pull/6)。实际合并时间2026-10-16 21:43:07 +08:00；合并后的初始分析基准为 `677a5adeef277adb4c6dd742749f06d02b01c2a7`，PR源头为 `ca4f0bfe64dfb738153da4d0a78ea277a969189d`。
- 复核期间并行 [PR #7](https://github.com/yuan2go/importer-pc/pull/7) 于21:49:12 +08:00合并，仅增加获授权公开的UI实物和文档，未改业务/测试/部署源码。交付前将其纳入，最终分析main为 `2579392ddcc0b4302d4aae865d4b2d8b3c083b59`；保留并行资料及其公开范围，原10项代码行号与判断不受该文档合并改变。
- 对照前一main `6d4063ec54e8167c8710e46befbe66f8b688ef08` 与上述基准的差异，再回查完整调用链；不是只读07勾选表。当前最后业务代码源提交 `493416f99970d95bb4a4076130de5c28ee33502d`，待构建元数据 ENGINEERING-CLOSURE-07 / Storage3 / SIMULATION-v1；没有本包新二进制或包SHA。
- 先核对工作区、开放PR，再读INDEX、07工作包、framework/decisions、前次复核、D/RV、公开范围、相关源码/部署脚本和验证源码。复用干净的已附加worktree，在 `codex/wp-ipc-main-review-03` 整理文档；原工作区受限未跟踪资料保持原样。
- 使用code-review的Standards与Spec两条独立阅读轴；主审再核对问题条件和行号。Standards以AGENTS和架构职责为准，Spec以07验收边界及供包UI要求为准，不把代码味道当硬缺陷，也不因此新建框架或问题跟踪系统。
- UI核对先只读获授权本地PRD/UI输入，后纳入#7的公开v1.1 README/GALLERY、完整交接、原HTML、分离CSS/JS及tokens；被动查看normal1920、split1366、record-detail1920三张现有PNG，没有重新渲染或执行原型。公开交付仅新增通用判断、工作包及既有REQ/BR/CAPI/D/RV索引，不复制客户全文、标识、视觉资产、私密来源或SDK/字体。
- 用户明确“不要测试”：测试、资料验证器、还原/构建/编译、应用/夹具、PowerShell、发布/冒烟、浏览器/WPF、Windows/SDK/WCS/实体动作全部 **NOT_RUN**。源码阅读与Git/PR元数据核对不是运行复现。分项记录见 [checks](main-static-review-03-checks.json)。

## 2. 10项工程闭环复核

下表不改写07的形成时实施记录，更不更改历史RV。所有行的运行验收均为NOT_RUN。

|原索引|本轮静态判断|已回查的实际路径|仍待证明/处理|
|---|---|---|---|
|E-01 当前输入有效性|STATIC_PATH_COMPLETE|`StationApplication.cs:330–342,360–382,727–734` 新Read/非法码/守卫拒收撤销当前Task/Package/Remaining/Supply，旧证实结果单独保存；`ViewModels.cs:96–108`、详情276–277行分开展示|纠正与旧回执并发、历史字节及显示结果需要09；实际可读性属于06|
|E-02 核对对象与动作|STATIC_PATH_COMPLETE|`MainWindow.Closure.cs:21–32,69–90` 当前通道与ReviewFits；`MainWindow.xaml.cs:117–129,178–195` 冻结owner/revision后确认、布局不足拒绝动作；XAML固定关键核对区与另一通道摘要|具体DPI、触控、长字段、展开详情后的几何与焦点仍W-01～03|
|E-03 定向来源核对|STATIC_PATH_COMPLETE|`StationApplication.cs:223–238,745–750` 精确Code/Scope/Source/Epoch/Version及命令回执；`StationClosure.cs:10–12` 匹配；UI92–98行保留所选证据；旧自由文本入口699行明确拒绝|证据更新/确认竞争和重复命令需09；不解除Unknown、过载、预留或实体|
|E-04 安全维护|STATIC_PATH_COMPLETE|`Maintenance.cs:127–154,180–215` 只读检查→原格式一致备份→持久身份回执→显式迁移；`Store.cs:41–76` 旧1/2拒绝隐式升级；`Program.cs:11–67` 与`station.ps1`显式Release选择|原格式/失败保旧/恢复隔离需09；PowerShell/账户/发布选择实际行为仍W-05|
|E-05 来源健康与代次|STATIC_PATH_COMPLETE|`DeviceCapabilities.cs:5–21` 来源确认/期限；`StationClosure.cs:44–103,117–136` 注册与旧/重放拒绝；业务守卫528–582行用来源有效性；Runtime352–418行受控重连，模拟源显式心跳|有限期限/静默过期/旧回调竞争需09；真实SDK健康含义及生产TTL仍B-01/B-03|
|E-06 历史/查询|PARTIAL_ENGINEERING_OPEN|`HistoryQueries.cs:49–165` 类型化SQL、分页及空箱/跨箱关闭链；UI133–218行加载/空/失败与代次；新查询/详情有身份守卫|旧HistoryAsync/AlarmHistoryAsync仍无身份检查；见Spec P2，下阶段08整改|
|E-07 常驻操作信息|STATIC_PATH_COMPLETE|`MainWindow.xaml:4–5`、`MainWindow.Closure.cs:41–60` 工位/身份、CAM0/CAM1/WCS确认和报警常驻，全局原因独立滚动；技术信息/最近记录折叠，拆包输入按状态显现|Windows长内容、跨页发现性、两通道辨识仍W-01/W-04；真实字段合同仍B-02|
|E-08 提示事件|STATIC_PATH_COMPLETE|`StationClosure.cs:14–42` 在已提交转换后形成有序有界队列；`OperatorNotificationPlayer.cs:9–29` 逐个请求播放后确认；`MainWindow.Closure.cs:34–39` 仅生命周期主窗消费|事件竞争/溢出/失败/退出需09；实际播放、可辨性、静音及触控仍W-04，API调用不是听审|
|E-09 停止/释放|STATIC_PATH_COMPLETE|`Lifecycle.cs:50–82` 独立清理与异常汇总；Runtime228–279/411–470/508–530行保留未终结任务和依赖；Application702–725行先发关闭信号；桌面/CLI保留原始与清理失败|故障源/超时/慢盘实际终态需09/W-06；真实回调/缓冲释放仍B-01|
|E-10 账户/设备租约|PARTIAL_ENGINEERING_OPEN|`MachineRuntimePolicy.cs`及`provision-runtime.ps1`明确固定账户、机级根/ACL、无策略拒绝；但Runtime先锁seed设备再选数据库Effective配置|最终生效设备可能未被所持租约覆盖；见Standards P2，下阶段08整改；Windows账户/ACL仍W-05|

“8项静态路径补齐”与“全部10项运行NOT_RUN”同时成立。两项是现有调用链的残留，不认定为PR #6新引入的回归。不能把E-06/E-10塞进“缺Windows”或“等SDK”，也不能因为两个剩余问题而重建已存在的共享业务、Outbox、健康、维护或UI框架。

## Standards

**1项明确发现；本轴最高优先级为P2：E-10最终生效配置与租约集合不一致。**

证据：[ProductRuntime.cs](../../src/Ipc.Product/ProductRuntime.cs) 66–72、79、89–99行；[ReferenceProduct.cs](../../src/Ipc.Product/ReferenceProduct.cs) 24–29行只校验配置结构/产品组合，没有校验所持资源键。依据：framework §11“为设备绑定建立本机独占控制”，07切片2的机级所有权约束。

触发条件：同一工位已有数据库Effective配置，设备集合含A；重新部署的合法seed配置改为B。启动先用seed取得工位/设备B租约，随后选数据库Effective并启动设备A；79行只检查PendingActivation，不检查已有Effective。另一工位、另一数据根可以持有A的租约，两个运行实例因而可同时启动同一配置设备。当前只注册模拟源，没有已发生物理设备争抢的证据。

整改方向：在任何来源启动前确定最终生效配置，核对工位/模式及设备身份，再取得覆盖实际集合的租约；或明确拒绝seed/Effective身份及设备集合错配。数据根锁、机级设备锁与设备端控制权继续分开。真实设备端协议未知仍B-01，不能替代此软件所有权整改。

## Spec

**2项明确发现；本轴两项均为P2：E-06历史读取权限旁路、UI-01实物UI落实差异。**

证据：[Model.cs](../../src/Ipc.Supply/Model.cs) 115–119行保留全部公开入口；[StationApplication.cs](../../src/Ipc.Supply/StationApplication.cs) 634–642行中，HistoryCore/AlarmHistoryCore直接读取Store，没有RequireIdentity；新QueryHistoryAsync/SessionDetailsAsync及DetailsAsync有身份检查。依据：07切片5“查询无副作用且无权时返回明确失败”，framework §6.4 QueryHistory/GetRequestDetail的“权限检查”。

退出登录或身份过期后，调用旧HistoryAsync仍可分页读取作业历史。AlarmHistoryAsync还有当前可操作UI路径：`MainWindow.xaml.cs:333–336`，`MainWindow.xaml:44` 的刷新/翻页按钮没有身份Tag守卫，应用入口也不拒绝。不能仅靠新类型化API有校验宣称所有历史读取已受控。

整改方向：统一历史读权限；旧入口委托受控新路径或显式拒绝，UI反馈无权/过期；明确当前状态快照的展示策略，避免把常驻诊断与完整历史读取混为一谈。此问题在现有模拟身份下就可整改，不等真实认证合同。未发现该轴范围扩张；其他指定路径的静态符合性见上表。

UI-01的实物要求、源码差异和适用范围见下一节；它独立于原10项工程统计。两个原E项残留与一个UI落实问题分别安排，不将普通模态确认或导航裁决直接等同于业务守卫失效。

两轴分别为Standards 1项 / Spec 2项；未合并为另一套严重度排名。三项都属工程剩余问题，而非已复现事故。

## 3. 实物UI落实与运行验证债务

PR #7已使v1.1原HTML、分离源码、PPTX/画板JSON、tokens和34张同源PNG进入main；不再把“完整UI原型未入库”当成此组的阻塞。它只扩大这一组生成设计的公开范围，原客户资料、其他资产/新Canva来源及真实接口许可仍分别受限。应用实现与设计实物不是同一个交付对象。

**UI-01 / P2：当前WPF尚未按实物形成完整逐页映射/差异裁决。** 公开README的“设计与实现取舍”、交接§2.3/7/8与`prototype/workstation.js:47,53,58–59`保留共享箱、同时双通道的结果/字段/数量/固定动作及低频辅助视图。现有`MainWindow.xaml:7–37`是单选通道核对区、另一通道简短摘要及折叠详情，六个Tab承载辅助页；`ViewModels.cs:105–106`把任务/数量主要拼成文本。07为E-02选择的单通道核对和ReviewFits确实改善了动作安全，但没有据此证明已实现基线的双通道信息密度、数量/拆包进度视觉及逐页组件层级。

另需在同包裁决普通供包是否每次模态确认：交接133/212行避免无意义确认，而`MainWindow.Closure.cs:77–81`每次供包均提供确认文案。保留owner/revision锁定，不简单删确认来模仿原型。

这是可据现有图与源码确定的实施/范围映射工作，不是本轮测得的像素遮挡，也不意味着必须删除07的安全方案或逐像素照抄旧HTML。新增[UI-BASELINE-ALIGNMENT-10](../work-packages/WP-IPC-UI-BASELINE-ALIGNMENT-10.md)，逐页明确继承/调整/待实现/受阻并落实获准差异；Windows可读性另属06。UI-01独立于原10项统计，不将E-02重新表述为“固定按钮完全没绑定任务”。

### 非Windows专属的运行验证债务

07没有构建或运行，本轮仍没有执行授权，不能确认编译通过、恢复防重通过或新包能启动。现有验证资产还保留旧语义：`AlignmentDataVerification.cs:75–97` 期待构造时隐式迁移和format2；`HardeningVerification.cs:196–201` 期待两条迁移/2:2。它们与当前显式升级/format3不一致，这是静态识别的待对齐项，不是本轮实际测试FAIL，也不是Windows环境问题。

新增 [REFERENCE-VALIDATION-09](../work-packages/WP-IPC-REFERENCE-VALIDATION-09.md) 承接核心合成验证和必要构建/发布启动；目前 **PLANNED / NOT_RUN**。未来须先保留原checksum/原请求字节/旧拒读边界并对齐验证源码，再按授权执行，不能删断言、把预期失败改成PASS，或拿历史ALIGNMENT/H04结果代替本轮。

## 4. Windows待验：W-01～06全部NOT_RUN

|索引|仍未验证的内容|与工程/真实能力的分界|
|---|---|---|
|W-01|实际分辨率、DPI/跨屏、最小有效区、长条码/任务/原因、当前核对与动作同屏|E-02/E-07源码路径存在，未测量实际WPF布局|
|W-02|双窗口共享同Runtime、独立草稿/筛选/选中/滚动、关闭副窗、唯一业务/发送器|不能用两个核心调用或静态单例代替实机操作|
|W-03|扫码器Enter/Tab、取消默认焦点、Tab圈定/Esc/归还、无箱初始焦点|键盘源码存在不等于真实输入设备通过|
|W-04|三类声音/相邻两通道、静音/系统声音、视觉等价、触控和持续可读性|有序队列与Play请求不证明现场听到和分辨|
|W-05|自包含包、PowerShell、固定普通账户/ACL/跨会话、路径、旧库升级与回滚隔离|先修E-10；机级策略文件存在不代表实际设备集合已被正确锁定|
|W-06|慢盘/故障中持续绘制、投影过期禁用、停止失败/重启/未决恢复|核心09与Windows06各自留证；SDK缓冲仍B-01|

继承 [WINDOWS-VALIDATION-06](../work-packages/WP-IPC-WINDOWS-VALIDATION-06.md)；须绑定08/10最终改动及09形成的同一SourceCommit/包SHA。旧包可作历史基准，不能作为新代码截图或新验收证据。本轮没有制作新包。

## 5. 真实接口、规则与许可：B-01～06全部BLOCKED

|索引|缺项及受阻部分|独立的工程边界|
|---|---|---|
|B-01 / D-01/14|获授权旧工程、实际设备型号/固件/SDK版本/位数/样例、回调/缓冲/停止与分发及联调许可|继承参考产品；不编造SDK方法，不称旧现场迁移完成|
|B-02 / D-02/07|版本化WCS合同、CAPI映射、副作用/预留、幂等/查询权威、关闭、报警/断档恢复及认证|Unknown保持原请求/原报文/原目标；loopback不是WCS合同|
|B-03 / D-05/06|相机—实体位—箱映射、入场/许可/离场/复位的身份/来源/顺序/时效与最终联锁|模拟心跳及显式实体事件不批准生产TTL或动作|
|B-04 / D-03/04|数量单位/需求版本/并发分配、等量/不足量、保留/移除、承运次数及剩余件/预留处置|SYNTHETIC-KEEP-v1不成为客户规则；AwaitingDisposition继续持有|
|B-05 / D-08/11/10|真实认证/跳过、换班、特殊处置权限、人工SOP与输入来源|E-06现有模拟权限旁路先修；不以缺真实认证掩盖工程问题|
|B-06 / D-09/12/13/15/16、PUB-01|目标工控机/显示输入、单站预算/保留政策、验收人员指标、本组以外原稿/视觉资产/SDK公开许可|Windows参考验证不等于现场放行，脱敏不自动等于许可|

继承 [INTEGRATION-BOUNDARY-05](../work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md)，按能力分项解阻。本轮未取得新的SDK/合同/客户批准或现场操作授权，Production继续NotReady。D批准状态不变，RV-01～17仍OPEN。

## 6. 下一阶段与交付

1. [ENGINEERING-FOLLOWUP-08](../work-packages/WP-IPC-ENGINEERING-FOLLOWUP-08.md)：只收敛E-06历史权限和E-10最终配置/租约；两条独立切片，均不依赖真实SDK/WCS。本轮仅定义，尚未实施。
2. [UI-BASELINE-ALIGNMENT-10](../work-packages/WP-IPC-UI-BASELINE-ALIGNMENT-10.md)：先读#7已入库实物，形成WPF逐页映射和获准差异，不改共享业务所有权；当前未实施。
3. [REFERENCE-VALIDATION-09](../work-packages/WP-IPC-REFERENCE-VALIDATION-09.md)：对齐旧验证预期，补07/08/10风险场景；明确获准恢复验证后才构建、运行及形成新包。当前NOT_RUN。
4. [WINDOWS-VALIDATION-06](../work-packages/WP-IPC-WINDOWS-VALIDATION-06.md)：对最终同版包实施W-01～06；当前NOT_RUN，不能以09模拟结果替代。
5. [INTEGRATION-BOUNDARY-05](../work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md)：可并行准备获授权资料，真实实现/联调逐项等待能力证据与授权；当前BLOCKED。

本次仅文档和工作包整理；不改业务/测试源码、不重写07的检查JSON、历史issues、D批准或原验收记录，不修旧HTML。更新当前导航与manifest，提交文档开发分支供main PR审查，不自行合并本次复核PR，不发布二进制。实际提交/PR以最终回执与GitHub核验为准。

已创建[文档main PR #8](https://github.com/yuan2go/importer-pc/pull/8)，创建时HEAD `16bed1eef4782c9d732aba74569a8bb286e75292`；当前分支的最终HEAD以GitHub PR及最终回执为准。本任务只合并获授权的PR #6，未合并本次复核PR。
