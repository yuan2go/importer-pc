> 历史N01–N07及PR26作者结果保留原身份；独立复审另发现P1/P2且architecture-quality有失败，见[本轮整改](../work-packages/WP-IPC-PR26-RECOVERY-CLOSURE.md)及[原独立报告](../reviews/pr26-independent-78298b5/README.md)。本页历史PASS不代表本轮阻断已闭。

# PR26审查修复与交付

本次修复独立审查的5项P1、2项P2，更新PR26，不合并。业务、当前指南、打包及扩展源码固定 **e914e75a109672cd1d52be91c40459f09231ea57**；证据提交仅保存回执。文档先行提交80b8f6a，随后实现1291ee6，收尾纳入最新main的整合源码e914e75。开工main为b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d，收尾main为4afe2afacde9e2acab7648fff3440fa3cc331b6e；审查基线2126ac2c06a0c352d96afbbf13ec1a719c352738。原main工作树和私有未跟踪资料未改。

PR26原592d2bdd已包含PR24 a2e987a8及PR23 966ec6aa。本次逐项评审后继承PR24后续b82b8d07/7850fcc02的永久回复索引、严格消费者读者和资源围栏等代码/锁文件/定向验证；执行中PR24由外部独立授权合并：最终head f531d3a6882891cfc7870f5dcccfe985009d6be4，merge 4afe2afacde9e2acab7648fff3440fa3cc331b6e。本次再次核对最终差异，完整继承445e1f6的R03去重前库存校验、其schema1/2/3消费者/卸载测试及原字节证据，再在本开发分支整合main；本任务未合并任何PR。main能力、PR26增量和历史PASS分开。原形成时报告及指南原字节在[归档](../archive/pre-pr26-rereview/identity.json)，原[审查02证据](evidence/architecture-quality-audit-02/README.md)未覆盖；其中OutcomeReconciled可以释放原图及DB3仅投影修复的结论不能继续作为闭环依据。本报告A编号属于审查02，不与审查01 AQ或缺PDF记录混同。

## 7项复核修复

|问题|实现与边界|新增验证|
|---|---|---|
|P1 缺raw结果核对变相清责|SQL5独立资料索引关联Committed Query，原Submit发生仍Awaiting/raw_hash=NULL。未结原件继续阻止图像释放、归档和原包退休；不提供删责任/强清命令|真实上传原件落盘后终止；移走原文件后原Query确认结果但原图/责任保留；完整恢复集取回实际原字节后本地T3，原WCS/Upload发送各1。实际旧2ad DB4已归档OutcomeReconciled迁移重新装载责任，历史已释放原图不伪造找回；恢复作业再次归档复用校验BLOB并重启|
|P1 旧状态升级死锁|窄离线recover-legacy-receipt先持有原应用租约、核DB/WAL hash、完整备份，再隔离加载逐文件校验匹配原发布，只Start/Health/Query冻结上传端口。不启动reader、不Submit。证明所有外部业务完成后显式升级DB5，原资料责任保留|真实8981d54旧Release制造在途终止，旧程序实际重启写出污染Blocked/DB3。新CLI经原ABI Query恢复主结果、升级；维护Query原件落盘后终止、重试仅本地T3。冲突、相反终态、上传物理完成、其他原件丢失保持旧schema/隔离|
|P1 回复ID长期索引缺口|继承PR24恢复raw也写永久原回复索引及历史补齐，实际原bytes是身份依据|原回复经65次Query离开短列表后，篡改同ID内容仍Conflict，保留原事实|
|P1 未知消费者空引用|继承PR24按Application拥有的读者验证配置与冻结绑定；未知、损坏、混合、未来消费者拒绝；继承最新main R03在去重前验证schema1/2/3原始库存及完整包闭包，不能以空集合绕过|Pending/冻结配置异常、未知标记、旧根、仅Pending备份重启后仍阻卸载；签名包集合及逆引用沿Store|
|P1 WCS维护围栏缺口|继承PR24在同一gate登记调用和资源维护，重连时Submit/Query/相关Resume与准入被拒，独立资源可继续|真实阻塞重连期间Query/Resume/入场并发断言；不吞超时、不提前释放所有权|
|P2 旧配置账未确认|同一ConfigurationRevision envelope新增Confirmation，保原envelope Base64/hash、EffectiveHash和启动尝试。无成功证据的旧输入启动失败/终止不虚报Effective，资源成功后仍AwaitingMaintenance并暂停|构造失败、部分Start失败、断进程重启；维护认证、RuntimeId/配置Revision/hash CAS；两个实际VM的读取身份不会被刷新推进，陈旧确认拒绝|
|P2 示例包版本过期|checked-in模板不设Version，构建要求显式新三段不可变版本；当前指南以媒介lock/identity为准。独立声明显式Version保兼容|缺失/空/范围/预发行/非字符串版本构建前拒绝；新能力1.8.1、观察1.1.1包精确匹配最终Release公共DLL，旧包身份不覆盖|

## A01–A09 / N01–N07

