> 当前参考实现/候选指向 [14报告](conformance-fix-14.md)；B-01～06仍BLOCKED，不因14核心修复或候选改变。Windows沿13统一后验，真实SDK/WCS/实体/数量/身份审批未取得，Production仍NotReady。下方09/11身份仅为历史。

> FIX-14当前指引：本文件下方13及更早“当前/本轮/已完成”保留形成时身份，已由 [14执行基线](conformance-fix-14-baseline.md)接管。A-01～04是待修确定缺陷；健康隔离和维护闭环是有效要求，不能按旧完成性表述判通过。Windows沿13决定统一后验，不寻找VM/runner；D/RV、真实SDK/WCS/现场门禁继续。原字节见 [归档](../archive/pre-conformance-fix-14/README.md)。

> 当前待验版本绑定[REVIEW-FOLLOWUP-11](review-followup-11.md)的新SourceCommit/包SHA。下文上一版边界保持；没有新增Windows/真实集成执行，W01～06 NOT_RUN，B01～06 BLOCKED。

# INTEGRATION-BOUNDARY-05 当前逐能力状态

2026-10-16；归属[AUDIT-RECONCILIATION-01](../work-packages/WP-IPC-AUDIT-RECONCILIATION-01.md)。本次授权只含合成/参考工程实施与验证，未取得生产WCS写入或真实设备动作授权。源码/包身份引用[09检查](reference-validation-09-checks.json)，不复制第二套D批准登记。

|能力|状态|缺失证据与准确受阻部分|已独立完成的参考边界|
|---|---|---|---|
|B-01 / D-01/14 旧工程与SDK|BLOCKED|未取得可核验既有现场C#工程、型号/固件/SDK位数和版本、官方回调/缓冲/停止样例及分发/联调许可；真实驱动与旧迁移不能声称完成|继承现有F-02参考工程，来源代次/健康/停止与所有权机制及合成验证|
|B-02 / D-02/07 WCS|BLOCKED|未取得真实版本合同、CAPI映射、副作用/预留、幂等/查询权威范围、关闭、认证和联调授权|SIMULATION-v1仅IPv4 loopback；原请求字节/哈希/目标/合同、Unknown不重发及独立接受账|
|B-03 / D-05/06 实体|BLOCKED|相机—实体位—箱/AGV映射、入场/许可/离场/复位权威与时效、最终动作执行者/联锁未获确认|显式模拟Admission/Depart/Reset，接受不自动离场，新epoch不重授权旧实体|
|B-04 / D-03/04 数量|BLOCKED|真实数量单位/需求归属、并发分配、自动/人工、保留/移除方向、剩余件/预留处置规则未获批准|SYNTHETIC-KEEP-v1可持续参考拆包与唯一接受数量；不作为生产默认|
|B-05 / D-08/11/10 身份|BLOCKED|真实认证/跳过、换班、特殊关闭/处置权限、人工核对SOP和扫码合同缺失|显式SIMULATION角色，有效身份与过期/登出鉴权；旧请求责任不因登录改写|
|B-06 / D-09/12/13/15/16、PUB-01 现场与资料|BLOCKED|目标工控机/显示输入、容量/保留/节拍指标、验收人员及本组以外原稿/资产/SDK公开许可不足|WPF交叉构建与同版参考包；[Windows06](windows-06.md)实际客户端操作仍NOT_RUN|

所有真实必需能力缺失时，`SupplyConfiguration.Validate`和产品装配拒绝Production；没有注册manual/replay/loopback为Production成功回退。发布启动冒烟核对Production拒绝时接收端调用数不增加，数据根不伪造生产成功。其结果只证明此参考产品拒绝规则，不表示已实现真实能力。

客户原稿、私密来源、凭据、生产数据、SDK/字体没有进入本次交付；限定v1.1生成资产沿用既有公开授权。未新增批准人、未改D/RV、未修写历史issues或原HTML逻辑。后续取得材料时按[05工作包](../work-packages/WP-IPC-INTEGRATION-BOUNDARY-05.md)逐能力登记来源/版本/指纹/实际批准与验证，再解除相应阻断。
