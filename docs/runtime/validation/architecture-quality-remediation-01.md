# AQA01 验证记录

本轮基线 main `0c5d7d25e572e80f23522c9ff7b562db920628a3`。执行结果、冻结产物身份和证据索引已补录；本页不继承原报告的PASS，也不将实现完成当成运行通过。

原只读审查：`D:\audits\importer-pc\AQA01-20261023-1622`，保持原字节和结论。整改逐次日志/命令/退出码：`D:\audits\importer-pc\AQA01-remediation-20261023`。材料仅本机合成与loopback，口令不输出；原失败日志不删除。

完整实施、兼容和Pending命令处置见[当前说明](../architecture-quality-remediation-01.md)。真实设备、厂商SDK和现场未操作；不修改HKLM政策或安装信任。

## 源码定向验证（2026-10-23）

环境：Windows 11 x64 10.0.22631，PowerShell7.6.5、Git2.56.0.windows.1、Python3.14.3、固定.NET SDK10.0.402 / runtime10.0.13。所有缓存、临时文件和验证数据在D盘；未更改机级策略、生产设备或保护信任。

以下命令统一先执行项目环境脚本，构建使用Release和RestoreLockedMode=true；测试入口为`dotnet tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll . <参数>`。

|记录名/参数|结果/退出码|解决的疑点|
|---|---|---|
|build-verification-11 / dotnet build tests/Ipc.Application.Verification -c Release -p:RestoreLockedMode=true|PASS / 0，无警告|真实运行时和既有UI投影编译|
|remediation-05 / --architecture-quality-remediation|PASS / 0|排队过期、实际投影、直接重启、旧回调/序号、双位、UntilDispatch过期重量仍阻止PLC/Resume、TCP运行/备份、管理退出/到期/改密码、六边界真实进程崩溃/同ID/原请求守卫及中断检查|
|application-architecture-03 / --application-architecture|PASS / 0|原挂起套件完整双位/三业务/可选能力/资源/迁移断言|
|resource-risks-02 / --resource-risks|PASS / 0|Windows机级设备租约、Stop超时保有、受控激活失败保旧、原raw恢复|
|configuration-confirmation-01 / --configuration-confirmation|PASS / 0|原配置CAS/hash/runtime及双窗守卫，确认保持暂停|
|receipt-custody-01 / --receipt-custody|PASS / 0|真实原raw冲断/缺失，Query只确定结果、原件恢复与再归档、不重发|
|ux-commercial-inputs-02 / --ux-commercial-inputs|PASS / 0|初始陈旧时间戳不推进序号、旧实体不回退显示、Unknown责任、设备/Revision重绑与输入/作业原子性|
|release-documents-01 / python -m unittest discover -s scripts -p test_release_documents.py -v|PASS / 0，2项|随包文档链接与缺失/越界拒绝|

早期失败保留：build-verification-01（新夹具遗漏ISyntheticObservationInput签名，退出1）；remediation-01（新夹具kind/admission参数顺序错误，dotnet退出-532462766）；application-architecture-01（其他旧Reconcile调用未授权）、-02（取消修复使所有权超时夹具提前结束），均保留退出/完整栈；ux-commercial-inputs-01在第二重绑保存断言失败，未输出原回执，不能倒推其唯一原因。随后保留原断言，补真实管理授权、有界/按用途的取消和finally释放，以及只对明确调用前拒绝的重试与实际回执日志。后续成功记录独立保存，不替换原失败。

## 冻结产物与边界

使用原package-application.py冻结Git导出。四份产物均来自实现提交`7024f1e0128a4d8cf6af5ec6b51d6100a22c5d62`；测试驱动收口提交`fe8d9dd5a11c40f15777e4b658a624512f8a7af5`仅修改smoke夹具、版本断言和新增过期重量验证，src/config与冻结实现无差异。后续证据提交不将产物改标为新HEAD。本轮不发布/安装新介质，不把原SM1.11.1重测或历史安装PASS扩张到本分支。

真实SDK、真实WCS/PLC/称重/取图协议和现场为BLOCKED（没有相应设备/合同条件）；实际安装/UAC、受保护Store卸载和原生窗口人工验收为NOT_RUN。扩展离线读取直接验证了真实已运行TCP根及完整备份，Store链的适用性为源码证据，未做机级Store变更。目标构建与本机portable进程不等于现场验收。


