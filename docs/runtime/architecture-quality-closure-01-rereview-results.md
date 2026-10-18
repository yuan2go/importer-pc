# PR24 独立审查五项整改与复审证据

本轮按用户授权修复 PR24-R01～R05，更新[同一 PR #24](https://github.com/yuan2go/importer-pc/pull/24)，不合并 main。结论为 **代码整改完成、必要参考验证通过，待独立复审**；不代表 Windows、真实协议或现场放行。

审查输入 head **a2e987a8d15eb397ad1a40cf1a303bdad4ec397b**，执行 main **b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d**。文档治理先提交 **b59318a0cb7298cffa08351aa6fab77f6efb46ce**，原入口字节和身份保存在[治理前归档](../archive/pre-pr24-rereview-20261018/README.md)。代码提交 b82b8d0、真实装配边界修正 7850fcc；最终业务/测试/随包指南及候选固定源码 **7850fcc02b04d1b572fcb89215945ee0f60be28c**。后续仅证据、现行导航与交付记录，原工作区及其他工作树未改动。

[独立审查结果原字节](evidence/architecture-quality-closure-01-rereview/input/review-result.original.json)及[原件 hash](evidence/architecture-quality-closure-01-rereview/input/identity.json)保留历史 CHANGES_REQUIRED；这份 JSON 不是审查01原 PDF。[前轮3d69607结果](architecture-quality-closure-01-results.md)、旧候选、历史失败和 D/RV 按形成时身份读取。

## 五项整改与固定源码

|问题|实际修改、固定源码|定向证据与结论|
|---|---|---|
|PR24-R01 / P1 回执身份恢复|[原身份登记/启动补齐](https://github.com/yuan2go/importer-pc/blob/7850fcc02b04d1b572fcb89215945ee0f60be28c/src/Ipc.Application.ParcelFlow/ParcelReplyIdentities.cs#L8)、[StoreRaw/RecoverRaw事务](https://github.com/yuan2go/importer-pc/blob/7850fcc02b04d1b572fcb89215945ee0f60be28c/src/Ipc.Application.ParcelFlow/ParcelRepository.cs#L160)。发生状态和ID/hash同事务；命名空间取原发生的request，外部错误RequestId不污染另一请求。保留首个hash及全部raw/归档BLOB|实际 Application 启动从 Awaiting raw恢复身份；事务中断回滚且原件不变；65次原Query淘汰旧投影后篡改同ID进入Conflict，Submit仍1次；旧DB4与归档BLOB缺索引可补齐；旧误判完成且原件矛盾时启动明确阻断、不改raw/DB。PASS，待复审|
|PR24-R02 / P2 Attached恢复原因|[RecoverLocalFacts](https://github.com/yuan2go/importer-pc/blob/7850fcc02b04d1b572fcb89215945ee0f60be28c/src/Ipc.Application.ParcelFlow/ParcelStages.cs#L140)只改变独立Upload状态/Failure，主State/Reason保持|真实DirectPlc已权威完成主链，Attached Dispatching中断重启后仍PhysicalCompleted且主Reason为null，Upload独立Unknown/Failure、原图保留。PASS，待复审|
|PR24-R03 / P2 离线消费者校验|[ParcelConsumers](https://github.com/yuan2go/importer-pc/blob/7850fcc02b04d1b572fcb89215945ee0f60be28c/src/Ipc.Application.ParcelFlow/ParcelConsumers.cs#L40)、[共用格式/拓扑规则](https://github.com/yuan2go/importer-pc/blob/7850fcc02b04d1b572fcb89215945ee0f60be28c/src/Ipc.Application.ParcelFlow/ParcelConfigurationRules.cs#L8)。校验Effective、Pending、冻结配置与阶段/包/实例/reader闭包，不加载扩展代码|未来Pending schema、跨站、悬挂包/reader、未来冻结配置均拒绝，原文件hash不变；合法精确Pending保有包；未知阶段、原接收无Owner保守阻断。PASS，待复审|
|PR24-R04 / P2 混合消费者分派|[装配分派器](https://github.com/yuan2go/importer-pc/blob/7850fcc02b04d1b572fcb89215945ee0f60be28c/src/Ipc.Assembly/ExtensionConsumerReaders.cs#L11)显式选择原Supply/Parcel读者，Factory/CLI/Product管理入口贯通；拒绝未知、歧义、链接及缺失根|真实Supply DB5与Parcel DB4只读检查不变更；实际签名包/Application旅程中Supply Pending阻卸载，清偿后无引用混合根不误阻卸载；备份仅Pending含包仍拒绝。PASS，待复审|
|PR24-R05 / P1 维护发送围栏|[资源登记/在途计数/维护](https://github.com/yuan2go/importer-pc/blob/7850fcc02b04d1b572fcb89215945ee0f60be28c/src/Ipc.Application.ParcelFlow/ParcelDeviceLifecycle.cs#L9)、[阶段双次检查与原Query](https://github.com/yuan2go/importer-pc/blob/7850fcc02b04d1b572fcb89215945ee0f60be28c/src/Ipc.Application.ParcelFlow/ParcelStages.cs#L22)、[认证](https://github.com/yuan2go/importer-pc/blob/7850fcc02b04d1b572fcb89215945ee0f60be28c/src/Ipc.Application.ParcelFlow/ParcelOperators.cs#L15)。先占资源再释放聚合锁；准入/Resume/继续/Submit/Query/健康/认证使用同一资源责任|共享WCS重连期间Resume、新件、原Query不能穿越；同服务在途调用拒绝维护，无关WCS调用不阻左相机维护；右位可恢复并连续完成。提供者忽略取消时超时不释放围栏，迟到返回记MaintenanceIncomplete；采集结束前不换reader会话，快照可读，原帧/旧epoch及租约保有。PASS，待复审|

共用规则只处理格式与技术能力，三个主流程继续各自拥有业务编排和判定。Sportmaster保留左右独立投递位、扫码取WCS有效格口且不下发PLC；DirectPlc/WcsToPlc各保留main顺序位。没有新增通用Application、流程引擎或未批准UI。

## 必要验证、真实入口与失败保留

完整命令、返回码、日志hash和宿主身份见[checks.json](evidence/architecture-quality-closure-01-rereview/checks.json)、[证据说明](evidence/architecture-quality-closure-01-rereview/README.md)。使用SDK10.0.402、真实Application/Factory/SQLite/装配及独立HTTP参考进程；外部门面仅注入延迟、丢回复或错误身份。

- `--review-closure`：五项新增定向验证；原件身份恢复及维护围栏正反例全部通过。
- `--architecture-quality`：容量/事务归档、分页、重启去重、备份、阶段隔离、真实配置CAS/审计/三个资源启动点断进程、HTTP限量/取消通过。继承已有两阶段激活，不重复开发或改旧批准记录。
- `--acquisition-lifecycle`、`--application-architecture`、`--backup-health-recovery`：采集保有/重连互斥、旧epoch、双位独立、健康身份/TTL/取消、原Query、资源释放/恢复与SQL兼容通过。
- `--quality-extensions`：精确签名包安装中断沿原操作恢复、真实绑定/激活/单次主链、运行资源/原件/离线备份/混合Supply消费者阻卸载及清偿后卸载通过。
- 锁定构建：测试工程、Sportmaster真实装配、DirectPlc发布、旧Product、Sportmaster WPF win-x64交叉构建通过，零编译错误；交叉构建不代表Windows运行。新依赖已记录全部受影响锁文件，外部NuGet版本未升级。
- 新ZIP独立解压核对SHA256SUMS、发布JSON及所有声明文件；实际Sportmaster装配启停/重启和WcsToPlc BeforeMain/Attached两条含认证、重量、原图上传、丢回执恢复及独立HTTP接收账的参考旅程通过。Release CLI真实安装/原操作恢复/卸载/操作账读回通过。

[原失败与中间日志登记](evidence/architecture-quality-closure-01-rereview/log-custody.json)保留修复前8项失败、编译错误、完整候选库存被拒绝及维护后未等新健康的测试失败。旧采集测试要求在取图未结束时重连成功，已改为断言等待取图结束、快照可读和原epoch/帧保有；维护后测试等待真实投影允许Resume/准入，没有放宽生产守卫。b82真实装配因调用内部RecoveryFiles编译失败，后续785分派器自行校验入口路径，并以真实Assembly/Product/WPF重建验证。编译失败后意外执行旧DLL的日志不计新源码PASS。未删除关键断言、跳过必要用例或吞异常。

资料校验保留既有267项历史错误，按(code,path)精确集合比较，不改校验工具、历史要求或断言换PASS；本轮结果见证据中的archive-error-comparison.json。

## 候选、兼容与受控恢复

|新不可变候选|源码|ZIP SHA256|
|---|---|---|
|sportmaster-portable-rereview-fixed|7850fcc02b04d1b572fcb89215945ee0f60be28c|28e8f5ebda3559f71424ba65b69f2c97e68adf10452913ed08f57ea260995a5c|
|wcs-to-plc-portable-rereview-fixed|7850fcc02b04d1b572fcb89215945ee0f60be28c|a2e34dd7d6c333c0fae35a23cc87ed07f7f0be925973c1e88fc7a448efdb17cf|

[候选解包清单](evidence/architecture-quality-closure-01-rereview/candidate-readback.json)及[兼容字节核对](evidence/architecture-quality-closure-01-rereview/compatibility.json)。本地候选与失败产物独立保留，不覆盖先前3d/e1/c666/ee59989候选。仓库只提交身份及验证证据，没有提交私钥、运行信任根、SDK或候选二进制。

Parcel SQL1～4及Supply SQL/格式5保留原字节；没有新DB迁移、公共契约或扩展协议语义变更，Application.Contracts仍1.6.0。新增装配维护依赖：Parcel携带Ipc.Supply/Ipc.Supply.Contracts读者，Product携带ParcelFlow/Application.Contracts读者，不启动对方业务或SDK。DirectPlc输出确认无Ipc.Integrations及其他两条Parcel主流程。

精确新宿主的签名示例为Debug **1.6.8**、Release **1.6.9**；[身份](evidence/architecture-quality-closure-01-rereview/capabilities-1.6.8-rereview.ipcx.identity.json)和[Release身份](evidence/architecture-quality-closure-01-rereview/capabilities-1.6.9-release-rereview.ipcx.identity.json)区分宿主785与声明插件输入最后变更b82，不混称同一身份。旧1.6.0～1.6.7包不覆盖/重签，原请求、合同、目标、配置/实现hash及原图不改写；已有未结仍需匹配旧发布/精确包沿原身份清偿。升级先保存完整原DB/WAL、配置、raw/归档、包及发布；回退只使用匹配的升级前一致备份，并先核对外部动作，不能倒退业务事实。

旧DB4缺回复身份索引可从原raw/归档补齐；已有假完成和矛盾原件会明确阻断，不自动“修正”为成功/失败、不生成新的外部发送。该类既有现场异常需要获批原协议调查和数据处置，保留持续责任。

## 未验证边界及后续

[Windows及现场逐项验收清单](validation/architecture-quality-closure-01-windows.md)已增加回执恢复、维护发送围栏和混合消费者步骤。

- **NOT_RUN**：Windows实际安装/运行、WPF交互与多窗、UAC/ACL、厂商SDK线程模型、真实断电、生产长跑节拍/磁盘/内存；本轮未重复旧Supply完整运行或Inspection全量验收，仅完成受影响Product构建及真实Supply DB5维护读者验证。未生成本轮win-x64完整交付包。
- **BLOCKED**：真实设备、获批WCS/PLC合同、实体/联锁/复位及现场权限/条件不足；独立参考进程和TestOnly签名不能替代。Production不因本轮参考PASS自动Ready。
- **BLOCKED**：AUDIT-01原PDF仍未取得，不伪造替代、没有直接写main；AUDIT-02及其原结论保持独立历史身份。
- 待独立复审R01～R05；现场若已有矛盾原件/误判完成，另行获批原件调查和处置。历史267项资料问题维持独立清理范围，不能用本轮验证宣布旧D/RV全部关闭。
