# PR31 最新head独立复审

结论：**本次四项重点范围内未发现剩余P1/P2阻断。Standards 0项，Spec 0项。** 此结论来自两个独立静态子审查、固定源码独立构建/执行和负向探针；作者既有PASS未当作本次运行结果。Windows/真实接口/生产及现场资格继续受下述边界限制。

固定PR head：**f068156ed9d4a0ffe4feee94b5c11e966aaa366b**。固定main/差异基线：**baac260214f910dca9ae4628ba6449b25188e2ac**。结束时重新读取GitHub，两者均未变化，PR仍OPEN。main已包含PR24/26。PR29/30及其他并行改动未混入本次源码。

## Standards

独立规范复核未发现新增P1/P2。AGENTS的文档先行、业务唯一修改权、原请求/回执/Unknown保有、失败不清责及验证分层要求在此次增量中得到保持。f38162c文档治理先于95c285b业务整改；后续新增的恢复分支也有维护主体、原因、原发生和备份身份审计。代码异味基线未形成可确认的范围内阻断；未将风格偏好报告为硬性违规。

## Spec

独立需求复核未发现缺失或错误实现导致的P1/P2。四项核验如下；所有链接固定本次完整head。

### 1. DB4升级前兼容判定：已闭合原F01

[ParcelRepository.cs:60](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/src/Ipc.Application.ParcelFlow/ParcelRepository.cs#L60)先只读打开并检查旧版本/迁移checksum与未结责任，随后才写库；[第93–102行](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/src/Ipc.Application.ParcelFlow/ParcelRepository.cs#L93)不再豁免DB4。[UpgradeOperations](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/src/Ipc.Application.ParcelFlow/ParcelRepository.cs#L106)流式读取活动和归档原件，核验归档body hash。[ParcelRuntime.cs:76](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L76)将其置于配置确认和中断恢复写入之前。

采用的是明确的保守兼容政策：未结普通DB4即使精确实现看似相同也不自动跨库升级，先匹配旧发布清偿。真实PR24 DB4副本，以及当前真实Application生成的WCS Unknown、PLC Accepted/Unknown、Attached Unknown、不同冻结实现、非空WAL均独立通过拒绝和原件保有检查；已结DB4升级正例通过。

资料专用例外沿[ParcelReceiptCustody.cs:50](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/src/Ipc.Application.ParcelFlow/ParcelReceiptCustody.cs#L50)在SQL5前核完整Owner集合、终态、原Query/raw/hash及绑定。历史OutcomeReconciled沿[离线入口](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/src/Ipc.Application.ParcelFlow/ParcelLegacyReceiptRecovery.cs#L34)先证明原依据、完整备份并写意图审计；缺raw责任仍在，不用新Query补原件。实际旧DB3发布Query/中断本地T3和旧DB4归档Owner正例通过。另对真实旧DB4副本注入缺Query原件、Query hash不符、孤儿未结回执，三例均拒绝、保持DB4且外呼Query=0；原来源根未改。

### 2. schema1/2实际main位冲突：已闭合原S01

[ParcelRuntime.cs:80](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/src/Ipc.Application.ParcelFlow/ParcelRuntime.cs#L80)按持久Effective实际Positions恢复，旧schema实际为main。[ParcelConflicts.cs:25](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/src/Ipc.Application.ParcelFlow/ParcelConflicts.cs#L25)对已错误保存left/right的未确认legacy行扩围：保留原ID，并将原body逐字节写入同事务审计。已确认行不重武装，新明确范围冲突仍位内隔离。

schema1与2的仅审计恢复、已有错误范围、Resume拒绝、授权确认、重启后不重武装及精确旧body保有均独立通过。

### 3. Pending激活屏障：通过

[ParcelReceiptCustody.cs:101](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/src/Ipc.Application.ParcelFlow/ParcelReceiptCustody.cs#L101)将未确认冲突纳入激活屏障，且冲突在BeginActivation之前恢复。schema2→schema3 Pending在冲突未确认时被拒绝，旧Effective/main及完整Pending保留；原冲突确认后必须显式restage，后续受控启动才能激活。独立运行通过；配置确认/CAS/中断选择器亦通过。

### 4. architecture-quality原失败：参考闭环成立，观测缺口保留

原失败日志仍为原字节，SHA256 **74a4e7aef8c6428691c78455521c2b1b3b21adb784bdd380bb1f7d98381fdbde**。旧运行没有记录该次返回回执，不能直接断言已找回旧返回值；已有数据库无首次Query/QueryInconclusive痕迹，与健康在途守卫拒绝机制吻合。

[Recovery02.cs:115–132](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/tests/Ipc.Application.Verification/ArchitectureQuality.Recovery02.cs#L115)通过受控健康等待，先断言Rejected、Configure=false、Query=0、原库存/上传事实/图像不变；排空后等待公开Configure并执行一次Query，保留原Unresolved/未释放图像断言。[生产CanConfigure](https://github.com/yuan2go/importer-pc/blob/f068156ed9d4a0ffe4feee94b5c11e966aaa366b/src/Ipc.Application.ParcelFlow/ParcelGuards.cs#L25)未改弱。本轮独立architecture-quality完成PASS，未删除原断言、吞异常或覆盖原FAIL。

## 本轮独立运行与证据

在仓库外从固定Git提交提取完整副本，2480个Git blob逐一校验一致；构建输出也在仓库外。SDK10.0.402，locked restore、-m:1 -nr:false，构建0警告/错误。未改任何工作树、配置、设计文档或PR，未合并。

- `--upgrade-conflict-closure`：退出0。
- `--historical-db4-upgrade`：退出0，使用原PR24已运行参考链DB4副本。
- `--architecture-quality`：退出0，含本轮原失败/维护围栏定向断言。
- `--configuration-confirmation`、`--receipt-custody`：各退出0。
- `--prior-custody-migration`：退出0，实际旧DB4 OutcomeReconciled/归档Owner。
- `--legacy-custody-upgrade`：退出0，实际8981d54旧发布及原DB3夹具。
- 独立negative-probe：三例通过，DB4保持、外部Query=0、原发生保留、原夹具根不变。

日志、退出码、原SQLite/WAL/raw/图像/配置运行根及负向副本保存在本artifact；runtime-custody.json登记来源和保留路径。source-custody.json证明源码身份、SQL1～5声明逐字节不变、生产维护守卫源码不变及原失败日志身份。本轮没有把作者原证据篡改为新PASS。

## 未验证边界及保留阻断

1. Windows安装/WPF多窗/HID/UAC/ACL、Windows扩展卸载：NOT_RUN。
2. 真实SDK/相机/秤/WCS/PLC/身份协议、实体关联、复位、安全联锁、现场及Production资格：BLOCKED。
3. 新完整产品候选、签名组合、实际扩展安装卸载、独立HTTP媒介旅程、旧Supply/Inspection完整验收：本轮NOT_RUN。原作者证据按其原源码读取，不能替代本次资格。
4. 长期节拍、真实断电/坏介质、完整旧站迁移、所有schema×损坏状态组合未穷举；待真实旧根/精确发布/备份盘点后逐根验证。对已经被旧程序写成DB5的根，本轮不降标记或倒退业务账。
5. 并行PR27/29/30未纳入，尤其PR30恢复文件重叠需要后续实际head集成核验。此次无差别全量测试、打包及R01～R05完整选择器重放未执行；所需已覆盖重点按上述独立选择器理解。
6. 原审查01 PDF仍未取得，BLOCKED；历史267项资料问题及原D/RV不关闭。本轮未重新执行全仓资料验证；固定源码和原日志字节校验不代表这些历史问题已解决。

Standards：0项阻断；Spec：0项阻断。结论仅限此次四项重点和所列证据，不扩张至未验证边界。
