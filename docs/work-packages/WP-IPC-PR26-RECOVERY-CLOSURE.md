> 后续独立复审及用户授权归档/合并见[PR31归档](../reviews/pr31-independent-rereview-f068156/README.md)。本页原实施阶段的不合并范围和历史PASS保留形成时含义；本次不重复测试、构建或打包。

# WP-IPC-PR26-RECOVERY-CLOSURE

基线main **baac260214f910dca9ae4628ba6449b25188e2ac**。用户授权修复PR26独立复审P1/P2并查明architecture-quality失败，独立分支提交新PR，不合并。PR27及其他工作树、主树未跟踪资料均保留。

有效权威：框架F决策、22独立业务/兼容位、19/20原请求责任及AQD-10；[原独立报告](../reviews/pr26-independent-78298b5/README.md)为问题输入，[现行入口原字节](../archive/pre-pr26-recovery-closure/README.md)为历史，不改原批准/失败。

## 执行计划（writing-plans，当前会话顺序执行）

- [x] 固定main/工作树/并行PR，逐项复核两项仍存在，归档原报告、日志和受影响入口，先收敛文档。
- [x] 新增 `ArchitectureQuality.UpgradeConflictClosure.cs` 和CLI选择器：通过真实Application/Factory/SQLite构造DB4，证明未明WCS/PLC/Attached拒绝且DB/config/raw保持；正常已结迁移正例；schema1/2实际main、错误legacy范围、确认/重启、Pending拓扑转换均明确验证。先运行留存RED。
- [x] `ParcelRepository`覆盖DB4并读取归档责任，禁止迁移前逃逸；`ParcelRuntime`将配置确认写入置于旧责任检查之后；`ParcelReceiptCustody`在旧格式就验证OutcomeReconciled/归档证据，专用备份迁移保持原件。SQL/ABI不变。
- [x] `ParcelConflicts`恢复实际位，并以原ID/旧body追加审计扩充历史错误范围；`ConfigurationResponsibilitiesClear`包含未确认冲突，Pending不越过历史责任。
- [x] 查明Recovery02原FAIL：记录真实回执，受控健康等待验证在途拒绝与之后Unresolved，保留原关键断言；只修已证明原因。
- [x] 必要验证：`dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false`；运行 `--upgrade-conflict-closure`、`--architecture-quality`及受影响 `--review-closure`、`--receipt-custody`、`--configuration-confirmation`、旧发布/旧OutcomeReconciled迁移。每轮记录实际SHA/选择器/退出码/原运行根，失败不覆盖。
- [x] 记录兼容/回退、固定源码证据、Windows/现场后验和未验边界，提交推送新PR并附加到本任务，不合并、不重复打包。

DB2/3/4未结普通升级保守拒绝；不能只抬版本、删责任或盲重发。仅已有原Query终态且所有外部工作已终结的资料责任可由专用入口迁移；原Awaiting、原图/缺口、原包/冻结身份均保留。配置schema1/2/3兼容，SQL1～5/checksum与ABI1.6.0保持。业务源码变化须新发布身份，既有包/原件不可覆盖。

验收界限：macOS参考实际Application/装配/SQLite及故障注入；Windows安装/WPF/HID/UAC/ACL NOT_RUN，真实SDK/设备/WCS/PLC及现场BLOCKED，生产门禁不变。

本轮实际交付、精确源码、验证/历史失败及未验边界见[结果](../runtime/pr26-recovery-closure-results.md)。PR发布状态另读GitHub；本任务不合并。
