# 并发任务整合报告 · 13:09续轮

起始main `9e5b6ab5be9cf70284acdbaa84b84994ca2377e9`；开始北京时间2026-10-20 13:09。前轮#60/#61已完成，本轮单独统计，不把前轮18笔增量相加。当前为调查实施记录；最终回执在本轮实际提交/合并后固定。

## 原任务、成果与证据

以下原用户消息和后继任务记录已实际读取，不从PR标题反造要求。全部源任务查询时idle。11个worktree及所有refs已快照至Git外 `D:/projects/ipc-reconciliation-snapshots/20261020-1309`；独立整合树不覆盖共享工作区。唯一未提交为旧sm-ui四文件，前轮已原字节归档，再次备份；不可把它们替换已完成#62。

|任务|原要求与后继指令|分支/起始HEAD/产物|已有证据与本轮处理|
|---|---|---|---|
|恢复原型设计质感|按原HTML与Impeccable，后明确“减少无关信息，把必要信息做大、做清楚”；再要求所有页面图|#62 codex/sportmaster-clear-workstation / 6e73250f56e09cfa1eaac9f7b8df650bbabdfd4f；3笔，UI/夹具/截图/运行报告；Git外36图图集|0e79e73同版100项原生检查；CI39365283961视觉失败；1条P2校验拒绝误标。图集诊断弹窗失败未造图，需保留并修复实际代码|
|海康驱动|接入已连接相机、按IP配置与左右无关；后要求真实读码测试|#63 codex/hikrobot-reader-01 / cff767b25b9655583d428f49db84170a406bc0bd；3笔，独立驱动/资格化原生边界/自包含修复/签名包规范/探针|ef55327冻结、26项驱动/5组政策及CI39367874458通过；真机连接释放，0帧0码，真实读码未证实；保留不把相机连接当读码通过|
|更换软件图片和名称、单EXE|用户更正名称为“菜鸟供包台”，明确允许图片及图标公开；后明确仅一个可双击EXE交付现场|#64 codex/cainiao-product-brand / 7bbc1b84574f8e4c9be2374132c68e956f401512；7笔，品牌图/图标、准备启动器/封装器、原生证据；本地172MB EXE|a7de058单EXE原源码、27准备/5封装/17原生；原品牌38项；CI39372834903视觉失败。新组合不可沿用旧EXE身份|
|简化无人值守安装设计|删除无人值守和批量要求，收敛普通安装包/必要说明，检查类似过度设计|#65 codex/simplify-installation-docs / 37a46cee9a66838aac099f7fcad3c9dc9a24ed40；1笔，13个文档/清单文件|11文档/336引用、本轮前CI39371238086通过；普通人工安装与后续单EXE要求兼容，合并时统一主入口|

四PR均非Draft，base为本轮起始main；共同基线/全部独有历史已核。源PR起始唯一提交集合14笔（含#62一次整合merge），不能用各branch tracking ahead相加。现有更早商业化/发行/架构成果已在起点，不重新计入新增。

## 裁决与验证边界

先治理文档，再编码；[本包](../work-packages/WP-IPC-PARALLEL-TASK-RECONCILIATION-20261020-02.md)及[编码前裁决](../design/parallel-task-reconciliation-20261020-02.md)是当前实施入口。旧报告/PDF、原始失败、D/RV和前轮收窄保留原形成时身份。本轮最终覆盖/验证/提交与合并回执逐项追加，不以无Git冲突或单PR局部PASS作为完成依据。

## 调查补充与实际冲突

#65已实际合并，merge `288ca6fed7bb2cd788675464f47bb04ae6f749e0`。#62原P2已以bf3f68f修正并解决评审线程；进一步a0ee213修复最小窗口与合成旧schema夹具。初始CI #62失败为1028 DIP客户区无法容纳1100目标，#64失败为定时刷新覆盖显式投影；原失败日志/结果收于[原#62失败结果](evidence/parallel-reconciliation-20261020-02/initial-pr62-RESULT.json)。尝试调整CI显示模式因无可枚举模式失败，未计PASS；后改测试专用原生窗口尺寸处理，产品守卫不变。

