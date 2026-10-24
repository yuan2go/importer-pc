# WIN11-24X7-STABILITY-REMEDIATION-01 验证边界

2026-10-24后续独立复核见[历史清理查询报告](retention-performance-review-01.md)及[原件和复现](../evidence/retention-performance-review-01/README.md)：固定main `12a7cbd`、原CI合成源码`6cebb301`，验证PR100的一次性TEMP归属查询。下文各阶段证据保持原SourceCommit；新百万行查询、迁移和恢复短测不代表完整维护RTO、真实24h/72h或现场资格。

2026-10-24，基线main `c5ac51d21a820f7b39ef458d7cf0c403c2cdec6b`，开发工作树 `D:/projects/importer-pc-stability`。本记录只覆盖本轮代码和合成证据；原审查、旧发布、REQ/BR/CAPI/D/RV保持各自身份。固定Release的SourceCommit、SHA256、启动退出码和原始证据清单在[证据目录](../evidence/win11-24x7-stability-remediation-01/README.md)。

2026-10-24随后PR97由其他获授权工作合入main `c310705b5a927c90e8f6bb93de908b2fea36e826`，本分支整合为 `ac085f3`，保留有效格口/跨页回执/意图和导航代次保护。只把新任务链夹具的内置RCS版本改为本发布1.3.2，原断言不改；其后PR98已由其他获授权工作合入main `016976c`；本分支再次整合并保留其字体、布局及日志筛选恢复，固定组合再次单独计证。本任务没有代合并PR。

本机：Windows 11家庭中文版10.0.22631，AMD Ryzen 5 3550H，4物理核/8逻辑线程，约16GB RAM，.NET 10.0.11 x64。上述.NET10.0.11为开发/原生夹具宿主；自包含固定EXE实际报告.NET10.0.13，独立见ENVIRONMENT.json。全部工程、临时、测试、数据在D盘；未改变全机策略、ACL或生产设备。用户目标为4核/8GB、双位合计30～60件/分钟、已结明细保留7～30天。当前主机不是8GB目标机。

## 已执行与受阻

|范围|实际结果及边界|
|---|---|
|新增 `--win11-stability`|PASS：前笔T3失败时第二笔持久责任、重启检查、慢marker跨位隔离、诊断满载/实际写入恢复、4读者/2管理席位/排空、SQLite真实中断、7～30天参数共模、过期删除前后故障和永久防重。合成端口。|
|AQA01 / 双相机 / ResourceRisks|PASS：复用原风险用例；TCP为本机loopback真实socket，非现场相机静默断路资格。|
|Sportmaster RCS / 心跳|PASS：HTTP合成服务器，原请求Unknown/迟到、raw责任、恢复退役、额度耗尽、无重投、重叠续配拒绝；到期删除后保留数值owner及防重。真实查询/处置合同BLOCKED。|
|快照 / 维护 / 升级冲突 / 窗口生命周期 / 版本兼容 / Windows门禁|对应受影响选择器PASS。升级测试发现并修复ActivationFailed后相同候选无法重新产生激活意图的实际缺陷；断言未删除。|
|海康Probe self-test|35 checks PASS：模拟native阻塞/取消/帧守恒/缺机/停止持有句柄。真实厂商DLL、双机、已入native后的最长阻塞时间NOT_RUN。|
|原生WPF `--win11-stability`|PASS：实际窗口显示/核对持久未决输入、7天参数进入共模、独立线程捕获1400ms真实Dispatcher卡顿、恢复、关闭及探针注销。合成业务，非设备验收。|
|Python采集器/兼容/发布文档|5、17、2项对应测试PASS；短时工具执行和规模预置不算24h。|
|`--parameter-refactor`整体|FAIL_ENVIRONMENT：参数重构段通过，后续本机显示继承用例在受保护 `D:/ImporterPc.Deployment/UserData/<SID>/presentation` 遭AccessDenied；未改ACL。新增保留天数另由原生和应用测试覆盖，不能把该选择器整体标PASS。|
|commercial-productization完整恢复|BLOCKED：40项先行断言通过，RestoreAsync因安装者权限拒绝；完整受保护恢复/接管需获授权维护环境。|
|历史 `--audit03-rcs`|FAIL_STALE_EXPECTATION：旧测试预期恢复后能Resume再形成Blocked作业；当前受保护恢复隔离在Resume前拒绝。原断言保留，不能绕过交接门禁以跑后续流程。当前RCS恢复/退役由上述新旧RCS专项覆盖；该旧选择器仍未整体通过。|

测试命令均先 `. ./scripts/project-environment.ps1`。应用选择器：`dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --win11-stability`（其他选择器见原始RUNS.jsonl）；海康：`dotnet tests/Ipc.Drivers.Hikrobot.Probe/bin/Release/net10.0/Ipc.Drivers.Hikrobot.Probe.dll --self-test`。原生固定介质：`./scripts/verify-commercial-wpf.ps1 -Media D:/实际固定候选 -Output D:/新证据目录 -Win11Stability`。这些命令不授予生产接口资格。

