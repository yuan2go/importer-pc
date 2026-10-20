# Sportmaster 审查整改与R-02定向分析

[工作包](../work-packages/WP-IPC-SPORTMASTER-AUDIT-REMEDIATION-01.md) · [证据与复跑](evidence/sportmaster-audit-remediation-01/README.md)。D-01～D-03在下述范围整改完成；R-02完成合成定向分析，性能风险尚未获客户预算/长时证据关闭。R-01规则未改，正式商业发布仍NOT_READY。

## 冻结、环境和实际范围

开工main `e0beb5d32c6a24a7799ba698731566bca6a6833a`已含PR57/58，开工无开放PR；保留原工作区与无关未跟踪报价表，在短路径隔离分支`codex/sportmaster-audit-remediation-01`实施。提交前main推进至`ad6dc2e2faf74a11bab148fcfa893eb379f87d5c`（PR59），先读其设计/结果再整合，保留日期失焦修复、全部日期/支持断言与冻结介质CI门禁。

最终本地运行源码、配置、随包说明与夹具为同一SHA **`934fd4af6a5b5587021892c0f9df169510115db8`**。视觉、首配/商业辅助页和R-02均引用此介质DLL，三个RESULT的SourceCommit与FixtureSourceCommit相同、FixtureMatchesMedium=true。后继仅整理独立证据、报告及manifest，不重标介质；CI按其实际PR检查SHA另记。

`ui-final.zip` SHA256 `3f0a3c09753615168a76a2d50885cdefc37b6dcbb3553bd91959e8f7928a6fbf`；746项库存，库存SHA256 `39e100e98fe45fbef145d489202554da4543ae3407bbfacf52a1641b53999b24`。本地候选未上传Release，EXE为NotSigned。Sportmaster参考core / win-x64自包含 / Simulation / 双manual reader / reference WCS，禁用称重、图像和普通作业登录，reference维护身份保留。业务1.4.0、Application.Contracts1.10.0、Parcel DB8、配置schema3；默认supply-station，classic显式保留。没有新增生产依赖或更改业务/驱动/协议/SQL/配置/准入/重试/恢复规则。

Windows 11 Home 22631 x64，Ryzen 5 3550H四核八线程、约16GB内存；SDK10.0.402，.NET/WPF10.0.13。1920×1080、125%/120DPI、系统字体；窗口约1366×768、1100×650、900×520及实际800×420 DIP客户区。非目标Windows26100+安装资格、非客户硬件。真实WPF窗口的RenderTargetBitmap导出属于控件渲染证据，不是HTML/效果图或含非客户区的OS桌面录像；操作为程序化原生输入，无现场用户观察。

## 整改与必要验证

|审查项|实际补差|触发与关闭证据|
|---|---|---|
|D-01|继承PR58日历/时钟/明确偏移、中文枚举、时间列分行和PR59非法日期失焦防回退；历史原件恢复动作移出说明滚动区，命令归属换行；展开筛选进入有界编辑区，查询动作固定，明确收起后返回记录/原件区|+05:30保留；非法时间/日期、开关日历、支持拒绝与显式纠正；800×420展开筛选时查询按钮完整≥44DIP且无被挤压的记录动作，收起保留原选择/owner；长检查说明滚动时恢复动作固定。完整历史旅程在1100×650复验，最小尺寸不宣称全部字段同时可见或完整处置旅程合格|
|D-02|已有中文操作回执投影移入共享VM，classic与默认界面共用；classic低频诊断/原观察默认收起，Blocked/PartiallyReady中文显示；完整原回执仍在诊断可复制|原Message/Feedback、业务状态/权限未变；暂停、左右独立、真实合成丢回执Unknown只Query不再发、拒绝/失效、焦点/选择/滚动/草稿及共享辅助窗通过|
|D-03|受影响指南原Git blob与hash归档；当前ABI1.10、DB8、CLI恢复目标8和实际账户政策一致；形成时DB7/PR44/NOT_RUN明确为历史|[清理前原件](../archive/pre-sportmaster-audit-remediation-01/README.md)可复核，原D/RV/旧验收不倒写；完整归档检查仍有历史失败，未加豁免|
|R-02|独立合成夹具、同版包内DLL测量、原始逐项样本和热点分析；无生产性能算法修改|300个输入须到其自身实体的SlotResolved才计样；无同侧在途重叠，不替R-01定规则；分布和长期边界如下|