## 最终解包和运行证据

完整命令、工作目录、时刻、实际退出码、日志及文件哈希见[RESULTS.json](../evidence/architecture-quality-remediation-01/RESULTS.json)和[证据索引](../evidence/architecture-quality-remediation-01/README.md)。保留所有46条命令回执和逐次日志，成功/失败分别记载；组合记录为schema3原索引及原事件流，已通过application_compatibility.py独立重验。

|冻结ZIP|版本|逐文件SHA核对数|ZIP SHA256|
|---|---|---|---|
|direct-plc-portable.zip|1.5.1|96|`bbeaae56f428acc9837537cfea5aa2e1ca4a40e22e6efebec2f1ca6ca3825aa1`|
|sportmaster-portable.zip|1.11.2|113|`81d32feea6c95f84314404e6fa687e894df0ce40736dac789586f510f03ca7e3`|
|sportmaster-win-x64.zip|1.11.2|739|`5fca3af3f84fd192c6c738466543cc9bdc8f6b445eb7d4be2d699d47f917ca8f`|
|wcs-to-plc-portable.zip|1.5.1|97|`037bda91a7f642eec4276a52952873aa25773cfda4ce452b22cb218dd12f9f49`|

|验证记录|结果/退出码|证据层级及结论|
|---|---|---|
|package-{sportmaster,direct-plc,wcs-to-plc}-portable|PASS / 0|实际选装发布；DirectPlc无WCS实现；DB8/ABI1.16/整改随包指南及全部登记文件核对|
|package-sportmaster-win-x64|PASS / 0|实际Windows目标/WPF编译及同源app/desktop完整介质，未做窗口人工验收|
|smoke-sportmaster-default-v2|PASS / 0|实际解包CLI、两个独立reader/实体取格口，重启保持原请求，正常停止|
|smoke-direct-plc-before-main-v3|PASS / 0|真实管理API、操作员、同件重量/PNG、独立HTTP参考进程；上传丢回执阻主链，重启原Query后显式继续至PlcAccepted，尚非物理完成|
|smoke-wcs-to-plc-attached|PASS / 0|附加上传Unknown独立于WCS→PLC；原Query清偿后保留原主链和操作员；PhysicalCompleted来自参考合同回执，非真实设备完成|
|combination-verify-* 三项|PASS / 0|命令/配置/提供者/实际事件/检查/参考资格一致；未将参考进程改标生产|
|native-entries|PASS / 0|解包自包含Ipc.Assembly.exe启动/快照/退出；Ipc.Desktop.exe --environment-report退出，未创建窗口|
|application-compatibility-02|PASS / 0，17项|版本声明/原事件/不可伪造资格和记录完整性；更新当前版本断言，保留历史候选身份|

两种可选恢复夹具显式选择已有WeightValidity=AtAssociation，验证已关联原件跨重启恢复；UntilDispatch=500ms的独立验证确认过期后保持原重量事实、Plc为空、Resume拒绝。没有延长生产超时或修改称重/发送守卫。独立读取并保存7份参考接收端原ledger，全部Executions=1、Request与冻结原件相同；上传原PNG字节的SHA也一致，原件文件保持本地。

额外失败及采集纠偏保持事实：application-compatibility-01发现当前版本断言仍停留SM1.8.2/PLC1.5.0；仅更新本轮版本期望，原负例断言保留。build-verification-10中新增测试误写枚举名AtDispatch，实际契约名UntilDispatch，修正后build11通过。smoke-direct-plc-before-main首轮重启后3秒UntilDispatch重量已过期，真实守卫拒绝继续，原事件保留。部分早期smoke结果索引被同名命令元数据覆盖，原事件完整保留在prior-events；采集器将命令回执移至commands后，以新输出路径重跑受影响场景，最终combination-*原索引与事件均保留并独立重验，不重建或改标旧索引。

参考Windows字段NOT_RUN沿用组合记录的目标/UI资格语义；运行宿主实际为本机Windows。原生目标构建、native启动、UI投影与真正窗口/安装/UAC/SDK/现场是不同层级，未验部分保持上表边界。没有改写原审查报告、原请求、原SQL迁移或D/RV，没有操控生产设备，也没有合并PR。
