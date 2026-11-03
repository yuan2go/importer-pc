# PR104 与 PR103/main 集成检查

2026-10-25，用户授权同步并在必要验证、最终HEAD CI/审查符合要求后合入main。PR104原HEAD为`168c713d4fe2c332c9ee2964e7cfaa69de3e4b97`；执行时main为PR103合并提交`c425435c6f980ca537a29e4e8353d12be341df61`，共同祖先`1dfb676a2970f80e80d635303d84dcec75676cf5`，main领先5个、PR104领先9个提交。采用将main合入PR104的合并提交，保留双方原提交，不rebase、不强推、不改变历史SourceCommit。

## 冲突与保全

六个共同路径中，INDEX自动合并后逐项检查；`.gitattributes`、manifest两文件、长期稳定性运行说明及验证Program发生冲突。两类证据的原字节规则并存；索引与运行说明同时保留可靠性增量入口和PR103完整维护失败结论；验证入口同时保留`--reliability-02`、强杀父/子进程和`--retention-qualification`。未改变其他分派或默认验证逻辑。

manifest按路径做三方合并，双方新增记录全部保留；共同变更的四条文件记录保存两侧原完整元数据，再登记合并后的Git blob/字节/hash。PR103的`retention_maintenance_qualification_01`元数据原样保留，其他历史记录不按当前文件批量重算。manifest自身校验使用Git规范化blob，避免Windows换行差异。

产品`src`和`config`与PR104原HEAD完全一致，保留生命周期所有权、健康Epoch、日志有界分页、单调时间退避及原请求责任；没有数据库迁移或业务版本提升。PR103的夹具、脚本、原始报告和证据均按原blob保留。新代码审查需要核对本次合并树与测试依赖，不能以原8da0ddc自动审查或旧CI40115977301代替。

只读复核命令：Windows先执行`. ./scripts/project-environment.ps1`，再运行`python scripts/check-pr104-integration.py --candidate <待核验提交>`。脚本检查双亲祖先、产品/配置树、PR103独有路径、两组证据清单逐字节hash、合并入口、历史manifest保全和本包当前清单。它不运行维护、不访问生产目录、不修改证据。

## 必要验证与最终门槛

本轮仅验证实际合并风险：锁定依赖Release构建；可靠性定向入口及9个强杀窗口；PR103完整原件夹具的小档显式模拟授权冒烟；manifest/证据/源码保全。最终HEAD由既有Linux与Windows工作流重新执行，Windows冻结发布、原生日志/管理/稳定性回归、权限拒绝和安装器证据保留实际合成merge身份。每个本机运行回执记录工作树或提交身份、实际程序集hash、命令、退出码及独立数据目录。旧PASS不扩张为新介质。

合并前核对远端main未变化、PR最终HEAD未移动、没有未解决审查问题、最终HEAD的Linux/Windows均成功、实际发布SourceCommit对应检查合并树。使用带expected HEAD的普通GitHub merge，不绕过保护。合并后核对实际main提交的双亲、源码树及PR状态，回执与CI链接记录在[PR104](https://github.com/yuan2go/importer-pc/pull/104)；若main前进则重新复核受影响部分。

本机必要检查已通过，原件见[集成证据清单](../evidence/pr104-main-integration-01/FILES.json)：Release锁定构建0警告0错误；`--reliability-02`退出0（29.35秒）；`--h02-crashes`退出0（16.52秒），九处强杀恢复均外发0次，独立输入/服务端账和恢复前后记录逐窗口留存。完整原件夹具的seed/create/显式模拟authority维护/verify均退出0，1000作业和1000输入退役，六类保护及永久身份保留；实际内部维护7.255秒，`ActualProtectedCli=false`、`ActualElevatedAuthority=false`。只检查此次分派/依赖整合，不构成受保护CLI或规模RTO重验。

本机程序集来自合并中的工作树，产品src/config与168c713相同，夹具Program仅增加PR103分派；实际DLL/hash、执行Program与脚本单列，不能称为最终HEAD构建。提交后CI单独固定最终HEAD和GitHub合成merge。只读核对已确认232个PR103独有路径、357份可靠性证据和210份维护证据原字节保留；7537条非本包历史manifest记录不变。本轮另有83份文本原件/hash，完整数据库和二进制留D盘。首版核对器逐文件启动Git开销过大，仅终止该只读核对进程，改为单一cat-file批量读取后相同断言通过；没有终止业务测试或修改原件换PASS。

## 仍未通过的资格

[PR103原始报告](retention-maintenance-qualification-01.md)保持原字节和身份：1千/1万完整受保护维护通过；604800条完整历史约9.46GB维护7855.705秒后查询中断FAIL，604800个作业退役、604806条输入仅退役157184条、剩余447622条。局部责任守恒不代表完整维护成功。2592000条未执行，604800条完整恢复因UAC取消未执行。

输入分页与完整命令预算修复属于独立[PR105](https://github.com/yuan2go/importer-pc/pull/105)，本次不合入、不关闭该缺陷或其资格缺口。目标4核8GB工控机、真实SDK/RCS、物理断电、正式安装接管及真实24h/72h现场长期稳定性仍NOT_RUN/BLOCKED。自动化测试、冻结发布、安装器构建和main合并均不替代这些验收。
