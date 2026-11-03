# SM-SERVER-PREVIEW-14 — 在线相机实时预览调度修复

2026-11-01，用户要求继续排查服务器相机在线但调试无视频的完整链路，找到代码问题直接修复。基线 main `3d7e0309`，复用本任务已有 D 盘 worktree 和 `codex/sm-server-preview-14` 分支；原工作区修改保留，开工无开放 PR。

沿内置 MVS 取帧、有界预览缓存、Application 管理只读契约和原生 WPF 轮询排查。继承 [11 工作包](WP-IPC-SM-CAMERA-STREAM-11.md)、[12 人工请求合同](WP-IPC-SM-MANUAL-CAMERA-INDEPENDENT-12.md)和 [13 资源生命周期](WP-IPC-SM-SHUTDOWN-RESOURCES-13.md)，不重建连接、解码或请求框架。

实际生产改动只调整同步持续采集循环的调度：每个现有采集所有者使用一个专用后台线程，避免永久占用 Application/预览/关闭协调共用的线程池。保留原 worker Task、取消、停止排空、设备独占、缓冲归还、SDK 完整性/ABI/物理身份、同帧解码、预览上限、管理授权和原请求/Unknown 守卫。不改相机参数、页面设计或多码规则。

RV-SM-SERVER-PREVIEW-14-01：独立进程约束共享线程池，仅加入新夹具在原产品复现“双相机 Ready 且有有效帧，但预览调用无法调度”；修复后通过并检查真实调用未退出前不释放租约。

RV-SM-SERVER-PREVIEW-14-02：原生 WPF 使用真实定时器与连续合成帧，覆盖无扫码视频、左右切换、历史页签、离页重进、管理撤销/恢复；另回归视频/锁图独立、扫码请求与停止/重连资源。

RV-SM-SERVER-PREVIEW-14-03：目标服务器安装对应 SourceCommit 后的实际双相机视频和现场联调；本轮无目标服务器调度/取帧原始记录，保持 NOT_RUN，不能把受限调度复现宣称为截图唯一根因或现场修复验收。

完整结果、运行命令及待验边界见 [实施记录](../../runtime/sm-server-preview-14.md)。公开范围仅通用源码、本文及合成验证文本；现场截图、配置、SDK、实际帧和账本不进入 Git。
