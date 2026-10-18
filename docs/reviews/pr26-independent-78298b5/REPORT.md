# PR26 最终head独立架构审查

结论：**CHANGES_REQUIRED：Standards 1项P2，Spec 1项P1。** 两项均在最终head重新复现；PR24重叠修复已继承，不再沿用旧head的缺失结论。

最终固定head：**78298b5dc7ccdcb7bb08e857d2d94f3681182fde**。按用户要求固定基线为PR24合并后的main **4afe2afacde9e2acab7648fff3440fa3cc331b6e**。

审查开始head为592d2bd；期间更新至e914e75，随后最终78298b5由外部操作合并为 **9a5d87ad41e3e936f56a7e58ec60de8f7aa53458**，时间2026-10-19 00:20:02 Asia/Shanghai。本任务没有修改仓库/PR或执行合并。旧592记录保留，不能当作新head结论。结束远端main又前进至baac260214f910dca9ae4628ba6449b25188e2ac；本报告仅审PR26最终head及其指定基线，不扩张至后续main变更。

新head的src/config/scripts/examples与e914e75逐字节一致；最后一提交只调整一处Custody05测试的Configure等待和文档/证据。本轮对最终head重新构建、执行变化选择器和两个独立问题探针，其余明确标记实际执行来源。

## Standards

### S01 · P2：旧schema冲突恢复未覆盖实际main投递位