原报告标明的classic最小窗口操作越界已实际复现并修复；保持紧凑状态区局部滚动及原件明细，默认UI在登录和50条长原因下也保留最小窗口条码/格口/操作。开发扩展原生200项PASS（a0ee213内容），不是现场通过。业务状态机/发送器/存储迁移未改。

[36张原生历史图集](../archive/sportmaster-native-gallery-20261020-0e79e73/README.md)与原导出器已取回，逐图SHA256相符；35PNG+1JPG。排除临时数据库、机器私有输出及构建缓存。5份既有PDF保持Git字节和原基线。

执行期间品牌源任务被新用户消息唤醒，新增107b82e（文档）、de105a5（安装选项/窗口衔接）及d10c51a（#65合并）。新用户原文要求安装目录与两个快捷方式选项，以及修复安装后遗留技术窗口；已读记录与源码，不从标题推断。初始快照之外再次保全Git外refs.bundle、暂存/未暂存补丁和冲突文件；已发支持的任务消息请源任务停止写入并交接。该后继成果纳入本轮，旧EXE不重标。

组合中间点2b216de（尚未包含上述新安装选项）已构建0警告/错误、驱动26项及5组政策、安装准备27项与封装5项PASS，原stdout保留；完整最终冻结验证另记，不能据此宣告最终通过。首次封装测试用错discover文件名导致0项/exit5，已用test_installer_package.py重跑5项PASS，不把空运行算通过。

## 已确认要求与最终实现位置（结果详见后表）

|确认要求|最终实现/来源|验证结果及其边界|
|---|---|---|
|Platform、Application、Driver、Component四类职责；一业务一实例、多页共享|既有framework/architecture22；ParcelRuntime、各独立Flow、Presentation；#62/#63不新建通用流程|源码对照；原生共享Application、辅助窗口/关闭重试断言|
|Sportmaster左右reader隔离，扫码到WCS格口，无PLC/图片展示或长期存储|SportmasterFlow、ParcelBindings、SupplyStationPosition；#62/63|双位延迟/Unknown核对、原请求归属、观察/准入拒绝/格口失效原生及核心冻结回归|
|DirectPlc、WcsToPlc独立单相机单投递位；DirectPlc UI延期|原独立项目/Flow及发布选择，源码未改变边界|源码核实；本次不重复现场测试/扩展开发UI|
|可选认证/称重/上传组合；业务语义在Application|原能力factory/ports/独立流程；#63 reader仅IP和SDK观察|factory原资格、驱动26断言及5组签名政策；Production缺资格拒绝|
|恢复/持久化/配置激活不回退|原DB8、schema3、ABI1.10.0、ParcelTargets、Startup/ExtensionStore；#63精确扩展边界|核心冻结恢复/防重/旧请求/配置/生命周期，UI失败不清原件|
|精简放大专业双位UI且设备诊断可达|#62布局、SupplyStationViewModel/Diagnostics、ShellWindow紧凑状态区|原生最小客户区、长内容、诊断OneWay、拒绝区分、焦点/选择/滚动/草稿|
|驱动与左右无关，只配置IP|#63 Ipc.Drivers.Hikrobot及原机级资源租约/资格化装配|同触发多帧/同码新触发/旧epoch/队列满/Stop失败/健康失败；真读码仍缺证据|
|菜鸟供包台名称及获准公开图片|#64 ProductBrand、cainiao.png/icon及桌面/安装资源|同源发布资源和实际原生启动；原图片/图标来源许可见源报告|
|一个可双击EXE、可选目录和两个快捷方式、完成正确关闭/启动|#64 Ipc.Installer、原StartupSetupWindow/ParcelStartupServices/DeploymentShortcuts、后继de105a5；整合767bf30保留已改指向入口|准备27/封装5、选项18及实际临时COM；最终EXE黑盒与真实安装另列，不能借合成完成页通过|
|仅普通人工安装，不要求无人值守/二次选型|#65文档及本轮现行操作入口|逐项文字/链接/manifest核实，保留高级已有机制和数据保护|

