# UNIFIED-RELEASE-01 恢复执行

2026-10-20 用户重新授权完成实际统一媒介与必要定向验证。最新远端 main `c445b0ec32a3db44f00a1968f3c501aa36eb786f`，复用 PR49 / `codex/unified-windows-release` 干净独立工作树。PR47/48 尚未合并；本包不引入其业务/DB8/UI增量。原工作区并行修改及报价表保持。

首轮停止回执、BUILDING 记录、部分 ZIP 和原失败保持 [原身份](evidence/unified-release-01/README.md)，不覆盖、不重标。本次在全新目录冻结；真实结果在执行后填写。当前状态 BUILD_PENDING，不能提前宣称八目标成功或 Windows 实机验收通过。

发行机制沿 [原范围](../work-packages/WP-IPC-UNIFIED-RELEASE-01.md) 和现有 `scripts/release-windows.py`，不另建发行框架。PR48 使用相同入口且与本包重叠，后续合并时须选择同一工具实现并重验最终组合；本次候选不冒充 PR47/48 的合并介质。
