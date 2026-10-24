# Windows 11 长期稳定性整改

2026-10-24归档增量：用户授权将已完成的[历史清理查询独立复核](../runtime/validation/retention-performance-review-01.md)、[原件和复现脚本](../runtime/evidence/retention-performance-review-01/README.md)经PR/CI合入main。复核固定`12a7cbd`，不重复修改业务代码；原证据身份、D/RV及待长稳/现场资格不变。

同日RECHECK-02：用户要求核查PR99查询风险并安全集成main；开工已确认PR99/100/101合入`5bb032d`，继承实际TEMP主键修复。独立百万规模、责任/迁移/故障复现及升级夹具竞态续修见上述报告的再核查段；新增CI升级专项，未改变生产查询/SQL1～9或真实资格。新分支`codex/retention-query-final-recheck-02`仅提交本轮拥有的增量，经最终PR/CI核对后合并，不吸收其他工作区材料。

状态：代码与本机专项验证已实施，固定Release记录独立保存，待现场资格；尚未取得24h/72h或真实设备资格。开工 main `c5ac51d21a820f7b39ef458d7cf0c403c2cdec6b`，独立D盘工作树 `D:/projects/importer-pc-stability`。2026-10-24核对PR97开放，head `a43ecb86efdc78ddc48e7082dcb6f96848138a8b`；PR98字体排版开放，head `2fb04fec970443539c73ced4482032c263ee32c7`。不代合并、不覆盖它们的有效格口、跨页回执或意图/导航代次保护。原工作区四个未跟踪附件保持本地。

依据：[原审查及原始ZIP](../reviews/win11-24x7-stability-audit-01/README.md)、[框架](../design/framework.md)、[决定](../design/framework-decisions.md)、[当前实施设计](../design/win11-24x7-stability-remediation-01.md)。原审查固定c91559f6及其Linux探针，不改写为本轮Windows结果。REQ/BR/CAPI、D/RV及SQL1～8校验和保持。

以下为开工时的风险—证据—缺口矩阵；实施结果在后列验证记录，不将开工缺口误作当前结论。

|改动风险|继承的现有证据/实现|开工复核与待补缺口|
|---|---|---|
|R01 输入责任|AQA01接收时间、InputObserved；ResourceRisks原件恢复|成功入队仍仅RAM，前笔T3失败丢后笔去向；补持久接收、故障/停止/崩溃恢复和逐笔核对|
|R02 诊断|有界256队列、轮转文件|满队列仍污染业务failure；分级、计数、写入恢复、排空|
|R03 SDK取消|句柄/委托保有、Stop失败重试|已开始native不可取消；取消后不得Ready，停止等待须有界且保有真实任务|
|R04 缺单机|TCP连接异常按位隔离|SDK缺机IOException未分类；仅无句柄或完整清理后可隔离|
|R05 SDK帧|原生短回调、有界队列、Epoch|overflow/异常帧计数不守恒，错误原因不可见|
|R06 慢marker|持久过载及确认账|共享锁内文件I/O；分离即时围栏和单一后台持久化所有者|
|R07 RCS Unknown|原ID/body/target/raw/attempt，Query=false|真实查询/处置合同未提供，外部BLOCKED；不虚构接口或重投|
|R08 历史/快照|有界工作集、同库归档、稳定查询上界|JSON扫描、重复容量查询、查询并发/取消与完整性核验规模成本|
|R09 授权I/O|后台主快照、日志中持续授权|日志入口同步Require；完整调用移后台并排空|
|R10 容量/编号|永久高水位、恢复退役、完整备份|剩余额度/预警、DB/raw/WAL/日志/备份预算与维护窗口|
|R11 独立监督|本地TCP健康与时间标记诚实|逐位终态/进展/等待、后台及独立观察，不能以WhenAll未完判健康|
|R12 关闭所有权|超时保留租约、窗口订阅清理|日志任务/不可取消I/O纳入排空，最终Dispose不可同步卡住Dispatcher|

验证按依赖执行ResourceRisks、AQA01、双TCP、RCS/心跳、恢复与相关原生验证；只新增本表缺口。每个结果记录源码、Release程序集、环境、命令、起止、退出码。真实SDK双机、RCS合同、物理故障、目标IPC、完整24h正常段及72h风险延伸分别计证；缺条件为NOT_RUN/BLOCKED，合成短测不扩张。

交付为开发分支提交及main PR，不发布安装包或操作生产设备。升级、回退、运行命令和逐项实测结果收录到本包运行记录；实施完成前不关闭本工作包。

本轮用户决策：4核/8GB、双位合计30～60件/分钟；现场选择7～30天，到期删除已结明细，永久防重与未决保留；RCS Unknown先保留。当前[运行/删除/迁移与回退](../runtime/win11-24x7-stability-remediation-01.md)及[R01～R12结果/未执行项](../runtime/validation/win11-24x7-stability-remediation-01.md)为实施入口。

收口期间PR97已由其他获授权工作合并，main `c310705b5a927c90e8f6bb93de908b2fea36e826`；本分支整合该main并保留其全部有效业务呈现守卫。PR98随后也由其他获授权工作合入main `016976c65a4ba3c9119f4cc0a131bae82283aae5`，本分支继承其字体/布局/日志恢复；没有代合并PR。上方开放PR状态为开工记录，不作为交付当前状态。
