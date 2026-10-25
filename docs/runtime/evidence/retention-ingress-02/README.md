# RETENTION-INGRESS-02 合成证据

产品固定`a192b7fe18b2f904fd7db9943e79b0173d0f8b28`，源main`c425435`。本目录只收集新合成文本/脚本/源码快照和hash，不收集数据库、DLL、SDK、安装数据或客户资料。`FILES.json`按字节列出本目录原件；`CAPTURE.json`记录来源。旧PR103资料保持原字节。

- `PRODUCT-a192b7f.json`：固定产品和首次夹具DLL；`FINAL-FIXTURE-IDENTITY.json`：后续强杀/计划测试的fixture身份，产品src保持。
- `ACTUAL-FULL-BACKUP-PLAN.json`：当前产品SQLite对原9.46GB闭合完整备份的只读EXPLAIN；不是完整维护/恢复。
- `fixed-component/RETENTION-INGRESS.json`：固定产品1千/1万/10万/60.48万输入组件。`development-component`保留较早未提交版，不能换写固定产品时间。
- `REGRESSIONS-fixed-a192b7f.json`及对应日志：五组实际定向入口和退出码；误传参数的首组回执保留原名。
- `SIMULATED-RECONCILED.json`及`simulated-*/`：完整1千/1万合成原件、显式模拟authority维护与55项对账，不是受保护CLI。
- `ingress-final.log`、`final-component/crash-*/`：输入事务真实子进程强杀、重开精确身份及恢复；不是物理断电。
- `UAC-START.json`：本次受保护完整规模尝试启动取消，无helper PID。没有通过UAC、权限或现场资格。
- `MIGRATIONS.json`：SQL1～9保持；构建、原失败日志和执行脚本另外保留。

复现方法见[运行说明](../../retention-ingress-02.md)。完整Windows验证继续使用既有`verify-retention-maintenance.ps1`；`-ReuseCompletedRun`从原完整备份恢复到新目录，源保持。只读计划入口：`dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --retention-ingress-plan <闭合完整备份.db> <新PLAN.json>`，必须Windows共享读排除写入，不允许忽略活动WAL。

最新CI状态及合成commit在[PR105](https://github.com/yuan2go/importer-pc/pull/105)单独核验；本地DLL/hash、原失败和各次CI不得互换身份。
