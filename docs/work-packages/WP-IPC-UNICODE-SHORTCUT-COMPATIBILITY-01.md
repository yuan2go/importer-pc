# UNICODE-SHORTCUT-COMPATIBILITY-01：Windows 快捷方式兼容性与集成工作包

> 2026-10-26执行增量：用户已授权按本包实施、完成回归并提交代码/PR。继承PR110的980a1383，只对先红夹具确认的RCW生命周期、目录冲突及空目标补缺；[当前实现与证据](../runtime/validation/unicode-shortcut-compatibility-01.md)、[操作和集成](../runtime/unicode-shortcut-compatibility-01.md)。PR112已在43382c59继承原Unicode修复，但仍需继承本轮差量。下文“仅分析/尚未提交/尚未接入”是11:17工作包形成时的事实，不覆盖本段执行授权和后续回执；原快照字节保留。

形成日期：2026-10-26。初核快照：2026-10-26 03:12 UTC；末次状态核对：03:17 UTC（新加坡时间 11:17）。本轮交付为分析和后续执行工作包，未修改生产代码、运行安装/卸载或重新执行产品测试。工作包保存在独立 D 盘工作树；未提交、推送或合并。本包的新编号 USC-01～USC-10 只用于本项追踪，不替换 ST、REQ、BR、CAPI、D、RV 编号。

## 1. 执行结论与最新状态

推荐继续采用 **Windows Shell Link COM 的显式 Unicode 接口 `IShellLinkW`，结合 `IPersistFile.Load/Save`**，统一创建和归属读取。保留现有部署权威、中文产品名称、升级/修复资格和卸载保护。不能只修 Save 而继续用 WScript.Shell 读取归属。

**修复已存在于 PR #110，尚未进入 main。不要重新实现第二套。** 开工时必须再次 fetch 和核对当前头：若已包含等价修复，只补本包列出的实际缺口及组合验证。

