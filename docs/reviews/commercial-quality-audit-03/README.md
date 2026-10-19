# WP-IPC-COMMERCIAL-QUALITY-AUDIT-03 · 原报告发布与下一阶段交接

[完整审查报告PDF](WP-IPC-COMMERCIAL-QUALITY-AUDIT-03.pdf) · [原始审查证据ZIP](WP-IPC-COMMERCIAL-QUALITY-AUDIT-03-evidence.zip) · [Codex执行提示词](Codex-next-phase.md)

## 报告身份与本次授权

本目录按用户2026-10-19后续授权，将已交付的原审查报告及证据发布到main，并结合发布时最新代码、文档和开放PR编制下一阶段提示词。原审查为只读；此次文档发布不改变其形成时范围，也不表示其中问题已修复或产品通过商用验收。

|原件|身份|
|---|---|
|PDF|31页，375075字节；SHA-256 `96f54f86fe06b8565686285e5e83a7860497e4da2e7bd84eb7edf6112ed0472b`|
|证据ZIP|425310字节，67个文件；SHA-256 `b5507a2f4d8721908c9c3e2b8ff7e1904ec83546df2c861e64e0c55c6bb77e85`|
|固定源码|main `ec698bd4e11d06c13c097c77f90cc5ce145ab3ea`，审查冻结于2026-10-19 01:30:38 UTC（09:30:38 +08:00）|
|验证范围|静态代码/文档/发布物核验、Linux隔离构建及定向参考验证；Windows实机、真实设备/协议和现场验收未由本次审查完成|

PDF和ZIP保持原字节。ZIP内包含报告生成源、102处固定源码索引和摘录、原运行日志、合成反例及环境设置失败；66个内部文件hash和ZIP CRC已逐项复核。绝对路径属于隔离审查环境；不含生产数据、凭据、私钥、厂商SDK或独立字体资产。历史报告01缺失仍按原记录保留，不能用报告03替代。

## 发布时最新main与PR #34

2026-10-19发布准备阶段重新抓取远端main，仍为上述SHA；原受审工作树干净。此次使用独立文档工作树，新增报告、证据、交接及索引/清单，不修改业务源码、产品配置、既有测试或现行设计。

