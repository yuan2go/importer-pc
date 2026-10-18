# PR32最新HEAD独立只读审查

结论：本次范围未发现新增可确认的P1/P2缺陷。两项恢复P2和custody夹具竞态在限定参考范围内闭环；不等于Windows/真实现场验收，也不把其他PR的修复计入PR32。

审查HEAD `0aac91922dfd04539f6015f7bc8dbf820a23d782`，main/base `baac260214f910dca9ae4628ba6449b25188e2ac`。GitHub最终回读PR32 OPEN，HEAD未变。并行PR27/29/30/31未改动或合并；PR32工作树审查前后干净。全部新增构建、合成夹具和审查材料在本Git外目录，不修改业务代码/配置/设计，不发布评论或合并。该审查不将作者上一轮静态结论当作独立证据。

|逐项|源码复核|本次新增独立验证|结论|
|---|---|---|---|
|S1永久回复身份|ParcelLegacyReceiptRecovery 47/66/95行，外部Query和本地原件重放都在接受前核首次原hash；DB4+原索引、DB2/3原occurrence顺序读取。StoreRaw保留矛盾原件，LegacyQueryConflict保原版本及独立责任，无新账。|最新HEAD独立Release构建；恢复P2组覆盖DB3/4的65条裁剪后矛盾及两条同字节幂等。另补DB2外查/本地×矛盾/同字节四种组合，全部通过。|PASS（参考）|
|Q1持久Query依据|ParcelReceiptCustody 20–52行，以持久关联、独立Committed原件SHA、永久ID、冻结请求/payload/配置和当前终态为依据；投影仅保留一致性守卫。|67个缺raw分别核对，依据在裁剪后和实际重启/候选激活后全部有效；相反终态、改配置及改raw拒绝。另复制新夹具，将已淘汰中间Query原件置入archived BLOB并删spool，库存及依据仍通过。|PASS（参考）|
|custody确定性并发|测试48–77行在预约前停车后续Monitor，放行一个真实WCS Health并等Entered，验证拒绝无修订/Query/owner变化，finally释放同一调用，再核排空成功。生产CanConfigure、资源围栏未改；原恢复/图像释放/归档断言保留。|独立运行custody并行于恢复和维护围栏组，完整占用→Rejected→排空→OutcomeKnownRawMissing路径通过。停止仍取消同一token，未遗留监控任务。|PASS（夹具竞态闭环）|
|选装发行物|未改Store/compositions及公共契约源文件、SQL1～5；1.8.2/1.1.2为新不可变包，作用域源1291ee6与宿主源fe4a4a2分开。|核心/选装/Windows三ZIP核SHA及94/97/730份文件；包集合0/2/2，精确公共DLL相同。新解压媒介安装/原操作恢复、双reader+WCS绑定/激活、启动失败保旧及Pending、服务维护、解绑/安全卸载通过；两包实际extension-check exit0。|PASS（便携参考/Windows静态）|

BLOB补测明确为直接SQL构造的合成存储分支，不能冒充缺raw责任下自然Archive成功。DB2补测是新建指定旧格式，不冒充真实旧发布。探针首次缺SQLite native库的失败及补库后的结果均保留，外部探针构建有一条CS8605警告；仓库独立构建0警告/0错误。

## custody原FAIL的独立核对

原FAIL SHA256 `9f2b79663b4ce0b04d80087447dce90ba002d9ca38012b3f5639c33e384f10c3`。只读immutable核原失败库（数据库SHA `4f228d889fa37419f109e1ec0372205e06f43c3a3eb40a038afd2cc37fe6eb73`，无非空WAL），revision13/Paused，Query发生0，主SlotResolved、WCS Accepted/attempt1、Upload Unknown/attempt1、原图未释放；审计止于MaintenanceAuthenticated。读前后数据库hash相同，未修改原证据。

结合原夹具和提前返回路径，Monitor持有externalWaits是可解释原安全拒绝的剩余正常路径；原运行缺命令返回值、瞬时计数和具体port，因此归因仍注明基于源码/持久事实的推导。不能将新受控trace倒写成原trace。旧DLL受控探针是继承证据；本次最新HEAD受控序列是新增直接观测。独立重跑PASS本身不承担因果解释。原FAIL不是已证明的责任丢失、死锁或设备故障。

## 证据归属

- 继承main/PR23/24：采集所有权、永久索引、归档/raw/分页、资源/消费者围栏等。PR26独立原件责任、配置旧账确认、真实8981旧DB3桥接及原包等维持形成时范围。未把这些算作PR32新增实现。
- PR32作者新增：fe4a4a2两处恢复代码及确定性夹具；其实际旧DB3桥接、配置/容量/三Application、签名消费者、Attached冒烟等原输出只读核验，没有冒充本次独立重跑。
- 本次新增：最新HEAD独立构建与恢复/custody/维护组；DB2及BLOB补测；原FAIL库只读读回；三媒介指纹核对、双包静态检查及新隔离选装旅程。最新HEAD与fe4a4a2之间src/tests/config/scripts/examples及随包维护指南无差异。独立构建默认SourceRevisionId=0aac；实际媒介保持fe4a4a2及精确DLL，二者分别记录。
- 原始6份FAIL/各阶段PASS日志字节数及SHA、25份作者输出SHA、5份归档原blob均匹配。公开检查本次仍FAIL267项，与PR26原错误数组逐项相同，新增0；未新增例外或弱化守卫。

## 边界与结果位置

本次未重复运行共享旧DB3原发布Query旅程，以避免触碰其冻结peer-root的原运行lease；已有真实旧发布证据保持继承，不以新建旧格式夹具替代。Windows实际运行/交互/UAC/ACL NOT_RUN；真实SDK/WCS/PLC协议、设备与现场BLOCKED。无现场设备调用。PR31的DB4预迁移/旧冲突另审，不在本结论内。

- `build.log`、`p2.log`、`custody.log`、`review-closure.log`：独立结果，均exit0。
- `spec-probe/`：DB2/BLOB小探针、原始native缺失失败、说明与SHA。
- `original-failure-readback.json`：原失败只读事实。
- `release-readback.json`、`package-checks.json`、`selected-journey/`：精确产物与新隔离真实旅程。
- `archive-checks.json`：FAIL267未改写。

规范轴：0项新增硬违规；需求轴：0项新增可确认缺陷。结论仅限上述范围和准确HEAD。
