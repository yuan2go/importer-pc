# UNICODE-SHORTCUT-COMPATIBILITY-01 实施与验证

开工main `a8cd431456f73e83040f75a0c5e3cc1423440d1b`；继承PR110 `980a1383e4589717c9a147746b6fd6271ef69088`，没有重新实现WSH到Unicode的改造。独立分支 `codex/unicode-shortcut-lifecycle-01`，工作树与全部证据留D盘，未接管其他任务或四份未跟踪附件。原[只读核查快照](../evidence/unicode-shortcut-compatibility-01/review-snapshot.json)保留形成时身份；本轮执行结果另计。

## 实际问题与最小修复

1. 包装对象释放后继续读取只得到底层 InvalidComObjectException。根RCW改为唯一可清空的所有者，Dispose原子取走一次，释放后接口访问显式 ObjectDisposedException；不分别释放接口别名，不新增静态共享RCW。
2. File.Exists 把精确快捷方式路径上的同名目录当不存在，Remove无错误返回。改为读取属性，仅对FileNotFound/DirectoryNotFound幂等跳过；目录和reparse冲突明确IOException，其他访问错误不吞掉。原祖先守卫保留。
3. 一个可加载但没有文件目标的Shell Link在Remove中被静默当成他人条目。GetPath空结果现在明确InvalidDataException；这是对S_FALSE/空结果的失败关闭，原件保留，不Resolve或猜目标。

验证脚本另有实际证据缺口：全局ApplicationManifest使三个ACP构建的Platform DLL也随manifest变化，哈希审计得到三个不同值。没有放宽相同产品DLL断言；改用只由验证项目消费的ShortcutProbeManifest，脚本对三组Platform DLL SHA256严格比较。原行为PASS与该次二进制一致性FAIL分别保留，不能把修改前的矩阵描述为“只改进程manifest、同一份产品DLL”。

创建、上一部署/描述文件信任、提交后修复、卸载固定清单及target/arguments归属合同未重建。没有把尚未复现的问题改写成生产缺陷；生产逐次保存后回读/跨进程原子替换、任意长路径、物理写入中断等仍是未取得资格的边界，不能以本轮短测宣布它们已完成。

本包SM1.14.2、DirectPlc/WcsToPlc1.8.2；Contracts1.18.0、DB9和SQL1～9保持。三产品版本与composition同步。DirectPlc/WcsToPlc原Desktop锁文件缺现有共享WPF的传递项目引用，锁定构建暴露NU1004；只在核对后更新Project元数据并复跑锁定构建，不降低最终RestoreLockedMode、不变更NuGet版本/内容哈希。失败原件保留。

## 先红与原件

- 基线980a1383：原脚本三ACP全部通过，这是对已有修复的继承确认。
- `dc51d50f`：新增失败夹具，发现生命周期/目录两项实际问题；资源故障夹具误把映射出的DirectoryNotFoundException当成不期望异常，首次输出保留，不把夹具问题记作第三个产品缺陷。
- `5a27a098`：只修正缺目录HRESULT的预期异常类型，产品字节仍等同980a1383；实际36断言、两个失败场景；资源循环411→411句柄、STA和其余保护已执行。
- `f9d56bd0`：前两项修复后三ACP每组39项生命周期断言通过，原选项回归保持。
- `e18296e6`：在前两项修复上增加空目标断言，41断言、仅空目标场景失败；先有失败再补空结果守卫。不是删掉此前失败断言。

所有原件与逐文件SHA256见[执行证据清单](../evidence/unicode-shortcut-compatibility-01/execution.json)。计数不把GetACP输出当业务断言；源码/测试/manifest/二进制身份分别记录，不将后续文档提交转记为这些旧运行源。

## 必要回归和边界

最终工程源 `e808ed4646fc790f9c1aa6132d873a5a9d46758b`。三种代码页都执行原installer-options及新增shortcut-lifecycle；Windows11本机22631的MTA与独立STA均使用真实Shell Link COM，未改变系统ACP或机器策略。每组原选项27项、新生命周期43项断言，两个入口分别先严格核验实际GetACP。

|预期/实际ACP|原选项|生命周期|同一份Platform DLL|
|---|---|---|---|
|1252 / 1252|27 PASS|43 PASS|SHA256严格相同|
|936 / 936|27 PASS|43 PASS|SHA256严格相同|
|65001 / 65001|27 PASS|43 PASS|SHA256严格相同|

三组Platform DLL的SHA256均为 `920F7886129BB4252963FC1A81E07C7A3C77A9C32E3F45CAAA26ADA611357D46`。原件在 `original/final-scopefix/`；`original/final/`是7b968e4b上行为通过但DLL一致性审计失败的前一轮，不混记。工作树尚有待归档文档改动，结果JSON保留真实GitStatus；产品、测试和脚本已经固定于上述源。之后文档/manifest提交不转记这些本机PASS。

覆盖四种选项、中文/扩展Unicode、完整字段回读、独立native目标读取、缺失入口和已归属描述/图标修复、版本升级/旧版保护、历史中文长名和Importer PC菜单、目标缺失、改目标/改参数/损坏文件/目录/空目标字节保护、分享冲突后修复与卸载重入、祖先/leaf junction拒绝，以及每种apartment下的100轮成功/Load失败/Save失败释放。不强制GC；句柄观测只证明该有界测试。

378字符目标在本机明确拒绝，原稳定链接hash不变；不将其记作长路径支持。部分菜单创建后遇冲突能够在夹具所有者保留并明确移动冲突原件后重新投影，不能把这等同提权RepairSystem成功。

本机实际执行installer-change-guards、p0-task-validate、受影响WPF构建；完整P0/aqa01/冻结介质由新PR精确测试合并源CI计证。未扩跑2592000规模或现场长期测试。

canonical manifest本轮记录与暂存Git对象逐项匹配，新增未列文件为0。全库核对还保留一项继承的文档欠账：980a1383已有404个历史归档文件未列入主manifest，本轮数量仍404，不记全库清单PASS，也不为本包扩改旧归档。详见[独立清单核查](../evidence/unicode-shortcut-compatibility-01/canonical-check.json)。

## PR112及其他分支

开工API和git祖先检查均确认PR112 `43382c59` 已经继承980a1383。PR112原三代码页PASS属于其原源，尚不含本轮生命周期差量。PR110未合并时，本轮main PR包含其原祖先，审查本轮差量用 `980a1383..本分支`；父PR进入main后差异自然缩小。不重新cherry-pick同一Unicode实现，不强推或合并他人PR。

PR112后续必须继承本轮增量并对实际组合头重跑三ACP、P0/ST08及部署守卫。与PR108/111集成时保持真实版本/库存和各自成果，不用本包或旧单分支PASS代替组合证据。

## 现场未完成

NOT_RUN：真实安装/升级/卸载与ResumeRemoval、HKLM受保护描述文件恢复成功路径、UAC/公共桌面ACL、系统重启/任务/SCM/WER、物理写满/断电、目标26100+/4核8GB、真实SDK/相机/RCS及24h/72h现场。G01真实RCS合同/授权、G02管理密码恢复政策、自动重连合同按原包继续BLOCKED_EXTERNAL。原请求、不明状态、D/RV身份不改变。

## 本轮CI的证据入口

本轮main PR的Checks页与保留的code-page/P0/frozen-medium artifacts记录准确run、检查头和测试合并源；PR说明及交付回执给出本轮实际链接。本页本机结果与这些CI分别计证，不沿用PR110或PR112旧CI作为本轮源的通过回执。CI产物构建不等于正式安装或现场通过。
