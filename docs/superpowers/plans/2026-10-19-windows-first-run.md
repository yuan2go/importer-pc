# Windows首次启动自动部署 Implementation Plan

> 使用当前会话逐项实施；用户已授权修复并要求完整交付，不重复请求方案或执行确认。

**Goal:** 双击Windows客户端后，软件自动完成受保护机器策略及既有数据初始化，允许UAC限权的原用户运行。

**Architecture:** Platform拥有固定机器策略安装与只读验证；Assembly负责原用户到固定提升helper的启动衔接；现有ParcelRuntime继续拥有数据锁/配置/SQLite初始化。脚本Run/Console复用相同准备入口，Diagnose仍只读。

**Tech Stack:** 既有C#/.NET10、WPF、PowerShell7维护脚本、Python打包。

## Global Constraints

- 范围及历史替代规则见WP-IPC-WINDOWS-FIRST-RUN-01；不删除/覆盖策略、租约或数据，不新增业务/数据库格式。
- 默认目录与显式旧目录一致；初次准备失败不能进入业务。未提权进程创建业务数据。
- 不新增包或信任，不把本次安装称为Production或Windows实际验收通过。

### Task 1: 账户与首次安装机制

Files: `src/Ipc.Platform/MachineRuntimePolicy.cs`, new `MachineRuntimeSetup.cs`, `tests/Ipc.Verification/RuntimeIdentityVerification.cs`。

- [ ] 在既有`--runtime-identity`入口增加deny-only允许及安装顺序回归，先记录RED。
- [ ] 账户判定只把启用的管理员组算作提权，继续拒绝服务SID和其他账户。
- [ ] 实现`MachineRuntimeSetup.EnsureRuntimeIdentity(Action<string> install)`及`InstallForAccount(string sid)`。纯顺序函数接收调用者SID、策略存在性、安装回调和最终校验，供合成验证调用。Windows安装固定ProgramData路径、受保护ACL、独占安装锁及原子发布，不调用业务。
- [ ] `/Users/yuan/.local/share/ipc-dotnet/dotnet run --project tests/Ipc.Verification -- --runtime-identity`通过；Windows分支单列NOT_RUN。

### Task 2: 接通全部启动入口和默认路径

Files: new `src/Ipc.Assembly/ParcelRuntimeSetup.cs`, `Ipc.Assembly.csproj`, `Program.cs`, `ParcelAssembly.cs`, `ParcelCli.cs`, `scripts/application.ps1`, `application.cmd`, `scripts/test_runtime_identity.py`。

- [ ] 增加Run/Console执行`runtime-prepare`、Diagnose仅`runtime-check`与无参数默认路径用例，先记录RED。
- [ ] 提升helper严格仅接受`--action runtime-install --runtime-sid SID`，在任何业务注册前分发。原用户SID传入helper，使用结构化ProcessStartInfo参数、runas、完整退出码及回读校验。
- [ ] 直接Assembly构造前调用准备；StartAsync的只读门禁保持。Run/Console缺DataRoot时使用既有LocalApplicationData路径；双击CMD直接启动桌面，维护参数仍沿现有脚本。
- [ ] `IPC_TEST_PWSH=/tmp/ipc-pwsh/pwsh python3 -m unittest discover -s scripts -p test_runtime_identity.py -v`通过。构建Sportmaster/DirectPlc/WcsToPlc装配和Sportmaster WPF。

### Task 3: 文档、固定媒介与PR

Files: `docs/INDEX.md`, `docs/design/framework.md`, `framework-decisions.md`, `docs/runtime/windows-application-delivery.md`, `getting-started.md`, `runtime-identity-preflight-01.md`, new results, `scripts/package-application.py`, `docs/manifest.json`及checksum。

- [ ] 改当前指引，保留旧证据，说明首次UAC、默认数据根、现有策略错误、取消及重试；不再要求基本参考启动手工provision。
- [ ] 更新随包启动说明，提交源码；从固定SHA构建portable和win-x64，解包核hash，portable新根snapshot/quit并检查SQLite integrity；不使用生产设备。
- [ ] 结果记录实际命令/退出码/固定源码与媒介hash，更新manifest。`git diff --check`、指南测试及同基线归档差额核对。
- [ ] 开发分支推送、创建main PR并附加当前任务；不合并main，报告Windows实际NOT_RUN及现场BLOCKED。
