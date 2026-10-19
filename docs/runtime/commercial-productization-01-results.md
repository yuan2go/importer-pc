# COMMERCIAL-PRODUCTIZATION-01 实施与交付结果

本次实施从 main `c445b0ec32a3db44f00a1968f3c501aa36eb786f` 开始，分支 `codex/commercial-productization-01`。开工工作树干净；开工及交付前开放 PR 核对无并行开放项。编码前提交 `662ef51`，保留七份原字节并裁决当前入口；[实施裁决](../implementation/commercial-productization-01-execution.md)记录 ADR COMM-A～G及收尾裁决。D/RV、客户原稿公开范围、旧失败及旧介质身份未改写。

本页记录实际软件路径与验证边界。冻结媒体的 SourceCommit 从其 `ASSEMBLY.json` / `application-release.json` 读取，ZIP SHA256 从独立交付回执读取；回执在构建/验证后形成，不把本页所在文档提交号冒作已运行介质身份。最终回执见[证据入口](evidence/commercial-productization-01/README.md)、[逐项结果](evidence/commercial-productization-01/checks.json)与[六介质身份](evidence/commercial-productization-01/media-identities.json)。本次已执行部分Windows原生只读/页面验证，未执行提权安装或真实现场验收。

## 核心范围与实现路径

|单元|当前实现|验证边界|
|---|---|---|
|COMM-00|工作区/main/PR核对、独立文档裁决提交、原件归档、当前操作指南、清单增量|文档原件/差额可复核；历史 D/RV 保留|
|COMM-01|`WindowsDeploymentSetup`、`TrustedProgramRoots`、`DeploymentMedia`；程序根、数据权限、机级账户、静态组件/闭包预检、固定helper、并排提交、系统安装项投影|默认/自选根贯穿helper、证据祖先、登记、维护；原生保护父目录/可写目录拒绝与非提权退出5通过；实际UAC/跨账户/系统项/需重启删除 NOT_RUN|
|COMM-02|`StartupWorkflows`、`StartupConfigurationDraft`、`ParcelInitialConfiguration`、`ConfigurationRevisions.StageInitialRepair`；新站种子、精确reader/角色、可选登录、加密草稿、分层检查、首配失败受控修复|合成复现失败/修复/失败候选/中断/CAS/原件保留；真实SDK/枚举/Production资格 BLOCKED|
|COMM-03|同一结构化/高级JSON设置、配置CAS与严格Pending；`ParcelEditorDrafts`复用独立草稿存储；无效输入及新增库存可继续；展示偏好独立于业务|不新增第二配置账；Windows DPAPI真实运行，维护账户跨用户后验|
|COMM-04|继承左右位、原观察/实体/作业/请求、epoch、共享实例；工作/记录/维护/帮助导航、辅助窗口和关闭保护|原生WPF局部任务验证；目标客户区/触控/DPI矩阵仍逐项NOT_RUN|
|COMM-05|`FaultGuidance`与原健康/守卫；CP类影响/下一动作、明确过期；受控重连仍走Application|无新设备控制协议；Unknown原事实/停止保所有权不放宽|
|COMM-06|`ParcelHistoryQuery`、`HistoryEditing`、HistoryPage；统一筛选、稳定序号上界、只读连接、流式CSV/取消/回执及事实指标|前导零/长码/公式、同码不同实体、固定边界、取消与不覆盖已验证|
|COMM-07|`ParcelProductMaintenance`私有DB/WAL/原件快照、完整备份、原责任计划；相同SourceCommit的程序Repair保留Unknown原账后并排修复，换源码仍按Upgrade拒绝；`WindowsDeploymentMaintenance`系统修复/库存卸载/重入；不回滚业务账|实际卸载/文件占用/重启与系统项后验；Unknown拒绝、原DB/WAL不改字节已验证|
|COMM-08|备份列表完整性、唯一配置账中计划策略、原完整恢复集、隔离恢复、`ParcelRestoreHandoff`及受保护登记切换；旧根围栏先于提交|完整集实际恢复；旧备份不得退役新事实、DB7接管拒绝与DB8旧读取者门槛；同机核心防双活已验证；受保护原机接管确认/换机资格后验|
|COMM-09|`ProductSupportWindow`、`ParcelProductSupport`、独立维护摘要与内置离线帮助；预览固定字节/容量/期限，排除秘密/完整payload/DB/原件|合成秘密标记不泄漏；原生支持页可达；未自动上传|
|COMM-10|统一产品标题、可重建自有图标、任务/焦点/自动化名；安全站点模板、`DeploymentBatch`固定逐站计划/返回码、统一冻结打包及随包指南|与人工安装同源，非隐式提权；没有克隆生产身份/请求；介质身份单独记录|

