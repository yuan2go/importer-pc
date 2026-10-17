# 独立审查01：原始证据与复核入口

审查源码：`8cabf06d3c6d53ba551b65e58a13db6cdcaba16a`，2026-10-17。所有运行均为隔离合成环境；未使用生产 WCS、真实 SDK 或设备动作。

## 目录

|路径|用途|
|---|---|
|[完整报告](../../../reviews/project-conformance-audit-01/report.md)|结论与证据解读，避免从单条 PASS 推断整体验收。|
|[来源登记](publication-provenance.json)|原始 PDF/ZIP 身份、逐项复制/派生/省略原因、原数据库哈希。|
|[原完整包清单](original-evidence-manifest.json)|原证据包全部条目，含未重复入 Git 的运行目录；不是本目录的存在性清单。|
|[本目录校验](SHA256SUMS)|本次 Git 证据目录内的文件哈希，不包含该校验文件自身。|
|[运行环境](verification/verification-environment.json)|Linux/SDK/锁定依赖、原路径与重现说明。|
|[命令与退出码](verification/verification-results.json)|选择器与两探针的原始 argv、时间及退出结果。|
|[完整覆盖](coverage/coverage.json)|544份文件的读取深度，不宣称整仓逐行审计。|
|[远端结束快照](metadata/remote-final.json)|原审查结束时 main、PR、Actions 状态。|
|[本次发布检查](publication/checks.json)|原件身份、新增链接/清单、源码未改和既有资料校验结果；全局267项历史错误原样保留，新增错误0。|

构建：[restore](logs/restore-escalated.log)、[build](logs/build-current.log)。必要选择器：[architecture](logs/verification-architecture.log)、[followup](logs/verification-followup.log)、[baseline首次](logs/verification-baseline.log)、[baseline复验](logs/verification-baseline-recheck.stdout.log)及[复验身份](verification/verification-baseline-recheck.json)。首次受限环境 [build.log](logs/build.log) 和首次 baseline 尾部不完整记录保留，未删除失败或改断言换 PASS。

A-01：[原探针](probes/contradiction/Program.cs)、[工程](probes/contradiction/ContradictionProbe.csproj)、[运行日志](logs/verification-contradiction-run.log)。A-02：[原探针](probes/health/Program.cs)、[工程](probes/health/HealthRepro.csproj)、[实际结果](probes/health/evidence/health-result.json)、[原运行日志](logs/verification-health-run.log)。两组探针用真实应用/SQLite；健康探针使用当前独立插件和自建 loopback，仅6次健康请求、0次副作用。

## 数据库和复核方法

[synthetic-databases.zip](probes/synthetic-databases.zip) 保存五份原字节：矛盾探针 station.db/WAL/SHM，以及健康探针两个对照 station.db；ZIP 内含各文件 SHA256SUMS。只向新隔离目录解包并按原组合读取，不能在证据原件上打开写连接、恢复或清理 WAL。

1. 新隔离目录取得固定源码与 global.json 锁定的 .NET SDK10.0.402；locked restore 后构建原工程，不修改原断言。
2. 原验证器入口与实际 argv 见上面的运行 JSON。复核只选与所查结论相关的选择器，使用新的独立数据目录和本地 Fixture。
3. 探针 csproj 保留原实际使用的绝对 ProjectReference。复核时仅在另一个临时副本映射到固定源码的 Ipc.Product.csproj，不改本目录原件。健康探针独立插件的原源码位于 `probes/health/plugin/`，按冻结公共契约构建；生成插件、密钥和包只留在隔离目录。
4. 矛盾探针参数是新空数据目录。健康探针参数依次是新空证据目录、独立插件 DLL、固定源码 SHA。记录本次环境/时间/命令/二进制身份，不把旧日志改成新版本结果。
5. WPF 交叉编译只证明构建；没有 Windows、真实接口/设备和现场验证就继续标 NOT_RUN。

完整原 ZIP 为 `WP-IPC-PROJECT-CONFORMANCE-AUDIT-01_8cabf06d_20261017_evidence.zip`，SHA-256 `7b3206639a3c28464348dbcf672a707485536d8196a19e0aff793d1acc4f0be0`。原包保留所有临时运行目录、二进制和数据库；本次选择性入库不销毁这些原证据，不把本目录称为原 ZIP 的字节等价替代。
