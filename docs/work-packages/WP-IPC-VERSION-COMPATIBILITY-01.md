# WP-IPC-VERSION-COMPATIBILITY-01

用户要求：业务版本贯通发行清单，建立业务模块、组件和驱动兼容声明与组合验证记录；最新main/并行PR核对，先清理过期文档，再实现、验证、提交main PR，不重做已有能力。

[设计](../design/version-compatibility-01.md) · [运行指南](../runtime/version-compatibility-01.md)。执行计划（writing-plans，当前会话顺序执行）：

- [x] 版本与发行：src/Ipc.Platform/BusinessCompatibility.cs / ApplicationRelease.cs，三个业务csproj和内嵌config/compatibility声明，compositions与打包版本一致；验证错误业务版本、缺声明、降级、改字节均拒绝。
- [x] 装配与配置：ParcelAssembly/ParcelCapabilityFactory复用精确包/能力校验，任何未声明提供者在设备构造前拒绝；验证候选配置、默认/可选绑定与签名包身份，保留旧开发/历史路径。
- [x] 组合记录：scripts/application_compatibility.py和既有package/smoke入口，记录发行物/实际配置/提供者/场景/原始输出，RUNNING/PASS/FAIL及拒绝覆盖；拒绝复用其他组合结果。
- [x] 验证交付：受影响C#/Python验证、三个portable发布解压冒烟、WPF兼容构建、可选前置/Attached及失败记录、必要签名扩展激活。保存失败证据、同步manifest，提交推送并创建main PR，不合并。

不修改其他工作树、未授权资料或旧证据。核心源码/打包冻结后执行候选，实际身份、结果和剩余边界写交付报告。Windows实际NOT_RUN，真实SDK/现场BLOCKED。

最终运行闭包c4e1319，业务1.2.1/精确参考扩展2.0.1；依次继承已合并PR23/24/26，再继承仅文档PR28。验证已完成，实际检查、候选及原失败见[证据](../runtime/evidence/version-compatibility-01/README.md)，远端PR完成后单独回执。

已创建并读回[PR #29](https://github.com/yuan2go/importer-pc/pull/29)，OPEN、main目标、创建时head1570b32、MERGEABLE/CLEAN，未合并。随后仅补本回执及manifest，运行闭包保持c4e1319。

## PR29 P1/P2整改执行计划（writing-plans，当前会话执行）

用户已授权修复、针对性回归、文档/边界更新并推送原PR，不合并。基线main baac260214f910dca9ae4628ba6449b25188e2ac / PR29 d954ba6；并行PR27 226ac9b、PR30 822ce92、PR31 f068156不纳入。不改其他工作区。

- [x] P1测试先行：scripts/test_application_compatibility.py增加原始snapshot与自洽改标配置、缺/错结果及场景、输入事件降级测试；先运行保留真实失败日志。修改scripts/application_compatibility.py，schema2绑定输入事件并校验实际配置/场景/结果，finish拒绝无事实PASS；既有schema1不倒写。
- [x] P2测试先行：ParcelCapabilityFactory提取原恢复校验为共享ReadPublishedRelease入口，VersionCompatibility.cs在三个业务上验证正确schema2通过、错版本/schema1/声明字节拒绝；先保留原三参实现FAIL，再补实际businessAssembly并用于安装/恢复/卸载。无真实提权或设备控制。
- [ ] 运行PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p test_application_compatibility.py -v及指南/选装包回归；dotnet build tests/Ipc.Application.Verification/Ipc.Application.Verification.csproj -m:1 -nr:false，执行--version-compatibility。必要三种WPF兼容构建；固定源码后新建WcsToPlc portable和Sportmaster win-x64候选，验证默认/BeforeMain/Attached真实进程与负例。
- [ ] 将原审查原件hash、red/green日志、新候选/运行身份及未验边界写docs/runtime/evidence/version-compatibility-01-rereview和整改结果；资料校验与267项历史失败分开。复核diff、提交并推送codex/version-compatibility，读取PR29 head并保持OPEN，不合并。