源任务已确认暂停写入并交接，新增三个源提交未推送的成果已保全并合入#64；d10c51a遗留manifest.sha256冲突标记及新代码记录缺失已由整合重建，未在远端合入失效清单。后继包装继续在独立目录进行，Git/PR由本整合任务统一负责。

## 最终组合证据与边界

实现冻结先为 `b33fd4972a9c3e582a06db4a88a0a1431212d092`，随后仅对安装完成页提示作3行修正形成 `12946f5beee51d448c44b1bfbb0669178d57eaf7`。正确运行账户在父页面忙碌禁用时不再被误标；实际账户守卫、helper、数据库和业务流程未改变。9项合成原生回归覆盖正确账户/错误账户、父禁用、目录与选项返回保留、完成不启动；[原日志](evidence/parallel-reconciliation-20261020-02/completion-parent-disabled-regression.log)。第一探针因未向所有项目传递BusinessModule使用了错误模块，修正命令后通过；原错误保留，未为此修改产品。

|证据|精确范围与结果|
|---|---|
|组合开发构建|b33实现；Sportmaster构建0警告/错误；[233项原生完整回归](evidence/parallel-reconciliation-20261020-02/final-development-native-233.log)，真实WPF/Dispatcher/HWND、合成身份和端口，含两类UI、辅助窗口、原请求/恢复、旧schema草稿/关闭、长内容、最小视口及原生backing 1600×900；不是物理触控/多屏资格|
|采集与扩展边界|同一组合Platform，[26项驱动断言及5组签名/ALC原生政策](evidence/parallel-reconciliation-20261020-02/final-driver-tests.log)通过；生命周期、背压、epoch、防重、旧回调/Stop重试和SDK缺资格拒绝；真实读码与现场未验|
|安装准备/封装|[27项准备](evidence/parallel-reconciliation-20261020-02/final-installer-tests.log)、[5项封装](evidence/parallel-reconciliation-20261020-02/final-packager-tests.log)通过；原故意末尾空格拒绝输入保持，不删断言；原18项快捷方式/序列化/COM及不同目标保护保持|
|受影响公共入口|DirectPlc与WcsToPlc按各自已有锁文件执行Locked构建，[DirectPlc](evidence/parallel-reconciliation-20261020-02/final-direct-plc-locked-build.log)/[WcsToPlc](evidence/parallel-reconciliation-20261020-02/final-wcs-to-plc-locked-build.log)均0警告/错误；不新增DirectPlc完整UI、不声称真实PLC通过|
|真实b33 Windows安装|[26项实际E2E](evidence/parallel-reconciliation-20261020-02/b33-real-RESULT.json)/[完整原日志](evidence/parallel-reconciliation-20261020-02/b33-real-E2E.log)与[已安装工作台](evidence/parallel-reconciliation-20261020-02/b33-real-actual-workstation.png)：从单EXE经真实文件夹选择、目录/快捷方式选项及返回、原runas helper/受保护登记、真实COM读回、Finish启动、原首配保存、双位Simulation工作台及两快捷方式重启，进程均正常退出。原探针Name误定位失败保留，改用原AutomationId后通过；原夹具SHA4b9434…单独保全。没有反射安装成功旁路|
|资料身份|以Git索引原字节、UTF-8及Pillow核验；修复初始新增29项（遗漏清单、删除SVG残留、原图集绝对链接和失效目录链接）。[精确错误集合对照](evidence/parallel-reconciliation-20261020-02/archive-delta-fixed.json)：起点与129候选同为267项（265个原PPTX媒体类型、2个原元数据标记），新增/移除均0，全树仍FAIL，不伪称全PASS、不新增资料豁免|

其中b33原EXE 172487752字节、SHA256 `e7295de46aeffd236dd29e2dcc2344b5255a7fb35f0ae3219d5bb8e9e9b39c19`；原冻结ZIP SHA256 `d192a88e49d2d735ee93b7dbae1581ed1d4addd60c69960e0a0a5af8893d354b`，库存748项、业务1.4.1。此旧组合已真实安装，但不能重标为129修正后的最终包。两个版本的实际验证分别记录。所有EXE未提供Authenticode签名；散列仅证明字节身份。

