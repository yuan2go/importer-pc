# STARTUP-DEPLOYMENT-02 实施与固定候选

2026-10-19。用户授权补齐启动失败可修复、基础配置独立于可选扩展、完整安装向导。继承本任务 PR44/首次运行01，未改并行 PR45 的 Supply/Inspection 身份工作或主工作树中的受限材料。源码固定 `e79a8bfdc65328619c77013fb314eaaef4a35a2f`，包含准备文档提交 `b2ccbf2`；最终记录提交不改变冻结媒介的 SourceCommit。业务1.3.3、公共契约1.9.0、Parcel DB7，旧SQL和旧候选不倒写。

## 已实施

- `IStartupMaintenance` 独立于运行实例。ParcelFlow拥有原账读取、完整候选校验、原因/CAS/所有权、原字节备份和Pending保存；不构造SDK/reader，不改Effective/原DB/旧请求。DB版本检查打开临时DB+WAL副本，修复了只读SQLite仍可能创建原目录sidecar的问题；Windows副本目录由维护拥有。完整备份复用原ParcelRecovery，在新目录恢复且保留隔离标记，恢复后界面重新读取新根。
- Assembly装配固定维护操作，Windows写入通过散列固定请求和受保护匹配helper提升；读回候选必须匹配，状态按实际账显示。不依赖业务Start、业务登录或可选认证扩展成功。原实例必须Stop/Dispose完成后才能进入维护或重试。
- `CapabilityCatalog` 保留原构造/解构签名，新增可忽略空值的库存诊断。可选库存缺失/损坏时提供内置目录；原绑定不替换，真正需要扩展的Validate/Create及Production资格继续拒绝。已有运行内维护页也显示独立库存诊断。
- 真实WPF四步安装向导、独立离线维护窗口及启动失败动作。Setup/Maintenance/普通入口不依赖PowerShell。运行账户、原数据、种子来源、独立信任及离线包由文件/文件夹选择器输入，自动计算hash。程序受保护并排部署、配置数据分离、完整文件复核、本版本开始菜单入口、最后提交位置登记。保留旧程序、seed、DB、政策、租约和操作证据；明确选择种子修复才创建新种子目录。软件不生成假信任、加载SDK、改SID或清责任。
- 桌面、控制台、可选维护脚本读同一受保护部署位置。脚本保留为高级维护；已同步随包[操作指南](startup-deployment-02.md)、[Windows指南](windows-application-delivery.md)、扩展指南、索引/框架及manifest。

新增窗口读取并继承SS-UI-VIS-001 v1.1实物与交接、现有WPF资源和参数草稿；不是用新静态HTML代替客户端。界面未在macOS伪造Windows截图。

## 实际验证

|验证|结果与边界|
|---|---|
|启动/部署定向Application验证|PASS，37项：独立目录、精确原表单、无数据读/校验、授权先行、Effective/Pending、原字节、CAS、占用、身份拒绝、原DB/WAL文件集合和hash不变、坏账保留、完整备份预检/占用/隔离/防覆盖/集合身份、无reader构造、摘要、新媒介复制/额外DLL/hash/链接/指南保留、测试信任及私钥拒绝|
|既有Application验证|PASS，72条检查，包含独立业务链、Unknown/重启、旧配置、资源生命周期及原raw恢复；未删断言或跳过关键测试|
|运行身份与首次配置共享决策|PASS，22项；Windows实际令牌/ACL仍NOT_RUN|
|三个Application装配及实际WPF页面|Sportmaster、DirectPlc、WcsToPlc隔离构建全部PASS，0警告/0错误；不能当作Windows交互通过|
|Python归档/组合工具单测|带Pillow环境10项PASS；系统Python无Pillow时1项环境失败保留日志，未改断言|
|PowerShell语法|PASS；未在macOS执行Windows安装脚本|
|四个固定ZIP|CRC、完整集合及全部SHA256PASS；三个Windows包由portable发布中实际Ipc.Platform.DeploymentMedia检查器读取，再核发布登记/外层清单，不能当作UAC/ACL执行|
|发布后portable冒烟|从/tmp启动冻结Sportmaster媒介，自动创建全新合成根；取得Started/空作业快照，第二所有者拒绝而原实例仍运行；完整Stop退出0，SQLite integrity_check=ok；没有真实设备外呼|
|公开归档|与main524916b相同带Pillow验证器比较；历史资产/元数据错误仍FAIL，新增错误0；精确数量及代码见[差额](evidence/startup-deployment-02/archive-delta.json)。不改原PPTX媒体或新增豁免换PASS|

调试测试运行在提交前与e79a8bf逐文件相同的源码上；发布冒烟直接运行e79a8bf冻结媒介。原始日志保留本地，[日志保管清单](evidence/startup-deployment-02/log-custody.json)分别记录原hash及路径替换后的公开hash。初次候选检查脚本误把多行JSON按单行解析，修正检查器后重新执行；产品输出/断言未改变，原检查器失败本地保留。检查日志不是现场验收回执。

## 固定媒介

四个ZIP均位于本任务工作树 `.artifacts/`，未上传GitHub Release。完整身份、外层清单hash与文件数见[artifacts.json](evidence/startup-deployment-02/artifacts.json)。

|Application / Target|ZIP SHA256|
|---|---|
|Sportmaster / portable|`d7f4fe5e0917fb4393ba61cc32d66889629ad97b46a9d54b3ea5aa8cc8aec3b2`|
|Sportmaster / win-x64|`e9aebafd80a1938e78c983adb247d0ef3cce5b04744d452fece72dbaa972bb62`|
|DirectPlc / win-x64|`1af49e1fd5b69d25a6afce04721ac3f833a840fb65420c8f0751cf911295409c`|
|WcsToPlc / win-x64|`8243a259fca37e5bbfaccf2865524db0a8e1bc8255a2899f53214f40df25ce26`|

ZIP名称为`startup-deployment-<application>-<target>-e79a8bf.zip`。完整解压后双击`Setup.cmd`，从向导完成安装并用开始菜单运行；独立维护用`Maintenance.cmd`或已安装启动修复入口。portable运行`dotnet app/Ipc.Assembly.dll --config app/config --data-root <新的隔离合成根>`。先前1760200首次运行01候选及其hash保持原值，不被本轮记录冒充升级。

## 未完成与验收边界

Windows原生UAC、不同账户授权、ACL/执行权、快捷方式、安装中断/并发、窗口交互/DPI/键盘及原生备份恢复为NOT_RUN，见[后验清单](validation/startup-deployment-02-windows.md)。失败保旧的Windows登记事务已实现和交叉构建，尚未原生故障注入验收。真实SDK/设备/WCS合同/现场为BLOCKED，Production仍NotReady，不关闭旧D/RV。macOS包检查和参考冒烟不能替代上述证据。

本轮没有实现静默SID重绑、自动清租约、自动删除业务数据、MSI/系统级卸载服务，也未扩张为前轮审查其他UX项（辅助窗口暂停、内部恢复ID、受控重启等）的全面整改。程序安装升级/修复向导是本包交付边界；政策损坏且身份不明的情况仍需维护核对原材料，软件不会猜测改权。
