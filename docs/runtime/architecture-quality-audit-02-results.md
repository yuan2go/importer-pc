# 审查02 N01–N07实施与交付

最终Release/随包指南/打包源码：**2ad633578141db4ef34e255217bc8815634ffd0a**。业务代码固定**bab9c3012652c3b8aae1d90627953734a1de29b8**，两者在src/tests/config/scripts/examples及构建文件完全相同；2ad整理当前指南与证据，构建提交元数据改变精确公共DLL，已另建签名包。N01文档先行提交1865139，随后业务提交bab9c30。开工与收尾执行main均为**b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d**；审查固定基线2126ac2c06a0c352d96afbbf13ec1a719c352738。使用独立工作树，原main工作树及未跟踪客户资料保留。

继承开放PR24的a2e987a8d15eb397ad1a40cf1a303bdad4ec397b，已核祖先包含PR23的966ec6aa17dfe29b96fc9b9113f3037ba586f6a0；未操作或代合并这两个PR。main在审查基线后仅含PR25文档发布，不能把PR24能力说成main已有。PR24最终3d69607源码的原始验证、旧候选及PASS保留形成时身份，本次重新构建与执行见[原始证据](evidence/architecture-quality-audit-02/README.md)。本报告A编号属于审查02，不与审查01 AQ或其缺PDF记录混同。

## 处理结果

|审查项 / 工作|最终实现与复用|本次实际验证|
|---|---|---|
|A01 / N02|复用PR24 ConfigurationRevisions.BeginActivation/ConfirmActivation：唯一配置账保留最后确认Effective、Pending和尝试；资源成功才确认，中断标Interrupted并禁止自动重试。没有第二配置账|构造前、构造后、部分Start后子进程退出再重启，旧Effective/候选/尝试保留；维护重新保存后确认。解包Release另实测已签reader启动失败保旧、完整资源清理后重新启动|
|A02 / N03|修复Attached Dispatching重启仍写主Reason；Flow按已Committed且hash匹配的原主阶段raw恢复旧Blocked/ImageUnknown投影，完整旧JSON留审计。新增显式原接收核对入口，原Awaiting、缺raw事实及新Query各自保留|真实Application上传外呼等待时终止子进程；主结果/Reason和WCS Attempts=1保持；另一位继续。无结果不清责，有效权威Query以新原件同T3关联OutcomeReconciled，旧ID/Source/空raw_hash不伪造；原图依原策略才释放|
|A03 / N04|继承DB4活跃/历史分离、永久去重、原raw、分页、工作集/未结容量守卫；增加基于SQLite实际页数、原件字节和磁盘保留的备份预检及恢复到新卷的容量计划|连续归档、分页、去重、备份恢复及超过64回执继承组重跑；容量不足在创建恢复集前拒绝；实际解包CLI做recovery-plan、restore和DB3离线修复，旧未结/原图/原件保留。无年龄删除，无客户保留期限假设|
|A04 / N03|继承持久冲突及受影响位；补旧版仅审计而无作用域的冲突恢复。未知范围明确为待检查的可能位，原历史不批量标清|同位冲突只阻left、right仍可接纳；跨位冲突保两位原身份；重启持续，暂停/认证/说明后审计解除，旧审计不会每次重启重新设障|
|A05 / N05|继承左右各自当前结果/命令目标与历史选择；补每位主结果/后台责任显示，维护回执命令绑定所选作业清单中的确切发生ID|两个位置交错、当前/历史Owner独立；实际解包两份签名reader与签名WCS各自完成。Windows画面/触控另列未验|
|A06 / N05|继承完整schema3 JSON与加载时修订CAS；维护页增加Effective/Pending完整只读对照，草稿、候选和失败可辨，旧单reader表单仍拒绝覆盖拓扑|两个真实ParcelViewModel同时加载，B保存后A自动刷新仍以旧草稿修订冲突，草稿及另一位/库存保持；完整配置校验/保存/受控重启与失败保旧实际走通|
|A07 / N06|继承Store原操作恢复/安全卸载、精确包/在用/未结/备份/逆依赖引用检查，未知消费者拒绝。未新增旁路|本次精确Release合同签名包安装中断恢复、在用/Pending/备份拒卸载、清偿后退休；重启后仅Pending含包的备份仍独立阻卸载；不可读消费者不返回空集合|
|A08 / N06|沿compositions核心闭包增加重复--extension-package输入；完整精确依赖、目标Release公共DLL和静态签名检查后，所选集合实际决定offline-extensions及composition.lock.json。不复制信任或私钥|核心媒介0包、选装媒介2包，分别独立解包核SHA；选装Release实际安装/恢复、绑定两reader及WCS、激活、作业、失败保旧、解绑退休。最终能力包1.7.2与观察包1.0.12为新不可变身份，ABI仍1.6.0|
|A09 / N01|先在现有README/INDEX、22运行、维护/扩展、UI STATUS整理当前事实；Supply操作独立成可读入口，替代指引原字节归档；随包按产品选择，保留有效19/20/22设计和历史证据|实际按解包包完成启动、接Driver、完整配置提交/重启与失败恢复；Python仅检查指南链接/集合，不用其结果冒充业务验收|
|N07|主结果解释留给三个Flow，共用恢复只管原件/事务；容量盘符读取和产品指南集合共享；没有全量改名、通用业务/恢复/插件框架或公共ID替换|原SQL1～4、配置schema3、旧冻结JSON/请求/包身份保留；三个独立业务及旧Supply/Inspection边界不变|

