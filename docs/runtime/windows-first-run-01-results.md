# Windows首次启动自动部署结果 · WINDOWS-FIRST-RUN-01

实施基线main `524916b2088f267175bec46daf01e6df020932b7`，当前固定实现/候选源码 `1760200b21199914d73db787c053eacb190d136d`。用户提供的Windows失败排查是本次问题输入，不冒称本环境已复现Windows令牌、UAC或ACL。主工作区受限未跟踪资料未动；原D/RV、原请求/数据库schema/签名包身份与旧候选不改。

## 交付行为

- 完整Windows候选解压后双击`application.cmd`或`desktop/Ipc.Desktop.exe`即可启动，基本桌面路径不依赖PowerShell。首次只有缺少机器策略时请求一次UAC，软件建立固定机器目录/策略/ACL。用户无需手工创建目录/JSON、设置权限或另建账户。
- 安装helper通过固定参数在任何业务注册前执行；不接收任意目标目录，不加载扩展、不读业务库、不启动设备。核对helper/Platform与当前客户端字节及已发布注册完整性后才请求提权。使用另一管理员凭据授权时仍绑定原用户SID。
- 原未提权进程在安装退出后完整读回策略，再进入既有数据锁/配置/SQLite流程，自动创建默认`%LOCALAPPDATA%\ImporterPc\data`。显式旧根不替换；已有策略不重写、不自动换账户。坏策略/不安全ACL/丢策略但有租约或扩展材料均保留并拒绝。
- UAC deny-only管理员成员允许日常启动；启用管理员权限、服务账户或不匹配SID仍拒绝。只读`runtime-check`/Diagnose及直接StartAsync门禁保留，Run/Console复用`runtime-prepare`。取消UAC后可重启重试；安装并发由保护目录内保留的独占锁串行化，临时文件落盘后无覆盖发布。

实现：[MachineRuntimeSetup](../../src/Ipc.Platform/MachineRuntimeSetup.cs)、[ParcelRuntimeSetup](../../src/Ipc.Assembly/ParcelRuntimeSetup.cs)、[启动脚本](../../scripts/application.ps1)。当前操作见[Windows指南](windows-application-delivery.md#首次启动与自动配置)，设计授权与替代关系见[工作包](../work-packages/WP-IPC-WINDOWS-FIRST-RUN-01.md)。未修改WPF页面/布局/原HTML。

## 验证事实

[检查与原日志hash](evidence/windows-first-run-01/checks.json)记录实际边界。原4项脚本基线PASS；新增回归先RED（缺安装机制及脚本入口），后7项脚本方法GREEN。共享身份/准备顺序验证涵盖deny-only允许、启用管理员/服务/其他SID拒绝、原SID传递、已有策略不提权、取消/安装/读回失败不继续。脚本使用合成子进程，C#身份用合成claims，不冒充真实Windows。仅临时脚本副本移除平台门禁，真实校验和/分支/退出码逻辑不变，合成原数据字节保持。

三个Application装配最终源码构建PASS，0警告/错误；Sportmaster实际WPF self-contained win-x64发布PASS。2项随包指南回归及三个产品完整指南渲染PASS。没有重复全量业务/恢复/负载测试。

两个最终ZIP均重新解包，核完整SHA256SUMS、文件集合及CRC；Windows app/desktop含helper且Ipc.Assembly/Ipc.Platform字节一致。[固定候选记录](evidence/windows-first-run-01/artifacts.json)保存完整SHA256与文件数。portable在其他cwd `/tmp` 使用全新未创建数据根实际启动，Started→snapshot→quit退出0，自动建库，快照作业数0，退出后只读SQLite integrity_check=`ok`。另起两个实际portable进程使用同一新数据根，第二所有者被原租约拒绝，第一所有者仍能读取快照并正常退出；[原资源回归](evidence/windows-first-run-01/resource-ownership.log)保留，不扩张为Windows文件锁实测。原完整stdout保留；首次核验器把缩进JSON当逐行JSON解析而失败，随后修正解析方式，读取原stdout完成核对，没有改产品换PASS。

候选仅在本地工作树`.artifacts/first-run-windows-1760200.zip`与`.artifacts/first-run-portable-1760200.zip`，未覆盖旧Release，也未把新源码的构建称为旧媒介已修复。最终证据/manifest提交不改变这些固定媒介的源码或随包文件。

## Windows实际后验与剩余边界

Windows原生执行、UAC提示/取消、真实账户/ACL/链接路径、并发首次安装及实际WPF双击仍 **NOT_RUN**；真实SDK/协议/设备/现场 **BLOCKED**，Production **NOT_READY**。交叉发布/portable冒烟不能替代这些验收。

在获授权独立Windows环境解包并核独立ZIP hash后验证：

1. 没有机器策略的普通账户及UAC过滤管理员分别双击：一次授权后自动安装，绑定原SID，默认数据目录/库自动创建；第二次启动不提权。标准用户使用不同管理员凭据也保持原SID。
2. 首次拒绝UAC：不进业务、不创建业务数据；重新启动授权后成功。并发两次启动只有同一最终策略；第二运行所有者仍由既有锁拒绝。
3. 已有策略反复Install/Run不改字节/ACL；其他用户不重绑。畸形、普通账户可写、链接、丢策略但留lease/扩展材料均保留并失败，DB/WAL/config及原请求不因失败改变。
4. 在源码验证环境执行`dotnet tests/Ipc.Verification/bin/Debug/net10.0/Ipc.Verification.dll --windows-runtime-setup`（独立提升会话）：只在临时合成根核安装/幂等、坏策略、原租约及错误ACL拒绝，不修改真实机器策略。本次只编译了此入口，没有执行。
5. 用新候选指定原停止数据根检查既有恢复/迁移守卫；真设备与Production另按原准入，不因安装成功取得权限。

归档使用同一带Pillow解释器比较固定main与本分支，具体差额见[记录](evidence/windows-first-run-01/archive-delta.json)。历史原资产错误保留，不删断言、不加豁免。

## 提交与PR

设计f6fe0d7、实现61ede7d、安装媒介校验与链接防护1760200、验证归档a6c8282；[PR #44](https://github.com/yuan2go/importer-pc/pull/44)已创建并关联本任务，未合并main。最终回执只更新本报告/计划/manifest，候选仍固定1760200。Windows实际待验边界不变。
