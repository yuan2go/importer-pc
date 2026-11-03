# ENGINEERING-CLOSURE-07 实施与交付

当前独立复核：[MAIN-REVIEW-03](../reviews/main-static-review-03.md)，基准main `2579392`（PR #6/#7已合并）。8项指定整改路径静态补齐，E-06/E-10仍有工程缺口；全部运行NOT_RUN，Windows06/真实集成05分别保持NOT_RUN/BLOCKED。后续见工程增量08、参考验证09；下文07/更早记录保留形成时范围。

2026-10-16。执行包：[WP-IPC-ENGINEERING-CLOSURE-07](../work-packages/WP-IPC-ENGINEERING-CLOSURE-07.md)。PR #5已按用户要求合并；执行基准main `6d4063ec54e8167c8710e46befbe66f8b688ef08`。开发分支 `codex/wp-ipc-engineering-closure-07`；代码源提交 `493416f99970d95bb4a4076130de5c28ee33502d`。

**10项工程整改均已实现，运行验证均NOT_RUN。** 用户明确要求不测试，因此没有运行测试、资料验证器、构建/还原/编译、应用/夹具、发布/冒烟或Windows/SDK/WCS操作。源码阅读与静态交叉复查不能证明编译通过、显示适配通过或真实接口可用。实际分项记录见 [checks](engineering-closure-07-checks.json)。

## 实现映射

|问题|本轮实现及实际文件|当前边界|
|---|---|---|
|E-01 重扫旧值|`Supply/Model.cs` 的CurrentInput/PreviousConfirmed；`StationApplication.InvalidateReadInput`在同一实体可修订Read时撤销当前Task/Package/Remaining/Supply；非法输入和守卫/容量拒收也保留新输入及旧证实快照；Desktop当前字段不复用历史值|旧观察/请求/回执保留；已在途/Unknown实体不被新非法读码改写。旧Submit重复回执只对匹配OwnerRevision比较语义|
|E-02 信息与动作|`Desktop/MainWindow.xaml`单窗选择当前通道，固定核对区显示条码、SKU/任务/格口、数量及拆包计数；长码换行；动作区与核对对象绑定；`ReviewFits`按实际滚动范围拒绝不完整核对区的供包/计数动作|扩展详情可收起；另一通道结果持续提示；不足区域明确要求扩大/收起详情。具体Windows尺寸、可读性仍W-01|
|E-03 定向来源核对|`ObservationReviewEvidence`带Code/Scope/Source/Epoch/EvidenceVersion，纳入原Command的Id/ExpectedRevision/幂等回执；每次拒收推进版本；只移除选中的精确版本；UI保留未变化的选区|旧无版本ReviewObservationsAsync签名明确拒绝变更；不清Unknown/过载/外部预留或实体责任|
|E-04 安全维护|`Supply/Maintenance.cs`分开只读Inspect、原格式一致备份及显式UpgradeVerified；`Product/Program.cs`维护动作不构造迁移Store；先保存备份SHA与程序/配置身份回执，再事务升级；`station.ps1`统一显式Release，删除无消费者指针协议|旧1/2启动拒绝并要求显式升级；恢复只迁移隔离副本；源备份保留。Windows脚本/SQLite运行未执行|
|E-05 健康与代次|`Platform/DeviceCapabilities.cs`区分状态时间/可靠确认/期限/来源；`StationClosure.cs`注册代次CAS、拒绝旧代次/来源/未来/非更新确认，守卫与报警用IsValid；`ObservationSources.cs`显式模拟心跳；Product/工程页提供受维护身份约束的重连|模拟1秒确认、5秒期限不是生产TTL；真实TTL未知继续不准入；重连不重新授权旧实体；投影读取不续期|
|E-06 历史闭环|`Supply/HistoryQueries.cs`会话/作业明确字段、状态、通道、日期查询与限定页；旧箱含空箱可发现验箱/关闭链及全部作业；`Desktop/HistoryPresentation.cs`完整日期和时区；查询/详情代次、加载/空/失败、选中与阅读快照分开|翻页重用返回的rowid/身份边界排除后插入父记录；既有记录/子事实和保留删除仍是实时变化，不声明不可变数据库快照|
|E-07 常驻信息|工位/身份有效性、CAM0/CAM1/WCS可靠确认与报警入口常驻；全局原因单独滚动；技术投影信息/来源/最近记录进入低频展开；非拆包输入隐藏|健康与报警不再被长原因滚走。真实WCS字段与报警合同仍B-02|
|E-08 提示事件|Supply在已提交业务转换后生成带序号的64容量提示队列；主生命周期窗口单消费者播放并在间隔后确认队首；副窗不消费；失败保留队首，满队列明确故障/计数并阻断新业务|提示覆盖接受/拒绝及进入需拆包状态；计数/清空以可视反馈为主，不每扫一件重复播放拆包提示。重启不回放全部历史成功；API播放请求不证明现场听到声音，W-04仍NOT_RUN|
|E-09 资源/异常|Platform逐模块汇总停止/释放；Product跟踪原停止/释放任务和未终结来源/worker，未安全结束不释放依赖与租约；Application独立发出关闭信号；构造恢复完成后才启动dispatcher；桌面及CLI同时保留操作/启动与清理失败|旧失败不被新清理异常覆盖；超时不等于取消。H04拒收marker仍同步持久化，慢磁盘/真实SDK终结能力待验|
|E-10 账户/租约|`MachineRuntimePolicy.cs`在Windows强制维护方拥有的机级策略、固定运行SID、ProgramData机级租约和受限ACL；运行/离线维护都检查；`provision-runtime.ps1`提供首次显式安装入口|首版支持单个指定账户跨登录会话，其他账户拒绝；缺策略/ACL不符拒绝启动业务；账户/ACL真实行为W-05未验，不宣称设备端控制权|

