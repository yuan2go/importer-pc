# WP-IPC-REVIEW-FOLLOWUP-11：PR9复核整改与组合所有权验证

2026-10-17；起点main `4ae5319c264f8cd0b2152d5d7242bb1ca39b65e5`（PR9已合并）；开发分支`codex/wp-ipc-review-followup-11`。用户已授权修复两个P2、补Product组合层动态故障、定向测试与更新同版参考包/报告/manifest，创建main PR，不自行合并。无生产WCS写入及真实设备动作授权。

前版本[原字节](../archive/pre-review-followup-11/README.md)保持；设计基准v1.1、schema/checksum/原请求/历史D/RV均不改变。[计划](../superpowers/plans/2026-10-17-ipc-review-followup-11.md)记录设计及验证选择。

- P2-01：Item与入场箱波次差异须常显，不藏于默认折叠辅助区域；未知保持未知，不以合成字符串差异新增生产业务守卫。
- P2-02：首次有效登录/进入新箱扫码状态，一次性转交专用箱码焦点并选择既有草稿，周期刷新不抢焦点；抽屉、投影过期、身份失效及不足区域须阻止或延后交接。
- V-Product：真实ProductRuntime启动与清理，动态验证来源未终止时store/journal/data/device租约保留，独立来源清理、原异常保留、晚完成后恰一次释放及重新启动。
- 发布：新的SourceCommit、Release/Storage/Contract、源码/模块/配置指纹与两个ZIP SHA；定向核心及解压包启动/维护冒烟。旧a080825包/证据不覆盖。

结果入口为[11实施报告](../runtime/review-followup-11.md)（完成后记录实际结果）。Windows环境BLOCKED、W01～06 NOT_RUN；真实集成B01～06 BLOCKED，缺项继承[06](../runtime/windows-06.md)与[05](../runtime/integration-05.md)，不以核心或编译结果关闭。
