# 批量部署预检回归 · 2026-10-26 续报

当前为修复后的冻结介质及最新 CI 验证阶段，不能沿用上一轮“全部完成”结论。总包为 [LIA-01～07](../../work-packages/WP-IPC-LIFECYCLE-UX-OPTIMIZATION-01.md)，U03/U21 本次重新打开；其余已正确成果不重写。历史 [0a3d4152 报告](lifecycle-ux-optimization-01.md) 保持原范围，归档前现行说明的原字节和哈希见 [FILES](../../archive/pre-lifecycle-batch-preflight-01/FILES.json)。

## 原失败与原因

- PR #108 头 `2a13b11c067841a0c88b0669fc43d03dc77adcc3`；main `3a5b1d13ec5969213db35ed43d72a5aecd437777`，无其他开放 PR、无已跟踪并行修改。
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
|修复后冻结介质、只读CLI|待执行|必须使用新源码、实际发布DLL和同提交夹具|
|PR最新CI|待运行|不能用本机夹具代替CI或正常部署成功|

源码中间失败原件保留：首次新校验复用了旧ID语法而拒绝数字开头GUID；随后新增测试的GUID字面量长度误写33位导致断言失败，已修正测试输入为严格32位，未删断言。后续13项及新增顺序边界后的14项PASS分别保留；所有源码阶段证据与冻结证据分开。

## 操作与兼容边界

高级集成以现有 `DeploymentBatch.Freeze(Review(...))` 固化计划。相同计划重新预检须用新回执文件，保留原 OperationId/PlanSha256；不得手改ID/散列绕过失败。新审阅才生成新操作。操作方式及各退出状态见 [当前生命周期指南](../lifecycle-ux-current-guidance-01.md) 和 [高级部署说明](../commercial-productization-01-operations.md)。

任何原操作记录存在即禁止本计划再提交；不解析一个 Failed/Unknown/残缺状态来推断可重试。批量入口在执行备份/安装前检查原记录，最终安装仍在原部署租约内检查同一操作文件，保留原记录和CAS。Unknown 交原计划的 `ParcelStartupServices.InspectInstallation` 只读核对，明确 Failed/NotCommitted 后才允许显式审阅新动作。

Schema1 无 OperationId 的旧计划保留原序列化/哈希，可只读预检；唯一部署登记完整匹配的原提交仍可只读 AlreadyInstalled。未提交旧计划执行为 LegacyPlanNeedsReview/退出2，不补ID。已开始/中断计划和原回执均不能覆盖。预检中断留下 Reviewing，恢复用新回执重新核验；提交中断保留原ID/日志，禁止重提。

Windows CI 的开发命令加载项目环境，SDK/缓存、发布介质、验证输出改用D盘。仅授权的一次性 GitHub Windows VM 初始化新的受保护D盘目录和HKLM位置，使用现有祖先/ACL守卫，拒绝覆盖旧锚点；不以环境变量或链接替代信任锚。本机只编译该隔离测试准备入口，不执行它。原生Windows旅程串行。

## 未完成的真实边界

本机实际安装/卸载/重启、系统登记提交往返、真实SDK/设备/现场均 NOT_RUN；预检/原DLL夹具不构成正常部署成功。G01真实RCS核对合同、G02管理密码恢复政策继续 BLOCKED_EXTERNAL，不增加查询、重发或密码恢复后门。管理模式与操作员分离、参数服务权限、真实日志查询/导出及帮助与支持删除成果不变；历史D/RV及原PASS/FAIL不改写。本PR不合并、不发布。
