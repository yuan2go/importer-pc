# 长期稳定性运行、维护及资格

[工作包与R01–R12](../work-packages/WP-IPC-WIN11-24X7-STABILITY-REMEDIATION-01.md) · [设计](../design/win11-24x7-stability-remediation-01.md) · [原审查](../reviews/win11-24x7-stability-audit-01/README.md)。当前源码Sportmaster **1.12.0**、DirectPlc/WcsToPlc **1.6.0**、Application.Contracts **1.17.0**、Parcel **DB9**。原SQL1～8/checksum、REQ/BR/CAPI/D/RV和旧媒介身份不改。实际冻结与验证身份在本页末尾关联的验证记录，不以本文或开发分支代替24h资格。

## 输入、故障和关闭

来源取得帧→驱动排队→Application持久Pending→业务Admitted→外部接受→物理完成是不同事实。true回调意味着原SQLite已经承担输入责任，不表示已准入或完成。每位同序；过期Epoch仍拒绝。消费者异常、停止、异常退出的输入保留Pending/Interrupted，启动暂停；维护页“原作业与接收责任”列出原输入ID、内容哈希和有界预览。逐笔检查实体及业务原件后填写说明确认，仅清该输入检查阻断，不重放、不改RCS未知结果。预览截断会明确标示，原DB内容保持。

普通诊断使用256项有界队列，拥塞/写失败公开Dropped、State、Failure、LastFailure、LastProgress；恢复必须实际写入探测成功，不清空业务故障。业务SQLite/raw失败仍关闭相关共享准入。过载marker有独立有界写者，不在双位共享锁内落盘；写失败不会伪造接受。

Stop关闭接收后等真实任务排空；读者最多4个、管理命令另有2个保留席位，管理状态多页观察合并为独立的一个在途读取（ManagementStatusOperations），不占命令席位、不缓存已完成状态。日志授权/扫描/导出、历史SQLite执行、状态观察、备份和最终Dispose均有所有者。UI超时只报告尚未停止，同一停止任务继续保有资源/租约。不得删lease、同时启动第二实例、杀进程后把原Unknown当失败。SDK已开始的native调用不能由托管token强行结束；Stop取消只取消等待，委托、缓冲、handle保有至native实际结束。若一直未结束，按下面挂起诊断保全证据，在获授权的维护窗口协调设备和进程处置。

TCP EOF/读错误按位隔离；无数据可能是无来件，本地Ready不证明远端仍活着，不发送虚构相机心跳。SDK1.0.4补缺单机ConfiguredReaderMissing分类、帧Received=Delivered+Rejected+InFlight及公开错误原因。真实SDK停止、重连、驱动/网卡断电与双机吞吐仍须目标组合实测。

## RCS Unknown和编号

用户本轮明确Unknown先保留。它表示已有外发尝试但没有确定结果；保留原ID、报文/hash、目标、合同、尝试及raw。真实适配器仍Query=false，不提供虚构查询或“改ID重投”。心跳恢复不解除Unknown。只有收到支持原请求关联、权威结果、时效、冲突和处置规则的真实合同，才能实现相应恢复；当前该项外部BLOCKED。

IntegrationIdBudget显示范围、永久高水位、Remaining；余量不高于范围10%提示RenewalRequired，0为Exhausted。按1件/秒峰值，剩余86400号仅对应约一天的业务请求额度（另核合同哪些操作消耗号码）。提前由外部系统分配独占新范围，暂停、管理模式保存完整候选、受控重启；旧请求保持原范围。恢复退役整个原范围，永久owner/range/retirement及回复哈希不随历史明细删除。

## 7～30天到期维护

用户确认4核/8GB，双位合计峰值30～60件/分钟，现场自行选择7～30天。系统参数“已结历史保留”填写HistoryDays，沿同一配置账/保存核对/首配共模生效。旧配置为空时保持原保留策略，不隐式启动删除。修改后重启激活；运行中的旧请求保持其冻结配置。到期提示HistoryRetentionMaintenanceDue表示有达到年龄的记录，维护仍逐笔检查是否真正可删。

执行前暂停并完全退出业务，在已登记的普通D盘路径保全资料；维护helper按既定安装者权限运行，不以链接绕过ACL。示例所有路径须换成现场确认值：

```powershell
# 源码开发命令先加载本项目D盘环境；安装介质不依赖源码脚本。
. ./scripts/project-environment.ps1
$release = 'D:\ImporterPc\releases\实际固定版本'
$db = 'D:\ImporterPcData\Production\实际站标识\sportmaster\sportmaster.db'
& "$release\app\Ipc.Assembly.exe" --action maintenance-inspect --file $db
# 使用刚检查的DatabaseSha256，不从旧记录猜测；destination必须为新路径。
& "$release\app\Ipc.Assembly.exe" --action expire-history --file $db --sha256 '实际原库SHA256' --destination 'D:\ImporterPcBackups\维护时间.db'
```

