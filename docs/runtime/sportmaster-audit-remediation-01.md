# Sportmaster 审查整改与定向性能分析

工作包：[WP-IPC-SPORTMASTER-AUDIT-REMEDIATION-01](../work-packages/WP-IPC-SPORTMASTER-AUDIT-REMEDIATION-01.md)。开工main `e0beb5d32c6a24a7799ba698731566bca6a6833a`，继承已合并PR57发行和PR58视觉实现。本页记录本轮实施和实测，不替换原审查及历史验收。

业务1.4.0、Application.Contracts1.10.0、Parcel DB8、配置schema3、Sportmaster默认supply-station；classic显式保留。没有业务协议、迁移SQL、运行资格或准入规则变更。原型D编号和独立审查D-01～03分开管理。

## 当前操作裁决

ParcelRepository缺省storageVersion=8，ParcelCli recover-legacy-receipt明确targetStorageVersion:8；旧API缺省5是兼容路径，不能当作当前CLI目标。新库不能交旧读者回滚。SQL1～7原字节不变；版本8由当前仓库迁移表定义，安装介质必须另核精确来源。

MachineRuntimePolicy.ValidateRuntimeIdentity要求指定SID；非提权UAC过滤令牌的deny-only管理员组可以通过，启用的管理员组、提升、服务和其他SID拒绝。runtime-identity-preflight-02中PR44尚开放/deny-only拒绝属于形成时记录；当前操作以实际政策及startup-deployment-02为准。源码判定与真实Windows令牌/ACL/安装验收分开。

## 本轮结果

|审查项|已继承与本轮补差|验证与关闭边界|
|---|---|---|
|D-01|PR58已加入日期/时间/显式时区、无效输入保留、中文枚举、时间列分行；本轮将历史恢复动作移出原实体说明滚动区，长命令归属/选择状态换行|包内WPF DLL：保留+05:30，非法25:99不沿用旧时间；清除边界、选择/原owner保持，800×420 DIP滚动长说明时恢复按钮固定。最小尺寸只证明动作几何；完整历史处置旅程在1100×650窗口复验，不能宣称最小客户区全部内容同时可读|
|D-02|继承新版默认supply-station及按需诊断；把已有中文操作回执投影移入共享VM，classic也使用；classic诊断/原观察默认收起，Blocked/PartiallyReady中文显示|原Message/Feedback和权限事实未改；完整技术回执在诊断中可复制；暂停、独立左右结果、Unknown原请求Query、拒绝/失效、焦点/选择/滚动/草稿和共享辅助窗均定向复验|
|D-03|归档受影响指南的原Git blob与hash；统一当前DB8、Contracts1.10、CLI目标8及实际UAC过滤政策；形成时PR44/DB7/NOT_RUN标明历史|当前指导不与旧验收混用；原版本/D/RV不倒写；[清理前原件](../archive/pre-sportmaster-audit-remediation-01/README.md)可逐字复核。原全树检查仍FAIL，本轮无新增错误|
|R-02|独立合成分析夹具和固定DLL验证入口；无生产性能算法修改|分析完成不等于性能风险关闭，见下表和后续工作|

原始结果、逐次耗时、散列、主要WPF图像与失败记录见[证据索引](evidence/sportmaster-audit-remediation-01/README.md)。图像由已打开原生WPF窗口的RenderTargetBitmap导出，不是HTML/原型或重新绘图，也不是含桌面/非客户区的屏幕录像。判断属于工程师检查，无现场用户观察。

### 冻结与验证环境

开工main包含PR57和58，未覆盖原工作区或其未跟踪报价表；本轮分支`codex/sportmaster-audit-remediation-01`。候选源码`ff2e7a7459db148d0ed8dd4537489710b402635f`，后续独立夹具`504c187e1eeb87fa993424f11ee3406f1b06b56a`，二者的生产源码/配置无差异。实际测试DLL从候选介质取出；RESULT分别保存SourceCommit、FixtureSourceCommit、FixtureMatchesMedium=false和逐DLL hash，不能把夹具提交写作介质来源。

Windows 11 Home 22631 x64，Ryzen 5 3550H / 4核8线程 / 约16GB内存，.NET SDK10.0.402 / .NET与WPF10.0.13。显示器1920×1080、125%/120DPI、系统中文字体；原生窗口覆盖1366×768、1100×650、900×520及实际800×420 DIP客户区。非客户硬件，非目标26100+安装资格。系统字体未打包，无新生产依赖。

候选为win-x64自包含Sportmaster参考core，Mode=Simulation、双manual reader、reference WCS、禁用称重/传图/普通作业登录，维护reference身份保留。默认supply-station，classic显式；Parcel DB8 / 配置schema3。ZIP和完整库存身份见证据。未上传二进制、创建Release或执行安装；没有连接真实设备/服务。

必要验证：WPF源码构建0警告/错误；包内DLL原生定向验证通过；独立性能夹具的每个输入须关联到自身实体的SlotResolved才计样，拒绝/超时使分析失败。现有`--ux-commercial-presentation`在Windows机器政策缺失处拒绝启动，记为受阻，未改原测试/业务政策换PASS。无全量测试/现场性能通过声明。

### R-02 样本和解释

使用默认ResidentHistory=200、MaximumOperations=10000，300个不同合法小写实体、唯一观察，左右交替且逐件等自身终态，不制造R-01同侧在途决策。合成ReferenceWcs也有本地文件IO；没有真实RCS网络。每档40次快照、深复制/容量/单次待回执查询探针，20次旧分页及20次实际UI冻结筛选/查询；原始样本和nearest-rank P50/P95/P99均保留。探针是不同实验，不能相加或用减法伪造分阶段归因。

