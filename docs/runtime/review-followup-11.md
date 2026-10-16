# REVIEW-FOLLOWUP-11：两个P2与Product组合所有权闭环

2026-10-17（Asia/Shanghai）；起点main `4ae5319c264f8cd0b2152d5d7242bb1ca39b65e5`（PR9已合并），开发分支`codex/wp-ipc-review-followup-11`。[工作包](../work-packages/WP-IPC-REVIEW-FOLLOWUP-11.md)、[计划](../superpowers/plans/2026-10-17-ipc-review-followup-11.md)、[完整checks](review-followup-11-checks.json)。**最终运行源码SourceCommit：`4edb909e71097a7da61506beaa10ae5db82ec6cb`**；后续报告/manifest提交不冒称二进制源码。

两个P2：**SOURCE_CORRECTED / SHARED_POLICY_PASS / WPF_BUILD_PASS**；Product组合层三故障：**PASS_REFERENCE**；E07、E08及持续双箱旅程：**PASS_REFERENCE**；portable解压启动/维护冒烟：**PASS**；win-x64交叉发布/字节回读：**PASS**。Windows环境 **BLOCKED**、W01～06 **NOT_RUN**，真实B01～06 **BLOCKED**；实际WPF截图0张，无现场准入。

## 权威与原证据

先核对最新main、主工作区及并行PR，复用干净隔离工作树建立分支；启动时无开放PR。主工作区三组未跟踪受限资料不动。业务修改前以`9aded34`保存11份相关原版本；README修改前另从同一main基点取回原字节，[最终12份原版本](../archive/pre-review-followup-11/README.md)及Git blob/SHA均保留。旧a080825报告、checks、STATIC_PATH_CLOSED/PASS/NOT_RUN及包保留形成时含义，新结果不倒写旧验收。

已复核v1.1 README/GALLERY、完整交接、原HTML、分离HTML/CSS/JS与tokens，并被动查看错波次拒绝和1366拆包原PNG。原设计、资产、schema/checksum、请求fixture/协议、共享业务及D/RV没有修改。不编造SDK/WCS合同/数量/身份批准；本轮仅合成参考授权，无生产WCS写入或真实设备动作。

## 两个P2

|问题|实际修正|验证边界|
|---|---|---|
|P2-01 波次差异被折叠|LaneControl当前Item波次移出Expander；已知原值不同时固定结果区并列显示入场箱/Item波次及差异，ReviewFits检查差异摘要矩形。ViewModel使用WaveComparison，未知保持未知，前导零不变|共用实际展示策略和XAML祖先结构检查PASS；没有新增接受规则。WPF长字段/几何仍NOT_RUN|
|P2-02 新箱扫码缺焦点交接|每窗BoxEntryFocusPolicy按有效身份、可扫资格和箱变化产生一次交接。排队回调再查ticket、身份、箱、忙态、投影、抽屉、区域和控件；Focus后SelectAll保留草稿，下一扫描替换旧值；相同投影不重复夺焦点|策略覆盖首次登录、新箱/拒绝、身份失效、延后、重复刷新、多窗独立及A→B旧回调竞争。Windows实际焦点/扫码/Tab仍NOT_RUN|

PresentationPolicy是Desktop实际调用的纯展示代码，以源码链接编入既有Verifier；没有新框架或第二套演示UI。测试未实例化WPF/ViewModel。运维中残留的“单通道核对、800×420”当前说明已改为实际双通道、1280×680保护和准确待验边界，前版本字节已保存，设计基准不变。

## Product组合层三故障

[完整动态记录](evidence/review-followup-11/product-ownership.json)通过内部来源工厂进入真实Product启动/Stop/Dispose。公开Host/CLI仍用ReferenceProduct注册工厂；模式/配置/最终租约校验均先行。没有配置注入开关或私有字段反射。

