# RETENTION-INGRESS-02 验证与集成条件

2026-10-25。产品修复固定提交 `a192b7fe18b2f904fd7db9943e79b0173d0f8b28`，基线为 PR103 合并后的 `c425435c6f980ca537a29e4e8353d12be341df61`。本轮修复的是输入分页，不重复 PR102 的 pending-command owner 查询整改。

**已证明输入分页的重复排序增长项被消除；没有取得60.48万完整历史维护通过或工业长期运行资格。** 真实受保护控制器启动被UAC取消，没有helper PID、没有新保护目录、没有执行大规模维护/恢复。现有9.46GB原失败库与完整备份不改写。代码、测试、合成回归及CI可单独审查，不能把它们标成最大规模现场验收。

实现和操作见[运行说明](../retention-ingress-02.md)，原件清单见[证据](../evidence/retention-ingress-02/README.md)。本机Windows11/4核约16GB及D盘延续PR103环境，不是目标4核8GB工控机。合成节拍、日期和批量数据不计入连续运行时长。

## 实际计划与增长复杂度

使用本轮产品SQLite3.54.0对PR103留存的9,464,315,904字节完整备份执行EXPLAIN。Windows共享读句柄排除写入/替换，锁前后均拒绝非空WAL；只读不可变URI，不改变ACL、不写原库。原查询仍为`parcel_ingress_pending (state=?)` + `USE TEMP B-TREE FOR ORDER BY`；修复后为`INTEGER PRIMARY KEY (rowid>?)`，无临时排序。保留owner和永久退役的两个相关子查询仍按admission/id唯一索引探测。实际SQL逐字继承全部保护谓词，仅对外层表加`NOT INDEXED`。[实际完整备份计划](../evidence/retention-ingress-02/ACTUAL-FULL-BACKUP-PLAN.json)

固定产品的磁盘组件另验证1千/1万/10万/60.48万输入、稀疏rowid、Processed/Inspected交替、Observation与Measurement归属、保留owner、Pending/Interrupted、coalesce优先级、截止相等、近期/null/非法日期，以及0/中点/末端和全分页精确身份oracle。输入state索引保留供其他路径使用，不增加持久索引/迁移。

|组件输入数|原首批VM约数|修复首批VM约数|修复全分页秒|实际输入删除秒|提交次数|
|---:|---:|---:|---:|---:|---:|
|1,000|40,000|9,000|0.00314|0.08743|4|
|10,000|391,000|9,000|0.04173|0.55699|40|
|100,000|3,901,000|9,000|0.33836|4.60690|391|
|604,800|23,589,000|9,000|1.79911|24.94643|2,363|

VM每1000条指令采样；计时不带VM回调。该组件使用真实DB9表/索引、短合成JSON、两个owner和每条一个关联审计，**不是604800个完整作业/原回包的大库**。首批工作量不再随剩余合格输入数增长；全遍历按rowid单向推进。子查询索引查找仍有自身成本，大量保护行仍需扫描。这不是实际工控机重复统计、完整维护RTO或旧7855秒的同条件速度比。

删除仍256输入/事务，WAL/FULL，2363次提交及全批执行后输入/hash/关联审计守恒；没有为计时关闭同步或拉大无界事务。作业仍一作业一事务，原件解析/保护检查/提交成本未用输入组件外推。原首次未提交版组件22.61秒保留其DLL身份，表格采用固定a192b7f的24.95秒结果，不挑最好数据。

## 完整原件、预算和恢复

复用PR103的完整原件生成/核验工具与实际reference种子，在新普通Simulation目录生成1千/1万完整结构。实际维护库、完整备份、六类保护和永久身份验证通过；调用的是显式模拟authority的内部测试入口，`ActualProtectedCli=false`、`ActualElevatedAuthority=false`。耗时5.6681/46.3548秒包含该夹具的维护及随后Verify，不是CLI独立RTO；本轮两档均未做VACUUM，不能与PR103的一万压缩结果直接作速度比。独立复用逐笔对账完成55项检查，包含每个退役body/input hash/state、六个保护owner的原始body、输入、raw BLOB、未决命令/回执、冲突、全部永久准入和回复指纹。[逐笔回执](../evidence/retention-ingress-02/SIMULATED-RECONCILED.json)

新增脚本显式区分模拟授权与受保护CLI回执，不为模拟输入伪造CLI时长或恢复PASS。原对账负向检查再次通过：真实Windows写共享拒绝、非空WAL拒绝、加锁后WAL复检、篡改hash拒绝；旧受保护小样本仅只读复核，未重跑维护。

