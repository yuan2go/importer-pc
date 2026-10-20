# 单窗口安装与一次授权

本轮[工作包](../work-packages/WP-IPC-INSTALLER-SINGLE-WINDOW-01.md)继承PR67的英文SM业务版本包、独立程序/数据目录、快捷方式和BUILD-INFO。解决用户实际报告的重复欢迎/准备窗口、多次UAC和安装闪动。原7c、129安装包及各自的验证仍为历史原身份。

## 正常安装流程

双击单EXE后直接显示原安装窗口，在同一个窗口自动校验/准备文件，随后显示安装选项；没有前置欢迎页或额外“下一步”，没有启动第二个安装UI进程。业务版本显示在同一个页头。程序目录、数据目录、开始菜单/桌面图标、可展开高级设置和返回保留输入继续可用。

选项的“下一步”只执行本地参数、介质及组件检查，展示摘要与新备份目标，不请求管理员权限。点击摘要的“安装”时Windows请求一次授权；同一个WinExe无窗口helper依次执行原责任预检、完整备份、受保护复制/登记及快捷方式。只在原PlanChange允许且ExecuteChange重验身份、配置、数据库原件及登记hash通过后执行；有Unknown、变动计划或占用仍拒绝，保留原部署。未绕过Windows授权或把普通安装UI/日常客户端提权。

安装完成仍在同一个窗口。完成后由普通权限窗口启动指定账户的工作台，图标只指向已安装版本，无setup参数。安装和首次工位配置仍是不同任务；本轮不预设实际相机/WCS配置或生产资格。高级独立维护、已审阅批处理和旧Install协议不改为自动放行。

## 实现和来源一致性

单EXE入口直接复用现有StartupSetupWindow，通过准备回调更新同一窗口，不另建目录界面。Ipc.Installer引用打包所选的Desktop项目，按同一冻结源码/PathMap/SourceLink及锁定依赖构建；封装前逐个核对共用Ipc DLL与ZIP中的Desktop字节，任一缺失/不一致拒绝出包。安装helper仍是同版Desktop的早期命令分支，创建任何WPF窗口前分发；不再拉起Console安装进程。原CLI保留。

安装文件名继续是 `CainiaoSupplyStation_SM_Setup_v1.4.1_win-x64.exe`，其中1.4.1是Sportmaster业务模块版本；此次安装器修复不提升业务模块版本。旧同名构建保存在原不可变目录，新构建进入新交付目录，并通过BUILD-INFO的分支/完整commit追溯。基础依赖和未再分发的海康依赖见[安装依赖](installer-release-info-01.md)。

## 验证边界

准备校验28项、封装7项与原计划/Unknown/配置变化/完整备份的聚焦8项已通过；三种业务选择均可编译，快捷方式隔离COM检查通过。一次完整commercial-productization运行在本机普通令牌执行Windows恢复授权时被原门禁拒绝，原失败保留，不声明该全套通过；本轮没有为了换PASS提升整个套件、删除断言或修改生产权限守卫。针对本轮调用的原计划与备份风险单独执行聚焦检查。

最终冻结EXE与实际原生结果见下节，原始断言、监测和源码fixture分别存档。未把编译/准备当实际安装，也不把本机Simulation当干净受支持Windows或海康/WCS/RCS现场验收；旧D/RV保持。

用户补充的Win10 22H2 Home/Pro与多CPU功能兼容目标、只读诊断、通用IL措施及实际待验矩阵见[Windows兼容](windows-client-compatibility-01.md)。单窗口首次d93封装已通过共享DLL同字节检查，但额外PDB输出被单EXE约束拒绝，未交付；仅设置CopyOutputSymbolsToPublishDirectory=false仍不足以排除引用符号，985尝试同样被拒绝；IncludeSymbolsInSingleFile实验被.NET10 SDK明确拒绝。最终对应用和包装器统一使用DebugType=embedded，将源码符号嵌入DLL并逐字节核对共用库；独立实验已确认输出只有一个EXE。未删除单EXE断言，原失败保留。

## 最终交付与实际结果（冻结27b0d8f）

源码`27b0d8f6cc0281fd82ee68fcda350233cb0a45ff`，业务SM/v1.4.1，最终单EXE177478140字节，SHA256 `062bd4878ff5f5c6c03fc7b0d01a026c59b55703c5d35c04f218eb961b4954e6`，未签名。实际EXE/730文件介质身份、原始探针与58项PASS见[交付证据](evidence/installer-single-window-01/README.md)。本次源码记录后的提交仅补证据/文档，随包BUILD-INFO继续精确标记27b0d8f；随包指南在实际结果前冻结，未伪称包含后补回执。

本机实际升级：自动准备和安装选项复用一个HWND；选目录、两种图标、浏览与返回保留输入通过；准备/预览零UAC，点击安装一次真实UAC（用户确认）；1043次名义20ms采样为零安装窗口隐藏、零新增可见控制台，不覆盖所有亚20ms事件。原配置/数据库保留、旧版本保留、完整备份、部署登记与根/app/desktop源码记录通过；完成及两个实际快捷方式均进入正常双位Simulation工作台。准备中一次关闭安全取消且不改登记、只清理自身临时目录。没有操作真实设备。

额外兼容验证：实际介质27个应用托管DLL无ReadyToRun头；本机正常与关闭托管intrinsic/R2R两种工作台启动均通过。仍仅Windows11 Home23H2/build22631和同一AMD Ryzen5 3550H；Win10 22H2/其他CPU/干净系统/海康SDK及现场待验。原完整commercial-productization普通令牌恢复授权失败及早先封装失败独立保留，不宣称完整套件或所有历史RV关闭。

## 并行文档成果对齐

最终源码冻结后main新增`12d20b2269a69049b6e8aeb03916d8dba6c9e055`（UI重设计文档）。本分支保留其新AGENTS/INDEX、设计、工作包、原件和manifest清单，与本次安装证据合并；无运行代码冲突，没有重打或改标27b0d8f EXE，也不声称已完成UIR-01～06 WPF移植。当前Windows checkout对上游保留名称`source/aux.js`使用仅本clone的sparse排除，Git索引仍保留上游原始blob；没有删除或重写并行设计原件。