业务 1.4.0，Application.Contracts 1.10.0，DB迁移SQL1～7未改，追加DB8语义读取门槛；BackupSchedule 缺省不序列化，原冻结配置/请求散列不因新增字段变化。旧扩展受精确合同/宿主字节约束，不因版本声明自动兼容新DLL；重新构建须新不可变版本、真实签名/独立信任和组合验收，不改标旧扩展。没有提供真实SDK、厂商安装程序、Production默认资格或商业代码签名。

## 风险验证与原失败

定向夹具 `tests/Ipc.Application.Verification/CommercialProductization.cs` 使用真实应用账/SQLite/恢复集/参考端口，并显式注入内部合成身份依赖。内部测试依赖未暴露为 CLI、配置或 Production 绕过。实际 DPAPI 在当前 Windows 上运行；合成身份不等于跨账户UAC、机器政策、真实设备或现场资格。

开发验证保留了原失败：原启动部署基线遇到未提权维护工作区权限门槛；离线检查副本 SQLite WAL 共享冲突已实际修复（关闭只读连接后再核字节）；原生WPF只读 Run/TextBox 默认双向绑定导致启动异常已修复为单向；历史全客户区矩阵要求1440×900，当前桌面工作区1536×816 DIP不足，未删除/放宽原断言。后续新增可执行的1100×600原生页面定向检查，与该矩阵限制分别记录。

|验收|可执行软件证据|外部/原生剩余|
|---|---|---|
|CPV-01|编码前裁决提交、七原件、当前路由与manifest增量|不关闭历史资产问题|
|CPV-02/03|原生默认/自选保护父目录只读核查、拒绝可写祖先、非提权执行退出5且不改数据/登记；指定SID/过滤管理员共享判定回归|UAC取消、两个真实账户、默认/自定义程序根实际安装/中断、安装后ACL与系统回读 NOT_RUN|
|CPV-04|独立新版本TestOnly签名包实际静态审查通过，未加载插件；篡改/缺精确依赖/冲突版本/Production信任拒绝通过|真实厂商安装/SDK/硬件资格 BLOCKED；新ABI静态合成包不冒充运行组合资格|
|CPV-05/06|坏未确认原配置、普通Pending拒绝、显式修复成功、候选失败、中断待重存、CAS、加密草稿/无效字段继续、保留原envelope|跨账户DPAPI/UAC真实首配旅程 NOT_RUN；实际SDK参数/设备身份 BLOCKED|
|CPV-07/08/09|共享实例、原请求/Unknown不重发、原生局部页/键盘/长码/帮助；旧epoch与Stop规则沿原实现|目标屏幕/高DPI/触控/读屏完整矩阵及真实长时负载 NOT_RUN|
|CPV-10|同筛选/序号边界、实体口径、CSV文本/原UTF8、取消不发布部分、结果回执和不覆盖|现场数据量/实际存储失败与吞吐承诺未取得|
|CPV-11/12|私有完整预检副本、原DB/WAL字节保持、完整恢复集实际恢复、计划待执行/执行/到限留旧、历史备份拒绝、防双活围栏|系统登记切换/卸载/重启删除原生旅程 NOT_RUN；跨机器退役与秘密/编号资格 BLOCKED|
|CPV-13|完整恢复集实际恢复、备份列表/策略、安全模板白名单、原机逻辑内容/围栏核心|受保护登记接管确认 NOT_RUN；换机退役/秘密/范围资格 BLOCKED|
|CPV-14|容量策略、到限留旧、运行内固定摘要预览/导出，合成参数/条码/payload/目标无明文泄漏；独立失败摘要|真实现场支持交接授权、故障卷/真实容量承诺另行取得|
|CPV-15/16|局部原生页面/键盘/品牌、人工/批量同源引擎、版本/源/介质固定；独立业务构建按实际日志记录|真实安装/批量维护、目标屏幕/触控DPI、商业签名/许可、现场准入未声明通过|

CPV编号的完整原始判定条件见[验收计划](validation/commercial-productization-01.md)，上表只关联证据，不重写原计划状态或旧结果。无关全历史测试未重跑；没有删断言、吞异常换PASS或添加历史引用豁免。

