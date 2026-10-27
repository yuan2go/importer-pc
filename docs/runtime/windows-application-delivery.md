# 当前Windows安装与维护

只支持当前版本从零安装，选择全新数据目录。不转换旧安装的数据、配置、账号或草稿。此前迁移和旧发行步骤已[归档](../archive/pre-fresh-baseline-01/README.md)。历史GitHub Releases保持原样；不能据其PASS声明新介质已验。

## 安装与运行

使用当前同源原生安装包。普通向导准备并验证介质，选择程序目录、数据目录及快捷方式，执行时申请Windows授权。正常流程不依赖PowerShell。程序和数据使用D盘；机级受保护根由管理员登记，ACL和普通祖先检查不可绕过。指定日常账户以普通权限运行，真正提权、SYSTEM或非指定SID不允许业务运行。

安装后使用开始菜单运行入口，未完成配置进入独立首配；启动修复无需业务Start成功。四类参数是同一个完整候选，管理模式授权、CAS、Pending/Effective及当前失败修复保持。停止未完成时不能创建第二个业务实例。详细交互见[生命周期指南](lifecycle-ux-current-guidance-01.md)、[首配指南](parameter-guidance-startup-01.md)。

Windows目标继续遵守[客户端功能门禁](windows-client-compatibility-01.md)：Win10 22H2和Win11均在范围，x64及系统构建检查保留；本轮不改为仅Win11。目标机器、真实SDK和现场资格仍单独验证。

## 同格式覆盖安装、修复与卸载

同数据格式的程序覆盖安装、同版修复、安装中断恢复、受保护登记和归属检查保留。新介质使用独立程序目录，先核SourceCommit、SHA256SUMS、业务/契约/配置/数据库身份及签名闭包，再按原安装操作提交。程序修复不回滚业务账，不覆盖当前配置。

非当前数据格式拒绝，请使用全新数据目录；无旧数据库升级或降级恢复。卸载只处理登记归属的程序、快捷方式、任务和服务，不任意删除运行数据、信任或备份。旧开发站点由用户另行卸载和清理，本任务不执行安装、卸载、SCM、HKLM或运行目录操作。

## 当前备份、恢复与诊断

停止客户端后离线维护获取原数据租约。备份使用SQLite一致快照，保留配置、原始回包、未释放图片、消费者及完整恢复集，COMPLETED最后落盘。现有目标拒绝覆盖；不手拷运行中的db冒充完整备份。WAL、synchronous=FULL、外键和校验保持。

恢复只接受当前格式完整集合，先PlanRestore检查hash、容量及身份，目标必须新建。恢复后仍暂停、隔离；原根退役、机器身份、RCS范围退役及实体核对完成前不能准入。Unknown不重发，查询成功不能填补原始回包缺失，原请求固定原目标与字节。

可选PowerShell7脚本与原生入口调用同一应用契约：Verify、Install、Diagnose、Run、Console、Inspect、Export、Backup、PlanRestore、Restore。具体参数见application.ps1及[启动部署](startup-deployment-02.md)。Supply使用station.ps1的Run/Fixture/Init/Backup/Preflight/Restore/Export；不提供Upgrade/Rollback。

诊断仅导出批准摘要，不公开数据库、凭据、条码、完整报文或私密路径。实际发布身份见ASSEMBLY.json/application-release.json、DEPENDENCIES.json和SHA256SUMS；第三方许可及获授权OFL字体原文随包，厂商SDK不得擅自分发。软件暂停不是安全急停。
