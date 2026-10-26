# PR110 中文快捷方式续修

开工main a8cd431456f73e83040f75a0c5e3cc1423440d1b；PR110头963165d71aec1e037525fd7f17bc3ed81b560c1a。隔离分支codex/pr110-unicode-shortcuts-01，不接管既有P0/ST08工作区。PR112最新工程源0502f148仍由原任务交付中，待固定头后继承，不重写ST08。

实际CI40223909128的p0-stability-boundaries失败于DeploymentShortcuts.Create的WScript.Shell保存中文路径；原失败保留。计划直接使用Windows IShellLinkW/IPersistFile的Unicode持久化及读取，保留中文名、精确目标/参数归属、升级和卸载保护，所有COM错误继续抛出。隔离子进程以manifest分别设置ACP1252/936/65001，不改全机区域设置。原COM回读断言保留其语义。

工作树三组隔离ACP1252/936/65001实测全部通过，各自原COM创建/四链接回读、升级、旧版本卸载、历史后缀清理、异目标/异参数保护和坏链接错误均执行。没有修改系统区域设置；COM读取不Resolve目标、不吞HRESULT失败，构造器加载失败也释放RCW。待固定工程提交后复验和CI，原FAIL不倒写。

实现沿用DeploymentShortcuts原计划/归属和祖先路径守卫，直接使用[IShellLinkW](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishelllinkw)/IPersistFile Unicode存取；独立探针使用[进程manifest activeCodePage](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests)，GetACP实际匹配才运行断言。原COM回读改走Unicode COM接口，断言语义保留并增加描述及中文目标。

版本SM1.14.1、DirectPlc/WcsToPlc1.8.1，Contracts1.18.0及DB9/SQL1–9不变。运行：先加载scripts/project-environment.ps1，再执行`./scripts/verify-shortcut-codepages.ps1 -Output D:/DevCache/importer-pc/new-shortcut-evidence`；输出根必须全新，脚本在三个独立apphost子进程运行，不调用安装入口。真实安装/卸载、UAC/任务/SCM/WER、物理磁盘故障、设备/RCS、目标IPC及24h/72h为NOT_RUN；G01/G02及自动重连合同为BLOCKED_EXTERNAL。旧单分支结果不继承为集成PASS。

[归档原件及哈希](../../archive/pre-pr110-unicode-shortcuts-01/FILES.json)。
