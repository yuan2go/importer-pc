# MANUAL-DEBUG-ROUND-LIFECYCLE-01

2026-10-31，按用户要求在最新 main `67ab87c1a1b146611504342c6ab080ed045231ec` 修复 PR162 的三个未解决审查线程：`PRRT_kwDOVLQU8s6x6o28`、`PRRT_kwDOVLQU8s6x6o2-`（P1）及 `PRRT_kwDOVLQU8s6x6o23`（P2）。不整合并行 PR161/163/164 的业务变更，不启用或调度 CI。

继承 AGENTS、INDEX、F-03/F-08/F-09/F-12 和 SM-MANUAL-CAMERA-INDEPENDENT-12 的现行合同：人工来源独立于相机，明确准备不外呼；发送必须持原冻结请求且仅一次；离页清理属于本窗原轮次，不宣称物理清场。已发送、在途、Pending 接收及 Unknown 保留原请求责任。

主流程：人工命令→Application 持久调试意图→PrepareWcs 冻结请求→明确发送→保全原回执。UI 只投影，不拥有设备或第二个发送器。PrepareWcs 可能先持久意图后返回 Blocked/Wcs=null；准备结果亦可能迟于页面离开到达，清理须覆盖这两个边界。

实现要求：共享“从未尝试且无在途阶段/待提交回执”判定；无 WCS 的失败意图允许审计后取消或明确替换；发送复用已有容量名额，新准入仍受容量限制，替换只扣除实际释放的一个名额，存储余量及其他生产守卫继续有效。异步委托固定原命令、RuntimeId、页面生命周期和代次，迟到结果按原 CommandId/RoundId 清理；其他窗口的新轮次不能被清除。

验证：真实 SQLite＋内置 RCS loopback 的容量1、ID耗尽、重复准备/发送/清理、旧轮次清理、新轮次保全、Accepted/Unknown/原始回执待T3；原生 WPF 延迟真实准备结果后离页、重进及另一窗口替换。只增加独立风险覆盖，不删除现行断言、不更改生产准入和平台保护来运行测试。软件合成、Production/Windows、SDK/客户RCS/现场分别记证。

现行12合同无需废除；本轮未发现必须删除的过期生产分支或测试。原索引先按字节归档，既有工作包/审查/验证记录不倒写。结果与精确运行命令见[实施记录](../runtime/manual-debug-round-lifecycle-01.md)。
