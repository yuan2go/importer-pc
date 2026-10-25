# CODE-RELIABILITY-HARDENING-02

2026-10-25集成续做：用户授权基于最新main同步PR104，在必要验证、最终HEAD CI和代码审查通过后合入main。本轮main固定`c425435c6f980ca537a29e4e8353d12be341df61`，已包含PR103；采用合并提交保留原分叉历史，不rebase或强推。处置与后续回执见[PR104集成记录](../runtime/validation/pr104-main-integration-01.md)。下文“PR103开放/不合并”是本包最初开工时的范围，不覆盖本轮明确授权；PR103的原始维护FAIL/NOT_RUN不改变，PR105继续独立。

2026-10-25 开工：远端 main `1dfb676a2970f80e80d635303d84dcec75676cf5`；D盘独立工作树 `D:/w/ipc-reliability-02`，分支 `codex/code-reliability-hardening-02`。原工作树及四个未跟踪附件不修改。唯一开放 PR103 为清理规模/受保护CLI资格，不并入本包或代其验收。

用户授权实际代码修复、必要工具/原始证据、文档、推送及main PR；不合并、不操作生产设备。沿四层、单实例多页、唯一配置/业务账；DB9 SQL1～9/checksum、REQ/BR/CAPI/D/RV、原请求及Unknown责任保持。现场条件未知不阻止代码级工作，真实SDK/RCS、物理断电、目标机24h/72h分别待验。

## 编码前风险—已有证据—必要补测

|改动风险|源码核对及已有证据|必要补测与处置原则|
|---|---|---|
|H02-01 崩溃责任|DB9 Pending先于交接；T1/T2/原件/T3；前阶段异常/正常Dispose恢复不能替代强杀|外部父进程在持久输入、T1/T2、远端接收、raw落盘、T3提交屏障强杀子进程；独立来源/服务端记录核对原ID/body/hash/目标、输入与接受，不自动重投|
|H02-02 生命周期|Stop排空、SDK任务/句柄保有、旧Epoch回调已实现；Start和Stop尚无共同启动所有权屏障|确定性交错Start/Stop/Dispose、Reconnect/Health/旧回调、接收落盘/Stop；取消只结束等待，所有者结束后才释放|
|H02-03 日志查询|确认每页从头扫描全部固定文件；Selection淘汰直接关闭在途句柄；CTS未Dispose；UI同步解析/拼接原文|固定文件上的有界索引复用、在途租用；记录扫描量、轮转/截短/hash、取消/撤权；后台格式化和总文本预算，原文完整导出|
|H02-04 存储争用|独立每位持久连接；共享SQLite单写者；已有慢marker跨位和队列满证据|真实BEGIN IMMEDIATE写锁、受控慢持久化/满队列、查询及停止重叠；核对交接false/true和Pending去向，依据实测调整等待预算，保持FULL耐久|
|H02-05 时间边界|确认RuntimeJournal恢复nextProbe依赖UtcNow；计时队列/处理已用Stopwatch；健康/业务时间有各自语义|局部TimeProvider接缝控制前跳/回拨，恢复相对等待用单调时间；快照寿命同理；健康确认不接受未来/过期证据，不改变业务报文/审计/跨重启规则|

前阶段证据：[稳定性](../runtime/validation/win11-24x7-stability-remediation-01.md)、[整合](../runtime/parallel-task-integration-01.md)、[保留查询复核](../runtime/validation/retention-performance-review-01.md)。已正确实现不重复重建。编码前清理全后台化与无限原文呈现的冲突说明，原字节见[归档](../archive/pre-code-reliability-hardening-02/README.md)；[增量设计](../design/code-reliability-hardening-02.md)只替代该范围。

验证先留修复前反证，再跑定向补测、受影响回归、Release/原生启动及现有CI；不反复全库测试。最终冻结验证保留真实源码/程序集hash、命令、退出码及原始日志；开发阶段未逐次保有二进制hash的记录明确标注未提交工作树身份，不用旧PASS扩张新树。已交付[运行/升级/回退](../runtime/code-reliability-hardening-02.md)与[五项处置和验证](../runtime/validation/code-reliability-hardening-02.md)，原始失败和最终冻结身份分列。
