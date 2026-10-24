# Windows 11 外部采集器

交付脚本：`Collect-Win11Stability.ps1`。本轮在 Linux 审查环境编写，**Linux 语法解析通过，Windows 原生执行 NOT_RUN**；使用官方 PowerShell 7.6.8 解析得到 0 个语法错误，证据见 `../evidence/collector-syntax.json`，对应脚本 SHA-256 为 `0ABB01E2B95E4C698A1B45F2EB12FCCF8DECD3B86FB8A382AC7C7B7575779833`。没有现场采样，也没有执行长稳测试。应先在隔离 Windows 11 上运行 1 分钟、核对 PID/窗口/CSV/退出行为，再用于正式 24 小时实验。

## 执行

应用由实验负责人按原安装、参数和身份规则在隔离环境启动，采集器只附着已运行进程：

```powershell
.\Collect-Win11Stability.ps1 `
  -TargetProcessId 1234 `
  -DataRoot D:\isolated\sportmaster-data `
  -OutputDirectory D:\audits\sm-tcp-24h-001 `
  -DurationHours 24 `
  -ExpectedSourceCommit c91559f6f981a9f2c9f89bd5afc271089f0e9d8e `
  -Combination 'Sportmaster / tcp-line x2 / exact RCS package identity supplied separately'
```

`ExpectedSourceCommit`、`Combination` 是操作者声明；环境文件另外读取实际进程可执行文件的哈希、相邻 `application-release.json` 与其哈希，两者必须人工核对。不能把声明当成验证。实际版本如不一致，应重新固定实验，不修改介质身份。

输出目录必须全新，且在应用数据根之外；支持 Windows PowerShell 5.1/PowerShell 7。同一权限账户通常足够，受保护进程读取失败会记录，采集器不自行提权。不接设备、不发送业务指令，不修改电源、更新、注册表、日志或数据库，不启停应用。

## 采集范围与上限

- `process.csv`：默认每秒一次，CPU 单核等效百分比和整机归一百分比、Private Bytes、Working Set、线程/句柄、GDI/USER、进程累计读写字节、外部 WM_NULL 响应和采样间隔。
- `storage.jsonl`：默认每分钟一次，在独立的采集器子作业读取文件元数据与剩余空间；跳过重解析点，默认最多 20,000 个文件/4,000 个目录/5 秒合作预算。超限为 PARTIAL，不当作完整目录大小。如果底层文件系统调用阻塞超过合作预算+3秒，主采集器停发后续存储扫描，最多保留一个未完子作业，继续进程/UI采样；不会在主采集循环同步调用可能同样卡住的 Stop-Job，不声称已经终止底层系统调用。
- `environment.json`：具体 OS/Edition（CIM Caption）、版本构建号、CPU/内存、安装的 .NET sharedfx 注册表条目、目标进程已加载的 CLR/WPF/SQLite/海康相关原生模块版本和路径。未装 SDK 不阻断采集；这些信息不能替代设备型号、固件和 SDK 厂商支持证明。
- `events.jsonl`：进程段起止、粗 UI 探针失败/恢复、存储采集超时、采集器异常。
- `summary.json`：实际持续时间、样本数、进程身份、粗 UI 延迟分位数、最长观察到的失败跨度、采样最长空缺、资源极值。结论固定为 `EVIDENCE_ONLY_NOT_A_PASS`。

WM_NULL 的 200 ms 超时只是采集设置；它不是产品验收阈值，也不是精确 Dispatcher 排队时间、可见帧响应或业务完成时间。无窗口、锁屏、最小化、跨会话权限变化都须结合原始记录解释，不能自动判故障或成功。UI 延迟分位数只统计返回的探针；失败数独立报告，不能从成功样本的分位数掩盖失败。

采样任务不追赶丢失的时间点，真实采样间隔单列；文件元数据扫描本身可能与业务 I/O 竞争，因此需对比开启前后的短时基线，必要时将低频扫描降至 5 分钟。脚本最多允许 168 小时，每秒至多一个 UI 样本；不会无限保留全部进程指标在内存。UI 成功延迟列表最多约 60.5 万条，仅用于最终分位数。

进程退出/身份变化后当前段终止，退出码 3；采集器自身错误退出码 2。没有自动重启，也不自动附着同名新进程。新进程必须新 PID、重新执行、新输出目录；中断前后不得合并成连续无中断通过。Ctrl+C 只停止主采集器并尝试写最终摘要；尚未返回的元数据子作业会以其JobId记录，留在当前PowerShell会话，不等待它停止，也不能把它写成已取消。请先诊断该卷，不要再启动其他元数据采集器叠加阻塞。控制台被强杀时，以已刷新的原始行和最后采样时间为准，不能补造结束时间。

## 必须另外补齐

1. **独立节拍业务账**：以负载发生器的独立时钟和唯一观察/实体标识记录 offered/emitted；联结应用 `InputObserved`、作业/请求/响应和独立 RCS 接收账，核对 admitted/rejected/completed/unresolved。WM_NULL/进程存活不能代替此账。队列深度、最老等待年龄及准确 Dispatcher 投递/执行时间目前没有现成对外指标，本脚本明确不伪造；需复用测试入口或后续增加受控、只读且有界的探针。
2. **托管堆和 GC**：隔离环境备有对应 .NET 诊断工具时，另开采集终端，附着现有 PID（不要用诊断工具启动新业务进程）：

   ```powershell
   dotnet-counters collect --process-id 1234 --refresh-interval 1 --counters System.Runtime --format csv --output D:\audits\sm-runtime-counters
   ```

   按实际工具版本确认可用指标。采集器与目标应用使用同一运行账户。读取分配/GC暂停/堆大小，不执行强制 GC；开始、结束和诊断开销单独记录。
3. **挂起和崩溃**：使用已获许可的 ProcDump 按 PID 采集。示例为每个实验段最多 3 份完整转储：

   ```powershell
   procdump64.exe -ma -h -n 3 1234 D:\audits\sm-dumps
   ```

   `-h` 是 Windows 窗口至少 5 秒不响应的挂起定义，并不表示业务停滞检测。另一次专门的崩溃实验可使用 `-e` 监听未处理异常；不要使用杀进程的 `-k`，不要覆盖原转储。转储采集会扰动进程与磁盘，必须标记为干预，与纯正常连续长稳分开计证。转储可能包含业务数据，应留在指定受保护审查目录。
4. **实际 I/O 延迟、系统事件、电源策略**：使用 Windows 性能监视器/WPR 的短时问题窗口，以及 Windows Application/System 事件日志；开始/结束分别保存 `powercfg /getactivescheme`、`powercfg /query`、网卡驱动及节能设置的只读输出。确认休眠、Windows 更新/重启维护窗口的现场政策；采集器不改策略。

## 判定

所有脚本运行无错误仍不能自动 PASS。至少另具同一源码/介质/SDK/配置/硬件的业务结果守恒、持续独立节拍负载和 UI 实际操作记录，正常段满 24 小时且无进程重启、无法解释的业务停止、漏单/串单/重发，才能评估首道门槛。故障恢复另立实验段。P95/P99、最长无响应、资源增长斜率与容量预算按现场节拍和硬件确定；资源极值不能单独判泄漏。

官方采集工具依据（本轮已查阅）：[dotnet-counters](https://learn.microsoft.com/en/dotnet/core/diagnostics/dotnet-counters)、[ProcDump](https://learn.microsoft.com/en-us/sysinternals/downloads/procdump)。这些工具说明不构成本项目已运行证据。
