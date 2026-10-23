# PR92 六项整改独立复核

2026-10-23。重新从源码、实际事务及进程结果形成判断，随后比对原整改结论。原只读请求、原失败、旧产物及D/RV保持身份；本轮允许继续修复并更新PR，禁止合并或操作生产设备。

## 结论

F01、F02在本次覆盖边界内成立，未复现需要修改业务实现的确定缺陷。F03、F04的实际入口与投影符合整改要求；F05的关键旧验证重新执行；F06仍有一处当前设计入口遗漏，已修正。定向验证扩充至100项断言，并增加真实子进程崩溃证据，补上持续CI覆盖。未更改src/config、数据库迁移、原请求或业务合同。

值得保留：三独立Application、按位队列、外部等待释放共享锁、业务与命令事实共用SQLite事务、跨资源动作先持久准入、原回执资料独立于业务结果、普通投影不改存储原件。没有必要为本轮再引入抽象层或第二套框架。

## 基线与范围

* 执行时远端main：`0c5d7d25e572e80f23522c9ff7b562db920628a3`；待复核PR头：`152596943480166c2399d1f4569841363b0d7c68`。开放PR仅#92，无inline review线程；本轮在原独立分支`codex/architecture-quality-remediation-01`继续，未混入原工作区的四个未跟踪附件。
* 测试/CI及设计入口澄清提交：`fb8a2c405059ce6f523b34340e215e2810307e8a`。本地最终测试在提交前执行，命令回执保留当时BaseCommit及两个测试源文件的SHA256，不能将工作树测试改标为已提交源码测试；随后CI独立检出提交验证。
* 环境：Windows11 x64 10.0.22631、PowerShell7.6.5、Git2.56.0.windows.2、Python3.14.3、.NET SDK10.0.402 / runtime10.0.13。先执行project-environment.ps1，项目缓存、临时根及运行数据均在D盘。未修改HKLM、安装信任或真实设备。
* 阅读：AGENTS、INDEX、工作包、framework/framework-decisions、Application架构22、管理模式、当前整改与验证说明、PR实际差异、ParcelRuntime/Commands/Repository/Stages/Inputs/Guards/Consumers/OperatorProjection/History/RestoreHandoff/ReceiptRecovery、CLI与真实工厂、对应测试及Windows工作流。原报告只作对照，不继承其PASS。
* 全部新命令/时刻/退出码及日志见[本轮证据](../runtime/evidence/pr92-independent-quality-review/README.md)。原审查、整改及新复核分别保存在`D:/audits/importer-pc/AQA01-20261023-1622`、`AQA01-remediation-20261023`、`AQA01-pr92-independent`，前两目录没有改写。

## 六项复核

|项|实际证据与判断|限制|
|---|---|---|
|F01 输入时序|EnqueueCore固定ReceivedAt；ObserveAsync先做来源/时间/序号门禁，既有身份按冻结规则处理，新实体排队过期写InputObserved。DisplayedOperation及实际SupplyStationViewModel抑制上一终结格口。双位、过期、重启、健康失效、旧注册及新增高序号负例均通过|依赖驱动如实提供设备、epoch、序号和时间；不可将软件观察当成物理实体传感证明|
|F02 命令一致性|CommandAsync先PriorCommand再CAS；Commit/Archive同事务写事实和receipt；跨资源AdmitCommand先落Pending。新增真实ArchiveHistory回滚、查询准入/响应持久后的进程崩溃、并发同ID及撤权验证通过|Pending是未完成的命令责任；Persisted可仅证明继续准入，不能替代外部接受。ConfirmRestore跨文件窗口本轮仅静态复核，未执行受保护恢复接管|
|F03 离线引用|ValidateConsumerConfiguration识别与实际工厂一致的manual/tcp-line；真实TCP根、Effective/Pending/Frozen及完整备份成功，未知驱动仍拒绝|未操作机级扩展Store安装/卸载；协议专用参数校验仍由驱动启动负责，离线识别不等于设备可用|
|F04 参数遮蔽|OperatorSnapshot的Inputs.ReaderBinding复用OperatorReader；普通Snapshot/Operation/History均遮蔽，管理退出/到期/改密码后重验；授权读原件前后字节相同|不将管理模式当操作员身份；本轮没有重做原生窗口人工验收|
|F05 验证可靠性|原架构、资源、原raw及输入回归重新执行；真实管理授权、旧断言、停止所有权及明确调用前拒绝保留。冻结组合记录独立重验三项|参考HTTP/TCP只证明参考协议。重复测试不能替代Windows现场和厂商SDK|
|F06 当前指引|运行指引/打包清单可达，历史原件哈希正确；另发现设计入口仍称历史版本/SQL范围为当前，按下表修正|仅整理相关冲突，未将所有历史文档改写为最新版本|

