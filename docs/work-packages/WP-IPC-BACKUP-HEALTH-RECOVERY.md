# WP-IPC-BACKUP-HEALTH-RECOVERY

2026-10-18用户授权直接复核并修复PR18评审4305731150/4305731157；执行基线main `0828654`（PR19已合并，无开放PR）。独立工作树`backup-health-recovery`，主工作区未跟踪资料保留。继承[19设计](../design/application-capability-19.md)、框架F-09、D/RV及公开范围，只用通用实现和合成数据。

## 设计与取舍

路径保持单一规范：仅本机枚举结果转换分隔符，持久清单一律`/`。不放宽全局ProductValidation反斜杠/越界/链接检查，不把外部清单的反斜杠自动改写。备份与恢复共用受限文件清单，拒绝Windows别名、重复大小写路径、越界、链接、哈希和集合差异；校验在创建恢复目标之前完成。旧Windows非规范清单保留原件并明确拒绝，不静默改写。SQLite online backup、原配置/回执/未释放原图、隔离暂停与Unknown原身份恢复维持。

健康预算由知道TimeoutMs和调用取消来源的ParcelRuntime拥有；ReferencePeer收到的单个token不能判定是调用预算还是停机，必须传播取消。统一启动、巡检和阶段检查预算，预算取消只报告NotReady，调用者/停机取消继续传播。发送后取消仍是Unknown且保留尝试/原回执责任，不进行通用重试。关联检查还发现旧loopback吞调用取消及PLC确认查询取消降级已知接受事实，均以新增失败回归锁定后修复。HTTP健康响应也设置有限内容读取，避免异常组件造成无界内存。

不增加第二框架、公共协议、配置权威、迁移SQL或依赖。三Application仍拥有业务流程。另一方案是在ReferencePeer吞掉所有取消，会把停机当普通断线；只改两行则遗漏恢复集合/启动无预算，不采用。

## 执行计划（writing-plans，同会话顺序执行）

- [x] 在现有`tests/Ipc.Application.Verification`增加`--backup-health-recovery`风险选择器；实际TCP延迟headers/body、取消和恢复篡改负例先跑main基线，保留失败。Windows路径通过同一BackupAsync旅程在Windows复验，本机不冒称实机复现。
- [x] 新增ParcelFlow内部恢复文件规范组件；`ParcelCommands.BackupAsync`和`ParcelRecovery.Restore`复用路径/清单检查，保留失败残件和原始业务数据。
- [x] 统一ParcelRuntime健康探测入口并修改启动、巡检、阶段调用；ReferencePeer限制健康响应读取，不吞调用取消。停机在发送前保留Prepared、发送后保留Unknown，不把停机视为worker故障。
- [x] 运行新增选择器、受影响17/18/19参考恢复测试及必要构建；发布portable解压启动冒烟。若无需UI修改不做UI重构或全量测试。
- [x] 保存实际日志/hash/命令和源码SHA，更新运行说明与manifest，开发分支提交并创建main PR，不合并。

Windows实机NOT_RUN；真实SDK/设备/协议/现场BLOCKED，Production仍拒绝。原评审线程的GitHub状态不由局部PASS自动关闭；历史失败和D/RV不倒写。

交付：[PR #20](https://github.com/yuan2go/importer-pc/pull/20)，运行源码2efb7b6；[实际结果](../runtime/backup-health-recovery.md)。只提交main PR，不合并。
