# PR110 中文快捷方式续修

开工main a8cd431456f73e83040f75a0c5e3cc1423440d1b；PR110头963165d71aec1e037525fd7f17bc3ed81b560c1a。隔离分支codex/pr110-unicode-shortcuts-01，不接管既有P0/ST08工作区。PR112最新工程源0502f148仍由原任务交付中，待固定头后继承，不重写ST08。

实际CI40223909128的p0-stability-boundaries失败于DeploymentShortcuts.Create的WScript.Shell保存中文路径；原失败保留。计划直接使用Windows IShellLinkW/IPersistFile的Unicode持久化及读取，保留中文名、精确目标/参数归属、升级和卸载保护，所有COM错误继续抛出。隔离子进程以manifest分别设置ACP1252/936/65001，不改全机区域设置。原COM回读断言保留其语义。

状态：IMPLEMENTING；新代码、跨代码页测试和CI尚未授予PASS。真实安装/卸载、UAC/任务/SCM/WER、物理磁盘故障、设备/RCS、目标IPC及24h/72h为NOT_RUN；G01/G02及自动重连合同为BLOCKED_EXTERNAL。旧单分支结果不继承为集成PASS。

[归档原件及哈希](../../archive/pre-pr110-unicode-shortcuts-01/FILES.json)。
