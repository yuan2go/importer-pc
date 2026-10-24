# HIERARCHY-02 深度复核证据

本轮继续 [PR97](https://github.com/yuan2go/importer-pc/pull/97)，读取执行时 main `c5ac51d`，原PR head `37b27a9`，无其他开放PR或待处理行内评论。生产修改 `cf2ab078120242e0f5c50c08525381fabc812d4b`，最终验证源码 `de8bcb6` 仅补既有夹具的登录刷新同步；原版反证源码 `3086b37e47c2d86d71bbc01627154f08e96d780a`（只增加独立夹具，生产代码保持原PR）。运行身份及文件哈希见 [RESULT.json](RESULT.json)、[FILES.json](FILES.json)。文档补交不扩大该源码的验证范围。

## 方法与证据身份

预设假设见[工作包](../../../../work-packages/WP-IPC-TASK-CONTENT-HIERARCHY-02.md)。依据为有效 [RCS合同](../../../../design/sportmaster-rcs-task.md)、V2作业/58字段/39动作、V3参数及管理模式；先裁决文档再编码。Impeccable采用WPF Assessment A/Operate，未开启网页检测替代原生验收。V2全部67张原始界面通过图集实际查看，原型源码/交互场景已读；浏览器拒绝本地file入口，未绕过，动态HTML原型本轮未运行。

所有实体、条码、端点与回包均为**合成场景**。现有manual读码适配器输入具有device/epoch/event/admission身份的观察，真实Sportmaster Application执行准入、持久化和命令守卫，现有RCS适配器发送冻结报文到独立loopback HTTP对端。预期L-07/R-09、L-08、L-09/R-10和各状态在夹具中按合同预先书写；对端只从请求读取关联requestId，不调用被测解析器/VM生成答案。Conflict仅在端口返回边界注入错误请求身份，随后由真实Application拒绝；不是RCS查询能力证明。

`chain-*.json`保存各阶段实际任务事实摘录、冻结HTTP正文/哈希、原回包、命令回执和显示值；通过admissionId、operationId、requestId、payload/body hash关联，不靠时间邻近。未拼接Application快照。公开摘录排除账号配置、会话、DB和无关机器数据；完整隔离运行目录仅留D盘。PNG来自同一窗口的WPF渲染，实际DPI125%，常用1366×768、1092×614及兼容最小800×420 DIP；精确尺寸见日志。

## 简短证据表

| 问题与原始材料 | 原因判断 / 修改决定 | 独立预期 | 实际结果 | 剩余限制 |
|---|---|---|---|---|
| L1取得L-07后观察INVALID；[原版失败日志](before/WINDOWS-WPF.log)与[原版画面](before/chain-invalid-after-slot-800.png) | Application正确清除当前已结束作业，UI仍要求点击禁用详情。改为历史查询指引，按已知筛选结果显示中文原因 | 无新作业/POST，无旧格口；历史原请求不变，右位R-09不变；指向可达入口 | 原版严格断言FAIL；[修改后](after/chain-invalid-after-slot-800.png)通过，同一链路的历史与右位断言通过 | 未测操作员定位时间 |
| 两个独立实体同码000123，原回包分别L-07/R-09 | 关联与布局正确，保留 | 两个operation/request/admission；wave0007、TASK-L1、SKU-L1、quantity0002原样保留，不能借请求quantity1 | [实际画面](after/chain-same-code-normal-1366.png)、冻结原请求和原回包一致；各1次发送，无物理完成声明 | 真实相机实体资格未验 |
| 左000124的回包缺wave/quantity；000127的success=true缺taskId | 旧文档把可选/必需字段混为一谈；修正文档，保留解析守卫 | 前者L-08且仅可选项“回执未包含”；后者Unknown，无格口/重发权限 | [缺可选项](after/chain-missing-fields-800.png)、[缺必需项](after/chain-unknown-800.png)符合独立预期；不支持查询时仍Unknown且POST数不增 | 合成原回包；非真实RCS验收 |
| 延迟左000125，右000124先成功；左000126明确拒绝；右000129注入错误回执身份 | 状态转换正确，主要原因仍为内部英文。翻译已知原因，保留远端原原因与编号 | 等待无格口；Rejected保留SYNTH_CHUTE_CLOSED；Conflict为Blocked且无有效结果；另一位独立 | [等待](after/chain-pending-left-right-success-800.png)、[拒绝](after/chain-rejected-800.png)、[冲突](after/chain-conflict-right-800.png)及真实状态/动作守卫通过 | 冲突属于明确注入的能力端口场景 |
| Unknown后新000128未准入；Snapshot读取故障 | 区分最新观察与原件责任，解释本次未准入及当前未决；失效机制保留 | 新观察不借旧结果，原000127责任不变；读取失效隐藏两位可操作结果 | [未准入](after/chain-blocked-after-unknown-800.png)、[失效](after/chain-stale-800.png)通过 | 物理包裹处置未验 |
| 日志页Pause传输拒绝且快照正常；真实Pause提交后延迟回执；真实Resume被守卫拒绝 | 既有意图/导航代次机制正确，保留；补已知守卫拒绝中文表达 | 正常快照不能掩盖命令失败；切页后晚到Pause可见；新Resume回执取代旧失败且仍暂停 | [拒绝](after/chain-pause-rejected-logs-800.png)、[迟到](after/chain-late-pause-history-800.png)、[恢复受阻](after/chain-resume-guard-logs-800.png)通过；旧夹具继续检查较早回执不能覆盖较新意图 | 受控传输拒绝不是现场网络统计 |
| 参数两类编码、右IP错误与切页；日志范围/本地时间 | 现有共模和快照正确，保留 | TASK与HEARTBEAT分开；错误保留输入，左右独立；改筛选后不得继续声称旧查询有效 | 参数原生回归、键鼠切页和日志查询通过；详见下节 | 参数候选未保存/未连接设备；无使用者比较 |

## 原生键鼠复核与反证

最终原生三组为task-hierarchy 202、参数9、内部页面25项PASS；共享呈现层48项断言通过（日志另有一行COMPLETE）。后者最终夹具源码`13953c3`，生产和原生夹具与`de8bcb6`无差异。锁定构建0警告/0错误，cf2ab07的win-x64发布入口`--environment-report`显式等待退出0且JSON有效；不是安装或受保护服务启动验收。

通过Windows原生窗口、控件树和真实鼠标/键盘，在1092×614 DIP检查：日志输入SYNTH并查询，当前范围明确包含该词；再改UPDATED并在刷新后追加“-2”，焦点和输入保留，范围变为待查询、导出受禁；高级时间入口可展开。返回作业，拒绝回执仍可见。左位“查看详情”进入历史，展开后显示原包裹000127、admission entity-l5，未误指新观察000128。记录时间显示本地+08:00，与原UTC事实是同一瞬间。

冻结de8bcb6 DLL后再次核对日志输入SYNTH、刷新后追加“-2”、查询、改为UPDATED、展开时间、返回作业，确认同样的输入/范围/焦点和回执行为；最终全局拒绝原因已为中文，两位分别为本次000128未准入/原000127未决与000129回执冲突。最终交互链日志为[interactive-chain.log](interactive-chain.log)，二进制哈希见RESULT。

参数窗口以现有共享候选测试：右IP为111时原字段错误可读；键入192.0.2.32并Tab，错误消失；切左仍为192.0.2.11/TASK-LEFT/HEARTBEAT-0；返回右仍为192.0.2.32/TASK-RIGHT/HEARTBEAT-1。本轮没有修改这段已正确实现。该检查是表单候选交互，不冒充运行设备、整窗保存或现场操作者测试。可用下述interactive入口重复。

保留的失败/反证：首次夹具把大写admission ID及缺必需JSON成员当正常输入，被真实准入/解析拒绝，已按有效合同修正输入；首次历史比较误用包含字典的record引用相等，改为序列化值比较，生产代码未为测试改规则。随后源码3086b37稳定复现“指向禁用详情”失败，才修改生产提示。另一次把整站阻断原因误作本位原因的预期失败，核对原PositionSnapshot后改成真实未决原件状态，未降低原件/权限断言。首次publish选错RID锁文件而失败，改用仓库已有win-x64锁文件，未放宽锁定还原。最终定向回归发现旧夹具登录后恰遇登录前的在途刷新，直接解引用空会话而失败；`de8bcb6`先断言真实Application已颁发alice会话，再有界等待画面收敛到同一session，保留严格身份断言，未改变生产轮询。[失败日志](login-refresh-race.log)保留cf2ab07身份。

没有代表性操作员或现场连续观察材料，因此判断正确率、定位时间、误读/求助率、顺序控制实验及节拍阈值均**未测**。本轮只证明排除了可复现误导和软件判断链错误风险，不能宣称效率或现场可用性提升。真实双相机/SDK/RCS、目标工控机与其他DPI、扫码/实体供包、安装升级/UAC/现场验收均NOT_RUN；D/RV与旧证据身份不变。

共享呈现回归还暴露旧probe未转发管理模式服务、仍把已中文化的Feedback断言为英文、在管理状态异步返回前同步阻塞测试dispatcher三项夹具过期问题。改为真实临时管理模式转发、同时断言原始回执与中文反馈、让测试dispatcher继续处理消息，原UI线程/关闭排空/命令归属断言全部保留。原失败分别见presentation-obsolete-probe、presentation-obsolete-english、presentation-blocked-test-dispatcher日志。一次与本轮交互窗口重叠运行触发资源租约拒绝，日志保留；正常关闭自建窗口后串行通过，未删除租约或绕过排他机制。

## 复现与停止范围

CI run112（PR head `9d49983`，实际合并源码 `389d4b7`）在新任务链启动时失败：[原始失败节选](ci112-machine-policy-failure.log)。原生首配、参数壳和原任务层级断言已执行通过；新夹具错误调用生产能力工厂，依赖本机已有的受保护机器租约策略，干净CI拒绝启动。此失败不改写上面的本机PASS，但推翻了旧夹具可在未部署机器独立复现的假设。修正仅在测试工厂：沿用既有SyntheticFactory的manual/登录端口构造边界，显式限制Simulation、manual与loopback，直接实例化原RCS适配器并注入真实Application；仍调用生产配置校验，保留全部47项链路断言。未安装机器策略、修改生产租约检查或拼接快照。最终冻结介质CI状态以PR97当前源码检查为准；旧RESULT及图保留各自源码身份。

```powershell
. ./scripts/project-environment.ps1
dotnet build tests/Ipc.Desktop.UiVerification -c Release -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --task-hierarchy
# 单独真实任务链；加 --interactive 停留供键鼠复核，完成后关闭窗口
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --task-hierarchy-chain --interactive
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --task-hierarchy-parameters --interactive
dotnet tests/Ipc.Desktop.UiVerification/bin/Release/net10.0-windows/Ipc.Desktop.UiVerification.dll . --internal-pages
```

输出目录由`.artifacts/WINDOWS-WPF-EVIDENCE.txt`指示。要复现修改前反证，在独立D盘checkout中检出3086b37并运行`--task-hierarchy-chain`，期望非零退出，失败点为“rejected input directs … reachable history”。不使用baseline开关跳过断言。

既有冻结Windows CI的`-TaskHierarchy`现在同时执行原投影/时序回归和新的真实任务链，保存chain JSON/PNG与失败日志，其他必需门禁保留。已完成关键链、受影响原生与回归后停止扩展；没有新框架、业务规则、配置schema、依赖或安装方式变化，未自动合并/正式发布/安装。
