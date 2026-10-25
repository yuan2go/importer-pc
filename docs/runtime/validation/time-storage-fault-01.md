# TIME-STORAGE-FAULT-01 · 问题卡与验证

F0 main a8cd431456f73e83040f75a0c5e3cc1423440d1b；初始红测试与System默认接缝提交b37f89ae。PR108/110/111未合并，原工作区/四附件和历史SourceCommit不动。Windows 11家庭中文版22631、约16GB，不是目标26100+/4核8GB。只用新D盘Simulation/本地合成对象，不改系统时间/机器政策、不接真实设备/RCS。

[初始红结果与DLL/log hash](../evidence/time-storage-fault-01/first-red.json)。[源码]定位机制、[测试夹具]人为注入/模拟时间与原生SQLite故障边界、[实测]本机时间分别记录。[文档/PR声明]不代替本次运行。

## ST12 问题卡

确认：b37的ParcelStorageCapacity.cs:16容量1秒、:23保留提示1分钟均以UTC差值节流。UTC回拨12小时而单调过去2秒，检查仍跳过，首个断言FAIL。PR111已有容量单调修复但不在main，本包没有转记其PASS。

修复：容量及保留提示节拍均GetTimestamp/GetElapsedTime，UTC留作审计/日历。全仓扫描512命中行及别名在[清单](../time-storage-fault-01-utc-inventory.md)。额外先红后修：操作员最长会话被回拨延长、管理模式CTS已到期而Status仍授权；服务15秒等待改Stopwatch。

验收：回拨、前跳不能跳过节拍、保留提示继续检查、操作员单调最长寿命、已过期管理token的状态拒绝，本机通过。残余风险：清单中的旧Product/Supply、可选重量、计划备份及持久委托等未做专项，不声称全仓风险已关闭。NOT_RUN：真实NTP/SCM回拨、跨班次长时与目标硬件。

## ST13 问题卡

确认：ParcelStages.ApplyReply及ParcelReceiptCustody、ParcelTerminalRecovery、ParcelLegacyReceiptRecovery都包含早于PreparedAt的拒绝；适配器SportmasterRcsTaskService只是本机赋时。真实应用调用本地ReferenceWcs期间，注入UTC回拨1小时且单调前进，首个用例形成Conflict/Blocked、仅一次外呼，断言FAIL。未发现独立客户文档规定必须用墙钟先后判冲突，不把猜测当合同。

修复：新增可缺省且不改变旧序列化的LocalClockEvidence，只由接收Application为新回包生成，保留原At/HTTP原字节；本机端点回拨、单调经过时间、原请求与原回包hash绑定。实时与恢复共用验证。ID/hash/终态矛盾等原冲突规则保持，已有Conflict不得解除。

验收：在途回拨正常接受；证据可持久恢复且无再次发送；无证明的早到仍拒绝；改证据/任意旧时间不被豁免；已有Conflict仍Blocked，本机通过。残余：不在观测范围的回拨继续保守拒绝。NOT_RUN：真实RCS/系统NTP、跨重启真时钟变化、现场联调；原RCS冻结/单次请求既有完整段需匹配本包CI另计。

## ST08 问题卡（完整闭环尚未完成）

确认更正：原Database.Open默认5秒，provider已有BUSY/LOCKED自动重试；不是“所有瞬时失败立刻停供”。本包初始用例在BeforeIngressDurable注入两次写前IOException，第一次即关闭accepting、来源返回false，断言FAIL。

已修：显式本地事务与提交阶段分类。只在未COMMIT且确认回滚时重试，原ID/body/hash固定，5秒单调预算/32次封顶退避。提交结果不明不重复、不报成功；FULL/损坏/结构类不做瞬时重试。WAL/FULL不变，未改DB9结构。

已验：两次写前I/O后第三次输入交接成功；真实SQLite写锁约188～194ms释放后只一行；两次写后、提交前注入有真实回滚而只留一行；真实已提交后的结果丢失只执行一次且报Uncertain；注入FULL及原生max_page_count耗尽均永久分类/一尝试/无行；持续I/O约5秒受预算限制；FULL=2/WAL保持。这里只是本地持久化阶段[测试夹具]，没有冒充物理抖动/写满。

未完成：main仍无ST14监督者。Runtime超预算后的致命日志/快速失败、监督拉起、永久故障抑制尚待PR110依赖方案，不能把当前关闭输入写成通过F1。已给出[具体方案](../time-storage-fault-01.md#st14依赖与待接入的闭环)。NOT_RUN/NOT_IMPLEMENTED：这些子进程闭环及真实磁盘故障、实际已登记任务/UAC/SCM/安装。

## 本机已有回归

新时间/存储夹具、Atomic边界、parameter-guidance-startup、deferred-connections、rcs-heartbeat、默认档retention-ingress均通过；core锁依赖构建、四产品WPF及自包含安装器编译0警告0错误。实际源与日志hash另附，不用这条摘要给后续提交授予PASS。未安装本机产品或政策，未关闭旧完整恢复缺口。

统一NOT_RUN：目标26100+/4核8GB、24h/72h、真实SDK/相机/RCS、物理断电、Production安装/接管、604800完整UAC恢复、2592000。未执行大档；没有把短测或CI授予现场运行资格。
