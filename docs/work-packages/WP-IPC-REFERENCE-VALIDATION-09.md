> 当前增量结果：[REVIEW-FOLLOWUP-11](WP-IPC-REVIEW-FOLLOWUP-11.md)，运行源码 `4edb909e71097a7da61506beaa10ae5db82ec6cb`。两个P2已补源码、Product组合所有权三故障动态参考验证PASS；Windows/真实集成仍NOT_RUN/BLOCKED。下文a080825和形成时记录保留其原版本含义。

# WP-IPC-REFERENCE-VALIDATION-09：07/08/10核心证据与同版交付


> 当前纠偏结果：**REFERENCE_PASS / WINDOWS_NOT_RUN / REAL_CAPABILITIES_BLOCKED**；SourceCommit `a08082563bc13b2f146e71c4be9136f0469af9db`。见[09同版报告](../runtime/reference-validation-09.md)及[检查](../runtime/reference-validation-09-checks.json)。本次实施/合成验证授权已执行；生产WCS/设备动作未授权，D/RV未变。

## 形成时工作包定义与记录

以下保留形成时状态与原要求，不能读作当前不测试禁令或本次结果；[原字节快照](../archive/pre-reconciliation-01/README.md)可核对原版本。

状态：**PLANNED / EXECUTION_NOT_RUN**；日期2026-10-16。基准main `2579392ddcc0b4302d4aae865d4b2d8b3c083b59`；依据 [MAIN-REVIEW-03](../reviews/main-static-review-03.md)。本包用于区分“工程源码路径”“核心合成运行”“Windows操作”和“真实现场”四层；用户当前不要测试，本轮不执行本包。

## 开始条件

1. 收到明确恢复相应验证的授权；重新核对真实main、08/10合并结果及并行变更，冻结待验源提交。测试/构建/发布各按实际授权范围，不从本文件存在推定授权。
2. 使用当前工程真实命令与锁定依赖，先读build/publish/verification入口；不假定旧二进制就是新源码。当前Storage3 / SIMULATION-v1，发布元数据若因08/10改变必须同步记录。
3. 只使用可审查合成输入、新数据根、独立loopback接收账及可保留的旧格式样本；无Windows和真实SDK亦可先覆盖核心。不得访问真实WCS或控制设备。

## V-01：验证资产对齐（先修改源码，后执行）

- [ ] 对齐`AlignmentDataVerification.LegacyMigration`构造时隐式迁移与format2预期，改为只读检查原格式、默认启动拒绝、原格式备份/身份回执、显式迁移及隔离恢复的独立断言。
- [ ] 对齐`HardeningVerification`迁移数量/最低读者预期为现行三条历史和3:3，继续固定schema1/id2/id3 checksum，保留旧读者拒绝、未知未来迁移拒绝和失败不改原库的断言。不能简单删掉旧兼容检查。
- [ ] 合成健康长时场景通过来源确认事件维持有效性，静默则应过期；不延长生产TTL、不以每次取Snapshot伪造来源新鲜度。
- [ ] 给08新增路径补针对性验证：所有历史入口的身份状态矩阵；seed/Effective/PendingActivation/恢复身份差异、两个工位争同设备、部分取得失败与来源未启动证明。

本轮以上均未修改或执行。静态识别的旧预期不一致不是一次已执行FAIL，也不能标成环境BLOCKED后忽略。

## V-02：按受影响风险执行核心合成验证

|范围|必须覆盖的行为|证据身份|
|---|---|---|
|E-01/E-03|业务拒绝后非法/合法重扫、等待新回执不显示旧任务；来源/代次/版本竞争及命令幂等；其它来源和Unknown/预留保留|Observation、Admission、Operation/revision、Command/Receipt、原请求/响应|
|E-04|format1/2只读检查/备份成功及失败不改格式；备份可读；回执先于迁移；迁移失败、写回执失败、未来格式/校验拒绝；原格式回退与隔离|原库/备份/回执SHA、旧/新程序配置、schema历史、隔离状态|
|E-05|来源静默过期、重复确认不续期、未来确认拒绝、新受控代次可达、旧健康/观察拒绝、旧实体许可失效|设备/来源/epoch、确认/失效时间、许可与阻断|
|E-06及08A|空箱与旧箱关闭链、明确字段/时区/状态、分页边界/保留删除、空与失败、全部入口未登录/有效/退出/过期|查询条件/上界、会话/作业/请求/Attempt、身份状态|
|E-08/E-09|两通道相邻提示、序号去重、满队列/失败；单项Stop/Dispose失败不跳过独立清理，超时任务继续持有依赖/租约，原异常保留|提示序号、任务终态、来源所有权、原始与清理异常|
|E-10及08B|最终生效配置与实际持有设备一致，另一工位争同设备、seed差异/恢复/激活回退、部分租约失败无先启来源|最终配置版本/稳定设备集合、租约键、RuntimeId与源启动计数|
|继承关键恢复/防重|T1/T2/T3中断、Unknown不换ID重发、固定payload/hash/contract/target、唯一接受账、关闭屏障与恢复隔离|原请求、发送尝试、接收账、接受事实及恢复数据根|

在授权范围内运行适当基线、受影响模块及关键恢复/防重/资源检查；发现真实失败则修复并重跑受影响范围，不能吞异常换PASS。通过后仅在新增变化/失败/疑点时扩大验证，不循环重跑无关项。

## V-03：构建、发布与后续入口

- [ ] 明确获准后执行必要还原/构建；记录真实SDK/NuGet/源码、命令和结果，不把代码阅读当编译结果。
- [ ] 明确获准后形成新离线包及必要发布启动冒烟，绑定SourceCommit、源码/配置/模块指纹、ReleaseVersion/StorageVersion/ContractVersion与ZIP SHA；失败保留原包/日志，不覆盖旧证据。
- [ ] 新增`docs/runtime/reference-validation-09.md`、`reference-validation-09-checks.json`及获准公开的合成证据；没有执行就不预建成功日志、截图、ZIP或SHA。
- [ ] UI10改动后的应用契约和组件路径亦纳入受影响范围，具体几何/触控不以核心结果代替。给 [Windows06](WP-IPC-WINDOWS-VALIDATION-06.md) 同一最终源提交和包；跨平台核心/包检查不关闭W-01～06。给 [集成05](WP-IPC-INTEGRATION-BOUNDARY-05.md) 的只是参考能力边界，不是SDK/WCS支持声明。

退出：已授权范围有真实PASS/FAIL/BLOCKED/NOT_RUN和原始身份，未验边界清楚，08遗留问题有归属；生产放行仍需相应D批准、真实接口/设备和现场/Windows结果。交付使用开发分支main PR，不自行合并。
