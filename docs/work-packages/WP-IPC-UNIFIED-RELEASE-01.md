# UNIFIED-RELEASE-01：main 统一 Windows 发行

当前最终交付见 [55025ce 完整八包发行](../runtime/unified-release-latest-55025ce.md)：PR53六原包身份不变，补齐同源Supply/Inspection，实际发布11资产；14门禁与八入口定向检查完成，完整Windows/现场边界未关闭。下文各阶段来源及停止记录按形成时保留。

当前交付：已从 main cfe6c0e 构建并实际发布统一参考媒介，核验结果与剩余 Windows/现场边界见[实际交付](../runtime/unified-release-main-20261020.md)。下文早期基线和停止记录按形成时阶段读取，不能取代此最终来源。

当前冻结准备：远端 main 已推进至 4f21b2d，包含 PR47/48/50；本包继承其商业化实现和当前发行指南，整合重叠脚本为单一入口。此前 c445 基线及 0f2cd71 候选保持原身份，不能代替当前业务 1.4.0 / Application.Contracts 1.10.0 / DB8 的验证。此项更新下文形成时“不引入未合并增量”的语境，因为这些增量现已合并。

2026-10-20 恢复授权：用户重新明确要求实际构建与必要定向验证；此前停止指令及中断回执作为历史证据保留，不能继续限制本次构建。最新 main 仍 c445b0e；PR47/48/49 尚开放。复用本包既有分支和脚本，核对 PR48 的重叠修正，不引入未合并产品增量。新输出使用全新目录和新冻结提交，停止回执与旧部分 ZIP 不重写。

2026-10-20 用户授权实际核查并整合 PR44/45/46 的发行成果。开工 main 为 `c445b0e`，开放 PR47 属于另一个尚未合并的商业化增量；本包从 main 独立分支实施，不引入其业务/DB8/UI功能。原工作区及未跟踪附件保持。既有 e79a8bf/16ecb57 介质、原发布记录、D/RV 和原失败不改写。

本包替代现行入口中“本次没有生成统一介质”作为后续行动的限制：历史发布记录仍准确描述原任务，本包负责新固定源码、新媒介、新证据。完整 COMM-00～10 仍按原工作包推进，本包不宣称完成它。

复用 scripts/package-application.py、package-business.py、DeploymentMedia、WindowsDeploymentSetup、ParcelStartupServices、MachineRuntimePolicy 及现有扩展静态装配链。统一发行三业务 Windows 自包含安装媒介、三业务 portable 对照媒介以及 Supply/Inspection portable；后两者当前没有相同原生安装器。统一意味着同一冻结源码、工具链和可核验清单，不是将多个业务放进一个运行实例。

要求：固定 main 祖先与 PR 合并来源；仅从干净已提交源码导出构建；SDK/锁文件/配置/指南/工具身份留证；同媒介公共 DLL 字节一致；精确业务版本、合同及依赖闭包一致；ZIP 路径/重复/CRC/完整文件和 SHA256 对照；失败保留日志且不产生成功回执；输出不可覆盖；旧扩展不继承新宿主资格。采用 PR47 已有打包层长路径/管道排空/SourceLink 隔离修正时仅继承对应机制并注明来源，不重做第二套框架。

必要验证限打包门禁、运行身份策略及入口顺序、现行安装/离线修复/扩展隔离风险、实际媒介的只读完整性与启动拒绝/静态身份。Windows UAC、ACL、另一账户、受保护实际安装、中断/升级恢复及人工桌面交互单列 NOT_RUN；真实 SDK/合同/设备/现场仍 BLOCKED。不为测试改机级政策、不绕过身份门禁、不自动控制设备。

依赖锁遵循 [NuGet locked mode](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)；固定源码和调试来源参考 [Source Link](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/sourcelink)。重复构建的二进制比较和 ZIP 逐字节可重现分别记账，不以一次成功宣称全部可重现。
