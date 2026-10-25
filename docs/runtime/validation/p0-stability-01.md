# P0-STABILITY-01 本次验证

交付基线 `a8cd431456f73e83040f75a0c5e3cc1423440d1b`。F0原基线为3a5b1d13；下面历史段落/摘要保留实际当时身份。新main重新核查、再执行本批回归，未执行项保持NOT_RUN，不继承历史PASS。

## ST01 问题卡

新main核查：PR109/a8cd4314已解决八组合的原心跳阻断、零读码器心跳、待配置重连及补齐RCS后的编号恢复。本包重放时保留这些主线代码，撤去重复修改。八组合在新main已通过；明确禁用的扩展读码器仍因缺bcr-code抛错，补充st01-main-a8-before.json先失败、after.json通过，当前新增产品差异只有禁用实例过滤及版本/兼容声明。后面的F0修复过程不是将已合并工作再次算成本包新缺陷。

确认：八组合夹具先于产品修改运行；mask 1/2/3（RCS启用且左/右/双相机延后）在联合校验、离线保存及Application启动复现缺少驱动bcr-code异常。首次夹具存在种子身份不一致和缺Windows运行政策的独立失败，修正夹具后再次记录，不将它们当产品缺陷。

修复：共用映射过滤待配置和明确禁用的实例；零活动读码器不发送心跳并保持未就绪。原业务报文生成、Unknown责任及一次扫码一次请求不改。

验收：八组合字段/联合检查/保存/隔离Application启动全部PASS；零活动读码器无心跳。原`--parameter-guidance-startup`、`--rcs-heartbeat`通过。新增隔离Application→TCP→RCS回归验证三次扫码（含相同条码）恰好三次原条码请求，原SQLite冻结Body与HTTP原文相等，旧quantity=7冻结报文逐字节保持。原`--sportmaster-rcs`组件协议/冻结报文断言通过，随后在受保护启动门禁因缺维护安装runtime policy受阻，该完整段NOT_RUN；旧测试未修改。

测试装配使用真实Application/SQLite/TCP/RCS组件及本地桩，不安装机器租约政策，不替代正式装配资格。新增扫码夹具首次错误读取正常投影的隐藏字段；改为只读原SQLite后通过，未放宽产品脱敏。

预留SM1.14.0（避开并行PR108的1.13.0）、RCS1.3.3；旧内置1.3.2候选可读取，新候选1.3.3，原请求字节/实现hash守卫不变。Contracts1.18.0/DB9不变。原始日志保留本地，公开回执无账户路径、凭据或现场数据。

2026-10-26复查新增并行PR109后，再以本包独立夹具证实同类遗漏：延后RCS的request-id-base/count为空时，补齐后Pending保存成功，受控重启仍解析旧未配置候选而失败；待配置相机重连错误报告Reconnected，待配置RCS重连抛异常。补充修改前/后摘要st01-activation-before/after.json保持实际当时本机父提交10eb35b9及工作树身份。修复仅筛除未配置候选的编号范围，既有范围表、永久编号底账、在库和归档原绑定仍全量核验；重连按Effective筛选，待配置项明确Rejected。原件、Unknown和Pending守卫未放宽。新夹具保留在ST01提交；集成PR109后丢弃重复的守卫代码修改，沿主线实现，不混入ST14。

残余：全部读码器延后时不发心跳待合同确认。正式Production资格/真实设备/RCS、完整24h/72h：NOT_RUN。

## ST02 问题卡

确认：修改前真实Application/SQLite夹具在AfterArchiveCommit后以独占句柄打开已归档spool；维护退出、accepting=0，失败断言已保存。更正原静态报告：清理阶段的spool hash矛盾、数据库BLOB缺失/损坏原先没有全部停供，后三个负向夹具也先失败。

修复：先独立验证数据库原件，再将冗余副本暂时读/删失败转为单调指数退避；第五次起公开告警、上限30秒继续重试。原hash/BLOB不一致明确故障；删除必须再次核验。启动回复身份核查同样避免依赖临时锁住的冗余文件，不削弱活动原件检查。

验收：本机Windows隔离Simulation四场景PASS。锁住时维护存活、accepting=1，另一位完成新输入及归档；解锁后无新命令自动清理。spool损坏、BLOB缺失、BLOB损坏均停供且未删除未核验文件。受控时钟验证499ms不重试、500ms到期、回拨/前跳不改变单调间隔、30秒/计数32封顶、告警和成功复位。新增启动锁测试首次发现回复身份校验仍读spool，补齐同一原件选择路径后通过。

既有`--retention-ingress`默认1000/10000档执行计划、清理、取消、备份及回滚检查通过；不扩张为604800/2592000、完整安装、Production或24h资格。NOT_RUN：真实磁盘/安全软件长期争用、目标IPC、真实接口和24h/72h。

