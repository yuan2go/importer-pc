# PR20 / PR21 协调整合 · 2026-10-18

用户本轮授权修复、复核、合并两项PR，取代两个工作包形成时“只交PR、不合并”的本次交付限制；原工作包和历史验收保持形成时事实。开工main `08286542bbd007433d85be2cb8d129b366b007e0`，PR20 `ecdbddc6640249347c7821cf3b1e58ccd7c22f83`，PR21 `e49249c2a5148544cdc5b7d8bf0a74a69c015026`。主目录未跟踪资料保留，隔离工作树顺序执行，不操作真实设备。

## 设计与执行计划

- [x] 读取最新main入口、framework/decisions、两个工作包与实际评审线程；以原请求、接受事实、独立回执责任和资源生命周期为不变量。
- [x] PR20链接路径负例先红灯；备份对源目录及目标所有已存在祖先拒绝符号链接/重解析点（含Junction、悬空链接），覆盖新父目录场景。普通外部路径仍允许。维护者须提供普通路径并保护祖先目录权限；检查不声称抵御有权限并发替换祖先的攻击。macOS的/tmp、/var等系统别名也受此规则约束，使用实际路径。
- [x] PR21真实提供者ProtocolEvidence由Python构建器与C#签名包验证器强制64位ASCII十六进制；协议证据与资格文件比较统一忽略十六进制字母大小写，保留原签名文档和包身份，不改签既有包。
- [x] 合并逐文件核对Commands/Guards/Runtime/Stages/Recovery，保留20的健康预算/取消、恢复清单和21的独立重量/图像守卫、Production资格与迁移；必要交叉用例及解包进程旅程验证。
- [x] 完成下列定向验证与独立双轴审查；正常合并次序为PR20→main进入PR21→PR21。实际合并提交与远端main以本页末尾及GitHub回执为准，禁止强推/绕过保护/自动合并。

已复现：新增BackupAsync实际入口回归在PR20原源码接受指向运行根的外部目录链接，返回BackupCreated（FAIL）。修复源码`1b6962e1909e1a33967fce8902eb001bec341d6c`定向构建0警告/0错误；同一--backup-health-recovery全组PASS，包含直接/嵌套/悬空链接拒绝、普通外部目录成功、原Unknown上传备份恢复、健康超时和取消、PLC接受事实保留。中间一次失败是悬空链接测试清理误用Directory.Delete，改为File.Delete，保留原失败日志。证据见[evidence](evidence/pr20-21-integration/README.md)。Windows Junction测试分支已加入，当前主机为macOS，实际Windows仍NOT_RUN。

## 验证边界

Windows执行/UI/UAC/ACL/Junction：NOT_RUN。真实SDK、秤/图像实体关联、WCS/PLC握手与现场验收：BLOCKED。生产资格的合成签名测试不构成批准；缺必需证据继续NotReady。原REQ/BR/CAPI/D/RV、SQL/checksum、历史失败和原包身份保留。本页下方记录最终源码及验证；合并回执独立于Windows/现场资格。

## 冲突逐项核对

|交集/相关闭包|合并决策和必要验证|
|---|---|
|ParcelCommands|保留21的资格/重量期限快照、安全取消原WCS/PLC意图、ImageReleased；保留20的新建外部备份、原件校验、完整清单、Flush/hash及祖先链接拒绝。|
|ParcelGuards / Runtime|21独立WeightPending/Expired、MainTerminated、CanSendUpload及Production准入保留；启动/巡检仅把健康调用换成20的ProbeHealthAsync，不恢复旧总状态守卫。|
|ParcelStages|20健康独立预算、停机发送前不制造尝试、发送后Unknown、已接受PLC查询失败保留原事实；21附加上传守卫、CompletesOnSubmit物理事实、重启零尝试Prepared原意图均保留。|
|ParcelRecovery（手工冲突）|先执行20完整文件集合/安全路径/哈希检查，再读配置；模式采用21的Enum.IsDefined，保持原Production而不降为Simulation。新合成恢复用例证明缺真实资格仍拒绝启动。|
|ParcelImages / Repository / FlowDefinition / 各Application|21图像释放先持久后删、未知原件保留、WeightValidity schema2和追加SQL2、LegacyUnsettled拒绝升级、WCS接受/格口/PLC意图同T3原样继承；这些文件与21原HEAD逐字节相同。|
|客户端 / 配置 / 打包脚本|21的Pause/Refresh独立Busy、只读DeviceId、protect-release/provision依赖及schema2配置原样继承，本轮不修改页面。Windows实际仍NOT_RUN。|
|INDEX / 19运行说明 / 测试入口 / manifest|当前入口同时指向两项增量；19运行说明两方增量并存；保留两个测试选择器；manifest取完整路径并集后按实际字节刷新，不取单方清单。|

自动文本合并文件也已对两项原HEAD核差异；没有用整文件选边覆盖。原SQL1、原签名包/历史证据、客户端页面及配置保持21的完整结果。参考提供者显式Simulation身份不改；不声称现场或客户合同批准。

## 最终源码与验证结果

最终运行/打包源码 **d5c8b7981ba8e4c8b9daf22df1cb686243815f3b**；[逐文件冻结输入](evidence/pr20-21-integration/frozen-source.json)与[检查记录](evidence/pr20-21-integration/checks.json)。后续只更新报告/manifest/合并回执，不改该运行闭包。候选仅本地保留，没有发布为已验收Release。