本轮所有真实安装仅限本机独立Simulation配置；没有真实海康帧、真实双相机触发、一触发一实体证明、客户WCS/RCS/PLC联调、目标现场节拍/长期故障/混合DPI/物理触控资格。Production继续缺精确SDK/组件/合同及独立部署资格时拒绝业务。原连接0帧记录和历史D/RV不改写。安装/升级后独立模拟数据及受保护登记保留，不通过卸载清理证明成功。

## 保留、归档与公开范围

36张0e79e73图集及原导出器、旧sm-ui四文件、初始README/INDEX、各任务代码/测试/配置/报告和原始失败均有索引。原Markdown含本机绝对链接，原字节存为 `.md.txt`；浏览版仅改相对图片引用。5份既有PDF：architecture-quality-audit-02、audit-reconciliation-01、commercial-quality-audit-03、project-conformance-audit-01、ux-commercial-quality-audit-01，Git blob与起点完全相同；不改原基线/结论。

真实安装的INSTALLATION.json、账户SID、实际选项/摘要/完成截图中的私有目录、厂商SDK、签名私钥、机器配置、数据库、缓存/中间输出不公开。完整原件保留Git外任务产物目录。EXE和ZIP为独立交付文件，Git中保存精确身份及可重跑脚本，不将172MB运行包塞入源码提交，也未擅自发布新GitHub Release。用户已明确允许公开的菜鸟图片/图标保留；iFinD参考资产及未授权客户原稿未纳入。

## 实际合并与统计截止

