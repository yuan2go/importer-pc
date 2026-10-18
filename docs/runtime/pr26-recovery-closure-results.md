# PR26独立复审P1/P2整改结果

基线main **baac260214f910dca9ae4628ba6449b25188e2ac**；最终业务/测试源码 **0b6fe6a11fb53640cadddc86009293883bb77d71**。文档治理先行提交 **f38162c**，后续交付提交仅文档和证据。独立分支 `codex/pr26-recovery-closure`，提交新PR供复审，不合并main。原审查固定head 78298b5、指定PR24基线4afe2af，见[原报告及失败](../reviews/pr26-independent-78298b5/README.md)；不把旧报告的后续main未审范围伪称已审。

## 问题与固定源码

### F01 / P1：DB4升级前判定

复核baac260仍存在DB4跳过未结检查、先SQL5再OriginalCapabilityUnavailable的路径。现按既有保守升级政策处理DB2/3/4：即使实现看似相同，普通跨存储升级也须先清偿外部责任。不能仅以主State终态、已归档或旧OutcomeReconciled推断清偿。

[ParcelRepository.cs:55](https://github.com/yuan2go/importer-pc/blob/0b6fe6a11fb53640cadddc86009293883bb77d71/src/Ipc.Application.ParcelFlow/ParcelRepository.cs#L55)在写库之前用只读连接检查版本/checksum及全部未结；[第89行](https://github.com/yuan2go/importer-pc/blob/0b6fe6a11fb53640cadddc86009293883bb77d71/src/Ipc.Application.ParcelFlow/ParcelRepository.cs#L89)覆盖DB4、非终态stage、图像、回执及流式归档原件。普通未结拒绝保持DB4，不checkpoint原WAL。[ParcelRuntime.cs:74](https://github.com/yuan2go/importer-pc/blob/0b6fe6a11fb53640cadddc86009293883bb77d71/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L74)将此判定置于配置确认/中断恢复写入之前。DB5同格式的来源Start前精确实现校验继续保留。

[资料专用迁移](https://github.com/yuan2go/importer-pc/blob/0b6fe6a11fb53640cadddc86009293883bb77d71/src/Ipc.Application.ParcelFlow/ParcelReceiptCustody.cs#L50)在SQL5前核每个活动/归档Owner及原Query绑定、原raw/hash、终态；库存流式读取，不把历史查询全部加载内存。缺依据/孤儿/未知状态保守拒绝。旧DB4 OutcomeReconciled沿[实际离线维护入口](https://github.com/yuan2go/importer-pc/blob/0b6fe6a11fb53640cadddc86009293883bb77d71/src/Ipc.Application.ParcelFlow/ParcelLegacyReceiptRecovery.cs#L34)校验已持久Query并完成完整备份、主体/原因/原发生/备份审计后迁移，不外呼新Query。独立静态复核指出的新增路径审计缺口已补齐并用定向断言验证。

真实PR24 DB4 WcsUnknown副本与合成当前Application生成的WCS Unknown、PLC Accepted/Unknown、Attached Unknown、不同冻结实现均在迁移前拒绝；DB/config/raw/image保持，匹配DB4读者仍可读。非空WAL保持逐字节一致。已结DB4可升级；实际8981d54 DB3旧发布Query/断进程/本地T3和历史已归档OutcomeReconciled仍可保守迁移，原Awaiting/raw_hash=null、图像或历史已释放事实、包/归档责任持续保留。

### S01 / P2：旧schema冲突保护实际位

[ParcelRuntime.cs:80](https://github.com/yuan2go/importer-pc/blob/0b6fe6a11fb53640cadddc86009293883bb77d71/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L80)使用持久Effective实际Positions恢复旧冲突；schema1/2为main。旧版已经错误保存left/right的legacy责任沿[同ID扩围事务](https://github.com/yuan2go/importer-pc/blob/0b6fe6a11fb53640cadddc86009293883bb77d71/src/Ipc.Application.ParcelFlow/ParcelConflicts.cs#L20)扩充main，将旧body原字节写入追加审计；CAS/确认行与暂停、revision、审计同事务。已确认责任不重新武装。普通已知范围冲突继续位内隔离。

[配置激活屏障](https://github.com/yuan2go/importer-pc/blob/0b6fe6a11fb53640cadddc86009293883bb77d71/src/Ipc.Application.ParcelFlow/ParcelReceiptCustody.cs#L101)包含未确认冲突，main→left/right Pending不能跳过旧责任；旧Effective/Pending及失败保持，维护检查/确认后显式restage、后续受控启动才激活。

真实Application验证schema1和2各自的原审计恢复、已有错误范围、Resume拒绝、精确旧body保有、确认和重启不重武装；另验证schema2→3 Pending转换。

## architecture-quality原失败诊断

[原日志](../reviews/pr26-independent-78298b5/e914-evidence/architecture-quality.log)保持FAIL/退出134。原调用未打印返回回执，原DB没有首次Query/QueryInconclusive痕迹；不能声称直接观测到其返回值。

[确定性诊断](evidence/pr26-recovery-closure/diagnosis/DIAGNOSIS.md)用原真实Application/Factory和受控健康完成证明：健康在途时Configure=false、恢复Rejected、QueryCalls=0，回执库存和图像不变；排空后同一原件Unresolved、仅一次Query、原Submit仍Awaiting。原因是测试未等待公开维护前置条件，生产围栏正确。

[Recovery02修正](https://github.com/yuan2go/importer-pc/blob/0b6fe6a11fb53640cadddc86009293883bb77d71/tests/Ipc.Application.Verification/ArchitectureQuality.Recovery02.cs#L115)先确定性断言在途拒绝/无外呼/原事实不变，释放健康调用，等待公开Configure，再验证原无结果Query。**原Unresolved/未释放图像断言保留**，未削弱CanConfigure或吞异常。最终architecture-quality完成PASS，原FAIL仍是原FAIL。

## 验证

SDK10.0.402；serial `-m:1 -nr:false`、locked restore。全部日志/退出码及原运行根登记见[证据](evidence/pr26-recovery-closure/README.md)，完整SQLite/WAL/raw在本地artifact保留。最终定向运行先完成构建，并固定DLL前后hash；早期95c阶段输出保留但不作最终冻结资格。

|必要验证|实际源码|结果/覆盖|
|---|---|---|
|Verification构建、Sportmaster Assembly独立构建|0b6fe6a|PASS，0 warning/error|
|--upgrade-conflict-closure|0b6fe6a|PASS：未结DB4、原件/非空WAL、已结正例、schema1/2实际main、历史扩围、确认重启、Pending切换|
|--historical-db4-upgrade|0b6fe6a|PASS：原PR24 DB4副本拒绝，DB/config/raw/image及源根保持|
|--legacy-custody-upgrade|0b6fe6a|PASS：真实8981d54旧发布DB3/断进程/本地Query T3，不重复Submit；缺原件/冲突拒绝|
|--prior-custody-migration|0b6fe6a|PASS：原旧DB4 OutcomeReconciled/归档Owner、完整备份、无新外呼、审计与资料保有|
|--architecture-quality|0b6fe6a|PASS：容量/归档、断进程激活、主附隔离、设备并发/原件恢复、原失败断言|
|--review-closure|0b6fe6a|PASS：R01～R05相关回执、Attached、双位维护围栏与混合消费者|
|--configuration-confirmation、--receipt-custody|0b6fe6a|PASS：旧账确认/CAS/中断，缺raw与原图/包持续责任|
|--inventory-closure|8039121|PASS：schema1/2/3去重前库存与Effective/Pending/Frozen；之后变更仅测试租约范围和旧资料迁移审计，消费者/库存源码未变|
|实际Assembly进程|0b6fe6a|PASS：左右独立原WCS各1次、无PLC；实际recover-legacy-receipt命令迁移旧DB4并生成COMPLETED备份|

修复前新增定向组15项FAIL保留。初次nullable编译错误已修正，日志保留。历史副本测试曾把根目录PID租约误当不可变原件，保留该FAIL；按ResourceLease实物限定排除该控制文件，业务DB/config/raw/image原字节断言保留并通过。独立诊断最初原生库解析失败也单独保留。没有删断言、改SQL/checksum或循环原失败刷PASS，没有打包或重签候选。

## 数据库、配置、扩展及回退

SQL1～5声明逐字节相同，[兼容证明](evidence/pr26-recovery-closure/compatibility-proof.json)。DB5、schema1/2/3、ABI1.6.0不升级；Platform/Storage/公共契约源未改。新业务二进制须新发布身份；扩展仍依实际目标公共DLL精确指纹准入，不能拿旧媒介/旧包的PASS代新组合资格。本轮未打新包或执行新签名组合卸载。

普通DB4未结升级按保守政策拒绝；使用匹配旧发布、原配置及完整备份清偿，不能直接加版本、删Unknown或重发。仅终结Attached且原Query证据完整的资料责任可用维护指南专用入口迁移，缺原字节不由Query补造。旧代码已经释放的图片保持缺口事实。

已被旧程序错误写成DB5的未结根，本修复不会降版本或恢复伪原件；保全现有根和升级前备份/原发布，先核对外部事实再决定匹配恢复方案。回退不得回滚业务接受账或重放请求；DB5旧程序仍拒读，升级前备份/发布必须匹配。

## 未验证边界及后续验收

- Windows安装/WPF多窗/HID/UAC/ACL、Windows扩展卸载：NOT_RUN。按既有[Windows与现场清单](validation/architecture-quality-closure-01-windows.md)验证，并增加DB4非空WAL拒绝、main位冲突确认和Pending切换。
- 真实SDK/相机/秤/WCS/PLC/身份协议、实体关联/复位/安全联锁/现场：BLOCKED。用匹配发布核真实Unknown/接受/物理完成；仅权威原Query允许清偿，软件暂停不是急停，Production门禁不变。
- 长期节拍、真实断电/坏介质、完整旧站迁移、全部schema×全部损坏状态未穷举。需真实旧根/完整备份/精确旧发布盘点后逐根验收。
- 新完整候选打包、签名选装、真实卸载、旧Supply/Inspection完整验收：本轮NOT_RUN；继承原源码证据，不升级为新候选PASS。
- 并行PR27、PR29（版本兼容）和PR30（RCS）未纳入。交付前main仍baac260；PR30与恢复/资料文件、29与入口/manifest重叠，后续集成须按实际head保留本轮守卫并做受影响验证。本任务不替他人合并或改工作树。
- 资料检查用同一带Pillow解释器比较：基线268项、本轮267项，新增0；本轮修正manifest.sha256文件名格式，原267项源资产问题保持FAIL，未放宽校验器。初次系统Python缺Pillow的输出与新公开JSON本地路径标记拒绝另行保留；公开路径映射副本显式标识，原JSON及SHA在本地保留。

原审查01 PDF仍未取得，BLOCKED；原审查02、REQ/BR/CAPI/D/RV及历史267项资料问题不倒写。公开范围仍PUBLIC_SUBSET_ONLY。