|故障|未终止时|终态|
|---|---|---|
|Stop抛错|原异常保留、源注册仍归属；不Dispose该源，独立源照常清理|显式解除故障，再成功Stop并恰一次Dispose|
|Stop任务不终止|5秒等待超时，同一Stop任务复用，不并行Dispose；独立资源清理|原任务晚完成后恰一次Dispose|
|Dispose任务不终止|Stop完成但原Dispose仍活；依赖保留，不重复Dispose|原Dispose晚完成后最终释放|

每类都实际断言：Product持有store/journal及数据根、最终站点/设备全部租约；同数据根第二Product和不同工位/数据根争设备均不能建源；来源回调入口已封闭，独立来源Stop/Dispose各一次。在Product.Dispose尚未完成时，经原ReconcileAsync查询真实独立接收账的丢回执请求，SQLite实际保存Accepted，journal实际追加T3Response/ReconciliationQuery；Stopping继续阻断，原payload/hash/Binding及单Attempt保留。晚完成后重试Dispose，来源恰一次释放、各键可再取得、lease路径不删除、公开注册工厂重启同库，原请求远端发送仍恰1次。

这补齐上一版Product组合层只有静态检查的验证缺口；不证明真实SDK缓冲、回调注销或设备端控制权。原始日志/三故障数据根/28份合成DB身份与SHA在checks登记。

## 定向验证与失败

实际SDK10.0.402，macOS ARM64。锁定还原及整个solution含WPF交叉构建 **PASS，0警告/0错误**。

- [E11定向日志](evidence/review-followup-11/target.log)：实际共享展示策略、固定波次结构、Product三故障PASS。
- [E07回归](evidence/review-followup-11/closure.log)：当前输入失效、来源/代次/健康、历史、提示队列及独立清理/超时PASS。
- [E08与旅程](evidence/review-followup-11/followup.log)：八入口身份矩阵、最终配置/租约、ABA/失败保旧和持续旅程PASS。
- [首构建失败](evidence/review-followup-11/first-build-failure.log)：新焦点回调CS8602；修成一次非空snapshot匹配，再完整构建通过。没有关闭警告或吞异常。
- [首资料校验](evidence/review-followup-11/first-archive-failure.json)发现两份新JSON的本地包路径命中元数据规则；改公开副本为相对包位置及WORKTREE占位，原ZIP/原字节SHA保留，校验器未放宽。
- 双轴只读复核发现1项旧A回调重排B的竞争，已加generation/ticket/IsCurrent/Retry及A→B回归；冻结源码Standards/Spec最终均0开放。审阅是静态证据，运行结果另列。

完整默认新增E11，旧选择器/断言未削减。Supply/schema/发送/事务未改，因此不重复未受影响的迁移/T1/T2/T3全量；旧a080825全选日志和PR9独立--data证据保留其SourceCommit。本轮使用关联E11/E07/E08及发布冒烟，不跳关键风险换PASS。[资料工具7测试](evidence/review-followup-11/archive-tests.log)PASS，资料总校验单列。

## 同版持续旅程

[完整旅程](evidence/review-followup-11/reference-journey.json)实际完成启动/身份/验箱→双通道→拒绝/纠正→左2件接受/独立离场复位→右8/4/4拆包→丢回执Unknown→重启→原请求核对→累计6件→右离场复位→关闭/新箱零计数/旧箱历史→再关闭重启两箱保留。

工位`SIM-090a404b8509414fa82a244cd7ed1d19`，丢回执请求`d2f6f75a6a8442b78e8916b0b28444d7`。实际独立接收账发送1次、Attempt1次，payload SHA匹配，唯一接受事实2条共6件。业务接受不等于实体完成，Unknown不换ID重发。原DB/备份留在checks登记的合成根。

## 新参考包

Release=`REVIEW-FOLLOWUP-11`，Storage/MinimumReader=`3/3`，Contract=`SIMULATION-v1`；两包SourceCommit均`4edb909e71097a7da61506beaa10ae5db82ec6cb`且WorkingTree=false。