|审查项/工作|闭环范围|
|---|---|
|A01/N02|保最后确认配置、候选、失败/中断尝试；补旧账资源成功后的显式维护确认，仍只有一套配置账|
|A02、A04/N03|主结果按原Committed raw恢复，Attached及每个原发生资料独立；原Query确认外部结果不释放资料；冲突持久/按位保护与审计解除继承并复验|
|A03/N04|继承活跃/历史分离、永久去重/raw保有/分页/容量/完整恢复集；补旧归档缺口重新进入工作集和再归档。原图仍按原策略、仅真实原件清偿后释放；无客户保留期限或自动历史删除|
|A05、A06/N05|继承双位目标、加载时配置草稿CAS；补旧账确认读时身份与真实双VM冲突。页面共享业务实例；DirectPlc完整UI保持批准延期|
|A07、A08/N06|严格消费者、Effective/Pending/在用/未结/备份/逆引用；最终新签名包与实际核心/选装媒介。安装、绑定、资源启动、生效及退休分别核验|
|A09/N01|文档先行，现有README/INDEX/22运行/维护/扩展/UI STATUS统一，Supply入口独立，原字节归档；当前指南不靠追加冲突说明维持。REQ/BR/CAPI/D/RV及原PDF/证据不改|
|N07|原ABI桥隔离在装配，业务恢复/资格属于Parcel，平台仅配置/存储机制；复用Store/compositions。SQL1～4、原冻结JSON/请求/hash/包身份保留，没有通用恢复框架或全量改名|

Sportmaster左右各自reader/投递位，扫码到WCS格口、不下PLC；DirectPlc与WcsToPlc各一个顺序位，以配置关联本件重量。保留事务/Outbox、原请求、未知不盲重发、PLC接受与物理完成区分、局部隔离及完整资源排空；旧Supply/Inspection规则不侵入。

## 迁移与恢复

可执行命令、离线备份和恢复到新卷步骤见[维护指南](architecture-quality-closure-01.md)。DB5追加SQL，不改SQL1～4；旧程序拒读新库。普通旧库启动仍拒绝未知外部责任，专用旧发布Query只支持可证明的Attached上传恢复，不代表任意旧业务自动迁移。

- 配置候选失败/终止：保Effective/Pending/尝试，核对后维护restage，资源全成功才确认。旧账无证据时先读取待确认原账，认证并以读取时`runtimeIdN:configurationRevision:effectiveSha256`提交ConfirmConfiguration，之后另行Resume。没有原备份不能猜回旧程序已经覆盖的值。
- 旧DB2/3污染及缺raw：停旧程序、保存完整原发布/信任/包/原件/图像，使用DB及非空WAL精确hash和新备份目标执行recover-legacy-receipt。原Query不明确或冲突保持隔离；中断重试先处理同一持久Query。成功UpgradedCustodyRetained不表示原件已清偿。
- 资料责任：ReceiptResponsibilities展示原发生及独立Query关系，真正原字节仅由完整恢复集按原发生/清单hash恢复后本地T3。永久缺失且无合同处置依据时保持可见、图像/包/归档屏障，不编造原件或删责任解锁。旧代码已经删除的图像是历史缺口。
- 发行与维护：按composition.lock/identity选择包，inspect/install不等于生效；完整配置CAS、受控重启后确认Effective。退休沿Store消费者检查；未知消费者失败保持包。旧包/备份仍需匹配旧发布与外部事实，不倒账盲重发。

## 证据与验证边界

本次原始输出、命令、退出码、源码/宿主和候选指纹见[复核修复证据](evidence/architecture-quality-audit-02-rereview/README.md)。1291中间候选及1.8.0/1.1.0包、第一次实际解包PASS保留原身份；纳入新main后公共DLL元数据改变，另建1.8.1/1.1.1新身份并重新验证实际选装/升级旅程。最终能力/观察包的PluginSourceCommit仍为1291ee6：它是各声明源码作用域最后修改提交；宿主及精确公共DLL以e914e75和identity实际hash为准，不重贴插件形成身份。新增PASS只表示各具体参考入口成立；继承PR23/24的形成时验证保持原身份，不冒称本次新增或main能力。最初审查反例、维护夹具错误、旧断言失败和新发现的归档装载失败均保留，未删断言或改历史FAIL换PASS。

Windows实际WPF双窗、触控/DPI/HID/声音、PowerShell/UAC/ACL及Windows后装运行 **NOT_RUN**；WPF交叉构建和win-x64媒介不能代替。厂商SDK、真实WCS/PLC/身份合同、实体关联、安全联锁与现场验收 **BLOCKED**，Production仍NotReady。长期真实节拍和断电/故障介质未验。原UI HTML/CSS/JS及同源PNG、审查02 PDF原字节保留，不声称历史D/RV或旧HTML全关闭。

最终候选：

|媒介|ZIP SHA256|
|---|---|
|核心portable|4cf65a3a9ef9f46523fbd9d3bee532ccb8a9cd50f93d76d2de7db58d1e53f135|
|选装portable|2944b98f8ef991a6809f8ab9974d54e99c377d2b19a157fdb4eecd4dcb8f2ac9|
|win-x64/WPF|09ce344bb949e023ee2d107858e417a4929f33c9c650aef38dded0078b162439|

核心/选装/win媒介94/97/727项文件SHA读回匹配。实际选装进程、旧发布升级旅程、最终CLI三schema库存正反例见证据。预期驱动启动失败的子进程exit -6保留；外层旅程exit0不掩盖该负例。一次并发缺raw验证有两断言FAIL，同一二进制独立重跑PASS；失败回执原先未打印，不能事后断言其唯一原因。夹具随后补公开维护可用状态等待及返回值打印，所有原业务断言和守卫不改；补充夹具及完整原断言再次PASS，精确测试源hash/仅夹具patch/公共DLL与Release字节相等另列；原FAIL仍单列，不称全部运行全绿。

资料校验仍FAIL：267项历史资产错误与前次逐项相同，新增0、移除0；最初结构化输出的9项本机路径标记保留原校验输出，再建立显式公开路径投影及原始字节SHA，没有放松验证器。全部运行/随包SOURCE_PATHS在e914后未改变，末次补充仅测试夹具、报告、证据与清单。
