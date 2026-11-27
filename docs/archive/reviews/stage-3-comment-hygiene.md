# Stage 3 注释清理与多业务解耦准备报告

基线为 Stage 2 提交 `0bf67b21`，工作分支为 `codex/stage3-extraction-audit`。本阶段交付 66 份源文件的注释修改、活跃文档索引和两份报告。[多业务架构解耦摸底报告](../../design/multi-business-extraction-audit.md) · [Stage 2 回归记录](stage-2-test-hygiene.md)

## 清理范围

逐文件扫描 Git 跟踪的 533 份 C# 和 24 份 XAML，434 份文件含注释。实际改动 61 份 C#、5 份 XAML。下表按连续的原注释块计数，行尾注释单独计一块，每块只归一类。

| 处理 | 块数 | 具体变化 |
| --- | ---: | --- |
| 纠正失实或容易误读的职责说明 | 14 | 区分软件解码与 MVID 原生识别、SDK 观察身份与 Application 发送决策、逻辑读头与物理相机身份、健康轮询与远端确认。收窄首配、工作卡、解码耗时、Supply 装配及视觉资源说明的适用范围 |
| 删除重复或显而易见的注释 | 41 | 删除复述布局、按钮、颜色、字段和单行调用的说明 |
| 去掉工单、授权及仓库规则引用 | 5 | 在重载落盘、手工输入、TCP 分帧和作业文案旁留下局部契约 |
| 精炼仍有价值的实现原因 | 30 | 保留 MVID 借用缓冲、GigE 断流后的释放顺序、WAL 副本、CAS 草稿归属、进程锁身份和并发收口顺序 |
| **合计** | **90** | 以删除或改写计数。词法扫描记录从 2,195 条降至 2,085 条，跨行改写会影响这一数字 |

核对了会影响现场判断的数值和入口。当前默认提示音为 80%，常规心跳为 500 ms，紧凑探测间隔为 250 ms。运动门控默认 150 ms，MVID 可先取预览帧，满足条件才取带识码结果的帧。运行空盘默认 300 ms 并要求至少三帧，显式校准单独要求 800 ms。管理工作区外可在单工位空闲条件下校准。上述值以实现为准，注释只解释职责和例外。本轮未发现需要删除的废弃 TODO 或成段注释掉的旧实现。[提示音](../../../src/Ipc.Platform/PresentationPreferences.cs) · [心跳配置](../../../src/Ipc.Application.Contracts/RcsRequestPolicy.cs) · [紧凑探测](../../../src/Ipc.Integrations.SportmasterRcs/RcsHeartbeat.cs) · [采集门控](../../../src/Ipc.Drivers.Hikrobot.Mvs/MvsAcquisition.cs) · [在位与校准](../../../src/Ipc.Drivers.Hikrobot.Mvs/MvsTrayPresence.cs) · [校准命令](../../../src/Ipc.Application.ParcelFlow/ParcelCommands.cs)

## 解耦结论

- 六个指定工程共 109 个文件、376 处精确字面量。其中 337 处是直接相等或不等比较，12 处是常量模式，27 处用于默认参数、调用、标识或集合等。两套 ParcelFlow 工程合计 74 个文件、264 处。解耦地图逐文件列出职责与成员，也区分了存量业务 ID 和可迁移的判断。
- 业务决策可沿拓扑、轮次、协议、参数、工作区、部署、历史与恢复七个边界归位。DirectPlc 可检验最小流程，WcsToPlc 可检验多阶段回执。Supply 有独立账本，Inspection 只接受模拟模式，两者各自保留现行边界。
- 建议分四个 PR 依次迁移字段与视图、装配与部署声明、轮次与协议决策、历史与恢复。每次保留共享事务中的 CAS、原请求冻结、Unknown 发送围栏、DB14 身份和当前部署所有权检查。各步对应的既有回归列在[解耦地图](../../design/multi-business-extraction-audit.md)。

## 本机复验

在独立的 Windows 工作树中执行 `. ./scripts/project-environment.ps1`，使用 .NET SDK 10.0.402、Sportmaster 装配和锁定还原。三个目标均从本阶段的最终源码构建。

| 检查 | 结果 |
| --- | --- |
| C# 及 XAML 等价性 | 对照 Stage 2，比对 557 份文件。C# 在源码所用条件编译分支下的语法标记、编译指令和 XML 文档注释一致，XAML 删除注释后的元素、属性与文本一致。另对所有 66 份修改文件剥离注释后逐行比较正文，忽略空行及行末空白，结果相同 |
| `dotnet build src/Ipc.Desktop` | 通过，0 警告、0 错误 |
| `dotnet build tests/Ipc.Core.Tests` | 通过，0 警告、0 错误 |
| `dotnet build tests/Ipc.Operations.Verification` | 通过，0 警告、0 错误 |
| `dotnet test tests/Ipc.Core.Tests` | 最终全量 95 通过、0 失败、0 跳过，TRX 运行时间 8 分 0 秒 |
| `python scripts/verify-industrial-docs.py` | 通过。72 份索引文件、69 页 Markdown、456 条本地链接、47 个交付源文件、5 个 CP 故障码 |
| 远端 CI / 真机相机、PDA、工控机 / 客户 RCS 联调 | 未运行 / 未运行 / 未运行 |

首次全量运行 94 通过、1 失败、0 跳过。失败的 `--state-journal` 先读取修订 26，随后提交软件暂停，合成工作站日志记录操作返回 `Conflict`、当前修订 28，断言期望 `Persisted`。该套件随后独立运行通过，最终全量中也通过。两次全量运行均保留 Stage 2 的测试断言和默认并行设置。[失败断言所在套件](../../../tests/Ipc.Application.Verification/StateJournalChecks.cs) · [命令修订守卫](../../../src/Ipc.Application.ParcelFlow/ParcelCommands.cs)