F01调用链：设备注册回调（事件）→EnqueueCore（有界队列及接收时间）→ProcessIngressAsync（本位串行）→ObserveAsync（源/实体/时效/业务守卫）→RecordInput或Save（SQLite事务）→PositionSnapshots/OperatorSnapshot（查询）→SupplyStationViewModel。WCS调用经AdvanceAsync/ExecuteStageAsync，外部等待释放gate；不是UI发硬件命令。

F02调用链：业务命令→PriorCommand（ID/完整内容hash）→CAS/业务守卫→本地Commit/Archive，或AdmitCommand→文件/Query→CompleteCommand。Query经BeginReceipt→原报文调用→StoreRaw→T3；原Awaiting资料责任与新Query是不同发生。

## 本轮发现与处理

|编号/类别|依据、精确位置与触发|防护、后果及处理|
|---|---|---|
|PR92-R01 / P3 确定文档缺陷|F06要求当前指引清晰；docs/design/application-architecture-22.md的顶部版本裁决及“配置与兼容”迁移段，仍将1.4.0/Contracts1.10和SQL1～6写作当前|现有运行指南已正确，但设计入口会误导后续扩展/升级。已明确历史版本身份及当前DB8/SQL1～8；[原检出字节和哈希](../archive/pre-aqa01-pr92-review/README.md)保留，架构裁决及旧验收不倒写|
|PR92-E01 / 重要证据缺口|.github/workflows/sportmaster-commercial.yml原frozen-sportmaster成功，但未直接执行ArchitectureQualityRemediation；F01负例也未证明拒绝高序号后仍能收正常下一件，F02崩溃主要覆盖本地/config路径|旧CI不能证明这些边界持续回归。新增独立aqa01-boundaries作业，锁定依赖构建并运行完整定向套件、保留失败日志；新增测试使用真实Runtime/SQLite/管理服务、现有端口包装及原有故障边界，无生产旁路|

## F01/F02新增运行证据

* 9000/9001/9002序号分别带未来时间、已陈旧时间、缺失事件ID：实际入队后拒绝，原输入不变；错误epoch/device在入队前被围栏拒绝。随后真实reader的下一普通序号成功取得本件格口，证明未污染有效序号。
* 实际ArchiveHistory在BeforeArchiveCommit抛出受控IOException：归档、命令回执、revision一起回滚；同一原命令随后只提交一次，再重放精确返回已存回执。
* 子进程在真实原响应文件Flush后退出77，隔离夹具移除该合成文件模拟原件丢失；用同一原请求走ReconcileReceipt。在命令准入后、Query响应持久后分别再次退出77。重启同ID仍是原Pending，异内容拒绝；Query发生数分别为0/1，原Submit Attempts=1，冻结请求逐字一致，旧Awaiting继续存在。
* Query raw持久后崩溃：启动仅本地应用该Query至Committed/SlotResolved；不会暗中替尚未完成的原发生检查创建结果依据。原命令仍Pending、旧raw仍缺失，需独立检查而非盲重发。这是保守恢复边界，不是原件已清偿。
* Query途中再次提交同ID得到Pending；改变Owner拒绝，不出现第二次Query；仍在途时CloseInterruptedCommand拒绝。正常完成后得到OutcomeKnownRawMissing，原ID精确返回终结回执。
* Query持有等待时实际ExitManagementMode取消：结果Unresolved、重放稳定、无结果依据、原Awaiting及请求保留，未重发业务请求。测试等待有限且finally释放，不靠吞异常取得PASS。