共同运行/配置/脚本/构建/solution/随包运维输入SHA：

`91036e12987b33656ebee458337de3d0b1ee85f9dd37f20b199f6092a02a80fb`

|包|本地位置|ZIP SHA256|
|---|---|---|
|portable，依赖.NET10运行时|`.artifacts/review-followup-4edb909-portable.zip`|`f38ad91a43b8244a0e25ca2581821d72a7c231baa0707212b2e82e925e7e99a1`|
|win-x64，自包含WPF/CLI/夹具|`.artifacts/review-followup-4edb909-win-x64.zip`|`a1d9a00ef06affb87def7598b2f29d81de61b4deac0dafdee9c358dee24b05b5`|

[包回读](evidence/review-followup-11/package-readback.json)：两ZIP实际解压，CRC、精确文件集合、每条SHA256SUMS及注册模块实际字节匹配，分别130/815清单文件；共同source指纹独立重算一致。RID二进制/配置SHA分别记录，不冒称不同RID同二进制SHA。

[portable解压冒烟](evidence/review-followup-11/unpacked-smoke.json)七项PASS：启动、第二实际进程拒绝、备份/预检/隔离恢复、诊断导出、不可写存储、模块篡改、Production拒绝且不调用合成接收端。win-x64没有执行EXE。新包仅存当前隔离工作树.artifacts，不写入源码Git；旧包保留。

## 阻塞与待验

Windows环境BLOCKED、W01～06 NOT_RUN、截图0张：DPI/双通道六状态与长字段、双窗草稿/焦点、真实扫码/确认、声音触控、普通账户ACL/跨会话/PowerShell/路径/维护、故障绘制/关闭恢复。没有拿HTML原图或策略测试替代。W01还须测固定差异摘要高度、1280×680及1366拆包ViewportHeight/必核矩形；首次Loaded前不足区域的暂停锁时序仍是待复现条件风险，本包未宣称已修复或已验收。

真实B01～06 BLOCKED：旧工程/SDK回调合同、WCS副作用/幂等/查询/认证、实体映射/准入离场权威、数量拆包规则、真实身份/SOP、现场硬件/容量节拍/验收和资料许可。[05](integration-05.md)与[06](windows-06.md)缺项不变；没有新增批准人，D/RV不关闭。

资料总校验继承FAIL267：原PPTX扩展名与Canva标记，最终比较结果见checks；不改基准原件、不删断言或扩大豁免换PASS。资料工具测试与应用测试分别报告。

## 复现

从本报告SourceCommit检出，使用global.json SDK、全新输出/合成根；详见[运维](operations.md)。

```sh
./scripts/build.sh
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --review-followup
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --closure
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll . --followup
python3 scripts/package-reference.py --target portable --output .artifacts/reference-11-portable
python3 scripts/package-reference.py --target win-x64 --output .artifacts/reference-11-win-x64
python3 scripts/smoke.py --product <新解压目录>/app --fixture <新解压目录>/fixture/Ipc.Fixture.dll --output <新结果.json>
```

公开日志仅替换本地工作树/SDK路径；原字节与公开副本SHA分别登记。4edb909后运行/打包/运维输入无变化。仅开发分支PR供main审查，不自行合并、不强推；实际PR回执随后附加。

## 开发分支PR回执

已创建[main PR #10](https://github.com/yuan2go/importer-pc/pull/10)，创建时HEAD `bf1c9e82038baaca6c5fbd32de1796fa476c6e58`、base `4ae5319`，读回OPEN / MERGEABLE / CLEAN。GitHub检查列表为空，不等于CI PASS；本报告PASS来自真实本地日志。后续仅本回执/计划/manifest文档提交，运行SourceCommit和两个包仍固定4edb909。未自行合并、未强推；最终远端状态以GitHub读回为准。
