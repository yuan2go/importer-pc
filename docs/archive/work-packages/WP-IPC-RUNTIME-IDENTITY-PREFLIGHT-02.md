# 全部运行入口身份门禁 · RUNTIME-IDENTITY-PREFLIGHT-02

2026-10-19用户授权直接检查、修复、定向验证并提交main PR。基线为最新main `524916b2088f267175bec46daf01e6df020932b7`，业务1.3.3；题述1.3.2及PR35 P1是较早快照。PR41已合并原SID修复，PR42合并维护P2，PR43记录发布。继承[01工作包](WP-IPC-RUNTIME-IDENTITY-PREFLIGHT-01.md)、framework/F决策、既有机器策略及公开范围；不重写原结果。

## 设计与范围

三个Parcel Application已有脚本runtime-check → ParcelAssembly.Create → ParcelRuntime.StartAsync的只读门禁，位于数据锁、配置账、数据库迁移/恢复前。新增差额为Supply运行入口只有较晚的ResolveLeaseRoot（未执行日常普通账户约束），Inspection持久JSON账本没有机器账户门禁。两者直接EXE/装配/API均应在读取启动配置与创建持久资源前调用同一RequireRuntimeIdentity，不新增可配置绕过或第二身份机制。

只补脚本会留下直接API路径；改变ResolveLeaseRoot的全局语义会影响独立维护权限。因此选择在ProductRuntime.StartCore的try内首行及InspectionRuntime.StartAsync的生命周期检查后调用既有门禁，保留ProductRuntime诊断/清理与Inspection异常传播。保留非Windows开发路径，不改变业务、驱动、组件、Contracts、DB schema及原请求。Inspection仍仅Simulation，不因此取得Production资格。

开放PR44涉及首次安装与UAC策略调整；本包不修改MachineRuntimePolicy、Parcel脚本或其首次安装路径。后续整合须让全部入口继续调用同一最终身份政策，不能丢弃任一门禁；PR44不是本包已合并依赖。

## 实施计划

- [x] 在scripts/test_runtime_identity.py增加Supply/Inspection真实源码顺序断言，先执行取得RED；扩展既有合成维护分支回归，保留各维护入口参数。此断言只证明源码结构，不证明Windows拒绝或数据库不变性。
- [x] 修改src/Ipc.Product/ProductRuntime.cs和src/Ipc.Application.Inspection/InspectionRuntime.cs，在首个配置读取/扩展初始化/锁/账本访问之前执行`MachineRuntimePolicy.RequireRuntimeIdentity();`，不更改其他路径。
- [x] 同一PowerShell定向回归GREEN；构建并运行Ipc.Verification的`--runtime-identity`共享判定；分别构建五种Ipc.Assembly装配及受影响Supply WPF，0警告/错误。Supply与Inspection使用隔离新根执行snapshot/quit启动冒烟，不触及设备或现场数据。
- [x] 更新本包结果、Supply/Inspection部署说明、INDEX及manifest。证据保留命令、基线SHA、退出码和范围；交付分支codex/runtime-identity-all-entrypoints，实际main PR由GitHub记录，不自行合并。

命令入口：`IPC_TEST_PWSH=<pwsh7> python3 -m unittest discover -s scripts -p test_runtime_identity.py -v`；`dotnet build tests/Ipc.Verification/Ipc.Verification.csproj -p:RestoreLockedMode=true -m:1 -nr:false`后执行其DLL的`--runtime-identity`；装配构建使用`-p:BusinessModule=<Supply|Inspection|Sportmaster|DirectPlc|WcsToPlc> -p:RestoreLockedMode=true --artifacts-path <isolated-output> -m:1 -nr:false`。DirectPlc/WcsToPlc须显式指定`-p:NuGetLockFilePath=packages.direct-plc.lock.json`/`packages.wcs-to-plc.lock.json`，不使用未匹配既有文件的派生锁名。

Windows原生SID/ACL/UAC、直接EXE拒绝、既有数据库迁移/恢复前的文件访问跟踪及拒绝前后字节/ACL对照仍NOT_RUN。真实SDK/设备/协议/现场BLOCKED，Production NOT_READY，D/RV及历史证据不改。旧媒介不会随源码修复更新。