|核查对象|固定身份与实读结果|执行含义|
|---|---|---|
|最新 main|`a8cd431456f73e83040f75a0c5e3cc1423440d1b`；DeploymentShortcuts 仍使用 WScript.Shell|main 的既有兼容性缺陷仍在；不能写成已合入修复|
|PR #109|已合并，合并提交即上述 main；GitHub merged_at 为 2026-10-25 17:11:46Z|相机/RCS独立延后配置不再是本包阻断，不重复整改|
|PR #110|开放；核查期间已从草稿转为待审，头仍为 `980a1383e4589717c9a147746b6fd6271ef69088`|末次API读取 updated_at=2026-10-26 03:14:20Z、draft=false；已新增 ShellLinkFile、Unicode 创建/读取/删除及三 ACP 探针；原 ST01/ST02/ST06/ST14 成果继承|
|修复前 CI|[40222910122](https://github.com/yuan2go/importer-pc/actions/runs/40222910122)：frozen-sportmaster、aqa01-boundaries 成功；p0-stability-boundaries 失败|失败步骤为 Existing Windows shortcut COM boundary，保留原 FAIL|
|代码页早期证据|[ci-ba3-followup.json](https://github.com/yuan2go/importer-pc/blob/963165d71aec1e037525fd7f17bc3ed81b560c1a/docs/runtime/evidence/p0-stability-01/ci-ba3-followup.json)：ACP1252 FAIL、936/65001 PASS|该 JSON 自身 CIRun 为 40222113898；它与 40222910122 是不同身份，不合并成同一次实验|
|修复后 CI|[40261696276](https://github.com/yuan2go/importer-pc/actions/runs/40261696276)：三个作业均 SUCCESS；原快捷方式步骤、新代码页步骤均 SUCCESS|P0 日志实际检出测试合并源 `95b1f29e4baf9574795b2835c39f2495a2976fff`，合并 `980a1383` 到 `a8cd4314`；三 ACP 汇总均 PASS。不能将此结果转记后续整合树|

本轮读取了 main 的 AGENTS、INDEX、framework、framework-decisions、D/RV 与公开范围；对照 PR110 的工作包、代码、测试、脚本、工作流和 CI 作业日志。机器可读核查快照见 [review-snapshot.json](../runtime/evidence/unicode-shortcut-compatibility-01/review-snapshot.json)。未下载或重发安装介质、受限附件或凭据。

源码定位：

- [main DeploymentShortcuts.cs](https://github.com/yuan2go/importer-pc/blob/a8cd431456f73e83040f75a0c5e3cc1423440d1b/src/Ipc.Platform/DeploymentShortcuts.cs)，blob `47bc4d110ce8af87f8efdb390e7ca32a77b12c27`。
- [修复后 DeploymentShortcuts.cs](https://github.com/yuan2go/importer-pc/blob/980a1383e4589717c9a147746b6fd6271ef69088/src/Ipc.Platform/DeploymentShortcuts.cs)，blob `380951cee7164b25faee3048c2a160ef2632dd02`。
- [ShellLinkFile.cs](https://github.com/yuan2go/importer-pc/blob/980a1383e4589717c9a147746b6fd6271ef69088/src/Ipc.Platform/ShellLinkFile.cs)，blob `5623e89dceeb2a5f9a0c3bc0bde0a56753ec0a44`。
- 同一修复源的 `tests/Ipc.Application.Verification/InstallerOptions.cs`、`scripts/verify-shortcut-codepages.ps1`、`.github/workflows/sportmaster-commercial.yml`。
- 生命周期调用链：`src/Ipc.Assembly/ParcelStartupServices.cs`、`src/Ipc.Platform/WindowsDeploymentSetup.cs`、`WindowsDeploymentMaintenance.cs`、`DeploymentPaths.cs`、`TrustedProgramRoots.cs`、`MachineRuntimePolicy.cs`。

## 2. 编码与 COM 机制分析

### 2.1 故障边界

main 的链路为托管 string → dynamic/IDispatch → WScript.Shell.CreateShortcut → TargetPath/Arguments 等属性 → Save。自动化字符串通常以 Unicode BSTR 传递，**使用 COM 或 C# string 并不保证自动化组件内部所有持久化步骤都使用宽字符接口**。

旧 CI 日志在 DeploymentShortcuts.Create 第45行报 FileNotFoundException：保存路径的中文目录和文件名变成问号。相同 Platform DLL、独立进程只改变 ACP 的实验在1252失败、936/65001通过。这支持“WSH 快捷方式持久化链存在依赖 ACP 的有损转换”的判断；未调试 Windows 内部实现，不能声称已定位它内部调用的某个 A 函数，也不能将所有 COM 组件概括为 ANSI。

中文无法通过1252无损表示，而936能覆盖本次常见中文；后者通过不能证明任意 Unicode 字符都安全。此问题同时威胁写入 `.lnk` 文件名、目标/工作目录/图标路径，以及随后读取 target/arguments 的归属比较。删除本身使用 .NET File.Delete；删除的关键风险在于前置读错归属、无法读取或误认目标，而不是把 File.Delete 简单归因为 ANSI API。

改变 chcp、控制台 OutputEncoding、CurrentCulture、PowerShell 文本编码，均不能证明原生 Shell Link 调用已获得正确 Unicode 路径。不得改系统区域设置、启用全机 UTF-8、换英文产品名、使用8.3短名、损失性转码或对问号作反向猜测。

### 2.2 选用接口与互操作合同（USC-01）

沿用 PR110 已有的 CLSID ShellLink `00021401-0000-0000-C000-000000000046`；显式查询 IID IShellLinkW `000214F9-0000-0000-C000-000000000046`。接口采用 IUnknown、准确的原生 vtable 顺序和参数宽度；string/StringBuilder 明确 LPWStr，长度以 UTF-16 字符计且包括终止符空间。不能用 IShellLinkA 或编码中性别名代替。

文件持久化使用 `System.Runtime.InteropServices.ComTypes.IPersistFile` 的 Unicode 文件名合同；Load 只读取，Save 使用明确的绝对目标路径。不要手写 `.lnk` 二进制格式或通过 powershell/vbscript 重新包一层 WSH。图标路径和 index 分开设置，避免把路径与 `,0` 的拼接当成原生 SetIconLocation 合同。

非 PreserveSig 的 void COM 方法将失败 HRESULT 转成异常，不能 catch 后返回空成功。还要理解 S_FALSE 是非负 HRESULT：GetPath 可以返回 S_FALSE 且空路径；“没有抛异常”不能证明完整取到 target。现有 ComTypes.Save 也不直接向 C# 暴露 HRESULT。用保存后重新 Load 的完整字段校验落实成功后置条件；如需要显式 HRESULT，应先核验完整接口 ABI，不能为了拿返回码随意改 vtable。

GetPath 使用 `SLGP_RAWPATH`（现代码为数值4，建议命名常量），不调用 Resolve。Resolve 会查找移动/改名后的目标，可能更新链接、搜索磁盘或显示 UI；这些行为不能参与归属授权。目标已不存在也应能读其原存储目标，不能找一个同名程序替代。

现代码为 getter 分配32768字符缓冲，但微软 GetPath 文档仍标示返回上限 MAX_PATH。**大缓冲不等于已支持任意长路径。** 对空值、截断、超出实际支持边界的输入应拒绝修改并保留原件；另设边界测试和明确限制，不缩短路径后继续安装。不可在本包中无证据宣布长路径兼容。

### 2.3 两种所有权必须分开（USC-02/USC-03）

**COM 对象所有权**：每次 new ShellLinkFile 拥有一次独立激活的根 RCW，IShellLinkW/IPersistFile 是同一 COM 身份的接口视图。只在唯一包装所有者中释放根 RCW，不能对根对象和每个接口别名各做一次 FinalReleaseComObject。不能把 RCW 缓存在静态字段、交给 UI/调用者共享，或跨 await 仍在使用时释放。构造器 Load 失败应释放已激活对象，再保留原异常。

PR110 已实现构造失败释放、using/finally，以及 Remove 在 File.Delete 前释放。当前 Dispose 直接 FinalReleaseComObject(instance)，尚无幂等标志和 disposed 访问守卫。现有调用通常只释放一次；这是可见的封装加固点，**不是已证明导致本次 ACP 故障的第二根因**。后续要求重复 Dispose 安全、释放后访问明确失败、原始 Load/Save 错误不被清理错误掩盖；不通过强制 GC/GC.WaitForPendingFinalizers 代替确定性释放。

线程模型也与字符编码分开。同步的激活→读写→释放留在同一调用线程；核对原生安装 helper、维修和验证进程实际 apartment。不能假设 WPF 的 STA 会覆盖 async 后续线程或控制台入口，也不能只凭 MTA 认定现有调用必然失败。若引入专用 STA，应由单一所有者管理消息泵、退出和异常；CLR 管理 RCW 时不随意添加不配对的 CoInitialize/CoUninitialize，更不能对已初始化线程强行改 apartment。必须验证实际使用的入口。

**快捷方式文件归属**：这是部署授权，不是 COM 引用计数。继续沿用原合同：候选精确路径、完整 target（OrdinalIgnoreCase）和原始 arguments（逐字匹配）；产品中文名和描述不是所有权凭据。同 target 不同 arguments 必须保留；不能 Trim、重排或以“命令等价”代替原字节比较。WorkingDirectory、Description、Icon 属于可修复投影字段，不在本包擅自改变既有归属合同。

稳定桌面升级仅允许：当前条目参数为空、原条目参数为空，且 previous 与 current 的 ApplicationId、RuntimeAccountSid、规范化 DataRoot 一致，target 精确属于 previous.ReleaseRoot/desktop/Ipc.Desktop.exe。正常路径的 previous 必须来自已核验的受保护原登记，不接受 UI 随意提供的结构体。

previous 缺省的修复分支只允许从可信程序根下、受保护且不超过65536字节的 deployment-location.json 恢复原身份；继续核验 ReleaseRoot 一致和同应用/账户/数据根。只读到一个同名 EXE、一个用户可写 JSON 或字符串前缀相同，均不能接管链接。

## 3. 生命周期与保护规则

### 3.1 创建与读取（USC-04）

1. 保持 Plan 的四种选项组合、历史默认、菜单三个版本化入口与精确桌面名 `菜鸟供包台.lnk`。菜单参数仍为 `""`、`--maintenance`、`--setup`。选项全关不创建目录或 COM 对象；绝不为绕过失败取消用户选择。
2. 上层继续从 CommonPrograms/CommonDesktopDirectory 取得系统目录，本地测试使用 D 盘隔离映射；不引入由低权限任意指定的提权写入路径。项目/缓存/产物仍在 D 盘，不擅改系统已知文件夹配置。
3. 每个候选在创建目录、读取、保存、删除前均维持 RequireOrdinaryAncestors 对目标及祖先的拒绝 reparse 规则；不因为新接口能跟随符号链接就删除守卫。路径不可访问与不存在要能区分，不得把访问失败统一按“空文件位”接管。
4. 已存在的条目先 Unicode Load，再按原归属合同判定；未知格式、损坏、无法读取、空 target 或不完整 target 保留原字节并报告。拒绝接管不应先改任何属性或保存；存在同名目录也不能当不存在的文件覆盖。
5. 新建使用宽字符 SetPath/SetArguments/SetWorkingDirectory/SetDescription/SetIconLocation、Unicode Save。保存后以新 RCW 重新 Load，验证完整文件名和 target/arguments/working directory/description/icon path+index；核对字符内容，不能只断言文件存在或“不包含问号”。每条成功才产生可核验的投影结果。

PR110 已做到上述接口替换和原归属守卫；第3项中存在性错误分类、第4项空/截断读取和第5项**生产路径保存后验证**仍需补证/按失败夹具补齐。当前字段回读位于测试，不能写成生产 Create 已自动回读。

### 3.2 修复和版本升级（USC-05）

WindowsDeploymentSetup.Install 当前先提交唯一受保护部署登记，再调用快捷方式投影；投影异常记录 `CommittedNeedsSystemRepair` 并抛出。保持该次序：新快捷方式失败不回滚业务数据库、原请求或以旧登记伪装新安装未提交，也不报告全部安装成功。

沿既有 RepairSystem → RepairSystemRegistration → CreateShortcuts 路径修复。只修复当前权威部署的可确认投影；缺失链接可补建，已归属链接可重建。原链接已改指向/改参数或损坏且无法证明归属，则保留并给出具体路径与原因，不能自动删除后重建以换 PASS。正常升级先建新版本入口和稳定桌面投影，再精确清理上一部署入口；新投影失败前不提前删旧条目。

针对中断写入，补以下有界恢复要求：新建失败不留下会被误认成功的条目；替换已归属条目时失败应保留可恢复原件。若现有直接 Save 不能满足，应在同一目标目录采用受控唯一暂存、Unicode Save→回读→最终发布；创建不得覆盖新出现的他人文件。只清理本次固定清单中的暂存，不扫描/递归删除桌面或菜单。四条快捷方式不宣称是一个文件系统事务；中途失败必须准确留下已完成/待修复状态。

实现暂存/替换前必须核对文件共享、目录 ACL、文件身份和祖先检查的实际保证：安装租约只序列化本产品操作，挡不住用户或其他进程同时改文件。一次 Exists/Hash 检查后再 Replace/Delete **不是原子 compare-and-swap**。不得以“再次校验了 hash”宣称消除了 TOCTOU；若不能建立足够的句柄/身份与路径保护，竞争时应保守拒绝。此项要求通过真实故障/并发夹具证明，再选择最小实现，不在无证据情况下引入通用事务框架。

### 3.3 卸载、旧版保护和遗留入口（USC-06）

Remove 仍按固定精确候选清单处理：当前中文菜单三入口、稳定桌面名、历史带 Application/commit/部署后缀的桌面名，以及历史 `Importer PC` 菜单下的三个入口。不得改成通配 `*.lnk` 或搜索所有含“菜鸟/Importer”的文件。

对每个已存在候选使用 Unicode Load、读存储的原目标和参数、先释放 RCW，再删除精确归属于待卸载版本的文件。新版稳定桌面已指向新版目录时，卸载旧版必须保留它。用户改指向、改参数和无关文件必须保留；目标 EXE 已不存在时也不能 Resolve 到其他版本以帮助“清理”。不因文件名乱码就猜测恢复旧中文名或扩大删除范围。

坏链接/拒绝访问/分享冲突/删除失败必须可诊断，保留原件；不能 catch 后继续报告整次卸载成功。当前 Remove 遇 Load 异常会抛出，卸载上层此前已将原登记置为 RemovedDataRetained；**退役事实不等于快捷方式清理完成**。应验证沿原维护日志、原介质与 ResumeRemoval 重入，不能重新提交卸载、重启已退役业务或删除数据来解锁。若残余用户链接有意保留，报告保留理由；不能用“都删了”描述。

## 4. 现有成果与后续差量

|项|980a1383 已有证据|下一执行动作|
|---|---|---|
|USC-01 Unicode 接口|IShellLinkW/ComTypes.IPersistFile、LPWStr、完整 vtable、raw path，不 Resolve|继承；核查 S_FALSE/空值/长度和真实入口，不重写接口体系|
|USC-02 RCW 生命周期|构造 Load 失败释放、using、删除前释放|补幂等 Dispose、释放后访问、Set/Save失败和循环故障的资源证据；只修证实的缺口|
|USC-03 归属|target/arguments、previous 同应用/账户/数据根检查保留；测试三种 previous 不匹配拒绝|补 previous=null 的受保护描述文件恢复及伪造/超配额拒绝|
|USC-04 创建/读取|中文目标/开始菜单/桌面；四入口完整字段回读；重复 Create|补独立读取视角、生产保存后置条件、空/截断/访问失败、非GBK字符|
|USC-05 修复/升级|升级稳定桌面、旧卸载保留新版测试通过|补提交后投影失败→RepairSystem、写失败/并发修改与暂存恢复；守住唯一登记|
|USC-06 卸载/遗留|历史后缀桌面清理、异目标/异参数保留、坏链接保留|补历史 Importer PC 三菜单、失效 target、ResumeRemoval失败后重入和删除失败|
|USC-07 安全边界|RequireOrdinaryAncestors 原代码未删除；原安装/备份/登记守卫继承|实际测 leaf/ancestor reparse、同名目录/不可读文件；不以源码仍有守卫代替测试|
|USC-08 三代码页|新脚本和CI三ACP成功；GetACP不符直接失败|继承证据身份；增量改动后重新运行，保留原断言和失败原件|
|USC-09 组合集成|PR110 对 main a8cd4314 的测试合并CI全绿|PR108/111/112组合树重新取证；不转记单分支PASS|
|USC-10 交付与现场|安装器构建/既有冻结job通过|正式安装/升级/修复/卸载、目标IPC与长期现场另验，不据CI关闭|

表中“补”指尚未从本轮已读源码/日志看到对应完整证明，不表示它们都已经复现为生产缺陷。先写失败/保护断言定位，再决定是否改代码；能够补证的内容不重复开发。

## 5. 必要回归矩阵与证据合同

### 5.1 ACP 必须是真实进程值

继续使用 PR110 的三个独立 apphost：manifest activeCodePage 分别为 en-US、zh-CN、UTF-8，预期 GetACP 分别为1252、936、65001。每个进程在 COM 断言前打印并严格匹配实际 GetACP；不匹配即 FAIL/明确不支持该探针环境，不能降为 PASS 或静默跳过。不能改用 `dotnet <dll>` 启动来宣称已加载 apphost 的 manifest。

官方支持边界：Windows11/Server2022（build20348+）支持按 locale 选非UTF-8的进程代码页；Windows10 的该 manifest 元素只支持 UTF-8。若验证其他 Windows 版本，需原有 ACP 不同的独立受控镜像/VM，不修改当前系统区域设置；不能以 zh-CN manifest 在不支持系统中成功启动当作936已测。当前 CI runner 为 windows-2025，不等于目标 Windows11 工控机已验。

### 5.2 三种 ACP 每种都必须执行

|用例组|必须断言的结果|
|---|---|
|R01 名称与选项|菜单/桌面四种选项、历史默认；固定 `菜鸟供包台.lnk`；菜单三个精确参数；都关闭不触碰目录/COM|
|R02 Unicode 往返|中文祖先、发布目录、菜单/桌面文件名、空格/中点；中文及超出936覆盖范围的有效Unicode字符串分别覆盖 helper 字段。完整 target/arguments/workdir/description/icon index 逐字段相等；目标为隔离合成文件，绝不执行|
|R03 读取独立性|销毁创建RCW后用新RCW回读；至少一种独立原生/独立声明读取视角或在受控VM中由Explorer检查。避免同一有错marshaller写读形成自证闭环；不用WSH作为非CJK场景唯一裁判|
|R04 重复/升级/修复|重复创建幂等；合法 previous 升级；旧卸载保留新版；同账户/应用/数据根不匹配拒绝；previous=null受保护描述恢复；伪造/越界/超配额描述拒绝；missing link修复|
|R05 既有保护|异目标、同目标异参数、其他应用、用户重定向、同名普通文件/目录、坏链接、目标缺失：不接管，拒绝或按原合同精确清理；被保护文件操作前后hash一致|
|R06 卸载遗留|当前四链接、旧长名桌面、Importer PC下三个历史菜单均按精确归属删除；无关文件保留；不存在项幂等；不能删除新版本或他人版本|
|R07 失败窗口|激活/Load/Set/Save/保存后读回/发布/Delete分别失败；拒绝访问、共享冲突；保存失败不报成功，不损坏他人链接；中途完成条目可重入核查；原部署提交后失败状态真实|
|R08 路径与竞争|祖先/leaf reparse拒绝，同名目录拒绝；检查后出现/被替换/被改参数时保守保护；无盘外/未知路径删除；不能用普通单线程测试宣称覆盖任意恶意竞态|
|R09 COM资源/线程|成功与构造/保存失败循环（建议100轮有界），Dispose重复及释放后使用；无持续增长的可观测句柄/锁、随后可重命名/删除测试文件；记录实际apartment。句柄短测不能证明24h内存稳定|
|R10 长度与空值|正常边界、超出支持长度、空target/S_FALSE/读取截断均有确定拒绝和原件保留，不能误归属；不宣称因32768缓冲获得长路径支持|

R02 的生产 Plan 参数仍是既有三个固定值，helper 的 Unicode 参数夹具不能成为新的生产开关。R07的COM激活故障若无法安全影响真实组件，使用窄范围故障注入/独立测试替身证明异常路径，并另跑真实COM成功与损坏Load；不得破坏系统COM注册。无权建立链接/ACL的环境应明确 NOT_RUN，不删安全断言。

### 5.3 执行入口及环境约束

在独立、干净且已包含980a1383或等价实现的D盘工作树内执行；不要在其他任务仍使用的工作树中覆盖bin/obj或启动探针。本次工作包所在 main 工作树**尚不含该修复脚本**，应由执行者选择包含修复的源码树。

```powershell
. ./scripts/project-environment.ps1
# 将目录替换为尚不存在的本次专用D盘证据目录。
./scripts/verify-shortcut-codepages.ps1 -Output D:/DevCache/importer-pc/usc01-run-20261026-01
```

继承现脚本的 RestoreLockedMode 与 apphost 直接启动方式；脚本不调用安装入口、不登记HKLM、任务或服务。上述为真实已存在命令；新增故障矩阵 selector 名称由实施后源码决定，不能在工作包中虚构成已可运行命令。为故障注入和生产回执编写必要的聚焦用例，再跑受影响部署/安装守卫、Platform/Assembly构建和现有P0/冻结作业。不要为文案修改扩跑无关大规模数据测试。

每次证据记录：SourceCommit、实际CI测试合并commit、干净树状态、产品及测试DLL SHA256、OS/build/架构、.NET SDK、manifest原件/hash、期望和实际GetACP、apartment、合成根、命令/退出码、逐项PASS/FAIL/NOT_RUN、冲突文件前后hash、残余文件清单。失败日志用新目录保留；原ci-ba3、40222910122及修复后CI各守身份。能恢复后重跑通过，也不能覆盖第一次FAIL。

## 6. 与 PR110 及并行 PR 的集成顺序

1. 再次 fetch main、PR110和开放PR；确认本包事实是否仍成立。PR110已有 `9cbdd6b3` 归档和 `980a1383` 工程修复，优先在获授权的后续分支继承；不得从旧963165d7再次移植WSH替换或把同一修复重复cherry-pick。若main已合入则直接以新main做差量。
2. PR110的兼容性问题与ST01/02/06/14根因独立，原ST整改和原FAIL均保留。当前ST06按原用户裁决仅第一阶段及重连方案，无自动重连执行路径；不能借本包补造已批准重连合同。PR109已在main，去重继承延后配置实现。
3. 当前PR110代码页故障已在其头获得修复后CI，不再把旧红灯描述为它的最新CI；但PR110仍未合并。若只交付已有修复，后续重点是评审和集成；若USC差量产生代码，追加可审阅提交并在精确新头重验，不沿用980a1383的PASS。
4. PR112已明确依赖PR110，原分叉基点为963165d7，必须确认实际头是否已继承980a1383。父PR合并后按真实main更新子分支/PR基线，保持提交历史，不强推；子PR旧中文快捷方式FAIL不能仅改报告，需要在包含修复的组合树重跑。
5. PR108在部署/维护/回执、PR111在共享基础设施有交叉；本包不代表它们已集成。整合者应核对具体文件差异（尤其ParcelStartupServices、WindowsDeploymentSetup/Maintenance、工作流、版本和清单），保持各项修复与测试语义。必要时单独建立集成分支/main PR；不擅自合并其他PR或发消息给其他任务。
6. 组合树至少通过原 Existing Windows shortcut COM boundary、新三ACP矩阵、受影响安装/升级/修复/卸载及安全守卫、P0与aqa01、Windows冻结构建和启动冒烟。冻结安装器、BUILD-INFO、库存/版本/manifest必须来自实际组合源；DB9/SQL1～9和原请求字节不因快捷方式改变。
7. 修正当前索引/运行说明中“仍待授权修复/最新CI失败/等待固定源复验”等过期**当前状态**，并同步canonical manifest/hash；原始证据、历史段落、D/RV编号与原SourceCommit不倒写。本轮文件是本地交接候选，尚未接入main的INDEX/manifest，不冒称已归档发布。

## 7. 完成条件与仍未完成的现场验证

代码级完成须同时具备：已有修复被继承、USC差量的实际缺口被修复或充分补证、三ACP真实COM和故障/归属/路径测试通过、精确集成源CI通过、发布库存与指南一致。若只是重新确认980a1383，应直接报告“已有修复，无重复实现”，不能人为制造代码改动凑交付。

以下保持 **NOT_RUN**，只有实际另行执行并留证才可变更：

- 目标 Windows11 工控机（含实际build、4核/8GB、实际账户/策略）上的正式安装、升级、启动修复、卸载及失败后继续清理；Explorer显示中文名、图标、工作目录，四入口实际启动及普通权限边界。
- 真实UAC批准/取消、CommonPrograms/Public Desktop ACL、提权helper的实际线程模型；旧介质安装→新介质升级→旧版本卸载保护；既有用户改指向和损坏条目处置。
- 系统重启、任务计划/SCM/WER、磁盘物理故障或断电后的完整恢复；安装器构建成功不等于这些已完成。
- 真实SDK/相机/RCS、实体/合同授权与现场24h/72h长期运行。G01真实RCS合同、G02管理密码恢复政策和自动重连合同仍按原工作包 BLOCKED_EXTERNAL；它们不阻止本包Unicode通用修复，却阻止相应生产能力验收。

本包不新增现场批准人、不更改RV开放状态、不授予投产资格，也不以英文系统CI通过代替目标工控机验收。

## 8. 可直接交给 Codex 的执行提示

> 执行 UNICODE-SHORTCUT-COMPATIBILITY-01。先加载 scripts/project-environment.ps1，核对工作区及开放PR，读取最新main的AGENTS、INDEX、framework、framework-decisions、P0工作包、本包及相关D/RV/公开范围。所有工作树、缓存和产物留在D盘，保护原工作区与未跟踪附件。
>
> 先重新核对main和PR110：本包快照main=a8cd4314；PR110头980a1383已实现IShellLinkW/IPersistFile Unicode创建、读取和卸载，新CI40261696276三job及三ACP已绿，实际测试合并源95b1f29。若已合入或有等价实现，直接继承，禁止重复开发。PR109已经合并，相机延后问题不是本包阻断。原40222910122及ci-ba3中的FAIL保留原身份。
>
> 按USC-01～10逐项核对现有代码和证据，先用必要失败/保护夹具证明尚存缺口，再做最小工业级修复。重点覆盖COM根RCW唯一所有者/幂等释放、完整Unicode与raw target读取、空值/长度/HRESULT、生产保存后回读、提交后投影失败修复、固定历史入口清理、卸载失败重入、已有/用户修改快捷方式和祖先路径保护。没有证实的问题先补证，不把建议写成已复现缺陷。
>
> 不改系统区域设置，不使用英文名/短路径规避，不跳断言，不放宽受保护部署、原登记/备份/账户/数据根和reparse守卫。损坏或无法证明归属的链接保留原字节；不要Resolve、猜测乱码、通配清理或吞COM错误。若加入暂存/替换，证明不覆盖竞争中的他人文件，不把Exists/Hash复查当原子CAS。
>
> 在新D盘证据目录运行已有verify-shortcut-codepages.ps1，三个独立apphost必须实际GetACP=1252/936/65001再做真实COM断言，补本包R01～R10未覆盖项。保留所有失败日志与hash，记录精确源码/测试合并源、DLL和manifest身份。完成相关构建、部署守卫、P0和冻结验证；不要操作本机正式安装、真实设备或系统注册来替代隔离验证。
>
> 明确PR110是已有修复来源，PR112需要继承它，PR108/111组合树另验；在开发分支交付实际差量和main PR，保持历史，不强推或自行合并。同步本包范围的当前说明、版本/库存和canonical manifest，历史证据不倒写。最终报告已有成果、实际修改、命令、commit/PR/CI、未完成和受阻；真实Windows安装/卸载、目标IPC、设备/RCS与24h/72h未做就明确NOT_RUN。

## 9. 官方技术依据

以下只支持接口和验证机制判断，不代表项目现场验收；故障内因推断依据第1/2节的项目日志，不伪称微软文档证明WSH内部具体实现。

- [IShellLinkW](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishelllinkw)：显式宽字符接口及方法合同。
- [GetPath](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinkw-getpath)：raw path、空结果/S_FALSE和文档长度上限。
- [IPersistFile.Load](https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-ipersistfile-load)、[Save](https://learn.microsoft.com/en-us/windows/win32/api/objidl/nf-objidl-ipersistfile-save)：Unicode持久化文件名与错误/保存语义。
- [ComTypes.IPersistFile.Save](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.comtypes.ipersistfile.save?view=net-10.0)：托管void方法签名。
- [IShellLinkW.Resolve](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishelllinkw-resolve)：搜索、跟踪与可能更新目标的副作用。
- [Marshal.FinalReleaseComObject](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshal.finalreleasecomobject?view=net-10.0)：释放RCW及释放后不可再使用。
- [COM apartments](https://learn.microsoft.com/en-us/windows/win32/com/processes--threads--and-apartments)：线程/apartment和消息处理约束。
- [Application manifests / activeCodePage](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests)、[Microsoft对locale代码页与build边界的说明](https://devblogs.microsoft.com/oldnewthing/20220531-00/?p=106697)：进程ACP探针能力及版本边界。
