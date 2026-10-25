# 代码可靠性02 · 验证记录

[工作包](../../work-packages/WP-IPC-CODE-RELIABILITY-HARDENING-02.md) · [运行与升级](../code-reliability-hardening-02.md) · [原始证据](../evidence/code-reliability-hardening-02/README.md)。起始/复核main为`1dfb676a2970f80e80d635303d84dcec75676cf5`；未吸收并行草案PR103、未修改原工作树附件。先提交文档替代/归档`c23997e`，实现`a9bd038`，组合版本同步后固定源码 **`51ad8500753c005e3bf98d5baf5c9699297d184c`**。

## 逐项处置

|项目|实际处置|证据和结论|
|---|---|---|
|H02-01 崩溃与恢复|保留已正确DB9及原请求链；增加父控制器强杀测试，未增加生产进程架构|9个窗口均PASS：输入落盘、T1提交、T2提交、外发前/后、原回包文件/索引落盘、T3提交前/后。每次独立左右2条输入、模拟服务端实际接收账和原请求完整比对；Unknown不重投，恢复外发0次|
|H02-02 生命周期/资源|修复启动尚未结束时Stop关闭资源、旧健康回调覆盖新Epoch、Stop同步前缀阻塞调用者；关闭队列等待真实接收所有者；日志撤权/淘汰/Dispose保留在途句柄|确定性屏障和150ms等待取消验证；接收成功后Stop排空到Interrupted、业务外发0；10轮启停/查询释放后读/管理观察计数0，两位worker均Stopped。10轮进程句柄始终368、线程21，私有内存约72.2→69.6MB；只是短时资源趋势|
|H02-03 日志与WPF|每页全扫描改为有界元数据复用、按原位置/hash复读；后台摘要/原文/上下文格式化、总呈现预算65536字符；快照实际读取退出后释放CTS/句柄|3000条首查3200次解码（索引3000+首屏200）；后续2页400次，123.05ms，保留轮转/上下文/hash拒绝/脱敏完整导出。131073条超过缓存仍保留尾部、相邻记录和全部导出，完整组合约19.50s；显式流式退化，不以缓存上限拒绝原文|
|H02-04 SQLite争用/过载|保留WAL/FULL、原5s busy timeout、逐位有界队列及共享故障围栏，没有证据要求提高等待或降低耐久|真实独立BEGIN IMMEDIATE锁下两位均约5478ms返回false，0虚构接收；WAL历史查询23.15ms。释放锁后仅显式新输入进入Processed；受控慢写/队列满/另一位完成沿用并重跑稳定性专项；成功交接/停止重叠176.53ms排空|
|H02-05 时间|RuntimeJournal退避改为局部TimeProvider单调时间；日志快照寿命使用定时CTS；健康语义保留|墙钟回拨12h+单调经过2s恢复写入；墙钟前跳2天但单调未经过不能绕过退避。已有健康未来确认与过期检查均正确。未改全机时间、业务时间、原报文、审计或UTC跨重启清理|

冻结核心命令、退出码、耗时和程序集SHA256见[CORE-RESULT.json](../evidence/code-reliability-hardening-02/final/CORE-RESULT.json)。Release构建0警告0错误；12个定向入口（本包/强杀、稳定性、AQA01、双相机、资源风险、RCS、心跳、窗口生命周期、呈现、升级冲突、版本兼容）均exit0。测试替身只替代外部设备/HTTP接收端，Application、SQLite、原请求冻结/回包恢复均为真实实现。没有删除关键断言或跳过失败换PASS。

## 失败、身份与发布边界

开发反证保留在证据目录：启动停止未保有资源、旧健康覆盖、每两页6000次重扫、时钟回拨不恢复、接收/停止阻塞及原生上下文7646852字符/Dispatcher约2969.90ms停滞。首个索引方案每行重复权限读取导致慢于基线的结果也保留，后续使用撤权token及每100ms权限复核，开始/结束/导出提交仍检查权限。开发记录未逐次保留二进制hash，明确为`c23997e`上逐步未提交改动，不能用该SHA伪称可复建；最终结果独立绑定51ad850和实际hash。

同源portable、win-x64冻结构建均已通过，[RELEASE.json](../evidence/code-reliability-hardening-02/final/RELEASE.json)保留原版本、SourceCommit、工具链和介质hash，位于`D:/releases/ipc-reliability-02`。包内记录WindowsAcceptance=NOT_RUN是构建时原始身份，后续原生验证单独关联，不能倒写包内回执。

冻结WPF首轮与本轮核心测试并行运行，同一合成设备资源租约被占用而拒绝启动。保留[native-resource-conflict](../evidence/code-reliability-hardening-02/final/native-resource-conflict/RESULT.json)，不删除租约、不绕过权限；结束本轮核心进程后串行验证同一介质，结果另列。该失败不计为设备或最终现场异常。