Sportmaster左右独立扫码至WCS有效格口，不下PLC；DirectPlc/WcsToPlc仍各一个main顺序位，按配置使用本件重量。PLC接受和物理完成分开，未知不重发。多页面共享Application和连接。DirectPlc完整UI保持批准延期；没有新增Sportmaster展图或长期图库。

## 迁移、恢复与维护方法

现行可执行命令见[维护指南](architecture-quality-closure-01.md)，产品启动见[首次运行](getting-started.md)，签名/接入见[扩展开发](../extensions/developer-guide.md)。

1. **配置中断**：保留同一configuration.json的Effective/Pending/Activation；读失败原尝试，完整Stop/Dispose后以维护身份比较候选，使用加载时配置Revision显式重新保存。重启资源成功才确认，不修改原请求目标或以seed覆盖有效账。旧程序已经遗失且没有审计/备份的旧值不能凭空找回。
2. **旧主状态污染**：当前DB4启动只从原已提交主raw恢复派生结果，旧快照入审计。旧DB2/3仍需先匹配旧发布清偿；确需独立修主投影时离线执行repair-main-projection，提交原DB SHA和检查说明，工具取得原应用租约、先生成完整备份，仅修可证明主结果，保持原schema和未结屏障。它不授权不兼容原能力执行或强行升级。
3. **缺raw的在途发生**：暂停/维护认证，记录页选原作业，再读取ReceiptResponsibilities（保留全部未清及最近200次发生）；ReconcileReceipt指定确切Awaiting发生及说明，仅原目标/实现的权威Query可建立新回执。无结果/不支持/冲突保持责任；原RawDurable丢件仍需恢复原字节。OutcomeReconciled只表达结果已有权威依据，旧raw仍为空且缺口可审计，不代表找回旧原件。
4. **容量维护**：保留所有未结原图、请求、包引用和永久去重；先Backup，停止旧根，再对容量充足新卷执行recovery-plan/restore。计划显示实际CopyBytes、保留和剩余增长空间；新根首次隔离，核对外部事实后恢复。原集保留，不能两个数据根同时控制设备；历史删除/客户期限不在本次自动决定范围。
5. **扩展维护**：所选媒介包先inspect/install，再同一完整配置账绑定和重启；安装不等于生效。卸载沿Store检查有效/候选/在用、原件、备份、逆依赖；未知或混合旧消费者保留包并使用匹配业务维护读者。离线修复含包备份也要求登记Effective/Pending，缺Store/独立信任时拒绝。

