# PR20 / PR21 协调整合 · 2026-10-18

用户本轮授权修复、复核、合并两项PR，取代两个工作包形成时“只交PR、不合并”的本次交付限制；原工作包和历史验收保持形成时事实。开工main `08286542bbd007433d85be2cb8d129b366b007e0`，PR20 `ecdbddc6640249347c7821cf3b1e58ccd7c22f83`，PR21 `e49249c2a5148544cdc5b7d8bf0a74a69c015026`。主目录未跟踪资料保留，隔离工作树顺序执行，不操作真实设备。

## 设计与执行计划

- [x] 读取最新main入口、framework/decisions、两个工作包与实际评审线程；以原请求、接受事实、独立回执责任和资源生命周期为不变量。
- [x] PR20链接路径负例先红灯；备份对源目录及目标所有已存在祖先拒绝符号链接/重解析点（含Junction、悬空链接），覆盖新父目录场景。普通外部路径仍允许。维护者须提供普通路径并保护祖先目录权限；检查不声称抵御有权限并发替换祖先的攻击。macOS的/tmp、/var等系统别名也受此规则约束，使用实际路径。
- [x] PR21真实提供者ProtocolEvidence由Python构建器与C#签名包验证器强制64位ASCII十六进制；协议证据与资格文件比较统一忽略十六进制字母大小写，保留原签名文档和包身份，不改签既有包。
- [x] 合并逐文件核对Commands/Guards/Runtime/Stages/Recovery，保留20的健康预算/取消、恢复清单和21的独立重量/图像守卫、Production资格与迁移；必要交叉用例及解包进程旅程验证。
- [ ] 定向构建/测试与证据通过后，先正常合并PR20，再将main合入PR21，比较测试树与最终树；正常合并PR21，核对远端main和PR状态。禁止强推、绕过保护、自动合并。

已复现：新增BackupAsync实际入口回归在PR20原源码接受指向运行根的外部目录链接，返回BackupCreated（FAIL）。修复源码`1b6962e1909e1a33967fce8902eb001bec341d6c`定向构建0警告/0错误；同一--backup-health-recovery全组PASS，包含直接/嵌套/悬空链接拒绝、普通外部目录成功、原Unknown上传备份恢复、健康超时和取消、PLC接受事实保留。中间一次失败是悬空链接测试清理误用Directory.Delete，改为File.Delete，保留原失败日志。证据见[evidence](evidence/pr20-21-integration/README.md)。Windows Junction测试分支已加入，当前主机为macOS，实际Windows仍NOT_RUN。

## 验证边界

Windows执行/UI/UAC/ACL/Junction：NOT_RUN。真实SDK、秤/图像实体关联、WCS/PLC握手与现场验收：BLOCKED。生产资格的合成签名测试不构成批准；缺必需证据继续NotReady。原REQ/BR/CAPI/D/RV、SQL/checksum、历史失败和原包身份保留。当前报告为执行中的增量，完成结果在后续提交补充。

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