既有六个崩溃边界、UntilDispatch重量过期拒绝、双位投影/重启、管理投影、TCP/备份负例一起执行；总计100个PASS断言及COMPLETE标记，完整日志不以摘要替代。

## 验证矩阵

|命令记录|本轮结果/退出码|证明范围|
|---|---|---|
|build-boundaries-final|PASS/0，零警告/错误|新增测试及实际引用构建|
|boundaries-final / --architecture-quality-remediation|PASS/0，100个断言|上述F01/F02边界及原定向场景；Windows本机合成、真实SQLite/loopback|
|receipt-custody / --receipt-custody|PASS/0|Monitor竞争不误发Query、缺raw保留、完整恢复集本地T3、原图释放与再归档|
|ux-inputs / --ux-commercial-inputs|PASS/0|旧实体/未知责任/设备替换与Revision重绑、输入与作业原子性|
|application-architecture / --application-architecture|PASS/0，63个断言及COMPLETE|三独立Application、双位/可选能力/资源与恢复旧断言|
|resource-risks / --resource-risks|PASS/0，13项|停止超时所有权、设备租约、激活失败保旧|
|compatibility|PASS/0，17项|记录/实际事件/原件/身份和参考资格不能重标|
|release-documents-final / release-documents-report|PASS/0，各2项|随包文档链接、缺失/越界拒绝；新增报告链接后再次通过|
|evidence-integrity|PASS/0|原118份证据、7份检出原件及7份main Git blob哈希|
|media-integrity|PASS/0|原四份ZIP及1045个清单文件逐一核哈希；不重建或改标旧介质|
|combination-*三项|PASS/0|原三份实际解包流程记录再次独立验证；不是本轮重新执行流程|

构建/测试命令和各次退出码全部保存，包括中间构建与首轮定向运行。新证据采集采用独立commands目录，未发生原整改中同名索引覆盖。真实SDK、客户协议、生产设备/现场仍BLOCKED；本轮本机原生窗口、安装/UAC、受保护Store变更和恢复接管NOT_RUN，不能由Linux定向或Windows CI的合成验收替代。

## CI身份与下一步

复核开始时，[run39944822728](https://github.com/yuan2go/importer-pc/actions/runs/39944822728)的frozen-sportmaster全部成功，对应PR头1525969与main0c5d7d2的GitHub测试合并提交`305e2738930256f37e483c9a4134c1b90ad8e987`。日志中的SourceCommit/安装器Payload保持该合并身份；它不是main已合并证据。Combined Status返回空statuses，不当成成功；以Actions实际job/step结论核对。

复核提交fb8a2c4的[run39951602203](https://github.com/yuan2go/importer-pc/actions/runs/39951602203)两个作业均SUCCESS：aqa01-boundaries在Ubuntu实际完成100个定向断言；frozen-sportmaster在Windows2025完成保护目录/管理员边界、首次配置/参数、原生旅程与独立Production拒绝、视觉交互、同源安装器打包和证据保留。Windows检出/发布身份为GitHub测试合并提交`d58d878849d599f3563dd0b1363bd917c5ea0087`，PR仍OPEN且未合并。状态JSON及有范围说明的日志选摘保存在本轮证据；不把组合记录或CI介质改标为本机原四份ZIP。

本报告及证据后续提交只修改docs，不改变上述执行代码；最终PR头的检查仍须按其自身CI状态核对，不借用父提交的绿灯。Linux新增作业不绕过Windows保护边界、不触发现场设备，只使用GitHub托管runner的合成工作区。

现有改进优先级：先等待本PR所有检查通过并审阅本报告，再按原发布流程决定后续，不自动合并。真实协议/SDK/现场及受保护恢复接管须具备独立条件后验收；不为补齐报告虚构接口或扩大本轮UI范围。Pending命令清单上限32及查询成本属于后续容量评估，当前没有长周期现场基准，不将其列为已证实业务失败。