同版完整视觉与首配/商业辅助页均PASS，前后库存未变；开发构建0警告/错误。原始日志、图像、逐DLL及来源见证据。旧`--ux-commercial-presentation`在缺机器政策时拒绝启动，记录为受阻，未改测试/政策换PASS。初次错误大写合成ID、未确认初始焦点的复合断言、开发BAML增量加载失败均保留原失败；合法ID、显式初始焦点及完整重建后保留原断言复验。

## R-02测量与解释

默认ResidentHistory=200、MaximumOperations=10000；300个不同合法实体/观察，左右交替，逐件等自身终态。ReferenceWcs也执行本地文件IO，没有真实网络。每档40次快照及深复制/容量/单次待回执探针，20次旧分页和20次实际UI冻结筛选/查询；nearest-rank P50/P95/P99与全部样本保留。各探针是独立实验，不能相加/相减伪造阶段归因。

|累计实体 / 驻留|快照P50 / P95 ms|深复制P50 ms|容量投影P50 ms|UI筛选查询P50 / P95 ms|
|---|---|---|---|---|
|0 / 0|1.80 / 3.13|0.27|0.03|3.60 / 10.31|
|50 / 50|38.99 / 50.98|5.72|5.45|9.11 / 12.39|
|199 / 199|138.30 / 164.17|17.42|20.55|26.95 / 34.35|
|300 / 101|67.08 / 80.48|8.53|10.44|37.23 / 46.35|

300件自身终态轮询P50/P95/P99为179.20/662.49/980.59ms，max1419.97ms。30次原生显式刷新至Dispatcher空闲P50/P95为80.93/167.94ms。受控50ms持gate实验10次总读取P50/P95为130.97/142.28ms，不是现场竞争分布。

一次新根StartAsync返回569.98ms、准入就绪688.40ms；同进程完成300件后，新窗口Show至有效快照与Dispatcher空闲1146.29ms。均单次、排除进程初始化/安装，不能写为完整EXE启动分布。

|空闲窗口|实际秒数|进程CPU秒|分配MB（十进制）|Private MB起→止|数据增长字节|50ms心跳P95 ms|
|---|---|---|---|---|---|---|
|1|24.04|2.39|441.0|150.5→134.9|0|64.67|
|2|24.29|5.52|946.2|166.3→149.8|0|65.09|

计划每段400×50ms，实际时长以上表为准；单/双窗分配约18.3/39.0MB/s。顺序样本有JIT/渲染/GC和系统后台干扰，不能作严格双窗成本比例或长期结论。计时器与显式刷新订阅期累计VM通知23,052次，不能除以30当单次通知。199驻留JSON快照约1.47MB；单次PendingReceipts探针P50约0.145ms，不代表累计重复查询成本。空闲数据无增长仅针对正常终态/无待回执，未证明异常积压容量受控。

终态轮询包含本地处理、合成对端IO、快照和10ms轮询，不是精确扫码到屏幕反馈，也不是上游网络耗时。原生刷新包含ServiceAsync、VM与Dispatcher空闲边界；50ms心跳含系统计时器量化。CPU/分配包括后台工作；短时Private变化不能证明泄漏或无泄漏。没有目标节拍、延迟、数据量或资源预算，不设通用毫秒门槛，也不宣称长期稳定性通过。

可复核热点：