## ST06 问题卡

确认：本机Windows原socket的KeepAlive=0，默认探测时间7200秒；FIN/RST能报故障但未退役驱动Epoch，持TCP连接停止转发时本地定时器继续续Ready。新夹具先失败；枚举值的数值别名导致首次JSON将Interval/RetryCount标成BlockSource/DontRoute，公开汇总保留别名含义说明，不误认为使用了UDP选项。

修复：配置并实际设置TCP keepalive三项；握手/入站字节更新有限确认，单调期限过后Faulted并关闭连接/退役Epoch，不由本地计时器制造存活。TCP1.1.0及各业务兼容声明同步，旧冻结RCS报文与DB9未改。

验收：Windows本地FIN/RST/停止转发三场景通过，读取实际socket验证time=1秒、interval=1秒、retry=2；入站期限2秒的停止转发场景约2.2秒内Faulted。只观察到一个原条码，旧连接后续数据未交接，无自动重连。原12类协议、停止取消保有/重启Epoch、健康证据组件断言原样执行通过。

残余与NOT_RUN：静默丢包的OS keepalive实际发现时间、真实相机空闲/网络/重连缓冲合同、目标IPC和24h/72h。300秒无来件期限需现场确认。自动重连方案已提交，开关及执行路径待合同确认；本轮没有把它标成已完成。

2026-10-26用户明确答复“尚未确认，先交付第一阶段和重连方案”；本批范围据此收口，不能把自动重连实现标成待交付欠项。

## ST14 问题卡

确认：在src、安装器、scripts及docs检索DispatcherUnhandledException、AppDomain.CurrentDomain.UnhandledException、TaskScheduler.UnobservedTaskException、SetUnhandledExceptionMode、WER/LocalDumps、计划任务/Run键/启动文件夹、sc failure。旧实现只有业务观察器、普通快捷方式和自动启动的授权服务；未发现三类全局钩子、自动WER投影、客户端自启动/失败重启和SCM恢复配置。原长稳文档要求管理员手配WER、外部诊断/电源/更新维护；前述程序化闭环未落实，电源/更新等仍属现场政策。新真实子进程夹具在修改前两场景均失败：异常退出无刷盘上下文，未观察任务无记录/计数。

修复：三类钩子及0xE0000014快速失败；已有一次UAC中登记有限WER、普通交互账号计划任务及SCM恢复；外部监督使用持久启动账、有限次数、单调健康间隔和明确循环保护。新任务/注册表/受控dump/服务动作全部进入卸载清单；原服务及WER按共享消费者处理，外部条目不覆盖。新部署可选策略不改变旧记录字节/构造合同，DB9和业务重放规则不变。独立Windows CI job纳入这些隔离夹具，CI本身不授予运行资格。

验收（本机Windows、全新隔离Simulation/合成进程）：AppDomain真实异常退出与任务未观察场景通过，日志含版本、commit、实例、Epoch、最近状态及进度，任务计数可见；编译原生产App构造器/Dispatcher钩子的WPF子进程记录后以指定退出码终止。进程监督验证强杀后按配置拉起、正常退出不重启、连续崩溃后预算持久围栏、更换监督者接管原PID/启动时间/hash、退役不杀业务、创建结果不明不创建第二进程、常规系统修复不清不明启动、单调健康预算复位。Windows Task Scheduler以TASK_VALIDATE_ONLY=1接受生成XML，没有注册任务。隔离文件清理只删受控WER名称，保留外部/畸形名字。

实际Application/SQLite/本地HTTP桩确认响应超时留下Unknown；停止再启动后冻结原请求不变，获得合成管理授权后核对/重连均未清除Unknown、未产生第二次HTTP请求。首次新增夹具漏掉管理授权，退出时旧超时HTTP桩又因listener过早Dispose掩盖了原授权异常；只修正新夹具并等待原桩完成，未改产品授权守卫或旧断言。

静态复查还探索了元数据读/替换：一个刻意跨消费阶段持有目标文件句柄的夹具失败，ShareDelete也未令Windows覆盖移动成功。但原File.ReadAllText在反序列化前已关闭句柄，该夹具不等价于实际调用生命周期，不能据此宣布原实现缺陷或删断言换PASS；撤回探索代码/夹具，原日志hash仅作未确认假设记录，实际投影竞争保留VM待验。构造器的版本文件读取上限4MiB、仅启动一次；目标IPC冷启动延迟未测，不能声称UI启动路径完全无同步IO。

残余：外部监督只处理进程异常退出，不能把进程挂起变为可安全强杀；系统登录会话、D盘可用性、WER政策/ACL、实际SCM/任务行为仍需要VM。全局记录为最后机会，磁盘不可写/挂起或native堆损坏时可能不完整。mini dump限制类型和份数，不承诺固定字节大小。周期读取部署登记与升级/退役投影的竞争仍需在VM重入场景覆盖。第三方同名Ipc.Desktop.exe及共享WER政策须由现场核对。