共享模型、来源契约和format3必须一起演进，故切片1/2/3/5的公共基线以一个原子核心提交落地，随后分别提交桌面、部署及静态复查修正；避免中间提交让新字段由旧读者接纳。任务切片和E编号保持不变。

## 版本、兼容与新入口

发布脚本中的**待构建元数据**为ReleaseVersion `ENGINEERING-CLOSURE-07`、StorageVersion `3`、ContractVersion `SIMULATION-v1`；不是已有新二进制。当前没有本包ZIP、包SHA或Windows截图，不得把ALIGNMENT-01包标成07包。

schema1及id2 SQL/checksum定义保留，新增id3只将storage_format更新到3/3。新输入/健康字段需要新读者；旧程序继续因未知迁移拒绝。新程序打开既有format1/2前只读核验并明确拒绝隐式迁移；新空数据根初始化3，已授权恢复只升级新根副本并保持隔离。原请求SIMULATION-v1报文不改写。

下面是将来明确获准构建/运行后的操作入口，**本轮均未执行**。应使用07的新脚本，不从旧发布目录运行旧升级脚本：

```powershell
# Windows首次安装：提升的维护会话，显式选择实际存在的运行账户
pwsh ./provision-runtime.ps1 -RuntimeAccount 'MACHINE\station-user'
# 之后日常运行、只读检查和备份使用该指定账户；地址/路径由部署者提供
./station.ps1 -Action Preflight -Release 'C:\releases\closure07' -DataRoot 'D:\station-data'
./station.ps1 -Action Backup -Release 'C:\releases\closure07' -DataRoot 'D:\station-data' -File 'D:\backups\original-format.db'
# 新脚本处理旧库：旧Release、选定新NextRelease、同一数据根、新备份/回执
./station.ps1 -Action Upgrade -Release 'C:\releases\previous' -NextRelease 'C:\releases\closure07' -DataRoot 'D:\station-data' -File 'D:\backups\pre07.db' -Receipt 'D:\backups\pre07.upgrade.json'
./station.ps1 -Action Run -Release 'C:\releases\closure07' -DataRoot 'D:\station-data'
# 回退要求原回执绑定的旧发布/配置、原格式备份和完全新的数据根
./station.ps1 -Action Rollback -NextRelease 'C:\releases\previous' -File 'D:\backups\pre07.db' -Receipt 'D:\backups\pre07.upgrade.json' -DataRoot 'D:\rollback-new'
```

安装脚本拒绝覆盖已有策略/非空租约目录，不删租约或接管现场设备。变更指定账户或修复已有部署ACL需另行受控维护，不允许普通用户改为自己的租约根。日常无需管理员；管理员只用于首次策略/ACL安装。以上身份是语法示例，不代表已有获批现场账户。

升级前有未决请求、活动箱、占位未清或恢复隔离则拒绝；备份可以在原格式保存证据但不解除这些阻断。写入前保存的升级回执绑定备份SHA、旧/新EXE及程序集、发布清单和配置哈希；失败后保留源库/备份/回执，先核对实际格式，不能盲重试或宣称回滚已经发生。普通Run始终显式选择Release，不读取active-release.txt。

## 不测试与待验记录

- 本包所有运行检查：**NOT_RUN（用户明确要求）**。静态复查已修正交叉衔接问题，但没有编译/解析器结果，不报PASS。
- 现有测试源码/断言未删除、未执行、未改成宽松通过。已识别的过期预期包括`AlignmentDataVerification.LegacyMigration`的构造时隐式迁移，以及`HardeningVerification`的两条迁移/2:2格式断言；恢复验证授权后应按显式迁移/三条历史记录/3:3及原checksum重新对齐，不能拿旧预期失败解释为环境缺Windows，也不能直接去掉历史兼容断言。模拟旧健康辅助调用现在有明确有限期限，后续长时场景须提供真实合成确认事件。
- W-01～06均NOT_RUN，沿用 [Windows06](../work-packages/WP-IPC-WINDOWS-VALIDATION-06.md)，最终必须用07实际新包绑定SourceCommit/SHA重新留证，不能复用旧包截图。
- B-01～06继续BLOCKED，见 [集成边界05](../work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md)。没有取得真实SDK/合同、生产数量/实体/权限批准或旧现场工程，Production继续NotReady，未控制设备。
- 历史RV-01～17仍OPEN；D批准、历史issues及ALIGNMENT/H04验收记录不倒写。此次“工程已实现”不等于旧HTML修复、现场迁移或生产验收。

交付是开发分支源码及main PR，不创建新发布资产、不自行合并本次实施PR。已创建 [main PR #6](https://github.com/yuan2go/importer-pc/pull/6)，创建时HEAD为`5e27df93aca167d09d6ab7735299713795248a05`；本次实施PR保持OPEN，后续文档回执提交仍在同一分支。代码源提交仍为`493416f`；当前最终HEAD以GitHub PR和最终答复核验，不把本文件自引用为自身提交。
