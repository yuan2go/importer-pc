# 三项维护P2 · 04证据

[结果](../../ux-maintenance-p2-04-results.md) · [命令及边界](checks.json) · [准确测试输入](tested-source.json) · [双轴复核](independent-review.md) · [日志字节身份](log-custody.json)。实现源ab8f4d4，主基线be79564；该UI实现后仅同步版本/精确依赖与随包指南，产品冻结ff31b0e / 1.3.3；后续为交付说明、manifest和证据。[实际候选](https://github.com/yuan2go/importer-pc/releases/tag/ux-reference-ff31b0e)及ARTIFACTS/SHA256SUMS区分原1.3.2。

冻结后的四个定向入口包含38条窗口、63条配置/列表、49条Busy/Pause投影、103条两位共享Application检查。三业务WPF及原生验证器只做交叉构建；原生检查源码已经增加页签/列表/只读和schema2实际Application下转换错误、未提交文本、取消/显式放弃。Windows运行、截图、DPI/焦点、账户/安装/更新恢复及负载均NOT_RUN。

close-red保留原关窗丢草稿反例；close-generation-first-failure保留本地中间实现遗漏子窗错误诊断的失败；close-reentrancy-red保留真实WPF关闭约束下重入失败。修正后完整生命周期断言保留并通过。其余两个本地C#夹具字典类型编译错误已在执行记录中报告并修正，未作为功能通过证据。

公开日志替换隔离worktree路径，属于明确的衍生副本；RawSha/PublishedSha分别登记，原日志在本地.artifacts及Release raw-verification-evidence.zip保留；没有将衍生字节冒称原始日志。输入均为合成参考、未使用真实设备或客户数据。

原生关闭限制参考[dotnet/wpf Window源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs)的InternalClose/VerifyNotClosing；事件替身只模拟该边界，不是Windows运行证明。原生后验沿[既有完整矩阵](../../validation/sportmaster-alternate-ui-windows.md)。

版本组合C#/Python17项通过。五ZIP CRC/逐文件hash/冻结SHA和Windows PE身份通过；三个core实际解包冒烟、签名选装/CAS/激活/失败恢复/退休、RCS原报文与raw回执/重启不重POST通过。完整原始CombinationRun及事件、selected/rcs旅程见raw ZIP，公开摘要不替代原字节。source-parity.json核全部生产/配置/脚本/示例/随包指南与产品冻结源一致。