|范围|证据与结论|
|---|---|
|三个原始问题|实际BackupAsync链接别名先FAIL后PASS；Python真实协议证据负例先FAIL后PASS（14项工具测试）；实际签名ZIP格式负例、大小写资格文件先FAIL后PASS，篡改/过期/错误配置/未批准协议继续拒绝。|
|独立审查发现的同类遗漏|首次审查发现Create持有证据阶段仍做大小写敏感比较。实际安装新合成包、签名独立资格记录，StartAsync→Create真实路径复现FAIL；修复后完整认证/采图/实测/WCS/PLC原查询激活旅程PASS。[负例](evidence/pr20-21-integration/logs/acquisition-red.log)、[最终激活](evidence/pr20-21-integration/logs/final-qualified-activation.log)。|
|共享恢复/资源/客户端|5af8d2c源码下--backup-health-recovery、--reliability-closure及AC17资源/恢复组PASS，覆盖Production模式恢复仍拒绝缺能力、未知上传原件、重量/采集守卫、安全取消与重启、实际ViewModel暂停/刷新、SQL1拒绝未结升级及SQL2、WCS格口和原PLC意图原子恢复。|
|最终增量必要复验|d5c8b79只再修改Create证据哈希比较；定向签名格式/资格、实际资格获取激活、AC19能力组、三选装发布闭包及解包进程重新验证PASS。未重复18的24组合、无关Supply/Inspection全矩阵或Windows交叉构建。|
|发布解压启动|最终d5c8b79三个portable ZIP实际构建、独立解压并验证SHA；三默认主链，以及WcsToPlc前置/附加上传两条独立HTTP接收进程旅程，共5条PASS。原请求/原操作人、未知上传查询、单次外部执行、物理完成与业务接受分别核验。|
|负证据及候选|保留初始错误、夹具契约注册遗漏和macOS临时别名测试失败。初版5af8d2c三候选因Create遗漏而被替代，原hash/字节本地保留，不能作为最终候选。|

复验使用global.json固定SDK10.0.402：

```sh
dotnet build tests/Ipc.Application.Verification -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --review-regressions
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --backup-health-recovery
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --reliability-closure
python3 -m unittest discover -s scripts -p test_extension_build.py
```

--qualified-capabilities沿原--signed-capabilities的两个参数（新签名包、独立测试信任文件），额外在临时根生成独立TestOnly合成签名者/新包身份与小写资格证据；原输入包不改、私钥不入库。通过实际Create仅证明合成Simulation资格获取路径，不是Production批准。全部命令/日志及实际源身份见[证据入口](evidence/pr20-21-integration/README.md)。

## Standards

首次独立审查：1项P2，ParcelCapabilityFactory.Create获取阶段仍区分大小写；已用真实StartAsync失败回归锁定并修复。修复复核：未发现进一步具体问题；资格预检、协议关联与获取比较一致，签名原文保持不变。配置/合同/包身份仍遵守既有精确身份契约。本轴为只读源码审查，未新增Windows/SDK/现场测试结论。

## Spec

首次静态审查没有具体冲突，但漏看上述二次比较；修复后的独立复核确认0项新增问题。新资格回归实际经过安装、StartAsync、Create、提供者激活，独立签名绑定配置/合同/精确包/前置声明/小写文件哈希。原包不改，新包使用独立ID、发布者、签名及输出；TestOnly签名与Simulation明确，不代表生产或SDK资格。

两轴最初发现数量分别1/0；已修复1/0，最终未解决0/0。双轴不互相替代，Windows/SDK/现场外部门禁继续开放。

## 资料与公开范围

本轮资料校验仍为FAIL：265项原PPTX媒体类型问题、2项原UI元数据标记，错误集合与PR21原archive-checks完全相同，新增文件/hash/链接错误0。未删原断言/资产或修改历史FAIL来换PASS。[实际输出](evidence/pr20-21-integration/archive-checks.json)。只提交获授权通用源码、合成数据和本轮证据，未上传客户原稿、SDK、生产数据、签名私钥或候选二进制；原主目录未跟踪资料保留。

合并次序和保护规则按实时GitHub复核；本报告不把“候选已验证”等同“已合并main”，最终回执由GitHub PR mergeCommit及main提交共同确认。

## 合并过程回执

PR20已按普通merge-commit流程合并，GitHub记录2026-10-18T08:46:20Z，mergeCommit **fa1d25211be454cd3cf49da40eb7d8d92e323652**，受核对HEAD d3a3ad293a9c77f4991b2fff29da0dfed6c218e6。随后将该main合入PR21，整合提交 **2d27ae82ae4e8b3b9a264a94e519ec96d9c15de3**；与已验证/记录的fd6e659完整树比较无变化，因此不重复业务测试。此后仅补文档/manifest；最终PR21 mergeCommit与main由GitHub记录确认，不把此形成时回执伪称已完成最后一步。

分支保护查询为未保护，仓库rulesets为空，两PR非draft、MERGEABLE/CLEAN、无CI检查或必需review；使用--match-head-commit普通手动merge，不用--admin、强推或自动合并。三个指定评审线程在修复及相关验证后处理，其状态与D/RV/Windows/现场验收分别管理。
