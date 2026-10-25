# 输入到期分页与维护预算

对应 [RETENTION-INGRESS-02](../work-packages/WP-IPC-RETENTION-INGRESS-02.md)，继承 PR103/main `c425435`。原完整规模失败和 PR102 查询修复仍各守原身份。

`ParcelHistoryRetention.ExpiredInputPageSql` 对外层 `parcel_ingress` 明确 `NOT INDEXED`，保留 SQLite rowid B-tree 寻址，避免 `(state,position,received_at)` 索引导致逐页筛选并临时排序。所有 state、completed_at、Observation/Measurement coalesce、保留 owner/永久退役子查询及顺序/LIMIT 原样继承。子查询继续使用唯一索引。游标只存在于本次独占维护连接，提交后推进；无并发输入/VACUUM，恢复时重新扫描，永久退役身份防复活。未命中保护行的顺序扫描最多经过一次；没有 OFFSET，也不缓存可变安全决定。

输入仍按最多256条一笔事务，批内复用预编译命令，原件 hash/state 进入永久退役表，输入及关联审计同事务删除。作业仍逐笔事务，永久 admission/reply/measurement/numeric 归属不删，WAL/FULL不变。没有把多个作业合成一个无界事务，也没有改 PR102 的 pending-command owner 派生表。SQL1–9原字节及DB9保持。

两小时预算现在始于 CLI 命令入口，Repository直调也在权限/租约/原库hash之前建立预算。单一截止时间传过初始化SQL、原件检查、分块hash、分步SQLite备份、恢复文件复制/核验、清理、后检查及可选压缩；阶段切换不重置。执行SQL每1000 VM指令检查，备份每256页检查，文件复制/hash每64KiB检查。取消不发布新的COMPLETED备份标记；已形成的完整备份和已提交退役事实保留，未提交输入页完整回滚。命令只有在Repository、factory和租约清理后才输出成功结果。

预算是协作取消，不是操作系统强杀。单次阻塞文件I/O、Flush、SQLite忙等待（原5秒）及必要回滚/句柄关闭可能在截止后结束；不会提前释放owner或以进程失控换“严格两小时退出”。维护前独立inspect、人工核对、恢复演练和业务接管不计入expire命令，分别计时。`parcel_retention_runs`的Completed仍表示明细退役核心完成，不能单独证明后检查/压缩/CLI成功。

版本为Sportmaster1.12.2、DirectPlc/WcsToPlc1.6.2，避开并行PR104的1.12.1/1.6.1；这不表示PR104已合入或其功能属于本分支。Application.Contracts1.17.0及DB9不变。新的公共平台程序集字节需整套同源交付，不能替换旧签名扩展的精确宿主依赖。旧未决请求仍按原实现/合同/目标处理，不因版本提升重发。

复现：先在Windows执行 `. ./scripts/project-environment.ps1`，构建 `tests/Ipc.Application.Verification` 和 `src/Ipc.Assembly` 的Release（`-p:RestoreLockedMode=true`）。`dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . --retention-ingress --large` 执行组件查询、实际分批删除及取消/恢复边界；去掉`--large`为CI1千/1万档。

完整受保护CLI继续用 `scripts/verify-retention-maintenance.ps1`，须真实安装者权限、管理员HKLM配置的D盘根及新Evidence目录。可用`-ReuseCompletedRun <先前PROTECTED-RESULT.json>`从已完成备份恢复完整合成原件到新目录；旧失败库、原备份和FIXTURE字节不变，新FIXTURE记录来源和原hash。仅接受该存储根Qualification下的合成夹具，actual restore仍校验整个备份manifest。`-RestoreAll`另做清理前完整备份的隔离恢复；对账仍用`reconcile-retention-qualification.py`。新预算说明写入回执，旧回执缺字段时沿用历史时点说明。

组件性能、完整维护、受保护权限/恢复、目标工控机及24h/72h/现场资格分别取证。未获完整规模结果前不据组件倍数外推RTO。原报告259.2万和60.48万恢复NOT_RUN保持原记录，新结果只在新验证报告登记。

技术依据：[SQLite rowid查询](https://www.sqlite.org/queryplanner.html)、[NOT INDEXED仍允许rowid查找](https://www.sqlite.org/lang_indexedby.html)、[VM取消回调](https://www.sqlite.org/c3ref/progress_handler.html)、[分步备份及未完成目标回滚](https://www.sqlite.org/c3ref/backup_finish.html)。实际产品执行计划和事务证据优先于理论估计。
