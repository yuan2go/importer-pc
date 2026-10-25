# 卡顿与容量批 · 运行与维护

本包基于main a8cd4314，SM1.15.0、DirectPlc/WcsToPlc1.9.0、Contracts1.19.0；DB9及SQL1–9不变。[工作包](../work-packages/WP-IPC-LATENCY-CAPACITY-01.md)及[本次回执](validation/latency-capacity-01.md)分别记录范围、基线和实际验证。与尚未合并PR108/110独立，旧介质/PASS守原身份。

## 快照与详情

历史归档数量首次从独立只读连接读取，拥有取消/停止资源；归档修订变化才刷新，已知本实例归档增量直接更新缓存。业务gate内不执行count(*)。复杂作业原件投影按业务修订/管理授权缓存，实时设备健康、到期判断、进度和阻断每次重建；不能让缓存延长Ready或管理权限。每个Application API调用仍反序列化独立副本；并发窗口只合并同一授权状态下的在途UI读取，草稿、选择、CAS和反馈归各窗。

作业详情按冻结请求/回执身份及明确的可变状态组成修订键。结构化详情可见时后台生成，原JSON仅在诊断区域实际可见且有管理模式时生成；每窗最多一个生成任务，切换选择/授权后旧结果不发布。显示最多65536个UTF-16字符，截断明确提示；“复制完整快照”后台重新读取本地原件并按当前权限返回完整JSON，不向RCS发Query、不裁剪数据库原件。复制期间选择/权限变化不复制旧对象。未测出热点的其他配置文本路径不顺手重构。

## 在线备份的gate与取消

备份仍要求暂停、管理模式及外呼/阶段排空。gate只核对条件、捕获原配置/图像清单并建立备份围栏；数据库分步复制、图像核验、文件复制和哈希在gate外执行。独立SQLite源连接固定读事务，复用256页分步备份；同一个OwnedRead停止token贯穿数据库、64KiB文件复制/哈希和最后COMPLETED授权。手动和计划备份共用此路径；计划任务也受原读操作预算和Stop所有权约束。

围栏期间不Resume、改配置、处置/推进作业或运行清理/图像释放；查询及独立授权退出仍可进行。原Unknown、原尝试和已接收责任不修改。取消不发布COMPLETED；残件保留为不可恢复集，原数据/租约沿既有Stop/Dispose真实结束后释放，不能因UI取消等待就清owner。单次native I/O、Flush和回滚仍可能延迟，协作取消不是操作系统硬截止。

## 容量阈值与估算

已有硬准入reserve保持：free < MinimumFreeBytes时阻断。新增两级提示：默认Critical=2×reserve（保留旧预警边界）、Warning=4×reserve，必须reserve < Critical < Warning。4×/2×代表在硬围栏前保留3份/1份reserve的维护响应余量，是技术相对默认，不是现场磁盘足够的证明；64MiB旧reserve默认也不是现场推荐。预警/状态变化及近期预测进入诊断日志和管理模式现有辅助状态。

完整候选Retention可配置WarningFreeBytes、CriticalFreeBytes；空值采用上述派生默认。通过既有完整配置导入/审阅/保存及受控重启生效，常规参数表单保留未改的高级项。只展示片段示意，不能单独作为完整配置导入：

```json
{"Retention":{"ResidentHistory":200,"MinimumFreeBytes":536870912,"WarningFreeBytes":2147483648,"CriticalFreeBytes":1073741824,"WalWarningBytes":67108864,"WalCheckpointSeconds":60}}
```

近期天数使用每分钟采样、最多12个样本的SQLite物理页增长与准入序列差，时间用单调时钟。B=ΔDBBytes/ΔJobs，r=ΔJobs/ΔSeconds，剩余天数=max(0,free-reserve)/(B×r×86400)。没有正增长/新作业时明确InsufficientSamples，不把空闲或复用页猜成无限天数。只估计近期数据库物理增长；图像、日志、备份、未决积压和未来流量突增须另预算，不自动删除任何责任数据。

现场容量公式：峰值或经确认全天平均λ件/秒，d天，实测完整每件净增长B，则历史预算H=λ×86400×d×B。再加永久身份索引的年限增长、最长未决/图像积压、日志、WAL、完整备份集N份、一次维护超期增长及恢复工作区。需求可用空间至少覆盖全部同时在盘对象；同卷VACUUM沿原规则另需约2倍DB加reserve，完整备份不因线上退役自动销毁。reserve应覆盖近期增长率×运维响应时间以及必要恢复余量；由维护响应周期推导阈值，不能只按盘标称容量选数值。

PR106的604800约9.46GB是完整合成档案，不是现场、真实RCS或真实相机实测；其约15.6KB/件只能作为该合成规格示例。本站应分别测HDD/eMMC/SATA SSD等实际介质下FULL提交P95/P99、持续写入、备份/恢复及断电行为，不能由容量或CI推断磁盘延迟/可靠性。7天、60件/分钟全天上界为604800件；30天2592000件，本包没有运行后者。按剩余天数和完整维护耗时安排提前维护/扩容；运行中不VACUUM、不自动到期清未决。默认7天只是历史策略，不是自动在线清理。

## WAL与导出

每实例单个后台任务默认每60秒执行独立连接PASSIVE检查点，记录文件前后大小、生命周期高水位、busy、log/checkpointed/backlog帧及逻辑回收。默认超过64MiB告警（技术观测阈值，须按实际写入和最长读窗口调优）；Retention.WalWarningBytes为1MiB～1TiB、WalCheckpointSeconds为1～3600秒。日志及辅助状态均可见，观察失败明确Unavailable。停止等待这个真实任务，不遗留连接。

PASSIVE不强制等待所有读者；busy=0不等于全部完成，须比较log与checkpointed帧。读者结束后完成检查点表示WAL可复用，不承诺物理文件立即变小；绝不手删活动WAL。同一业务实例最多一个历史导出，第二个明确拒绝，原10万行/64MiB/2分钟预算和完整筛选/回执保留，未重构导出。

技术依据：[SQLite PASSIVE及三列结果](https://www.sqlite.org/pragma.html#pragma_wal_checkpoint)、[分步在线备份](https://www.sqlite.org/backup.html)、[dotnet-counters采集](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters)。本地实测、夹具和目标现场资格分别记录，不以资料声明替代实测。

同一窗口的新刷新不加入自己的旧请求；消费者用弱身份登记，不持有已关闭窗口。详情缓存还包括显式选择generation，普通500ms快照替换不增加该generation。