## 运行与复核

用户从新介质 `Setup.cmd`、开始菜单运行/启动修复或系统产品维护进入，按[完整操作指南](commercial-productization-01-operations.md)执行。源码必要验证：

```powershell
dotnet run --project tests/Ipc.Application.Verification -- D:/projects/importer-pc --commercial-productization
dotnet run --project tests/Ipc.Verification -- --runtime-identity
dotnet run --project tests/Ipc.Desktop.UiVerification -- D:/projects/importer-pc --comm-native-pages
```

以上路径替换为本机仓库；SDK严格使用global.json。发布使用 `scripts/package-application.py --application <sportmaster|direct-plc|wcs-to-plc> --target <win-x64|portable> --output <新目录> --dotnet <固定SDK>`，必须从已提交、干净发布输入执行。冻结后用 `verify-commercial-media.ps1` 对同一 Windows 介质做合成核心和只读无人值守预检；新源码或新字节另建候选，不改标旧ZIP。

**可执行核心工程范围完成；整包现场验收尚未通过。** 本页列出的 Windows 特权/目标屏幕和真实接口/现场门槛未满足前，不能宣布可商业现场投产。许可收费、集中监控、远程操作、多语言/OEM等条件项未触发，未生成假资格或空平台。


## 最终冻结与交付身份

最终运行/打包源码为 `bd86fbb01b249f07e25fb12e003fed2a130a9c1c`，业务1.4.0、Application.Contracts1.10.0、DB8，SDK10.0.402。六套介质覆盖 Sportmaster、DirectPlc、WcsToPlc 的 win-x64 与 portable；portable 只提供框架依赖控制台，Windows包包含实际WPF、安装/维护入口及自包含运行时。DirectPlc页面仍明确为延期边界。文档/回执所在提交会晚于冻结源码，不能替换此运行身份。

介质在仓库本地 `.artifacts/comm/releases/<application>-bd86fbb-<target>.zip`，每包包含 `ASSEMBLY.json`、`application-release.json`、`SHA256SUMS`、依赖/许可清单及固定源码链接的操作指南。ZIP与逐文件身份、大小及数量记录在六介质回执；完整介质与包内文件必须一起交接，不从单个EXE拼装安装器。没有创建生产默认、商业签名或重新标记历史扩展。

同一 Sportmaster Windows 介质上的核心夹具与1100×600原生WPF检查均引用其真实发布DLL，检查前后核对介质不变；它们覆盖首配失败/成功/失败候选/中断/CAS、真实DPAPI、无效草稿恢复、历史固定筛选与CSV取消、防重原责任、备份策略与完整恢复、同源修复、DB7/8门槛、旧备份接管拒绝/源围栏和共享页面。合成身份/端口仅存在于测试构造依赖，没有配置或CLI生产绕过。另用真实旧70ccedc冻结DLL拒绝新DB8并核对数据库字节不变。

新增静态组件夹具使用针对本冻结ABI重新独立构建的 `reference.barcode@1.4.2`、RSA-PSS/SHA256、Simulation/TestOnly；只验证签名/内容/依赖与不执行代码，未安装、绑定、激活或取得Production资格。旧1.4.0/1.4.1包与旧介质身份保留，不改标兼容。

收尾实际发现并修复安全解包根未建立，以及SDK向上发现外层Git导致SourceLink变化、控制台/桌面公共DLL字节分叉的问题。前者在合法签名包上复现，后者由原PDB的9692f4d/dcfb8b1来源差异证实；最终发布显式固定SourceLink且拒绝公共Ipc DLL分叉。9692f4d局部核心/页面PASS不等于该整套介质可交付，最终六包另建身份。开发期失败、夹具编译错误、健康轮询真实占用时测试须有界等待、Windows无符号链接权限与屏幕不足分别保留，未删原断言或覆盖失败记录。

实际运行从解压后 `Setup.cmd` 开始，完成安装后使用指定Windows账户非提权启动开始菜单入口；失败首配进入“启动修复”，无需先成功启动业务。既有业务根必须沿原配置/身份/请求继续，维护入口 `Maintenance.cmd`，不得把重新建空根当作修复。完整安装、修复、迁移、卸载与无人值守返回码见操作指南。冻结复核命令和各环境剩余见证据入口；这些检查不会提权、安装机器政策或控制真实设备。
