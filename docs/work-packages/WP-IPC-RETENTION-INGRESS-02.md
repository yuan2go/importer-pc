# RETENTION-INGRESS-02：输入分页与维护预算整改

2026-10-25，以 PR103 合并后的 main `c425435c6f980ca537a29e4e8353d12be341df61` 开工。独立 D 盘工作树，分支 `codex/retention-ingress-pagination-02`；PR104 可靠性任务保持独立。

本轮用户授权修复 PR103 新发现的输入清理分页增长复杂度及完整命令预算。此前 RETENTION-QUALIFICATION-01 的“仅补证、不改业务源码”只适用于原包；其失败、SourceCommit、数据库和完整备份原件不改写。PR102 已完成的命令 owner 查询整改继续继承，不重复实施。

范围：保留 DB9 / SQL1–9、永久身份、未决输入、原请求、六类保护、完整备份及事务原子性；验证实际产品 SQLite 的索引、rowid 游标、保护谓词和删除事务成本。预算仍为两小时，不降低 WAL/FULL 或安全条件。复用完整原件夹具和逐笔对账，记录组件、完整维护、真实受保护 CLI、目标工控机长期稳定资格的独立结果。

公开范围仅为通用源码、测试、复现说明及合成文本证据/hash；大型数据库、DLL、SDK、账号、生产数据及未授权附件不入 Git。F-03/F-09/F-12、REQ/BR/CAPI 和历史 D/RV 保持原身份；不能由本轮局部 PASS 宣布现场验收。

实际实现和复现见[运行说明](../runtime/retention-ingress-02.md)，当前结果及集成条件见[验证报告](../runtime/validation/retention-ingress-02.md)。产品固定a192b7f；60.48万输入组件、1千/1万完整原件模拟授权维护、逐笔对账和事务恢复通过；本轮完整规模受保护CLI的UAC启动取消，最大规模维护/恢复资格未关闭。交付[PR105](https://github.com/yuan2go/importer-pc/pull/105)，不自动合并main，PR104保持独立。