新增调用[ParcelRuntime.cs:78](https://github.com/yuan2go/importer-pc/blob/78298b5dc7ccdcb7bb08e857d2d94f3681182fde/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L78)使用`RecoverLegacyConflicts(definition.PositionRoles)`。Sportmaster的声明是left/right，但合法schema1/2通过[ParcelTopology.cs:21](https://github.com/yuan2go/importer-pc/blob/78298b5dc7ccdcb7bb08e857d2d94f3681182fde/src/Ipc.Application.Contracts/ParcelTopology.cs#L21)实际运行main；[PositionReady/CanResume](https://github.com/yuan2go/importer-pc/blob/78298b5dc7ccdcb7bb08e857d2d94f3681182fde/src/Ipc.Application.ParcelFlow/ParcelGuards.cs#L9)因此查不到当前位的冲突。

最终head真实Application已复现schema1、schema2：旧未知范围冲突恢复为Positions=[left,right]，实际位置main；普通Resume返回Persisted，Paused=false、main.Admit=true，但数据库冲突仍acknowledged=0，没有IdentityConflictAcknowledged审计。初始seed配置已经正常完成资源确认，因此不是绕过新配置确认的非法夹具。

固定证据：standards-probe/legacy-conflict-proof.json、run1.json、run2.json及各自原DB/配置。违反架构22的旧schema单reader兼容和N03的冲突维护确认要求。应按持久Effective的实际位恢复范围，并明确Pending拓扑切换如何保有旧冲突；不能仅用启动Pause代替位守卫。

## Spec

### F01 · P1：DB4未结作业先升级到DB5，再因原实现不兼容拒绝启动

[ParcelRepository.cs:79](https://github.com/yuan2go/importer-pc/blob/78298b5dc7ccdcb7bb08e857d2d94f3681182fde/src/Ipc.Application.ParcelFlow/ParcelRepository.cs#L79)用`legacy >= 4`无条件跳过旧未结守卫，构造函数[第63–66行](https://github.com/yuan2go/importer-pc/blob/78298b5dc7ccdcb7bb08e857d2d94f3681182fde/src/Ipc.Application.ParcelFlow/ParcelRepository.cs#L63)先提交SQL5。随后[ParcelRuntime.cs:107–110](https://github.com/yuan2go/importer-pc/blob/78298b5dc7ccdcb7bb08e857d2d94f3681182fde/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L107)才核对未结原实现并抛OriginalCapabilityUnavailable。

独立取PR24已运行参考链生成的DB4 WcsUnknown原件副本，用最终head启动，结果：

```
BeforeVersion=4
AfterVersion=5
StartFailure=OriginalCapabilityUnavailable
State=WcsUnknown, RecoveryRequired=true
DatabaseChanged=true, ConfigurationChanged=true
```

未启动任何来源，却已写未来存储标记；匹配DB4读者随后拒绝“Unknown/changed consumer migration”。原DB4、WAL、raw和配置仍在原独立审查artifact保留，探针只改副本。

新[旧发布Query工具](https://github.com/yuan2go/importer-pc/blob/78298b5dc7ccdcb7bb08e857d2d94f3681182fde/src/Ipc.Application.ParcelFlow/ParcelLegacyReceiptRecovery.cs#L35)只支持主链已完成的Attached上传，不能代偿此未明WCS/PLC主链。此实现违背现行维护指南“普通旧库启动在迁移前拒绝未明主链/上传/原件”。需要把DB4也纳入升级前原件/精确实现判断，仅已证明资料责任的受控迁移可例外；不能以先升级再报错替代兼容。

固定证据：migration-probe/results.log、prior-reader-refusal.log、Program.cs及DB5副本；原DB4来源身份记录在original-source-custody.json。不能直接改回版本号、删除责任或盲重发来补救。

## 已闭重叠项与兼容边界

- e914/最终head包含4afe2af祖先。PR24 R01原回执身份补齐、R03所有schema去重前库存校验、R04混合消费者及R05维护围栏已继承；对应库存和R01～R05定向回归在e914独立运行通过，相关业务源码与最终head一致。旧592的“缺失主线修复/冲突”不再作为当前阻断。
- 新配置账对旧Effective保留原字节/Base64/hash和尝试，成功Start后仍要求维护主体、运行身份、CAS和EffectiveHash确认；新候选中断保留Pending且不自动重试。`--configuration-confirmation`独立通过，没有另一个已确认N02缺陷。确认行为不证明找回旧程序已遗失的值。
- 旧DB2/3缺raw的原先死路已有实际旧发布Query入口。使用实际8981d54旧Release/其DB3夹具独立运行：Query落raw后进程终止，重试沿同一发生本地T3，无额外外呼；升级DB5后原Awaiting/空raw_hash、图像、包责任保持。旧592的OutcomeReconciled可转回独立资料责任；`--legacy-custody-upgrade`和`--prior-custody-migration`在e914通过。此结论不关闭F01的未明主链DB4案例。
- SQL1～4/checksum保留，SQL5是明确未来读者门槛；旧程序拒读DB5。ABI1.6.0、配置schema1/2/3、DB版本及精确宿主DLL哈希是不同身份。合并后宿主/签名包不能混用旧候选指纹；本轮未重新签名或打包完整产品。

## 必要验证与失败保留

最终head：锁定测试工程构建PASS；变化后的`--receipt-custody` PASS；两个独立探针均复现上述问题。SDK10.0.402，-m:1 -nr:false，所有产物在仓库外。

e914（业务源码与最终head一致）：`--inventory-closure`、`--review-closure`、`--configuration-confirmation`、`--receipt-custody`、实际旧发布`--legacy-custody-upgrade`、`--prior-custody-migration`均完成退出0。原日志和运行根保留在本artifact的e914-evidence中。

较宽`--architecture-quality`在e914退出134，有1条失败：`inconclusive original Query never dispositions missing raw or releases its image`。该调用未等待public Configure可用；日志未记录返回回执，未将可能的健康外呼围栏竞争定案为第三个业务缺陷。原日志、断言和原DB保留，不删除、不重试刷PASS，不将该组标通过。最终782仅修了另一个Custody05调用的等待，没有改这条Recovery02断言；本轮没有把原作者PASS代作此运行结果。

上一592初始Python模块路径错误、旧探针设置错误与实际代码失败分别保留；不是最终head产品缺陷。原审查02 PDF只读提取核要求，不修改或重制；审查01原PDF缺件仍独立保留。

## 未验证范围

- Windows实际安装/WPF多窗/HID/UAC/ACL、Windows后装卸载：NOT_RUN。
- 真实SDK/设备/WCS/PLC/身份合同、实体关联/复位/安全联锁/现场：BLOCKED，Production不自动Ready。
- 长期节拍、真实断电/故障介质、完整旧站迁移、所有schema×所有未明主链/原图/回执组合：未独立穷举。
- 最终完整产品候选/签名选装安装卸载、独立HTTP媒介旅程、WPF交叉构建和旧Supply/Inspection完整验收：本轮未重跑；原作者证据按原源码/宿主读取。
- 后续main baac260不在本次PR26审查范围。历史267项资料错误、REQ/BR/CAPI/D/RV及Windows/现场门禁不因外部合并关闭。

Standards：1项P2（S01）；Spec：1项P1（F01）。本任务未修改仓库、PR或合并。
