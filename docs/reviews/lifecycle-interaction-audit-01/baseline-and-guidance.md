# 审查对象与有效基线

本页区分源码事实、交付介质与现场运行，所有“当前”仅指本次审查时点。

## 身份与环境

|对象|实际核验值|适用边界|
|---|---|---|
|远端 main|`3a5b1d13ec5969213db35ed43d72a5aecd437777`；PR107 合并结果|开始/收尾均核验；并行开放 PR 查询为 0，不能据此假定没有本地并行工作|
|本轮独立工作区|`D:\w\la01-r2`，上述 SHA 的 detached worktree|原工作区既有文件及另一个审查工作区不修改；报告作为新目录交付到原仓库。不继承其他审查的未提交结论|
|源码版本|SM 1.12.3；Contracts 1.18.0|由 csproj 读取；版本号相同也不代表二进制相同|
|本轮运行|Debug/net10.0-windows 同源 Desktop；UiVerification 使用 `IPC_FROZEN_DESKTOP` 指向该 Desktop|实际业务DLL ProductVersion为 `1.12.3+3a5b1d13ec5969213db35ed43d72a5aecd437777`；两个受控合成场景，无真实设备调用；哈希见 evidence/environment.json|
|Windows|Windows 11 家庭中文版 10.0.22631，x64；实际 DPI 125%|1366×768、1092×614、800×420 为测试客户区 DIP，不冒充三种物理显示器或 100% DPI|
|工具|.NET SDK 10.0.402；运行时 10.0.13|每次开发命令先加载 project-environment.ps1；临时文件/运行数据均在 D 盘|
|已归档本地参考介质|`frozen-7d7487a5/DELIVERY.json`：源码 `7d7487a571353f57aeaf01f14425760a737330b0`；SM 1.12.3；ZIP 134218886 bytes；SHA256 `c3d742bc178d79ee00ca750f132fa80ab8477a486741d7d184c1046c939db1e8`|读取仓库回执，未重新下载或安装；不是当前 main 的安装验收|
|GitHub 可见发布|返回的 30 个 release 中按发布时间最新为 [SM1.10.2 原冻结交付](https://github.com/yuan2go/importer-pc/releases/tag/installer-sm-v1.10.2-c9e482a30d2d)，2026-10-21 16:22:30 UTC，prerelease|公开发行与最新源码明显不同。latest 接口无稳定版结果，不能解释成“没有发布”。本轮未执行该旧 EXE|
|现场版本与安装状态|未知|未读取现场数据/注册表来猜测；其他对话或旧安装截图不是本轮现场证据|

原工作区存在的四份未跟踪 HTML/spec/ZIP 均未纳入审查产物或 Git；不把未获许可材料当成公开规格。原生截图只保存在仓库外本机审查证据目录，报告保留定位与哈希，不打包品牌/客户原稿或 SDK/字体文件。

## 现行规则及覆盖差异

先读 AGENTS、INDEX、framework、framework-decisions，再沿安装/首配/管理模式/参数/作业/维护索引核对实现。采用项目 Impeccable 的原生审查及 WPF Operate 约束、ui-ux-pro-max 的 WPF 指引；通用建议服从以下基线。

1. `PARAMETER-GUIDANCE-STARTUP-01` 优先：仓ID、生产线ID；同一候选明确统一供包台编号；左右相机编码仍各守用途；新建日志/已结历史默认 7 天；本机窗口模式；明确延后相机/RCS但相关业务拒绝；新取格口请求 quantity=1；完整 TCP 行为一次扫描，同码不同次扫描不合并。
2. `MANAGEMENT-MODE-01`：操作员业务身份、管理模式会话、Windows/UAC 三层独立。15 分钟授权、失败锁定、重新授权不等于切换操作员；不能恢复“软件管理员账号”旧设计。
3. 参数以 `PARAM-UX-02`/V3、parameter-refactor、对应增量工作包为准。四类页面编辑同一完整候选，草稿/Pending/Effective 分开；旧请求固定原目标/报文/合同。
4. 作业/首配以 V2 实物和后续原生层级、内部页、字体规范为准。V1/旧HTML不作为当前验收。原型用于理解设计，本轮不声称逐页原型测试等于原生通过。
5. F-02～F-10 的既有架构继续有效：业务实例唯一、UI只调应用契约、适配器实现端口、唯一配置账及持久化事实。历史 RV-01～RV-17 的 OPEN 和 D 未决状态不由本轮局部验证关闭。

|模块/形态|源码核对|本轮结论边界|
|---|---|---|
|Sportmaster|双位读码 → RCS取格口；SlotResolved/WcsRejected 为主流程结果；不直控PLC；支持新任务、原请求及迟到/未知投影|主要完整走查场景。新 quantity=1 不覆盖原回执中的 quantity 字符串；格口已取得不等于实体完成|
|DirectPlc|源码1.6.2；存在 Application 和原生 Presentation/ShellWindow；业务直接走 PLC，接受/物理完成分开|共用参数、历史、维护机制可复用本轮源码发现；专用完整 UI/真实PLC交付资格不能从 SM 推定，本轮未原生执行|
|WcsToPlc|源码1.6.2；存在 Application 和原生 Presentation/ShellWindow；WCS之后PLC|外部请求链、未知责任多一阶段；未用SM结果替代其恢复验收|
|Supply/Inspection 参考形态|有早期产品/参考装配与模拟路径；不具有相同的SM默认页面合同|不擅自宣布所有模块已完成同样的桌面交付；若纳入现场包须另给可运行清单、岗位任务和资格证据|
|单EXE / win-x64 / ZIP兼容入口|SM主路径单EXE；自包含win-x64；ZIP/Setup.cmd为兼容；CLI模拟为另一形态|同版本号、同目录或同名安装包都不足以互换证据，必须匹配 SourceCommit/manifest/hash|

## 旧指引处置清单（只登记，未改原文）

|编号|材料与矛盾|本轮采用/后续处置|
|---|---|---|
|GD01|INDEX:30/32 仍分别称“当前重测SM1.11.1”“当前管理模式交付SM1.11.0”|改成有日期及SourceCommit的历史锚点；在顶部只保留一个当前源码/当前交付状态入口，不能改旧包身份|
|GD02|commercial-productization-01-operations:7 将SM1.12.0/Contracts1.17.0称当前|加明确适用版本；由实际发行回执关联版本，不能仅替换数字后宣称旧验证覆盖1.12.3|
|GD03|该指南:16–18 及 startup-deployment-02 的人工安装说明要求选择运行账户、站号、模式、信任/扩展；现向导实际是目录/快捷方式，自动沿原部署或新建Users/Production种子与离线包|按当前入口重写正常安装路径；将仍存在的底层高级API单列实现资料，不要求使用者寻找不存在的控件|
|GD04|该指南:75 说同源安装向导有“继续退役程序的中断清理”；Desktop无调用 ResumeRemovalAsync 的入口|先注明用户入口缺口（F02），实现并隔离验证后再恢复“可操作”表述|
|GD05|旧独立编号、启动必填、账号管理员、V2参数布局说明|已有替代标记的维持历史身份；仅清理仍指向日常操作的链接，不重复重写已正确的PR107/管理模式/V3规则|
|GD06|早期DB8/旧Windows NOT_RUN 与后续DB9/同源PASS并列|保留原时点；升级/恢复指导核对实际兼容矩阵与完整恢复集，不能把旧库迁移前提直接套到当前库，也不能用后续PASS覆盖旧版|
|GD07|旧“DirectPlc完整UI延后”与现有ShellWindow代码并存|区分“有参考原生壳”和“专用完整交付/现场验收”，避免写成代码不存在或正式交付已完成|

后续文档整改必须由实现与介质事实驱动；本轮原件、编号、原哈希和历史验收记录均未改写。
