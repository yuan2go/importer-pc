# 批量部署预检回归 · 2026-10-26 续报

原基线修复源720af857已通过本次定向检查；main随后合入PR109为a8cd4314，已整合到开发分支，需按新基线重新冻结并核对最新CI；不能沿用上一轮“全部完成”结论。总包为 [LIA-01～07](../../work-packages/WP-IPC-LIFECYCLE-UX-OPTIMIZATION-01.md)，U03/U21 本次重新打开；其余已正确成果不重写。历史 [0a3d4152 报告](lifecycle-ux-optimization-01.md) 保持原范围，归档前现行说明的原字节和哈希见 [FILES](../../archive/pre-lifecycle-batch-preflight-01/FILES.json)。

## 原失败与原因

- PR #108 头 `2a13b11c067841a0c88b0669fc43d03dc77adcc3`；main `3a5b1d13ec5969213db35ed43d72a5aecd437777`，开工核查时无其他开放 PR、无已跟踪并行修改。收口前发现新增 PR #109（头75ae3417），生产源码无文件交集。其后主线已将109合入为a8cd4314；本次将最新main整合进开发分支，保留其源码/证据及共享入口。INDEX同时保留两条现行入口，canonical目录保留双方元数据并重算实际Git记录；不把109证据算作本包新验证。
- [Actions 40210140349](https://github.com/yuan2go/importer-pc/actions/runs/40210140349)：Linux 成功，Windows 冻结工程门失败。原介质/夹具均为实际 merge 源 `8408a38a2440097acee19d5101a528e738d413b8`，core `Frozen media batch preflight failed`，不是 head SHA 的另一名称。原始 core RUN/RESULT、acceptance 和 run/artifact 身份保存在 [original-ci-40210140349](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/original-ci-40210140349/RUN-IDENTITY.json)。
- 本机用未改变的 `0a3d4152` 冻结 Windows DLL 及同提交夹具复现相同失败；原回执 InvalidDataException/退出2，库存未改变。原 [FAIL](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/baseline-0a3d4152/RESULT.json) 保留，不改标 PASS。
- 批量入口以 `Review` 重新观察输入时生成新 OperationId，完整请求比较正确拒绝了不同身份。修复分开新建审阅与 `RevalidateDeployment`：重新观察全部输入、保留原 ID、继续完整 JSON 比较，验证后返回原冻结请求，避免字典枚举顺序改变原线性化哈希。
- 还发现安装路径使用扩展 ID 语法校验 Wire.Id 的 GUID；数字开头的合法 GUID 被拒。本次仅增加独立的操作 ID 校验，接受规范小写32位 GUID及此前安全命名 ID。扩展 ID、受保护路径、授权和原记录完整性不放宽；真实安装未执行。

## 当前定向结果

|检查|结果与证据|边界|
|---|---|---|
|源码批量守卫|14项 PASS；同计划三次预检、字典顺序/原哈希、16种GUID首字符及0/9实际预检、配置字节/Mode/登记CAS不符、计划篡改、原回执及Reviewing保留、旧计划、五种原记录及残缺记录阻止重提、恢复原输入后重预检|工作树源码测试，种子介质为0a3d4152，不能称为旧发布DLL通过；记录中的SourceCommit字段仅是该种子介质身份|
|既有生命周期守卫|`--lifecycle-ux` PASS|原请求/安装核对、日志资源、完整备份/到期内核的受影响定向回归；合成身份/端口和私有SQLite|
|共享装配构建|Sportmaster、DirectPlc、WcsToPlc Release PASS|没有新增应用契约或DB版本；未发布/安装|
|指南与发布构建测试|指南3项 PASS；冻结构建1项 PASS（独立工作区DLL一致）|只验证本次随包映射/冻结构建风险，不扩测历史套件|
|修复后冻结介质、只读CLI|PASS；源码和夹具均720af857，core含14项批量检查；实际Ipc.Assembly.exe --preflight退出0；741文件库存及夹具DLL字节匹配|真实安装、设备及业务未启动，不作为正常部署成功|
|PR最新CI|待运行|不能用本机夹具代替CI或正常部署成功|

源码中间失败原件保留：首次新校验复用了旧ID语法而拒绝数字开头GUID；随后新增测试的GUID字面量长度误写33位导致断言失败，已修正测试输入为严格32位，未删断言。后续13项及新增顺序边界后的14项PASS分别保留；所有源码阶段证据与冻结证据分开。

## 操作与兼容边界

高级集成以现有 `DeploymentBatch.Freeze(Review(...))` 固化计划。相同计划重新预检须用新回执文件，保留原 OperationId/PlanSha256；不得手改ID/散列绕过失败。新审阅才生成新操作。操作方式及各退出状态见 [当前生命周期指南](../lifecycle-ux-current-guidance-01.md) 和 [高级部署说明](../commercial-productization-01-operations.md)。

任何原操作记录存在即禁止本计划再提交；不解析一个 Failed/Unknown/残缺状态来推断可重试。批量入口在执行备份/安装前检查原记录，最终安装仍在原部署租约内检查同一操作文件，保留原记录和CAS。Unknown 交原计划的 `ParcelStartupServices.InspectInstallation` 只读核对，明确 Failed/NotCommitted 后才允许显式审阅新动作。

Schema1 无 OperationId 的旧计划保留原序列化/哈希，可只读预检；唯一部署登记完整匹配的原提交仍可只读 AlreadyInstalled。未提交旧计划执行为 LegacyPlanNeedsReview/退出2，不补ID。已开始/中断计划和原回执均不能覆盖。预检中断留下 Reviewing，恢复用新回执重新核验；提交中断保留原ID/日志，禁止重提。

Windows CI 的开发命令加载项目环境，SDK/缓存、发布介质、验证输出改用D盘。仅授权的一次性 GitHub Windows VM 初始化新的受保护D盘目录和HKLM位置，使用现有祖先/ACL守卫，拒绝覆盖旧锚点；不以环境变量或链接替代信任锚。本机只编译该隔离测试准备入口，不执行它。原生Windows旅程串行。

## 未完成的真实边界

本机实际安装/卸载/重启、系统登记提交往返、真实SDK/设备/现场均 NOT_RUN；预检/原DLL夹具不构成正常部署成功。G01真实RCS核对合同、G02管理密码恢复政策继续 BLOCKED_EXTERNAL，不增加查询、重发或密码恢复后门。管理模式与操作员分离、参数服务权限、真实日志查询/导出及帮助与支持删除成果不变；历史D/RV及原PASS/FAIL不改写。本PR不合并、不发布。

## 本次实际冻结与交付清单

修复提交 `720af857421f6c1b0bb67a5159691cff93c0ed29`；前置归档/撤回完成结论提交 `ea4333f4`。本机仅冻结受影响的Sportmaster Windows介质，未重复生成portable或运行其他历史/大库/长期套件。SM1.13.0 / DirectPlc及WcsToPlc1.7.0 / Contracts1.19.0 / DB9保持未发布候选版本；新增两个既有装配的精确NuGet锁文件，不升级依赖。

- 本地介质：`D:/deliveries/lifecycle-batch-20261026-720af857/sportmaster-win-x64`。ZIP 134217082 bytes，SHA256 `a8b350ebaf8bfeeface332082a68afc4e6dcabd514be1b77f227819998b09a4b`。库存741文件，SHA256SUMS哈希 `9945f7531f677961eaf04d9772c9ad1d91d5932eefcc0fe0c517ba45f87d5ebd`。未上传该本地ZIP或创建Release。
- 冻结证据：[RESULT](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/frozen-720af857/RESULT.json)、[14项批量结果](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/frozen-720af857/BATCH-RESULT.json)、[真实CLI只读结果](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/frozen-720af857/CLI-RESULT.json)。两个实际DLL哈希同时匹配原库存和夹具运行副本；CLI后重新核对741文件未改变，数据根未创建。
- 原始失败、源码阶段、冻结证据分别保留；canonical Git manifest只收录已登记Git文件，原manifest及checksum追加按2a13b11c的Git原字节归档。仓库外附件、账号和计划输入未加入Git；受影响文档索引和证据清单同步。

复核运行方法（PowerShell，输出必须用新目录/新回执）：

```powershell
Set-Location D:/projects/importer-pc
. ./scripts/project-environment.ps1
./scripts/verify-commercial-media.ps1 -Media D:/deliveries/lifecycle-batch-20261026-720af857/sportmaster-win-x64 -Output D:/DevCache/importer-pc/lifecycle-batch-new-evidence
# 已有高级计划仅作只读预检；不是安装命令
& D:/deliveries/lifecycle-batch-20261026-720af857/sportmaster-win-x64/app/Ipc.Assembly.exe --deployment-plan D:/delivery/site-plan.json --receipt D:/delivery/new-preflight.json --preflight
```

当前#108保持草稿，待最新CI后更新实际结论。不合并、不发布、不安装本机软件；不存在正常部署成功或现场放行的声明。

## main前进后的整合

main于本次推进中合入PR109（a8cd4314），导致首轮文档头与main的INDEX/manifest冲突，GitHub未生成该冲突头的CI运行；这不是新的CI通过或失败。现已保留双方内容并解冲突，生产源码无重叠。720af857与原CLI/core PASS降为整合前中间候选，原文件/哈希不改；后续仅按新增基线风险再做受影响的冻结core/只读CLI与最新CI，不重跑其他本机历史/大库/长期测试。不将开发分支整合误记为合并108到main。