预算保持两小时，始于CLI入口；Repository入口在hash/初始化之前建立并链接同一deadline。八个阶段注入取消，确认初始化前取消保持原库字节，备份阶段前取消不建备份，后置取消保留完整备份，失败返回前释放owner。实际执行的递归SQL被VM回调中断；64MiB备份在目标已开始收页后取消，源hash不变、目标句柄释放、无COMPLETED。预算覆盖的是命令生命周期，不能强制终止单次阻塞系统I/O、Flush或必要回滚；没有承诺进程一定在7200.000秒退出。

实际SQLite异常注入覆盖输入页中间、提交前、提交后；重开后保留准确已提交前缀、未提交页整体回滚。另以两个独立子进程分别在第二页第一笔删除后和第一批提交后由控制器强杀，仅终止本测试创建的PID。重开均恰有256条永久退役、353条保留（344合格+9保护）及对应审计，恢复只退役剩余344条；再次清理不重复、不复活。它证明进程终止后的SQLite边界，不等同硬件物理断电。原作业提交前后故障、未决请求防重、备份/隔离恢复、版本兼容及升级冲突测试也通过。

## 固定身份与必要检查

产品ParcelFlow DLL SHA256 `C9CDACF9431BD23B173AB49C25081C6176EBC87C4FBB4176E25CBC3FFB2529B2`，src tree `6326499b6a9ecd87ae84e40b8be20a8eb28c3cfe`。首轮固定验证夹具hash及后续强杀/只读计划夹具的源码与DLL hash分别保存；后续测试/文档不改产品src。Release两入口构建0警告0错误；SQL1～9逐段比较与main完全一致。Sportmaster1.12.2、DirectPlc/WcsToPlc1.6.2、Contracts1.17.0/DB9；并行PR104未混入。

固定a192b7f五组定向入口均退出0：`--retention-ingress --large`、`--win11-stability`、`--audit03-maintenance`、`--version-compatibility`、`--upgrade-conflict-closure`。后续测试补强的两次真实强杀及最终只读大库计划/小档取消测试另有回执。新增小档查询、事务与预算测试进入Linux CI；Windows原冻结发布/安装器/原生检查继续执行。最终head CI及实际合成commit在[PR105](https://github.com/yuan2go/importer-pc/pull/105)核验，不借用先前head成功，也不把安装器构建当作本机安装。

保留的非PASS：首次计划断言误要求admission索引covering，实际唯一索引可正确探测，已改为检查真实seek与无临时排序；一次PowerShell参数拼接把定向参数拆开，误入历史默认AC17流程后因管理模式拒绝，五份原日志保留，不计定向执行；强杀夹具首次编译可空性错误已修。原断言/权限/产品异常均未吞掉换PASS。UAC取消独立保留，未再次提权或绕过保护目录执行同一大规模命令。

最终交付复核发现默认`Ipc.Assembly`构建是Supply，直接传Parcel的`business-identity`被拒；它不影响上述Parcel库测试，但不能充当Sportmaster CLI冒烟。现行复现显式加`-p:BusinessModule=Sportmaster`，实际新CLI正确自报sportmaster/1.12.2；脚本在新保护目录创建前核对实际可执行入口与业务DLL身份。该构建固定70858da（src/config与a192b7f相同），不能替换原产品DLL/hash。manifest自hash亦按Git规范化blob重新计算，避免Windows换行字节与Git字节混淆。

## 集成判断和剩余门槛

PR105针对明确新瓶颈交付局部产品修复，可进入工程审查；当前不自动合并main、不发布或安装。安全集成需最终head Linux/Windows CI通过、代码审查无未决问题、与PR104的Repository/测试入口/workflow/版本/manifest冲突审查；保持原事务、完整备份、权限与原请求责任。main若前进，必须检查合并树并重跑受影响检查，不能强推或借旧CI。

只读`git merge-tree`核验a192b7f与PR104分支发现9个冲突路径：`.gitattributes`、composition及三个业务版本项目、manifest两文件、稳定性运行文档、测试Program入口；Repository和workflow可文本自动合并但仍需语义复核。没有实际合并/更改PR104或main，探针不作为最终合并树验证。[原探针](../evidence/retention-ingress-02/PARALLEL-MERGE-PROBE.txt)

完整规模资格仍缺：60.48万约9.46GB清理全链+完整恢复（本轮`NOT_RUN_UAC_CANCELLED`），259.2万（同次控制器未启动；另需实际容量预检），大量未决/保护状态组合、最大规模VACUUM/迁移、真实raw大小及维护窗口、正式Production安装/接管、目标4核8GB、真实SDK/RCS、预热后24h和独立72h/跨日/物理故障/现场验收。PR103原60.48万FAIL、259.2万NOT_RUN和恢复取消保持原身份，本轮组件与小档模拟PASS不能关闭这些项或历史D/RV。
