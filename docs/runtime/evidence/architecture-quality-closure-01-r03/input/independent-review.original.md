# PR #24 独立复审

结论：**CHANGES_REQUIRED**。Standards轴剩余1项P2，R03未完全闭合；Spec轴对本轮R01、R02、R05未发现有证据支持的剩余阻断。R04分派缺陷在本次核验范围内关闭。此结论只覆盖列明源码及参考验证，不构成Windows/SDK/现场验收。

固定head：**3f6f762d31655ef0fe8d6562ed413beed682e2f9**；main：**b2dc55b60fa6a3da88964d7b75413f1b7cda9c4d**。比较基线a2e987a8d15eb397ad1a40cf1a303bdad4ec397b。审查结束远端仍为此head、PR OPEN、main未变。PR23/26未作为本head的修复。仅在仓库外创建隔离副本、合成探针及证据；未修改原工作树已跟踪源码、文档或PR，未合并。

## Standards

### P2 · R03未完全关闭：schema1/2矛盾实例可被去重隐藏

触发：兼容schema1/2中，CapabilityInstances含原PLC/维护绑定，再加入同PLC InstanceId但Package/Module/Implementation不同的第二实例，Extensions仍null。离线检查应拒绝矛盾配置，实际返回零引用。

已独立复现：schema1 Pending、schema2 Pending、Effective及ConfigurationHash匹配的未结FrozenConfiguration均返回references=0；检查前后DB/config SHA256完全相同。schema3的相同矛盾正确拒绝。未来schema和直接悬挂的主绑定也正确拒绝，所以这不是整体校验缺失，而是旧schema的重复实例绕过。未执行实际卸载；确认的是供卸载使用的零引用证明错误。

固定证据：

- [ParcelConsumers.cs:78](https://github.com/yuan2go/importer-pc/blob/3f6f762d31655ef0fe8d6562ed413beed682e2f9/src/Ipc.Application.ParcelFlow/ParcelConsumers.cs#L78)：只检查每个Bound是否存在于库存，未先检查库存身份唯一性。
- [ParcelConfigurationRules.cs:42](https://github.com/yuan2go/importer-pc/blob/3f6f762d31655ef0fe8d6562ed413beed682e2f9/src/Ipc.Application.ParcelFlow/ParcelConfigurationRules.cs#L42)：schema<3提前退出拓扑校验；重复/矛盾校验在同文件第72行，旧schema不执行。
- [ParcelTopology.cs:26](https://github.com/yuan2go/importer-pc/blob/3f6f762d31655ef0fe8d6562ed413beed682e2f9/src/Ipc.Application.Contracts/ParcelTopology.cs#L26)：All按InstanceId去重，先保留主角色绑定而丢掉含包的矛盾项；后续包闭包检查看不到该项。

违反AQD-07.3“矛盾实例均保守拒绝、不以零引用替代不确定性”。应在去重之前对所有受支持schema校验原库存的唯一性、相同身份内容一致性和完整包引用；验收覆盖Effective/Pending/未结Frozen三处。该问题是本次审查阻断，不将零引用返回当作安全卸载许可。

R04：[装配分派器](https://github.com/yuan2go/importer-pc/blob/3f6f762d31655ef0fe8d6562ed413beed682e2f9/src/Ipc.Assembly/ExtensionConsumerReaders.cs#L11)选择业务所属读者，Parcel Factory/CLI及Supply Product运行/CLI均接入。真实Supply DB5与Parcel DB4只读检查通过，未知/歧义/链接根拒绝。实际新签名包完整卸载旅程本次未独立执行。

## Spec

本轴没有确认的遗漏、范围漂移或剩余实现错误；不把推测或外部SDK缺件报成源码缺陷。

|条目|结论及本次证据|固定源码|
|---|---|---|
|R01|原发生request绑定永久ID/hash，恢复事务回滚不动原raw；旧索引、归档BLOB、缺件及矛盾保守阻断；超过64条后篡改仍Conflict，合法新物理完成仍可接受，Submit不重发|[ParcelReplyIdentities.cs:8](https://github.com/yuan2go/importer-pc/blob/3f6f762d31655ef0fe8d6562ed413beed682e2f9/src/Ipc.Application.ParcelFlow/ParcelReplyIdentities.cs#L8)、[RecoverRaw:214](https://github.com/yuan2go/importer-pc/blob/3f6f762d31655ef0fe8d6562ed413beed682e2f9/src/Ipc.Application.ParcelFlow/ParcelRepository.cs#L214)|
|R02|Attached Dispatching恢复及失败/缺raw保持主State/Reason；真实DirectPlc定向重启通过|[ParcelStages.cs:140](https://github.com/yuan2go/importer-pc/blob/3f6f762d31655ef0fe8d6562ed413beed682e2f9/src/Ipc.Application.ParcelFlow/ParcelStages.cs#L140)|
|R05|资源在释放聚合锁前登记和计数；慢WCS期间Resume/新件/原Query均不穿越；超时仍占资源，Stop取消不释放原应用租约；认证在途阻重连，维护期间认证不调用端口；迟到返回非成功；现有左右独立用例重新执行通过|[ParcelDeviceLifecycle.cs:16](https://github.com/yuan2go/importer-pc/blob/3f6f762d31655ef0fe8d6562ed413beed682e2f9/src/Ipc.Application.ParcelFlow/ParcelDeviceLifecycle.cs#L16)、[ParcelStages.cs:22](https://github.com/yuan2go/importer-pc/blob/3f6f762d31655ef0fe8d6562ed413beed682e2f9/src/Ipc.Application.ParcelFlow/ParcelStages.cs#L22)|

## 独立执行与边界

新隔离副本锁定构建测试工程、真实Sportmaster Assembly和旧Product均成功。重新执行--review-closure、--architecture-quality均成功，后者包含容量/归档/重启和三个配置资源启动点终止进程。仓库外另写spec-probe与maintenance-probe，分别调用真实生产Repository/ApplyReply及Application/Factory/SQLite；延迟门面只注入外部故障，不替代业务流程。standards-probe完成并复现R03缺陷，其退出0表示探针完成，不能解读成产品PASS。

证据：review-result.json固定源码和DLL hash；standards-probe/results.log与只读合成数据库；spec-probe/run2.log；maintenance-probe/results.log/result.json；原始build与两项选择器日志。初次spec外置夹具缺原生SQLite库的失败日志保留，随后补正确原生依赖完成，未修改产品源码。

NOT_RUN：Windows实际运行/多窗/UAC/ACL、WPF实机、完整Windows打包、真实断电/生产长跑；本次没有独立重建新签名包或执行完整安装恢复/卸载周期，未重复其余全量测试。配置断进程仅是受控进程终止，不等于真实断电。

BLOCKED：真实SDK/设备/WCS/PLC合同及现场权限/条件；AUDIT-01原PDF仍未取得。既有267项资料问题没有在本次重新运行全量工具，不把上一轮数字当本轮独立PASS。旧epoch健康Unavailable投影交错未取得阻断级实证，不列为已确认发现。

轴汇总：Standards 1项P2；Spec 0项确认阻断。建议修复R03上述兼容schema缺口后再复审。

工作区负面证据：原main的五个未跟踪资料根保留，PR工作树仍clean，已跟踪文件无差异。收尾发现原main新增一个参考peer lease目录（进程已结束、无活动句柄），归属未确认，未修改/删除；见workspace-end.json。不把该并发/夹具残留归因成已确认源码问题。