命令不启动reader/SDK/RCS。独占原库租约，检查已激活7～30天策略、暂停、DB9、预期hash及无未关闭WAL，完整校验原库/raw，先把可归档终态转同库原件并生成完整COMPLETED备份，再执行分批删除。只有主链终态有真实Committed raw依据、附加图像/上传已清、无未决回执/冲突/命令/输入，且最后业务变更早于期限，才删除明细。新DB9逐笔输入审计与接收账关联；到期已处理输入删除后保留接收ID/原哈希/终态凭据。旧未关联的审计不猜测归属删除。

保留永久Admission/Observation/Position索引、回复哈希、测量ID/hash、数值请求owner/range/retirement和每次删除凭据。已删除实体再次输入明确Blocked；身份矛盾形成可见冲突。中断前事务无删除，中断后已提交凭据不撤销；使用当前hash重新检查可继续。备份里的旧明细仍存在，备份留存/销毁独立管理，不能把删除线上明细称作备份也已销毁。

`expire-history-compact`额外在停止状态执行VACUUM；要求空闲空间至少两倍原DB加配置reserve，并在前后运行完整性核验。删除释放SQLite可复用页，不等于文件立刻缩小；VACUUM可缩卷，不代表SSD物理擦除。依据：[SQLite VACUUM](https://www.sqlite.org/lang_vacuum.html)。SQL清理/压缩有2小时执行预算，超时保留已提交凭据和完整备份，报告实际失败；不能回退旧编号或宣称全部完成。

## 容量、备份及恢复时限

按全天峰值做上界规划：7天302400～604800件；30天1296000～2592000件。实际全天平均节拍、每件原报文/图像尺寸须另测，不能只数操作行。MaximumOperations仅限制未结责任；ResidentHistory限制工作集，均不是总历史额度。

|对象|预算和维护责任|
|---|---|
|DB详情/归档raw|用固定Release完整链路测每件净增长，乘峰值日量×保留天数；另外预留一次维护超期增长。归档仍占同一DB。|
|永久索引/Unknown/图像|单独按设备使用年限及最长未决持续时间预算；不得到期删未决换容量。|
|WAL|独立观测；读者最多4个、2分钟预算，底层SQLite有progress中断。长读结束后复核checkpoint；不删除活动WAL。|
|普通日志|256项队列；原4×约5MB轮转及可选天数，丢失计数留可见。业务审计不由诊断轮转控制。|
|导出|每次10万行/64MiB/2分钟；目的路径新建，失败/取消有回执。|
|备份|完整集数量由计划MaximumSets控制，到限提示维护，不删除旧集腾空间；完整集含DB/raw/配置/原图/维护证据。COMPLETED之前不可用。|
|维护临时/压缩|完整新备份的空间加DB双倍临时空间及reserve；所有临时/cache在D盘。|

reserve是硬准入围栏；低于2×reserve提前提示。现场根据实测每小时增长G、维护响应时限T和备份/压缩成本设置reserve，至少覆盖G×T与恢复工作区，不能沿用64MiB技术默认宣称现场足够。备份继续暂停+管理授权+外呼排空的维护窗口；软件公开计划到期/失败，到期未做须纳入班次运维。恢复时间目标由现场确定，记录扫描校验、复制、启动复核、重新联调各阶段实际时长，未测为NOT_RUN。

迁卷沿现有recovery-plan/restore及受保护部署接管：容量预检、完整hash、原根退役、防双活、原部署身份与新根隔离。旧备份不能退役含更新责任的新根，不能绕过恢复交接。DB9不能交旧DB8及更早读者；回退程序只可配原隔离备份按既有恢复门禁核对，禁止业务账倒退或号码复用。

## Windows运行政策和独立诊断

GUI和application.ps1统一原生x64系统/进程、内核10.0 build14393功能门禁。该门禁兼容旧现场启动，不是Windows生命周期或24×7批准。Windows11版本很多，逐组合登记edition、完整build/累计补丁、CPU/RAM、磁盘/NIC/固件、x64厂商SDK2.0.0.4及文件hash；供应商约束和系统支持周期以[Microsoft发布信息](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information)及厂商依据为准。ARM仿真不作为本SDK交付组合。

本机记录为Windows11家庭版22631、4核/8线程、约16GB；仅本机执行项可用其结果。22000/22621、其他22631补丁、26100/26200/26300等x64家族需按现场实际版本独立验证，未测不标PASS。26H1等新硬件平台不因名称相似继承x64 SDK资格。8GB目标内存压力、Windows更新前后、跨日、锁屏/解锁、RDP连接/断开、多屏热插拔/DPI切换、NIC短断/静默断路、睡眠/唤醒均须独立用例。

日常仅已登记普通权限账户；管理员维护ACL/HKLM的D盘程序、数据、机器策略根，禁止映射/符号链接绕过保护。供包期间由现场批准的电源策略禁止自动睡眠/休眠及设备节能断链；更新、重启及驱动变更进入维护窗口，先暂停/Stop/完整备份。唤醒或断链不自动重发未知结果。软件暂停不是安全急停。

每个窗口独立500ms调度探针记录Dispatcher等待P95/P99上界及最大值；业务实例独立1秒写runtime-progress.json，公开每位State/LastProgress/ProcessingSince/QueueDepth/OldestWaiting、后台任务、查询/管理任务、诊断丢弃和进程/DB/WAL资源。不依赖UI定时器，也不以WhenAll未完成判健康。外部采集持有PID的kernel handle并核对冻结EXE路径、RuntimeId和SourceCommit，进程退出、重启、采集间隙或监督过期均不能拼接成连续段。

崩溃转储可由管理员按[WER LocalDumps](https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps)为Ipc.Desktop.exe单独配置D盘受控目录，DumpCount建议现场明确小上限（例如2），保留足够磁盘并限制ACL。挂起用经批准的[ProcDump](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump)在原PID仍在时抓取一次完整dump；完整转储可能含原报文/凭据，仅本地受控保管。工具未随本包安装，未自动修改WER/电源/更新策略。工具退出/超时不代表业务native线程结束；先保留租约、记录worker/native状态和堆栈，再按批准的现场停止程序处置。

## 长稳和数据规模验证

复制[计划模板](../../config/stability-target-plan.example.json)到D盘证据目录，填写实际SourceCommit、硬件/Windows/SDK/RCS组合、阈值、磁盘与证据预算及维护窗口。模板默认双位各0.5Hz仅对应用户确认的合计峰值60/min；低负载或偏载另开独立场景。空白条件会拒绝采集，不虚构批准人。真实相机测试需要已授权现场条件。

```powershell
. ./scripts/project-environment.ps1
python scripts/stability-qualification.py collect --plan D:/stability/plan.json --release D:/releases/固定候选 --progress D:/data/Simulation/站标识/sportmaster/runtime-progress.json --output D:/stability/新24h目录
# 完整正常段结束后停止并用同发布生成完整离线备份；source-events为独立来源日志。
python scripts/stability-qualification.py analyze --collection D:/stability/新24h目录 --database D:/backups/完整备份.db --source-events D:/stability/source-events.jsonl --output D:/stability/分析.json
```

`collect --smoke-seconds 30`只检查采集器，永远SMOKE_ONLY。正常段默认15分钟预热后连续24h；故障注入另开目录，不计入正常段；按风险执行72h及多次跨日。使用原[独立loopback负载脚本](../reviews/win11-24x7-stability-audit-01/verification/LOOPBACK-README.md)时保持127.0.0.1、双位独立节拍，负载总时长须覆盖预热+完整正常段+两端核对余量。socket-write只是来源写出，分析通过唯一合成条码/位与接收账逐笔核对；重复/多码/跨边界/缺失均单列，不静默丢弃。真实SDK来源需提供同等可关联的原来源日志后按其合同适配，不冒用合成条码结论。

分析先校验COMPLETED全文件hash、DB完整性及DB9，再流式导出输入ID/原hash/来源Epoch/序列/去向，计算逐位排队和业务处理P95/P99/最大值。统计业务处理完成不代表RCS接受或物理完成；并发源日志/RCS日志/原业务账继续核对。采集提供UI延迟、worker状态、资源趋势、最老等待与节拍，仍需审查性能阈值、真实设备合同和故障恢复证据。工具只给COLLECTED_REQUIRES_RECONCILIATION或ANALYZED_REVIEW_REQUIRED，不自动授予生产PASS。

当前没有完整24h/72h、目标8GB、多Windows版本和实际双SDK/RCS组合证据，这些资格均NOT_RUN/BLOCKED。规模预置与短时原生验证单独记录，不能累计或改标为长稳。

实际构建、冻结SHA及原始证据见[本轮验证记录](validation/win11-24x7-stability-remediation-01.md)。
