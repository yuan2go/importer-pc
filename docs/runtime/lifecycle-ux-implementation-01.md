> 后续审查P1/P2待整改：长期维护入场委托与持续授权、完整恢复集核对。已通过的批量15项/CI保留原范围，不能作为这两项通过证据。

> 2026-10-26批量部署整改：工程源eca5a1eb的冻结core/15项、实际只读CLI及CI40218628397双绿；当前结论和报告头最新检查见[续报](validation/lifecycle-batch-preflight-01.md)。旧FAIL/PASS各守原范围，原完成结论不直接沿用；G01/G02、真实产品安装和现场门槛保留。

本次批量整改：[续报](validation/lifecycle-batch-preflight-01.md)。新建 `Review` 与原计划 `RevalidateDeployment` 分开；后者保留原身份并重读全部输入/登记，完整比较后返回原冻结请求。`DeploymentBatch` 在执行前拒绝任何已存在原操作记录；原安装租约内仍再次防重复。无ID旧计划仅只读兼容/原提交核对，未提交执行要求显式重新审阅。成功重入复用原InstalledReceipt目标守卫，并核对发布根实际SourceCommit；同指纹下登记漂移不能假确认。规范GUID的独立校验不改变扩展ID或授权。既有管理模式、日志、历史、业务守卫及UI保留。

# 生命周期优化实施记录

2026-10-25，开工基线 `3a5b1d13ec5969213db35ed43d72a5aecd437777`，已 fetch origin/main，开放 PR 为 0。开发分支 `codex/lifecycle-ux-optimization-01`。工作区原有六份入口修订、七包规划及三审查材料保留；四份 HTML/spec/ZIP 附件不提交。旧 worktree 不修改。

## 编码前处置与责任

本批按 LIA-04A → 01/03 → 02/06L → 06U/07 → 05/04B 顺序实施，只有一条代码线，原生 Windows 验证串行。共享契约、入口、测试 Program、版本、INDEX/STATUS 和交付清单由同一集成者维护。

|核查|处置|
|---|---|
|DG01|源码 main 3a5b1d13、当地 SM1.12.3 EXE、公开 SM1.10.2、未知现场版本分别登记于当前操作入口；新候选另记身份。|
|DG02|普通向导只选程序/数据/快捷方式；旧账户/模式/信任选择器属于高级机制。修正 getting-started 与 windows-application-delivery 的失效普通步骤，原字节另存。|
|DG03|沿管理模式和独立操作员，Windows 授权仅安装维护；旧管理员账号文档留历史。|
|DG04|源码已无旧帮助页面及摘要契约；随包旧 UI/历史指南退出当前操作映射，保留原文及 Git 身份。日志、诊断、恢复继续保留。|
|DG05|安装 Unknown 核对、卸载续办、配置应用仍是本批工程缺口；在实现验证前不描述为已可用。|
|DG06|V3 参数布局、V2 作业/首配、PR107 字段/默认、TYPOGRAPHY-01 按增量设计继承。原件和哈希不改。|
|DG07|最终更新 release_documents 的实际产品映射、候选库存和冻结源；旧 manifest/SourceCommit 不倒写。|

## 风险与验证选择

01 验证原操作核对、防重复、目录变更、固定清单及未知回执；真实 UAC/系统卸载/重启为 NOT_RUN。
03 使用完整历史查询契约及至少三页合成数据验证固定上界、前后页、条件变化、详情及导出。
02 验证服务鉴权、撤权、停止失败、草稿和 Pending 应用；沿原配置账及资源所有权。
06 验证真实日志固定文件选择、上下文、两种导出、取消/失权/已有文件，原生日期/焦点/标准及紧凑布局。
07 验证正式备份状态投影与受控到期维护的小规模真实存储闭环；不改删除算法、索引、预算，不重复大库和长期测试。
05 验证交接只读、原实体/请求固定及权限；G01 无权威查询合同、G02 无批准密码恢复政策继续 BLOCKED_EXTERNAL。

以下为本轮独立工程结果；原规划原件固定于4c42f567及规划归档。目标工控机、设备/SDK/RCS、24h/72h及现场验收独立。

## 实施结果