## 数据规模与正常持续时间分开

`--stability-history-scale`在实际ParcelHistoryReader中查询 **2,592,000** 条合成历史索引/投影（30天、60/min上界）。预置41.379秒，DB 1,280,540,672字节，进程峰值工作集42,143,744字节；5次全历史metrics 3.473～3.504秒，单左位2.999～3.206秒，精确条码1.614～2.660毫秒。只说明该索引和投影路径，**不代表**全部业务原件/raw规模、8GB压力、启动完整性复核或持续吞吐合格。5个样本不报告长期P95/P99。

预置库不加入Git；保留合成生成代码和结果JSON。规模测试程序集哈希单独保留，不借用后续冻结介质身份。无完整24h正常段，没有可报告的24h吞吐、业务/UI P95/P99、最长停滞或资源趋势通过结论。

## R01～R12结果

|风险|代码及局部验证|仍需条件/资格|
|---|---|---|
|R01|DB9逐笔接收Pending/Interrupted/Processed/Inspected、每位worker和重启检查；T3两输入PASS|目标节拍完整来源/接收/业务账对账NOT_RUN|
|R02|普通诊断独立有界、Dropped/故障/恢复探测/实际排空PASS|目标磁盘长时故障趋势NOT_RUN|
|R03|已入native取消边界、Stop真实任务所有权及委托/handle保留PASS模拟|真实SDK最长阻塞和停止NOT_RUN；不无依据进程化|
|R04|缺指定SDK单机分类，按位隔离PASS模拟|真实双机掉线/重连NOT_RUN|
|R05|接收=交付+拒绝+在途，错误可见PASS模拟|真实帧和原生缓冲寿命压力NOT_RUN|
|R06|marker离开callback/共享锁，慢写时另一位实际T3通过|共享SQLite/磁盘失败仍阻断相关业务，目标机成本NOT_RUN|
|R07|保留真实原请求、Unknown不重发、迟到/重启合成PASS|用户要求先保留；权威查询/处置合同BLOCKED|
|R08|索引/事务投影、快照重复查询削减、底层读取消、并发上限PASS|全原件规模及启动复核/RTO、8GB/24h NOT_RUN|
|R09|完整授权I/O后台化、管理保留席位/迟到保护/所有者排空PASS|受保护原生部署权限场景部分BLOCKED|
|R10|容量告警、永久额度/续配/恢复退役、7～30天受控删除/压缩实现并测试|现场维护窗口、完整备份/恢复耗时及容量参数待实测；受保护CLI端到端NOT_RUN|
|R11|逐位进展/终态/最老等待、后台/资源监督及独立WPF探针PASS|实际冻结组合正常24h、72h及跨日NOT_RUN|
|R12|查询/导出/CTS/订阅/后台及真实停止任务排空，超时保有租约PASS|真实SDK永久阻塞与现场挂起处置NOT_RUN|

运行、SQL9升级/回退、保留天数、到期删除、完整备份、Windows ACL/SDK/NIC/锁屏/RDP/休眠/更新政策和采集命令见[运行指南](../win11-24x7-stability-remediation-01.md)。缺设备/合同/执行时间的项目保持NOT_RUN/BLOCKED，不以CI、模拟、加速预置或重启片段累计填补。工作包保留待资格验证状态。

整合PR97后的受影响回归（win11-stability、sportmaster-rcs、ux-commercial-presentation、sportmaster-window-lifecycle、原生task-hierarchy）通过；不将旧CI/旧冻结结果扩张至该树。旧精确扩展绑定须按现有启动维护/完整候选/兼容检查完成受控版本转换，缺原版本/资格时拒绝启动，不能把旧冻结请求的版本、ID和报文改成新值以消除阻断。

PR99首次Windows CI（head `12061273`，实际合并源码 `28fb86b1`，run `40083342205`）在原生管理/日志交互中发现状态观察占用管理命令席位，重叠刷新触发并发上限；安装目录/权限边界、首次配置/参数/任务链及业务流程已通过，但整个Windows job为FAIL。保留[首次日志](../evidence/win11-24x7-stability-remediation-01/ci-first-120612/WINDOWS-JOB.log)。后续修复把状态观察独立合并为一个有停止所有权的任务，管理命令两个席位不变；补充32个重叠观察、观察阻塞时撤销、完成后重新读取、停止排空及迟到授权不得覆盖新撤销状态的验证，原有原生断言保持不变。后续冻结和CI结果按各自SourceCommit另记。


最终本地冻结代码f938924：新增管理观察专项和UX迟到状态专项PASS，原生管理/日志恢复PASS、实际EXE只读启动PASS。该DLL的原生稳定性初次在Loaded刷新重叠时被新鲜度守卫正确拒绝；夹具a14c1d9增加有界等待及实际首帧就绪断言后10项PASS。生产DLL与夹具身份分别记录，首次失败保留，不把以前e235d85或200d8ad的原生证据改写成最终源码。
