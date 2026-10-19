# 运行账户SID前置修复 · 2026-10-19

基线main `be7956447054054dfdcfce725e8fbbbdfc87407a`；PR37远端已MERGED，head `1d486b34d3e3bcce2c6f0040a347bd1a5264024c`，merge `07095fdf4e04519934076ede24bfa8607207f9bc`。PR39/40也已合并，无开放PR。工作范围见[本包](../work-packages/WP-IPC-RUNTIME-IDENTITY-PREFLIGHT-01.md)。

## 独立复核与修复

main与PR37的`scripts/application.ps1` blob均为`b8c33d111713f124a2eefb6882ae33857022dd43`。原Diagnose先启动业务身份/部署检查，随后才用脚本局部比较SID；Run和Console没有同等检查。[PR35 P1原线程](https://github.com/yuan2go/importer-pc/pull/35#discussion_r4310270689)开工时仍未解决。ParcelRuntime.StartAsync还会在能力工厂检查SID之前取得数据目录锁、创建/恢复配置及打开迁移数据库，因此仅补脚本不能覆盖直接EXE/API启动。此为源码顺序证据，不能称为Windows已发生的数据损坏。

新增`MachineRuntimePolicy.RequireRuntimeIdentity()`，复用原机器策略/schema/固定LeaseRoot/ACL/SID验证，不写目录/锁/DB。`runtime-check` CLI调用同一门禁；脚本Diagnose/Run/Console在其他业务子进程之前先调用它。`ParcelAssembly.Create`和`ParcelRuntime.StartAsync`也先验证，覆盖三个Application、两布局、直接EXE和直接Application启动；UI仍保有既有启动诊断和失败退出路径。日常门禁额外拒绝指定账户中的Administrators组及SYSTEM/本地服务/网络服务SID。现有ResolveLeaseRoot和安装/离线维护分支保持原权限语义，不全局封禁管理员维护。

管理员判定取WindowsIdentity.UserClaims中的启用/deny-only SID，覆盖UAC过滤令牌。[微软WindowsIdentity实现](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Security.Principal.Windows/src/System/Security/Principal/WindowsIdentity.cs)显示Groups会过滤deny-only组，UserClaims的AddGroupSidClaims则分别产生GroupSid和DenyOnlySid；[IsInRole官方说明](https://learn.microsoft.com/en-us/dotnet/api/system.security.principal.windowsprincipal.isinrole?view=net-10.0)说明UAC双令牌差异。新代码不做账户名字符串或本地化组名比较，不提权、自动换账户、改策略或删租约。

## 定向证据与复跑

证据目录：`docs/runtime/evidence/runtime-identity-preflight-01/`。修改前脚本实测Run/Console忽略合成的身份拒绝，仍启动子进程并返回0；Diagnose先调用两个业务子进程再因机器策略缺失退出。回归在修复后通过，拒绝时只调用runtime-check；正常参数和Inspect维护分支保持。首轮测试曾因macOS `/var`链接被原路径防护拒绝，修正测试临时根为真实路径后才取得上述RED，环境失败另保留。

```sh
IPC_TEST_PWSH=/absolute/path/to/pwsh python3 -m unittest discover -s scripts -p test_runtime_identity.py -v
dotnet restore tests/Ipc.Verification/Ipc.Verification.csproj --locked-mode
dotnet build tests/Ipc.Verification/Ipc.Verification.csproj --no-restore -m:1 -nr:false
dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll --runtime-identity
```

PowerShell用例在macOS/Linux使用合成可执行文件，仅从临时脚本副本移除Windows OS/build门禁，其余真实校验和/分支/子进程退出处理不变。身份拒绝由合成helper提供，不能验证Windows令牌或ACL；数据字节断言也仅属于该合成目录。C#用例验证真实共享判定的SID空值/不匹配、管理员启用/deny-only claims、系统服务SID，以及现有维护语义。源码断言验证门禁位于配置读取和数据锁之前，不把静态断言称为实机数据库不变性证明。

已通过：4个PowerShell回归方法（含三入口允许/拒绝子例）、共享身份判定定向执行、三个Application装配构建、Sportmaster WPF交叉构建（全部0警告/0错误）及3个随包指南回归。原日志与[检查记录](evidence/runtime-identity-preflight-01/checks.json)保留本轮真实边界。未运行全量业务/恢复/负载测试。

源码提交`f354d3ff8d8a46d3eaca02db61877be1fd506785`的Sportmaster portable候选已生成、重新解包并逐文件核SHA256SUMS；控制台Started→snapshot→quit退出0、作业数0，进程退出后只读SQLite integrity_check为ok。[冒烟身份记录](evidence/runtime-identity-preflight-01/startup-smoke.json)中ZIP SHA256为`39c4373a4bdf655b982283342e4a961bddf440b7dbdb9f7def0c2a38d9fe3048`，SHA256SUMS文件hash为`e05356df09f808fa3bf79342f07b058bf6c8ec2eeb02b0b356695b6ebf3e4da3`。该ZIP仅保留于本地`.artifacts/sid-portable.zip`，不作为Windows发行或覆盖已有Release。`runtime-check`在portable明确返回PortableDevelopmentOnly/WindowsPolicyChecked=false，不返回Windows已验。

最小启动复跑（使用全新绝对Simulation数据根；不提交任何实体/作业）：

```sh
python3 scripts/package-application.py --application sportmaster --target portable --output .artifacts/new-sid-portable --dotnet /absolute/path/to/dotnet
# 解压ZIP并核SHA256SUMS后：
dotnet /absolute/unpacked/app/Ipc.Assembly.dll --action runtime-check
printf 'snapshot\nquit\n' | dotnet /absolute/unpacked/app/Ipc.Assembly.dll --config /absolute/unpacked/app/config --data-root /absolute/new-simulation-root
```

源码后的证据提交只更新本报告、manifest和验证记录，不改变固定媒介中的源码/配置/随包指南；新Windows媒介及原生验收尚未生成/执行。

归档核对采用同一带Pillow的Python对固定main快照和本分支比较：[差额](evidence/runtime-identity-preflight-01/archive-delta.json)为未新增失败，刷新当前manifest后移除1项原校验和错误；265项历史PPTX媒介类型与2项历史metadata错误保持，归档整体仍FAIL。不修改历史资产或添加豁免换取PASS。

## Windows后验与交付边界

Windows实机NOT_RUN；真实SDK/设备/协议/现场BLOCKED，Production NOT_READY。后验使用此修复的固定源码/全新可信媒介，记录SourceCommit、ZIP/SHA256SUMS、OS/build、策略hash/ACL、令牌SID/组和退出码。在独立实验环境逐项验证：

|账户/策略|入口与预期|
|---|---|
|指定普通SID、有效受保护策略|runtime-check/Diagnose成功；Run/Console及直接EXE正常启动、退出；快捷方式继承Run；原租约仍阻止第二所有者|
|其他普通SID、提升管理员、UAC过滤管理员、SYSTEM/服务SID|Run/Console/Diagnose及直接EXE在配置持久化、DB访问/迁移/恢复之前失败，返回非零；桌面失败诊断关闭后退出2|
|指定SID被错误配置为管理员|启用和deny-only Administrators组都拒绝日常启动；不能靠取消提升绕过|
|缺失/畸形/不可读/可写策略、链接路径、错误schema/LeaseRoot/ACL|预检失败，不启动业务；维护修复保留原策略，不由程序重建|
|独立提升维护会话|Verify、ProvisionData及既有安装/Inspect/Backup/PlanRestore/Restore/Export按原权限和数据守卫执行；日常Run不能借维护权限运行|

拒绝场景同时覆盖全新数据根和有旧库/迁移/恢复标记的停止根：由获授权维护在前后读取完整目录清单、DB/WAL/config/标记hash及时间、ACL/owner，并以文件访问跟踪证明未打开DB；仅hash不变不能证明未读。桌面和原生控制台用相同Configuration/DataRoot，各入口单独留证。不得使用生产设备或删除现有策略来制造用例。

本PR不覆盖现行`ux-reference-6ce1d6d`发布，也不重新签名或覆盖旧扩展。新宿主公共DLL字节改变后，后续独立候选必须重新验证精确依赖/包/信任与组合，不能复用旧包资格。业务/Contracts/schema和原请求未变；D/RV及历史FAIL/PASS不倒写。