发现开放[PR #34](https://github.com/yuan2go/importer-pc/pull/34)，读取head为 `c07304418aa68428331ba141418edfb3519a6939`，base为上述main。它的[工作包](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/docs/work-packages/WP-IPC-COMMERCIAL-QUALITY-FIX-03.md)使用CQ03-01～04编号，与本报告F01～F05不能按数字对应。本次不合并该PR。下一阶段执行时必须重新读取main、PR状态及后续提交。

|本报告项目|开放PR中的实际覆盖|下一阶段处理|
|---|---|---|
|F01 / P1，等待重量时既有实体矛盾被准入守卫掩盖|未修改[ObserveAsync的处理顺序](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L218-L234)。PR的“当前件身份”是UI命令目标修复。|原问题保留，修复有效来源、既有实体一致性与新件准入之间的顺序及隔离。|
|F02 / P2，单秤测量身份冲突拖停独立位|测量处理及仓储抛异常路径未改；见[MeasureAsync](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L258-L276)。|原双独立秤反例对应路径仍在，按影响范围隔离，并保留真正共享存储故障的保护。|
|F03 / P2，RCS双位异步T1与编号高水位冲突|[准备与高水位检查](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L306-L325)未改；RCS版本升级不代表编号算法已修正。|实现可持久证明的唯一编号归属，保持旧原请求、范围和双位独立。|
|F04 / P2，classic快照失败后保留旧Ready|CQ03-02增加[共享快照有效性](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/src/Ipc.Application.ParcelFlow.Desktop/ParcelSnapshotValidity.cs#L5-L51)及两布局动作保护；CQ03-01另修当前位作业身份。|复核并继承，补本报告反例及实际Windows交互；本次静态读取不构成PR全量复审或Windows通过。|
|F05 / P2，当前且随包的升级/ABI说明矛盾|ABI1.7.0、CLI DB6与旧API默认DB5已区分；但[指南第63行](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/docs/runtime/architecture-quality-closure-01.md#L61-L65)仍允许DB4/5保留责任升级，与[第111行](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/docs/runtime/architecture-quality-closure-01.md#L111)及普通旧库启动预检冲突。|部分覆盖，文档先行阶段必须收口；区分普通升级、已证明终态的窄修复和专用原回执恢复路径。|

以上F01～F03判断来自原反例证据与PR代码差异核对，没有对PR HEAD重跑原反例。PR的自有验证结果按其[结果报告](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/docs/runtime/commercial-quality-fix-03-results.md)及固定源码读取，不当作本次独立运行证据。

F05另有未覆盖项：该PR未修改当前且随包的[Windows验收指南](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/docs/runtime/validation/architecture-quality-closure-01-windows.md#L3-L18)，仍要求记录Parcel DB5并“再显式升级DB5”。应修正当前操作目标，保留真正历史候选的旧版本事实。

### 应继承的修复与整合风险

PR新增[终态恢复标记整理](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/src/Ipc.Application.ParcelFlow/ParcelTerminalRecovery.cs#L9-L120)：以原权威终态或受守卫未发送取消为依据，保留修复前JSON/hash及归档原版本。下一阶段修F01/F02时，身份冲突应沿现有独立责任处理，不能塞进一个RecoveryRequired布尔值；终态标记收敛不能自动清偿身份冲突、缺失原回执、原图或原包责任。

该PR业务版本为1.2.5，声明能力包2.1.3、reader1.2.3、RCS1.0.5；main仍是报告中的1.2.4及其原组合。下一阶段以最终整合源码及实际公共DLL重新确定不可变包版本，不重复占用PR已经使用的版本，也不把未合并分支的候选算作main产物。PR尚有版本测试同步意见：其[兼容测试](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/scripts/test_application_compatibility.py#L27-L36)仍断言1.2.4。应按真实当前声明更新受影响断言和夹具，保留原失败；不用删测试或全局放宽精确兼容规则收口。

本次从PR固定Git对象导出该测试所需10个文件，仅执行 `CompatibilityTests.test_business_version_matches_every_declared_composition`：1项测试、1个失败、退出码1，实际为 `AssertionError: '1.2.5' != '1.2.4'`。见[原日志](pr34-compatibility-check.log)与[输入hash/命令记录](pr34-compatibility-check.json)。失败发生在第一个Sportmaster分支；这是过期版本断言，不是三个产品项目与composition的版本实际冲突，也不否定作者其他已运行用例。

该PR的[产物记录](https://github.com/yuan2go/importer-pc/blob/c07304418aa68428331ba141418edfb3519a6939/docs/runtime/evidence/commercial-quality-fix-03/verification.json#L369-L371)包含104176727字节的Sportmaster Windows ZIP，记录SHA-256为 `c36da7b74748236ed3fc619035a9029fd98e9cdb3253bb466eafbf9869cb60b9`。本次未取得或独立核验这一新ZIP。冻结运行源码 `8eee4c61082e1c7eeda245d17a2202556d233eab` 到PR head仅八项文档/证据变化，不因此判产物过期。DirectPlc/WcsToPlc在该PR没有新的Windows ZIP；Windows原生操作及真实SDK/协议/现场证据仍缺。原审查对1.2.4媒介的字节核验不自动适用于1.2.5候选。

## 工作包依赖与完成标准

详细目标、迁移与反例在原PDF第11节。以下保留同一编号，并按用户本次要求落实完整工业工程；方案大小由具体需求、失效后果和维护成本决定。

|工作包|必须交付的结果|依赖与必要验证|
|---|---|---|
|WP-A03-00|先详细读文档，整理相关现行设计/运行/升级入口，关闭F05；明确首个交付组合及待确认项。|业务代码前独立提交文档整理；核实际命令、版本、支持范围与随包内容，保留原要求和历史原件。|
|WP-A03-01|F01/F02从输入、持久冲突、阻断/隔离、界面提示、授权核对到重启恢复完整闭环。|在00后；两PLC业务矛盾读码后的未发动作保持零发送，双秤局部冲突不拖停另一独立位。|
|WP-A03-02|RCS编号持久唯一，实际T1顺序与双位异步业务兼容。|在00后，与01协调共享仓储和迁移；逆序T1、重启、范围耗尽和旧请求不变。|
|WP-A03-03|两布局共享正确的当前件身份与快照时效，操作员能辨认失效并安全维护。|在00后复核PR34已有实现；失败/挂起/迟到/实例变化、CAS和实际Windows交互。|
|WP-A03-04|一个明确真实设备/SDK/协议组合的完整接入和故障处置依据。|动作路径依赖01，涉及RCS反序组合依赖02；真实实体、同件重量、协议接受/物理完成和Unknown分别验。|
|WP-A03-05|所选Application的实际Windows媒介及安装、配置、维护、升级/回滚/修复/卸载闭环。|依赖00/03，并与04汇合；普通账户、权限/独占、不同cwd、升级中断及恢复按同一媒介hash实测。|
|WP-A03-06|确认负载、离线/容量、故障恢复、诊断、备份、支持和许可交接；按组合给准入结论。|依赖适用的01～05；时长和阈值按现场目标及风险确定，不凭正常扫码样本宣布长期稳定。|

00后可按职责并行推进01/02/03，并同步准备真实组合与Windows环境；01/02共享接收、仓储或SQL变更时应先明确契约再整合。工业工程完整性不能用增加框架、文件或测试数量替代；继续保留三个独立Application、原请求冻结、单一配置账、事务/可靠发送、受控扩展和资源/原件保护。

缺少Windows、SDK、设备或真实合同，不阻止完成其余实现、发布候选和交接资料；缺失层级继续NOT_RUN/BLOCKED。受影响组合的错控、丢件责任、重复副作用或无法恢复关键风险未关闭时，不得以“有条件通过”放行。Sportmaster不新增展图/长期图库，DirectPlc全量UI的已批准延期与其指定商用场景所需操作界面分别判断。

## 发布验证范围

本次原件字节、ZIP内部hash/CRC、公开来源与实际文件内容已核对。`python3 tools/verify_archive.py --root . --scope public` 在基线与加入本次资料后各执行一次：文件数3013→3019，错误均为267项，错误集合相同，新增及移除错误均为0；整体仍为FAIL。保留原设计资产错误与公开范围规则，没有增豁免、改校验器或改旧资产。最终文档字节及manifest另按修改文件逐项核对。此次仅重跑上述一项PR版本测试，没有重跑业务构建或全库测试；历史审查的参考PASS继续限于其原执行范围。

旧INDEX和manifest的原字节可从固定main取得；本次不重复复制整套历史归档。后续文档清理应修正当前正文与导航，并保留可追溯历史入口，不持续追加互相矛盾的“最新说明”。
