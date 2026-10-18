# WP-IPC-VERSION-COMPATIBILITY-01

用户要求：业务版本贯通发行清单，建立业务模块、组件和驱动兼容声明与组合验证记录；最新main/并行PR核对，先清理过期文档，再实现、验证、提交main PR，不重做已有能力。

[设计](../design/version-compatibility-01.md) · [运行指南](../runtime/version-compatibility-01.md)。执行计划（writing-plans，当前会话顺序执行）：

- [x] 版本与发行：src/Ipc.Platform/BusinessCompatibility.cs / ApplicationRelease.cs，三个业务csproj和内嵌config/compatibility声明，compositions与打包版本一致；验证错误业务版本、缺声明、降级、改字节均拒绝。
- [x] 装配与配置：ParcelAssembly/ParcelCapabilityFactory复用精确包/能力校验，任何未声明提供者在设备构造前拒绝；验证候选配置、默认/可选绑定与签名包身份，保留旧开发/历史路径。
- [x] 组合记录：scripts/application_compatibility.py和既有package/smoke入口，记录发行物/实际配置/提供者/场景/原始输出，RUNNING/PASS/FAIL及拒绝覆盖；拒绝复用其他组合结果。
- [ ] 验证交付：受影响C#/Python验证、三个portable发布解压冒烟、WPF兼容构建、可选前置/Attached及失败记录、必要签名扩展激活。保存失败证据、同步manifest，提交推送并创建main PR，不合并。

不修改其他工作树、未授权资料或旧证据。核心源码/打包冻结后执行候选，实际身份、结果和剩余边界写交付报告。Windows实际NOT_RUN，真实SDK/现场BLOCKED。

最终运行闭包c4e1319，业务1.2.1/精确参考扩展2.0.1；依次继承已合并PR23/24/26，再继承仅文档PR28。验证已完成，实际检查、候选及原失败见[证据](../runtime/evidence/version-compatibility-01/README.md)，远端PR完成后单独回执。