DB4迁移SQL未改，旧DB2/3修复保持原标记；新回执资料状态在原表保留，不删除原发生。Application.Contracts没有新ABI变更，最终能力包1.7.2与观察包1.0.12按实际Release公共DLL独立签名，继承PR23/24采集所有权。旧包、签名、请求和历史候选不覆盖；降版仍走匹配旧发布/完整备份并核对外部动作，禁止原地忽略责任。

## 必要验证及候选

[checks与原日志](evidence/architecture-quality-audit-02/checks.json)、[最终进程退出码](evidence/architecture-quality-audit-02/process-exits-2ad.json)、[最终源码/指南逐文件hash](evidence/architecture-quality-audit-02/final-source-files.json)、[宿主指纹](evidence/architecture-quality-audit-02/test-host.json)。固定SDK10.0.402、locked restore、单节点构建；核心、Sportmaster WPF交叉构建与三个必要候选构建成功。2ad最终AQ、签名扩展/Pending备份、七模块能力与解包旅程通过；bab9的Release AA22/AC17资源覆盖完全相同的业务代码，按原宿主身份读取。两个实际VM/CAS、产品指南检查均通过。没有无差别全库验证。

核心和选装portable各从ZIP新解包核文件hash；实际进程及独立HTTP Attached旅程完成登录、本件实测、真实PNG字节、上传丢回复、重启原查询及接收端每请求一次。选装另实际运行两份签名reader/WCS、受控配置、启动失败保旧及安全退休。[旅程](evidence/architecture-quality-audit-02/selected-journey.json)与[CLI旧库恢复](evidence/architecture-quality-audit-02/recovery-cli.json)保留具体事实。

|候选（最终构建源码均2ad6335）|ZIP SHA-256|
|---|---|
|sport-core portable|3738b23a7b2e8fe84d19b1979d453310924625ecc5c8c31ad607480f23bd42d3|
|sport-selected portable|e70d8197844f959717e4e88dab33d8238cb8e3ece4b9173561023c30ebdebbbc|
|sport-win win-x64|ce7580cfaaefca0f324f445133d944af51702adce5336a275735e5f9fb7adb3e|

候选在本机Codex artifacts的`ipc-architecture-quality-02-20261018/final`目录，未作为生产Release发布，二进制和私钥未提交。运行：`dotnet app/Ipc.Assembly.dll --config app/config --data-root <新隔离根>`；输入snapshot和BusinessCommand JSON，结束quit。Windows包含实际所属WPF及受保护部署脚本；构建/解包不代表Windows运行。

最初上传夹具漏能力库存被正确拒绝；新断言空值检查曾编译失败，均保留原日志。两次中间命令在失败构建后继续执行旧入口，明确NOT_FINAL，不计最终PASS；修正后采用构建成功才运行并固定宿主hash。没有删断言、弱化原件/资格/迁移守卫或抹掉失败。

## 验证边界

- **NOT_RUN**：Windows实际WPF/双窗/DPI/HID/触控/声音、PowerShell/UAC/ACL、Windows后装运行；真实节拍长期负载、断电/故障介质及旧站全量迁移。WPF交叉构建、两个VM和win-x64候选不关闭这些门禁。
- **BLOCKED**：厂商SDK、真实WCS/PLC/身份合同、实体测量/采集关联、离场/复位/安全联锁及现场批准。无法权威查询或缺匹配旧实现的原件继续保留责任，不能标作已清偿。Production仍NotReady。
- 原UI HTML/CSS/JS、同源PNG和原审查02 PDF保持原字节；未声称旧HTML/RV已修复，D/REQ/BR/CAPI/RV原状态不改。审查01缺原PDF的历史记录不由本次审查02材料替代。
- 资料校验FAIL保留与PR24完全相同的267项历史资产错误，新增0；系统Python缺Pillow的首次输出与随后采用完整依赖的校验分开保留。不能以格式工具或测试数量认定工业现场资格。

早期bab9候选、1.7.0/1.0.11包、对应宿主及原始检查以*.original.json和Git形成时记录保留；最终2ad公共DLL另用1.7.2/1.0.12，旧身份未覆盖。后续仅回执/清单提交不重贴候选SourceCommit；重新构建新HEAD仍须匹配实际精确公共DLL。