|累计实体 / 驻留|快照P50 / P95 ms|深复制P50 ms|容量投影P50 ms|UI筛选查询P50 / P95 ms|
|---|---|---|---|---|
|0 / 0|1.87 / 2.99|0.27|0.03|3.93 / 10.16|
|50 / 50|41.20 / 53.11|8.97|5.56|8.85 / 11.47|
|199 / 199|138.27 / 203.97|20.91|23.79|26.70 / 35.14|
|300 / 101|76.12 / 92.40|8.58|12.25|40.46 / 61.44|

300件自身终态轮询P50/P95/P99为187.53/519.66/700.66ms，max875.86ms；单次冷启动602.12ms返回、716.16ms准入就绪，窗口显示至有效快照与Dispatcher空闲1435.78ms（均只有一次，不作分布）。原生显式刷新30次P50/P95为97.64/365.88ms。受控50ms持gate实验10次总读取P50/P95为129.57/142.00ms，不是现场竞争样本。

|原生空闲窗口|实际秒数|进程CPU秒|分配MB（十进制）|Private MB起→止|数据字节增长|50ms心跳P95 ms|
|---|---|---|---|---|---|---|
|1|26.64|8.88|524.9|140.6→156.4|0|83.94|
|2|23.96|3.47|1011.9|181.7→154.1|0|65.02|

计划每段400×50ms，实际时长以上表为准。双窗分配约42.2MB/s，单窗约19.7MB/s；CPU不单调，受首段预热/渲染/GC及开发机后台负载影响，不能据此声称双窗CPU更低。Private下降也不证明无泄漏。数据总量为8,747,158字节且两段空闲未增长；仅正常终态/无待回执条件，不代表异常积压日志已受控。VM通知23,817次覆盖订阅后显式刷新和两段计时器刷新，不能除以30解释为每次显式刷新数。199驻留的JSON快照约1.47MB，深复制及重复同步责任查询足以形成需继续追踪的门锁/分配热点。

`InputToOwnTerminalPoll`含本地处理、合成对端文件IO、快照与10ms轮询，不是扫码到屏幕呈现的精确延迟，也不是上游网络耗时。`NativeRefreshThroughDispatcherIdle`包含ServiceAsync调度、快照、VM通知和Dispatcher空闲边界，是刷新实验；50ms心跳含系统计时器量化。分配/CPU按整个测量段计，包含后台工作，内存升高不能据此判泄漏。

源码热点可复核：

* `ParcelCommands.SnapshotAsync`在同一gate内建投影并Wire.Encode/Decode全量深复制；该副本隔离保护可变原件，不能简单删复制或提前释放锁。
* `ParcelHistory.CapacityAvailable/Capacity/ArchiveEligible`、`ParcelGuards.Availability/PositionSnapshots`反复遍历驻留作业；`IsSettled`和ArchiveEligible各自读取PendingReceipts。后者是同步SQLite查询，待raw时还读/解码/hash文件。容量/准入、每位状态及全局快照重复调用，门锁内成本随驻留与资料责任增加。单次SQL探针不等于整段累计查询成本。
* 两窗口共享Application但各500ms刷新，业务连接未重建；快照成本按读取次数重复。PR58已减少位置重复通知，本轮不把旧UI问题重复实施。Snapshot设置仍通知共享字段；本轮保留了实际原生刷新通知数。
* `ParcelHistoryQuery.cs / ParcelHistoryReader`在冻结selection下执行JSON字段筛选；当前300条样本只覆盖小库存，不能说明客户保留期大历史已达标。启动验证原件/请求历史也未做长库负载。

### 后续工作与验收

发布前：保持R-01未决，由客户/上游确认实体识别和同侧在途规则；R-03由仓库管理者决定main保护和必需检查。SDK/RCS合同/真实组合与Unknown SOP、目标Windows完整安装/账户/迁移/恢复、签名/支持责任仍须分别取得。

性能工程下一包：先取得目标硬件、峰值读码节奏、双窗口使用方式、积压/原件/历史规模与延迟/资源预算；按本次探针定位gate等待、投影、SQLite调用次数和深复制分配。可优先评估**同一次gate内**批量读取待回执/资料责任与复用只读容量计算，禁止跨修订缓存准入结果；再评估隔离副本与按需投影。必须保留返回快照可变隔离、原请求/原报文/原目标、Unknown不重发、容量/身份/配置守卫及崩溃恢复回归，比较同硬件同DLL模式的原始分布；不得通过增大队列、删历史/校验或吞错取得性能改善。

试运行期间：按批准节拍和客户保留量回放、监测队列/回执资料/日志增长，覆盖峰值并发、上游慢/超时/Unknown和恢复；执行约定时长的CPU/内存/句柄/订阅清理/长期容量观察。关闭条件是合同规则与目标预算有负责人、目标Windows和真实组合证据完整；合成PASS不代替。

精简执行提示词：读取本工作包、原独立审查及本页证据，冻结最新main并核对并行PR；保持一个业务Application及原请求守卫。先按实测热点做gate内批量读取的等价性设计，再在隔离分支实施最小优化；同硬件/负载比较快照与实际UI延迟、SQL次数和分配，保留异常/恢复/资源验证与原失败。未获真实合同不得修改R-01、重试或去重规则；未做目标长时/现场不宣称商业性能通过。

## 剩余发布阻塞

R-01需客户/上游确认同侧在途观察的准入归属和新实体规则，保留原审查失败，不用UI或合成样本替代。R-02无目标硬件、峰值节拍、历史规模与延迟预算，本轮仅软件分析和短时测量。真实SDK、设备、RCS合同/Unknown处置SOP和受控现场验证未取得；目标Windows版本上的完整安装/普通账户/双账户/重启/升级迁移/备份恢复需另验；签名、支持责任及商业资格未取得。正式商业发布仍NOT_READY。