* [SnapshotAsync](../../src/Ipc.Application.ParcelFlow/ParcelCommands.cs)在gate内构造投影并全量JSON深复制。副本保护可变原件隔离，不能直接删复制或提前释放锁。
* [Capacity/ArchiveEligible](../../src/Ipc.Application.ParcelFlow/ParcelHistory.cs)、[Availability/PositionSnapshots](../../src/Ipc.Application.ParcelFlow/ParcelGuards.cs)重复遍历驻留作业；IsSettled与ArchiveEligible各读PendingReceipts。[该查询](../../src/Ipc.Application.ParcelFlow/ParcelRepository.cs)是同步SQLite，待raw时还读/解码/hash文件；单次探针不能代替累计调用成本。
* 两窗共享Application/连接，但各500ms刷新，重复竞争同一gate。PR58已减少位置通知，本轮继承；共享Snapshot仍发布多字段通知。通知数含显式与计时器刷新，不能直接除以30当每次显式刷新数。
* [ParcelHistoryReader](../../src/Ipc.Application.ParcelFlow/ParcelHistoryQuery.cs)以冻结selection执行JSON字段筛选；300条只证明小库存。最大保留量、资料积压、长库启动原件验证、配置保存及峰值并发未测。

此前ff2e7a7/独立夹具504c187的样本与初次892a404整合候选保留各自身份，只用于追溯；不是新介质成绩，不构成受控A/B对比。

## 当前操作裁决与阶段判断

ParcelRepository缺省storageVersion=8，ParcelCli recover-legacy-receipt明确targetStorageVersion:8；旧API缺省5仅兼容路径，不能当当前CLI目标。新库不能交旧读者回滚，SQL1～7原字节未改。MachineRuntimePolicy要求指定SID：非提权UAC过滤令牌的deny-only管理员组可通过，提升/启用管理员组、服务及其他SID拒绝；形成时PR44开放/deny-only拒绝不作当前指令。源码判定与真实安装账户验收分开。

|阶段|本轮判断|
|---|---|
|源码候选|声明范围D-01～03完成，最新PR59并存；最终PR CI另记|
|本地介质可用|构建、完整库存和包内原生合成验证通过；未安装/签名/发布|
|原生体验|当前Windows125%及声明尺寸通过；目标多DPI、HID/触控/现场用户未验|
|受控现场试运行|SDK/精确组合/RCS合同、R-01及Unknown SOP未取得，未批准|
|正式商业发布|NOT_READY；下列阻塞与运维资格未关闭|

## 剩余阻塞与后续工作包

发布前必须：R-01由客户/上游确认同侧在途实体归属与准入规则，原审查失败不由UI/PASS覆盖；真实SDK、设备、许可、RCS认证/查询/幂等与Unknown SOP及精确组合联调；目标Windows安装/指定普通账户/第二账户/重启/升级迁移/失败恢复/备份恢复/卸载保留数据；签名、支持责任和现场资格。R-03 main保护及必需检查由仓库管理者决定，本轮未改仓库策略。

性能工程下一包：取得目标硬件、峰值扫码节拍、双窗使用方式、积压/历史规模和延迟/资源预算。先跟踪gate等待、投影、SQL调用次数与分配，评估**同一次gate内**批量读取责任、复用只读容量计算；不跨修订缓存准入结果。再评估隔离副本/按需投影。保留返回快照隔离、原报文/目标/合同/请求关联、Unknown不重发、容量/身份/CAS守卫和崩溃恢复；同硬件同负载比较原始分布，不增大队列、删校验/历史或吞错换改善。

试运行期间：按批准节拍与保留量回放，覆盖并发、慢上游/超时/Unknown和恢复，监测队列、资料责任、日志/磁盘增长及约定时长的CPU/内存/句柄/订阅清理。后续优化：现场用户的多DPI、键盘/扫码/HID/触控、最小客户区任务负担；文档按各新证据更新，原D/RV/历史版本不倒写。

精简执行提示词：读取本工作包、原审查与证据，冻结最新main并核对并行PR；保持独立Application与原请求守卫。按R-02热点先做gate内批量读取的等价性设计，再最小实施，同硬件/负载测快照与实际UI延迟、SQL次数、分配及关键异常/恢复。未获真实合同不得修改R-01、重试或去重；未做目标长时/现场不宣布商业性能通过。
