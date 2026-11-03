# STABILITY-INTEGRATION-02

2026-10-26用户授权：修复PR110中文快捷方式，继承原任务ST08并接入ST14，按真实祖先整合PR108 LIA-01～07及PR111。main a8cd4314，PR110工程980a1383，PR112当前叠加255ccd4a（原任务仍在补验证/回执，最终头待固定），PR108 665f43a9，PR111 4da6bf6e。隔离D盘分支codex/stability-integration-02，既有未提交/冲突工作区与受限附件不动。

先读AGENTS、INDEX、framework/decisions、P0、time-storage、latency-capacity及lifecycle-batch-preflight的工作包/设计/运行/验证和公开范围。按用户授权先归档当前增量指引，再补恢复断言；不重建七包、配置账、业务账或监督框架。不合并main、不发布/本机安装/控制设备。

当前工程假设：Unknown保留但另一位可能在监督重启后恢复准入。以真实双位Application子进程先验证；若证实，只在现有启动契约和原Paused账加入恢复核对围栏，恢复原请求/原文/目标、有限重启、永久Block保持。仅确认未提交且回滚的输入事务允许有限重试；不增加ST06自动重连。

先稳定110→112父子头，再依据merge祖先整合108/111，保留父历史，不摘取重复提交。交叉按业务语义解决：部署系统项/卸载清单、原操作身份、管理授权寿命、Fatal观察、备份取消/围栏、时间节拍、测试入口和同源介质。版本/Contracts/组合锁/工作流/指南/manifest统一，历史证据不变。

验证限本轮风险：三代码页COM；短锁、回滚、丢提交结果、持续IO、FULL/坏库、退出/有限拉起/循环保护、Unknown不重放和全位暂停；受影响授权/停止/备份/部署守卫。最后受影响产品构建、同源介质冒烟及最新CI。通过无新增风险停止扩测，不跑大库或长期全套。

G01真实RCS核对合同、G02密码恢复政策、自动重连合同BLOCKED_EXTERNAL；真实安装/UAC/任务/SCM/WER、物理存储故障、设备、目标26100+/4核8GB、24h/72h、604800完整UAC恢复、2592000均NOT_RUN。记录实际source/commit/PR/CI、运行/回滚与未完成边界，不继承分支PASS为集成PASS。

[当前报告](../runtime/validation/stability-integration-02.md)。