NOT_RUN：本机未UAC安装/卸载、未创建任务/HKLM项、未改变SCM或防火墙/网络政策；真实计划任务强杀自拉起、WER生成/份数轮换、SCM崩溃动作、系统卸载无登记残留、4核8GB/26100+、真实SDK/RCS/相机、24h/72h和物理断电均未验。签名、Production接管、604800完整恢复UAC及2592000规模维护也未因本包获得PASS。

## 本次回归与证据身份

新基线a8cd4314整合后重新执行14组，含PR109的deferred-connections全部通过；原生WPF参数/七组合与生产Dispatcher钩子子进程通过；四产品WPF及自包含安装器构建零警告/零错误。准确日志hash、源码blob和范围在integration-a8-after.json；这份整合补证独立于旧ST01～ST14摘要。发布文档两项检查通过。

本包实际重新筛查src九个工程目录的28个修改文件（其中21个C#）、config两个文件、scripts一个文件及CI一个文件；清单在整合摘要。未将本轮P0定向修复称作全src长期稳定性审查。确认无DB9/SQL1–9结构变更；与主线重复的重连/编号恢复及心跳循环源码没有差异。

本页记录实际工作树验证，最初ST14确认父提交为10eb35b9、后续重跑父提交见各摘要；产品文件身份由本次manifest及最终提交固定，未把旧介质PASS改记本次版本。结构化摘要在evidence/p0-stability-01/st14-before.json、st14-after.json；本机原日志仅保留本地hash，不上传机器账号路径或私有日志。提交后的同源介质构建/启动冒烟如执行，由PR补充准确SourceCommit和范围，不反写历史介质。

最终重跑12组均PASS：p0-recovery、p0-supervisor、p0-deferred、p0-cleanup、p0-tcp、p0-tcp-components、p0-rcs、p0-unknown-restart、parameter-guidance-startup、rcs-heartbeat、installer-options、installer-change-guards。补充原生产WPF钩子子进程和Windows任务XML仅验证均PASS。ST02提交时的retention-ingress默认1000/10000档PASS仍守当时身份，没有当作30天规模证据。新改文件重新核对UI调度、持有等待、异常观察、句柄释放、单调期限、未知原件和卸载登记。

Windows WPF四产品及安装器构建结果见摘要。安装器首次直接build因依赖未统一self-contained出现NETSDK1150；按发行方式传入-r win-x64 --self-contained true及BusinessModule=Sportmaster后成功，不以更改项目去掩盖该命令差异。完整受保护RCS启动原测试仍受本机缺maintainer-installed runtime policy限制，NOT_RUN；新增Simulation装配不是其资格替代。

## CI后续（原69df6bef不改写）

PR110首次run 40220770606未全绿：Linux旧诊断断言仍要求LocalTransportOnly字样，改为同时核对原观察时间、有限确认时间/期限以及“TCP keepalive不是应用心跳、业务准入分开”的实际证据；不删业务、Epoch或防重断言。Windows隔离快捷方式夹具在COM Save失败，先补齐实际安装本来已存在的release/working目录和不可执行合成目标，保留全部COM回读/所有权断言，Server结果待新CI。冻结WPF失败为新独立钩子夹具partial App与已引用冻结App冲突及缺构造器；将该独立夹具限定在专用编译符号，独立P0 job仍执行原生产构造器/Dispatcher测试，所有冻结原生界面旅程继续原样编译执行。修正仅测试/夹具，无产品源码变化；四工程提交之后追加验证修正，因已推送/冻结不强推。

69df6bef同源SM1.14.0 Windows介质发布、环境报告与业务身份入口启动通过；单EXE打包通过且未签名、实际安装NOT_RUN。准确介质/安装器hash和失败CI原件摘要见ci-69df-followup.json；不把这些PASS改记后续验证提交，也不将失败CI计PASS。

ba3d8b08/run40222113898：Linux完整既有边界PASS；冻结WPF混编已解决，本机以69df介质+ba3独立固定夹具重跑参数/延后/全屏旅程PASS，FixtureMatchesMedium=false，明确两种来源。冻结CI其余失败在StartupExperience.cs:58用显示名称Single选择RCS，兼容目录保留两个版本使假设失效；改为唯一精确的当前版本descriptor选择，原真实字段/身份断言保留并加强版本检查。首次Windows目录前置修正未解决COM失败，此假设已否证，不算修复。