原始SQL1～9、checksum、REQ/BR/CAPI/D/RV与历史验收身份保持。最终IPC、真实SDK/RCS合同、物理断电、24h/72h和现场验收仍NOT_RUN/BLOCKED。本机Windows11 build22631、125% DPI、合成短测不能替代这些资格。升级/回退见运行说明：完整同源目录、原账/防重不回滚、未决原请求精确实现守卫不绕过。

## 冻结原生结果与后续收口

51ad850介质的日志专项PASS：401条合成上下文、原始3881973字符，实际呈现65536字符、上下文约171.34ms；Dispatcher采样最大435.95ms包含首次查询/布局，不能宣称全程低于100ms。开发反证约2969.90ms，二者是局部测量而非目标机吞吐保证。稳定性原生专项PASS。管理模式旧夹具固定等待150ms，原截图显示首次设置仍为禁用的构造初态，查找按钮失败；保留[native-management-before](../evidence/code-reliability-hardening-02/final/native-management-before/RESULT.json)。夹具80dc783改为8秒有界等待真实可操作按钮，不改产品或原断言，同一51ad850介质全部管理回归PASS，见[native-management](../evidence/code-reliability-hardening-02/final/native-management/RESULT.json)。

强杀夹具c161b02增加从磁盘重读独立输入/服务端账并逐笔对比原观察、行hash及恢复前后状态，全部9个窗口PASS，见[crash-disk/RESULT.json](../evidence/code-reliability-hardening-02/final/crash-disk/RESULT.json)。该提交src/config与51ad850完全相同；测试程序集及产品程序集仍记录本次实际hash，不能借用51ad850二进制身份。早先服务端日志是Wire多行序列，新的文件为逐行JSON；原记录不重写。

最后截图发现状态栏新增预算提示会挤压结果区，23af651把提示移到已有原文框的开头/末尾，并计入同一65536总字符预算，保持原状态栏和布局。此提交是最终产品源码，核心/SQL没有再改；51ad850介质作为前候选保留，不扩大其原生PASS到23af651。最终介质及UI结果在后续回执单独列明。


最终 **23af651** portable/win-x64介质均PASS，见[最终RELEASE.json](../evidence/code-reliability-hardening-02/final/release-23af651/RELEASE.json)。Windows最终三组冻结原生（日志、管理模式、稳定性）都以相同23af651夹具/DLL通过，回执和实际程序集hash见该目录各`native-*/RESULT.json`。日志上下文153.45ms，Dispatcher采样最大393.46ms（含首次布局），实际呈现65536字符；预算提示在原文开头/末尾可见，不增加状态栏高度。实际发布EXE只读business-identity启动exit0。未执行安装、真实设备或现场验收。

可复现命令已记录CORE-RESULT、crash-disk/RESULT、RELEASE及运行说明；Windows冻结命令为`. ./scripts/project-environment.ps1`后执行`scripts/verify-commercial-wpf.ps1 -Media D:/releases/ipc-reliability-02-final/sportmaster-win-x64-23af6518f55b -Output <新的D盘目录> -ReliabilityLogs`，另外两个入口为`-ManagementMode`和`-Win11Stability`。三项串行执行，避免合成设备租约冲突。最终CI以PR所列精确head/merge checks为准，仓库本机回执不代替远端CI。


## PR104首轮CI及夹具修复

HEAD8da0ddc、PR合成merge af0c4ade的[运行40115179715](https://github.com/yuan2go/importer-pc/actions/runs/40115179715)：Linux边界PASS；Windows冻结发布/受保护管理边界/首配参数与任务层级/工程流程通过，但新增日志专项在实际机器身份预检处拒绝，最终Windows job FAIL。原日志与失败身份见[CI原件](../evidence/code-reliability-hardening-02/ci-40115179715/RESULT.json)。

该夹具本应验证真实Application/SQLite/WPF及合成日志，不能要求CI安装真实机器运行策略。8d3a67d复用既有CommercialProductization.SyntheticFactory和显式RuntimeIdentityPreflight测试接缝，与原管理/稳定性原生夹具一致；只改tests，不改产品部署、身份、权限或业务断言。修正夹具配同一23af651冻结DLL本机PASS（169.20ms上下文、436.09ms全程最大Dispatcher采样，原文预算和撤权断言保留），[独立身份回执](../evidence/code-reliability-hardening-02/ci-40115179715/local-fixed-fixture/RESULT.json)标明FixtureMatchesMedium=false。最终PR head的完整CI须重新通过，不能沿用首轮Linux结果替代新head。
