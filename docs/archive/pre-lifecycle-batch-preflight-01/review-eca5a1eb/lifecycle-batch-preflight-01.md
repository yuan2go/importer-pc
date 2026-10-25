# 批量部署生命周期整改 · 最终工程验证续报

本次剩余批量部署整改已在工程源 `eca5a1eb94469fcd23bdf6b6374bee8864fe66cc` 完成源码、冻结介质和CI验证。CI [40218628397](https://github.com/yuan2go/importer-pc/actions/runs/40218628397) 的Linux、Windows、安装器打包及交付/证据归档全部SUCCESS。真实产品安装/卸载/重启、设备和现场仍NOT_RUN；G01/G02继续BLOCKED_EXTERNAL。不能将本结论扩张为现场资格或把上一轮“全部完成”直接沿用。

总包：[LIA-01～07](../../work-packages/WP-IPC-LIFECYCLE-UX-OPTIMIZATION-01.md)；[U/G总账](../../reviews/lifecycle-ux-reconciliation-01.md)。PR [#108](https://github.com/yuan2go/importer-pc/pull/108) 已恢复可审查，不合并、不发布。当前main为 `a8cd431456f73e83040f75a0c5e3cc1423440d1b`；原开工main为3a5b1d13。主线自行合入PR109后，183f9fac将其整合进本开发分支，保留双方源码、CI、目录及证据；不是把108合入main。

## 实际修复与闭环

|范围|当前行为|
|---|---|
|同计划身份|新建Review才生成OperationId。RevalidateDeployment保留原ID，重新读取介质、账户/目录、配置/信任/组件哈希和原登记CAS；继续完整JSON比较，验证后返回原冻结请求，保留字典顺序及原PlanSha256。输入/原登记变化拒绝，不改ID或散列绕过。|
|固定ID校验|Wire.Id生成的规范小写非空32位GUID允许数字开头；沿用此前安全命名ID。扩展ID语法、路径和权限不放宽，不重抽随机ID。|
|原操作与未知|任何原记录（Prepared、ReadyToCommit、Unknown、Failed、Completed或残缺）都阻止同操作再次提交；在备份/安装前检查，最终安装仍在原部署租约内防重复。Unknown沿原Deployment调用现有InspectInstallation只读核对；明确Failed/NotCommitted后才显式审阅新动作。|
|原回执/中断|每次预检或提交使用新回执，CreateNew拒绝覆盖成功/失败/Reviewing原文件。预检中断恢复用新回执重新核验；提交中断保留原操作/日志，不能通过新ID掩盖Unknown。|
|成功重入|AlreadyInstalled复用正常安装读回的同一守卫：应用、状态、SourceCommit、账户、数据根、快捷方式和完整请求指纹均须匹配；另核对原发布根介质来源。同指纹下登记目标漂移仍拒绝，不能确认成旧计划成功。|
|旧计划边界|具备现有完整审阅字段、输入未变的Schema1无ID旧计划保留原序列化/哈希，可只读预检或核对完整匹配的原提交；未提交执行为LegacyPlanNeedsReview/退出2，不自动补身份。字段缺失或旧默认值不满足当前完整比较时继续拒绝，须显式新审阅。|
|交付留存|Windows输出统一在D:/scd/共同父目录，避免artifact动作把D盘根误解为仓库工作目录。保留所有工程门和两个上传步骤，未以跳过归档或断言换PASS。|

代码复用ParcelStartupServices、DeploymentBatch、原Windows部署记录/租约及唯一配置账。管理模式与操作员独立显示、参数服务层权限、真实日志查询/导出、历史及备份机制、帮助与支持删除保持既有成果。没有RCS查询、重发或密码恢复后门。

## 必要验证与精确范围

|检查|结果|边界|
|---|---|---|
|最终冻结core及批量守卫|PASS，15项：同计划三次预检；序列化/字典顺序；16种GUID首字符与0/9实际预检；配置字节/Mode/登记CAS变化；篡改；旧回执及Reviewing保留；旧计划；原记录五态/残缺拒绝重提；恢复原输入；同指纹七种登记投影漂移拒绝|实际发布DLL及同提交夹具；HKLM部署登记未做真实改写，登记漂移为同一实际守卫的合成投影。|
|实际只读EXE入口|Ipc.Assembly.exe --preflight退出0；原数据根未创建，741文件库存未变|真实CLI预检，不是业务启动/正常部署成功。|
|三产品Release构建|Sportmaster、DirectPlc、WcsToPlc PASS；后两者显式组合、串行、RestoreLockedMode，0警告/错误|本批当前源码；无新外部依赖版本。|
|既有受影响守卫|--lifecycle-ux PASS|安装结果投影、日志资源、完整备份/到期内核及Unknown保留；合成身份/端口、私有SQLite。|
|指南/冻结构建/目录边界|指南3项、独立工作区DLL一致性1项、精确生成目录边界1项PASS|没有本机历史全套、大库或24h/72h扩测。|
|最终工程源CI|40218628397 SUCCESS，Linux与Windows均SUCCESS，交付和证据真实留存|实际测试合并源4224e7458a43c3fc8cd7502ad9de9ae45422217c，与PR头eca5a1eb及本机冻结源各守原身份。|

最终CI [身份/产物摘要](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/ci-40218628397/RUN-IDENTITY.json)、[core](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/ci-40218628397/CORE-RESULT.json)、[15项](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/ci-40218628397/BATCH-RESULT.json)、[工程门](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/ci-40218628397/engineering-gate.json)、[生产拒绝](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/ci-40218628397/production-gate.json)、[安装器回执](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/ci-40218628397/INSTALLER.json)保留原字节。已核对下载artifact摘要、SourceCommit=FixtureCommit、15项及两个运行DLL哈希匹配该CI原库存。打包安装器ActualWindowsInstallation=NOT_RUN，不把打包当安装成功。

本报告提交仅归档文档和证据。产品、配置、脚本、测试、工作流、工具及随包指南输入按最终DELIVERY的Git对象逐项保持不变；该提交本身的最新检查以 [PR实际checks](https://github.com/yuan2go/importer-pc/pull/108/checks) 和PR正文的实际运行号为准，不把工程源的PASS倒写成尚未执行的新头PASS。

## 当前本机介质与运行

- 当前冻结目录：`D:/deliveries/lifecycle-batch-20261026-eca5a1eb/sportmaster-win-x64`。ZIP 134218106 bytes，SHA256 `f58e5015102bf7338d68f482ca512526b30c7d9172caace064af0ad7f700e7aa`。
- 库存741文件；SHA256SUMS哈希 `64f43cb03e71e329b6ac85987fa5f494e26800169b4cdb1d297148290af4e9e1`。源码/夹具均eca5a1eb；CLI后库存及实际运行副本DLL逐项匹配。
- [交付与输入Git对象](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/frozen-eca5a1eb/DELIVERY.json)、[冻结core](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/frozen-eca5a1eb/RESULT.json)、[批量15项](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/frozen-eca5a1eb/BATCH-RESULT.json)、[真实CLI](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/frozen-eca5a1eb/CLI-RESULT.json)为当前本机证据。720af857和183f9fac是被替代的中间候选；旧0a3d4152及其原PASS/FAIL不改变。
- SM1.13.0 / DirectPlc及WcsToPlc1.7.0 / Contracts1.19.0 / DB9。仍是未发布候选；本机ZIP未上传，CI安装介质仅沿既有PR artifact留存，未创建Release。

PowerShell复核（新证据目录/回执；仅只读预检）：

```powershell
Set-Location D:/projects/importer-pc
. ./scripts/project-environment.ps1
./scripts/verify-commercial-media.ps1 -Media D:/deliveries/lifecycle-batch-20261026-eca5a1eb/sportmaster-win-x64 -Output D:/DevCache/importer-pc/lifecycle-batch-new-evidence
& D:/deliveries/lifecycle-batch-20261026-eca5a1eb/sportmaster-win-x64/app/Ipc.Assembly.exe --deployment-plan D:/delivery/site-plan.json --receipt D:/delivery/new-preflight.json --preflight
```

高级集成用现有DeploymentBatch.Freeze固化Review结果；普通向导没有导出计划按钮。正常安装/运行步骤和退出状态以 [当前生命周期指南](../lifecycle-ux-current-guidance-01.md) 与 [高级部署说明](../commercial-productization-01-operations.md) 为准，只有可信同源完整介质及授权环境才能执行。源码夹具明确用 `-p:BusinessModule=Sportmaster` 统一还原/构建组合，串行构建后锁定验证；不能让隐式Supply还原图与Sportmaster构建混用。

## 失败、归档与来源沿革

1. 开工头2a13b11c，原 [CI40210140349](https://github.com/yuan2go/importer-pc/actions/runs/40210140349) 的实际merge源8408a38a，core报Frozen media batch preflight failed；本机0a3d4152实际DLL复现退出2/InvalidDataException，库存不变。原FAIL保留。
2. 编码前ea4333f4归档相关现行说明并撤回完成结论；[十份原件/哈希](../../archive/pre-lifecycle-batch-preflight-01/FILES.json)及原manifest/checksum保留。720af857修复身份/防重复，183f9fac整合最新main。INDEX同时保留LIA和PR109入口，canonical目录保留双方记录与来源；既有缺录的main文件只作继承目录补齐，不计作LIA新实现/验收。
3. [CI40216393477原FAIL](../evidence/lifecycle-ux-optimization-01/batch-preflight-20261026/ci-40216393477/RESULT.json)：工程、原生及打包SUCCESS，但D盘顶层兄弟目录共同根导致两个上传FAIL。原JOB日志保留，失败轮没有Windows artifact，不补造。5c11a2a4改D:/scd/共同父目录后，[CI40217369663](https://github.com/yuan2go/importer-pc/actions/runs/40217369663)双绿且真实留存；该轮仍是14项旧源，不扩张到新增重入守卫。
4. eca5a1eb复用InstalledReceipt守卫补成功重入目标漂移拒绝，加入第15项；本机冻结与CI40218628397均通过。既有项目引用元数据同步到当前Contracts1.19，源码夹具锁定显式组合，无外部包升级。
5. 源码阶段的GUID旧语法拒绝、测试GUID字面量33位误写、共享obj写锁及隐式还原图锁不符均保留原FAIL；修正输入/显式组合后通过，未删断言、吞异常或关闭锁定校验。各源码阶段与冻结介质证据分开。

原阶段证据及本次续报的完整字节清单见 [FILES.json](../evidence/lifecycle-ux-optimization-01/FILES.json)。原规划、D/RV编号、字体/视觉基线、历史验收及旧介质身份不倒写。

## 剩余边界

G01真实RCS权威核对/人工处置合同及授权、G02批准的管理密码恢复政策继续BLOCKED_EXTERNAL。真实产品安装/卸载/重启、UAC交互、完整部署登记往返、目标IPC触控/DPI、真实SDK/RCS/设备和现场均未由本批本机验证放行；24h/72h与历史大库资格不扩张。CI的真实Windows权限/SCM/ACL和合成端口旅程各守其范围，不是正常部署成功。没有合并108、发布、安装本机软件或上传受限附件；仓库外账号/现场数据与四份未跟踪附件保持不入Git。
