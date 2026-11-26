# PR #222 卸载数据边界复核

复核基线：main `ddcb058fcd6911582609add48e754300bcdf8e12`，2026-11-05。
本次只运行 D 盘新建的合成目录、注入的安装登记/回执及 WPF 回调；未调用真实安装登记写入、提权卸载、服务删除或部署入口。

## 结论与修正

原 main 不能判定为数据删除边界完全通过。以下前三项已在原实现复现；第四项来自调用链审查，并用替换登记与原身份的定向用例验证修正。

|等级|缺口与复现|修正|
|---|---|---|
|P1|SM 与另一应用的安装登记指向同一 DataRoot，目录内放置另一应用的合成数据库。原计划枚举整个 DataRoot，执行后另一应用文件消失。|拒绝与其他主安装登记的数据、配置或程序目录互相包含的范围；初次执行与原清单重试都检查。删除期间占用其他已登记应用及支持应用的部署租约，防止新安装在检查后加入共用目录。|
|P1|计划核对完成后，在保存 Purging 的回调中改写已列出的文件。原实现按文件名删除新字节，最终回执为 Purged。|Windows 使用同一文件句柄核对类型、最终路径及 SHA-256 后设置删除标记，期间禁止写入、删除与重命名共享；目录也用句柄只删空目录。内容变化保留文件，不产生完成回执。程序文件卸载仍按其原清单规则执行。|
|P2|界面先选删除，提交返回结果未确认，改选保留，再查看已核实的“程序已卸载、数据保留”结果。原逻辑把缓存的旧删除选择合并回来，提供带删除意图的重试。|无原数据删除操作时使用当前单选值；窗口不缓存删除选择。已提交的部分删除仍只允许继续原清单。|
|P2|旧窗口点击时才获取最新登记哈希；程序卸载结束后又独立从当前登记生成数据计划。两处都可能把原意图重新绑定到后来出现的安装。|窗口打开时冻结登记及安装范围；登记变化要求重开。同一安装正常退役可更新哈希，范围不能变化。后台生成删除计划必须与本次程序卸载的完整退役登记及原操作文件一致，之后仍执行计划身份比较。|

同时补齐程序目录、部署回执目录的子目录排除，并将相同范围检查用于中断重试；完成前同时检查目录或替代根文件残留。没有新增业务请求查询、备份、确认文字或额外操作步骤。

## 入口与状态核对

- 普通卸载窗口和安装介质卸载窗口共用同一组单选，初始为保留。关闭重开重新默认保留；从删除切回保留，提交 false。
- 已完成程序移除的结果窗口构造为保留；SM 静默卸载只移除程序，没有自动删除数据选项；维护请求的 DeleteData 默认 false。
- 已知失败不提交数据删除；结果未确认时不能再次提交。查看结果是只读操作，不直接删除数据。
- 已有 Purging/PartiallyPurged 登记不是新的默认删除意图。保留选项不能启动继续删除；只有明确选择删除才可继续原操作，不能换根或换清单。已删除部分不能凭保留选项恢复。
- 初次执行检查整份清单与字节；执行时逐文件检查同一句柄。中断后只删除原清单中仍匹配的文件，已缺失文件视为原清单余项已完成，清单外文件保留并保持部分删除。
- 根、祖先目录、目录项的 reparse point 检查阻止链接、junction 和挂载点进入范围。已在 Windows 实测合成 junction；未实际挂载卷。句柄最终路径检查防止打开时经过改变的路径重定向到其他位置。
- 回执与登记分开落盘：仅回执完成而登记未完成仍显示部分删除；目录已消失也可按原清单补完登记。操作文件或根身份不一致，不能借另一安装继续。受保护登记、回执、共享驱动及范围外备份保留。

以上是登记所界定的本站范围保护，不是任意目录内容的业务归属推断；没有登记且被放入本站专用数据根的文件，仍属于用户明确选择删除的范围。保护依赖原有受保护登记、ACL 和部署租约，不能用于抵御有权篡改这些保护的 Windows 管理员。

## 未确认 RCS 请求的实际影响

取消请求状态检查是已确认的产品策略，不是遗漏的守卫。保留数据时，原数据库、原请求、报文、目标与图片保留；卸载不把请求写成完成。明确选择删除时，Unknown 请求的本地记录及对账依据随本站数据库删除；软件不会因此撤销 RCS 已执行的动作，也不会向 RCS 补发、伪造确认或创建卸载备份。因此可能失去本机事后核对原请求的依据，这属于主动永久删除的结果，不能误报成请求已经结清。

## 验证与复现

先加载 `scripts/project-environment.ps1`。新回归入口：

```powershell
dotnet build tests/Ipc.Application.Verification/Ipc.Application.Verification.csproj
$env:IPC_VERIFICATION_WITHOUT_VENDOR_RUNTIME = '1'
dotnet tests/Ipc.Application.Verification/bin/Debug/net10.0/Ipc.Application.Verification.dll . --uninstall-data-boundary
```

原实现的合成输出：

```text
FAIL another registered application's shared data survives station deletion
EVIDENCE commit-mutation receipt=Purged; saved=Purged; error=; fileExists=False
FAIL a reviewed filename cannot delete different bytes written during commit
FAIL a changed file cannot produce a completed data-deletion receipt
```

原 WPF 注入回归在 `inspection preserves a later retention choice when no data-deletion operation exists` 失败。修正后这条及新增登记变化、重开、切回保留用例通过。

本机验证：

- 编译：SM Desktop、Application.Verification、Desktop.UiVerification、Core.Tests。
- 本机合成回归：新边界 14 项；既有删除 35 项；生命周期 57 项；占用/损坏数据 48 项；恢复 22 项；同次放置及 RCS 心跳回归通过。
- Windows 原生 WPF：真实 HWND/Dispatcher/按钮事件，卸载与登记回调注入；包括原部分删除的只读核对及显式重试，不是实际卸载。
- Windows 原生文件：新句柄删除路径、内容改变拒绝、正常删除、空目录、部分重试及合成 junction 已运行。
- CI：以本分支实际运行状态为准，不继承 PR #222 的结果。
- Windows 受保护实机卸载/UAC/SCM、真实 SDK、客户 RCS、现场验收：未运行。

真实卸载、数据删除、部署或安装包操作须另行展示具体目标、影响、执行步骤并获得用户明确批准。本次未发布安装包、未上传 OSS、未清理本机安装或备份。