|PR|起始HEAD|实际merge提交|结果|
|---|---|---|---|
|[#65](https://github.com/yuan2go/importer-pc/pull/65)|37a46cee9a66838aac099f7fcad3c9dc9a24ed40|288ca6fed7bb2cd788675464f47bb04ae6f749e0|merged|
|[#62](https://github.com/yuan2go/importer-pc/pull/62)|6e73250f56e09cfa1eaac9f7b8df650bbabdfd4f|6415bb873d12ca5d759e4ced432d0f26f7fdecc9|merged；最终HEAD a39370d，CI39375343162通过，P2已修正/解决|
|[#63](https://github.com/yuan2go/importer-pc/pull/63)|cff767b25b9655583d428f49db84170a406bc0bd|cf1450e30f38057ab15faa62e36607cc31ee83a1|merged；最终HEAD646e8b4，CI39375379388通过|
|[#64](https://github.com/yuan2go/importer-pc/pull/64)|7bbc1b84574f8e4c9be2374132c68e956f401512|97c73e73b21b2439f95186d7468bb0d0374b6d6e|merged；最终HEAD36dc715，CI39375420095通过；含执行中新任务|
|[#66](https://github.com/yuan2go/importer-pc/pull/66)|b33fd4972a9c3e582a06db4a88a0a1431212d092|以GitHub实际状态及结束回执核对|本整合PR；后续完成页提示、文档/证据追加均在此PR|

顺序65→62→63→64→66。每个源PR在预期HEAD上核对CI与评审；#62→#63及#63→#64每次最新main均以`git merge-tree --write-tree`证明预期组合树等于已检查的源HEAD树，没有旧分支回带问题。main当时无保护/必需审批和必需检查规则，但仍完成现有Windows工作流；没有绕过保护、强推、squash/rebase或用关闭代替合并。

本页统计截止为整合分支 `12946f5beee51d448c44b1bfbb0669178d57eaf7`（不含其后的本页证据提交和最终PR合并），当时远端main `97c73e73b21b2439f95186d7468bb0d0374b6d6e`。唯一集合：源任务17笔（起始14+执行中新3），本轮新建内容9笔/合并16笔，起始到截止分支共42笔。完整SHA及分类见[截止提交表](evidence/parallel-reconciliation-20261020-02/commits-at-report-cutoff.json)。所有原提交原样保留，1:1进入最终历史，无重写映射。

结束时须在最后一次报告提交、推送、实际合并后重新读取origin/main，以 `git rev-list --count 9e5b6ab5be9cf70284acdbaa84b84994ca2377e9..<最终main>` 重算唯一总数，并核验GitHub全部PR merged、成果包含、最终运行源码差异和公开文件身份。最终完整SHA、最终数量及本PR合并回执在结束回复中给出；本页不预造尚未发生的合并SHA。

## 最终交付补充 · 12946f5真实升级完成

最终EXE：`D:/releases/cainiao-final-12946f5/菜鸟供包台安装程序.exe`，**172487240字节**，SHA256 `9c4f084ea903970183179619462f4e14ab68cf4c7ced3ea1be2e2772df3b0ad1`；目录中仅此一个文件。介质ZIP SHA256 `e6c32e5b8fe0b09d5dd88263946e240592107a413f4d54ee594a558ec387697d`，同源12946f5、业务1.4.1、748项库存，启动器和内嵌介质来源相同，Authenticode实际NotSigned。

[最终真实30项结果](evidence/parallel-reconciliation-20261020-02/final-real-RESULT.json)、[原始升级日志](evidence/parallel-reconciliation-20261020-02/final-real-E2E.log)、[字节/源/夹具身份](evidence/parallel-reconciliation-20261020-02/final-real-delivery.json)、[真实已安装工作台](evidence/parallel-reconciliation-20261020-02/final-real-actual-workstation.png)。已读取原始日志、核对源码/夹具/EXE/ZIP散列并查看公开图：用户在系统UAC授权后，原预检/安装helper成功；保留原种子、唯一配置账原字节及旧版本/回执，原安装计划创建一个完整升级前备份并逐项核对DB与全部恢复伴随文件hash；登记切换正确，所选两个快捷方式目标正常启动且无--setup，完成页不再误报账户，Finish和两快捷方式三次均进入真实Simulation双位工作台，正常关闭exit0。b33干净安装26项和129升级30项分别证明各自版本，不把升级冒称129干净重装/卸载通过。

最终冻结宿主Platform DLL还由实际win-x64自包含Probe执行[26驱动断言/5组原生政策](evidence/parallel-reconciliation-20261020-02/final-frozen-self-contained-policy-tests.log)全部通过，[精确合同身份](evidence/parallel-reconciliation-20261020-02/final-frozen-policy-identity.json)。这是显式合成SDK/签名输入，未加载现场SDK或接入设备；证明组合后的自包含框架/公共合同边界未回退。

本轮必要验证的状态：受影响工程构建、233项组合原生回归、完成页9项回归、驱动26/政策5（含实际冻结自包含）、准备27/封装5、选项18、b33干净安装26、最终129真实升级30均PASS；现有源PR和本整合实现CI均PASS。原PPTX/元数据历史267项仍FAIL；真实读码、客户服务/PLC、目标现场长期/物理/多屏与Production资格NOT_RUN/NOT_APPROVED。当前机器保留129的独立Simulation部署，测试进程已退出，未卸载或删除原数据。

收尾复查12个共同仓库worktree；除本整合待提交证据和原sm-ui四文件外均clean。原sm-ui草稿及忽略目录图集已保全，后继安装任务在独立clone/输出目录执行且已idle，未写共享树。全部Open PR查询只余#66；4个起始PR均GitHub真实merged，没有新增遗漏PR。无法取得的旧私有客户原件许可、另一版本设计原字节及独立真实SDK/现场资格作为证据缺口保留，未声称找回/验收。

最终完整资料候选核验4490文件、2399引用，267项错误集合与起点完全相同；[公开校验回执](evidence/parallel-reconciliation-20261020-02/final-public-validation.json)。原sm-ui四文件与前轮已归档原件逐字节相同；5份PDF原blob身份见[清单](evidence/parallel-reconciliation-20261020-02/preserved-pdfs.json)。最后报告提交后还将以不可变HEAD再核验并复查最终远端main，结果在结束回执给出。
