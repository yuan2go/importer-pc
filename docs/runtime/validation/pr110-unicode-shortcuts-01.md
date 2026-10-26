# PR110 中文快捷方式续修

开工main a8cd431456f73e83040f75a0c5e3cc1423440d1b；PR110头963165d71aec1e037525fd7f17bc3ed81b560c1a。隔离分支codex/pr110-unicode-shortcuts-01，不接管既有P0/ST08工作区。PR112最新工程源0502f148仍由原任务交付中，待固定头后继承，不重写ST08。

实际CI40223909128的p0-stability-boundaries失败于DeploymentShortcuts.Create的WScript.Shell保存中文路径；原失败保留。计划直接使用Windows IShellLinkW/IPersistFile的Unicode持久化及读取，保留中文名、精确目标/参数归属、升级和卸载保护，所有COM错误继续抛出。隔离子进程以manifest分别设置ACP1252/936/65001，不改全机区域设置。原COM回读断言保留其语义。

工作树三组隔离ACP1252/936/65001实测全部通过，各自原COM创建/四链接回读、升级、旧版本卸载、历史后缀清理、异目标/异参数保护和坏链接错误均执行。没有修改系统区域设置；COM读取不Resolve目标、不吞HRESULT失败，构造器加载失败也释放RCW。待固定工程提交后复验和CI，原FAIL不倒写。

实现沿用DeploymentShortcuts原计划/归属和祖先路径守卫，直接使用[IShellLinkW](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishelllinkw)/IPersistFile Unicode存取；独立探针使用[进程manifest activeCodePage](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests)，GetACP实际匹配才运行断言。原COM回读改走Unicode COM接口，断言语义保留并增加描述及中文目标。

版本SM1.14.1、DirectPlc/WcsToPlc1.8.1，Contracts1.18.0及DB9/SQL1–9不变。运行：先加载scripts/project-environment.ps1，再执行`./scripts/verify-shortcut-codepages.ps1 -Output D:/DevCache/importer-pc/new-shortcut-evidence`；输出根必须全新，脚本在三个独立apphost子进程运行，不调用安装入口。真实安装/卸载、UAC/任务/SCM/WER、物理磁盘故障、设备/RCS、目标IPC及24h/72h为NOT_RUN；G01/G02及自动重连合同为BLOCKED_EXTERNAL。旧单分支结果不继承为集成PASS。

[归档原件及哈希](../../archive/pre-pr110-unicode-shortcuts-01/FILES.json)。

## 后续自动复审

980a1383的CI40261696276三job全绿，属于该源的工程门；随后自动复审新增P1 TCP选择版本与声明不一致、P2健康周期复位少算当前子进程。当前按实际源码补失败断言，修复及新CI另计，不沿用旧PASS宣称新问题关闭。

两项均取得实际失败：正式选择器报告1.0.1而Descriptor/兼容声明为1.1.0；健康窗口后MaximumRestarts=0却多拉起。修复选择器复用TcpLineObservationSource.Descriptor身份，健康复位保留当前子进程计数1。原健康测试把0次预算期望为可重启一次是错误预期，本轮收紧为0次及2次两个明确预算；原断言/证据仍在旧提交，不降低保护。

新增监督探针首次使用全程120倍时钟，将后续较慢的崩溃子进程也判为健康；只停止本次已确认PID/父进程的探针，保留原日志。夹具在第一次健康后切回正常单调速率，取得明确红断言再修产品。未改变产品时间算法或等待预算来换PASS。

更正后的选择器在未修改HEAD源码重跑红断言，明确记录去重后TCP=1.0.1、随包descriptor=1.1.0；修复后正常兼容声明验证通过。监督健康复位后的0次、2次预算均通过。首次选择器夹具错误假设每台reader单独一条provider，改为“两个实例、一条去重provider”；一次文件恢复保留旧时间戳使增量DLL沿用基线，强制刷新该源时间戳并重构建后才计绿。各次原log hash均保留在review-red.json。

本次续修版本SM1.14.2、DirectPlc/WcsToPlc1.8.2、Contracts1.18.0，DB9不变。后续固定提交CI及叠加整合分别取得新证据，不沿用980a全绿给复审修复授予PASS。