补充确认原有快捷方式问题：DeploymentShortcuts.cs:45在main与本批字节相同；本机相同Platform DLL、全新目录且目标/中文父目录确实存在，探针只改变本进程manifest，ACP1252仍失败且COM路径变问号，ACP936/65001均PASS。没有改变系统区域设置，未将CI改为中文/UTF8来取代该失败；按用户不混无关改动规则另包待授权。本包CI保留失败门禁，将原COM测试独立列为最后一个always步骤，使P0 Dispatcher与安装守卫也得到实际执行，不删、不跳COM断言。该缺陷对非CJK代码页Windows安装投影仍是风险，不能因P0完成宣布安装链条通过。见ci-ba3-followup.json。

### 最终已执行补证（检查绑定1c769e10，实际CI源32170b66）

run40222910122：Linux完整既有边界PASS；frozen-sportmaster完整job PASS，12项原生WPF/合成核心/内部删除内核及真实SCM授权服务边界摘要均PASS。实际冻结介质与固定夹具同源32170b665eba672b1c091eb6bcc3a05d15a9d109（检查绑定head1c769e109a5b7e2eb2de585f1db4bb0f81c9b07f）；签名及完整实际安装仍NOT_RUN。CI单EXE打包成功，hash73f1a55834283dc67df0cb06e2f75c039668d13d7290ace693c51439a7c5c64e，NOT_SIGNED，独立于先前69df介质。

Windows P0所有选择器、安装变更守卫及单列生产Dispatcher钩子步骤均退出0，之后原有COM快捷方式断言仍失败，job及整次CI仍FAIL，不宣称全绿。实际原生异常退出码-536870892且上下文刷盘；TCP FIN约105ms、RST约31ms、配置2秒入站期限的停止转发约2019.8ms退役Epoch，未重连或重放。P0日志、所有冻结RESULT的来源/范围/hash、构建安装器hash及NOT_RUN见[ci-1c-final.json](../evidence/p0-stability-01/ci-1c-final.json)，原件保留在该run artifacts。

本机旧69df介质+固定1c独立夹具的完整StartupExperience也PASS，FixtureMatchesMedium=false；与同源CI证据分开。后续提交仅把本段及manifest固定进仓库，不修改产品/测试，不把这些PASS改记后续commit。原有COM缺陷另包待授权，未改系统区域设置、未弱化断言。真实计划任务、自启动/UAC、WER转储、SCM故障恢复动作及完整卸载仍按下述VM步骤NOT_RUN；授权服务边界PASS不能替代这些未执行项。

## 隔离Windows VM验收步骤（尚未执行）

1. 使用可回滚的Win11 26100+ VM、指定普通交互账号和全新D盘Simulation根；保留安装前任务、HKLM64 LocalDumps、服务及卸载登记清单。检查空间后用同源SM1.14.0候选安装，确认只有原一次UAC，启动普通权限，不连设备或真实RCS。
2. 核对计划任务SID、InteractiveToken/LeastPrivilege、固定程序路径、默认30秒/3次客户端预算与至少60秒的任务自身重试；注销/登录确认自启动。未登录场景应无交互工作窗口，不用自动登录绕过验收。
3. 在全新Simulation中记录客户端PID、启动时间及原请求；仅强杀该VM测试客户端，计时应在配置延迟加启动耗时内出现新PID。另对监督者强杀，确认任务按配置恢复且接管原客户端，不多建进程。连续异常退出超过阈值后停止拉起，state/alert、任务退出77及普通启动维护提示可见。常规已知循环保护仅在原客户端退出后由已有维护修复入口复位。
4. 强杀不等于WER崩溃转储。另将本次UiVerification输出完整复制到全新隔离harness目录，仅把其apphost副本改名Ipc.Desktop.exe（保留原DLL及依赖），直接运行副本--p0-dispatcher-child <全新诊断根>，不要修改已安装介质。该合成WPF钩子触发真实fail-fast；连续3次后受控dump目录应最多2个mini dump，普通账号写入成功、其他账号不可读；不要附加会截获异常的调试器。记录SourceCommit、退出码、WER策略、dump数量/大小/hash。native SDK原生崩溃仍另验。
5. 只对该VM安装的授权服务执行受控异常结束，核对5秒/30秒/NONE及86400秒复位、非零服务失败也触发；正常停止不触发。复核管理凭据、管道及会话权限没有被恢复动作绕过。
6. 用合成RCS响应丢失制造Unknown，再按第3步崩溃重启，核对原ID/hash/字节、Attempts=1、原Unknown仍在且没有第二次请求。配置Pending只经原受控重启激活，不通过设备重连应用。
7. 覆盖任务更新/数据根迁移、关闭客户端同时卸载、投影中断后修复、创建进程后记账中断等竞争。卸载后本产品任务不存在；最后消费者的拥有WER键/dump及SCM服务不存在；其他产品或外部登记保留。对比SystemEntries与卸载前清单，未知文件/业务数据的保留必须进入回执，不能用全目录强删换无残留。