|包|真实实现路径及责任|
|---|---|
|01|`WindowsDeploymentSetup`持久固定OperationId及请求/计划指纹，`DeploymentOperationInspection`取得原部署互斥锁后只读投影，登记提交与系统入口修复分开。`StartupSetupWindow`Unknown不释放介质、不默认重复提交。`WindowsDeploymentMaintenance`核对同源原清单，完整保留原receipt；`ProductMaintenanceWindow`按残件/重启/退役待清理显示，目录变化立即废弃原备份计划。|
|02|共享`ConfigurationActivation.cs`、双宿主和`ParcelApp.xaml.cs`复用排空/草稿/真实停止、释放后唯一重开与原激活。退出显示Pending、不可见和Unknown责任。ManagementModePresentation到期/服务不可用失权，原编辑保留；双宿主返回同页。管理密码窗口保留G02说明；操作员/免登录独立。完整参数目录与校验仍由Application要求权限。|
|03|扩展`IParcelHistorySessionApplication`取消令牌，沿原索引固定上界/游标，无新查询账。`HistoryEditing`/`ParcelTargets`保持条件草稿与已应用范围、前后页和选定原作业；实际业务名称映射稳定ID/历史停用位，准确CSV范围。|
|04|唯一当前操作指南随三产品打包；退出旧UI/账号/支持页面的有效随包映射。历史源件及原FILES保留，原发布/现场版本分列；版本/组合/锁文件与候选库存核对。|
|05|`RequestHandover.cs`只投影原实体/作业/请求、已知/未知/尝试及原合同/实现身份；模式外去除敏感散列，受限诊断脱敏。复制/选中/翻页不外呼。冻结RCS v1无权威Query时明确G01。|
|06|日志使用既有DateTimeInput/时区/快捷范围、固定已应用范围和二类导出复核/路径定位，沿原Reader/原子导出；新增选择生命周期释放，查询失败也不留句柄。首配叶页/类别进度、登录焦点/Esc、差异定位和默认安全动作；友好中文快捷方式保留原归属。|
|07|`ParcelScheduledBackup`正式状态契约含最近成功，后续失败/到限不抹去。`HistoryRetentionWindow`+`ParcelStartupRetention`在原owner与模式授权下沿现有内核预检/完整备份/到期删除/回执；固定目标/CAS、取消、备份后中断、丢终态只读核对、原Unknown/永久责任保护；不改内核删除/SQL/索引/预算。|

独立维护回执位于现有`startup-maintenance/<32位原身份>/retention-result.json`，沿恢复集证据目录约定。它只投影本次意图/结果，原数据库`parcel_retention_runs`及完整恢复集仍是执行事实，不生成第二份业务责任账。核对时先关闭自己的SQLite会话再核对DB/WAL指纹，避免持有自身sidecar读会话造成伪失败。

工程版本：Sportmaster1.13.0；DirectPlc、WcsToPlc1.7.0；Application.Contracts1.19.0；DB9无格式变更。未改变真实接口/生产缺能力拒绝守卫。项目Impeccable按Operate/WPF适配查阅，ui-ux-pro-max独立`--stack wpf`查询；实际查看V2/V3原型、交接及全部截图后沿现有资源/官方字体实施。网页检测不作为原生验收。

## 实际验证和运行

逐项命令、失败保留、源码冻结与产物身份见[验证报告](validation/lifecycle-ux-optimization-01.md)，文件字节见[evidence](evidence/lifecycle-ux-optimization-01/README.md)。必要验证通过后不再重复全量、无关大库或长期测试。未控制真实设备、未安装/卸载本机软件、未上传四份受限附件；所有工作/运行缓存沿项目脚本D盘。

开发运行：PowerShell先`. ./scripts/project-environment.ps1`；`dotnet run --project src/Ipc.Desktop -p:Application=Sportmaster -- --config D:/projects/importer-pc/config/sportmaster --data-root D:/DevCache/importer-pc/lifecycle-manual`。这只使用参考Simulation配置，仍沿现有Windows机器策略/身份预检；缺少合法部署资格时使用诊断/启动修复，不绕过真实守卫。普通已安装入口、独立`Maintenance.cmd`及系统产品维护按当前指南使用。同源候选是本地审查产物，不代表授权安装或正式发行。

G01/G02继续BLOCKED_EXTERNAL；真实系统登记往返/UAC/重启、目标IPC、SDK/RCS、24h/72h及现场均未验。旧D/RV和历史FAIL/PASS不倒写。原删除内核及其大库资格不因小规模本批通过而升级。

补充：备份正式状态使用显式单向WPF绑定，防止只读计算投影被TextBox默认双向模式写回。既有恢复到参数的原生旅程发现并验证此修正，见验证报告；不以早先未经过该入口的PASS覆盖该缺陷。当前候选以随包ASSEMBLY/BUILD-INFO/SHA256SUMS核对，不用版本号或后续文档头替代身份。

最小窗口增量：固定查询范围和紧凑详情的真实高度参与历史预算；经典壳收紧紧凑外边距，保持字体、44DIP准入动作和200DIP作业区。原生全旅程/真实请求链及新旧夹具边界见验证报告。
