# Windows 11 24×7 稳定性独立审查

任务：`WP-IPC-WIN11-24X7-STABILITY-AUDIT-01`。

- [原报告 PDF](WP-IPC-WIN11-24X7-STABILITY-AUDIT-01.zh-CN.pdf) · [完整文本](audit-report.zh-CN.txt)
- [原始证据与脚本 ZIP](WP-IPC-WIN11-24X7-STABILITY-AUDIT-01-evidence.zip) · [机器可读摘要](audit-summary.json)
- [下一阶段 Codex 提示词](Codex-next-phase.md) · [固定源码引用](report-source-references.json)
- [Windows 外部采集说明](verification/README-collector.md) · [独立双TCP/RCS负载说明](verification/LOOPBACK-README.md)
- [全部原证据阅读说明](START-HERE.zh-CN.txt) · [本次发布文件清单](publication-manifest.json)

## 原报告身份与授权

用户于2026-10-24明确要求将上一轮分析产物提交到本仓库main，并根据最新代码、文档提供后续Codex修复提示。本次发布仅增加原报告、原证据、独立验证脚本及交接入口，不实施业务修复，不改变生产配置、既有测试、当前设计和历史验收结论，不发布安装包或操作设备。

原审查源码：`c91559f6f981a9f2c9f89bd5afc271089f0e9d8e`；tree：`670e37021dd526dd1045efebc14e8a1ab4e82569`。原审查中的“只读、未提交”描述形成时点；本次用户后续授权公开归档，不改写该历史事实。

|产物|原字节大小|SHA256|
|---|---:|---|
|PDF，18页|285329|`c865e4082f7e4c8d295296e611aba1c5805e2fb63df5a5f2700a170dd2799190`|
|证据ZIP，86项|698771|`a2ee7262d5788625c86c3cee34de06a5aeda1ad3fc931e043c5cc9e9cd187a0a`|

PDF和ZIP原字节不变；单列的文本、摘要、引用与verification文件均与ZIP成员相同。ZIP含实际命令、UTC起止、退出码、失败记录、程序集比对、运行时/SDK替身探针和SQL实验原始结果；不是只上传结论摘要。合成数据不冒充现场数据，未包含凭据、私密访问链接、真实业务原库、厂商SDK或独立字体文件；PDF排版字体子集仅嵌入文档。

## 发布时最新 main 与并行工作

2026-10-24（北京时间）重新核对远端main、开放PR、AGENTS/INDEX、framework/decisions、架构整改、恢复、RCS、部署说明及关键路径。main仍为`c91559f6…`，原审查工作区干净；本次使用独立文档工作树。发布前后目标提交及文件身份由实际远端回读核验，不通过修改原报告的SourceCommit伪装重测。

[PR #97](https://github.com/yuan2go/importer-pc/pull/97)在本次交接复核时仍未合并，head为`37b27a9e7f8ef7f90bc9aa6ddf383c542daf5fc5`。已读取其元数据、变更清单、15份生产源码差异与[HIERARCHY-02说明](https://github.com/yuan2go/importer-pc/blob/37b27a9e7f8ef7f90bc9aa6ddf383c542daf5fc5/docs/runtime/task-content-hierarchy-02.md)，该增量属于Desktop呈现层，不代表main已具有该能力。本次不代为合并PR97，也不复用其测试数量宣布长期资格。

|复核结果|对下一步修复的影响|
|---|---|
|PR97调整有效格口优先、空任务隐藏、Unknown/未准入时旧格口隐藏、日志范围与本地时间，并增加全局操作回执及意图/导航代次保护|执行时先核合并状态，继承正确成果，不重做版式或覆盖并行UI修改。|
|PR97未改ParcelRuntime/存储/驱动/同步授权/最终Dispose/独立监督|R01–R12不能因该UI工作关单；R08/R09/R11/R12仍需实质闭合。|
|AQA01-F01解决的是过期观察投影，已有输入接收时间并不等于持久接收责任|继续补本轮R01；旧包“接收时间仅在运行队列”的范围不能被当成禁止在既有Parcel账追加必要责任和迁移。|
|MaximumOperations限制不可归档责任；终态同库归档已存在|不重建归档，不误报累计10000件必停；重点是磁盘、历史查询/启动成本、编号及维护预算。|
|真实RCS Query=false，Unknown不自动重发；SDK native取消边界未闭合|先明确权威恢复合同与SDK生命周期，缺外部合同不得虚构接口或假装恢复。|
|Windows兼容指南与旧部署文档/高级脚本仍存在build门禁冲突|在业务编码前更新相关当前依据和入口，保留旧版本、介质及验收身份。|

## 结论与证据使用

当前不能宣称已具备Windows11下24×7交付资格：存在已复现代码责任/恢复缺口，且目标IPC、真实驱动/设备/RCS、普通权限安装生命周期及完整24h正常段证据不足。R01为来源接收/内存入队成功但尚未业务Admitted的后续输入无持久去向；R02为当前运行实例没有现有管理恢复入口，不能绝对称必须重启进程。SDK替身取消/帧计数反例不等于真实厂商DLL挂死实测。

原短测环境为Linux .NET10 Release。运行时探针保持实际Runtime/Platform/Storage程序集原字节，但包含Verification程序集及manual/reference、边界异常、运行身份预检替身；真实磁盘/生产启动门禁不在其覆盖范围。当前同tree CI的SourceCommit是`4396162…`，Windows为Server2025，并非Windows11；production gate为FAIL。旧Windows性能为另一SHA、300笔串行和约24秒空闲样本，不能替代本次长稳。

本次文档发布只做文件哈希、ZIP成员、引用和变更边界检查，没有重新运行业务测试或24h实验；没有把文档提交等同修复完成。原正常/故障、Linux/Windows、替身/真实设备及现场状态各自保留。

## 验证脚本与注意事项

`verification/Collect-Win11Stability.ps1`只附着已运行PID；Linux PowerShell7.6.8解析0错误，Windows原生执行仍NOT_RUN。它记录外部粗UI/进程与有界目录指标，不提供精确Dispatcher、业务队列、GC或实际I/O延迟，结论固定`EVIDENCE_ONLY_NOT_A_PASS`。使用前先按说明做隔离Windows一分钟预检。

`verification/loopback_load.py`只监听127.0.0.1，按独立双位节拍发码并提供合成RCS，配置通过原受支持的隔离装配流程准备。`--duration 86520 --warmup 120`才包含86400秒计划负载；必须在预热结束前两侧就绪，尾部排空/关闭另计。脚本自测不是应用测试，真实SDK/设备另验，`soak_pass`固定`NOT_DETERMINED`。

最终修复须按报告工作包完成状态、故障、恢复、兼容与运维闭环。正常负载完整24h为本项目首道门槛，故障恢复独立计证，按风险继续72h及多次跨日；缺环境/合同如实NOT_RUN，不靠定时重启、强制GC、无限重试、清库或降低业务正确性取得通过。
