# UNIFIED-RELEASE-01 恢复执行

当前恢复交付已完成：[最终 main cfe6c0e 的实际媒体与核验](unified-release-main-20261020.md)。本页下文的 c445/4f21/0f2cd71 是恢复各阶段的形成时来源；全部原产物和失败保留。

当前目标更新：在恢复构建过程中，main 实际合并 PR47/48/50 至 4f21b2d。本分支合并该版本，保留当前产品代码、指南和原始证据，并把两个重叠发行入口收敛为一个；新冻结需重新验证 1.4.0 / ABI1.10.0 / DB8。以下 c445/0f2cd71 为先前候选的形成时记录，不冒充最终组合。D:/r/u0f2cd 与 D:/r/selected0f 的既有输出保留，未取得成套成功回执，不发布为最终最新 main 介质。

2026-10-20 用户重新授权完成实际统一媒介与必要定向验证。最新远端 main `c445b0ec32a3db44f00a1968f3c501aa36eb786f`，复用 PR49 / `codex/unified-windows-release` 干净独立工作树。PR47/48 尚未合并；本包不引入其业务/DB8/UI增量。原工作区并行修改及报价表保持。

首轮停止回执、BUILDING 记录、部分 ZIP 和原失败保持 [原身份](evidence/unified-release-01/README.md)，不覆盖、不重标。各阶段使用全新目录冻结；最终八目标结果见上方记录，Windows 实机安装验收仍未取得通过。

发行机制沿 [原范围](../work-packages/WP-IPC-UNIFIED-RELEASE-01.md) 和现有 `scripts/release-windows.py`，不另建发行框架。PR48 使用相同入口且与本包重叠，后续合并时须选择同一工具实现并重验最终组合；本次候选不冒充 PR47/48 的合并介质。

恢复阶段发现 main 的扩展发布使用打开的 NamedTemporaryFile 建立硬链接后立即 unlink，Windows 抛 WinError32，导致成功路径及发布失败清理受影响。局部继承 PR47 已有“flush/fsync、关闭句柄后再 link/unlink”的修正；不引入其业务增量。第一次恢复候选来源 3b09d40 只完成 Sportmaster portable，已停止以采用此修正；原目录 D:/r/u3b09 保留，不纳入最终发行。源版本不变不代表精确宿主字节相同，扩展须重新按最终冻结宿主检查。
