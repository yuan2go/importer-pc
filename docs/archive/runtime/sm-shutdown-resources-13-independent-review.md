# PR163 资源关闭独立复核

2026-10-31。复核基线 main `67ab87c1a1b146611504342c6ab080ed045231ec`，原PR头 `1413668d9d6b9c5f5321f05d921d8ce50c59cdf4`。本轮直接读取源码、原始日志及Git字节，在独立Linux工作树执行定向验证。证据及逐项状态见 [summary.json](evidence/sm-shutdown-resources-13-review/summary.json)。原Windows记录和本轮Linux验证分别记证。

## 结论及补修

两项原修复成立；新增复核发现一处仍会丢失后台错误的历史路径，已在同PR补修。通过已执行的源码与合成边界验证后，可按仓库正常PR规则整合；这不授予真实相机、Windows原生或长稳验收资格。

- Application关闭先围住新输入并停止真实来源，等待原生调用、读取、工作任务、维护和外呼退出，再停止端口。Task已经Faulted/Canceled说明该Task终结，允许继续排空其余所有者；任务仍未结束时不因取消等待而释放资源。原PR按Task登记完整AggregateException，避免只留下await抛出的第一个异常；Stop与首次Dispose仍报告错误，实际释放后重复Dispose不重放旧错误。
- **补修：维护先清理失败任务会丢失异常。** `MaintainAsync` 原先直接移除所有已完成辅助Task。让原图入账先失败，再等待两个维护完成周期后关闭，原PR代码报 `Background failure was lost`。现在维护移除Faulted/Canceled任务前，登记到与关闭共享的Task键错误账；使用ConcurrentDictionary支持维护与关闭排空其他任务的并发，按同一Task去重，不把失败重新当作活动任务。成功任务正常清理。原请求、Pending原图及原PNG保持原义。
- 回归覆盖关闭期间失败的Stop→Dispose、直接Dispose，以及关闭前失败并经过维护的第三条路径。三条均验证错误可见、SQLite可重开、PNG保全、冻结WCS事实和HTTP请求计数不变、未伪造Saved；重复Dispose前后原生调用序列必须完全一致。
- MVS切换在持有lifecycle时同时等待原采集Task与缓存边界。原采集Task退出而边界未完成时，明确失败并保留隔离；finally释放lifecycle，让Stop真正归还缓冲、Stop/Close/Destroy/Finalize并释放租约。原生调用仍阻塞时，取消切换只结束等待，不能释放租约或销毁句柄。Capture在持锁期间不能被另一轮Start/Stop替换，Reject在inspectionGate内只完成对应barrier，不接管新轮次。Probe的87项合成检查涵盖提前退出、取消、缓冲归还失败、销毁失败重试和重复停止。

## acquisition-lifecycle 超时判定

原PR的31份日志长度/SHA256及7份被测源码Git Blob与摘要一致。原PR与作者main对照日志均在23条PASS之后、`AcquisitionLifecycle.RuntimeCases`恢复准入等待处超时。

本轮又在两个独立工作树分别构建并执行**未修改的**`--acquisition-lifecycle`：原PR生产源码与最新main都在相同23条断言后超时。两者快照均为：

- 左位 `IngressInterrupted:left`、不准入；右位正常准入。
- `slow`与`independent`均SlotResolved且ImageReleased；旧请求/采集完成与输入责任清偿是不同事实。
- 测试重连使旧epoch排队输入形成未核对责任，只调用AcknowledgeOverload、Resume和ContinuePrepared，没有调用合法的InspectInterruptedIngress清偿原输入责任，随后直接等待左位Admit。

相关Ingress、旧epoch判断、PositionReady和InspectInterruptedIngress生产代码未被PR修改。由源码路径、原始日志及独立同源对照共同支持：这是继承的测试旅程缺口，不是本PR新增的输入责任。该套仍记**FAIL/TIMEOUT**；后半段Unknown与重启场景没有被这次运行覆盖，不能借对照宣称全程无风险。未删除或放宽IngressInterrupted，未自动核对、丢弃或重放输入。

## 验证边界与夹具

本轮Application、Probe构建零警告/错误；资源关闭、resource-risks、原图、audit03-input与MVS Probe按各自日志记录。故障注入使用合成SDK/解码端口，SQLite、PNG及loopback HTTP实际执行，不能解释为真实设备长期稳定性验收。

扩大验证时，camera-rounds夹具固定加载win-x64的ZXing.dll；manual-supply与manual-rcs-debug使用Windows受保护Production草稿。Linux环境无法完成这些路径，保留阻断原日志，不改生产门禁或伪造Windows通过。

离线激活首次运行缺少显式NUGET_PACKAGES；配置实际已还原缓存后，又复现等待谓词的`Single`在重连健康项暂时不存在时直接抛错。该`Single`在main已存在。仅将等待中的三处查找改为SingleOrDefault及null等待，重复设备仍会抛错，后续配置、epoch、原请求次数及原生清理所有权断言保留。失败与修正轮分别记证。

未重跑Windows WPF、未操作当前客户端/真实相机、未执行客户RCS、安装、发布或目标机24/72小时验收；均NOT_RUN。原PR的Windows证据只对其原SourceCommit有效。未启用或调度GitHub Actions。

本轮仅新增复核记录与合成证据，不改写历史验证摘要/索引。清单按暂存Git字节更新，继承原PR来源。合并时固定实际已审查head，不绕过保护、不强推；最终合并事实以GitHub PR与main读取为准。
