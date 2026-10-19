# 全部运行入口身份门禁 · 2026-10-19

基线main `524916b2088f267175bec46daf01e6df020932b7`，业务1.3.3；[本工作包](../work-packages/WP-IPC-RUNTIME-IDENTITY-PREFLIGHT-02.md)继承[原修复与Windows后验](runtime-identity-preflight-01.md)。本次重新读取GitHub及源码，题述P1对旧PR35源码成立；当前main已经包含PR41（merge `9f4b32f319d3a66d993cfbafcdda90656e7fd687`）的Parcel前置修复。PR42为维护P2，PR43为发布回执。PR35原线程仍isResolved=false，这不代表当前main仍缺原修复，也不证明Windows发生过数据库修改。

## 真实执行顺序与本次差额

|入口|身份校验及持久资源顺序|
|---|---|
|application.ps1 Diagnose/Run/Console；CMD/快捷方式|完整性检查 → runtime-check只读机器策略/schema/ACL/SID门禁 → 诊断/部署检查或运行子进程；原修复保留|
|三个Parcel控制台/桌面直接EXE|ParcelAssembly.Create门禁 → 注册/构造 → ParcelRuntime.StartAsync再次门禁 → seed读取 → 数据锁 → 配置账 → ParcelRepository打开/SQL迁移 → 配置/冲突/原件恢复 → 能力创建；直接Application API由StartAsync门禁覆盖|
|Supply Product/Assembly/WPF/直接ProductRuntime|**本次补齐**StartCore try内首行门禁 → manifest/seed读取 → 扩展初始化 → 原租约政策解析 → 数据锁 → SupplyStore打开/迁移 → 配置激活/恢复 → 驱动启动；原Diagnostics/清理与非零退出保留|
|Inspection Assembly/直接InspectionRuntime|**本次补齐**StartAsync生命周期检查 → 同一门禁 → seed读取/Simulation约束 → 数据锁 → JSON账本读取或原子创建；该业务无SQLite，不冒称数据库复现|
|独立离线/扩展维护|仍在运行实例构造前独立派发；原ResolveLeaseRoot、安装权限、数据锁及原件守卫保留。Supply export需要运行实例，沿日常账户政策；不将其解释为任意账户离线导出|

Supply原来在数据锁和数据库前比较指定SID，因此未发现“其他SID先改库”的同一旧缺陷；剩余为指定SID错误配置为管理员时没有日常普通账户约束，且扩展初始化早于完整身份门禁。Inspection没有机器账户门禁，任何可写数据根的账户均可建立其Simulation账本。两处均复用既有MachineRuntimePolicy.RequireRuntimeIdentity，无新政策格式/配置开关或测试绕过。基座、Application、驱动及组件职责不变，业务/Contracts/Storage版本不变，原SQL及请求身份未改。

当前主线政策拒绝指定SID之外账户、管理员启用/deny-only组及SYSTEM/本地/网络服务。UAC限制令牌下IsInRole可能返回false，依据[微软WindowsPrincipal文档](https://learn.microsoft.com/en-us/dotnet/api/system.security.principal.windowsprincipal.isinrole?view=net-10.0)；本包不改变已存在的claims判定。开放PR44拟调整首次安装及UAC政策，本包不纳入或重写其代码；后续整合仍须保留所有入口的统一前置门禁并复验最终政策。

## 定向验证

6项PowerShell/Python回归通过：三运行入口允许/拒绝子例、原Parcel源码顺序、Supply/Inspection源码顺序、5项独立维护派发。新增源码顺序回归先在未修复源码取得RED（仅Supply/Inspection缺少门禁），补两行后同一命令GREEN。首次编写维护用例时将两个既有action名写错，已按真实脚本更正为recovery-plan/restore，之后才记录正式RED；未修改业务脚本配合错误用例。PowerShell运行在macOS，临时副本仅排除OS/build门禁，业务子进程为合成程序，不能证明Windows令牌、ACL或原生文件访问。

共享C#身份判定验证指定普通SID允许，错误/空SID、四类管理员claims、系统服务SID拒绝，并保留旧租约维护语义；非Windows门禁明确仅开发路径。五种装配Supply、Inspection、Sportmaster、DirectPlc、WcsToPlc和Supply WPF交叉构建均0警告/错误。DirectPlc/WcsToPlc首轮构建的默认派生锁名没有匹配既有带连字符文件，已移除本轮生成的两份重复锁，并显式指定packages.direct-plc.lock.json/packages.wcs-to-plc.lock.json重新锁定构建通过；未改既有依赖锁。未运行全量业务/负载或真实设备套件。日志与精确命令见[定向检查](evidence/runtime-identity-preflight-02/checks.json)，公开日志仅将工作区路径替换为占位符并移除行尾空格。

复跑入口：

```sh
IPC_TEST_PWSH=<pwsh7> python3 -m unittest discover -s scripts -p test_runtime_identity.py -v
dotnet build tests/Ipc.Verification/Ipc.Verification.csproj -p:RestoreLockedMode=true -m:1 -nr:false
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll --runtime-identity
dotnet build src/Ipc.Assembly/Ipc.Assembly.csproj -p:BusinessModule=Supply -p:RestoreLockedMode=true --artifacts-path .artifacts/new-supply-build -m:1 -nr:false
# BusinessModule分别换为Inspection/Sportmaster/DirectPlc/WcsToPlc，输出使用独立新目录。
# DirectPlc和WcsToPlc另显式传入-p:NuGetLockFilePath=packages.direct-plc.lock.json或packages.wcs-to-plc.lock.json。
dotnet build src/Ipc.Desktop/Ipc.Desktop.csproj -p:BusinessModule=Supply -p:RestoreLockedMode=true -p:EnableWindowsTargeting=true --artifacts-path .artifacts/new-supply-desktop -m:1 -nr:false
```

## Windows与交付边界

同一带Pillow的Python对固定main快照及本分支执行归档检查，[差额](evidence/runtime-identity-preflight-02/archive-delta.json)无新增错误：main 268项，本分支更新manifest校验和后267项；265项历史PPTX媒介类型及2项历史metadata错误保留，归档整体仍FAIL。没有改历史资产或添加豁免。

Windows原生SID/ACL/UAC、CMD/快捷方式/WPF及直接EXE拒绝、数据库迁移恢复前未打开/写入的文件访问跟踪与前后目录/hash/时间/ACL对照全部NOT_RUN。后验继承01矩阵，并增加Supply专用Product/Assembly/WPF/直接API和Inspection Assembly/直接API：正确普通SID允许；其他SID/提升管理员/服务/缺失或畸形策略拒绝。每项使用隔离新根及停止的旧版本/恢复标记实验根，维护者保全原DB/WAL/config/标记与JSON账本，不借删除现场策略或租约制造用例；维护权限另按原入口测试。

真实SDK/设备/合同/现场BLOCKED，Production NOT_READY。本次不合并PR44、不更改D/RV、不关闭历史评审线程、不覆盖旧Release/签名扩展；源码修复与实际Windows部署分别验收。必要新媒介须固定新源码并重新核对精确依赖，不能把旧1.3.2/1.3.3包重标为包含本次代码。
