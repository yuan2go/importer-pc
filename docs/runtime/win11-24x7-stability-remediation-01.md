# SM 长稳采集与完整备份分析

本页是当前操作说明，不授予生产资格。当前 Parcel DB13 只接受真实基线与结构；非当前格式在写入前拒绝并保留原件，不提供升级、迁移或双路径。历史规模、短测与失败结果见[原验证记录](../archive/runtime/validation/win11-24x7-stability-remediation-01.md)，仅适用于各自来源提交，不表示当前版本或连续24/72小时现场已通过。

## 运行和维护边界

相机帧、观察、持久输入接收与处置、实际放置、RCS 请求意图、发送预登记、原始回执和外部接受是不同事实。SM 同次放置只请求一次；重复观察可以合并，不按相同条码推断同一件。人工键盘/PDA 独立准入；RCS 离线暂停自动识码并保留预览。新站默认海康原生识码，已有站点保留已保存路线。管理中心打开时暂停自动供包和识码，明确调试读取与发送各自执行。

Unknown 保留原请求、原目标、原报文及尝试，不盲重发。确认离位且调用及回执责任收口后进入历史，不占当前放置。新请求默认由 RCS 分配格口，也可按左右位共用设置指定格口；Application 自动持久分配请求编号，重启不复用；不配置编号范围。永久编号所有权及原始回执不能通过清理、恢复或新建目录绕过。

业务库、原请求、必要回执损坏或实际存储不可用继续阻断业务。SQLite 备份复制错误可能来自目标盘，不能仅凭异常类型判定业务库已损坏；复制失败后在原操作预算内独立只读核验源库，只有源库自身的读取或完整性失败才进入业务存储致命守卫，目标失败保留失败回执与残件、不发布完整集。源库核验因本次取消而未完成时保留未确认事实，不伪报源库损坏或备份成功。备份计划状态是辅助信息，其读取、解析、写回失败时停止该计划并告警，保留原文件，其他维护和合法供包在业务存储正常时继续。未启用计划不读取残留辅助文件。恢复方法见[实施手册](../operator/implementation-guide.md)。上次运行中断、结果尚未核对时保留原目标，不自动再次备份。

完整备份包含数据库和同名 `.recovery` 目录，只有 `COMPLETED.json` 发布且全部文件校验成功才可使用。在线备份的枚举、快照和文件复制共用现有围栏，期间不删除依赖的原件；文件复制不长期占用业务锁，成功、失败和取消均释放围栏。只有 `BackupCreated` 更新最近成功信息，拒绝、取消、失败及结果未确认保留完整回执和真实原因。

备份份数到限停止新增，不自动删除旧集；完整集和恢复工作区单独规划容量，64MiB 技术保留值不代表现场充足。恢复到新隔离目录，核对机器身份、原根退役和外部效果后才能启用；不能撤销已经发送的请求。到期维护继续保留永久身份、历史 Unknown 和未收口责任，具体入口见[生命周期维护](lifecycle-ux-current-guidance-01.md)。

## 采集

复制[计划模板](../../config/stability-target-plan.example.json)到 D 盘，填写实际来源提交、硬件、Windows完整版本、SDK/RCS组合、阈值、磁盘与证据预算。工具不启动或控制设备，不拼接重启前后的运行实例。正常段、故障注入和各次重启分别用新目录。

```powershell
. ./scripts/project-environment.ps1
python scripts/stability-qualification.py collect --plan D:/stability/plan.json --release D:/releases/固定候选 --progress D:/data/Simulation/站标识/sportmaster/runtime-progress.json --output D:/stability/新24h目录
```

`--smoke-seconds 30` 永远标记为 `SMOKE_ONLY`。正常段默认预热900秒、连续24小时；72小时按现场计划另行执行。计划里的输入速率是 Application 接收速率，不是相机帧率、物理件数或新请求速率。真实相机连续观察的节拍与回环合成负载不同，不能沿用合成输入目标作为现场漏件率。

采集保留 PID、RuntimeId、SourceCommit、实际单调时长、资源、UI探针和后台任务进度。进程退出、身份改变、采集间隙、时钟异常及监督过期分别留缺项。软件停止等待真正的SDK和后台任务退出；超时只取消等待，不伪报资源已释放，也不清除 Unknown。Windows电源、更新、网卡和SDK组合须在目标机单独验证。

## 完整离线分析

先通过同源应用生成完整备份，停止或等待备份完成，不接受活动库、WAL/SHM/journal侧文件、缺件集合或仅改版本号的伪 DB13。`--verifier` 指向同源介质中的 `app/Ipc.Assembly.exe`（开发可用同一入口的 DLL）。核验复用 Application 的 ParcelRecovery 和 Repository，检查完成标记、路径及完整文件集合/哈希、配置与应用身份、当前数据库基线/真实结构和完整性。分析前后各核验一次，SQLite 用不可变只读方式打开，不创建侧文件。

```powershell
python scripts/stability-qualification.py analyze --collection D:/stability/新24h目录 --database D:/backups/完整备份.db --verifier D:/releases/固定候选/app/Ipc.Assembly.exe --output D:/stability/分析.json
```

没有独立来源证据时报告 `NOT_RECONCILED`；实际放置数、外部RCS接收请求/响应数及正确率为未知，不补零。只分析正常时间窗内、同一运行实例的输入，按输入关联的 OperationId/RequestId读取原作业与回执，绝不按条码把重复观察算成重复请求。备份时的结果标为 Snapshot；调用完成和HTTP响应须有同一运行实例、本时间窗的本地回执时钟证据，缺失单列，不能把时间窗后收到的结果算入本段。清理导致缺少明细时列出缺项，不从统计汇总猜配原件。人工命令、帧和来源实际件数不能由该输入账完整重建时仍须补证。

现有回环发生器每个位置使用唯一合成条码时，可明确增加 `--source-kind loopback-unique-code --source-events D:/stability/source-events.jsonl`，输出仅为 `SYNTHETIC_LOOPBACK_ONLY`。socket 写出不等于应用接收；缺失、重复、未写出、未处置及窗口边界差异单列。真实标签、同码重放和实际相机不能套用此关联。

排队、处理和 UI 各自统计。输入排队加处理的 P95/P99 是逐条相加后再排序，仍不等于曝光至格口显示；阶段 P95 不相加。相机主机交付至格口结果的同观察端到端统计见[双相机性能](../design/sm-dual-camera-performance.md)，MVID 调用包含等图，不能与 ZXing 纯解码直接比较。

输出新文件，不覆盖先前分析。容量、路径、格式、原件哈希或关联身份校验失败不发布成功报告。报告始终为 `ANALYZED_REVIEW_REQUIRED`，保留采集状态、来源提交、运行实例、实际时长及缺项，不签发生产资格，不恢复已退出的运行时资格门禁。烟测、合成验证及重启分段不能累计为连续24/72小时现场运行。

当前任务不执行真实24/72小时测试。编译、本机测试、CI、Windows实机、真实SDK、客户RCS与现场长稳分别报告，未运行明确标注。
