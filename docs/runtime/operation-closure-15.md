# OPERATION-CLOSURE-15 实施与验证

从执行时最新main `5548b3f962c409079618635d3128830ea7887069` 建立隔离分支`codex/operation-closure-15`；实现与同版候选冻结于 **`013862fc2d1769605624f8e10df5cc5b448a7954`**。本包授权工程工作已完成：每次接收独立责任、箱结果/可用性、输入隔离、维护草稿/异步选择、追溯结构和合成旧部署升级。**核心必要验证通过；实际客户端截图0，Windows行为/DPI/HID/声音/触控/PowerShell/ACL为NOT_RUN，真实接口/产品/现场按项BLOCKED。** UI实际资格没有关闭。

[逐问题/AC与checks](operation-closure-15-checks.json) · [完整原始证据](evidence/operation-closure-15/README.md) · [执行基线/计划](operation-closure-15-baseline.md) · [前置原字节](../archive/pre-operation-closure-15/README.md) · [UI映射](../ui/operation-closure-15-mapping.md) · [Windows后验](validation/operation-closure-15-windows.md) · [同版参考候选](https://github.com/yuan2go/importer-pc/releases/tag/operation15-reference-013862f)。开发分支已提交，[main PR #15](https://github.com/yuan2go/importer-pc/pull/15)为OPEN，未合并。

## 基线与责任边界

开工时原工作区HEAD0096f34，5组未跟踪任务/客户材料未移动、覆盖或提交；其他工作树不变。0096→5548只增加文档；PR13/14已合并，开工和交付前查无其他开放PR，origin/main仍5548。FIX14的68cdc36证据按原范围继承，不重做已闭原子问题。前置文档提交882ec3f早于业务代码；将需改旧入口原字节、来源SHA/blob、SHA256及替代关系归档后更新当前入口。最终README/UI状态也先补存5548原字节，再更新入口，未重写有效设计。

原PDF固定8cabf06d，SHA256 `7586e7c58f37266eea4a27af453a7a48fad86d14d7f225ba2824755ab457d441`。原12/13/14任务、POST-PR14问题表、历史首次失败、原SQL1～4/checksum、D/RV与v1.1实物不改。[保留核验](evidence/operation-closure-15/preserved-inputs.json)逐字节覆盖62项及SQL；25项新处置保存在15 checks，不倒写历史问题来源。

基座仍提供机制，Supply唯一业务权威，UI调用应用契约，Product/IntegrationRuntime保有网络和整次查询资源。没有第二发送器/接受账、通用解锁、自动重发、微服务、PLC直控或生产模拟回退。软件暂停不是安全急停。

## AC15-01～08实施与退出

|AC|结果与实现|验证/仍有门禁|
|---|---|---|
|01|执行版本/并行PR核查、读实物/交接、原字节归档和过期入口清理；当前入口指向15，不改原任务及批准基准。|882ec3f先于业务；snapshots/source/preserved-inputs和资料校验差量。|
|02|`ResponsePersistence`每次外呼持久登记发生ID，发送登记并入原交付授权事务；原件按发生ID Flush，T3原子保存响应/冲突/完成证明。重启逐次重放/确认；无原件的该次责任保留RawUnavailable。|真实SQLite Q1/Q2组合故障、实际Product并发原查询、T3后中断/原件重放、原身份/唯一接受/守卫及资源链PASS。缺失原件不伪造、不由另一查询消责。|
|03|`CurrentBoxResult`从本箱原Context/CheckClose/Close持久请求和响应派生；状态与最后拒绝/原因/原ID并存。当前健康/来源/投影和动作理由就近显示，Accepted事实不变。|三类拒绝、另通道成功不盖箱拒绝、旧箱归属、刷新/重启/权限及过期策略PASS；实际WPF长内容/客户区NOT_RUN。|
|04|`ScanInputDraft`接线至LaneControl真实SendAsync/刷新生命周期：每通道一次一条，忙态只读，字符/粘贴/Enter/Tab显式拒收提示重扫，失败保留选中。`ConfigurationDraft.IntegrationTarget`局部读取不Build；严格保存/预览不放松。`SelectionLoad`保护成功/错误/finally，导入/新建/身份失效使旧返回失效。|输入owner/version/Persisted、迟到/失败、部分草稿、A慢失败/B快成功/取消代次和原维护CAS/直接激活核心PASS。真实HID/焦点/双窗及完整页面事件旅程NOT_RUN。|
|05|箱会话与Item分别显示对象码/原ID/接受量/尝试与未接管摘要；列表全宽、详情按需/返回，筛选有标签/本地日期时间/时区/非法范围反馈；技术树默认折叠。详情为次动作、重复入口移除。ReviewFits排除隐藏的非必需次动作，仍检查关键事实/固定动作/波次差异/拆包输入。|应用鉴权/过滤分页、时间边界/拒绝反馈、现有保护策略和WPF构建PASS；实际日期控件、滚动、触控、长字段/DPI逐项NOT_RUN。仅复用既有tokens/字体，无字体文件。|
|06|保留匹配旧宿主与原1.0.1包，实际安装/保存/激活/重启Effective→清偿原Unknown与实体/会话→无扩展过渡Effective→备份/显式3→5升级→安装新1.0.3/保存/激活/重启→供包/追溯/停止重启。|最终冻结候选完整合成旅程PASS，最终接受账4；备份目标冲突保旧、旧读者拒绝且库不变。旧发布/seed/包字节未改。真实旧根/备份覆盖仍UNVERIFIED，拒绝卸载，无绕过。Windows ACL独立NOT_RUN。|
|07|受影响选择器、故障/防重/并发/资源、locked restore、WPF/发布构建、实际解压核心/扩展冒烟；双宿主两类插件各两次独立编译核精确manifest/payload。|见下表实际源SHA，保留所有失败/完成标记。portable/win全文件SHA/CRC同源PASS；实际Windows截图0，未以HTML替代。|
|08|当前文档/ADR/UI映射/部署恢复、配置/格式/精确包身份、manifest与可运行候选/完整日志/逐问题矩阵；开发分支及main PR。|工程交付供审阅；外部资格分项保留，D/RV不因PR或局部PASS关闭。|

## 接收、追溯与恢复

新增前向Storage/MinimumReader **5/5**，原迁移1～4和checksum保持。表`receipt_occurrences`记录Id/RequestId/AttemptId/来源/开始/状态/原响应hash和内容；同请求两次接收不共享清责位。AwaitingResponse、NoResponse、Committed与RawUnavailable分开，原业务Unknown和原接受事实独立。显式无响应只结束调用自己的登记，不能撤销已接管原件。

查询先登记再外呼，整次查询/T3/停止仍由原运行时持有资源；发送使用既有交付授权事务，未在最后发送守卫之后增加额外登记等待。正在运行的Prepared/Dispatching登记不被误判为历史Unknown阻塞另一通道，未结责任仍参与关闭/激活/维护检查。运行中存储失败持续锁定；重启后缺raw只保留该次不可恢复责任。已T3提交的raw校验后清理，不再凭历史终态制造永久未决；格式4无逐次身份的围栏按未知覆盖导入并保留原文件，不靠任意raw推定全部完成。

恢复列表、原身份查询、Guard、历史详情/导出/清理、维护预检、备份升级及精确旧包保留读取同一责任事实。T3不改原报文/hash/合同/目标/实现/attempt，不重计或倒扣唯一接受账；矛盾与原终态正交。缺原件明确未取得/不可恢复，需原责任来源/权威核对依据，仍无通用解除或盲重发。

## 实际验证与来源

环境macOS26.5.2 ARM64，SDK10.0.402/runtime10.0.13；无新增依赖，单节点MSBuild。每项记录命令/时间/源SHA/平台/退出码/完整完成标记和日志hash。[源差异证明](evidence/operation-closure-15/source-evidence-comparison.json)明确复用范围，不能把所有选择器写成013重新执行。

|范围|实际运行源|结果|
|---|---|---|
|locked restore|3c36ba3；之后依赖/锁文件未变|PASS|
|baseline/followup/closure/review-followup/architecture|110d2b1；最终所有非Desktop运行时代码相同|PASS；含崩溃窗、双通道发送仲裁、装配/冻结宿主/停止资源|
|新operation-closure与conformance（含1/2/3/4→5）|0ccf3fc；其后只补UI详情身份和升级脚本SHA参数|PASS；实际Product并发原查询、接管/T3组合故障、重启、维护/输入/时间策略|
|WPF最终交叉构建|d2b2ca2；之后仅升级脚本参数格式改变|PASS，0警告/0错误；不是Windows运行|
|portable/win-x64发布、解压原生核心/扩展冒烟、旧部署旅程、插件8次独立编译|013862f|PASS，原发布字节不变、全文件SHA/CRC；Production缺能力拒绝且fixture业务0|

保留并解释的失败：首次编译变量名、过期format4期望已修；8df的发送登记使活跃Prepared被当Unknown阻塞另一通道，110d修正该区分且保留原“双Prepared”断言；110d并发测试只等待了可能同一已完成任务，0cc改为等待两任务后取快照，不改业务/有效断言；旧部署脚本小写SHA被精确校验拒绝，013传标准大写后完整旅程通过；d2打包ZIP遇ENOSPC且收集记录也失败，保留部分ZIP/日志/恢复记录，校验迁移仅本任务产物后重归档，最终统一013重新打包。没有删断言/吞异常/跳关键测试换PASS。0cc/d2本地候选已撤回，未发布；最终仅013分发。

## 候选与可运行闭环

[预发布附件](https://github.com/yuan2go/importer-pc/releases/tag/operation15-reference-013862f)为Simulation通用参考实现，非现场发布。源指纹`a2b4f5c6d781da3fcc156c55e6cb9f4d9500942dee5a0ae634ef7697aa0ea14c`，合同SIMULATION-v1/扩展契约1，新参考扩展1.0.3；观察/集成插件源分别6507edb4/68cdc36f。旧1.0.1/1.0.2未重签。仅分发独立TestOnly公共信任，私钥不入库/包/附件。

|附件|SHA256|
|---|---|
|operation15-final-portable.zip|d5bfe897360e74c84041e55b4f076eefa9c406b89dedf9bfd8734ce53a713dea|
|operation15-final-win-x64.zip|2bdf336c9b694f511501aa6af3fc3a48f57419f0a60344d020e2e9552aa60bbd|

[候选全文件身份](evidence/operation-closure-15/candidate-inventory.json)与[远端全字节回读](evidence/operation-closure-15/remote-readback.json)覆盖源码包/两个ZIP/精确ipcx/公共信任；内层CRC、文件集合、SHA与本地一致。平台公共DLL指纹在manifest内精确绑定，PSS签名随机，不要求ZIP签名字节复现；签名manifest/payload跨宿主和独立编译一致。

解压到新目录，不覆盖旧根。portable需固定.NET运行时；启动本地fixture后用`dotnet app/Ipc.Product.dll --config app/config --data-root <独立合成根> --action serve`；Windows运行`app/Ipc.Desktop.exe`并按随包OPERATIONS.md/EXTENSIONS.md配置指定账户/ACL/独立信任。完整发布验证可在仓库运行：

```sh
python3 scripts/smoke.py --product <解压目录>/app --fixture <解压目录>/fixture/Ipc.Fixture.dll --dotnet <固定dotnet> --output <新core.json>
python3 scripts/smoke-extension.py --release <解压目录> --dotnet <固定dotnet> --output <新extension.json>
python3 scripts/verify-operation-upgrade.py --old-release <原13匹配解压目录> --new-release <本次解压目录> --dotnet <固定dotnet> --output <新升级证据目录>
```

升级脚本保留旧/新只读发布内容，使用SaveConfig/ActivateConfig及受控重启，不换seed伪造Effective。先核旧接收/未知/实体/会话与精确包责任；任何不足仅阻塞对应能力。恢复隔离、权限/CAS、包可见性与LegacyConsumersUnverified门禁不变；详见[operations](operations.md)和[扩展指南](../extensions/developer-guide.md)。

资料校验整体仍 **FAIL（原有267项：265处PPTX格式错配、2处私密元数据标记）**；与既有证据逐错误比较无新增，见[差量](evidence/operation-closure-15/archive-comparison.json)。本次源码/文档/链接/manifest身份无新增错误；未修改原资产或放松校验器把整体改成PASS。
