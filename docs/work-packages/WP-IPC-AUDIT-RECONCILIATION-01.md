# WP-IPC-AUDIT-RECONCILIATION-01：纠偏执行与同版参考交付

执行基准main：`856e4de73f09a1c4f12af798c4d21e8631a6a86f`；开发分支：`codex/wp-ipc-audit-reconciliation-01`。本次用户明确授权实施08、10及09的合成/参考测试、构建、启动和发布验证，不授权生产WCS写入和真实设备动作。源快照见[纠偏前证据](../archive/pre-reconciliation-01/README.md)，顺序见[执行计划](../superpowers/plans/2026-10-16-ipc-audit-reconciliation.md)。

目标与约束：沿现有模块化单体补齐工程残留、v1.1 WPF和持续参考闭环，不重建框架、不修改设计基准迁就代码。07已完成机制直接继承。SQL/checksum、原请求/目标/合同、历史issues、D批准和RV状态保留；真实缺项按05逐能力隔离，不让Production静默模拟成功。

|执行范围|实现/证据入口|验收边界|
|---|---|---|
|08A 历史权限|Supply历史/报警/详情/类型化查询/配置与Unknown查询统一有效身份；桌面失效反馈|应用合成身份矩阵与Windows实际呈现分别验证|
|08B 配置所有权|最终Effective/Pending选择、站点/模式核验、实际设备资源租约、失败保旧|软件所有权验证不代替Windows ACL及设备端控制权|
|10 PAGE01～08|[逐页映射](../runtime/ui-baseline-alignment-10.md)|源码/交叉构建不等于实际WPF截图或Windows验收|
|09 核心与同版交付|[验证报告](../runtime/reference-validation-09.md)|真实运行与历史结果分开；包固定SourceCommit与字节身份|
|06 Windows|[同版待验记录](../runtime/windows-06.md)|无Windows目标时W01～06保留NOT_RUN，环境为BLOCKED|
|05 真实集成|[能力边界](../runtime/integration-05.md)|SDK/WCS/实体/数量/身份/现场证据分别BLOCKED|

退出要求：交付具体源码、实际运行证据、可运行包/指纹、复现命令与剩余边界，开发分支创建main PR供审查，不合并、不强推。原工作包的形成时记录已逐字节保存，本次授权覆盖过期“不要测试”的当前限制；不倒写形成时NOT_RUN。
